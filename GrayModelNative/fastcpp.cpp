// ---------------------------------------------------------------------------
// fastcpp.cpp  —  pure-C++ rotation-invariant NCC matcher (no OpenCV)
//   SSE2 integral images + SSE2 FFT complex-multiply + hand-written FFT.
// ---------------------------------------------------------------------------
#include "fastcpp.h"

#include <cmath>
#include <complex>
#include <chrono>
#include <algorithm>
#include <vector>

#ifdef _MSC_VER
#include <emmintrin.h>   // SSE2
#endif

namespace fastcpp {
namespace {

const float PI = 3.14159265358979323846f;

inline int nextPow2(int n) { int p = 1; while (p < n) p <<= 1; return p; }

// ---- iterative radix-2 Cooley-Tukey 1D FFT (in-place) --------------------
void fft1d(std::complex<float>* a, int n, bool inverse) {
    // bit-reversal permutation
    for (int i = 1, j = 0; i < n; ++i) {
        int bit = n >> 1;
        for (; j & bit; bit >>= 1) j ^= bit;
        j ^= bit;
        if (i < j) std::swap(a[i], a[j]);
    }
    for (int len = 2; len <= n; len <<= 1) {
        double ang = (inverse ? 2.0 : -2.0) * PI / len;
        std::complex<float> wlen((float)std::cos(ang), (float)std::sin(ang));
        for (int i = 0; i < n; i += len) {
            std::complex<float> w(1, 0);
            for (int k = 0; k < len / 2; ++k) {
                std::complex<float> u = a[i + k];
                std::complex<float> v = a[i + k + len / 2] * w;
                a[i + k] = u + v;
                a[i + k + len / 2] = u - v;
                w *= wlen;
            }
        }
    }
    if (inverse) {
        float inv = 1.0f / n;
        for (int i = 0; i < n; ++i) a[i] *= inv;
    }
}

// 2D FFT: rows then columns (each 1D transform is normalised on the inverse).
void fft2d(std::vector<std::complex<float>>& a, int P, int Q, bool inverse) {
    std::vector<std::complex<float>> row(Q);
    for (int r = 0; r < P; ++r) {
        for (int c = 0; c < Q; ++c) row[c] = a[(size_t)r * Q + c];
        fft1d(row.data(), Q, inverse);
        for (int c = 0; c < Q; ++c) a[(size_t)r * Q + c] = row[c];
    }
    std::vector<std::complex<float>> col(P);
    for (int c = 0; c < Q; ++c) {
        for (int r = 0; r < P; ++r) col[r] = a[(size_t)r * Q + c];
        fft1d(col.data(), P, inverse);
        for (int r = 0; r < P; ++r) a[(size_t)r * Q + c] = col[r];
    }
}

// ---- SSE2 integral-image column pass -------------------------------------
// II / II2 are (W+1) x (H+1); the first row/col are zero.  The row prefix
// sums are done sequentially; the vertical accumulation (each row += prev
// row) is a plain vector add and is vectorised with SSE2.
void buildIntegral(const std::vector<float>& img, int w, int h,
                   std::vector<float>& II, std::vector<float>& II2) {
    const int SW = w + 1, SH = h + 1;
    II.assign((size_t)SW * SH, 0.f);
    II2.assign((size_t)SW * SH, 0.f);
    for (int y = 0; y < h; ++y) {
        float rs = 0.f, rs2 = 0.f;
        for (int x = 0; x < w; ++x) {
            float v = img[(size_t)y * w + x];
            rs += v; rs2 += v * v;
            II[(size_t)(y + 1) * SW + (x + 1)]  = rs;   // pure row prefix
            II2[(size_t)(y + 1) * SW + (x + 1)] = rs2;
        }
    }
#if defined(_MSC_VER) && (defined(_M_X64) || _M_IX86_FP >= 2)
    for (int y = 1; y < SH; ++y) {
        float* cur = &II[(size_t)y * SW];
        const float* prev = &II[(size_t)(y - 1) * SW];
        int x = 0;
        for (; x + 4 <= SW; x += 4)
            _mm_storeu_ps(cur + x, _mm_add_ps(_mm_loadu_ps(cur + x), _mm_loadu_ps(prev + x)));
        for (; x < SW; ++x) cur[x] += prev[x];
    }
    for (int y = 1; y < SH; ++y) {
        float* cur = &II2[(size_t)y * SW];
        const float* prev = &II2[(size_t)(y - 1) * SW];
        int x = 0;
        for (; x + 4 <= SW; x += 4)
            _mm_storeu_ps(cur + x, _mm_add_ps(_mm_loadu_ps(cur + x), _mm_loadu_ps(prev + x)));
        for (; x < SW; ++x) cur[x] += prev[x];
    }
#else
    for (int y = 1; y < SH; ++y)
        for (int x = 0; x < SW; ++x) {
            II[(size_t)y * SW + x]  += II[(size_t)(y - 1) * SW + x];
            II2[(size_t)y * SW + x] += II2[(size_t)(y - 1) * SW + x];
        }
#endif
}

// ---- SSE2 complex multiply: specC = specI * conj(specT) -------------------
// Processes two complex numbers per iteration (std::complex<float> is {re,im}).
void correlateSpectrum(const std::vector<std::complex<float>>& specI,
                       const std::vector<std::complex<float>>& specT,
                       std::vector<std::complex<float>>& specC) {
    const size_t n = specI.size();
    specC.resize(n);
#if 1 // SSE2 complex-multiply (validated against scalar reference)
    size_t k = 0;
    for (; k + 1 < n; k += 2) {
        __m128 a = _mm_loadu_ps((const float*)&specI[k]);   // aRe0 aIm0 aRe1 aIm1
        __m128 b = _mm_loadu_ps((const float*)&specT[k]);   // bRe0 bIm0 bRe1 bIm1
        __m128 aRe = _mm_shuffle_ps(a, a, _MM_SHUFFLE(2, 0, 2, 0)); // aRe0 aRe1 aRe0 aRe1
        __m128 aIm = _mm_shuffle_ps(a, a, _MM_SHUFFLE(3, 1, 3, 1)); // aIm0 aIm1 aIm0 aIm1
        __m128 bRe = _mm_shuffle_ps(b, b, _MM_SHUFFLE(2, 0, 2, 0));
        __m128 bIm = _mm_shuffle_ps(b, b, _MM_SHUFFLE(3, 1, 3, 1));
        __m128 re = _mm_add_ps(_mm_mul_ps(aRe, bRe), _mm_mul_ps(aIm, bIm));
        __m128 im = _mm_sub_ps(_mm_mul_ps(aIm, bRe), _mm_mul_ps(aRe, bIm));
        __m128 res = _mm_unpacklo_ps(re, im);               // re0 im0 re1 im1
        _mm_storeu_ps((float*)&specC[k], res);
    }
    for (; k < n; ++k) {
        std::complex<float> a = specI[k], b = specT[k];
        specC[k] = a * std::conj(b);
    }
#else
    for (size_t k = 0; k < n; ++k) {
        std::complex<float> a = specI[k], b = specT[k];
        specC[k] = a * std::conj(b);
    }
#endif
}

// ---- rotate a tw x th template by `deg` (bilinear, replicate border) ------
void rotateTemplate(const std::vector<float>& src, int w, int h,
                    std::vector<float>& dst, double deg) {
    dst.assign((size_t)w * h, 0.f);
    double th = deg * PI / 180.0;
    double c = std::cos(th), s = std::sin(th);
    double cx = (w - 1) / 2.0, cy = (h - 1) / 2.0;
    auto clmp = [](int v, int n) { return v < 0 ? 0 : (v >= n ? n - 1 : v); };
    for (int y = 0; y < h; ++y) {
        for (int x = 0; x < w; ++x) {
            double dx = x - cx, dy = y - cy;
            double sx = dx * c + dy * s + cx;     // rotate template by +ang (CCW),
            double sy = -dx * s + dy * c + cy;    // matching OpenCV getRotationMatrix2D convention
            int x0 = (int)std::floor(sx), y0 = (int)std::floor(sy);
            double fx = sx - x0, fy = sy - y0;
            int xa = clmp(x0, w), xb = clmp(x0 + 1, w);
            int ya = clmp(y0, h), yb = clmp(y0 + 1, h);
            float v00 = src[(size_t)ya * w + xa], v01 = src[(size_t)ya * w + xb];
            float v10 = src[(size_t)yb * w + xa], v11 = src[(size_t)yb * w + xb];
            float top = v00 + (v01 - v00) * (float)fx;
            float bot = v10 + (v11 - v10) * (float)fx;
            dst[(size_t)y * w + x] = top + (bot - top) * (float)fy;
        }
    }
}

double boxOverlap(const FcMatchResult& a, const FcMatchResult& b) {
    double aw = a.templateWidth, ah = a.templateHeight;
    double bw = b.templateWidth, bh = b.templateHeight;
    double ax1 = a.centerX - aw / 2, ay1 = a.centerY - ah / 2;
    double bx1 = b.centerX - bw / 2, by1 = b.centerY - bh / 2;
    double ix1 = std::max(ax1, bx1), iy1 = std::max(ay1, by1);
    double ix2 = std::min(ax1 + aw, bx1 + bw), iy2 = std::min(ay1 + ah, by1 + bh);
    double iw = ix2 - ix1, ih = iy2 - iy1;
    if (iw <= 0 || ih <= 0) return 0;
    double inter = iw * ih, uni = aw * ah + bw * bh - inter;
    return uni > 0 ? inter / uni : 0;
}

auto now() { return std::chrono::steady_clock::now(); }
double ms(std::chrono::steady_clock::time_point a,
          std::chrono::steady_clock::time_point b) {
    return std::chrono::duration<double, std::milli>(b - a).count();
}

} // anonymous namespace

// ===========================================================================
bool FastMatcher::setSource(const unsigned char* data, int w, int h, int step) {
    W_ = w; H_ = h;
    srcF_.assign((size_t)w * h, 0.f);
    for (int y = 0; y < h; ++y) {
        const unsigned char* row = data + (size_t)y * step;
        for (int x = 0; x < w; ++x) srcF_[(size_t)y * w + x] = row[x];
    }
    buildIntegral(srcF_, W_, H_, II_, II2_);
    return true;
}

bool FastMatcher::setTemplate(const unsigned char* data, int w, int h, int step) {
    tw_ = w; th_ = h;
    tmplF_.assign((size_t)w * h, 0.f);
    for (int y = 0; y < h; ++y) {
        const unsigned char* row = data + (size_t)y * step;
        for (int x = 0; x < w; ++x) tmplF_[(size_t)y * w + x] = row[x];
    }
    // FFT size depends on the (fixed) template size, so it is known now.
    P_ = nextPow2(W_ + tw_ - 1);
    Q_ = nextPow2(H_ + th_ - 1);
    std::vector<std::complex<float>> specI((size_t)P_ * Q_, 0);
    for (int y = 0; y < H_; ++y)
        for (int x = 0; x < W_; ++x)
            specI[(size_t)y * Q_ + x] = std::complex<float>(srcF_[(size_t)y * W_ + x], 0);
    fft2d(specI, P_, Q_, false);
    specI_.swap(specI);
    return true;
}

std::vector<FcMatchResult> FastMatcher::match(double aStart, double aEnd,
                                              double aStep, double thr,
                                              double maxOverlap, int topN) {
    auto t0 = now();
    std::vector<FcMatchResult> all;
    if (specI_.empty() || tmplF_.empty()) return all;
    if (aStep <= 0) aStep = 1.0;

    const int N = tw_ * th_;
    const int SW = W_ + 1;
    const int nSteps = (int)std::floor((aEnd - aStart) / aStep + 1e-9) + 1;

    // Each angle rotates+FFTs the (centred) template, correlates with the
    // precomputed scene spectrum, then NCC-normalises via the integral images.
    // Angles are independent, so they run in parallel (OpenMP).
    #pragma omp parallel
    {
        std::vector<FcMatchResult> localAll;
        #pragma omp for schedule(dynamic, 1)
        for (int ai = 0; ai < nSteps; ++ai) {
            double ang = aStart + ai * aStep;

            std::vector<float> rot;
            rotateTemplate(tmplF_, tw_, th_, rot, ang);

            // centre the template (zero mean) — matches TM_CCOEFF_NORMED.
            double meanT = 0;
            for (float v : rot) meanT += v;
            meanT /= N;
            std::vector<float> Tc(rot);
            for (float& v : Tc) v -= (float)meanT;
            double varT = 0;
            for (float v : Tc) varT += v * v;
            double sqrtVarT = std::sqrt(varT);
            if (sqrtVarT < 1e-6) continue;

            // FFT of the centred template, padded to P x Q at the origin.
            std::vector<std::complex<float>> specT((size_t)P_ * Q_, 0);
            for (int y = 0; y < th_; ++y)
                for (int x = 0; x < tw_; ++x)
                    specT[(size_t)y * Q_ + x] = std::complex<float>(Tc[(size_t)y * tw_ + x], 0);
            fft2d(specT, P_, Q_, false);

            // correlation = IFFT( specI * conj(specT) )
            std::vector<std::complex<float>> specC;
            correlateSpectrum(specI_, specT, specC);
            fft2d(specC, P_, Q_, true);

            // NCC over the valid search region (O(1) per window via integral images).
            std::vector<FcMatchResult> cand;
            for (int y = 0; y + th_ <= H_; ++y) {
                for (int x = 0; x + tw_ <= W_; ++x) {
                    double sumI  = II_[(size_t)(y + th_) * SW + (x + tw_)]
                                 - II_[(size_t)y * SW + (x + tw_)]
                                 - II_[(size_t)(y + th_) * SW + x]
                                 + II_[(size_t)y * SW + x];
                    double sumI2 = II2_[(size_t)(y + th_) * SW + (x + tw_)]
                                 - II2_[(size_t)y * SW + (x + tw_)]
                                 - II2_[(size_t)(y + th_) * SW + x]
                                 + II2_[(size_t)y * SW + x];
                    double varI = sumI2 - sumI * sumI / N;
                    if (varI <= 1e-6) continue;
                    float num = specC[(size_t)y * Q_ + x].real();
                    double ncc = num / (std::sqrt(varI) * sqrtVarT);
                    if (ncc >= thr && ncc <= 1.0001) {
                        FcMatchResult r;
                        r.score = ncc;
                        r.centerX = x + tw_ / 2.0;
                        r.centerY = y + th_ / 2.0;
                        r.angle = ang;
                        r.templateWidth = tw_;
                        r.templateHeight = th_;
                        cand.push_back(r);
                    }
                }
            }
            // 3x3 local non-maximum suppression (keeps the strongest in each cluster)
            for (const auto& r : cand) {
                int cx = (int)std::round(r.centerX), cy = (int)std::round(r.centerY);
                bool keep = true;
                for (const auto& o : cand) {
                    if (&o == &r) continue;
                    int ox = (int)std::round(o.centerX), oy = (int)std::round(o.centerY);
                    if (std::abs(ox - cx) <= 2 && std::abs(oy - cy) <= 2 && o.score > r.score) {
                        keep = false; break;
                    }
                }
                if (keep) localAll.push_back(r);
            }
        }
        #pragma omp critical
        { all.insert(all.end(), localAll.begin(), localAll.end()); }
    }

    // global NMS by centre distance + angle, then TopN + overlap filter.
    std::sort(all.begin(), all.end(),
              [](const FcMatchResult& a, const FcMatchResult& b) { return a.score > b.score; });
    std::vector<FcMatchResult> out;
    double distThr = 0.5 * std::min(tw_, th_);
    for (const auto& r : all) {
        bool ok = true;
        for (const auto& o : out) {
            double dx = r.centerX - o.centerX, dy = r.centerY - o.centerY;
            double d = std::sqrt(dx * dx + dy * dy);
            double ad = std::abs(r.angle - o.angle);
            if (ad > 180) ad = 360 - ad;
            if (d < distThr && ad < 12 && boxOverlap(r, o) > maxOverlap) { ok = false; break; }
        }
        if (ok) out.push_back(r);
        if ((int)out.size() >= topN) break;
    }

    lastMatchMs_ = ms(t0, now());
    return out;
}

} // namespace fastcpp
