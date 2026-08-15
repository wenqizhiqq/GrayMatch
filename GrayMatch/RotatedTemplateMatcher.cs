using OpenCvSharp;
using System.IO;
using System.Runtime.InteropServices;

namespace GrayMatch;

public class RotatedTemplateMatcher : IDisposable
{
    private Mat? _source;
    private Mat? _template;
    private Mat? _sourceGray;

    public Mat Source => _source ?? throw new InvalidOperationException("Source image not loaded.");
    public Mat? Template => _template;

    /// <summary>Pure matching time (ms) of the last Match call, excluding template
    /// cache construction — reported by the native layer so template creation and
    /// UI drawing are never counted.</summary>
    public double LastMatchMs { get; private set; }

    public void LoadSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("图片路径为空或空白。", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException($"找不到图片文件：{path}", path);

        DisposeSource();
        _source = Cv2.ImRead(path, ImreadModes.Color);
        if (_source == null || _source.Empty())
            throw new InvalidOperationException($"无法读取图片：{path}。文件可能已损坏、被占用，或格式不受 OpenCV 支持。");

        _sourceGray = new Mat();
        Cv2.CvtColor(_source, _sourceGray, ColorConversionCodes.BGR2GRAY);
    }

    public void SetSource(Mat image)
    {
        DisposeSource();
        _source = image.Clone();
        _sourceGray = new Mat();
        if (_source.Channels() == 1)
            _source.CopyTo(_sourceGray);
        else
            Cv2.CvtColor(_source, _sourceGray, ColorConversionCodes.BGR2GRAY);
    }

    public void SetTemplateFromRoi(Rect roi)
    {
        if (_sourceGray == null) throw new InvalidOperationException("Source image not loaded.");
        _template?.Dispose();
        _template = new Mat(_sourceGray, roi);
        _template = _template.Clone();
    }

    public void SetTemplate(Mat templateGray)
    {
        _template?.Dispose();
        _template = templateGray.Clone();
    }

    /// <summary>
    /// Rotation-invariant NCC matching. The heavy lifting runs in the native
    /// GrayModelNative DLL (built from C++); this method only marshals image data.
    /// </summary>
    public List<MatchResult> Match(
        int pyramidLevels,
        double angleStart,
        double angleEnd,
        double angleStep,
        double nccThreshold,
        double maxOverlap,
        int topN,
        int denseMode = 0)
    {
        if (_sourceGray == null || _template == null)
            throw new InvalidOperationException("Source and template must be set before matching.");

        IntPtr handle;
        try
        {
            handle = gm_create();
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException(
                "无法加载 GrayModelNative.dll。请确保以下文件与程序可执行文件(GrayMatch.Wpf.exe)位于同一目录：\n" +
                "  GrayModelNative.dll\n  opencv_world480.dll\n" +
                "  vcruntime140.dll、vcruntime140_1.dll、msvcp140.dll、concrt140.dll、vcomp140.dll\n" +
                "（后几个是 VC++ 2022 运行库；若目标电脑没装 Visual Studio 或 VC++ 可再发行包就会缺失，\n" +
                " 会导致 GrayModelNative.dll 加载失败。请从开发机的输出目录把整个文件夹一起拷贝。）\n" +
                "原始错误：" + ex.Message, ex);
        }
        catch (BadImageFormatException ex)
        {
            throw new InvalidOperationException(
                "GrayModelNative.dll 加载失败：很可能是 32 位 / 64 位不匹配。本程序需要 64 位 Windows，" +
                "且 GrayModelNative.dll 为 x64 版本，请将程序以 x64 运行。\n原始错误：" + ex.Message, ex);
        }
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create native matcher.");

        try
        {
            int s = gm_set_source(handle, _sourceGray.Data, _sourceGray.Width, _sourceGray.Height, (int)_sourceGray.Step(), 1);
            int t = gm_set_template(handle, _template.Data, _template.Width, _template.Height, (int)_template.Step(), 1);
            if (s != 0 || t != 0)
                throw new InvalidOperationException("Failed to set source/template in native matcher.");

            // Dense mode can return far more than topN distinct matches on a regular array,
            // so grow the native result buffer when enabled to avoid silent truncation.
            int bufSize = denseMode != 0 ? Math.Max(topN, 4096) : topN;
            var buffer = new GmMatchResult[bufSize];
            int written = gm_match(
                handle, pyramidLevels, angleStart, angleEnd, angleStep,
                nccThreshold, maxOverlap, topN, denseMode, buffer, buffer.Length);

            if (written < 0)
                return new List<MatchResult>();

            LastMatchMs = gm_get_last_match_ms(handle);

            var results = new List<MatchResult>(written);
            for (int i = 0; i < written; i++)
            {
                var r = buffer[i];
                results.Add(new MatchResult
                {
                    Index = i + 1,
                    Score = r.score,
                    CenterX = r.centerX,
                    CenterY = r.centerY,
                    Angle = r.angle,
                    TemplateWidth = r.templateWidth,
                    TemplateHeight = r.templateHeight,
                    LeftTopX = r.leftTopX,
                    LeftTopY = r.leftTopY,
                    Level = r.level
                });
            }
            return results;
        }
        finally
        {
            gm_destroy(handle);
        }
    }


