// AngleProbe - diagnose why a rotation-symmetric part (brake disc) produces
// wildly different angles for the "same" detection.
//
// Experiment 1: self-rotation NCC. Rotate the template against itself and
//   measure Pearson NCC inside an inscribed circle. Peaks at non-zero angles
//   mean the template is rotation-symmetric -> the angle is NOT uniquely
//   determined, regardless of the matcher.
// Experiment 2: the real native matcher (same call the WPF UI makes) on a few
//   typical hand-drawn ROIs, printing every hit's center + angle.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media.Imaging;
using GrayMatch;
using OpenCvSharp;

internal static class Program
{
    private static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string path = args.Length > 0 ? args[0] : @"d:\wqz\code\GrayMatch\brake.webp";
        string mode = args.Length > 1 ? args[1] : "all";

        Mat gray = LoadGray(path);
        Console.WriteLine($"image: {gray.Width} x {gray.Height}");
        Mat color = Cv2.ImRead(path, ImreadModes.Color);
        string outDir = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "out");
        Directory.CreateDirectory(outDir);

        var m = new RotatedTemplateMatcher();
        m.LoadSource(path);

        Rect disc = FindDisc(gray);
        Console.WriteLine($"disc bbox: x={disc.X} y={disc.Y} w={disc.Width} h={disc.Height}");
        Console.WriteLine();

        int cx = disc.X + disc.Width / 2, cy = disc.Y + disc.Height / 2;

        var rois = new (string Name, Rect R)[]
        {
            ("WHOLE-DISC",        Clamp(new Rect(disc.X + 6, disc.Y + 6, disc.Width - 12, disc.Height - 12), gray.Size())),
            ("CENTER-45%",        Clamp(Centered(cx, cy, (int)(disc.Width * 0.45), (int)(disc.Height * 0.45)), gray.Size())),
            ("CENTER-25%",        Clamp(Centered(cx, cy, (int)(disc.Width * 0.25), (int)(disc.Height * 0.25)), gray.Size())),
            ("CENTER-15%",        Clamp(Centered(cx, cy, (int)(disc.Width * 0.15), (int)(disc.Height * 0.15)), gray.Size())),
            ("OUTER-HOLES-ARC",   Clamp(new Rect(disc.X + (int)(disc.Width * 0.72), disc.Y + (int)(disc.Height * 0.34), (int)(disc.Width * 0.22), (int)(disc.Height * 0.30)), gray.Size())),
            ("ONE-HOLE-CLUSTER",  Clamp(new Rect(disc.X + (int)(disc.Width * 0.76), disc.Y + (int)(disc.Height * 0.05), (int)(disc.Width * 0.16), (int)(disc.Height * 0.16)), gray.Size())),
        };

        foreach (var roi in rois)
        {
            Console.WriteLine("================================================================");
            Console.WriteLine($"[{roi.Name}] roi={roi.R.X},{roi.R.Y} {roi.R.Width}x{roi.R.Height}");
            var tpl = new Mat(gray, roi.R).Clone();

            if (mode is "all" or "self")
            {
                Console.WriteLine("-- self-rotation NCC (template vs rotated template, inscribed circle) --");
                PrintSelfRotationPeaks(tpl);
            }

            if (mode is "all" or "match")
            {
                Console.WriteLine("-- native matcher (pyramid 4, -180..180 step 1, thr 0.50, overlap 0.10, topN 291, dense) --");
                RunNative(m, roi.R);
                DrawResults(color, roi.R, m, Path.Combine(outDir, roi.Name + ".png"));
            }
            Console.WriteLine();
        }
    }

    // ---------------------------------------------------------------- native

    private static void RunNative(RotatedTemplateMatcher m, Rect roi)
    {
        m.SetTemplateFromRoi(roi);
        var sym = m.TemplateSymmetry;
        Console.WriteLine($"symmetry: Order={sym.Order} 45={sym.Ncc45:F3} 90={sym.Ncc90:F3} 180={sym.Ncc180:F3} -> {sym.Describe()}");
        var res = m.Match(4, -180, 180, 1, 0.50, 0.10, 291, 1);
        Console.WriteLine($"hits={res.Count}  time={m.LastMatchMs:F1} ms");

        foreach (var r in res.Take(14))
            Console.WriteLine($"  c=({r.CenterX,7:F1},{r.CenterY,7:F1})  angle={r.Angle,8:F1}  score={r.Score:F4}  lvl={r.Level}  box={r.TemplateWidth}x{r.TemplateHeight}");

        // how many distinct angles sit on top of the SAME center (+/-10 px)?
        var groups = GroupByCenter(res, 10.0);
        var multi = groups.Where(g => g.Count > 1).OrderByDescending(g => g.Count).ToList();
        Console.WriteLine($"distinct centers={groups.Count}, centers carrying >1 angle={multi.Count}");
        foreach (var g in multi.Take(6))
            Console.WriteLine($"  center ({g[0].CenterX:F0},{g[0].CenterY:F0}): {g.Count} angles = " +
                              string.Join(", ", g.Select(x => x.Angle.ToString("F1"))) +
                              $"  scores={string.Join(",", g.Select(x => x.Score.ToString("F3")))}");
    }

    private static List<List<MatchResult>> GroupByCenter(List<MatchResult> res, double tol)
    {
        var groups = new List<List<MatchResult>>();
        foreach (var r in res)
        {
            List<MatchResult> target = null;
            foreach (var g in groups)
            {
                var f = g[0];
                double dx = f.CenterX - r.CenterX, dy = f.CenterY - r.CenterY;
                if (dx * dx + dy * dy <= tol * tol) { target = g; break; }
            }
            if (target == null) { target = new List<MatchResult>(); groups.Add(target); }
            target.Add(r);
        }
        return groups;
    }

    private static void DrawResults(Mat color, Rect roi, RotatedTemplateMatcher m, string path)
    {
        var res = m.Match(4, -180, 180, 1, 0.50, 0.10, 291, 1);
        var canvas = color.Clone();
        Cv2.Rectangle(canvas, roi, new Scalar(0, 255, 255), 5);
        Cv2.PutText(canvas, "TEMPLATE", new Point(roi.X + 10, roi.Y + 40),
                    HersheyFonts.HersheySimplex, 1.2, new Scalar(0, 255, 255), 3);

        foreach (var r in res)
        {
            var rr = new RotatedRect(new Point2f((float)r.CenterX, (float)r.CenterY),
                                     new Size2f(r.TemplateWidth, r.TemplateHeight), (float)r.Angle);
            var pts = rr.Points();
            for (int i = 0; i < 4; i++)
                Cv2.Line(canvas, (Point)pts[i], (Point)pts[(i + 1) % 4], new Scalar(0, 255, 0), 3);
            Cv2.PutText(canvas, $"{r.Angle:F0}deg {r.Score:F2}",
                        new Point((int)r.CenterX - 70, (int)r.CenterY - 12),
                        HersheyFonts.HersheySimplex, 0.9, new Scalar(0, 255, 0), 2);
        }

        Cv2.ImWrite(path, canvas);
        Console.WriteLine("  saved " + path);
    }

    // ----------------------------------------------------------- self rotation

    private static void PrintSelfRotationPeaks(Mat tpl)
    {
        Mat small = tpl;
        bool scaled = false;
        int maxDim = Math.Max(tpl.Width, tpl.Height);
        if (maxDim > 220)
        {
            double f = 220.0 / maxDim;
            small = new Mat();
            Cv2.Resize(tpl, small, new OpenCvSharp.Size(0, 0), f, f, InterpolationFlags.Area);
            scaled = true;
        }
        int w = small.Width, h = small.Height;
        var center = new Point2f((w - 1) / 2f, (h - 1) / 2f);

        // inscribed circle mask
        var mask = new byte[w * h];
        double rad = Math.Min(w, h) * 0.48;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double dx = x - center.X, dy = y - center.Y;
                if (dx * dx + dy * dy <= rad * rad) mask[y * w + x] = 1;
            }
        int inCount = 0;
        for (int i = 0; i < mask.Length; i++) if (mask[i] != 0) inCount++;

        byte[] basePix = ToBytes(small);
        var scores = new double[361];
        var rot = new Mat();
        byte[] rotPix = new byte[w * h];

        for (int a = 0; a <= 360; a++)
        {
            if (a == 0) { Array.Copy(basePix, rotPix, basePix.Length); }
            else
            {
                using var mm = Cv2.GetRotationMatrix2D(center, a, 1.0);
                Cv2.WarpAffine(small, rot, mm, new OpenCvSharp.Size(w, h), InterpolationFlags.Linear, BorderTypes.Replicate);
                rotPix = ToBytes(rot);
            }
            scores[a] = Ncc(basePix, rotPix, mask, inCount);
        }

        Console.WriteLine($"  ({(scaled ? "resized to " : "size ")}{w}x{h}, circular px={inCount})");
        // every local maximum, best first - not just the >=0.90 ones
        var peaks = new List<(int Ang, double S)>();
        for (int a = 0; a <= 360; a++)
        {
            double prev = scores[(a - 1 + 361) % 361], next = scores[(a + 1) % 361];
            if (scores[a] >= prev && scores[a] >= next && scores[a] >= 0.50)
                peaks.Add((a, scores[a]));
        }
        var merged = new List<(int Ang, double S)>();
        foreach (var p in peaks)
        {
            if (merged.Count > 0 && Math.Abs(p.Ang - merged[^1].Ang) <= 2)
            {
                if (p.S > merged[^1].S) merged[^1] = p;
                continue;
            }
            merged.Add(p);
        }
        // drop the wrap-around duplicate (0 and 360 are the same pose)
        if (merged.Count > 1 && merged[^1].Ang == 360)
        {
            if (merged[^1].S > merged[0].S) merged[0] = merged[^1];
            merged.RemoveAt(merged.Count - 1);
        }

        var top = merged.OrderByDescending(p => p.S).Take(8).ToList();
        Console.WriteLine($"  local maxima(>=0.50)={merged.Count}, top: " +
                          string.Join("  ", top.Select(p => $"{p.Ang}deg:{p.S:F3}")));

        var gaps = new List<double>();
        var strong = merged.Where(p => p.S >= 0.90).OrderBy(p => p.Ang).ToList();
        for (int i = 1; i < strong.Count; i++) gaps.Add(strong[i].Ang - strong[i - 1].Ang);
        if (gaps.Count == 0)
            Console.WriteLine("  -> only one pose >=0.90: angle mostly determined by this ROI");
        else
            Console.WriteLine($"  -> {strong.Count + 1} equivalent poses (>=0.90), mean gap={gaps.Average():F1} deg " +
                              $"({360.0 / gaps.Average():F1}-fold): the SAME patch matches several angles");
    }

    private static byte[] ToBytes(Mat gray8)
    {
        var buf = new byte[gray8.Width * gray8.Height];
        if (gray8.Step() == gray8.Width) Marshal.Copy(gray8.Data, buf, 0, buf.Length);
        else
            for (int y = 0; y < gray8.Height; y++)
                Marshal.Copy(gray8.Data + y * (int)gray8.Step(), buf, y * gray8.Width, gray8.Width);
        return buf;
    }

    private static double Ncc(byte[] a, byte[] b, byte[] mask, int n)
    {
        if (n == 0) return 0;
        double sa = 0, sb = 0, saa = 0, sbb = 0, sab = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (mask[i] == 0) continue;
            double va = a[i], vb = b[i];
            sa += va; sb += vb; saa += va * va; sbb += vb * vb; sab += va * vb;
        }
        double ma = sa / n, mb = sb / n;
        double va2 = saa / n - ma * ma, vb2 = sbb / n - mb * mb;
        if (va2 <= 1e-9 || vb2 <= 1e-9) return 0;
        return (sab / n - ma * mb) / Math.Sqrt(va2 * vb2);
    }

    // ------------------------------------------------------------- utilities

    private static Rect FindDisc(Mat gray)
    {
        using var bw = new Mat();
        Cv2.Threshold(gray, bw, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
        Cv2.FindContours(bw, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        Rect best = new(0, 0, gray.Width, gray.Height);
        double bestArea = 0;
        foreach (var c in contours)
        {
            double a = Cv2.ContourArea(c);
            if (a > bestArea) { bestArea = a; best = Cv2.BoundingRect(c); }
        }
        return Clamp(best, gray.Size());
    }

    private static Rect Centered(int cx, int cy, int w, int h) => new(cx - w / 2, cy - h / 2, w, h);

    private static Rect Clamp(Rect r, OpenCvSharp.Size sz)
    {
        int x = Math.Max(0, Math.Min(r.X, sz.Width - 4));
        int y = Math.Max(0, Math.Min(r.Y, sz.Height - 4));
        int w = Math.Min(r.Width, sz.Width - x);
        int h = Math.Min(r.Height, sz.Height - y);
        return new Rect(x, y, Math.Max(4, w), Math.Max(4, h));
    }

    private static Mat LoadGray(string path)
    {
        var m = Cv2.ImRead(path, ImreadModes.Grayscale);
        if (m is { IsDisposed: false } && !m.Empty()) return m;

        using var fs = File.OpenRead(path);
        var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = dec.Frames[0];
        var gray = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Gray8, null, 0);
        int w = gray.PixelWidth, h = gray.PixelHeight;
        var buf = new byte[w * h];
        gray.CopyPixels(buf, w, 0);
        var mat = new Mat(h, w, MatType.CV_8UC1);
        Marshal.Copy(buf, 0, mat.Data, buf.Length);
        return mat;
    }
}
