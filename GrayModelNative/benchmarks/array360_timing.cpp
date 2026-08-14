// Reproduces the WPF defaults that the user is running and times them on a
// regular array (the "密集阵列/规则阵列" use case). Goal: prove whether the
// several-second latency comes from dense mode being ON by default.
#include "gray_model_native.h"
#include <opencv2/opencv.hpp>
#include <cstdio>
#include <vector>
#include <chrono>
#include <algorithm>
#include <cmath>
using namespace cv;

static double median(std::vector<double>& v) {
    if (v.empty()) return 0;
    std::sort(v.begin(), v.end());
    return v[v.size() / 2];
}

int main() {
    // ---- build a regular array of identical bright disks on dark bg ----
    int cols = 25, rows = 15;          // 375 identical targets (a BGA-like array)
    int t = 30;
    int W = 1600, H = 1200;
    Mat gray(H, W, CV_8UC1, Scalar(0));
    Mat disk(t, t, CV_8UC1, Scalar(0));
    circle(disk, Point(t / 2, t / 2), t / 2 - 2, Scalar(255), -1);
    int gapx = (W - 40) / cols, gapy = (H - 40) / rows;
    for (int r = 0; r < rows; ++r)
        for (int c = 0; c < cols; ++c) {
            int x = 20 + c * gapx, y = 20 + r * gapy;
            disk.copyTo(gray(Rect(x, y, t, t)));
        }
    // template = one of the disks
    Mat tpl = gray(Rect(20, 20, t, t)).clone();

    // ---- WPF defaults as shipped ----
    double aS = -180, aE = 180, step = 1.0, thr = 0.9, ov = 0.1;
    int pyramid = 4;
    printf("image %dx%d  template %dx%d  array=%d targets\n", W, H, tpl.cols, tpl.rows, cols * rows);
    printf("params: angles[%.0f,%.0f]/%.0f  thr=%.2f  overlap=%.2f  pyramid=%d\n", aS, aE, step, thr, ov, pyramid);

    void* h = gm_create();
    printf("gm_create handle=%p\n", h);
    for (int topN : { 640, 64 }) {
        for (int dense : { 1, 0 }) {
            int s = gm_set_source(h, gray.data, gray.cols, gray.rows, (int)gray.step, 1);
            int tt = gm_set_template(h, tpl.data, tpl.cols, tpl.rows, (int)tpl.step, 1);
            if (s != 0 || tt != 0) printf("  WARN set_source=%d set_template=%d\n", s, tt);
            std::vector<double> times; int lastN = 0;
            for (int rep = 0; rep < 12; ++rep) {
                GmMatchResult out[4096];
                int n = gm_match(h, pyramid, aS, aE, step, thr, ov, topN, dense, out, 4096);
                if (rep >= 4) { times.push_back(gm_get_last_match_ms(h)); lastN = n; }
            }
            printf("  topN=%-4d dense=%-2d  -> found %-4d  median=%.2f ms  min=%.2f  max=%.2f\n",
                   topN, dense, lastN, median(times), times.front(), times.back());
        }
    }
    gm_destroy(h);
    return 0;
}
