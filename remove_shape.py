import io, os, sys

ROOT = r"D:\wqz\code\GrayMatch"

def read(p):
    with io.open(p, encoding='utf-8') as f:
        return f.read()

def write(p, s):
    with io.open(p, 'w', encoding='utf-8', newline='') as f:
        f.write(s)

def once(s, old, new, label):
    c = s.count(old)
    if c == 0:
        print("SKIP (already applied):", label)
        return s
    assert c == 1, "MULTIPLE occurrences of [%s]: %d" % (label, c)
    return s.replace(old, new, 1)

# ---------------------------------------------------------------------------
# 1) NATIVE HEADER
# ---------------------------------------------------------------------------
p = os.path.join(ROOT, "GrayModelNative", "gray_model_native.h")
s = read(p)
s = once(s,
    "// Runs rotation-invariant NCC matching.\n"
    "// matchMode: 0 = grayscale NCC (raw intensity), 1 = shape NCC (Sobel edge map).\n"
    "// Results are written into outResults (up to maxResults). Returns the number written.",
    "// Runs rotation-invariant NCC matching.\n"
    "// Results are written into outResults (up to maxResults). Returns the number written.",
    "h: comment")
s = once(s,
    "                           int topN,\n                           int matchMode,\n                           GmMatchResult* outResults,",
    "                           int topN,\n                           GmMatchResult* outResults,",
    "h: signature")
write(p, s)

# ---------------------------------------------------------------------------
# 2) NATIVE CPP
# ---------------------------------------------------------------------------
p = os.path.join(ROOT, "GrayModelNative", "gray_model_native.cpp")
s = read(p)
s = once(s,
    "\n"
    "// Sobel gradient-magnitude map, used by shape (edge) matching. Running NCC over\n"
    "// this instead of raw intensity makes the score depend on contours rather than\n"
    "// absolute brightness, so it tolerates illumination changes and flat-region noise.\n"
    "//\n"
    "// IMPORTANT: the Sobel output MUST be CV_32F. Using CV_16S and then calling\n"
    "// cv::magnitude() crashes (access violation) in the shipped opencv_world480/vc16\n"
    "// build, because cv::magnitude only accepts floating-point matrices.\n"
    "cv::Mat gradientMagnitude(const cv::Mat& gray) {\n"
    "    cv::Mat gx, gy;\n"
    "    cv::Sobel(gray, gx, CV_32F, 1, 0, 3);\n"
    "    cv::Sobel(gray, gy, CV_32F, 0, 1, 3);\n"
    "    cv::Mat mag;\n"
    "    cv::magnitude(gx, gy, mag);\n"
    "    // Return CV_32F, NOT 8-bit. A 3x3 Sobel magnitude reaches ~1020 on 8-bit\n"
    "    // input, so converting back to CV_8U (convertScaleAbs) clips every strong\n"
    "    // edge to 255. That flattening destroys the very contrast the shape match\n"
    "    // relies on and made sharp, unrotated targets drop out. cv::matchTemplate\n"
    "    // accepts CV_32F directly, and TM_CCOEFF_NORMED is scale-invariant, so\n"
    "    // keeping full float range costs nothing.\n"
    "    return mag;   // CV_32FC1, same size as the input\n"
    "}\n"
    "\n"
    "// Caches rotated templates per angle so each distinct angle is warped only once\n",
    "\n"
    "// Caches rotated templates per angle so each distinct angle is warped only once\n",
    "cpp: gradientMagnitude fn")
s = once(s,
    "// across the whole match (coarse sweep + every fine window reuses the cache).\n"
    "// When `grad` is set, the cached entry is the gradient magnitude of the ROTATED\n"
    "// template: rotate first, then differentiate, so the edge map matches what the\n"
    "// rotated object really looks like (rotating a gradient map would smear it).\n"
    "struct TemplateCache {\n"
    "    const cv::Mat* tmpl;\n"
    "    bool grad;\n"
    "    std::map<int, cv::Mat> cache;\n"
    "    explicit TemplateCache(const cv::Mat* t, bool useGradient = false)\n"
    "        : tmpl(t), grad(useGradient) {}\n",
    "// across the whole match (coarse sweep + every fine window reuses the cache).\n"
    "struct TemplateCache {\n"
    "    const cv::Mat* tmpl;\n"
    "    std::map<int, cv::Mat> cache;\n"
    "    explicit TemplateCache(const cv::Mat* t)\n"
    "        : tmpl(t) {}\n",
    "cpp: TemplateCache")
