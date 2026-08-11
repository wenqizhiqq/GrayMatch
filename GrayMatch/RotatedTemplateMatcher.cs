using OpenCvSharp;
using System.Runtime.InteropServices;

namespace GrayMatch;

public class RotatedTemplateMatcher : IDisposable
{
    private Mat? _source;
    private Mat? _template;
    private Mat? _sourceGray;

    public Mat Source => _source ?? throw new InvalidOperationException("Source image not loaded.");
    public Mat Template => _template ?? throw new InvalidOperationException("Template not created.");

    /// <summary>Pure matching time (ms) of the last Match call, excluding template
    /// cache construction — reported by the native layer so template creation and
    /// UI drawing are never counted.</summary>
    public double LastMatchMs { get; private set; }

    public void LoadSource(string path)
    {
        DisposeSource();
        _source = Cv2.ImRead(path, ImreadModes.Color);
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
        int topN)
    {
        if (_sourceGray == null || _template == null)
            throw new InvalidOperationException("Source and template must be set before matching.");

        IntPtr handle = gm_create();
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create native matcher.");

        try
        {
            int s = gm_set_source(handle, _sourceGray.Data, _sourceGray.Width, _sourceGray.Height, (int)_sourceGray.Step(), 1);
            int t = gm_set_template(handle, _template.Data, _template.Width, _template.Height, (int)_template.Step(), 1);
            if (s != 0 || t != 0)
                throw new InvalidOperationException("Failed to set source/template in native matcher.");

            var buffer = new GmMatchResult[topN];
            int written = gm_match(
                handle, pyramidLevels, angleStart, angleEnd, angleStep,
                nccThreshold, maxOverlap, topN, buffer, buffer.Length);

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
        double globalBrightnessThresh = 28)
    {
        var outList = new List<DefectResult>();
        if (_sourceGray == null || _template == null || results == null || results.Count == 0)
            return outList;

        var srcGray = _sourceGray;      // cached by LoadSource — no per-call full-image conversion
        var tmpl = _template;
        int tw = tmpl.Width, th = tmpl.Height;
        int minDim = Math.Min(tw, th);

        double minArea = minAreaFrac * tw * th;
        const double maxAreaFrac = 0.60;    // above this it is a whole-instance shift, not a local defect
        // sub-pixel / sub-degree pose error always lights up the instance outline: ignore a border band
        int margin = Math.Max(2, (int)Math.Round(0.04 * minDim));

        var buckets = new List<DefectResult>[results.Count];

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
            Cv2.WarpAffine(srcGray, patch, m, new Size(tw, th), (InterpolationFlags)1, BorderTypes.Replicate);

            using var diff = new Mat();
            Cv2.Absdiff(tmpl, patch, diff);
            double meanDiff = Cv2.Mean(diff).Val0;

            using var mask = new Mat();
            Cv2.Threshold(diff, mask, diffThreshold, 255, ThresholdTypes.Binary);
            // 1-px ridges are alignment noise, not defects. Keep the kernel at 2x2: a 3x3 open
            // erases genuine 2-px-wide scratches entirely.
            using var k = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 2));
            Cv2.MorphologyEx(mask, mask, MorphTypes.Open, k);
            // erase the border band (instance outline)
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
                Cv2.DrawContours(cmask, new List<Point[]> { c }, 0, Scalar.All(255), -1);
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
                    Score = sev
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

        return outList;
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
