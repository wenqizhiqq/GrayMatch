// graymatch.cpp -- 干净、自包含的旋转不变灰度 NCC 模板匹配器（高斯金字塔 coarse-to-fine）
// 内置自测。可脱离 GrayModelNative.dll 独立编译运行。
//
// 算法流程
// ---------
// * 用 cv::matchTemplate(TM_CCOEFF_NORMED) 做归一化互相关(NCC)。
// * 对源图和模板同时建高斯金字塔。
// * 最粗层：固定 15° 步长全图廉价粗扫，得到候选种子(seeds)。
// * 渐进细化：从粗层往细层(k=L-1..1)，每层角度步长减半
//   stepAt(k)=15°/2^(L-k)，并只在种子的小窗口内精修，逐步把角度钉死。
// * 最终精扫：在原始分辨率上，对每颗种子做 ±9°、1° 步长的小窗搜索。
//   （全分辨率精扫可以避免在缩略图上做最终匹配导致的低分辨率假阳。）
// * 按包围盒重叠率做非极大值抑制(NMS)。
// * 整体耗时 <= 旧双遍全分辨率方法：最终精扫等同于旧方法，但只触达小窗。
//
// Windows / MSVC cl 编译（中文注释需 /utf-8，否则可能报 C4819）:
//   cl /std:c++17 /O2 /EHsc /openmp /utf-8 /I<opencv include> graymatch.cpp <opencv_world480.lib>
// 运行：把 opencv_world480.dll 放在 graymatch.exe 旁边即可，不需要 GrayModelNative.dll。
//
// 速度调参建议（对应 WPF 截图里 221.4 ms 的原因）:
// 1. 金字塔层级尽量 >= 3（例如 4），层级 1 几乎没加速。
// 2. 起始/终止角度不要设 -180~180，按实际目标范围来（图中只有 0°~60°，设 -5~65 即可）。
// 3. 角度步长 1° 精度最好；若允许 2°~3° 误差，可改大为 2° 或 5°。
// 4. NCC 阈值 >= 0.40 可减少细扫时的假阳和重复窗。

#include <opencv2/opencv.hpp>
#include <cstdio>
#include <cmath>
#include <vector>
#include <map>
#include <string>
#include <chrono>
#include <algorithm>

using namespace cv;
static const double PI = 3.14159265358979323846;

// ---------------------------------------------------------------------------
// 匹配结果 + 旋转模板缓存
// ---------------------------------------------------------------------------
struct MatchResult {
    double centerX = 0, centerY = 0, angle = 0, score = 0;  // 中心坐标、角度(°)、NCC 分数
    int templateWidth = 0, templateHeight = 0;              // 原始模板尺寸（用于画框）
};

// 把模板按 angle(°) 旋转，返回最小外接矩形大小的图像
static Mat makeRotated(const Mat& tpl, double angle) {
    Point2f c(tpl.cols / 2.0f, tpl.rows / 2.0f);
    Mat M = getRotationMatrix2D(c, angle, 1.0);
    Rect bbox = RotatedRect(c, tpl.size(), angle).boundingRect();
    M.at<double>(0, 2) += bbox.width / 2.0 - c.x;
    M.at<double>(1, 2) += bbox.height / 2.0 - c.y;
    Mat r;
    warpAffine(tpl, r, M, bbox.size(), INTER_LINEAR, BORDER_CONSTANT, Scalar(0));
    return r;
}

// 每一层金字塔的旋转模板惰性缓存。键 = angle*4，精度 0.25°。
struct RotatedCache {
    const Mat* tpl = nullptr;
    std::map<int, Mat> cache;
    const Mat& get(double a) {
        int k = (int)(a * 4 + (a >= 0 ? 0.5 : -0.5));
        auto it = cache.find(k);
        if (it != cache.end()) return it->second;
        Mat r = makeRotated(*tpl, a);
        return cache[k] = r;
    }
};

