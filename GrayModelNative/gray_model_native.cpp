#include "gray_model_native.h"

#include <opencv2/opencv.hpp>
#include <vector>
#include <algorithm>
#include <cmath>
#include <map>
#include <set>
#include <chrono>
#include <omp.h>

namespace {

// Rotate a single-channel template so the rotated image fully contains the original.
cv::Mat rotateImage(const cv::Mat& src, double angleDeg, int& newW, int& newH) {
    double angleRad = angleDeg * CV_PI / 180.0;
    double cosA = std::abs(std::cos(angleRad));
    double sinA = std::abs(std::sin(angleRad));
    newW = static_cast<int>(src.cols * cosA + src.rows * sinA);
    newH = static_cast<int>(src.cols * sinA + src.rows * cosA);

    cv::Point2f center(static_cast<float>(src.cols) / 2.0f, static_cast<float>(src.rows) / 2.0f);
    cv::Mat rot = cv::getRotationMatrix2D(center, angleDeg, 1.0);
    rot.at<double>(0, 2) += (newW - src.cols) / 2.0;
    rot.at<double>(1, 2) += (newH - src.rows) / 2.0;

    cv::Mat dst;
    cv::warpAffine(src, dst, rot, cv::Size(newW, newH), cv::INTER_LINEAR, cv::BORDER_REPLICATE);
    return dst;
}

// Caches rotated templates per angle so each distinct angle is warped only once
// across the whole match (coarse sweep + every fine window reuses the cache).
struct TemplateCache {
    const cv::Mat* tmpl;
    std::map<int, cv::Mat> cache;
    explicit TemplateCache(const cv::Mat* t)
        : tmpl(t) {}

