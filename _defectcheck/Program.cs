using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GrayMatch;
using OpenCvSharp;

class Program
{
    static void Main()
    {
        string img = @"C:\Users\admin\Pictures\灰度匹配\OIP-C (1).jpeg";
        string outDir = @"D:\wqz\code\GrayMatch\_oipctest_output";
        Directory.CreateDirectory(outDir);

        using var src = Cv2.ImRead(img);
        if (src.Empty()) { Console.WriteLine("FAILED to load image"); return; }

        int tx = 18, ty = 18, tw = 34, th = 34;
        var templ = new Mat(src, new Rect(tx, ty, tw, th)).Clone();

        var matcher = new RotatedTemplateMatcher();
        matcher.LoadSource(img);
        matcher.SetTemplate(templ);

        Console.WriteLine("=== DENSE MODE SWEEP (OIP-C ball array) ===");

        // baseline: pyramid=4 without dense (seed cap 24)
        var p4 = matcher.Match(4, 0, 0, 1, 0.5, 0.5, 999, 0);
        Console.WriteLine($"pyramid=4  dense=0  -> {p4.Count} matches  {matcher.LastMatchMs:F1} ms");

        // dense: pyramid=4 with unbounded coarse NMS
        var p4d = matcher.Match(4, 0, 0, 1, 0.5, 0.5, 999, 1);
        Console.WriteLine($"pyramid=4  dense=1  -> {p4d.Count} matches  {matcher.LastMatchMs:F1} ms");

        // dense: pyramid=2 (less aggressive downsample, better coarse localization)
        var p2d = matcher.Match(2, 0, 0, 1, 0.5, 0.5, 999, 1);
        Console.WriteLine($"pyramid=2  dense=1  -> {p2d.Count} matches  {matcher.LastMatchMs:F1} ms");

        // reference: legacy full-resolution (pyramid=0)
        var p0 = matcher.Match(0, 0, 0, 1, 0.5, 0.5, 999, 0);
        Console.WriteLine($"pyramid=0  (legacy) -> {p0.Count} matches  {matcher.LastMatchMs:F1} ms");

        Console.WriteLine(
            p4d.Count > 1 ? "DENSE_MODE_OK" : "DENSE_MODE_NO_EFFECT");

        Save(src, p4, outDir, "dense_pyr4_off.jpg", "pyramid=4 dense=OFF (cap 24)");
        Save(src, p4d, outDir, "dense_pyr4_on.jpg", "pyramid=4 dense=ON (unbounded NMS)");
        Save(src, p0, outDir, "dense_pyr0_ref.jpg", "pyramid=0 legacy (reference, full scan)");
    }

    static void Save(Mat src, List<MatchResult> results, string outDir, string filename, string caption)
    {
        var vis = src.Clone();
        foreach (var r in results)
            DrawRotatedRect(vis, r, new Scalar(0, 255, 0), 2);
        Cv2.PutText(vis, $"{caption} => {results.Count}",
            new Point(10, 20), HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 0, 255), 1);
        string path = Path.Combine(outDir, filename);
        Cv2.ImWrite(path, vis);
        Console.WriteLine($"saved {path}");
    }

    static void DrawRotatedRect(Mat img, MatchResult r, Scalar color, int thickness)
    {
        double phi = r.Angle * Math.PI / 180.0;
        double cosv = Math.Cos(phi), sinv = Math.Sin(phi);
        double w2 = r.TemplateWidth / 2.0, h2 = r.TemplateHeight / 2.0;
        var pts = new Point[4];
        pts[0] = Rotate(w2, h2, cosv, sinv, r.CenterX, r.CenterY);
        pts[1] = Rotate(-w2, h2, cosv, sinv, r.CenterX, r.CenterY);
        pts[2] = Rotate(-w2, -h2, cosv, sinv, r.CenterX, r.CenterY);
        pts[3] = Rotate(w2, -h2, cosv, sinv, r.CenterX, r.CenterY);
        for (int i = 0; i < 4; i++) Cv2.Line(img, pts[i], pts[(i + 1) % 4], color, thickness);
    }

    static Point Rotate(double ux, double uy, double cosv, double sinv, double cx, double cy)
    {
        return new Point(
            (int)Math.Round(cx + (ux * cosv - uy * sinv)),
            (int)Math.Round(cy + (ux * sinv + uy * cosv)));
    }
}