// ---------------------------------------------------------------------------
// 在单层图像 src 上做角度扫描，返回按阈值和局部极大筛选并剪枝后的峰值。
// 返回的 centerX/centerY 是在 src 坐标系下的中心。
// ---------------------------------------------------------------------------
static std::vector<MatchResult> matchLevel(const Mat& src, RotatedCache& cache,
                                           int tplW, int tplH,
                                           double a0, double a1, double aStep,
                                           double thr, int topN) {
    std::vector<MatchResult> out;
    Mat res;
    for (double a = a0; a <= a1 + 1e-9; a += aStep) {
        const Mat& rt = cache.get(a);
        if (rt.cols > src.cols || rt.rows > src.rows) continue;
        matchTemplate(src, rt, res, TM_CCOEFF_NORMED);
        Mat mask = res >= thr;
        std::vector<Point> pts;
        findNonZero(mask, pts);
        for (const Point& p : pts) {
            float v = res.at<float>(p);
            // 3x3 局部非极大抑制
            bool isMax = true;
            for (int dy = -1; dy <= 1 && isMax; ++dy) {
                for (int dx = -1; dx <= 1; ++dx) {
                    if (dx == 0 && dy == 0) continue;
                    int yy = p.y + dy, xx = p.x + dx;
                    if (yy < 0 || xx < 0 || yy >= res.rows || xx >= res.cols) continue;
                    if (res.at<float>(yy, xx) > v) { isMax = false; break; }
                }
            }
            if (!isMax) continue;
            MatchResult m;
            m.centerX = p.x + rt.cols / 2.0;
            m.centerY = p.y + rt.rows / 2.0;
            m.angle = a;
            m.score = v;
            m.templateWidth = tplW;
            m.templateHeight = tplH;
            out.push_back(m);
        }
    }
    // 峰值剪枝：NCC 曲面在阈值附近会有很多弱局部极大，若全部返回会灌爆下游精扫。
    // 这里按分数排序，按最小间距去重，只保留 topN 个最靠谱的峰值。
    std::sort(out.begin(), out.end(),
              [](const MatchResult& a, const MatchResult& b) { return a.score > b.score; });
    std::vector<MatchResult> pruned;
    double sep = std::max(tplW, tplH) * 0.25;
    for (const auto& m : out) {
        bool keep = true;
        for (const auto& p : pruned) {
            double dx = m.centerX - p.centerX, dy = m.centerY - p.centerY;
            if (dx * dx + dy * dy < sep * sep) { keep = false; break; }
        }
        if (keep) pruned.push_back(m);
        if ((int)pruned.size() >= topN) break;
    }
    return pruned;
}

// ---------------------------------------------------------------------------
// 按轴对齐包围盒重叠率(IOU)计算两个结果的重叠
// ---------------------------------------------------------------------------
static double overlap(const MatchResult& a, const MatchResult& b) {
    double ax1 = a.centerX - a.templateWidth / 2.0, ay1 = a.centerY - a.templateHeight / 2.0;
    double ax2 = a.centerX + a.templateWidth / 2.0, ay2 = a.centerY + a.templateHeight / 2.0;
    double bx1 = b.centerX - b.templateWidth / 2.0, by1 = b.centerY - b.templateHeight / 2.0;
    double bx2 = b.centerX + b.templateWidth / 2.0, by2 = b.centerY + b.templateHeight / 2.0;
    double ix1 = std::max(ax1, bx1), iy1 = std::max(ay1, by1);
    double ix2 = std::min(ax2, bx2), iy2 = std::min(ay2, by2);
    double iw = std::max(0.0, ix2 - ix1), ih = std::max(0.0, iy2 - iy1);
    double inter = iw * ih;
    double ua = (ax2 - ax1) * (ay2 - ay1) + (bx2 - bx1) * (by2 - by1) - inter;
    return ua > 0 ? inter / ua : 0;
}

