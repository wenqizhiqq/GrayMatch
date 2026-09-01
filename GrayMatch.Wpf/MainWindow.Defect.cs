// ============================================================
// 温启志◆编写◇微信﹕187◆1936◇1399
// ============================================================
// ============================================================
// 温启志◆编写◇微信﹕187◆1936◇1399
// ============================================================
// ============================================================
// 温启志◆编写◇微信︕187◆1936◇1399
// ============================================================
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
    /// silently produce a nonsense mask 鈥? a 99-px erode kernel would erase every defect and
    /// look like "detection stopped working".
    ///
    /// 鏈?灏忛潰绉?鍗犳瘮 is shown as a percentage because that reads better in the UI; the detector
    /// wants a fraction of the template area, hence the /100.
    /// </summary>
    private void ApplyDefectParams()
    {
        // TextChanged fires while InitializeComponent is still constructing the panel, so the
        // later textboxes can still be null on the first few callbacks.
        if (SldDefectDiff == null || SldDefectEdgeTol == null || SldDefectEdgeGrad == null ||
            SldDefectErode == null || SldDefectDilate == null || SldDefectMinArea == null ||
            SldDefectGlobal == null)
            return;

        double minAreaPct = Clamp(SldDefectMinArea.Value, 0.001, 50);

        _matcher.DefectOptions = new DefectOptions
        {
            DiffThreshold = (int)Clamp(SldDefectDiff.Value, 1, 254),
            MinAreaFrac = minAreaPct / 100.0,
            GlobalBrightnessThresh = (int)Clamp(SldDefectGlobal.Value, 1, 254),
            EdgeTolerance = (int)Clamp(SldDefectEdgeTol.Value, 0, 30),
            EdgeGradThresh = (int)Clamp(SldDefectEdgeGrad.Value, 1, 254),
            ErodeSize = (int)Clamp(SldDefectErode.Value, 0, 15),
            DilateSize = (int)Clamp(SldDefectDilate.Value, 0, 15),
        };

        if (TbDefectDiffVal != null) TbDefectDiffVal.Text = ((int)SldDefectDiff.Value).ToString();
        if (TbDefectEdgeTolVal != null) TbDefectEdgeTolVal.Text = ((int)SldDefectEdgeTol.Value).ToString();
        if (TbDefectEdgeGradVal != null) TbDefectEdgeGradVal.Text = ((int)SldDefectEdgeGrad.Value).ToString();
        if (TbDefectErodeVal != null) TbDefectErodeVal.Text = ((int)SldDefectErode.Value).ToString();
        if (TbDefectDilateVal != null) TbDefectDilateVal.Text = ((int)SldDefectDilate.Value).ToString();
        if (TbDefectMinAreaVal != null) TbDefectMinAreaVal.Text = SldDefectMinArea.Value.ToString("0.00");
        if (TbDefectGlobalVal != null) TbDefectGlobalVal.Text = ((int)SldDefectGlobal.Value).ToString();
    }

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

    /// <summary>Any defect textbox edited 鈥? re-read the whole panel (cheap, and keeps it simple).</summary>
    private void DefectParam_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressAutoMatch) return;
        ApplyDefectParams();
        ScheduleAutoMatch();
    }

    private void BtnDefectReset_Click(object sender, RoutedEventArgs e)
    {
        SldDefectDiff.Value = 45;
        SldDefectEdgeTol.Value = 0;
        SldDefectEdgeGrad.Value = 30;
        SldDefectErode.Value = 2;
        SldDefectDilate.Value = 3;
        SldDefectMinArea.Value = 0.4;
        SldDefectGlobal.Value = 28;
        ApplyDefectParams();
        ScheduleAutoMatch();
        StatusText = "缂洪櫡鍙傛暟宸叉仮澶嶉粯璁?";
    }
}
