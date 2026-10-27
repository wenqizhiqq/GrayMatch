#include "point_matcher.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstring>
#ifdef PM_DEBUG
#include <cstdio>
#endif

#ifdef _OPENMP
#include <omp.h>
#endif

namespace pmatch {
namespace {

constexpr double kPi = 3.14159265358979323846;

// 方向量化 16 bin（22.5°/bin）。粗扫靠它做 O(1) 命中判定，精扫用精确 cos 值。
constexpr int kBins = 16;
constexpr double kBinToDeg = 360.0 / kBins;

// 粗扫几何：位置格网间距 stride、方向位图涂抹半径 rc。
// 自洽条件：rc >= stride/2 + maxRadius*sin(coarseAngleStep/2) + 1，
// 下面按这个式子反推粗角度步长，所以改这两个常量时步长会自动跟着收紧。
constexpr int kCoarseStride = 6;
constexpr int kCoarseRadius = 10;

// 精修最后一级与最终复核共用的容差。两处必须是同一个值，否则爬山目标和复核目标会脱节。
constexpr float kFinalTol = 1.0f;
// 收尾穷举的小窗：位置 ±3px、角度 ±2°。爬坡可能落在无梯度的平台上，窗口要能盖住实测漂移。
constexpr int kFinalWindow = 3;

// 角度 -> 旋转系数对 (cos, -sin)。
// 必须用 OpenCV getRotationMatrix2D 的符号约定：正角 = 把模板按 M=[[c,s],[-s,c]] 转过去，
// 而 gm_match 的 NCC 路径正是拿这个 angle 去 warpAffine 模板的，XAML 叠加层和缺陷检测
// 也都按它解释。内核若用标准数学约定（sin 不取负），上报角度就会整体反号。
inline void angleCS(double deg, float out[2]) {
    const double r = deg * kPi / 180.0;
    out[0] = (float)std::cos(r);
    out[1] = (float)-std::sin(r);
}

inline int directionBin(float dx, float dy) {
    double a = std::atan2((double)dy, (double)dx);
    if (a < 0) a += 2.0 * kPi;
    return (int)std::lround(a * 180.0 / kPi / kBinToDeg) & (kBins - 1);
}

// 模型点方向允许命中的 bin 位：本体 bin 加左右邻 bin（约 ±22.5° 的角度容差）。
inline uint16_t allowedBits(float dx, float dy) {
    const int b = directionBin(dx, dy);
    return (uint16_t)((1u << b) |
                      (1u << ((b + 1) & (kBins - 1))) |
                      (1u << ((b + kBins - 1) & (kBins - 1))));
}

void toGray(const unsigned char* data, int w, int h, int step, int channels,
            std::vector<uint8_t>& out) {
    out.assign((size_t)w * h, 0);
    if (channels == 3) {
        for (int y = 0; y < h; ++y) {
            const unsigned char* s = data + (size_t)y * step;
            uint8_t* d = out.data() + (size_t)y * w;
            for (int x = 0; x < w; ++x) {
                const unsigned b = s[x * 3 + 0], g = s[x * 3 + 1], r = s[x * 3 + 2];
                d[x] = (uint8_t)((19 * r + 183 * g + 54 * b + 128) >> 8);   // Rec.601
            }
        }
    } else {
        for (int y = 0; y < h; ++y)
            std::memcpy(out.data() + (size_t)y * w, data + (size_t)y * step, (size_t)w);
    }
}

// [1 2 1]/4 可分离平滑，边界复制。
void smooth(std::vector<uint8_t>& g, int w, int h, int passes) {
    if (passes <= 0) return;
    std::vector<uint8_t> tmp((size_t)w * h);
    for (int p = 0; p < passes; ++p) {
        for (int y = 0; y < h; ++y) {
            const uint8_t* r = g.data() + (size_t)y * w;
            uint8_t* o = tmp.data() + (size_t)y * w;
            for (int x = 0; x < w; ++x) {
                const int l = r[x > 0 ? x - 1 : 0], c = r[x], rr = r[x + 1 < w ? x + 1 : w - 1];
                o[x] = (uint8_t)((l + 2 * c + rr + 2) >> 2);
            }
        }
        for (int x = 0; x < w; ++x) {
            for (int y = 0; y < h; ++y) {
                const int u = tmp[(size_t)(y > 0 ? y - 1 : 0) * w + x];
                const int c = tmp[(size_t)y * w + x];
                const int d = tmp[(size_t)(y + 1 < h ? y + 1 : h - 1) * w + x];
                g[(size_t)y * w + x] = (uint8_t)((u + 2 * c + d + 2) >> 2);
            }
        }
    }
}

// Sobel 3x3。模长按 |g|/4 折算回灰度级：对比度 Δ 的阶跃边 Sobel 响应是 4Δ，
// 于是 edgeThreshold 可以直接按“灰度跳变至少这么多”理解（1..254）。
// 边界一圈留 0（无梯度）。outMag 可为 null。
void gradientField(const std::vector<uint8_t>& g, int w, int h, int floor_,
                   std::vector<int8_t>& qx, std::vector<int8_t>& qy,
                   std::vector<float>* outMag) {
    qx.assign((size_t)w * h, 0);
    qy.assign((size_t)w * h, 0);
    if (outMag) outMag->assign((size_t)w * h, 0.f);
    if (w < 3 || h < 3) return;

    for (int y = 1; y + 1 < h; ++y) {
        const uint8_t* r0 = g.data() + (size_t)(y - 1) * w;
        const uint8_t* r1 = g.data() + (size_t)y * w;
        const uint8_t* r2 = g.data() + (size_t)(y + 1) * w;
        int8_t* oqx = qx.data() + (size_t)y * w;
        int8_t* oqy = qy.data() + (size_t)y * w;
        float* om = outMag ? outMag->data() + (size_t)y * w : nullptr;
        for (int x = 1; x + 1 < w; ++x) {
            const int gx = (int)r0[x + 1] - (int)r0[x - 1]
                         + 2 * ((int)r1[x + 1] - (int)r1[x - 1])
                         + (int)r2[x + 1] - (int)r2[x - 1];
            const int gy = ((int)r2[x - 1] + 2 * (int)r2[x] + (int)r2[x + 1])
                         - ((int)r0[x - 1] + 2 * (int)r0[x] + (int)r0[x + 1]);
            const double root = std::sqrt((double)gx * gx + (double)gy * gy);
            const double mag = root * 0.25;
            if (om) om[x] = (float)mag;
            if (mag < floor_ || root < 1.0) continue;
            const double inv = 127.0 / root;
            oqx[x] = (int8_t)std::lround(gx * inv);
            oqy[x] = (int8_t)std::lround(gy * inv);
        }
    }
}

struct Seed {
    float x, y;
    double ang;
    float sc;
};

} // namespace

// ============================================================================
// setSource / setTemplate
// ============================================================================

bool PointMatcher::setSource(const unsigned char* data, int w, int h, int step, int channels) {
    if (!data || w < 8 || h < 8) return false;
    std::vector<uint8_t> gray;
    toGray(data, w, h, step, channels, gray);
    smooth(gray, w, h, 1);

    // 场景梯度一次算清并缓存。低阈值 8 只用来丢掉纯噪声像素，
    // 真正的对比度门槛 edgeThreshold 在 match() 里建 mask / 精打分时才套用，
    // 这样调阈值滑块不必重算 Sobel。
    std::vector<int8_t> gx, gy;
    std::vector<float> mag;
    gradientField(gray, w, h, 8, gx, gy, &mag);

    sw_ = w; sh_ = h;
    sx_.swap(gx);
    sy_.swap(gy);
    smag_.assign((size_t)w * h, 0);
    for (size_t i = 0; i < mag.size(); ++i)
        smag_[i] = (uint8_t)std::min(255.0, std::max(0.0, (double)mag[i]));
    return true;
}

bool PointMatcher::setTemplate(const unsigned char* data, int w, int h, int step, int channels) {
    if (!data || w < 8 || h < 8) return false;
    tw_ = w; th_ = h;
    std::vector<uint8_t> gray;
    toGray(data, w, h, step, channels, gray);
    templateGray_.swap(gray);
    mp_.clear();
    suffix_.clear();
    return true;
}

// ============================================================================
// train：模板 -> 带方向的边缘点云（按权重降序，供早停用）
// ============================================================================

bool PointMatcher::train(const PmParams& p) {
    mp_.clear();
    suffix_.clear();
    maxRadius_ = 0.f;
    if (templateGray_.empty()) return false;

    // 平滑必须作用在副本上：templateGray_ 要按原始像素留存，
    // 否则同一个 handle 上重复 match()（多尺度缩放会这么做）会把模板越磨越糊。
    std::vector<uint8_t> tmpl = templateGray_;
    smooth(tmpl, tw_, th_, std::max(0, std::min(3, p.smoothing)));

    std::vector<int8_t> gx, gy;
    std::vector<float> mag;
    const int edgeThr = std::max(1, std::min(254, p.edgeThreshold));
    gradientField(tmpl, tw_, th_, edgeThr, gx, gy, &mag);

    // 点间距：默认按模板短边 1/12，夹在 [3,16]。太小点冗余且慢，太大丢形状细节。
    int spacing = p.pointSpacing > 0 ? p.pointSpacing : (int)std::lround(std::min(tw_, th_) / 12.0);
    spacing = std::max(3, std::min(16, spacing));
    spacing_ = spacing;

    const int maxPts = p.maxPoints > 0
                           ? p.maxPoints
                           : std::max(48, std::min(400, (tw_ / spacing) * (th_ / spacing)));

    const int gw = (tw_ + spacing - 1) / spacing;
    const int gh = (th_ + spacing - 1) / spacing;

    struct Cell { float m; int x, y, bin; };
    std::vector<Cell> cells((size_t)gw * gh, Cell{ -1.f, 0, 0, 0 });

    // 3x3 局部极大：把边缘带压成一条，点正好落在边上；再按格网取每格最强者保证间距。
    for (int y = 1; y + 1 < th_; ++y) {
        for (int x = 1; x + 1 < tw_; ++x) {
            const size_t i = (size_t)y * tw_ + x;
            const float m = mag[i];
            if (m < edgeThr) continue;
            const int a = gx[i], b = gy[i];
            if (a == 0 && b == 0) continue;

            bool isMax = true;
            for (int yy = y - 1; yy <= y + 1 && isMax; ++yy) {
                for (int xx = x - 1; xx <= x + 1; ++xx) {
                    if (xx == x && yy == y) continue;
                    if (mag[(size_t)yy * tw_ + xx] > m) { isMax = false; break; }
                }
            }
            if (!isMax) continue;

            Cell& c = cells[(size_t)(y / spacing) * gw + (x / spacing)];
            if (m > c.m) {
                c.m = m;
                c.x = x;
                c.y = y;
                c.bin = directionBin((float)a / 127.f, (float)b / 127.f);
            }
        }
    }

    std::vector<Cell> pts;
    pts.reserve(cells.size());
    for (const Cell& c : cells)
        if (c.m > 0.f) pts.push_back(c);
    if ((int)pts.size() < 6) return false;      // 点太少没有判别力，直接判定不可用

    std::sort(pts.begin(), pts.end(), [](const Cell& l, const Cell& r) { return l.m > r.m; });
    if ((int)pts.size() > maxPts) pts.resize(maxPts);

    const float wRef = pts.front().m > 1.f ? pts.front().m : 1.f;
    const float cx = tw_ * 0.5f, cy = th_ * 0.5f;
    mp_.reserve(pts.size());
    for (const Cell& c : pts) {
        // 方向从量化值反归一化即可，权重用归一化梯度模：越强的边越可信。
        const size_t i = (size_t)c.y * tw_ + c.x;
        float dx = (float)gx[i] / 127.f, dy = (float)gy[i] / 127.f;
        const double len = std::sqrt((double)dx * dx + (double)dy * dy);
        if (len < 1e-6) continue;
        MPoint q;
        q.x = (float)c.x - cx;
        q.y = (float)c.y - cy;
        q.dx = (float)(dx / len);
        q.dy = (float)(dy / len);
        q.w = std::max(0.05f, c.m / wRef);
        mp_.push_back(q);
    }
    if ((int)mp_.size() < 6) return false;

    // suffix_[i] = Σ w[i..n-1]，suffix_[n] = 0：早停时“剩余权重全算满分”的上界。
    suffix_.assign(mp_.size() + 1, 0.f);
    for (int i = (int)mp_.size() - 1; i >= 0; --i)
        suffix_[i] = suffix_[i + 1] + mp_[i].w;

    for (const MPoint& q : mp_)
        maxRadius_ = std::max(maxRadius_, std::sqrt(q.x * q.x + q.y * q.y));
    if (maxRadius_ < 1.f) maxRadius_ = 1.f;
    return true;
}

// ============================================================================
// 打分
// ============================================================================

float PointMatcher::scoreSoft(float cx, float cy, double /*angleDeg*/, const float* cs,
                              int upto, float tol, float thr, int edgeThreshold) const {
    const int n = (int)mp_.size();
    if (n == 0) return -1.f;
    upto = std::min(upto, n);
    const float total = suffix_[0] - suffix_[upto];
    if (total <= 1e-6f) return -1.f;
    const float need = thr * total;
    const float beyond = suffix_[upto];      // 子集之外的权重，不参与本子上界
    const float cosA = cs[0], sinA = cs[1];
    const int r = std::max(0, (int)std::ceil(tol));

    float acc = 0.f;
    for (int i = 0; i < upto; ++i) {
        const MPoint& m = mp_[i];
        const float px = cx + m.x * cosA - m.y * sinA;
        const float py = cy + m.x * sinA + m.y * cosA;
        const float ndx = m.dx * cosA - m.dy * sinA;
        const float ndy = m.dx * sinA + m.dy * cosA;

        const int ix = (int)std::lround(px), iy = (int)std::lround(py);
        const int x0 = std::max(0, ix - r), x1 = std::min(sw_ - 1, ix + r);
        const int y0 = std::max(0, iy - r), y1 = std::min(sh_ - 1, iy + r);

        float best = 0.f;
        for (int y = y0; y <= y1; ++y) {
            const size_t row = (size_t)y * sw_;
            for (int x = x0; x <= x1; ++x) {
                const size_t idx = row + x;
                if (smag_[idx] < edgeThreshold) continue;      // 弱边缘不算命中
                const float dot = ((float)sx_[idx] / 127.f) * ndx + ((float)sy_[idx] / 127.f) * ndy;
                if (dot > best) {
                    best = dot;
                    if (best >= 0.999f) break;
                }
            }
            if (best >= 0.999f) break;
        }
        if (best < 0.f) best = 0.f;                            // 方向相反记 0 分，不给负分
        else if (best > 1.f) best = 1.f;                       // 方向量化到 int8 后模长可略大于 1
        acc += m.w * best;

        if (acc + (suffix_[i + 1] - beyond) < need) return -1.f;   // 够不着阈值，判死
    }
    return acc / total;
}

float PointMatcher::scoreCoarse(float cx, float cy, const float* rx, const float* ry,
                                const uint16_t* bits, const std::vector<uint16_t>& mask,
                                int upto, float thrTimesTotal, float floorBeyond) const {
    float acc = 0.f;
    for (int i = 0; i < upto; ++i) {
        const int ix = (int)std::lround(cx + rx[i]);
        const int iy = (int)std::lround(cy + ry[i]);
        if ((unsigned)ix < (unsigned)sw_ && (unsigned)iy < (unsigned)sh_ &&
            (mask[(size_t)iy * sw_ + ix] & bits[i]) != 0)
            acc += mp_[i].w;
        if (acc + (suffix_[i + 1] - floorBeyond) < thrTimesTotal) return -1.f;
    }
    return acc;
}

// ============================================================================
// match
// ============================================================================

std::vector<PmMatchResult> PointMatcher::match(const PmParams& params) {
    std::vector<PmMatchResult> out;
    if (sw_ == 0 || sh_ == 0 || templateGray_.empty()) return out;
    if (!train(params)) return out;

    const int nPts = (int)mp_.size();
    const int edgeThr = std::max(1, std::min(254, params.edgeThreshold));
    const double aStart = params.angleStart, aEnd = params.angleEnd;
    if (!(aEnd > aStart)) return out;

    // ---- 粗容差 -> 粗角度步长，保证真姿态不会被格网漏掉 ----
    const int rc = std::min(kCoarseRadius, std::max(3, std::min(sw_, sh_) / 8));
    const int stride = std::max(2, std::min(kCoarseStride, rc));
    const double slack = std::max(1.0, (double)rc - stride * 0.5 - 1.0);
    double coarseStep = std::asin(std::min(1.0, slack / (double)maxRadius_)) * 360.0 / kPi;
    coarseStep = std::max(1.0, std::min(20.0, coarseStep));
    const int nAngles = (int)std::floor((aEnd - aStart) / coarseStep + 1e-6) + 1;

    // ---- 方向位图：bit k = “半径 rc 内存在方向落在 bin k 的边缘” ----
    // 涂源先按格网抽稀（每格保留方向集合的并 + 最强点位置），否则同一条边缘上
    // 成片的像素会把 mask 写爆，800x600 也要几十毫秒。
    std::vector<uint16_t> mask((size_t)sw_ * sh_, 0);
    {
        const int srcSpacing = std::max(3, rc / 2);
        const int gw = (sw_ + srcSpacing - 1) / srcSpacing;
        const int gh = (sh_ + srcSpacing - 1) / srcSpacing;
        struct Cell { float m; int x, y; uint16_t bits; };
        std::vector<Cell> cells((size_t)gw * gh, Cell{ 0.f, 0, 0, 0 });

        for (int y = 1; y + 1 < sh_; ++y) {
            for (int x = 1; x + 1 < sw_; ++x) {
                const size_t i = (size_t)y * sw_ + x;
                if (smag_[i] < edgeThr) continue;
                Cell& c = cells[(size_t)(y / srcSpacing) * gw + (x / srcSpacing)];
                c.bits |= (uint16_t)(1u << directionBin((float)sx_[i] / 127.f, (float)sy_[i] / 127.f));
                if ((float)smag_[i] > c.m) { c.m = (float)smag_[i]; c.x = x; c.y = y; }
            }
        }
        for (const Cell& c : cells) {
            if (c.m <= 0.f || c.bits == 0) continue;
            const int y0 = std::max(0, c.y - rc), y1 = std::min(sh_ - 1, c.y + rc);
            const int x0 = std::max(0, c.x - rc), x1 = std::min(sw_ - 1, c.x + rc);
            for (int y = y0; y <= y1; ++y) {
                uint16_t* row = mask.data() + (size_t)y * sw_;
                for (int x = x0; x <= x1; ++x) row[x] |= c.bits;
            }
        }
    }

    // ---- Pass 1 粗扫：角度格网（OpenMP 并行）x 位置格网，只用最强的一部分点 ----
    const int nc = std::min(nPts, std::max(6, nPts / 3));
    const float coarseTotal = suffix_[0] - suffix_[nc];
    const float coarseBeyond = suffix_[nc];
    const double thr = std::max(0.05, std::min(0.99, params.scoreThreshold));
    // 粗扫只负责“别漏”，严格阈值留给精扫把关（与 gm_match 两趟设计的口径一致）。
    const float coarseThr = std::max(0.30f, (float)thr - 0.20f);
    const float coarseNeed = coarseThr * coarseTotal;
    // 种子留得宽一点：粗分数排序并不总能保证真目标排在假目标前面。
    const int maxSeeds = std::max(192, std::min(1024, std::max(1, params.topN) * 16));

    std::vector<Seed> seeds;
    seeds.reserve(4096);

    const auto t0 = std::chrono::steady_clock::now();

#ifdef _OPENMP
    #pragma omp parallel
#endif
    {
        std::vector<Seed> local;
        std::vector<float> rx(nPts), ry(nPts), rdx(nPts), rdy(nPts);
        std::vector<uint16_t> bits(nPts);

#ifdef _OPENMP
        #pragma omp for schedule(dynamic, 1) nowait
#endif
        for (int ai = 0; ai < nAngles; ++ai) {
            const double ang = aStart + ai * coarseStep;
            float cs[2];
            angleCS(ang, cs);
            const float c = cs[0], s = cs[1];   // (cos, -sin)，见 angleCS 注释
            for (int i = 0; i < nPts; ++i) {
                const MPoint& m = mp_[i];
                rx[i] = m.x * c - m.y * s;
                ry[i] = m.x * s + m.y * c;
                // 方向必须跟着姿态旋转后再量化，否则 bin 判据是错的。
                rdx[i] = m.dx * c - m.dy * s;
                rdy[i] = m.dx * s + m.dy * c;
                bits[i] = allowedBits(rdx[i], rdy[i]);
            }
            for (int y = 0; y < sh_; y += stride) {
                for (int x = 0; x < sw_; x += stride) {
                    const float acc = scoreCoarse((float)x, (float)y, rx.data(), ry.data(),
                                                  bits.data(), mask, nc, coarseNeed, coarseBeyond);
                    if (acc < 0.f) continue;
                    local.push_back(Seed{ (float)x, (float)y, ang, acc / coarseTotal });
                }
            }
        }

#ifdef _OPENMP
        #pragma omp critical
        {
            seeds.insert(seeds.end(), local.begin(), local.end());
        }
#else
        seeds.insert(seeds.end(), local.begin(), local.end());
#endif
    }

    std::sort(seeds.begin(), seeds.end(), [](const Seed& a, const Seed& b) { return a.sc > b.sc; });
#ifdef PM_DEBUG
    printf("  dbg nPts=%d nc=%d maxR=%.1f coarseStep=%.2f nAngles=%d stride=%d rc=%d "
           "coarseRaw=%d coarseTop=%.3f\n",
           nPts, nc, maxRadius_, coarseStep, nAngles, stride, rc, (int)seeds.size(),
           seeds.empty() ? 0.f : seeds.front().sc);
#endif
    {
        std::vector<Seed> kept;
        kept.reserve(maxSeeds);
        const double minDx = tw_ * 0.5 * (1.0 - std::max(0.0, std::min(0.99, params.maxOverlap)));
        const double minDy = th_ * 0.5 * (1.0 - std::max(0.0, std::min(0.99, params.maxOverlap)));
        for (const Seed& sd : seeds) {
            bool dup = false;
            for (const Seed& k : kept) {
                if (std::abs(sd.x - k.x) < minDx && std::abs(sd.y - k.y) < minDy &&
                    std::abs(sd.ang - k.ang) < coarseStep * 0.75) { dup = true; break; }
            }
            if (!dup) kept.push_back(sd);
            if ((int)kept.size() >= maxSeeds) break;
        }
        seeds.swap(kept);
    }
#ifdef PM_DEBUG
    printf("  dbg seedsKept=%d seedsTop=%.3f\n", (int)seeds.size(),
           seeds.empty() ? 0.f : seeds.front().sc);
#endif

    // ---- Pass 2 精扫：逐个种子爬山（位置+角度，软 cos 打分），再全点复核 ----
    const int nf = std::min(nPts, 120);
    std::vector<PmMatchResult> fine;
    fine.reserve(seeds.size());
#ifdef PM_DEBUG
    int dbgRej = 0;
#endif

#ifdef _OPENMP
    #pragma omp parallel
#endif
    {
        std::vector<PmMatchResult> local;
#ifdef _OPENMP
        #pragma omp for schedule(dynamic, 1) nowait
#endif
        for (int si = 0; si < (int)seeds.size(); ++si) {
            Seed sd = seeds[si];
#ifdef PM_DEBUG
            const float sx0 = sd.x, sy0 = sd.y;
            const double sang0 = sd.ang;
#endif
            float cs[2];
            angleCS(sd.ang, cs);

            // 种子只保证“差不远”（位置 ±rc、角度 ±coarseStep），所以爬山阶段一律不做
            // 阈值早停：拿严格阈值去评粗姿态，会把真种子在开始 refinement 之前就判死。
            float best = scoreSoft(sd.x, sd.y, sd.ang, cs, nf, 3.0f, 0.f, edgeThr);

            // 前三级用松容差爬坡，先把粗扫残留的十几像素/近 14° 误差吃掉。
            const float posSteps[3] = { 3.f, 1.f, 1.f };
            const float lvlTol[3] = { 3.0f, 2.0f, 1.5f };
            const double angSteps[3] = { std::max(1.0, coarseStep * 0.5),
                                         std::max(0.5, coarseStep * 0.25), 1.0 };
            for (int lvl = 0; lvl < 3; ++lvl) {
                const float ps = posSteps[lvl];
                const double as = angSteps[lvl];
                const float tol = lvlTol[lvl];
                bool moved = true;
                int guard = 0;
                while (moved && guard++ < 10) {
                    moved = false;
                    const float dxs[6] = { ps, -ps, 0.f, 0.f, 0.f, 0.f };
                    const float dys[6] = { 0.f, 0.f, ps, -ps, 0.f, 0.f };
                    const double das[6] = { 0.0, 0.0, 0.0, 0.0, as, -as };
                    for (int k = 0; k < 6; ++k) {
                        const double na = sd.ang + das[k];
                        if (na < aStart - 1e-9 || na > aEnd + 1e-9) continue;
                        float ncs[2];
                        angleCS(na, ncs);
                        const float sc = scoreSoft(sd.x + dxs[k], sd.y + dys[k], na, ncs,
                                                   nf, tol, 0.f, edgeThr);
                        if (sc > best + 1e-5f) {
                            best = sc;
                            sd.x += dxs[k];
                            sd.y += dys[k];
                            sd.ang = na;
                            cs[0] = ncs[0];
                            cs[1] = ncs[1];
                            moved = true;
                            break;
                        }
                    }
                }
            }

            // 收尾改成小窗穷举，不再继续爬坡：容差窗口让打分是位置的阶梯函数，
            // 爬坡一旦落到平台上（对称模板实测能偏 3px 而分数不变）就没有梯度可循，
            // 只能靠穷举把真位姿捞回来。穷举直接用严格阈值 + 严格容差，和最终复核同源，
            // 于是既没有“松目标优化、紧目标评判”的分数悬崖，也顺手把达不到阈值的姿态早停掉。
            float fscore = -1.f;
            float bx = sd.x, by = sd.y;
            double ba = sd.ang;
            for (double da = -2.0; da <= 2.001; da += 1.0) {
                const double na = sd.ang + da;
                if (na < aStart - 1e-9 || na > aEnd + 1e-9) continue;
                float nc2[2];
                angleCS(na, nc2);
                for (int oy = -kFinalWindow; oy <= kFinalWindow; ++oy) {
                    for (int ox = -kFinalWindow; ox <= kFinalWindow; ++ox) {
                        const float v = scoreSoft(sd.x + (float)ox, sd.y + (float)oy, na, nc2,
                                                  nPts, kFinalTol, (float)thr, edgeThr);
                        if (v > fscore) {
                            fscore = v;
                            bx = sd.x + (float)ox;
                            by = sd.y + (float)oy;
                            ba = na;
                        }
                    }
                }
            }
            if (fscore < 0.f) {
#ifdef PM_DEBUG
                if (dbgRej++ < 8)
                    printf("  rej seed(%.0f,%.0f,%.2f) climb=%.3f -> 小窗内无姿态过阈值\n",
                           sx0, sy0, sang0, best);
#endif
                continue;
            }
            sd.x = bx; sd.y = by; sd.ang = ba;

            // 抛物线插值要的是同容差、但不做早停的度量
            auto evalAll = [&](float x, float y, double ang) -> float {
                float a[2];
                angleCS(ang, a);
                const float v = scoreSoft(x, y, ang, a, nPts, kFinalTol, 0.f, edgeThr);
                return v < 0.f ? 0.f : v;
            };

            // 抛物线插值到亚像素 / 亚度（步长 1px / 1°，插值量限制在 ±1 内）
            auto vertex = [](float a, float b, float c) {
                const float d = a - 2.f * b + c;
                if (std::abs(d) < 1e-6f) return 0.f;
                return std::max(-1.f, std::min(1.f, 0.5f * (a - c) / d));
            };
            sd.x += vertex(evalAll(sd.x - 1.f, sd.y, sd.ang), fscore, evalAll(sd.x + 1.f, sd.y, sd.ang));
            sd.y += vertex(evalAll(sd.x, sd.y - 1.f, sd.ang), fscore, evalAll(sd.x, sd.y + 1.f, sd.ang));
            sd.ang += vertex(evalAll(sd.x, sd.y, sd.ang - 1.0), fscore,
                              evalAll(sd.x, sd.y, sd.ang + 1.0));
            fscore = evalAll(sd.x, sd.y, sd.ang);
            if (fscore < (float)thr) continue;

            PmMatchResult r{};
            r.score = fscore;
            r.centerX = sd.x;
            r.centerY = sd.y;
            r.angle = sd.ang;
            r.templateWidth = tw_;
            r.templateHeight = th_;
            r.leftTopX = (int)std::lround(sd.x - tw_ * 0.5);
            r.leftTopY = (int)std::lround(sd.y - th_ * 0.5);
            r.level = 0;
            local.push_back(r);
        }

#ifdef _OPENMP
        #pragma omp critical
        {
            fine.insert(fine.end(), local.begin(), local.end());
        }
#else
        fine.insert(fine.end(), local.begin(), local.end());
#endif
    }

    const auto t1 = std::chrono::steady_clock::now();
    lastMatchMs_ = std::chrono::duration<double, std::milli>(t1 - t0).count();
#ifdef PM_DEBUG
    printf("  dbg fine=%d\n", (int)fine.size());
#endif

    // ---- 结果 NMS（与 gm_match 同一套“占较小框的比例”重叠判据）----
    std::sort(fine.begin(), fine.end(),
              [](const PmMatchResult& a, const PmMatchResult& b) { return a.score > b.score; });
    for (const PmMatchResult& cur : fine) {
        bool sup = false;
        for (const PmMatchResult& k : out) {
            const double x1 = std::max((double)cur.leftTopX, (double)k.leftTopX);
            const double y1 = std::max((double)cur.leftTopY, (double)k.leftTopY);
            const double x2 = std::min((double)(cur.leftTopX + cur.templateWidth),
                                       (double)(k.leftTopX + k.templateWidth));
            const double y2 = std::min((double)(cur.leftTopY + cur.templateHeight),
                                       (double)(k.leftTopY + k.templateHeight));
            const double inter = std::max(0.0, x2 - x1) * std::max(0.0, y2 - y1);
            const double minArea = std::min((double)cur.templateWidth * cur.templateHeight,
                                            (double)k.templateWidth * k.templateHeight);
            if (minArea > 0 && inter / minArea > params.maxOverlap) { sup = true; break; }
        }
        if (!sup) out.push_back(cur);
        if ((int)out.size() >= std::max(1, params.topN)) break;
    }
    return out;
}

} // namespace pmatch
