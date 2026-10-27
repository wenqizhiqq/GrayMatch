// ---------------------------------------------------------------------------
// point_matcher_test.cpp — 点匹配内核的独立验证，不依赖 OpenCV。
//   合成一张 800x600 图，把同一个不对称模板放在 4 个位置、4 个旋转角，
//   其中一个目标对比度压到 0.45 倍（模拟光照变暗），检查：
//     1) 检出数量与位置/角度误差
//     2) 低对比度目标是否仍能检出（点匹配的核心卖点）
//     3) 搜索耗时
//   编译：见 _build_pm.bat（cl /O2 /std:c++17 /openmp）
// ---------------------------------------------------------------------------
#include "point_matcher.h"

#include <cmath>
#include <cstdio>
#include <vector>

namespace {

const double kPi = 3.14159265358979323846;
const int TW = 120, TH = 80;

// 不对称图案：横条 + 竖条 + 一个偏置圆，保证旋转可辨、边缘方向多样。
void buildTemplate(std::vector<unsigned char>& t) {
    t.assign((size_t)TW * TH, 200);
    auto put = [&](int x, int y, int v) {
        if (x >= 0 && x < TW && y >= 0 && y < TH) t[(size_t)y * TW + x] = (unsigned char)v;
    };
    for (int y = 20; y < 60; ++y)
        for (int x = 15; x < 105; ++x) put(x, y, 40);              // 暗横块
    for (int y = 8; y < 72; ++y)
        for (int x = 52; x < 68; ++x) put(x, y, 30);               // 竖条
    for (int y = 0; y < TH; ++y)
        for (int x = 0; x < TW; ++x) {
            const double dx = x - 92, dy = y - 18;
            if (dx * dx + dy * dy <= 13 * 13) put(x, y, 250);      // 亮圆（不对称标志）
        }
    for (int y = 62; y < 74; ++y)
        for (int x = 18; x < 46; ++x) put(x, y, 120);              // 灰角块
}

// 把模板以 (cx,cy) 为中心旋转 ang 度贴进场景；gain 缩放对比度。
void paste(std::vector<unsigned char>& scene, int W, int H,
           const std::vector<unsigned char>& t, double cx, double cy, double ang, double gain) {
    const double r = ang * kPi / 180.0;
    const float cs = (float)std::cos(r), sn = (float)std::sin(r);
    const int reach = (int)std::ceil(std::sqrt(TW * TW + TH * TH) / 2.0) + 2;
    for (int y = (int)cy - reach; y <= (int)cy + reach; ++y) {
        for (int x = (int)cx - reach; x <= (int)cx + reach; ++x) {
            if (x < 0 || x >= W || y < 0 || y >= H) continue;
            const float vx = (float)(x - cx), vy = (float)(y - cy);
            // dst(x,y)=src(M^-1·(v))，M=[[c,s],[-s,c]]（OpenCV getRotationMatrix2D 正角约定）
            const float tx = vx * cs - vy * sn + TW * 0.5f;
            const float ty = vx * sn + vy * cs + TH * 0.5f;
            if (tx < 0 || tx >= TW - 1 || ty < 0 || ty >= TH - 1) continue;
            const int x0 = (int)tx, y0 = (int)ty;
            const float fx = tx - x0, fy = ty - y0;
            const float v = t[(size_t)y0 * TW + x0] * (1 - fx) * (1 - fy) +
                            t[(size_t)y0 * TW + x0 + 1] * fx * (1 - fy) +
                            t[(size_t)(y0 + 1) * TW + x0] * (1 - fx) * fy +
                            t[(size_t)(y0 + 1) * TW + x0 + 1] * fx * fy;
            const int base = 200;
            scene[(size_t)y * W + x] = (unsigned char)std::max(0.0, std::min(255.0,
                base + (v - base) * gain));
        }
    }
}

struct Truth { double cx, cy, ang; };

int runCase(const char* name, const std::vector<unsigned char>& scene, int W, int H,
            const std::vector<unsigned char>& tmpl, const std::vector<Truth>& truths,
            const pmatch::PmParams& p, double tolPx, double tolDeg, int minHits,
            int maxHits = 1 << 30) {
    pmatch::PointMatcher pm;
    pm.setSource(scene.data(), W, H, W, 1);
    pm.setTemplate(tmpl.data(), TW, TH, TW, 1);
    const auto res = pm.match(p);
    int hits = 0;
    double worstPx = 0, worstDeg = 0;
    for (const Truth& g : truths) {
        const pmatch::PmMatchResult* best = nullptr;
        for (const auto& r : res) {
            if (std::abs(r.centerX - g.cx) > 12 || std::abs(r.centerY - g.cy) > 12) continue;
            double da = std::abs(r.angle - g.ang);
            while (da > 180) da = std::abs(da - 360);
            if (da > 12) continue;
            if (!best || r.score > best->score) best = &r;
        }
        if (!best) { printf("    [MISS] truth (%.0f,%.0f) %.0f°\n", g.cx, g.cy, g.ang); continue; }
        double da = std::abs(best->angle - g.ang);
        while (da > 180) da = std::abs(da - 360);
        worstPx = std::max(worstPx, std::hypot(best->centerX - g.cx, best->centerY - g.cy));
        worstDeg = std::max(worstDeg, da);
        ++hits;
    }
    const bool ok = hits >= minHits && hits <= maxHits && worstPx <= tolPx && worstDeg <= tolDeg;
    printf("[%s] pts=%d res=%d hits=%d/%d(want %d..%d) posErr<=%.2fpx angErr<=%.2f° ms=%.2f thr=%.2f edge=%d -> %s\n",
           name, pm.pointCount(), (int)res.size(), hits, (int)truths.size(), minHits, maxHits,
           worstPx, worstDeg, pm.lastMatchMs(), p.scoreThreshold, p.edgeThreshold,
           ok ? "PASS" : "FAIL");
    if (!res.empty()) {
        printf("    scores:");
        for (size_t i = 0; i < res.size() && i < 6; ++i) printf(" %.3f@%.0f,%.0f/%.1f°",
                                                               res[i].score, res[i].centerX, res[i].centerY, res[i].angle);
        printf("\n");
    }
    return ok ? 0 : 1;
}

} // namespace

