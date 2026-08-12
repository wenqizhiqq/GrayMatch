#pragma once
#include <cstdint>
#include <complex>
#include <vector>

// ---------------------------------------------------------------------------
// fastcpp : a self-contained, OpenCV-free rotation-invariant NCC template
// matcher implemented in pure C++.
//
//   * Hand-written SSE2   -> integral-image column accumulation + FFT
//                            complex-multiply (the correlation kernel).
//   * Integral images     -> O(1) per-window mean / variance for NCC
//                            normalisation (no per-window recompute).
//   * Frequency domain    -> cross-correlation via FFT: for every rotation
//                            angle we FFT-correlate the (rotated) template
//                            with the (once-FFT'd) scene, then finish NCC
//                            with the integral-image normalisation.
//
// The result is mathematically identical to OpenCV's TM_CCOEFF_NORMED at
// every position, but with zero OpenCV dependency.
// ---------------------------------------------------------------------------

namespace fastcpp {

struct FcMatchResult {
    double score = 0;          // NCC in [-1, 1]
    double centerX = 0;        // matched centre (image pixels)
    double centerY = 0;
    double angle = 0;          // rotation applied to the template (degrees)
    int templateWidth = 0;
    int templateHeight = 0;
};

class FastMatcher {
public:
    FastMatcher() = default;

    // Grayscale 8-bit source / template. step = row stride in bytes.
    bool setSource(const unsigned char* data, int w, int h, int step);
    bool setTemplate(const unsigned char* data, int w, int h, int step);

    // Rotation-invariant NCC sweep. Returns accepted matches (already NMS'd).
    std::vector<FcMatchResult> match(double angleStart, double angleEnd,
                                     double angleStep, double nccThreshold,
                                     double maxOverlap, int topN);

    double lastMatchMs() const { return lastMatchMs_; }

private:
    int W_ = 0, H_ = 0;        // source size
    int tw_ = 0, th_ = 0;      // template size
    int P_ = 0, Q_ = 0;        // FFT padded size (P rows, Q cols)

    std::vector<float> srcF_;                  // grayscale source
    std::vector<float> tmplF_;                 // grayscale template
    std::vector<float> II_, II2_;              // integral images of source (sum, sum²)
    std::vector<std::complex<float>> specI_;   // FFT of the (padded) scene, computed once

    double lastMatchMs_ = 0.0;
};

} // namespace fastcpp