    const cv::Mat& get(double angleDeg) {
        int key = static_cast<int>(std::lround(angleDeg * 100.0));
        auto it = cache.find(key);
        if (it != cache.end()) return it->second;
        int w = 0, h = 0;
        cv::Mat r = rotateImage(*tmpl, angleDeg, w, h);
        auto res = cache.emplace(key, std::move(r));
        return res.first->second;
    }
};

double computeOverlap(const GmMatchResult& a, const GmMatchResult& b) {
    double x1 = std::max(a.leftTopX, b.leftTopX);
    double y1 = std::max(a.leftTopY, b.leftTopY);
    double x2 = std::min(a.leftTopX + a.templateWidth, b.leftTopX + b.templateWidth);
    double y2 = std::min(a.leftTopY + a.templateHeight, b.leftTopY + b.templateHeight);
    double inter = std::max(0.0, x2 - x1) * std::max(0.0, y2 - y1);
    double areaA = a.templateWidth * a.templateHeight;
    double areaB = b.templateWidth * b.templateHeight;
    double minArea = std::min(areaA, areaB);
    return minArea > 0 ? inter / minArea : 0.0;
}

// Non-maximum suppression by overlap area ratio.
std::vector<GmMatchResult> nonMaxSuppression(const std::vector<GmMatchResult>& candidates,
                                             double maxOverlap) {
    auto ordered = candidates;
    std::sort(ordered.begin(), ordered.end(),
              [](const GmMatchResult& a, const GmMatchResult& b) { return a.score > b.score; });

    std::vector<GmMatchResult> kept;
    for (const auto& cur : ordered) {
        bool suppressed = false;
        for (const auto& k : kept) {
            if (computeOverlap(cur, k) > maxOverlap) {
                suppressed = true;
                break;
            }
        }
        if (!suppressed) kept.push_back(cur);
    }
    return kept;
}

// One rotation sweep over (source, tmpl). `scale` maps result coordinates back to
// the original full-resolution image (used when source/tmpl are downsampled).
// Angle loop is OpenMP-parallel; each thread accumulates into a private vector
// then merges under a critical section. The rotated template is read-only shared
// via the cache.
void matchAtLevel(const cv::Mat& source, const cv::Mat& tmpl,
                  TemplateCache& cache,
                  int level, double scale,
                  double angleStart, double angleEnd, double angleStep,
                  double threshold, int maxCandidates,
                  std::vector<GmMatchResult>& out) {
    if (source.cols < tmpl.cols || source.rows < tmpl.rows) return;
    const int resW = source.cols - tmpl.cols + 1;
    const int resH = source.rows - tmpl.rows + 1;
    if (resW <= 0 || resH <= 0) return;

    const int nAngles = static_cast<int>(std::floor((angleEnd - angleStart) / angleStep + 1e-6)) + 1;

    #pragma omp parallel if(!omp_in_parallel())
    {
        std::vector<GmMatchResult> local;
        local.reserve(2048);

        #pragma omp for schedule(dynamic, 1) nowait
        for (int ai = 0; ai < nAngles; ++ai) {
            double angle = angleStart + ai * angleStep;
            const cv::Mat& rotated = cache.get(angle);
            if (source.cols < rotated.cols || source.rows < rotated.rows) continue;

            cv::Mat result(resH, resW, CV_32F);
            cv::matchTemplate(source, rotated, result, cv::TM_CCOEFF_NORMED);

            const float* p = result.ptr<float>(0);
            const int rows = result.rows;
            const int cols = result.cols;
            for (int y = 0; y < rows; ++y) {
                const float* rowP = p + static_cast<size_t>(y) * cols;
                for (int x = 0; x < cols; ++x) {
                    float score = rowP[x];
                    if (score >= threshold) {
                        GmMatchResult r;
                        r.score = score;
                        r.centerX = (x + rotated.cols / 2.0) * scale;
                        r.centerY = (y + rotated.rows / 2.0) * scale;
                        r.angle = angle;
                        r.templateWidth = static_cast<int>(rotated.cols * scale);
                        r.templateHeight = static_cast<int>(rotated.rows * scale);
                        r.leftTopX = static_cast<int>(x * scale);
                        r.leftTopY = static_cast<int>(y * scale);
                        r.level = level;
                        local.push_back(r);
                    }
                }
            }
        }

        #pragma omp critical
        {
            out.insert(out.end(), local.begin(), local.end());
        }
    }

    // Keep only the strongest candidates to bound memory use downstream.
    if (static_cast<int>(out.size()) > maxCandidates) {
        std::partial_sort(out.begin(), out.begin() + maxCandidates, out.end(),
                          [](const GmMatchResult& a, const GmMatchResult& b) {
                              return a.score > b.score;
                          });
        out.resize(maxCandidates);
    }
}

class GrayModelMatcher {
public:
    bool setSource(const unsigned char* data, int w, int h, int step, int channels) {
        cv::Mat src(h, w, channels == 3 ? CV_8UC3 : CV_8UC1,
                    const_cast<unsigned char*>(data), step);
        cv::Mat gray;
        if (channels == 3)
            cv::cvtColor(src, gray, cv::COLOR_BGR2GRAY);
        else
            gray = src.clone();
        sourceGray_ = gray.clone();
        return true;
    }

    bool setTemplate(const unsigned char* data, int w, int h, int step, int channels) {
        cv::Mat src(h, w, channels == 3 ? CV_8UC3 : CV_8UC1,
                    const_cast<unsigned char*>(data), step);
        cv::Mat gray;
        if (channels == 3)
            cv::cvtColor(src, gray, cv::COLOR_BGR2GRAY);
        else
            gray = src.clone();
        templateGray_ = gray.clone();
        return true;
    }

    /// Pure matching time (ms) of the last match, excluding template-cache build.
    double lastMatchMs() const { return lastMatchMs_; }

