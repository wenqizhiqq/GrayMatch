// ============================================================
// 温启志◆编写◇微信﹕187◆1936◇1399
// ============================================================
// 模板旋转对称性分析
// ------------------------------------------------------------
// 旋转不变 NCC 会把模板在每个角度上都试一遍。如果模板本身旋转对称
// （圆形孔、方形孔、六角螺母……），那么"转 90°"和"不转"看起来一模一样，
// 分数几乎相同 —— 此时匹配器输出的角度就只是噪声：同一个孔这次报 0°，
// 下次报 -90°，再下次报 111°，但分数都是 0.99。
//
// 这里在模板建立时算一次"自旋转 NCC 曲线"：
//   profile[g] = 模板绕中心转 g 度后与自身的 NCC（只统计内切圆内的像素，
//                避开正方形画布四角的干扰）
// 然后据此判定对称重数：
//   Order = 0  → 圆形对称（连续对称），角度不可测
//   Order = n  → n 重对称，角度只能确定到 360/n 的范围内
//   Order = 1  → 角度唯一，正常输出
// 匹配结束后据此把角度归一化（对称模板统一报 0°，n 重对称取模），
// 这样同一类目标的角度读数就稳定了。
using OpenCvSharp;

namespace GrayMatch;

/// <summary>模板的旋转对称性。</summary>
public sealed class TemplateSymmetry
{
    /// <summary>1 = 角度唯一；n = n 重旋转对称；0 = 圆形对称（角度不可测）。</summary>
    public int Order { get; init; } = 1;

    /// <summary>等价角度周期（度）。Order=0 时无意义，取 360。</summary>
    public double PeriodDeg => Order > 1 ? 360.0 / Order : 360.0;

    /// <summary>转 45° / 90° / 180° 时的自相关分数，用于说明判定依据。</summary>
    public double Ncc45 { get; init; } = 1.0;
    public double Ncc90 { get; init; } = 1.0;
    public double Ncc180 { get; init; } = 1.0;

    /// <summary>最早出现的等价周期（度）及其自相关分数。</summary>
    public double PeakAngle { get; init; }
    public double PeakNcc { get; init; }

    /// <summary>模板几乎没有灰度起伏（纯色块），任何角度都"像"，角度同样不可测。</summary>
    public bool NoTexture { get; init; }

    /// <summary>角度是否不唯一（对称模板）。</summary>
    public bool IsAmbiguous => Order != 1;

    /// <summary>短标签，画在目标框上（"圆形对称"/"4重对称"/"无纹理"）。</summary>
    public string ShortLabel => Order switch
    {
        0 when NoTexture => "无纹理",
        0 => "圆形对称",
        > 1 => $"{Order}重对称",
        _ => ""
    };

    /// <summary>一句话中文说明，UI 状态栏 / 提示用。</summary>
    public string Describe() => Order switch
    {
        0 when NoTexture => "几乎无纹理（纯色块），角度不可测",
        0 => "圆形对称：转到任何角度都几乎一样，角度不可测",
        > 1 => $"{Order} 重对称：每 {PeriodDeg:0.#}° 重复一次，角度只能确定到这个范围内",
        _ => "角度唯一"
    };

    /// <summary>
    /// 把角度按对称周期归一化：圆形对称直接归零；n 重对称取模到 [-period/2, period/2)。
    /// 若该值落在用户搜索区间外，再平移一个周期落回区间内（例如只搜 0~180 时不会报负角度）。
    /// </summary>
    public double Canonicalize(double angleDeg, double rangeStart = -180, double rangeEnd = 180)
    {
        if (Order == 0) return 0.0;
        if (Order <= 1) return angleDeg;

        double p = PeriodDeg;
        double a = angleDeg - p * Math.Round(angleDeg / p);   // [-p/2, p/2)
        if (a >= rangeStart - 1e-9 && a <= rangeEnd + 1e-9) return a;

        double up = a + p, down = a - p;
        bool upOk = up >= rangeStart - 1e-9 && up <= rangeEnd + 1e-9;
        bool downOk = down >= rangeStart - 1e-9 && down <= rangeEnd + 1e-9;
        if (upOk && downOk) return Math.Abs(up) <= Math.Abs(down) ? up : down;
        if (upOk) return up;
        if (downOk) return down;
        return a;
    }
}

internal static class TemplateSymmetryAnalyzer
{
    private const int MaxSide = 96;          // 分析用的降采样上限（越大越准，越慢）
    private const double FlatNcc = 0.95;     // 45° 自相关 >= 该值 → 视为圆形对称
    private const double AltNcc = 0.90;      // 其它角度自相关 >= 该值 → 视为等价姿态
    private const double MinStd = 2.0;       // 灰度标准差低于该值 → 纹理不足，不做判定

    public static TemplateSymmetry Analyze(Mat? template)
    {
        if (template == null || template.IsDisposed || template.Empty())
            return new TemplateSymmetry();

        Mat gray = template;
        bool ownsGray = false;
        try
        {
            if (template.Channels() > 1)
            {
                gray = new Mat();
                Cv2.CvtColor(template, gray, template.Channels() == 4
                    ? ColorConversionCodes.BGRA2GRAY
                    : ColorConversionCodes.BGR2GRAY);
                ownsGray = true;
            }

            Mat work = gray;
            bool ownsWork = false;
            try
            {
                int maxDim = Math.Max(gray.Width, gray.Height);
                if (maxDim > MaxSide)
                {
                    work = new Mat();
                    double f = (double)MaxSide / maxDim;
                    Cv2.Resize(gray, work, new OpenCvSharp.Size(0, 0), f, f, InterpolationFlags.Area);
                    ownsWork = true;
                }
                return AnalyzeCore(work);
            }
            finally
            {
                if (ownsWork) work.Dispose();
            }
        }
        finally
        {
            if (ownsGray) gray.Dispose();
        }
    }

