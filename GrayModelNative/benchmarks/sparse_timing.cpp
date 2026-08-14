#include "gray_model_native.h"
#include <opencv2/opencv.hpp>
#include <cstdio>
#include <vector>
#include <chrono>
#include <algorithm>
#include <cmath>
using namespace cv;
int main(int argc, char** argv) {
    int nTargets = (argc > 1) ? atoi(argv[1]) : 12;
    int W = 640, H = 420, t = 30;
    Mat gray(H, W, CV_8UC1, Scalar(0));            // dark background (like a PCB)
    Mat disk(t, t, CV_8UC1, Scalar(0));
    circle(disk, Point(t / 2, t / 2), t / 2 - 2, Scalar(255), -1);   // bright ball template
    std::vector<Point> pos;
    int cols = (int)ceil(std::sqrt((double)nTargets));
    int rows = (nTargets + cols - 1) / cols;
    int gapx = (W - 40) / cols, gapy = (H - 40) / rows;
    int k = 0;
    for (int r = 0; r < rows && k < nTargets; ++r)
        for (int c = 0; c < cols && k < nTargets; ++c) {
            int x = 20 + c * gapx, y = 20 + r * gapy;
            disk.copyTo(gray(Rect(x, y, t, t)));
            pos.push_back(Point(x, y));
            ++k;
        }
    Mat tpl = gray(Rect(pos[0].x, pos[0].y, t, t)).clone();
    printf("sparse image %dx%d  targets=%d  template %dx%d\n", W, H, nTargets, t, t);
    void* h = gm_create();
    int topN = 999;
    for (int dense : {0, 1}) {
        gm_set_source(h, gray.data, gray.cols, gray.rows, (int)gray.step, 1);
        gm_set_template(h, tpl.data, tpl.cols, tpl.rows, (int)tpl.step, 1);
        std::vector<double> times; int lastN = 0;
        for (int rep = 0; rep < 20; ++rep) {
            GmMatchResult out[4096];
            int n = gm_match(h, 4, -45, 45, 1, 0.35, 0.5, topN, dense, out, 4096);
            if (rep >= 5) { times.push_back(gm_get_last_match_ms(h)); lastN = n; }
        }
        std::sort(times.begin(), times.end());
        printf("dense=%d  count=%d  median=%.2f ms  min=%.2f max=%.2f\n", dense, lastN, times[times.size() / 2], times.front(), times.back());
    }
    gm_destroy(h);
    return 0;
}
