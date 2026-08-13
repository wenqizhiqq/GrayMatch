#include "gray_model_native.h"
#include <opencv2/opencv.hpp>
#include <cstdio>
#include <vector>
#include <algorithm>
#include <chrono>
#include <cmath>

using namespace cv;

// Draw a SOLID-WHITE target, identical to the C# MatcherTests scene, so accuracy
// numbers are directly comparable. Size follows the scene spec (so small templates
// really are small -- rotationally ambiguous, which is realistic for tiny patches).
static void drawTarget(Mat& img, Point2f center, Size size, double angle, bool /*small*/) {
    Mat patch(size.height, size.width, CV_8UC3, Scalar(255, 255, 255));
    double rad = angle * CV_PI / 180.0;
    int newW = (int)(std::abs(size.width * cos(rad)) + std::abs(size.height * sin(rad)));
    int newH = (int)(std::abs(size.width * sin(rad)) + std::abs(size.height * cos(rad)));
    Point2f rc(size.width / 2.f, size.height / 2.f);
    Mat M = getRotationMatrix2D(rc, angle, 1.0);
    M.at<double>(0, 2) += (newW - size.width) / 2.0;
    M.at<double>(1, 2) += (newH - size.height) / 2.0;
    Mat rotated(newH, newW, CV_8UC3);
    warpAffine(patch, rotated, M, Size(newW, newH), INTER_LINEAR, BORDER_CONSTANT, Scalar(0, 0, 0));
    int x = (int)(center.x - newW / 2.0), y = (int)(center.y - newH / 2.0);
    int sx = std::max(0, x), sy = std::max(0, y);
    int w = std::min(newW, img.cols - sx), h = std::min(newH, img.rows - sy);
    if (w <= 0 || h <= 0) return;
    int ox = x < 0 ? -x : 0, oy = y < 0 ? -y : 0;
    rotated(Rect(ox, oy, w, h)).copyTo(img(Rect(sx, sy, w, h)));
}

struct SceneSpec {
    int rows, cols;
    Size tsize;
    bool small;
    std::vector<Point2f> centers;
    std::vector<double> angles;
};

static Mat buildScene(const SceneSpec& s) {
    Mat src(s.rows, s.cols, CV_8UC3, Scalar(0, 0, 0));
    for (size_t i = 0; i < s.centers.size(); ++i)
        drawTarget(src, s.centers[i], s.tsize, s.angles[i], s.small);
    GaussianBlur(src, src, Size(3, 3), 0.8);
    return src;
}

static double nowMs() {
    return std::chrono::duration<double, std::milli>(
        std::chrono::steady_clock::now().time_since_epoch()).count();
}

// Reconstruct what the OLD (regressed) and the NEW logic would choose for L + coarse angle step.
static void diagL(int minTpl, int minSrc, int pyramid, double baseFine) {
    // OLD
    int Lo = pyramid + 1;
    while (Lo > 1 && ((minTpl >> Lo) < 10 || (minSrc >> Lo) < 40)) --Lo;
    if (Lo < 1) Lo = 1;
    double stepOld = baseFine * (1 << Lo);  // OLD had NO clamp
    // NEW: image-driven L (capped at 6), fixed coarse angle step of 15 deg.
    // The progressive per-level refinement (each level halves the step) is what
    // actually pins the angle down, so a fixed coarse step is sufficient and keeps
    // the coarsest seed within +/-7.5 deg (well inside the 0.35x pass's +/-9 deg band).
    int Ln = pyramid + 1;
    while (Ln < 6 && ((minSrc >> (Ln + 1)) >= 64)) ++Ln;
    while (Ln > 1 && (minTpl >> Ln) < (Ln >= 3 ? 6 : 4)) --Ln;
    if (Ln < 1) Ln = 1;
    double stepNew = 15.0;   // fixed coarse angle step at every depth
    printf("    diag: minTpl=%d minSrc=%d pyramid=%d\n", minTpl, minSrc, pyramid);
    printf("      OLD -> L=%d  coarseImg=%dx%d  coarseStep=%.1f deg  ~%d angles  coarsePxAng=%.1e\n",
           Lo, minSrc >> Lo, minSrc >> Lo, stepOld, (int)(360.0 / stepOld),
           (double)((minSrc >> Lo) * (minSrc >> Lo)) * (360.0 / stepOld));
    printf("      NEW -> L=%d  coarseImg=%dx%d  coarseStep=%.1f deg  ~%d angles  coarsePxAng=%.1e\n",
           Ln, minSrc >> Ln, minSrc >> Ln, stepNew, (int)(360.0 / stepNew),
           (double)((minSrc >> Ln) * (minSrc >> Ln)) * (360.0 / stepNew));
}