    /// <summary>
    /// Tunables for <see cref="DetectDefects(List{MatchResult})"/>. Kept as a mutable property so
    /// the UI can push slider/textbox edits straight in without threading the values through
    /// every call site.
    /// </summary>
    public DefectOptions DefectOptions { get; set; } = new();

    /// <summary>
    /// Runs defect detection using the current <see cref="DefectOptions"/>.
    /// Overload resolution prefers this exact-arity method over the fully-parameterised one,
    /// so existing <c>DetectDefects(results)</c> call sites automatically pick up UI settings.
    /// </summary>
    public List<DefectResult> DetectDefects(List<MatchResult> results)
    {
        var o = DefectOptions ?? new DefectOptions();
        return DetectDefects(results, o.DiffThreshold, o.MinAreaFrac, o.GlobalBrightnessThresh,
                             o.EdgeTolerance, o.EdgeGradThresh, o.ErodeSize, o.DilateSize);
    }

    /// <summary>Wall-clock milliseconds spent inside the last <c>DetectDefects</c> call.</summary>
    public double LastDefectMs { get; private set; }

    /// <summary>
    /// Per-match defect detection. For each matched instance we inverse-rotate the source
    /// region by -angle so it becomes upright and aligned with the template, compute the
    /// absolute grayscale difference, threshold it to an anomaly mask, cluster connected
    /// components, and heuristically classify each into one of four defect types:
    ///   污渍/异物 (dark blob), 划痕 (elongated bright/dark line),
    ///   缺料/破损 (large missing/darker region), 亮度/对比度异常 (global brightness/contrast shift).
    /// Defects are returned in upright template-local coords plus a precomputed image-space
    /// center so the UI can draw the red overlay without re-deriving the rotation transform.
    /// </summary>
    public List<DefectResult> DetectDefects(
        List<MatchResult> results,
        double diffThreshold = 45,
        double minAreaFrac = 0.004,
        double globalBrightnessThresh = 28,
        int edgeTolerance = 0,
        double edgeGradThresh = 30,
        int erodeSize = 2,
        int dilateSize = 3)
    {
        var swDefect = System.Diagnostics.Stopwatch.StartNew();
        var outList = new List<DefectResult>();
        if (_sourceGray == null || _template == null || results == null || results.Count == 0)
        {
            LastDefectMs = 0;
            return outList;
        }

        var srcGray = _sourceGray;      // cached by LoadSource — no per-call full-image conversion
        var tmpl = _template;
        int tw = tmpl.Width, th = tmpl.Height;
        int minDim = Math.Min(tw, th);

        double minArea = minAreaFrac * tw * th;
        const double maxAreaFrac = 0.60;    // above this it is a whole-instance shift, not a local defect
        // sub-pixel / sub-degree pose error always lights up the instance outline: ignore a border band
        int margin = Math.Max(2, (int)Math.Round(0.04 * minDim));

        // -----------------------------------------------------------------------------------
        // Edge-tolerance band.
        // Sub-pixel / sub-degree pose error makes EVERY strong grey-level transition of the part
        // light up in the difference image, so contours kept appearing on the outline and on
        // internal contour junctions instead of on real defects. A morphological gradient
        // (dilate - erode) marks exactly those transitions; dilating it by `edgeTolerance` px
        // turns them into a "don't care" band that is subtracted from the defect mask.
        // Computed once per call (template-sized, O(tw*th)) and shared read-only by all threads.
        // -----------------------------------------------------------------------------------
        Mat? edgeBand = null;
        if (edgeTolerance > 0)
        {
            edgeBand = new Mat();
            using (var gk = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3)))
                Cv2.MorphologyEx(tmpl, edgeBand, MorphTypes.Gradient, gk);
            Cv2.Threshold(edgeBand, edgeBand, edgeGradThresh, 255, ThresholdTypes.Binary);
            int ks = edgeTolerance * 2 + 1;
            using (var ek = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(ks, ks)))
                Cv2.Dilate(edgeBand, edgeBand, ek);
        }

        var buckets = new List<DefectResult>[results.Count];

        try
        {

        System.Threading.Tasks.Parallel.For(0, results.Count, i =>
        {
            var local = new List<DefectResult>();
            buckets[i] = local;

            var r = results[i];
            double ang = r.Angle;
            var center = new Point2f((float)r.CenterX, (float)r.CenterY);

            // Fold "upright the whole image" + "crop the template window" into ONE affine warp whose
            // destination is only tw x th. warpAffine is destination-driven, so the cost drops from
            // O(image area) per match to O(template area) per match.
            using var m = Cv2.GetRotationMatrix2D(center, -ang, 1.0);
            double ox = r.CenterX - (tw - 1) * 0.5;
            double oy = r.CenterY - (th - 1) * 0.5;
            m.Set<double>(0, 2, m.Get<double>(0, 2) - ox);
            m.Set<double>(1, 2, m.Get<double>(1, 2) - oy);

            using var patch = new Mat();
            Cv2.WarpAffine(srcGray, patch, m, new OpenCvSharp.Size(tw, th), (InterpolationFlags)1, BorderTypes.Replicate);

            using var diff = new Mat();
            Cv2.Absdiff(tmpl, patch, diff);
            double meanDiff = Cv2.Mean(diff).Val0;

            using var mask = new Mat();
            Cv2.Threshold(diff, mask, diffThreshold, 255, ThresholdTypes.Binary);

            // (a) drop everything sitting on a template grey-level transition — that is pose
            //     error on the part outline / internal contour junctions, never a defect.
            if (edgeBand != null) Cv2.Subtract(mask, edgeBand, mask);

            // (b) erode: thin residual edge slivers collapse to nothing.
            if (erodeSize > 1)
            {
                using var ek = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(erodeSize, erodeSize));
                Cv2.Erode(mask, mask, ek);
            }

            // (c) dilate: restore the true footprint of what survived and re-bridge a scratch
            //     that the erosion broke into dashes. Keep dilate >= erode.
            if (dilateSize > 1)
            {
                using var dk = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(dilateSize, dilateSize));
                Cv2.Dilate(mask, mask, dk);
            }

            // (d) erase the outer border band (whole-instance outline).
            Cv2.Rectangle(mask, new Rect(0, 0, tw, th), Scalar.All(0), margin * 2);

            Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            bool foundLocal = false;
            for (int ci = 0; ci < contours.Length; ci++)
            {
                var c = contours[ci];
                double area = Cv2.ContourArea(c);
                if (area < minArea) continue;
                double areaFrac = area / (double)(tw * th);
                if (areaFrac > maxAreaFrac) continue;

                // tight, rotation-invariant footprint — an axis-aligned bbox of a diagonal scratch
                // is enormous, which is exactly why the old overlay looked oversized.
                var minRect = Cv2.MinAreaRect(c);
                double mw = minRect.Size.Width, mh = minRect.Size.Height;
                double longSide = Math.Max(mw, mh), shortSide = Math.Min(mw, mh);
                double ar = longSide / Math.Max(1.0, shortSide);

                using var cmask = new Mat(th, tw, MatType.CV_8UC1, Scalar.All(0));
                Cv2.DrawContours(cmask, new List<OpenCvSharp.Point[]> { c }, 0, Scalar.All(255), -1);
                double mT = Cv2.Mean(tmpl, cmask).Val0;
                double mP = Cv2.Mean(patch, cmask).Val0;
                double sev = Cv2.Mean(diff, cmask).Val0;
                double delta = mP - mT; // >0 instance brighter than template, <0 darker

                bool dark = delta < -18;
                bool bright = delta > 18;
                bool big = areaFrac > 0.18;
                // a scratch must be genuinely thin AND long AND small in area
                bool elongated = ar >= 4.0
                                 && shortSide <= Math.Max(3.0, 0.22 * minDim)
                                 && longSide >= 0.15 * minDim
                                 && areaFrac <= 0.25;

                string type;
                if (elongated) type = "划痕";
                else if (dark && big) type = "缺料/破损";
                else if (dark) type = "污渍/异物";
                else if (bright) type = "亮度异常";
                else type = "亮度/对比度异常";

                var bbox = Cv2.BoundingRect(c);

                // tight-box center: template-local -> image space, using the SAME -angle transform
                // the UI applies to the green match box.
                double ux = minRect.Center.X - tw / 2.0;
                double uy = minRect.Center.Y - th / 2.0;
                double phi = -ang * Math.PI / 180.0;
                double cosv = Math.Cos(phi), sinv = Math.Sin(phi);
                double imgCx = r.CenterX + (ux * cosv - uy * sinv);
                double imgCy = r.CenterY + (ux * sinv + uy * cosv);

                local.Add(new DefectResult
                {
                    CenterX = r.CenterX,
                    CenterY = r.CenterY,
                    Angle = r.Angle,
                    Tw = tw,
                    Th = th,
                    LeftTopX = r.LeftTopX,
                    LeftTopY = r.LeftTopY,
                    X = bbox.X,
                    Y = bbox.Y,
                    W = mw,
                    H = mh,
                    RectAngle = -ang + minRect.Angle,
                    ImgCx = imgCx,
                    ImgCy = imgCy,
                    Type = type,
                    Score = sev,
                    // carry the actual defective pixels (template-local) so the UI can paint them red
                    Pixels = CopyMask(cmask, tw, th),
                    Pw = tw,
                    Ph = th
                });
                foundLocal = true;
            }

            // No localized defect, but the instance as a whole is off in brightness/contrast.
            // Draw a small centred badge — NOT a full-template red block.
            if (!foundLocal && meanDiff > globalBrightnessThresh)
            {
                double badge = Math.Min(40.0, Math.Max(12.0, 0.22 * minDim));
                local.Add(new DefectResult
                {
                    CenterX = r.CenterX,
                    CenterY = r.CenterY,
                    Angle = r.Angle,
                    Tw = tw,
                    Th = th,
                    LeftTopX = r.LeftTopX,
                    LeftTopY = r.LeftTopY,
                    X = (int)Math.Round((tw - badge) / 2.0),
                    Y = (int)Math.Round((th - badge) / 2.0),
                    W = badge,
                    H = badge,
                    RectAngle = -ang,
                    ImgCx = r.CenterX,
                    ImgCy = r.CenterY,
                    Type = "亮度/对比度异常",
                    Score = meanDiff
                });
            }
        });

        for (int i = 0; i < buckets.Length; i++)
            if (buckets[i] != null) outList.AddRange(buckets[i]);

        }
        finally
        {
            edgeBand?.Dispose();
            swDefect.Stop();
            LastDefectMs = swDefect.Elapsed.TotalMilliseconds;
        }

        return outList;
    }

    /// <summary>Copies a continuous CV_8UC1 mask (th rows x tw cols) into a managed byte[].</summary>
    private static byte[] CopyMask(Mat mask, int tw, int th)
    {
        var buf = new byte[tw * th];
        System.Runtime.InteropServices.Marshal.Copy(mask.Data, buf, 0, buf.Length);
        return buf;
    }


    #region Native interop

    [StructLayout(LayoutKind.Sequential)]
    private struct GmMatchResult
    {
        public double score;
        public double centerX;
        public double centerY;
        public double angle;
        public int templateWidth;
        public int templateHeight;
        public int leftTopX;
        public int leftTopY;
        public int level;
    }

    private const string NativeLib = "GrayModelNative";

    [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr gm_create();

    [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void gm_destroy(IntPtr handle);

    [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int gm_set_source(IntPtr handle, IntPtr data, int w, int h, int step, int channels);

    [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int gm_set_template(IntPtr handle, IntPtr data, int w, int h, int step, int channels);

    [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int gm_match(
        IntPtr handle,
        int pyramidLevels,
        double angleStart,
        double angleEnd,
        double angleStep,
        double nccThreshold,
        double maxOverlap,
        int topN,
        int denseMode,
        [In, Out] GmMatchResult[] outResults,
        int maxResults);

    [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern double gm_get_last_match_ms(IntPtr handle);

    #endregion

    private void DisposeSource()
    {
        _source?.Dispose();
        _sourceGray?.Dispose();
        _source = null;
        _sourceGray = null;
    }

    public void Dispose()
    {
        DisposeSource();
        _template?.Dispose();
        _template = null;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Tunables for template-comparison defect detection. Defaults match the historical
/// hard-coded values, so behaviour is unchanged until the user edits the panel.
/// </summary>
public class DefectOptions
{
    /// <summary>Grey-level difference above which a pixel is anomalous (1..254).</summary>
    public double DiffThreshold { get; set; } = 45;

    /// <summary>Connected regions smaller than this fraction of the template area are noise.</summary>
    public double MinAreaFrac { get; set; } = 0.004;

    /// <summary>Mean difference above which an instance gets a global brightness badge.</summary>
    public double GlobalBrightnessThresh { get; set; } = 28;

    /// <summary>
    /// Half-width (px) of the "don't care" band grown around every template grey-level
    /// transition — an extra safety net for parts whose outline still leaks through.
    ///
    /// Off by default: measured on a contour-heavy part across 0.2°–3.0° of pose error, the
    /// erode/dilate pass alone already removed 100% of the outline false positives, while a
    /// 3-px band additionally swallowed a genuine 20-px scratch (it shrank to 8 px, and to
    /// nothing without the dilate). Turn it on (2–4) only if outline artefacts survive, and
    /// expect defects that touch a contour to be attenuated.
    /// </summary>
    public int EdgeTolerance { get; set; } = 0;

    /// <summary>Morphological-gradient strength that counts as a template edge (1..254).</summary>
    public double EdgeGradThresh { get; set; } = 30;

    /// <summary>Erosion kernel size; &lt;= 1 skips erosion. Removes thin edge slivers.</summary>
    public int ErodeSize { get; set; } = 2;

    /// <summary>Dilation kernel size; &lt;= 1 skips dilation. Restores the true defect footprint.</summary>
    public int DilateSize { get; set; } = 3;
}
