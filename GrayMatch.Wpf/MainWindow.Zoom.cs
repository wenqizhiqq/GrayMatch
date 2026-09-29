// ============================================================
// 温启志◆编写◇微信﹕187◆1936◇1399
// ============================================================
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GrayMatch.Wpf;

/// <summary>
/// 图像显示：固定“整幅自适应窗口”，不做缩放/平移。
///
/// 之前是 <c>ScrollViewer</c> + 渲染变换（滚轮缩放、中键拖动），现在按需求去掉：
/// 显示交给 XAML 里的 <c>Viewbox</c>（Stretch=Uniform），
/// 无论打开新图还是改窗口大小，图像始终完整铺满并居中，不需要任何交互。
///
/// 这里只做一件事：把 <c>ImageGrid</c> 的逻辑尺寸设成图像原始尺寸，
/// 这样 overlay（ROI/结果/缺陷）与像素坐标一一对应；未缩放的 Grid 比视口大，
/// 鼠标命中区域覆盖整个视口，任意位置都能框选 ROI。
/// 坐标说明：Viewbox 施加的是布局缩放，
/// <c>Mouse.GetPosition(ImageGrid)</c> 返回的仍是未缩放的图像坐标，与显示比例无关。
/// </summary>
public partial class MainWindow
{
    private void ImageViewer_Loaded(object sender, RoutedEventArgs e)
    {
        // 换图时把 Grid 尺寸对齐到新图。
        DependencyPropertyDescriptor.FromProperty(Image.SourceProperty, typeof(Image))
            .AddValueChanged(SourceImage, OnSourceImageChanged);

        SyncImageSize();
    }

    private void OnSourceImageChanged(object? sender, EventArgs e) => SyncImageSize();

    /// <summary>
    /// 让 ImageGrid 的逻辑尺寸等于图像显示尺寸（设备无关单位：像素 × 96/DPI）。
    /// 自适应由外层 Viewbox 完成，这里只负责坐标系对齐。
    /// </summary>
    private void SyncImageSize()
    {
        var src = SourceImage?.Source;
        if (src == null) return;

        double w = ContentWidth(src);
        double h = ContentHeight(src);
        if (w <= 0.5 || h <= 0.5) return;

        // Grid 尺寸初始是 NaN（NaN 参与比较恒为 false），必须单独判 NaN。
        if (double.IsNaN(ImageGrid.Width) || Math.Abs(ImageGrid.Width - w) > 0.01)
            ImageGrid.Width = w;
        if (double.IsNaN(ImageGrid.Height) || Math.Abs(ImageGrid.Height - h) > 0.01)
            ImageGrid.Height = h;

        // 让 <Image> 自身也按同一尺寸渲染：位图 DPI 元数据与容器尺寸不一致时两者会错位。
        if (SourceImage != null)
        {
            if (double.IsNaN(SourceImage.Width) || Math.Abs(SourceImage.Width - w) > 0.01)
                SourceImage.Width = w;
            if (double.IsNaN(SourceImage.Height) || Math.Abs(SourceImage.Height - h) > 0.01)
                SourceImage.Height = h;
        }
    }

    /// <summary>图像显示宽度（设备无关单位 = 像素 × 96 / DPI）。</summary>
    private static double ContentWidth(ImageSource src)
    {
        var bmp = src as BitmapSource;
        double dpi = (bmp != null && bmp.DpiX > 1) ? bmp.DpiX : 96.0;
        return src.Width * 96.0 / dpi;
    }

    /// <summary>图像显示高度（设备无关单位 = 像素 × 96 / DPI）。</summary>
    private static double ContentHeight(ImageSource src)
    {
        var bmp = src as BitmapSource;
        double dpi = (bmp != null && bmp.DpiY > 1) ? bmp.DpiY : 96.0;
        return src.Height * 96.0 / dpi;
    }
}