s = once(s, "\n        if (grad) r = gradientMagnitude(r);", "", "cpp: if(grad)")
s = once(s,
    "    // matchMode: 0 = grayscale NCC (raw intensity), 1 = shape NCC (edge/gradient).\n"
    "    std::vector<GmMatchResult> match(int /*pyramidLevels*/, double angleStart, double angleEnd,\n"
    "                                     double angleStep, double nccThreshold, double maxOverlap,\n"
    "                                     int topN, int matchMode) const {",
    "    std::vector<GmMatchResult> match(int /*pyramidLevels*/, double angleStart, double angleEnd,\n"
    "                                     double angleStep, double nccThreshold, double maxOverlap,\n"
    "                                     int topN) const {",
    "cpp: match sig")
s = once(s, "\n        const bool gradMode = (matchMode == 1);", "", "cpp: gradMode")
s = once(s, "        TemplateCache coarseCache(&coarseTmpl, gradMode);",
              "        TemplateCache coarseCache(&coarseTmpl);", "cpp: coarseCache")
s = once(s,
    "        // In shape mode the scene is differentiated once per pass; the template\n"
    "        // side is differentiated inside the cache (see TemplateCache::get).\n"
    "        cv::Mat coarseSrcForMatch = gradMode ? gradientMagnitude(coarseSrc) : coarseSrc;",
    "        cv::Mat coarseSrcForMatch = coarseSrc;", "cpp: coarseSrcForMatch")
s = once(s, "            TemplateCache fullCache(&templateGray_, gradMode);",
              "            TemplateCache fullCache(&templateGray_);", "cpp: fullCache")
s = once(s, "            cv::Mat fullSrc = gradMode ? gradientMagnitude(sourceGray_) : sourceGray_;",
              "            cv::Mat fullSrc = sourceGray_;", "cpp: fullSrc")
s = once(s, "        TemplateCache fineCache(&fineTmpl, gradMode);",
              "        TemplateCache fineCache(&fineTmpl);", "cpp: fineCache")
s = once(s, "\n                if (gradMode) subFine = gradientMagnitude(subFine);", "", "cpp: subFine")
s = once(s,
    "GRAYMODEL_API int gm_match(void* handle, int pyramidLevels, double angleStart, double angleEnd,\n"
    "                           double angleStep, double nccThreshold, double maxOverlap, int topN,\n"
    "                           int matchMode, GmMatchResult* outResults, int maxResults) {",
    "GRAYMODEL_API int gm_match(void* handle, int pyramidLevels, double angleStart, double angleEnd,\n"
    "                           double angleStep, double nccThreshold, double maxOverlap, int topN,\n"
    "                           GmMatchResult* outResults, int maxResults) {",
    "cpp: gm_match sig")
s = once(s, "                            nccThreshold, maxOverlap, topN, matchMode);",
              "                            nccThreshold, maxOverlap, topN);", "cpp: gm_match call")
write(p, s)

# ---------------------------------------------------------------------------
# 3) RotatedTemplateMatcher.cs
# ---------------------------------------------------------------------------
p = os.path.join(ROOT, "GrayMatch", "RotatedTemplateMatcher.cs")
s = read(p)
s = once(s,
    "    public double LastMatchMs { get; private set; }\n\n"
    "    /// <summary>Matching strategy: 0 = grayscale NCC (raw intensity),\n"
    "    /// 1 = shape NCC (Sobel gradient / edge map). Shape mode is robust to\n"
    "    /// illumination changes because it scores contours, not brightness.</summary>\n"
    "    public int MatchMode { get; set; } = 0;\n\n",
    "    public double LastMatchMs { get; private set; }\n\n",
    "cs: MatchMode prop")
s = once(s,
    "    /// <param name=\"matchMode\">0 = grayscale NCC, 1 = shape (edge) NCC.</param>\n"
    "    public List<MatchResult> Match(\n"
    "        int pyramidLevels,\n"
    "        double angleStart,\n"
    "        double angleEnd,\n"
    "        double angleStep,\n"
    "        double nccThreshold,\n"
    "        double maxOverlap,\n"
    "        int topN,\n"
    "        int matchMode = 0)\n"
    "    {",
    "    public List<MatchResult> Match(\n"
    "        int pyramidLevels,\n"
    "        double angleStart,\n"
    "        double angleEnd,\n"
    "        double angleStep,\n"
    "        double nccThreshold,\n"
    "        double maxOverlap,\n"
    "        int topN)\n"
    "    {",
    "cs: Match sig")
s = once(s,
    "                nccThreshold, maxOverlap, topN, matchMode, buffer, buffer.Length);",
    "                nccThreshold, maxOverlap, topN, buffer, buffer.Length);",
    "cs: Match call")