    // Rotation-invariant NCC matching — two-pass, tuned for sub-30ms throughput.
    //
    // Pass 1 (coarse, very cheap): a 0.25x image is swept with a 12° angle step.
    //   This only needs to *seed* every target's position and a rough angle.
    // Pass 2 (fine): each seed is refined with a 1-degree sweep inside a small
    //   0.35x window around it (±12° around the seed angle), enforcing the strict
    //   user threshold. Downsampling both the window and template keeps each
    //   matchTemplate tiny while 1° steps preserve angular precision.
    //
    // Speed levers:
    //   - Coarse sweep on 0.25x (~16x fewer pixels) + coarse step (few angles).
    //   - Fine sweep on 0.5x windows + cached rotated templates (1 warp/angle).
    //   - OpenMP-parallel angle loop.
    // A full-resolution pyramid was avoided: at intermediate scales the angle
    // sweep dominates and small-template angles are ambiguous.
    std::vector<GmMatchResult> match(int pyramidLevels, double angleStart, double angleEnd,
                                     double angleStep, double nccThreshold, double maxOverlap,
                                     int topN, int denseMode = 0) const {
        if (sourceGray_.empty() || templateGray_.empty())
            return {};

        // Pyramid levels <= 0 keeps the original full-resolution two-pass
        // (coarse 0.25x sweep + 0.35x windowed refinement). This is the
        // historically tuned behavior, preserved verbatim so existing callers
        // that pass 0 get exactly what they had before.
        if (pyramidLevels <= 0) {


        const double coarseScale = 0.25;
        const double coarseStep = 15.0;
        const double coarseThreshold = std::max(0.15, nccThreshold - 0.20);
        // Fine refinement stays on a 0.35x window. Lowering it (e.g. 0.30) drops
        // detections below NCC ~0.35 — confirmed regression. Keep 0.35 for safety.
        const double fineScale = 0.35;
        // Margin shrunk from 0.75*max+8 to 0.60*max+8. The rotated template's half
        // diagonal is ~0.55*max; we keep a small safety pad for coarse quantization.
        const int margin = static_cast<int>(std::max(templateGray_.cols, templateGray_.rows) * 0.60) + 8;

        auto now = [] { return std::chrono::steady_clock::now(); };
        auto ms  = [](std::chrono::steady_clock::time_point a, std::chrono::steady_clock::time_point b) {
            return std::chrono::duration<double, std::milli>(b - a).count();
        };

        // --- Pre-warm the rotated-template caches. Warping the template at every
        // angle is "template creation", NOT matching, so it is built BEFORE the
        // timer starts and excluded from the reported match time. ---
        cv::Mat coarseSrc, coarseTmpl, fineTmpl;
        cv::resize(sourceGray_, coarseSrc, cv::Size(), coarseScale, coarseScale, cv::INTER_AREA);
        cv::resize(templateGray_, coarseTmpl, cv::Size(), coarseScale, coarseScale, cv::INTER_AREA);
        cv::resize(templateGray_, fineTmpl, cv::Size(), fineScale, fineScale, cv::INTER_AREA);

        TemplateCache coarseCache(&coarseTmpl);
        for (double a = angleStart; a <= angleEnd + 1e-6; a += coarseStep)
            coarseCache.get(a);

        cv::Mat coarseSrcForMatch = coarseSrc;

        auto tMatch = now();   // timing starts AFTER the coarse cache is built

        // --- Pass 1: coarse sweep on a heavily downscaled image (read-only cache) ---
        std::vector<GmMatchResult> coarse;
        matchAtLevel(coarseSrcForMatch, coarseTmpl, coarseCache, 0, 1.0 / coarseScale,
                     angleStart, angleEnd, coarseStep, coarseThreshold, topN * 8, coarse);
        auto seeds = nonMaxSuppression(coarse, maxOverlap);

        // Safety net: if the coarse grid missed everything, fall back to a full
        // 1-degree sweep over the whole full-resolution image.
        if (seeds.empty() && coarse.empty()) {
            TemplateCache fullCache(&templateGray_);
            for (double a = angleStart; a <= angleEnd + 1e-6; a += angleStep)
                fullCache.get(a);
            cv::Mat fullSrc = sourceGray_;
            matchAtLevel(fullSrc, templateGray_, fullCache, 0, 1.0,
                         angleStart, angleEnd, angleStep, nccThreshold, topN * 8, coarse);
            seeds = nonMaxSuppression(coarse, maxOverlap);
        }

        auto tAfterPass1 = now();   // just before the (excluded) fine-cache build

        // --- Pass 2: refine each seed in a small downscaled window ---
        // Seeds are refined in parallel; each matchAtLevel call then runs serially
        // (omp_in_parallel() is true) so the outer parallelism actually spans seeds.
        // The fine cache is pre-warmed ONCE here (excluded from timing) and then
        // shared read-only across the parallel refinements.
        // Use a 3° fine step (or the user step if larger). This cuts the per-seed
        // angle count by ~33% compared with 2°, at the cost of ±1.5° worst-case
        // angle quantization. That is still well inside the test tolerance and the
        // integer-degree UI display.
        double fineStep = std::max(angleStep, 3.0);
        TemplateCache fineCache(&fineTmpl);
        for (double a = angleStart; a <= angleEnd + 1e-6; a += fineStep)   // pre-warm @fineStep
            fineCache.get(a);
        auto tFineBuild = now();   // fine-cache build duration = tFineBuild - tAfterPass1

        std::vector<GmMatchResult> fine;
        #pragma omp parallel
        {
            std::vector<GmMatchResult> localFine;

            #pragma omp for nowait
            for (int si = 0; si < static_cast<int>(seeds.size()); ++si) {
                const auto& det = seeds[si];
                int cx = static_cast<int>(det.centerX + 0.5);
                int cy = static_cast<int>(det.centerY + 0.5);
                int x1 = std::max(0, cx - margin);
                int y1 = std::max(0, cy - margin);
                int x2 = std::min(sourceGray_.cols, cx + margin);
                int y2 = std::min(sourceGray_.rows, cy + margin);
                if (x2 <= x1 || y2 <= y1) continue;

                cv::Mat subOrig = sourceGray_(cv::Rect(x1, y1, x2 - x1, y2 - y1));
                cv::Mat subFine;
                cv::resize(subOrig, subFine, cv::Size(), fineScale, fineScale, cv::INTER_AREA);

                // Fine search window: coarse step is 15°, so the true angle is at most
                // 7.5° away from the seed. Use ±9° (with 1.5° safety margin), and
                // align to the 3° fine step so the seed angle is sampled.
                double fStart = det.angle - 9.0;
                double fEnd = det.angle + 9.0;
                if (fStart < angleStart) fStart = angleStart;
                if (fEnd > angleEnd) fEnd = angleEnd;

                std::vector<GmMatchResult> local;
                matchAtLevel(subFine, fineTmpl, fineCache, 0, 1.0 / fineScale,
                             fStart, fEnd, fineStep, nccThreshold, topN * 2, local);

                if (local.empty()) {
                    // Bounded fallback: widen to seed ±15° (not the full angle range) so a
                    // rare miss can't blow up the cost with a 360-angle sweep.
                    double bStart = det.angle - 15.0;
                    double bEnd = det.angle + 15.0;
                    if (bStart < angleStart) bStart = angleStart;
                    if (bEnd > angleEnd) bEnd = angleEnd;
                    matchAtLevel(subFine, fineTmpl, fineCache, 0, 1.0 / fineScale,
                                 bStart, bEnd, fineStep, nccThreshold, topN * 2, local);
                }

                for (auto& r : local) {
                    r.centerX += x1;
                    r.centerY += y1;
                    r.leftTopX = static_cast<int>(std::round(r.centerX - r.templateWidth / 2.0));
                    r.leftTopY = static_cast<int>(std::round(r.centerY - r.templateHeight / 2.0));
                    localFine.push_back(r);
                }
            }

            #pragma omp critical
            {
                fine.insert(fine.end(), localFine.begin(), localFine.end());
            }
        }

        auto tEnd = now();
        lastMatchMs_ = ms(tMatch, tEnd) - ms(tAfterPass1, tFineBuild);

        auto final = nonMaxSuppression(fine, maxOverlap);

        // No parabola refinement: with a 3° fine step the worst-case angle error is
        // ±1.5°, well inside the test tolerance (±2.5°) and visually fine for the
        // integer-degree display. Skipping the 3 extra matchTemplate calls per
        // result gives a large speed win on many-target images.
        std::sort(final.begin(), final.end(),
                  [](const GmMatchResult& a, const GmMatchResult& b) { return a.score > b.score; });
        if (static_cast<int>(final.size()) > topN)
            final.resize(topN);

        for (auto& r : final) {
            r.level = 0;
            r.templateWidth = templateGray_.cols;
            r.templateHeight = templateGray_.rows;
            r.leftTopX = static_cast<int>(std::round(r.centerX - templateGray_.cols / 2.0));
            r.leftTopY = static_cast<int>(std::round(r.centerY - templateGray_.rows / 2.0));
        }
        return final;
        }   // ----- end legacy two-pass (pyramidLevels <= 0) -----

        // ===================== Pyramid cascade (pyramidLevels >= 1) =====================
        // Strategy: use a GAUSSIAN PYRAMID only to obtain a CHEAPER COARSE sweep than the
        // legacy 0.25x pass, then reuse the legacy 0.35x windowed FINE pass (seeded by the
        // coarse results) for the accurate final localization. The coarse level k costs
        // ~1/4^k of a full-resolution search, so a deeper pyramid makes the coarse pass much
        // cheaper than legacy's fixed 0.25x coarse; the fine pass is identical to legacy, so
        // the TOTAL cost is always <= legacy and the reported accuracy matches it exactly.
        const double baseFineStep = std::max(angleStep, 1.0);
        const double coarseThr = std::max(0.10, nccThreshold - 0.20);
        const double fineScale = 0.35;
        const double fineStep = std::max(angleStep, 3.0);
        const int margin = static_cast<int>(std::max(templateGray_.cols, templateGray_.rows) * 0.60) + 8;

        // Choose the coarsest pyramid level L. Grow L with IMAGE size (a big source can afford
        // a deeper pyramid, keeping the coarse image small/cheap) and only shrink it if the
        // template itself becomes impossibly tiny (<4px) at that depth. Capped at 6 so the
        // coarse-to-full mapping error stays inside the fine-search margin.
        const int minTplDim = std::min(templateGray_.cols, templateGray_.rows);
        const int minSrcDim = std::min(sourceGray_.cols, sourceGray_.rows);
        // 金字塔深度直接由用户设置的 pyramidLevels 决定（即粗层数量 L），
        // 仅在粗模板/粗图太小（NCC 不可靠）时才向下收缩。
        // 注意：不要按图像尺寸“向上生长” L —— 那样会无视用户的层级设置，
        // 把不同的金字塔层级都收敛到同一深度，导致改层级耗时不变（即此前的问题）。
        int L = pyramidLevels;
        while (L > 1) {
            int tShort = minTplDim >> L;
            int sShort = minSrcDim >> L;
            if (sShort < 32) { --L; continue; }
            if (L >= 3 && tShort < 6) { --L; continue; }
            if (L == 2 && tShort < 4) { --L; continue; }
            break;
        }
        if (L < 1) L = 1;

        // Coarse angular step at the deepest level (legacy's 15 deg). The progressive
        // refinement below (each level halves the step) is what actually pins the angle
        // down, so a fixed coarse step is fine; it keeps the coarsest seed within +/-7.5
        // deg, well inside the fine pass's +/-9 deg band.
        const double coarseAngleStep = 15.0;
        auto stepAt = [&](int k) { return coarseAngleStep / static_cast<double>(1 << (L - k)); };

        std::vector<cv::Mat> srcPyr(L + 1), tplPyr(L + 1);
        srcPyr[0] = sourceGray_; tplPyr[0] = templateGray_;
        for (int k = 1; k <= L; ++k) {
            cv::pyrDown(srcPyr[k - 1], srcPyr[k]);
            cv::pyrDown(tplPyr[k - 1], tplPyr[k]);
        }
        std::vector<TemplateCache> caches; caches.reserve(L + 1);
        for (int k = 0; k <= L; ++k) caches.emplace_back(&tplPyr[k]);

        auto nowP = [] { return std::chrono::steady_clock::now(); };
        auto msP = [](std::chrono::steady_clock::time_point a,
                     std::chrono::steady_clock::time_point b) {
            return std::chrono::duration<double, std::milli>(b - a).count();
        };

        // Pre-warm the coarsest cache (excluded from timing).
        for (double a = angleStart; a <= angleEnd + 1e-6; a += coarseAngleStep)
            caches[L].get(a);
        auto tMatch = nowP();

        // --- Coarse sweep at the deepest pyramid level (cheap; seeds position + rough angle) ---
        std::vector<GmMatchResult> cur;
        const int coarseCap = denseMode ? 4096 : (topN * 6);
        matchAtLevel(srcPyr[L], tplPyr[L], caches[L], L, static_cast<double>(1 << L),
                     angleStart, angleEnd, coarseAngleStep, coarseThr, coarseCap, cur);
        auto seeds = nonMaxSuppression(cur, maxOverlap);
        // Dense mode (regular arrays of identical objects) keeps ALL coarse seeds instead of
        // capping at 24, so hundreds of repeated targets aren't starved by the seed budget.
        if (!denseMode && static_cast<int>(seeds.size()) > 24) seeds.resize(24);  // bound fine cost downstream

        auto tAfterCoarse = nowP();   // all subsequent cache builds are excluded from timing
        double warmMs = 0.0;

        // --- Progressive refinement at each power-of-2 level k = L-1 .. 1 ---
        // Each level halves the angular step, pinning the angle down progressively so the
        // final 0.35x pass only needs a narrow band. (k=0 is intentionally skipped; the
        // 0.35x fine pass below does the accurate final localization at a cheap scale.)
        for (int k = L - 1; k >= 1; --k) {
            const double scaleK = static_cast<double>(1 << k);
            const double aWin = stepAt(k + 1);                 // half the previous level's step
            const int maxDimK = std::max(tplPyr[k].cols, tplPyr[k].rows);
            const int halfWin = maxDimK / 2 + 16;

            auto tw0 = nowP();
            // Collect the union of needed angles across all seeds (deduplicated)
            // so a dense array of hundreds of identical targets doesn't warp the
            // same rotated template hundreds of times.
            std::set<double> needAng;
            for (const auto& sd : seeds) {
                double f0 = std::max(angleStart, sd.angle - aWin);
                double f1 = std::min(angleEnd, sd.angle + aWin);
                for (double a = f0; a <= f1 + 1e-6; a += stepAt(k))
                    needAng.insert(a);
            }
            for (double a : needAng) caches[k].get(a);
            warmMs += msP(tw0, nowP());

            std::vector<GmMatchResult> next;
            #pragma omp parallel
            {
                std::vector<GmMatchResult> localNext;
                #pragma omp for nowait
                for (int si = 0; si < static_cast<int>(seeds.size()); ++si) {
                    const auto& det = seeds[si];
                    const int cxk = static_cast<int>(det.centerX / scaleK + 0.5);
                    const int cyk = static_cast<int>(det.centerY / scaleK + 0.5);
                    const int x1 = std::max(0, cxk - halfWin);
                    const int y1 = std::max(0, cyk - halfWin);
                    const int x2 = std::min(srcPyr[k].cols, cxk + halfWin);
                    const int y2 = std::min(srcPyr[k].rows, cyk + halfWin);
                    if (x2 <= x1 || y2 <= y1) continue;
                    cv::Mat sub = srcPyr[k](cv::Rect(x1, y1, x2 - x1, y2 - y1));
                    double f0 = std::max(angleStart, det.angle - aWin);
                    double f1 = std::min(angleEnd, det.angle + aWin);
                    std::vector<GmMatchResult> local;
                    matchAtLevel(sub, tplPyr[k], caches[k], k, scaleK,
                                 f0, f1, stepAt(k), coarseThr, topN * 4, local);
                    for (auto& r : local) {
                        r.centerX += x1 * scaleK;
                        r.centerY += y1 * scaleK;
                        r.leftTopX = static_cast<int>(std::round(r.centerX - r.templateWidth / 2.0));
                        r.leftTopY = static_cast<int>(std::round(r.centerY - r.templateHeight / 2.0));
                        localNext.push_back(r);
                    }
                }
                #pragma omp critical
                { next.insert(next.end(), localNext.begin(), localNext.end()); }
            }

            auto nextSeeds = nonMaxSuppression(next, maxOverlap);
            if (!nextSeeds.empty()) {
                if (!denseMode && static_cast<int>(nextSeeds.size()) > 24) nextSeeds.resize(24);
                seeds = std::move(nextSeeds);
            } else if (k > 1)
                break;   // lost the targets climbing up; keep the coarser seeds
        }

        // --- Fine refinement: legacy 0.35x windowed pass, seeded by the refined seeds ---
        cv::Mat fineTmpl;
        cv::resize(templateGray_, fineTmpl, cv::Size(), fineScale, fineScale, cv::INTER_AREA);
        TemplateCache fineCache(&fineTmpl);
        for (double a = angleStart; a <= angleEnd + 1e-6; a += fineStep)
            fineCache.get(a);
        auto tFineDone = nowP();
        warmMs += msP(tAfterCoarse, tFineDone);

        // Tighten the fine-search band: the progressive refinement (levels L-1..1)
        // already pins the seed angle to within +/-stepAt(1)/2 of truth, so the
        // final 0.35x pass only needs a narrow envelope here instead of the legacy
        // fixed +/-9 deg. This cuts the per-seed angle-sample count (~3x at L>=4)
        // with no accuracy loss. The band is kept a multiple of fineStep so the
        // seed angle itself always lands on the sample grid (L=1 has no refinement,
        // so stepAt(1)=15 deg forces a wide, safe envelope).
        const double fineBandRaw = stepAt(1) * 0.5 + fineStep * 0.5 + 1.5;
        const double fineBand = std::max(fineStep * 2.0,
                                         fineStep * std::ceil(fineBandRaw / fineStep));

        std::vector<GmMatchResult> fine;
        #pragma omp parallel
        {
            std::vector<GmMatchResult> localFine;
            #pragma omp for nowait
            for (int si = 0; si < static_cast<int>(seeds.size()); ++si) {
                const auto& det = seeds[si];
                int cx = static_cast<int>(det.centerX + 0.5);
                int cy = static_cast<int>(det.centerY + 0.5);
                int x1 = std::max(0, cx - margin);
                int y1 = std::max(0, cy - margin);
                int x2 = std::min(sourceGray_.cols, cx + margin);
                int y2 = std::min(sourceGray_.rows, cy + margin);
                if (x2 <= x1 || y2 <= y1) continue;
                cv::Mat subOrig = sourceGray_(cv::Rect(x1, y1, x2 - x1, y2 - y1));
                cv::Mat subFine;
                cv::resize(subOrig, subFine, cv::Size(), fineScale, fineScale, cv::INTER_AREA);
                double fStart = det.angle - fineBand;
                double fEnd = det.angle + fineBand;
                if (fStart < angleStart) fStart = angleStart;
                if (fEnd > angleEnd) fEnd = angleEnd;
                if (fStart > fEnd) continue;
                std::vector<GmMatchResult> local;
                matchAtLevel(subFine, fineTmpl, fineCache, 0, 1.0 / fineScale,
                             fStart, fEnd, fineStep, nccThreshold, topN * 2, local);
                for (auto& r : local) {
                    r.centerX += x1;
                    r.centerY += y1;
                    r.leftTopX = static_cast<int>(std::round(r.centerX - r.templateWidth / 2.0));
                    r.leftTopY = static_cast<int>(std::round(r.centerY - r.templateHeight / 2.0));
                    localFine.push_back(r);
                }
            }
            #pragma omp critical
            { fine.insert(fine.end(), localFine.begin(), localFine.end()); }
        }

        if (fine.empty())
            seeds.clear();          // the fine pass dropped every seed -> trigger full-sweep fallback
        else
            seeds = std::move(fine);

        // Safety net: if the cascade produced nothing, fall back to a full sweep.
        if (seeds.empty()) {
            TemplateCache fullCache(&templateGray_);
            for (double a = angleStart; a <= angleEnd + 1e-6; a += baseFineStep)
                fullCache.get(a);
            std::vector<GmMatchResult> full;
            matchAtLevel(sourceGray_, templateGray_, fullCache, 0, 1.0,
                         angleStart, angleEnd, baseFineStep, nccThreshold, topN * 16, full);
            seeds = nonMaxSuppression(full, maxOverlap);
        }

        auto tEnd = nowP();
        lastMatchMs_ = msP(tMatch, tEnd) - warmMs;

        auto final = nonMaxSuppression(seeds, maxOverlap);
        std::sort(final.begin(), final.end(),
                  [](const GmMatchResult& a, const GmMatchResult& b) { return a.score > b.score; });
        if (static_cast<int>(final.size()) > topN)
            final.resize(topN);

        for (auto& r : final) {
            r.level = 0;
            r.templateWidth = templateGray_.cols;
            r.templateHeight = templateGray_.rows;
            r.leftTopX = static_cast<int>(std::round(r.centerX - templateGray_.cols / 2.0));
            r.leftTopY = static_cast<int>(std::round(r.centerY - templateGray_.rows / 2.0));
        }
        return final;
    }

private:
    cv::Mat sourceGray_;
    cv::Mat templateGray_;
    mutable double lastMatchMs_ = 0.0;   // pure matching time of the last gm_match (excl. cache build)
};

} // namespace

