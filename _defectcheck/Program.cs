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

        var user = matcher.Match(4, -180, 180, 1, 0.5, 0.99, 64);
        Save(src, user, outDir, "compare_user.jpg",
            "user params: pyramid=4 angle=-180~180 overlap=0.99 topN=64");

        var preset = matcher.Match(0, 0, 0, 1, 0.5, 0.5, 999);
        Save(src, preset, outDir, "compare_array_preset.jpg",
            "array preset: pyramid=0 angle=0 overlap=0.5 topN=999");

        Console.WriteLine($"user={user.Count}  preset={preset.Count}");
    }

    static void Save(Mat src, List<MatchResult> results, string outDir, string filename, string caption)
    {
        var vis = src.Clone();
        foreach (var r in results)
            DrawRotatedRect(vis, r, new Scalar(0, 255, 0), 2);
        Cv2.PutText(vis, $"{caption} => {results.Count} matches",
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
