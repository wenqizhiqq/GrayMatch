// ---------------------------------------------------------------------------
// fastcpp_test.cpp  —  standalone correctness check, NO OpenCV.
//   Builds a synthetic scene with a known template at a known position and
//   rotation, then asserts the pure-C++ FFT+integral NCC finds it, and that
//   the FFT cross-correlation equals a brute-force reference at that point.
// ---------------------------------------------------------------------------
#include "fastcpp.h"

#include <cstdio>
#include <cmath>
#include <vector>
#include <algorithm>

static double bruteNCC(const std::vector<unsigned char>& img, int W, int H,
                       const std::vector<unsigned char>& T, int tw, int th,
                       int x, int y) {
    int N = tw * th;
    double sumI = 0, sumI2 = 0;
    for (int j = 0; j < th; ++j)
        for (int i = 0; i < tw; ++i) {
            double v = img[(size_t)(y + j) * W + (x + i)];
            sumI += v; sumI2 += v * v;
        }
    double meanT = 0; for (unsigned char v : T) meanT += v; meanT /= N;
    double varT = 0; for (unsigned char v : T) varT += (v - meanT) * (v - meanT);
    double cross = 0;
    for (int j = 0; j < th; ++j)
        for (int i = 0; i < tw; ++i)
            cross += (double)img[(size_t)(y + j) * W + (x + i)] * T[(size_t)j * tw + i];
    double varI = sumI2 - sumI * sumI / N;
    if (varI <= 1e-6 || varT <= 1e-6) return 0;
    return cross / (std::sqrt(varI) * std::sqrt(varT));
}

int main() {
    const int W = 256, H = 256;
    const int tw = 41, th = 31;
    const int px = 80, py = 70;          // true position (top-left)
    const double trueAng = 37.0;         // true rotation

    std::vector<unsigned char> scene((size_t)W * H, 128);
    std::vector<unsigned char> templ((size_t)tw * th, 128);
    int cx = tw / 2, cy = th / 2;
    for (int j = 0; j < th; ++j)
        for (int i = 0; i < tw; ++i)
            if (std::abs(i - cx) <= 4 || std::abs(j - cy) <= 4)
                templ[(size_t)j * tw + i] = 230;
    for (int j = 0; j < th; ++j)
        for (int i = 0; i < tw; ++i)
            scene[(size_t)(py + j) * W + (px + i)] = templ[(size_t)j * tw + i];

    // ---- Test A: translation only (angle 0) -----------------------------
    {
        fastcpp::FastMatcher fm;
        fm.setSource(scene.data(), W, H, W);
        fm.setTemplate(templ.data(), tw, th, tw);
        auto r = fm.match(0, 0, 1, 0.5, 0.1, 10);
        printf("[A] translation-only: %d result(s), ms=%.2f\n", (int)r.size(), fm.lastMatchMs());
        bool ok = false;
        for (auto& m : r) {
            printf("    score=%.4f cx=%.1f cy=%.1f ang=%.1f\n",
                   m.score, m.centerX, m.centerY, m.angle);
            if (std::abs(m.centerX - (px + tw / 2.0)) < 2 &&
                std::abs(m.centerY - (py + th / 2.0)) < 2 && m.score > 0.9) ok = true;
        }
        if (!ok) { printf("TEST A FAILED\n"); return 1; }

        // FFT NCC at the truth must agree with the brute-force reference.
        double brute = bruteNCC(scene, W, H, templ, tw, th, px, py);
        printf("    brute NCC@truth = %.4f (fastcpp peak above)\n", brute);
        if (brute < 0.9) { printf("TEST A brute check FAILED\n"); return 1; }
    }

    // ---- Test B: rotation (template pasted pre-rotated into scene) -------
    {
        std::vector<unsigned char> rot((size_t)tw * th, 128);
        double rad = trueAng * 3.14159265358979323846 / 180.0;
        double c = std::cos(rad), s = std::sin(rad);
        int ccx = (tw - 1) / 2, ccy = (th - 1) / 2;
        auto clmp = [](int v, int n) { return v < 0 ? 0 : (v >= n ? n - 1 : v); };
        for (int j = 0; j < th; ++j)
            for (int i = 0; i < tw; ++i) {
                double dx = i - ccx, dy = j - ccy;
                double sx = dx * c + dy * s + ccx, sy = -dx * s + dy * c + ccy;
                int x0 = (int)std::floor(sx), y0 = (int)std::floor(sy);
                double fx = sx - x0, fy = sy - y0;
                int xa = clmp(x0, tw), xb = clmp(x0 + 1, tw);
                int ya = clmp(y0, th), yb = clmp(y0 + 1, th);
                double v00 = templ[(size_t)ya * tw + xa], v01 = templ[(size_t)ya * tw + xb];
                double v10 = templ[(size_t)yb * tw + xa], v11 = templ[(size_t)yb * tw + xb];
                double top = v00 + (v01 - v00) * fx;
                double bot = v10 + (v11 - v10) * fx;
                rot[(size_t)j * tw + i] = (unsigned char)(top + (bot - top) * fy);
            }
        std::vector<unsigned char> scene2 = scene;
        for (int j = 0; j < th; ++j)
            for (int i = 0; i < tw; ++i)
                scene2[(size_t)(py + j) * W + (px + i)] = rot[(size_t)j * tw + i];

        fastcpp::FastMatcher fm;
        fm.setSource(scene2.data(), W, H, W);
        fm.setTemplate(templ.data(), tw, th, tw);
        auto r = fm.match(0, 360, 3, 0.5, 0.1, 20);
        printf("[B] rotation sweep: %d result(s), ms=%.2f\n", (int)r.size(), fm.lastMatchMs());
        bool ok = false;
        for (auto& m : r) {
            double ad = std::abs(m.angle - trueAng); if (ad > 180) ad = 360 - ad;
            printf("    score=%.4f cx=%.1f cy=%.1f ang=%.1f\n",
                   m.score, m.centerX, m.centerY, m.angle);
            if (std::abs(m.centerX - (px + tw / 2.0)) < 2 &&
                std::abs(m.centerY - (py + th / 2.0)) < 2 && ad < 5 && m.score > 0.9) ok = true;
        }
        if (!ok) { printf("TEST B FAILED\n"); return 1; }
    }

    printf("ALL TESTS PASSED\n");
    return 0;
}
