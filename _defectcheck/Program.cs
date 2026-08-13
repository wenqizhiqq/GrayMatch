using OpenCvSharp;
using GrayMatch;

// Verify the defect-pixel path end-to-end (the feature behind "paint defective pixels red").
// The native matcher's fine-pass score for this high-frequency synthetic scene is low, so we
// feed a manually-constructed MatchResult at the known placement — exactly what the WPF does
// after a successful match — to exercise DetectDefects + the per-pixel -> image-space mapping.

var rnd = new Random(7);
int SW = 500, SH = 400, TW = 80, TH = 60;
int ox = 120, oy = 100;
double cx = ox + TW / 2.0, cy = oy + TH / 2.0;   // true template center

var clean = new Mat(TH, TW, MatType.CV_8UC1, new Scalar(200));
for (int y = 0; y < TH; y++)
    for (int x = 0; x < TW; x++)
        clean.Set<byte>(y, x, (byte)(120 + 80 * rnd.NextDouble()));;

var srcGray = new Mat(SH, SW, MatType.CV_8UC1, new Scalar(60));
clean.CopyTo(new Mat(srcGray, new Rect(ox, oy, TW, TH)));
// injected defects inside the template area
Cv2.Rectangle(srcGray, new Rect(ox + 20, oy + 18, 24, 14), new Scalar(20), -1);   // dark blob
Cv2.Line(srcGray, new Point(ox + 10, oy + 45), new Point(ox + 70, oy + 50), new Scalar(245), 2); // scratch

var srcColor = new Mat();
Cv2.CvtColor(srcGray, srcColor, ColorConversionCodes.GRAY2BGR);

var m = new RotatedTemplateMatcher();
m.SetSource(srcColor);
m.SetTemplate(clean.Clone());

// Simulate a successful match result at the known placement (angle 0).
var result = new MatchResult
{
    Index = 1,
    Score = 0.95,
    CenterX = cx,
    CenterY = cy,
    Angle = 0,
    TemplateWidth = TW,
    TemplateHeight = TH,
    LeftTopX = ox,
    LeftTopY = oy,
    Level = 0
};

var defects = m.DetectDefects(new List<MatchResult> { result });
Console.WriteLine($"defects={defects.Count}");

int totalPixels = 0, inBounds = 0, outOfBounds = 0;
foreach (var d in defects)
{
    if (d.Pixels == null) { Console.WriteLine($"  [{d.Type}] Pixels=null !!!"); continue; }
    int set = 0;
    for (int i = 0; i < d.Pixels.Length; i++) if (d.Pixels[i] != 0) set++;
    totalPixels += set;

    // Replicate the WPF red-paint mapping: upright template-local px -> image space via -angle.
    double phi = -d.Angle * System.Math.PI / 180.0;
    double cosv = System.Math.Cos(phi), sinv = System.Math.Sin(phi);
    for (int ly = 0; ly < d.Ph; ly++)
        for (int lx = 0; lx < d.Pw; lx++)
        {
            if (d.Pixels[ly * d.Pw + lx] == 0) continue;
            double ux = lx - d.Tw / 2.0, uy = ly - d.Th / 2.0;
            int ix = (int)System.Math.Round(d.CenterX + (ux * cosv - uy * sinv));
            int iy = (int)System.Math.Round(d.CenterY + (ux * sinv + uy * cosv));
            if (ix >= 0 && iy >= 0 && ix < SW && iy < SH) inBounds++; else outOfBounds++;
        }
    Console.WriteLine($"  [{d.Type}] sev={d.Score:F1} maskPixels={set} imgC=({d.ImgCx:F0},{d.ImgCy:F0}) Pw={d.Pw} Ph={d.Ph}");
}

Console.WriteLine($"TOTAL mask pixels={totalPixels}  mapped inBounds={inBounds} outOfBounds={outOfBounds}");
Console.WriteLine(outOfBounds == 0 && totalPixels > 0 ? "CHECK_OK" : "CHECK_FAIL");