int main() {
    void* h = gm_create();
    const int N = 15;
    auto run = [&](const Mat& gray, const Mat& tpl, int p, double a0, double a1, double step,
                   double thr, int topN) -> std::pair<int, double> {
        gm_set_source(h, gray.data, gray.cols, gray.rows, (int)gray.step, 1);
        gm_set_template(h, tpl.data, tpl.cols, tpl.rows, (int)tpl.step, 1);
        std::vector<double> times; int lastN = 0;
        for (int r = 0; r < N; ++r) {
            GmMatchResult out[128];
            int n = gm_match(h, p, a0, a1, step, thr, 0.25, topN, 0, out, 128);
            if (r >= 3) { times.push_back(gm_get_last_match_ms(h)); lastN = n; }
        }
        std::sort(times.begin(), times.end());
        return { lastN, times[times.size() / 2] };
    };

    // ---- Test 1: 800x600, 4 targets @ {0,30,60,-45}, [-90,90] step 5 (correctness + speed) ----
    SceneSpec t1; t1.rows = 600; t1.cols = 800; t1.tsize = Size(120, 80); t1.small = false;
    t1.centers = { Point2f(220,170), Point2f(620,170), Point2f(220,470), Point2f(620,470) };
    t1.angles = { 0, 30, 60, -45 };
    Mat color1 = buildScene(t1); Mat gray1; cvtColor(color1, gray1, COLOR_BGR2GRAY);
    Mat tpl1 = gray1(Rect((int)t1.centers[0].x - 60, (int)t1.centers[0].y - 40, 120, 80)).clone();
    const std::vector<double> known1 = { 0, 30, 60, -45 };
    printf("==================================================================\n");
    printf(" TEST 1 : 800x600, 4 targets @ {0,30,60,-45}, range [-90,90] step 5\n");
    printf("          expected: count>=4, score>=0.35, |angleErr|<=2.5, box 120x80\n");
    printf("------------------------------------------------------------------\n");
    printf("%-10s %8s %8s %8s %8s %s\n", "pyramid", "count", "tMin", "tMed", "tMax", "angleErr/box/score");
    for (int p = 0; p <= 4; ++p) {
        gm_set_source(h, gray1.data, gray1.cols, gray1.rows, (int)gray1.step, 1);
        gm_set_template(h, tpl1.data, tpl1.cols, tpl1.rows, (int)tpl1.step, 1);
        std::vector<double> times; std::vector<GmMatchResult> last;
        for (int r = 0; r < N; ++r) {
            GmMatchResult out[64];
            int n = gm_match(h, p, -90, 90, 5, 0.35, 0.25, 20, 0, out, 64);
            if (r >= 3) { times.push_back(gm_get_last_match_ms(h)); if (r == N - 1) last.assign(out, out + n); }
        }
        std::sort(times.begin(), times.end());
        double worst = 0;
        for (double ka : known1) { double best = 1e9; for (auto& r : last) best = std::min(best, std::abs(r.angle - ka)); worst = std::max(worst, best); }
        int badBox = 0; for (auto& r : last) if (r.templateWidth != 120 || r.templateHeight != 80) badBox++;
        double minS = 1e9; for (auto& r : last) minS = std::min(minS, r.score);
        printf("%-10d %8d %8.2f %8.2f %8.2f  angErr=%.2f boxBad=%d minScore=%.3f\n",
               p, (int)last.size(), times.front(), times[times.size()/2], times.back(), worst, badBox, minS > 0 ? minS : 0.0);
    }

    // ---- Test 2: 900x600, 4 targets, full [-180,180] step 1 (perf) ----
    SceneSpec t2; t2.rows = 600; t2.cols = 900; t2.tsize = Size(120, 80); t2.small = false;
    t2.centers = { Point2f(220,180), Point2f(680,180), Point2f(220,480), Point2f(680,480) };
    t2.angles = { 0, 30, 60, -45 };
    Mat color2 = buildScene(t2); Mat gray2; cvtColor(color2, gray2, COLOR_BGR2GRAY);
    Mat tpl2 = gray2(Rect((int)t2.centers[0].x - 60, (int)t2.centers[0].y - 40, 120, 80)).clone();
    printf("\n==================================================================\n");
    printf(" TEST 2 : 900x600, 4 targets, full sweep [-180,180] step 1 (perf)\n");
    printf("          expected: count>=3\n");
    printf("------------------------------------------------------------------\n");
    for (int p : { 1, 2, 3, 4 }) {
        auto [cnt, med] = run(gray2, tpl2, p, -180, 180, 1, 0.35, 50);
        printf("  pyramid=%d  count=%d  median=%.2f ms\n", p, cnt, med);
    }

    // ---- Test 3: LARGE image 1600x1200, normal 120x80 template, full sweep step 1 ----
    SceneSpec t3; t3.rows = 1200; t3.cols = 1600; t3.tsize = Size(120, 80); t3.small = false;
    t3.centers = { Point2f(400,300), Point2f(1200,300), Point2f(400,900), Point2f(1200,900) };
    t3.angles = { 0, 35, 118, 250 };
    Mat color3 = buildScene(t3); Mat gray3; cvtColor(color3, gray3, COLOR_BGR2GRAY);
    Mat tpl3 = gray3(Rect((int)t3.centers[0].x - 60, (int)t3.centers[0].y - 40, 120, 80)).clone();
    printf("\n==================================================================\n");
    printf(" TEST 3 : 1600x1200 LARGE, normal 120x80 template, full [-180,180] step 1\n");
    printf("          compare legacy(pyramid=0) vs pyramid to show speedup on big images\n");
    printf("------------------------------------------------------------------\n");
    for (int p : { 0, 1, 2, 3, 4 }) {
        auto [cnt, med] = run(gray3, tpl3, p, -180, 180, 1, 0.35, 50);
        printf("  pyramid=%d  count=%d  median=%.2f ms\n", p, cnt, med);
    }

    // ---- Test 4: LARGE image 1600x1200, SMALL 32x18 template = the 4x regression trigger ----
    SceneSpec t4; t4.rows = 1200; t4.cols = 1600; t4.tsize = Size(32, 18); t4.small = true;
    t4.centers = { Point2f(400,300), Point2f(1200,300), Point2f(400,900), Point2f(1200,900) };
    t4.angles = { 0, 35, 118, 250 };
    Mat color4 = buildScene(t4); Mat gray4; cvtColor(color4, gray4, COLOR_BGR2GRAY);
    Mat tpl4 = gray4(Rect((int)t4.centers[0].x - 16, (int)t4.centers[0].y - 9, 32, 18)).clone();
    printf("\n==================================================================\n");
    printf(" TEST 4 : 1600x1200 LARGE, SMALL 32x18 template, full [-180,180] step 1\n");
    printf("          THE REGRESSION CASE (small template forced old L->1, coarse blowup)\n");
    printf("------------------------------------------------------------------\n");
    diagL(18, 1200, 4, 1.0);
    for (int p : { 0, 1, 2, 4 }) {
        auto [cnt, med] = run(gray4, tpl4, p, -180, 180, 1, 0.35, 50);
        printf("  pyramid=%d  count=%d  median=%.2f ms\n", p, cnt, med);
    }

    gm_destroy(h);
    printf("\nDONE\n");
    return 0;
}
