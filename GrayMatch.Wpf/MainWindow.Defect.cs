using System.Windows;
using System.Windows.Controls;

namespace GrayMatch.Wpf;

/// <summary>
/// Defect-detection parameter panel.
///
/// Lives in its own partial file so the tuning logic stays separate from the main window
/// plumbing. The values are pushed into <see cref="RotatedTemplateMatcher.DefectOptions"/>
/// rather than passed at the call site, which keeps <c>RunMatchAsync</c> untouched: its
/// existing <c>_matcher.DetectDefects(results)</c> call resolves to the overload that reads
/// these options.
/// </summary>
public partial class MainWindow
{
    // Panel defaults, kept in one place so the XAML, the reset button and the parse fallbacks
    // can never drift apart.
    private const string DefDiff = "45";
    private const string DefEdgeTol = "0";
    private const string DefEdgeGrad = "30";
    private const string DefErode = "2";
    private const string DefDilate = "3";
    private const string DefMinAreaPct = "0.4";
    private const string DefGlobal = "28";

    /// <summary>
    /// Reads the panel and pushes it into the matcher.
    ///
    /// Every value is clamped to a range the detector can actually use, so a typo cannot
    /// silently produce a nonsense mask — a 99-px erode kernel would erase every defect and
    /// look like "detection stopped working".
    ///
    /// 最小面积占比 is shown as a percentage because that reads better in the UI; the detector
    /// wants a fraction of the template area, hence the /100.
    /// </summary>
    private void ApplyDefectParams()
    {
        // TextChanged fires while InitializeComponent is still constructing the panel, so the
        // later textboxes can still be null on the first few callbacks.
        if (TbDefectDiff == null || TbDefectEdgeTol == null || TbDefectEdgeGrad == null ||
            TbDefectErode == null || TbDefectDilate == null || TbDefectMinArea == null ||
            TbDefectGlobal == null)
            return;

        double minAreaPct = Clamp(Parse(TbDefectMinArea.Text, 0.4), 0.001, 50);

        _matcher.DefectOptions = new DefectOptions
        {
            DiffThreshold = Clamp(Parse(TbDefectDiff.Text, 45), 1, 254),
            MinAreaFrac = minAreaPct / 100.0,
            GlobalBrightnessThresh = Clamp(Parse(TbDefectGlobal.Text, 28), 1, 254),
            EdgeTolerance = (int)Clamp(Parse(TbDefectEdgeTol.Text, 0), 0, 30),
            EdgeGradThresh = Clamp(Parse(TbDefectEdgeGrad.Text, 30), 1, 254),
            ErodeSize = (int)Clamp(Parse(TbDefectErode.Text, 2), 0, 15),
            DilateSize = (int)Clamp(Parse(TbDefectDilate.Text, 3), 0, 15),
        };
    }

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

    /// <summary>Any defect textbox edited — re-read the whole panel (cheap, and keeps it simple).</summary>
    private void DefectParam_Changed(object sender, TextChangedEventArgs e) => ApplyDefectParams();

    private void BtnDefectReset_Click(object sender, RoutedEventArgs e)
    {
        TbDefectDiff.Text = DefDiff;
        TbDefectEdgeTol.Text = DefEdgeTol;
        TbDefectEdgeGrad.Text = DefEdgeGrad;
        TbDefectErode.Text = DefErode;
        TbDefectDilate.Text = DefDilate;
        TbDefectMinArea.Text = DefMinAreaPct;
        TbDefectGlobal.Text = DefGlobal;
        ApplyDefectParams();
        StatusText = "缺陷参数已恢复默认";
    }
}
