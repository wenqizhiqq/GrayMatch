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

    /// <summary>Matching strategy: 0 = grayscale NCC (raw intensity),
    /// 1 = shape NCC (Sobel gradient / edge map). Shape mode is robust to
    /// illumination changes because it scores contours, not brightness.</summary>
    public int MatchMode { get; set; } = 0;

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
    /// <param name="matchMode">0 = grayscale NCC, 1 = shape (edge) NCC.</param>
    public List<MatchResult> Match(
        int pyramidLevels,
        double angleStart,
        double angleEnd,
        double angleStep,
        double nccThreshold,
        double maxOverlap,
        int topN,
        int matchMode = 0)
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
                nccThreshold, maxOverlap, topN, matchMode, buffer, buffer.Length);

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
        int matchMode,
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