// 按分数降序做 NMS，去掉与已保留结果重叠超过 maxOverlap 的候选
static std::vector<MatchResult> nms(const std::vector<MatchResult>& in, double maxOverlap) {
    std::vector<MatchResult> v = in;
    std::sort(v.begin(), v.end(), [](const MatchResult& a, const MatchResult& b) { return a.score > b.score; });
    std::vector<MatchResult> out;
    for (const auto& m : v) {
        bool ok = true;
        for (const auto& o : out)
            if (overlap(m, o) > maxOverlap) { ok = false; break; }
        if (ok) out.push_back(m);
    }
    return out;
}

// ---------------------------------------------------------------------------
// 金字塔深度选择：防止粗层模板过小导致高旋转目标信号丢失。
// L>=3 要求粗模板短边 >=6px；L==2 允许 >=4px；粗层源图短边必须 >=32px。
// ---------------------------------------------------------------------------
static int chooseDepth(int tplShort, int srcShort, int requested) {
    int L = requested;
    while (L > 0) {
        int tShort = tplShort >> L;
        int sShort = srcShort >> L;
        if (sShort < 32) { --L; continue; }
        if (L >= 3 && tShort < 6) { --L; continue; }
        if (L == 2 && tShort < 4) { --L; continue; }
        break;
    }
    return L;
}

// ---------------------------------------------------------------------------
// 匹配器
// ---------------------------------------------------------------------------
class GrayMatcher {
public:
    void setSource(const Mat& src) { src_ = src.clone(); }
    void setTemplate(const Mat& tpl) { tpl_ = tpl.clone(); tplW_ = tpl.cols; tplH_ = tpl.rows; }

