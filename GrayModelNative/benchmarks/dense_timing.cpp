#include "gray_model_native.h"
#include <opencv2/opencv.hpp>
#include <cstdio>
#include <vector>
#include <chrono>
#include <algorithm>
using namespace cv;
int main(int argc, char** argv) {
    const char* path = (argc > 1) ? argv[1] : "D:/wqz/code/GrayMatch/_oipc.jpg";
    double aS = (argc > 2) ? atof(argv[2]) : -180.0;
    double aE = (argc > 3) ? atof(argv[3]) : 180.0;
    Mat color = imread(path);
    if (color.empty()) { printf("load fail\n"); return 1; }
    Mat gray; cvtColor(color, gray, COLOR_BGR2GRAY);
    Mat tpl = gray(Rect(18, 18, 34, 34)).clone();
    printf("image %dx%d  template %dx%d  angles [%.0f,%.0f]/1\n", gray.cols, gray.rows, tpl.cols, tpl.rows, aS, aE);
    void* h = gm_create();
    int topN = 999;
    for (int dense : {0, 1}) {
        gm_set_source(h, gray.data, gray.cols, gray.rows, (int)gray.step, 1);
        gm_set_template(h, tpl.data, tpl.cols, tpl.rows, (int)tpl.step, 1);
        std::vector<double> times; int lastN = 0;
        for (int r = 0; r < 20; ++r) {
            GmMatchResult out[4096];
            int n = gm_match(h, 4, aS, aE, 1, 0.35, 0.5, topN, dense, out, 4096);
            if (r >= 5) { times.push_back(gm_get_last_match_ms(h)); lastN = n; }
        }
        std::sort(times.begin(), times.end());
        printf("dense=%d  count=%d  median=%.2f ms  min=%.2f max=%.2f\n", dense, lastN, times[times.size()/2], times.front(), times.back());
    }
    gm_destroy(h);
    return 0;
}
