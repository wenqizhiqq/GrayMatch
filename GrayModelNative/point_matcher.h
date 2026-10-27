#pragma once

// ---------------------------------------------------------------------------
// point_matcher : 几何点匹配内核（PatMax 思路的自研实现），纯 C++，不依赖 OpenCV。
//
// 与 NCC 路径的本质区别：模板不再被 warp 成一张张旋转图像，而是训练成一团
// 「带方向的边缘点」。旋转只是对这些点做一次 2x2 矩阵乘法，代价可以忽略。
//
//   train : 平滑 -> Sobel 梯度 -> 3x3 局部极大 + 格网去重抽点 -> 保留对比度达标的点
//           每个点存 (相对模板中心的坐标, 单位梯度方向, 权重=归一化梯度模)
//   coarse: 位置格网 x 角度格网上打分。方向命中判定用「方向位图」O(1) 查表
//           （mask[p] 的 bit k = p 的半径 r 邻域内是否存在方向落在 bin k 的边缘），
//           点按权重降序累加，用「剩余权重上界」分支限界早停 ——
//           绝大多数假姿态在第 1~3 个点就被淘汰，这是速度来源。
//   fine    : 每个种子爬山式局部精修（位置+角度，软 cos 打分），再抛物线插值到亚像素/亚度。
//
// 打分基于梯度「方向」而非灰度值，光照整体变化只改变梯度模（已被阈值+归一化消掉），
// 所以天然抗光照 —— 这是灰度 NCC 做不到的。
// ---------------------------------------------------------------------------

#include <cstdint>
#include <vector>

namespace pmatch {

// 字段与 gray_model_native.h 的 GmMatchResult 一致，导出层可逐字段直接拷贝。
struct PmMatchResult {
    double score;
    double centerX;
    double centerY;
    double angle;
    int templateWidth;
    int templateHeight;
    int leftTopX;
    int leftTopY;
    int level;
};

struct PmParams {
    double angleStart = -180.0;
    double angleEnd = 180.0;
    double angleStep = 1.0;
    double scoreThreshold = 0.5;   // 0..1
    double maxOverlap = 0.1;
    int topN = 10;

    int edgeThreshold = 30;        // 边缘对比度下限（灰度级，1..254）
    int pointSpacing = 0;          // 特征点最小间距(px)，0 = 按模板尺寸自动推
    int maxPoints = 0;             // 特征点上限，0 = 按模板尺寸自动推
    int smoothing = 1;             // 抽梯度前的 [1 2 1] 平滑次数，0..3
};

class PointMatcher {
public:
    bool setSource(const unsigned char* data, int w, int h, int step, int channels);
    bool setTemplate(const unsigned char* data, int w, int h, int step, int channels);

    // 训练 + 搜索。返回已 NMS、按分数降序、最多 topN 个结果。
    // lastMatchMs() 只计搜索时间，训练与建索引按本工程惯例不计（与 gm_match 一致）。
    std::vector<PmMatchResult> match(const PmParams& params);

    double lastMatchMs() const { return lastMatchMs_; }
    int pointCount() const { return static_cast<int>(mp_.size()); }

private:
    struct MPoint {
        float x, y;      // 相对模板中心（模板像素坐标）
        float dx, dy;    // 单位梯度方向
        float w;         // 权重 = 梯度模 / 最强点梯度模，降序排列
    };

    // 场景：量化方向（int8 分量对，低于 floors 的像素为 0）+ 钳到 uint8 的梯度模。
    std::vector<int8_t> sx_, sy_;
    std::vector<uint8_t> smag_;
    int sw_ = 0, sh_ = 0;

    std::vector<uint8_t> templateGray_;   // setTemplate 存下来，match 里按参数训练

    std::vector<MPoint> mp_;
    std::vector<float> suffix_;   // suffix_[i] = Σ w[i..end]，早停用的剩余权重上界
    float maxRadius_ = 0.f;       // 模型点到中心的最大距离，用于推粗角度步长
    int tw_ = 0, th_ = 0;
    int spacing_ = 6;
    mutable double lastMatchMs_ = 0.0;

    bool train(const PmParams& p);

    // 软打分：前 upto 个点，位置容差 tol（像素）。达不到 thr 时提前返回 -1。
    float scoreSoft(float cx, float cy, double angleDeg, const float* cosSin,
                    int upto, float tol, float thr, int edgeThreshold) const;

    // 粗打分：方向位图 O(1) 查表。返回命中权重和（不达标返回 -1）。
    float scoreCoarse(float cx, float cy, const float* rx, const float* ry,
                      const uint16_t* bits, const std::vector<uint16_t>& mask,
                      int upto, float thrTimesTotal, float floorBeyond) const;
};

} // namespace pmatch
