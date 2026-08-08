using OpenCvSharp;
using Xunit;
using Xunit.Abstractions;

namespace GrayMatch.Tests;

public class MatcherTests : IDisposable
{
    private readonly RotatedTemplateMatcher _matcher = new();
    private readonly ITestOutputHelper _output;

    public MatcherTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Can_Detect_Rotated_Patterns()
    {
        // Build a synthetic image: black background with 4 white rotated rectangles.
        using var source = new Mat(600, 800, MatType.CV_8UC3, Scalar.All(0));
        var centers = new[] { new Point2f(220, 170), new Point2f(620, 170), new Point2f(220, 470), new Point2f(620, 470) };
        var angles = new[] { 0d, 30d, 60d, -45d };

        for (int i = 0; i < centers.Length; i++)
        {
            DrawRotatedRect(source, centers[i], new OpenCvSharp.Size(120, 80), angles[i], Scalar.All(255));
        }

        Cv2.GaussianBlur(source, source, new OpenCvSharp.Size(3, 3), 0.8);

        // Template is an unrotated rectangle from the first target location.
        var roi = new Rect((int)centers[0].X - 60, (int)centers[0].Y - 40, 120, 80);
        using (var gray = new Mat())
        {
            Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
            _matcher.SetSource(gray);
        }
        _matcher.SetTemplateFromRoi(roi);

        var results = _matcher.Match(
            pyramidLevels: 1,
            angleStart: -90,
            angleEnd: 90,
            angleStep: 5,
            nccThreshold: 0.35,
            maxOverlap: 0.25,
            topN: 20,
            matchMode: 0);
        _output.WriteLine($"[native] match took {_matcher.LastMatchMs:F1} ms (pure matching, excl. template cache), {results.Count} results");

        Assert.True(results.Count >= 4, $"Expected at least 4 detections, got {results.Count}. Top: {string.Join(" | ", results.Take(10).Select(r => $"{r.Score:F3}@{r.Angle:F0} ({r.CenterX:F0},{r.CenterY:F0})"))}");
        Assert.All(results, r => Assert.True(r.Score >= 0.35, $"Score {r.Score} below threshold"));

        // Angle accuracy: every true target must be recovered within ±2.5°.
        // (Range -90..90 avoids 180°-symmetry ambiguity for the rectangle template.)
        var knownAngles = new[] { 0d, 30d, 60d, -45d };
        foreach (var ka in knownAngles)
        {
            var best = results.OrderBy(r => Math.Abs(r.Angle - ka)).First();
            double err = Math.Abs(best.Angle - ka);
            Assert.True(err <= 2.5, $"Target angle {ka}° recovered as {best.Angle:F1}° (err {err:F2}°)");
        }

        // Box geometry: every result must be the ORIGINAL (unrotated) template size,
        // centered on its detected center — otherwise the green box is mis-sized/tilted.
        Assert.All(results, r =>
        {
            Assert.Equal(120, r.TemplateWidth);
            Assert.Equal(80, r.TemplateHeight);
            Assert.True(Math.Abs((r.LeftTopX + r.TemplateWidth / 2.0) - r.CenterX) <= 1.0,
                $"Box not centered on X (leftTop {r.LeftTopX} + {r.TemplateWidth}/2 vs center {r.CenterX:F1})");
            Assert.True(Math.Abs((r.LeftTopY + r.TemplateHeight / 2.0) - r.CenterY) <= 1.0,
                $"Box not centered on Y (leftTop {r.LeftTopY} + {r.TemplateHeight}/2 vs center {r.CenterY:F1})");
        });
    }

    [Fact]
    public void Benchmark_All_Angle_Sweep()
    {
        // Larger scene + full-angle sweep (360 angles) to show native throughput.
        using var source = new Mat(600, 900, MatType.CV_8UC3, Scalar.All(0));
        var centers = new[] { new Point2f(220, 180), new Point2f(680, 180), new Point2f(220, 480), new Point2f(680, 480) };
        var angles = new[] { 0d, 30d, 60d, -45d };

        for (int i = 0; i < centers.Length; i++)
            DrawRotatedRect(source, centers[i], new OpenCvSharp.Size(120, 80), angles[i], Scalar.All(255));
        Cv2.GaussianBlur(source, source, new OpenCvSharp.Size(3, 3), 0.8);

        var roi = new Rect((int)centers[0].X - 60, (int)centers[0].Y - 40, 120, 80);
        using (var gray = new Mat())
        {
            Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
            _matcher.SetSource(gray);
        }
        _matcher.SetTemplateFromRoi(roi);

        var results = _matcher.Match(
            pyramidLevels: 4,
            angleStart: -180,
            angleEnd: 180,
            angleStep: 1,
            nccThreshold: 0.35,
            maxOverlap: 0.25,
            topN: 50,
            matchMode: 0);
        _output.WriteLine($"[native] full 360-angle sweep (pyramid=4) took {_matcher.LastMatchMs:F1} ms (pure matching, excl. template cache), {results.Count} results");

        Assert.True(results.Count >= 3, $"Expected >=3, got {results.Count}");
    }

    private static void DrawRotatedRect(Mat img, Point2f center, OpenCvSharp.Size size, double angle, Scalar color)
    {
        using var patch = new Mat(size.Height, size.Width, MatType.CV_8UC3, color);
        var textOrg = new Point(size.Width / 4, size.Height * 3 / 4);
        Cv2.PutText(patch, "2", textOrg, HersheyFonts.HersheyDuplex, size.Width / 35.0, Scalar.All(0), 2);

        double rad = angle * Math.PI / 180.0;
        double cos = Math.Abs(Math.Cos(rad));
        double sin = Math.Abs(Math.Sin(rad));
        int newW = (int)(size.Width * cos + size.Height * sin);
        int newH = (int)(size.Width * sin + size.Height * cos);
        var rotCenter = new Point2f(size.Width / 2f, size.Height / 2f);
        using var rotMat = Cv2.GetRotationMatrix2D(rotCenter, angle, 1.0);
        rotMat.Set<double>(0, 2, rotMat.At<double>(0, 2) + (newW - size.Width) / 2.0);
        rotMat.Set<double>(1, 2, rotMat.At<double>(1, 2) + (newH - size.Height) / 2.0);

        using var rotated = new Mat(newH, newW, MatType.CV_8UC3);
        Cv2.WarpAffine(patch, rotated, rotMat, new OpenCvSharp.Size(newW, newH), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(0));

        int x = (int)(center.X - newW / 2.0);
        int y = (int)(center.Y - newH / 2.0);
        var roi = new Rect(x, y, newW, newH);
        var safeRoi = new Rect(
            Math.Max(0, roi.X),
            Math.Max(0, roi.Y),
            Math.Min(roi.Width, img.Width - Math.Max(0, roi.X)),
            Math.Min(roi.Height, img.Height - Math.Max(0, roi.Y)));
        if (safeRoi.Width <= 0 || safeRoi.Height <= 0) return;

        var srcRoi = new Rect(
            safeRoi.X - roi.X,
            safeRoi.Y - roi.Y,
            safeRoi.Width,
            safeRoi.Height);
        using var srcPart = new Mat(rotated, srcRoi);
        using var dstPart = new Mat(img, safeRoi);
        srcPart.CopyTo(dstPart);
    }

    public void Dispose()
    {
        _matcher.Dispose();
        GC.SuppressFinalize(this);
    }
}
