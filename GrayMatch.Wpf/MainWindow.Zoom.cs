// ============================================================
// 温启志◆编写◇微信﹕187◆1936◇1399
// ============================================================
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GrayMatch.Wpf;

/// <summary>
/// 图像显示：Viewbox 负责“整幅自适应窗口”，自适应之上再叠一层渲染变换做缩放/平移。
///
/// 控件：
///   - 滚轮        → 以光标为锚点缩放，1.0 就是“整幅铺满”，所以换图、改窗口大小都不用重算适配比例
///   - 中键拖动    → 平移
///   - 中键双击    → 复位到整幅自适应
///
/// 变换挂在 <c>ImageViewport.RenderTransform</c>（先 Scale 后 Translate，原点 (0,0)）：
/// 屏幕点 = 缩放 × 自适应后的内容点 + 平移量。外层 <c>ImageArea</c> 自身不变换、只开
/// <c>ClipToBounds</c>，所以放大后超出视口的部分会被裁掉，不会出现手工算中心位置对不上的老问题。
///
/// 坐标一致性：ROI 框选与结果叠加层都在 <c>ImageGrid</c> 内部，其逻辑尺寸 = 图像原始尺寸
/// （见 <see cref="SyncImageSize"/>），与像素一一对应。<c>Mouse.GetPosition(ImageGrid)</c>
/// 取的是整棵视觉树的逆变换，包含这里的缩放/平移，返回的始终是未缩放的图像坐标。
/// </summary>
public partial class MainWindow
{
    private const double ZoomStep = 1.15;
    private const double MinZoom = 0.1;
    private const double MaxZoom = 20;
    private const double EdgeHold = 60;   // 平移时图像至少要留在视口内的像素数，防止一拖就把图拖丢

    private bool _isPanning;
    private Point _panCursor;   // 中键按下时的光标（ImageArea 坐标）
    private Point _panOrigin;   // 中键按下时的平移量

    private void ImageViewer_Loaded(object sender, RoutedEventArgs e)
    {
        // 换图时把 Grid 尺寸对齐到新图。
        DependencyPropertyDescriptor.FromProperty(Image.SourceProperty, typeof(Image))
            .AddValueChanged(SourceImage, OnSourceImageChanged);

        SyncImageSize();
    }

    private void OnSourceImageChanged(object? sender, EventArgs e)
    {
        SyncImageSize();
        ResetView();    // 新图一律回到整幅自适应
    }

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

    /// <summary>滚轮缩放：光标下的图像点在缩放后仍落在光标下。</summary>
    private void ImageArea_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (SourceImage?.Source == null) return;

        double old = ImageZoom.ScaleX;
        if (old <= 0) old = 1;
        double next = Math.Clamp(old * (e.Delta > 0 ? ZoomStep : 1 / ZoomStep), MinZoom, MaxZoom);
        if (Math.Abs(next - old) < 1e-9) { e.Handled = true; return; }

        var c = e.GetPosition(ImageArea);
        // 屏幕 = s·内容 + t，令内容点 p 前后重合：t' = c - (s'/s)·(c - t)
        ImagePan.X = c.X - next / old * (c.X - ImagePan.X);
        ImagePan.Y = c.Y - next / old * (c.Y - ImagePan.Y);
        ImageZoom.ScaleX = ImageZoom.ScaleY = next;
        ClampPan();
        e.Handled = true;
    }

    private void ImageArea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // 只认中键：左键要留给 ImageGrid 上的 ROI 框选。
        if (e.ChangedButton != MouseButton.Middle) return;

        if (e.ClickCount == 2)
        {
            ResetView();
            e.Handled = true;
            return;
        }

        _isPanning = true;
        _panCursor = e.GetPosition(ImageArea);
        _panOrigin = new Point(ImagePan.X, ImagePan.Y);
        ImageArea.Cursor = Cursors.SizeAll;
        ImageArea.CaptureMouse();
        e.Handled = true;
    }

    private void ImageArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;
        if (e.MiddleButton != MouseButtonState.Pressed)
        {
            EndPan();     // 捕获被抢走（切窗口等）时别再跟着鼠标跑
            return;
        }

        var p = e.GetPosition(ImageArea);
        ImagePan.X = _panOrigin.X + (p.X - _panCursor.X);
        ImagePan.Y = _panOrigin.Y + (p.Y - _panCursor.Y);
        ClampPan();
        e.Handled = true;
    }

    private void ImageArea_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning || e.ChangedButton != MouseButton.Middle) return;
        EndPan();
        e.Handled = true;
    }

    private void ImageArea_SizeChanged(object sender, SizeChangedEventArgs e) => ClampPan();

    private void EndPan()
    {
        _isPanning = false;
        if (ImageArea.IsMouseCaptured) ImageArea.ReleaseMouseCapture();
        ImageArea.Cursor = Cursors.Arrow;
    }

    private void ResetView()
    {
        ImageZoom.ScaleX = ImageZoom.ScaleY = 1;
        ImagePan.X = ImagePan.Y = 0;
    }

    /// <summary>限制平移范围：图像再怎么拖，也得有 <see cref="EdgeHold"/> 像素留在视口里。</summary>
    private void ClampPan()
    {
        double cw = ImageArea.ActualWidth, ch = ImageArea.ActualHeight;
        double iw = ImageGrid.Width, ih = ImageGrid.Height;
        if (cw <= 0 || ch <= 0 || double.IsNaN(iw) || double.IsNaN(ih) || iw <= 0 || ih <= 0) return;

        double fit = Math.Min(cw / iw, ch / ih);        // Viewbox 的自适应比例
        double s = ImageZoom.ScaleX;
        double drawnW = iw * fit * s, drawnH = ih * fit * s;
        // Viewbox 把自适应后的内容居中，我们的缩放以 (0,0) 为原点，所以中心偏移也要一起乘 s。
        double centerLeft = (cw - iw * fit) / 2, centerTop = (ch - ih * fit) / 2;

        double minX = EdgeHold - drawnW - s * centerLeft, maxX = cw - EdgeHold - s * centerLeft;
        double minY = EdgeHold - drawnH - s * centerTop,  maxY = ch - EdgeHold - s * centerTop;

        ImagePan.X = Math.Clamp(ImagePan.X, Math.Min(minX, maxX), Math.Max(minX, maxX));
        ImagePan.Y = Math.Clamp(ImagePan.Y, Math.Min(minY, maxY), Math.Max(minY, maxY));
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
