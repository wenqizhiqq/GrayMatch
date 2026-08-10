#include "gray_model_native.h"
#include <opencv2/opencv.hpp>
#include <cstdio>
#include <vector>
#include <algorithm>
#include <chrono>

using namespace cv;

static double nowMs() {
    return std::chrono::duration<double, std::milli>(
        std::chrono::steady_clock::now().time_since_epoch()).count();
}

int main() {
    // 1. Load test image
    Mat img = imread("digits_test.png", IMREAD_COLOR);
    if (img.empty()) {
        printf("ERROR: failed to load digits_test.png\n");
        return 1;
    }
    Mat gray;
    cvtColor(img, gray, COLOR_BGR2GRAY);
    printf("Loaded %dx%d\n", gray.cols, gray.rows);

    // 2. Auto-crop a tight template from the bottom-left cell (unrotated boxed '2')
    //    Layout: 3 rows x 6 cols. Bottom row y ~ [670, 1000], left cell x ~ [0, 220].
    Mat roi = gray(Rect(0, 670, 220, 330));
    Mat bin;
    threshold(roi, bin, 200, 255, THRESH_BINARY);
    std::vector<std::vector<Point>> contours;
    findContours(bin, contours, RETR_EXTERNAL, CHAIN_APPROX_SIMPLE);
    if (contours.empty()) {
        printf("ERROR: no contour found in bottom-left cell\n");
        return 1;
    }
    size_t best = 0;
    double bestA = contourArea(contours[0]);
    for (size_t i = 1; i < contours.size(); ++i) {
        double a = contourArea(contours[i]);
        if (a > bestA) { bestA = a; best = i; }
    }
    Rect r = boundingRect(contours[best]);
    const int margin = 4;
    int x1 = std::max(0, r.x - margin);
    int y1 = std::max(0, r.y - margin);
    int x2 = std::min(roi.cols, r.x + r.width + margin);
    int y2 = std::min(roi.rows, r.y + r.height + margin);
    Mat tpl = roi(Rect(x1, y1, x2 - x1, y2 - y1)).clone();
    printf("Template auto-cropped: %dx%d at (%d,%d) area=%.0f\n",
           tpl.cols, tpl.rows, x1, y1 + 670, bestA);

    // 3. Run matcher
    void* h = gm_create();
    gm_set_source(h, gray.data, gray.cols, gray.rows, (int)gray.step, 1);
    gm_set_template(h, tpl.data, tpl.cols, tpl.rows, (int)tpl.step, 1);

    const double overlap = 0.25;
    const int topN = 50;
    GmMatchResult out[128];

    // Test several thresholds to find the clean operating point for this sparse digit image.
    for (double thr : { 0.30, 0.40, 0.50, 0.60 }) {
        for (int p : { 0, 4 }) {
            printf("\n=== pyramid=%d, angle [-5..65] step 1, thr=%.2f ===\n", p, thr);
            double t0 = nowMs();
            int n = gm_match(h, p, -5.0, 65.0, 1.0, thr, overlap, topN, out, 128);
            double dt = gm_get_last_match_ms(h);
            printf("Detected %d in %.2f ms (wall %.2f ms)\n", n, dt, nowMs() - t0);

            std::vector<GmMatchResult> v(out, out + n);
            std::sort(v.begin(), v.end(), [](const GmMatchResult& a, const GmMatchResult& b) {
                return a.centerX < b.centerX;
            });
            for (int i = 0; i < n; ++i) {
                const char* mark = (v[i].score > 0.90) ? " *" : "";
                printf("  #%02d x=%6.1f y=%6.1f angle=%7.2f score=%.3f size=%dx%d%s\n",
                       i + 1, v[i].centerX, v[i].centerY, v[i].angle,
                       v[i].score, v[i].templateWidth, v[i].templateHeight, mark);
            }
        }
    }

    gm_destroy(h);
    return 0;
}