    private static TemplateSymmetry AnalyzeCore(Mat tpl)
    {
        int w = tpl.Width, h = tpl.Height;
        if (w < 6 || h < 6) return new TemplateSymmetry();

        int n = w * h;
        var pix = new byte[n];
        if (tpl.Step() == w)
        {
            System.Runtime.InteropServices.Marshal.Copy(tpl.Data, pix, 0, n);
        }
        else
        {
            for (int y = 0; y < h; y++)
                System.Runtime.InteropServices.Marshal.Copy(tpl.Data + y * (int)tpl.Step(), pix, y * w, w);
        }

        // 内切圆掩码 + 灰度统计（纹理太少时任何角度都"像"，不能下结论）
        // 旋转中心必须用像素中心 (w-1)/2：用 w/2 会让奇数尺寸的模板每次都平移半个像素，
        // 自相关被人为压低，圆形也会被误判成"角度唯一"。
        var center = new Point2f((w - 1) / 2f, (h - 1) / 2f);
        double rad = Math.Min(w, h) * 0.45;
        var mask = new byte[n];
        int inCount = 0;
        double sum = 0, sum2 = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double dx = x - center.X, dy = y - center.Y;
                if (dx * dx + dy * dy > rad * rad) continue;
                mask[y * w + x] = 1;
                inCount++;
                double v = pix[y * w + x];
                sum += v; sum2 += v * v;
            }
        }
        if (inCount < 24) return new TemplateSymmetry();
        double mean = sum / inCount;
        double var = sum2 / inCount - mean * mean;
        // 纯色块：NCC 分母接近 0，任何角度都会得到虚高的分数，角度自然不可信。
        if (var <= MinStd * MinStd) return new TemplateSymmetry { Order = 0, NoTexture = true };

        var profile = new double[181];
        profile[0] = 1.0;
        using (var rotated = new Mat())
        {
            for (int a = 1; a <= 180; a++)
            {
                using var m = Cv2.GetRotationMatrix2D(center, a, 1.0);
                Cv2.WarpAffine(tpl, rotated, m, new OpenCvSharp.Size(w, h),
                               InterpolationFlags.Linear, BorderTypes.Replicate);
                profile[a] = MaskedNcc(pix, rotated, mask, inCount);
            }
        }

        double p45 = profile[45], p90 = profile[90], p180 = profile[180];

        // 1) 圆形对称：45° 还很像，90° 也还很像 → 旋转基本不改变外观
        if (p45 >= FlatNcc && p90 >= AltNcc)
            return new TemplateSymmetry { Order = 0, Ncc45 = p45, Ncc90 = p90, Ncc180 = p180 };

        // 2) n 重对称：找出 >=45° 的最早等价峰（45° 以下的高分只是插值平滑，不算周期）
        double peakAngle = 0, peakNcc = 0;
        for (int a = 45; a <= 180; a++)
        {
            double cur = profile[a];
            if (cur < AltNcc) continue;
            bool localMax = (a == 180 || cur >= profile[a + 1]) && cur >= profile[a - 1];
            if (!localMax) continue;
            peakAngle = a;
            peakNcc = cur;
            break;  // 只取最早（周期最小）的那个峰
        }

        int order = 1;
        if (peakAngle > 0)
        {
            double raw = 360.0 / peakAngle;
            int cand = (int)Math.Round(raw);
            if (cand >= 2 && cand <= 12 && Math.Abs(raw - cand) <= 0.15) order = cand;
        }

        return new TemplateSymmetry
        {
            Order = order,
            Ncc45 = p45,
            Ncc90 = p90,
            Ncc180 = p180,
            PeakAngle = peakAngle,
            PeakNcc = peakNcc
        };
    }

    private static double MaskedNcc(byte[] a, Mat b, byte[] mask, int n)
    {
        byte[] bp;
        int bw = b.Width, bh = b.Height;
        if (b.Step() == bw)
        {
            bp = new byte[bw * bh];
            System.Runtime.InteropServices.Marshal.Copy(b.Data, bp, 0, bp.Length);
        }
        else
        {
            bp = new byte[bw * bh];
            for (int y = 0; y < bh; y++)
                System.Runtime.InteropServices.Marshal.Copy(b.Data + y * (int)b.Step(), bp, y * bw, bw);
        }
        if (bp.Length != a.Length) return 0;

        double sa = 0, sb = 0, saa = 0, sbb = 0, sab = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (mask[i] == 0) continue;
            double va = a[i], vb = bp[i];
            sa += va; sb += vb; saa += va * va; sbb += vb * vb; sab += va * vb;
        }
        double ma = sa / n, mb = sb / n;
        double va2 = saa / n - ma * ma, vb2 = sbb / n - mb * mb;
        if (va2 <= 1e-9 || vb2 <= 1e-9) return 0;
        return (sab / n - ma * mb) / Math.Sqrt(va2 * vb2);
    }
}