    // pyramidLevels<=0：最小金字塔（一层 0.5x 粗层）
    // pyramidLevels>=1：额外建这么多粗层，对大图加速明显
    // 所有设置都走同一套 coarse-to-fine + 全分辨率小窗精扫流程。
    std::vector<MatchResult> match(double a0, double a1, double /*angleStep*/,
                                   double nccThr, double maxOverlap, int topN,
                                   int pyramidLevels, double& elapsedMs) {
        auto t0 = std::chrono::steady_clock::now();
        int srcShort = std::min(src_.cols, src_.rows);
        int tplShort = std::min(tplW_, tplH_);
        std::vector<MatchResult> seeds;

        int levels = std::max(1, pyramidLevels);
        int L = chooseDepth(tplShort, srcShort, levels);

        // 建源图和模板的金字塔
        std::vector<Mat> srcPyr(L + 1), tplPyr(L + 1);
        srcPyr[0] = src_; tplPyr[0] = tpl_;
        for (int k = 1; k <= L; ++k) {
            pyrDown(srcPyr[k - 1], srcPyr[k]);
            pyrDown(tplPyr[k - 1], tplPyr[k]);
        }
        std::vector<RotatedCache> caches(L + 1);
        for (int k = 0; k <= L; ++k) caches[k].tpl = &tplPyr[k];

        const double coarseStep = 15.0;
        double coarseThr = std::max(0.10, nccThr - 0.20);  // 粗层阈值比用户阈值低一点，避免漏种子
        auto stepAt = [&](int k) { return coarseStep / (double)(1 << (L - k)); };

        // ---- 最粗层：全图廉价扫 ----
        auto cur = matchLevel(srcPyr[L], caches[L], tplW_, tplH_, a0, a1, coarseStep, coarseThr, topN * 6);
        for (auto& m : cur) { m.centerX *= (1 << L); m.centerY *= (1 << L); }
        seeds = nms(cur, maxOverlap);
        if ((int)seeds.size() > 24) seeds.resize(24);  // 限制种子数，避免窗搜爆炸

        // ---- 渐进细化 k = L-1 .. 1 ----
        for (int k = L - 1; k >= 1; --k) {
            double step = stepAt(k);
            double band = (k == L - 1) ? coarseStep : stepAt(k + 1);
            int scaleK = 1 << k;
            int maxDimK = std::max(tplPyr[k].cols, tplPyr[k].rows);
            int halfWin = maxDimK / 2 + 16;
            std::vector<MatchResult> next;
            for (const auto& sd : seeds) {
                int cxk = (int)(sd.centerX / scaleK + 0.5);
                int cyk = (int)(sd.centerY / scaleK + 0.5);
                int x1 = std::max(0, cxk - halfWin), y1 = std::max(0, cyk - halfWin);
                int x2 = std::min(srcPyr[k].cols, cxk + halfWin);
                int y2 = std::min(srcPyr[k].rows, cyk + halfWin);
                if (x2 <= x1 || y2 <= y1) continue;
                Mat sub = srcPyr[k](Rect(x1, y1, x2 - x1, y2 - y1));
                double f0 = std::max(a0, sd.angle - band);
                double f1 = std::min(a1, sd.angle + band);
                auto r = matchLevel(sub, caches[k], tplW_, tplH_, f0, f1, step, coarseThr, topN * 4);
                for (auto& m : r) {
                    m.centerX = (m.centerX + x1) * scaleK;
                    m.centerY = (m.centerY + y1) * scaleK;
                    next.push_back(m);
                }
            }
            if (next.empty()) break;  // 本层全丢，保留上一层种子
            seeds = nms(next, maxOverlap);
        }

        // ---- 最终全分辨率小窗精扫 ----
        double fineStep = 1.0;
        int margin = (int)(std::max(tplW_, tplH_) * 0.6) + 8;
        std::vector<MatchResult> fine;
        for (const auto& sd : seeds) {
            int cx = (int)(sd.centerX + 0.5), cy = (int)(sd.centerY + 0.5);
            int x1 = std::max(0, cx - margin), y1 = std::max(0, cy - margin);
            int x2 = std::min(src_.cols, cx + margin), y2 = std::min(src_.rows, cy + margin);
            if (x2 <= x1 || y2 <= y1) continue;
            Mat sub = src_(Rect(x1, y1, x2 - x1, y2 - y1));
            double f0 = std::max(a0, sd.angle - 9.0);
            double f1 = std::min(a1, sd.angle + 9.0);
            if (f0 > f1) continue;
            RotatedCache fc; fc.tpl = &tpl_;
            auto r = matchLevel(sub, fc, tplW_, tplH_, f0, f1, fineStep, nccThr, topN * 2);
            for (auto& m : r) { m.centerX += x1; m.centerY += y1; fine.push_back(m); }
        }
        auto res = nms(fine, maxOverlap);
        if (res.empty()) {
            // 兜底：如果种子链全失败，直接在全图上做精细全扫（不会比旧方法更差）
            RotatedCache fc; fc.tpl = &tpl_;
            auto r = matchLevel(src_, fc, tplW_, tplH_, a0, a1, fineStep, nccThr, topN);
            res = nms(r, maxOverlap);
        }

        auto t1 = std::chrono::steady_clock::now();
        elapsedMs = std::chrono::duration<double, std::milli>(t1 - t0).count();
        std::sort(res.begin(), res.end(), [](const MatchResult& a, const MatchResult& b) { return a.centerX < b.centerX; });
        return res;
    }

private:
    Mat src_, tpl_;
    int tplW_ = 0, tplH_ = 0;
};

// ---------------------------------------------------------------------------
// 自测辅助函数
// ---------------------------------------------------------------------------
static Mat drawTemplate(const Mat& src, Point2f c, Size sz, double ang, uchar val) {
    Mat block(sz.height, sz.width, CV_8UC1, Scalar(val));
    Point2f rc(sz.width / 2.f, sz.height / 2.f);
    Mat M = getRotationMatrix2D(rc, ang, 1.0);
    int nw = (int)(std::fabs(sz.width * cos(ang * PI / 180.0)) + std::fabs(sz.height * sin(ang * PI / 180.0)));
    int nh = (int)(std::fabs(sz.width * sin(ang * PI / 180.0)) + std::fabs(sz.height * cos(ang * PI / 180.0)));
    M.at<double>(0, 2) += nw / 2.0 - rc.x;
    M.at<double>(1, 2) += nh / 2.0 - rc.y;
    Mat r(nh, nw, CV_8UC1);
    warpAffine(block, r, M, Size(nw, nh), INTER_LINEAR, BORDER_CONSTANT, Scalar(0));
    int x = (int)(c.x - nw / 2.0), y = (int)(c.y - nh / 2.0);
    int sx = std::max(0, x), sy = std::max(0, y);
    int w = std::min(nw, src.cols - sx), h = std::min(nh, src.rows - sy);
    if (w > 0 && h > 0)
        r(Rect(std::max(0, -x), std::max(0, -y), w, h)).copyTo(src(Rect(sx, sy, w, h)));
    return src;
}

