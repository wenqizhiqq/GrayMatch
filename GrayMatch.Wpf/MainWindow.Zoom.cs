// ============================================================
// 温启志◆编写◇微信﹕187◆1936◇1399
// ============================================================
// ============================================================
// 温启志◆编写◇微信﹕187◆1936◇1399
// ============================================================
// ============================================================
// 温启志◆编写◇微信﹕187◆1936◇1399
// ============================================================
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GrayMatch.Wpf;

/// <summary>
/// Image-view zoom &amp; pan, implemented with a <see cref="ScrollViewer"/> + a
/// <see cref="System.Windows.Media.ScaleTransform"/> in <c>ImageGrid.LayoutTransform</c>.
///
/// Why ScrollViewer? The previous <c>Viewbox</c>/<c>Border</c> + render-transform (scale+translate)
/// approach kept clipping the bottom of the image because manual centering math never lined up with
/// WPF's actual layout. A <c>ScrollViewer</c> structurally guarantees the whole content is reachable:
/// when the image is larger than the viewport you get scrollable area, and when it is smaller the
/// <c>HorizontalContentAlignment/Center</c> settings auto-center it. We only ever touch the scale;
/// scrolling/centering is delegated to the ScrollViewer.
///
/// ROI selection (left-drag, handled on <c>ImageGrid</c> in the main code-behind) is untouched:
/// <c>Mouse.GetPosition(ImageGrid)</c> returns image-space (unscaled) coordinates regardless of the
/// layout transform, so the ROI rectangle and the result/defect overlays all stay glued to the pixels.
///
/// Controls:
///   - Mouse wheel        → zoom in/out about the cursor
///   - Middle button drag → pan
///   - Double click       → reset / fit-to-view
///   - New image / resize → auto fit (until the user has manually transformed)
/// </summary>
public partial class MainWindow
{
    private bool _isPanning;
    private Point _panStart;          // cursor position at pan start (in ScrollViewer space)
    private Point _scrollStart;       // scroll offsets at pan start
    private bool _manualTransform;    // user has zoomed/panned → stop auto-fitting
    private bool _initialFitPending = true;
    private bool _isFitting;          // re-entrancy guard for FitToView
    private int _fitCount;

    private void ImageViewer_Loaded(object sender, RoutedEventArgs e)
    {
        // LayoutUpdated is the reliable "the image has been laid out" signal.
        // It fires after every arrange pass; we use a one-shot flag so we only fit once per image.
        ImageGrid.LayoutUpdated += ImageGrid_LayoutUpdated;

        // Also listen to the Source DP for cases where the source is swapped without a full layout diff.
        DependencyPropertyDescriptor.FromProperty(Image.SourceProperty, typeof(Image))
            .AddValueChanged(SourceImage, OnSourceImageChanged);

        // The window is its own DataContext (INotifyPropertyChanged); SourceBitmap is a direct,
        // reliable signal that a new image was opened.
        this.PropertyChanged += OnWindowPropertyChanged;
    }

