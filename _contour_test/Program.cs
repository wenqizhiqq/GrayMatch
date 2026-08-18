using System;
using System.Linq;
using OpenCvSharp;
using GrayMatch;

string imgPath = @"C:\Users\admin\Pictures\Screenshots\图片1300x1000.png";
using var matcher = new RotatedTemplateMatcher();
using var gray = Cv2.ImRead(imgPath, ImreadModes.Grayscale);
matcher.SetSource(gray);
using var src = matcher.Source;
int roiX = 0, roiY = src.Rows * 2 / 3, roiW = src.Cols / 6, roiH = src.Rows / 3;
using var roi = new Mat(src, new Rect(roiX, roiY, roiW, roiH));
using var bin = new Mat();
Cv2.Threshold(roi, bin, 200, 255, ThresholdTypes.Binary);
Cv2.FindContours(bin, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
int best = 0; double bestA = Cv2.ContourArea(contours[0]);
for (int i = 1; i < contours.Length; i++) { double a = Cv2.ContourArea(contours[i]); if (a > bestA) { bestA = a; best = i; } }
var r = Cv2.BoundingRect(contours[best]);
int margin = 4;
int x1 = Math.Max(0, r.X - margin), y1 = Math.Max(0, r.Y - margin);
int x2 = Math.Min(roi.Cols, r.X + r.Width + margin), y2 = Math.Min(roi.Rows, r.Y + r.Height + margin);
matcher.SetTemplateFromRoi(new Rect(roiX + x1, roiY + y1, x2 - x1, y2 - y1));
Console.WriteLine($"Template size: {matcher.Template!.Width}x{matcher.Template.Height}");

matcher.UseContour = true;
foreach (int p in new[] { 0, 3, 5 })
{
    var results = matcher.Match(p, -180, 180, 2.0, 0.25, 0.25, 50);
    var ordered = results.OrderBy(r => r.Angle).ToList();
    Console.WriteLine($"pyramid={p}: count={results.Count} min={(results.Count>0?results.Min(r=>r.Score):0):F3} ms={matcher.LastMatchMs:F1}");
    Console.WriteLine("  " + string.Join(", ", ordered.Take(30).Select(r => $"{r.Angle:F0}°/{r.Score:F2}")));
}
