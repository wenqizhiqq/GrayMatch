import io, os

BASE = r"D:\wqz\code\GrayMatch"
cpp = os.path.join(BASE, "GrayModelNative", "gray_model_native.cpp")

with io.open(cpp, encoding="utf-8") as f:
    s = f.read()

def once(old, new, label):
    c = s.count(old)
    assert c == 1, "ANCHOR [%s] count=%d" % (label, c)
    return s.replace(old, new, 1)

# ---- Edit 1: open the legacy two-pass branch for pyramidLevels <= 0 ----
a1 = (
    "        if (sourceGray_.empty() || templateGray_.empty())\n"
    "            return {};\n"
)
a1new = (
    "        if (sourceGray_.empty() || templateGray_.empty())\n"
    "            return {};\n"
    "\n"
    "        // Pyramid levels <= 0 keeps the original full-resolution two-pass\n"
    "        // (coarse 0.25x sweep + 0.35x windowed refinement). This is the\n"
    "        // historically tuned behavior, preserved verbatim so existing callers\n"
    "        // that pass 0 get exactly what they had before.\n"
    "        if (pyramidLevels <= 0) {\n"
)
s = once(a1, a1new, "open-legacy-if")

# ---- Edit 2: close the legacy branch and append the pyramid cascade ----
a2 = (
    "        for (auto& r : final) {\n"
    "            r.level = 0;\n"
    "            r.templateWidth = templateGray_.cols;\n"
    "            r.templateHeight = templateGray_.rows;\n"
    "            r.leftTopX = static_cast<int>(std::round(r.centerX - templateGray_.cols / 2.0));\n"
    "            r.leftTopY = static_cast<int>(std::round(r.centerY - templateGray_.rows / 2.0));\n"
    "        }\n"
    "        return final;\n"
    "    }\n"
)

cascade = '''        for (auto& r : final) {
            r.level = 0;
            r.templateWidth = templateGray_.cols;
            r.templateHeight = templateGray_.rows;
            r.leftTopX = static_cast<int>(std::round(r.centerX - templateGray_.cols / 2.0));
            r.leftTopY = static_cast<int>(std::round(r.centerY - templateGray_.rows / 2.0));
        }
        return final;
        }   // ----- end legacy two-pass (pyramidLevels <= 0) -----

        // ===================== Pyramid cascade (pyramidLevels >= 1) =====================
        // Build a Gaussian pyramid of BOTH the source and the template. The coarsest
        // level performs ONE cheap full sweep; every finer level then refines each
        // surviving seed inside a small window. Because matching cost at level k is
        // ~1/4^k of a full-resolution search, the total cost is dominated by the tiny
        // coarse pass, while the finest level is always full resolution so the reported
        // position/angle accuracy matches the legacy path.
        const double baseFineStep = std::max(angleStep, 1.0);
        const double coarseThr = std::max(0.05, nccThreshold - 0.30);

        // Cap the number of levels so the coarsest template stays at least ~10px on its
        // short side and the coarsest image stays >= 40px -- below that the tiny
        // template is too ambiguous to seed reliably.
        const int minTplDim = std::min(templateGray_.cols, templateGray_.rows);
        const int minSrcDim = std::min(sourceGray_.cols, sourceGray_.rows);
        int L = pyramidLevels;
        while (L > 1 && ((minTplDim >> L) < 10 || (minSrcDim >> L) < 40))
            --L;
        if (L < 1) L = 1;

        std::vector<cv::Mat> srcPyr(L + 1), tplPyr(L + 1);
        srcPyr[0] = sourceGray_; tplPyr[0] = templateGray_;
        for (int k = 1; k <= L; ++k) {
            cv::pyrDown(srcPyr[k - 1], srcPyr[k]);
            cv::pyrDown(tplPyr[k - 1], tplPyr[k]);
        }
        std::vector<TemplateCache> caches; caches.reserve(L + 1);
        for (int k = 0; k <= L; ++k) caches.emplace_back(&tplPyr[k]);

        auto stepAt = [&](int k) { return baseFineStep * static_cast<double>(1 << k); };

        auto nowP = [] { return std::chrono::steady_clock::now(); };
        auto msP = [](std::chrono::steady_clock::time_point a,
                     std::chrono::steady_clock::time_point b) {
            return std::chrono::duration<double, std::milli>(b - a).count();
        };

        // Pre-warm the coarsest cache (excluded from timing).
        for (double a = angleStart; a <= angleEnd + 1e-6; a += stepAt(L))
            caches[L].get(a);
        auto tMatch = nowP();

        // --- Coarsest full sweep (seeds every target's position + rough angle) ---
        std::vector<GmMatchResult> cur;
        matchAtLevel(srcPyr[L], tplPyr[L], caches[L], L, static_cast<double>(1 << L),
                     angleStart, angleEnd, stepAt(L), coarseThr, topN * 16, cur);
        auto seeds = nonMaxSuppression(cur, maxOverlap);

        double warmMs = 0.0;
        // --- Ascend L-1 .. 0: refine each seed in a small window at the finer scale ---
        for (int k = L - 1; k >= 0; --k) {
            const double scaleK = static_cast<double>(1 << k);
            const double aWin = stepAt(k + 1);                 // angular half-band to re-scan
            const int maxDimK = std::max(tplPyr[k].cols, tplPyr[k].rows);
            const int halfWin = maxDimK / 2 + 16;

            // Pre-warm this level's cache (excluded from timing).
            auto tw0 = nowP();
            for (const auto& sd : seeds) {
                double f0 = std::max(angleStart, sd.angle - aWin);
                double f1 = std::min(angleEnd, sd.angle + aWin);
                for (double a = f0; a <= f1 + 1e-6; a += stepAt(k))
                    caches[k].get(a);
            }
            warmMs += msP(tw0, nowP());

            const double lvlThr = (k == 0) ? nccThreshold : coarseThr;
            std::vector<GmMatchResult> next;
            #pragma omp parallel
            {
                std::vector<GmMatchResult> localNext;
                #pragma omp for nowait
                for (int si = 0; si < static_cast<int>(seeds.size()); ++si) {
                    const auto& det = seeds[si];
                    // seed center is in original coords; map to level-k coords by /scaleK
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
                                 f0, f1, stepAt(k), lvlThr, topN * 4, local);
                    for (auto& r : local) {
                        // matchAtLevel returns coords relative to the crop origin; shift
                        // by the crop origin (level-k pixels) scaled back to original.
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
            if (!nextSeeds.empty())
                seeds = std::move(nextSeeds);
            else if (k > 0)
                break;   // lost the targets climbing up; keep the coarser seeds
        }

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
'''

s = once(a2, cascade, "append-cascade")

with io.open(cpp, "w", encoding="utf-8", newline="") as f:
    f.write(s)
print("patched OK; new length", len(s))