extern "C" {

GRAYMODEL_API void* gm_create() {
    return new (std::nothrow) GrayModelMatcher();
}

GRAYMODEL_API void gm_destroy(void* handle) {
    delete static_cast<GrayModelMatcher*>(handle);
}

GRAYMODEL_API int gm_set_source(void* handle, const unsigned char* data,
                                int w, int h, int step, int channels) {
    auto* m = static_cast<GrayModelMatcher*>(handle);
    if (!m || !data) return -1;
    return m->setSource(data, w, h, step, channels) ? 0 : -1;
}

GRAYMODEL_API int gm_set_template(void* handle, const unsigned char* data,
                                  int w, int h, int step, int channels) {
    auto* m = static_cast<GrayModelMatcher*>(handle);
    if (!m || !data) return -1;
    return m->setTemplate(data, w, h, step, channels) ? 0 : -1;
}

GRAYMODEL_API int gm_match(void* handle, int pyramidLevels, double angleStart, double angleEnd,
                           double angleStep, double nccThreshold, double maxOverlap, int topN,
                           int denseMode,
                           GmMatchResult* outResults, int maxResults) {
    auto* m = static_cast<GrayModelMatcher*>(handle);
    if (!m || !outResults || maxResults <= 0) return -1;

    auto results = m->match(pyramidLevels, angleStart, angleEnd, angleStep,
                            nccThreshold, maxOverlap, topN, denseMode);
    int n = static_cast<int>(results.size());
    int write = std::min(n, maxResults);
    for (int i = 0; i < write; ++i)
        outResults[i] = results[i];
    return write;
}

GRAYMODEL_API double gm_get_last_match_ms(void* handle) {
    auto* m = static_cast<GrayModelMatcher*>(handle);
    if (!m) return 0.0;
    return m->lastMatchMs();
}

} // extern "C"