int main() {
    const int W = 800, H = 600;
    std::vector<unsigned char> tmpl;
    buildTemplate(tmpl);

    std::vector<unsigned char> scene((size_t)W * H, 200);
    // 背景加点无关的结构，避免整幅平坦
    for (int y = 0; y < H; y += 37)
        for (int x = 0; x < W; ++x) scene[(size_t)y * W + x] = 150;

    const std::vector<Truth> full = { { 180, 150, 0 }, { 560, 160, 30 }, { 200, 430, 60 }, { 600, 440, -45 } };
    for (const Truth& g : full) paste(scene, W, H, tmpl, g.cx, g.cy, g.ang, 1.0);

    pmatch::PmParams p;
    p.angleStart = -90; p.angleEnd = 90; p.angleStep = 1;
    p.scoreThreshold = 0.5; p.maxOverlap = 0.1; p.topN = 10;
    p.edgeThreshold = 30; p.smoothing = 1;

    int fail = 0;
    fail += runCase("A 4 targets, contrast 1.0", scene, W, H, tmpl, full, p, 2.5, 2.5, 4);

    // B：把第 4 个目标重贴成 0.45 倍对比度（光照变暗），降低 edgeThreshold 后应仍检出
    std::vector<unsigned char> dim = scene;
    paste(dim, W, H, tmpl, 600, 440, -45, 0.45);
    pmatch::PmParams pb = p;
    pb.edgeThreshold = 10;
    fail += runCase("B weak-contrast target", dim, W, H, tmpl, full, pb, 2.5, 2.5, 4);

    // C：同样用 dim 场景，但把 edgeThreshold 抬到 100。0.45 倍对比度目标的边缘折算只有约
    //    76 个灰度级（强目标是 170），应当落选；用来证明阈值真的在起作用，
    //    而不是“不管什么参数都检出 4 个”。
    pmatch::PmParams pc = p;
    pc.edgeThreshold = 100;
    fail += runCase("C weak target must drop out", dim, W, H, tmpl, full, pc, 2.5, 2.5, 2, 3);

    // F：对照 —— 全强对比度场景 + 同样的高阈值，应当 4 个全中。
    //    若这里掉召回，说明高阈值下点太稀是内核问题而不是正常的取舍。
    fail += runCase("F high threshold, all strong", scene, W, H, tmpl, full, pc, 2.5, 2.5, 4);

    // D：无目标场景必须报空（假阳性检查）
    std::vector<unsigned char> blank((size_t)W * H, 200);
    for (int y = 0; y < H; y += 37)
        for (int x = 0; x < W; ++x) blank[(size_t)y * W + x] = 150;
    {
        pmatch::PointMatcher pm;
        pm.setSource(blank.data(), W, H, W, 1);
        pm.setTemplate(tmpl.data(), TW, TH, TW, 1);
        const auto res = pm.match(p);
        printf("[D no target] res=%d -> %s\n", (int)res.size(), res.empty() ? "PASS" : "FAIL");
        if (!res.empty()) ++fail;
    }

    // E：大角度全量程 + 步进 5°，看耗时是否仍在预算内
    pmatch::PmParams pe = p;
    pe.angleStart = -180; pe.angleEnd = 180; pe.angleStep = 5;
    pmatch::PointMatcher pmE;
    pmE.setSource(scene.data(), W, H, W, 1);
    pmE.setTemplate(tmpl.data(), TW, TH, TW, 1);
    const auto resE = pmE.match(pe);
    printf("[E full sweep] res=%d ms=%.2f -> %s\n", (int)resE.size(), pmE.lastMatchMs(),
           (int)resE.size() >= 4 ? "PASS" : "FAIL");
    if ((int)resE.size() < 4) ++fail;

    // G：诊断用 —— 场景里只放 0° 那一个目标。若这里掉，就是姿态相关的内核问题；
    //    若这里中而 F 不中，则是多种子互相挤掉（种子限流/NMS 太狠）。
    std::vector<unsigned char> one((size_t)W * H, 200);
    for (int y = 0; y < H; y += 37)
        for (int x = 0; x < W; ++x) one[(size_t)y * W + x] = 150;
    paste(one, W, H, tmpl, 180, 150, 0, 1.0);
    fail += runCase("G single 0-deg target, edge100", one, W, H, tmpl, { { 180, 150, 0 } }, pc, 2.5, 2.5, 1);
    fail += runCase("H single 0-deg target, edge30", one, W, H, tmpl, { { 180, 150, 0 } }, p, 2.5, 2.5, 1);

    printf("\n%s\n", fail == 0 ? "ALL PASS" : "SOME FAILED");
    return fail;
}