s = once(s,
    "    [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]\n"
    "    private static extern int gm_match(\n"
    "        IntPtr handle,\n"
    "        int pyramidLevels,\n"
    "        double angleStart,\n"
    "        double angleEnd,\n"
    "        double angleStep,\n"
    "        double nccThreshold,\n"
    "        double maxOverlap,\n"
    "        int topN,\n"
    "        int matchMode,\n"
    "        [In, Out] GmMatchResult[] outResults,\n"
    "        int maxResults);",
    "    [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]\n"
    "    private static extern int gm_match(\n"
    "        IntPtr handle,\n"
    "        int pyramidLevels,\n"
    "        double angleStart,\n"
    "        double angleEnd,\n"
    "        double angleStep,\n"
    "        double nccThreshold,\n"
    "        double maxOverlap,\n"
    "        int topN,\n"
    "        [In, Out] GmMatchResult[] outResults,\n"
    "        int maxResults);",
    "cs: PInvoke")
write(p, s)

# ---------------------------------------------------------------------------
# 4) MainWindow.xaml
# ---------------------------------------------------------------------------
p = os.path.join(ROOT, "GrayMatch.Wpf", "MainWindow.xaml")
s = read(p)
s = once(s,
    '                    <Separator Margin="0,12,0,6"/>\n'
    '                <TextBlock Text="匹配方式" FontWeight="Bold" FontSize="13" Margin="0,2,0,6"/>\n'
    '                <CheckBox x:Name="ChkShapeMode" Content="形状匹配（边缘）"\n'
    '                          IsChecked="{Binding IsShapeMode}" Margin="0,2,0,2"/>\n'
    '                <TextBlock Text="勾选 = 按轮廓/梯度匹配，抗光照变化；取消 = 灰度 NCC"\n'
    '                           FontSize="10" Foreground="Gray" TextWrapping="Wrap" Margin="0,0,0,4"/>',
    "", "xaml: shape group")
write(p, s)

# ---------------------------------------------------------------------------
# 5) MainWindow.xaml.cs
# ---------------------------------------------------------------------------
p = os.path.join(ROOT, "GrayMatch.Wpf", "MainWindow.xaml.cs")
s = read(p)
s = once(s,
    "    public string MatchMsText { get => _matchMsText; set => Set(ref _matchMsText, value); }\n\n"
    "    private bool _isShapeMode;\n"
    "    /// <summary>Bound to the 形状匹配 checkbox: true = shape/edge NCC, false = grayscale NCC.</summary>\n"
    "    public bool IsShapeMode { get => _isShapeMode; set => Set(ref _isShapeMode, value); }\n\n",
    "    public string MatchMsText { get => _matchMsText; set => Set(ref _matchMsText, value); }\n\n",
    "xamlcs: IsShapeMode")
write(p, s)

# ---------------------------------------------------------------------------
# 6) Form1.cs
# ---------------------------------------------------------------------------
p = os.path.join(ROOT, "GrayMatch", "Form1.cs")
s = read(p)
s = once(s,
    "    private NumericUpDown _numTopN = null!;\n"
    "    private CheckBox _chkShape = null!;\n"
    "    private Panel _canvasPanel = null!;",
    "    private NumericUpDown _numTopN = null!;\n"
    "    private Panel _canvasPanel = null!;",
    "form: field")
s = once(s,
    "\n        // 形状匹配开关: checked = edge/gradient NCC, unchecked = grayscale NCC.\n"
    "        _chkShape = new CheckBox\n"
    "        {\n"
    "            Text = \"形状匹配（边缘）\",\n"
    "            AutoSize = true,\n"
    "            Left = 8,\n"
    "            Top = y + 6,\n"
    "        };\n"
    "        _leftPanel.Controls.Add(_chkShape);\n"
    "        y = _chkShape.Top + 28;\n",
    "\n", "form: checkbox build")
s = once(s,
    "            results = await Task.Run(() => _matcher.Match(pyramid, start, end, step, threshold, overlap, topN, _chkShape.Checked ? 1 : 0), token);",
    "            results = await Task.Run(() => _matcher.Match(pyramid, start, end, step, threshold, overlap, topN), token);",
    "form: match call")
write(p, s)

# ---------------------------------------------------------------------------
# 7) MatcherTests.cs
# ---------------------------------------------------------------------------
p = os.path.join(ROOT, "GrayMatch.Tests", "MatcherTests.cs")
s = read(p)
s = once(s,
    "            topN: 20,\n            matchMode: 0);",
    "            topN: 20);", "test: call1")
s = once(s,
    "            topN: 50,\n            matchMode: 0);",
    "            topN: 50);", "test: call2")
write(p, s)

print("ALL PATCHES DONE")
PY_DONE = True