static Mat makeScene(int rows, int cols, Size tsize, const std::vector<Point2f>& centers,
                     const std::vector<double>& angles) {
    Mat img(rows, cols, CV_8UC1, Scalar(0));
    for (size_t i = 0; i < centers.size(); ++i)
        drawTemplate(img, centers[i], tsize, angles[i], 255);
    GaussianBlur(img, img, Size(3, 3), 0.8);
    return img;
}

static int g_fail = 0;
static void check(bool cond, const char* name) {
    printf("  [%s] %s\n", cond ? "PASS" : "FAIL", name);
    if (!cond) ++g_fail;
}

int main() {
    printf("==== GrayMatch 干净版自测 ====\n");

    // ---------- 测试 S1：合成图 800x600，4 个目标，低阈值 ----------
    {
        Size ts(120, 80);
        std::vector<Point2f> c{{220,170},{620,170},{220,470},{620,470}};
        std::vector<double> a{0,30,60,-45};
        Mat scene = makeScene(600, 800, ts, c, a);
        Mat tpl = scene(Rect((int)c[0].x - 60, (int)c[0].y - 40, 120, 80)).clone();
        printf("\n-- 测试 S1: 800x600, 4 个目标 @ {0,30,60,-45}, [-90,90] 步长5, 阈值0.35 --\n");
        GrayMatcher m; m.setSource(scene); m.setTemplate(tpl);
        for (int p = 0; p <= 4; ++p) {
            double ms = 0;
            auto r = m.match(-90, 90, 5, 0.35, 0.25, 20, p, ms);
            double worst = 0;
            for (double ka : a) { double best = 1e9; for (auto& x : r) best = std::min(best, std::fabs(x.angle - ka)); worst = std::max(worst, best); }
            int badBox = 0; for (auto& x : r) if (x.templateWidth != 120 || x.templateHeight != 80) ++badBox;
            double minS = 1e9; for (auto& x : r) minS = std::min(minS, x.score);
            printf("  金字塔=%d 数量=%d 耗时=%.2fms 最大角度误=%.2f 框错误=%d 最低分=%.3f\n",
                   p, (int)r.size(), ms, worst, badBox, minS > 0 ? minS : 0.0);
            if (p == 1 || p == 4) {
                check(r.size() == 4, "S1 数量==4");
                check(worst <= 2.5, "S1 角度误差<=2.5");
                check(badBox == 0, "S1 框尺寸正确");
            }
        }
    }

    // ---------- 测试 S2：大图 1600x1200，4 个目标，全 360° 步长1 ----------
    {
        Size ts(120, 80);
        std::vector<Point2f> c{{400,300},{1200,300},{400,900},{1200,900}};
        std::vector<double> a{0,35,118,250};
        Mat scene = makeScene(1200, 1600, ts, c, a);
        Mat tpl = scene(Rect((int)c[0].x - 60, (int)c[0].y - 40, 120, 80)).clone();
        printf("\n-- 测试 S2: 1600x1200, 4 个目标, 全 [-180,180] 步长1 --\n");
        GrayMatcher m; m.setSource(scene); m.setTemplate(tpl);
        for (int p : {0,1,2,4}) {
            double ms = 0;
            auto r = m.match(-180, 180, 1, 0.35, 0.25, 50, p, ms);
            printf("  金字塔=%d 数量=%d 耗时=%.2fms\n", p, (int)r.size(), ms);
            if (p == 4) { check(r.size() == 4, "S2 金字塔=4 数量==4"); }
        }
    }

    // ---------- 测试 S3：1600x1200 上放 32x18 小模板（回归用例）----------
    {
        Size ts(32, 18);
        std::vector<Point2f> c{{400,300},{1200,300},{400,900},{1200,900}};
        std::vector<double> a{0,35,118,250};
        Mat scene = makeScene(1200, 1600, ts, c, a);
        Mat tpl = scene(Rect((int)c[0].x - 16, (int)c[0].y - 9, 32, 18)).clone();
        printf("\n-- 测试 S3: 1600x1200, 小模板 32x18, 全 [-180,180] 步长1 --\n");
        GrayMatcher m; m.setSource(scene); m.setTemplate(tpl);
        for (int p : {0,1,2,4}) {
            double ms = 0;
            auto r = m.match(-180, 180, 1, 0.35, 0.25, 50, p, ms);
            printf("  金字塔=%d 数量=%d 耗时=%.2fms\n", p, (int)r.size(), ms);
            if (p == 4) { check(r.size() == 4, "S3 金字塔=4 数量==4"); }
        }
    }

    // ---------- 测试 S4：真实旋转数字图（若存在）----------
    {
        std::vector<std::string> paths = {"digits_test.png",
                                          "C:/gmrun5/native/digits_test.png",
                                          "C:/Users/admin/Pictures/灰度匹配/图片1300x1000.png"};
        Mat img = imread(paths[0], IMREAD_GRAYSCALE);
        if (img.empty()) img = imread(paths[1], IMREAD_GRAYSCALE);
        if (img.empty()) img = imread(paths[2], IMREAD_GRAYSCALE);
        if (img.empty()) {
            printf("\n-- 测试 S4: 数字图未找到，跳过 --\n");
        } else {
            printf("\n-- 测试 S4: 真实旋转数字图 %dx%d, 从左下角单元格自动裁剪模板 --\n",
                   img.cols, img.rows);
            Mat roi = img(Rect(0, 670, std::min(220, img.cols), std::min(330, img.rows - 670)));
            Mat bin; threshold(roi, bin, 200, 255, THRESH_BINARY);
            std::vector<std::vector<Point>> ct; findContours(bin, ct, RETR_EXTERNAL, CHAIN_APPROX_SIMPLE);
            size_t bi = 0; double ba = 0;
            for (size_t i = 0; i < ct.size(); ++i) if (contourArea(ct[i]) > ba) { ba = contourArea(ct[i]); bi = i; }
            Rect rr = boundingRect(ct[bi]);
            Mat tpl = roi(Rect(std::max(0,rr.x-4), std::max(0,rr.y-4),
                               std::min(roi.cols,rr.x+rr.width+4)-std::max(0,rr.x-4),
                               std::min(roi.rows,rr.y+rr.height+4)-std::max(0,rr.y-4))).clone();
            printf("  模板 %dx%d\n", tpl.cols, tpl.rows);
            GrayMatcher m; m.setSource(img); m.setTemplate(tpl);
            for (double thr : {0.30, 0.40}) {
                printf("  -- 阈值=%.2f --\n", thr);
                for (int p : {0, 4}) {
                    double ms = 0;
                    auto r = m.match(-5, 65, 1, thr, 0.25, 50, p, ms);
                    int nTrue = 0; for (auto& x : r) if (x.score > 0.90) ++nTrue;
                    printf("    金字塔=%d 数量=%d 高分(>0.9)=%d 耗时=%.2fms\n", p, (int)r.size(), nTrue, ms);
                    if (p == 4) {
                        check(r.size() == 18, "S4 金字塔=4 数量==18");
                    }
                }
            }
        }
    }

    printf("\n==== %s (%d 处失败) ====\n", g_fail == 0 ? "全部通过" : "存在失败", g_fail);
    return g_fail == 0 ? 0 : 1;
}
