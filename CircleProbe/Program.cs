// CircleProbe — 专门验证「圆里的小圆」这类旋转对称模板的角度问题。
// 1) 自动找出盘上的小圆孔（外圈孔带 + 中心螺栓孔），拿其中一个当作模板
// 2) 用与界面完全相同的参数跑 native 匹配，打印每个命中的位置/角度/分数
// 3) 对模板做自旋转 NCC 扫描，量化「模板本身有多少重旋转对称」
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
        Mat color = Cv2.ImRead(path, ImreadModes.Color);
        Console.WriteLine($"image: {gray.Width} x {gray.Height}");

        if (mode == "shape")
        {
            RunShapeTests();
            return;
        }

        var m = new RotatedTemplateMatcher();
        m.LoadSource(path);

        // 找小圆孔：白底上的黑洞（这里盘是深色、孔透白底）
        var holes = FindRoundHoles(gray);
        Console.WriteLine($"round holes found: {holes.Count}");
        foreach (var h in holes.Take(12))
            Console.WriteLine($"  hole r={h.Radius:F1} center=({h.Center.X:F0},{h.Center.Y:F0})");

        string outDir = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "out");
        Directory.CreateDirectory(outDir);

        // 取三个代表：外圈小孔（最小）、中等孔、中心螺栓孔
        var picks = new List<(string Name, Rect R)>();
        if (holes.Count > 0)
        {
            var sorted = holes.OrderBy(h => h.Radius).ToList();
            picks.Add(("HOLE-SMALL", HoleRoi(sorted.First(), gray.Size())));
            picks.Add(("HOLE-MEDIAN", HoleRoi(sorted[sorted.Count / 2], gray.Size())));
            picks.Add(("HOLE-LARGE", HoleRoi(sorted.Last(), gray.Size())));
        }

        foreach (var p in picks)
        {
            Console.WriteLine();
            Console.WriteLine("================================================================");
            Console.WriteLine($"[{p.Name}] roi={p.R.X},{p.R.Y} {p.R.Width}x{p.R.Height}");
            var tpl = new Mat(gray, p.R).Clone();
            PrintSymmetry(tpl);

            if (mode is "all" or "match")
            {
                var res = RunNative(m, p.R);
                Draw(color, p.R, res, Path.Combine(outDir, p.Name + ".png"));
            }
        }
    }

    // 合成形状：验证"对称重数"判定本身对不对
    private static void RunShapeTests()
    {
        var cases = new (string Name, int Sides)[]
        {
            ("circle", 0), ("square", 4), ("hexagon", 6), ("triangle", 3),
            ("pentagon", 5), ("cross", 0), ("random", -1),
        };
        foreach (var c in cases)
        {
            using var img = MakeShape(c.Name, 120);
            var sym = TemplateSymmetryAnalyzer.Analyze(img);
            Console.WriteLine($"{c.Name,-9} Order={sym.Order,-3} 45={sym.Ncc45:F3} 90={sym.Ncc90:F3} 180={sym.Ncc180:F3} " +
                              $"peak={sym.PeakAngle}deg/{sym.PeakNcc:F3}  -> {sym.Describe()}");
            var samples = new[] { 0.0, 15, 30, 45, 90, 120, 179, -170 };
            Console.WriteLine("           folded(-180..180): " +
                              string.Join("  ", samples.Select(a => $"{a:0}->{sym.Canonicalize(a):0}")));
            Console.WriteLine("           folded(0..180):     " +
                              string.Join("  ", samples.Select(a => $"{a:0}->{sym.Canonicalize(a, 0, 180):0}")));
        }
    }

    private static Mat MakeShape(string name, int size)
    {
        var img = new Mat(size, size, MatType.CV_8UC1, new Scalar(30));
        var c = new Point(size / 2, size / 2);
        int r = (int)(size * 0.36);
        switch (name)
        {
            case "circle":
                Cv2.Circle(img, c, r, new Scalar(230), -1, LineTypes.AntiAlias);
                Cv2.Circle(img, c, r / 3, new Scalar(30), -1, LineTypes.AntiAlias);
                break;
            case "square":
                Cv2.Rectangle(img, new Rect(c.X - r, c.Y - r, r * 2, r * 2), new Scalar(230), -1);
                Cv2.Circle(img, c, r / 3, new Scalar(30), -1, LineTypes.AntiAlias);
                break;
            case "hexagon":
                Cv2.FillConvexPoly(img, Polygon(c, r, 6, 30), new Scalar(230));
                Cv2.Circle(img, c, r / 3, new Scalar(30), -1, LineTypes.AntiAlias);
                break;
            case "triangle":
                Cv2.FillConvexPoly(img, Polygon(c, r, 3, -90), new Scalar(230));
                Cv2.Circle(img, c, r / 3, new Scalar(30), -1, LineTypes.AntiAlias);
                break;
            case "pentagon":
                Cv2.FillConvexPoly(img, Polygon(c, r, 5, -90), new Scalar(230));
                Cv2.Circle(img, c, r / 3, new Scalar(30), -1, LineTypes.AntiAlias);
                break;
            case "cross":
                Cv2.Rectangle(img, new Rect(c.X - r, c.Y - r / 3, r * 2, r * 2 / 3), new Scalar(230), -1);
                Cv2.Rectangle(img, new Rect(c.X - r / 3, c.Y - r, r * 2 / 3, r * 2), new Scalar(230), -1);
                break;
            default:
                var rnd = new Random(7);
                for (int i = 0; i < 6; i++)
                {
                    var p1 = new Point(c.X + rnd.Next(-r, r), c.Y + rnd.Next(-r, r));
                    var p2 = new Point(c.X + rnd.Next(-r, r), c.Y + rnd.Next(-r, r));
                    Cv2.Line(img, p1, p2, new Scalar(230), rnd.Next(3, 10), LineTypes.AntiAlias);
                }
                break;
        }
        return img;
    }

    private static Point[] Polygon(Point c, int r, int sides, double startDeg)
    {
        var pts = new Point[sides];
        for (int i = 0; i < sides; i++)
        {
            double a = (startDeg + 360.0 * i / sides) * Math.PI / 180.0;
            pts[i] = new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
        }
        return pts;
    }

    private static List<MatchResult> RunNative(RotatedTemplateMatcher m, Rect roi)
    {
        m.SetTemplateFromRoi(roi);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var sym = m.TemplateSymmetry;
        sw.Stop();
        Console.WriteLine($"-- 库判定({sw.Elapsed.TotalMilliseconds:F1}ms): Order={sym.Order}  45deg={sym.Ncc45:F3} 90deg={sym.Ncc90:F3} 180deg={sym.Ncc180:F3} " +
                          $"peak={sym.PeakAngle}deg/{sym.PeakNcc:F3}  => {sym.Describe()}");
        var res = m.Match(4, -180, 180, 1, 0.50, 0.10, 291, 1);
        Console.WriteLine($"-- native: hits={res.Count} time={m.LastMatchMs:F1} ms --");

        Console.WriteLine("  top hits (score desc):");
        foreach (var r in res.Take(15))
            Console.WriteLine($"    c=({r.CenterX,7:F1},{r.CenterY,7:F1}) angle={r.Angle,8:F1} score={r.Score:F4} box={r.TemplateWidth}x{r.TemplateHeight}");

        // 同一个位置上的不同角度
        var groups = GroupByCenter(res, Math.Max(4.0, Math.Min(roi.Width, roi.Height) * 0.35));
        var multi = groups.Where(g => g.Count > 1).OrderByDescending(g => g.Count).ToList();
        Console.WriteLine($"  distinct centers={groups.Count}, centers with >1 angle={multi.Count}");
        foreach (var g in multi.Take(5))
            Console.WriteLine($"    ({g[0].CenterX:F0},{g[0].CenterY:F0}): " +
                              string.Join(" | ", g.Select(x => $"{x.Angle:F1}deg/{x.Score:F3}")));

        // 角度直方图（10 度一档）
        var hist = res.GroupBy(r => (int)Math.Floor((r.Angle + 180) / 10.0) * 10 - 180)
                      .OrderByDescending(g => g.Count()).Take(8);
        Console.WriteLine("  angle histogram: " + string.Join("  ", hist.Select(g => $"[{g.Key},{g.Key + 10})={g.Count()}")));
        return res;
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

    // 自旋转对称性：把模板绕中心转 g 度后与自身做 NCC（只统计内切圆内的像素）
    private static void PrintSymmetry(Mat tpl)
    {
        Mat small = tpl;
        int maxDim = Math.Max(tpl.Width, tpl.Height);
        if (maxDim > 96)
        {
            double f = 96.0 / maxDim;
            small = new Mat();
            Cv2.Resize(tpl, small, new OpenCvSharp.Size(0, 0), f, f, InterpolationFlags.Area);
        }
        int w = small.Width, h = small.Height;
        var center = new Point2f((w - 1) / 2f, (h - 1) / 2f);
        double rad = Math.Min(w, h) * 0.47;
        var mask = new byte[w * h];
        int n = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double dx = x - center.X, dy = y - center.Y;
                if (dx * dx + dy * dy <= rad * rad) { mask[y * w + x] = 1; n++; }
            }

        byte[] basePix = ToBytes(small);
        var profile = new double[181];
        var rot = new Mat();
        for (int a = 0; a <= 180; a++)
        {
            if (a == 0) { profile[0] = 1; continue; }
            using var mm = Cv2.GetRotationMatrix2D(center, a, 1.0);
            Cv2.WarpAffine(small, rot, mm, new OpenCvSharp.Size(w, h), InterpolationFlags.Linear, BorderTypes.Replicate);
            profile[a] = Ncc(basePix, ToBytes(rot), mask, n);
        }

        var peaks = new List<(int Ang, double S)>();
        for (int a = 1; a < 180; a++)
            if (profile[a] >= profile[a - 1] && profile[a] >= profile[a + 1] && profile[a] >= 0.80)
                peaks.Add((a, profile[a]));
        var top = peaks.OrderByDescending(p => p.S).Take(6).ToList();

        double minProf = double.MaxValue;
        for (int a = 1; a <= 180; a++) minProf = Math.Min(minProf, profile[a]);
        Console.WriteLine($"-- self-rotation ({w}x{h}, r={rad:F1})");
        Console.WriteLine("   curve: " + string.Join(" ", new[] { 5, 10, 15, 22, 30, 45, 60, 72, 90, 120, 150, 180 }
                                    .Select(a => $"{a}deg={profile[a]:F3}")));
        Console.WriteLine($"   min over 1..180 = {minProf:F3}   (>=0.97 全角度 => 圆形对称，角度无意义)");
        Console.WriteLine("   local maxima >=0.80: " + string.Join("  ", top.Select(p => $"{p.Ang}deg:{p.S:F3}")));
        var first = peaks.Where(p => p.S >= 0.97).OrderBy(p => p.Ang).FirstOrDefault();
        if (first.Ang > 0)
            Console.WriteLine($"   smallest period {first.Ang}deg (ncc {first.S:F3}) => {360.0 / first.Ang:F2}-fold");
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

    private sealed record Hole(Point2d Center, double Radius, Rect BBox);

    // 孔是白底（>=200）里的暗圆：先取亮区，再挑接近圆形的小连通域
    private static List<Hole> FindRoundHoles(Mat gray)
    {
        using var bright = new Mat();
        Cv2.Threshold(gray, bright, 180, 255, ThresholdTypes.Binary);
        Cv2.FindContours(bright, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var list = new List<Hole>();
        foreach (var c in contours)
        {
            var bbox = Cv2.BoundingRect(c);
            double area = Cv2.ContourArea(c);
            double per = Cv2.ArcLength(c, true);
            if (per <= 0) continue;
            double circ = 4 * Math.PI * area / (per * per);
            double r = Math.Sqrt(area / Math.PI);
            if (circ < 0.75 || r < 3 || r > 40) continue;
            if (bbox.Width > bbox.Height * 1.4 || bbox.Height > bbox.Width * 1.4) continue;
            // 只要完整落在图像内部的孔，避免边缘裁切把模板变成一条白边
            if (bbox.X < 40 || bbox.Y < 40 ||
                bbox.X + bbox.Width > gray.Width - 40 || bbox.Y + bbox.Height > gray.Height - 40) continue;
            list.Add(new Hole(new Point2d(bbox.X + bbox.Width / 2.0, bbox.Y + bbox.Height / 2.0), r, bbox));
        }
        return list;
    }

    private static Rect HoleRoi(Hole h, OpenCvSharp.Size sz)
    {
        // 正方形 ROI，孔居中，四周留一圈盘面（模板就是"小圆 + 一圈背景"）
        int side = (int)Math.Round(h.Radius * 3.2);
        side = Math.Max(12, Math.Min(side, Math.Min(sz.Width, sz.Height)));
        int x = (int)Math.Round(h.Center.X) - side / 2;
        int y = (int)Math.Round(h.Center.Y) - side / 2;
        x = Math.Max(0, Math.Min(x, sz.Width - side));
        y = Math.Max(0, Math.Min(y, sz.Height - side));
        return new Rect(x, y, side, side);
    }

    private static void Draw(Mat color, Rect roi, List<MatchResult> res, string path)
    {
        var canvas = color.Clone();
        Cv2.Rectangle(canvas, roi, new Scalar(0, 255, 255), 3);
        foreach (var r in res.Take(40))
        {
            var rr = new RotatedRect(new Point2f((float)r.CenterX, (float)r.CenterY),
                                     new Size2f(r.TemplateWidth, r.TemplateHeight), (float)r.Angle);
            var pts = rr.Points();
            for (int i = 0; i < 4; i++)
                Cv2.Line(canvas, (Point)pts[i], (Point)pts[(i + 1) % 4], new Scalar(0, 200, 0), 2);
            Cv2.PutText(canvas, $"{r.Angle:F0}", new Point((int)r.CenterX - 12, (int)r.CenterY - 10),
                        HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 120, 255), 2);
        }
        Cv2.ImWrite(path, canvas);
        Console.WriteLine("  saved " + path);
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