    private void OnWindowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e?.PropertyName == "SourceBitmap")
        {
            _manualTransform = false;
            _initialFitPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(FitToView));
        }
    }

    private void ImageGrid_LayoutUpdated(object? sender, EventArgs e)
    {
        if (_initialFitPending && SourceImage?.Source != null &&
            ImageGrid.ActualWidth > 1 && ImageGrid.ActualHeight > 1 &&
            ImageViewport.ViewportWidth > 0 && ImageViewport.ViewportHeight > 0)
        {
            _manualTransform = false;
            FitToView();
            _initialFitPending = false;
        }
    }

    private void OnSourceImageChanged(object? sender, EventArgs e)
    {
        // A freshly loaded image should always fit, regardless of any prior zoom/pan.
        _manualTransform = false;
        _initialFitPending = true;

        // Force the grid to exactly match the source image's size. This avoids any mystery where the
        // grid sizes to the viewport or to overlay children instead of the actual bitmap.
        if (SourceImage?.Source != null)
        {
            ImageGrid.Width = SourceImage.Source.Width;
            ImageGrid.Height = SourceImage.Source.Height;
        }

        // Multiple deferred attempts cover every layout timing edge case.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitToView));
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(FitToView));
    }

    private void ImageGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The grid's layout size changes both when a NEW image loads AND when our scale changes
        // (LayoutTransform affects layout). Guard with _isFitting so the scale change does not
        // re-trigger a fit; guard with _manualTransform so a user's zoom/pan survives an ordinary
        // window resize.
        if (_isFitting || _manualTransform) return;
        FitToView();
    }

    private void ImageArea_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var sv = (ScrollViewer)sender;

        // Position of the cursor in the IMAGE's own (unscaled) coordinate space. GetPosition on the
        // child element returns local coordinates that already account for the layout transform, so
        // this is independent of the current zoom and correct even when the content is centered.
        Point pContent = e.GetPosition(ImageGrid);
        // Position of the cursor within the ScrollViewer viewport (screen-relative).
        Point pViewport = e.GetPosition(sv);

        double oldScale = ImageScale.ScaleX;
        double factor = e.Delta > 0 ? 1.12 : 1.0 / 1.12;
        double newScale = Math.Max(0.05, Math.Min(40, oldScale * factor));
        ImageScale.ScaleX = newScale;
        ImageScale.ScaleY = newScale;

        // Force the layout so the new scrollable extent is known, then scroll so the same content
        // point stays under the cursor:  scrollOffset + pViewport == pContent * newScale.
        sv.UpdateLayout();
        sv.ScrollToHorizontalOffset(pContent.X * newScale - pViewport.X);
        sv.ScrollToVerticalOffset(pContent.Y * newScale - pViewport.Y);

        _manualTransform = true;
        e.Handled = true;
    }

    private void ImageArea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var sv = (ScrollViewer)sender;

        // Double-click (left) resets the view to fit. ScrollViewer/Grid have no MouseDoubleClick
        // event, so we read ClickCount off the normal MouseDown.
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            _manualTransform = false;
            FitToView();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Middle && e.ButtonState == MouseButtonState.Pressed)
        {
            _isPanning = true;
            _panStart = e.GetPosition(sv);
            _scrollStart = new Point(sv.HorizontalOffset, sv.VerticalOffset);
            sv.CaptureMouse();
            _manualTransform = true;
            e.Handled = true;
        }
    }

    private void ImageArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;
        var sv = (ScrollViewer)sender;
        Point p = e.GetPosition(sv);
        // Dragging right moves content right → scroll offset decreases.
        sv.ScrollToHorizontalOffset(_scrollStart.X - (p.X - _panStart.X));
        sv.ScrollToVerticalOffset(_scrollStart.Y - (p.Y - _panStart.Y));
        e.Handled = true;
    }

    private void ImageArea_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning && e.ChangedButton == MouseButton.Middle)
        {
            _isPanning = false;
            ((ScrollViewer)sender).ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Scale the image so it fits the viewport. Because the <c>ImageGrid</c> lives inside a
    /// <c>ScrollViewer</c> with centered content alignment, the ScrollViewer itself takes care of
    /// centering (when the image is smaller than the viewport) or making it scrollable (when larger).
    /// We only set the scale and reset the scroll position to the origin.
    /// </summary>
    private void FitToView()
    {
        if (_isFitting) return;
        _isFitting = true;
        try
        {
            if (SourceImage == null || SourceImage.Source == null) return;
            double srcW = SourceImage.Source.Width;
            double srcH = SourceImage.Source.Height;
            if (srcW <= 0 || srcH <= 0) return;

            double vpW = ImageViewport.ViewportWidth;
            double vpH = ImageViewport.ViewportHeight;
            if (vpW <= 0 || vpH <= 0) return;

            double scale = Math.Min(vpW / srcW, vpH / srcH);
            ImageScale.ScaleX = scale;
            ImageScale.ScaleY = scale;

            // Reset scroll to the origin; when the image fits it is auto-centered, and when it is
            // larger this shows the top-left corner. Do it after layout so clamping is correct.
            ImageViewport.ScrollToHorizontalOffset(0);
            ImageViewport.ScrollToVerticalOffset(0);

            _fitCount++;
            StatusText = $"适配#{_fitCount} 缩放={scale:F3} 源={srcW:F0}x{srcH:F0} 视口={vpW:F0}x{vpH:F0}";
        }
        finally
        {
            _isFitting = false;
        }
    }
}
