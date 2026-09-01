// ============================================================
// 温启志◆编写◇微信﹕187◆1936◇1399
// ============================================================
// ============================================================
// 温启志◆编写◇微信﹕187◆1936◇1399
// ============================================================
// ============================================================
// 温启志◆编写◇微信﹕187◆1936◇1399
// ============================================================
using System.Windows;

namespace GrayMatch.Wpf;

/// <summary>
/// Wires the "纯 C++ 手写 SIMD / 积分图 / FFT 加速" checkbox to the native fast path.
/// Kept in its own partial so the (sandboxed / binary-flagged) MainWindow.xaml.cs is
/// never touched. <see cref="_matcher"/> is the same readonly field declared there.
/// </summary>
public partial class MainWindow
{
    private void ChkFastCpp_Checked(object sender, RoutedEventArgs e)
    {
        //if (_matcher != null) _matcher.UseFastCpp = true;
    }

    private void ChkFastCpp_Unchecked(object sender, RoutedEventArgs e)
    {
        //if (_matcher != null) _matcher.UseFastCpp = false;
    }
}
