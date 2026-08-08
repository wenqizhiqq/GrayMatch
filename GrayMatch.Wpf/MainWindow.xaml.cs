using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GrayMatch.Wpf;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly RotatedTemplateMatcher _matcher = new();
    private CancellationTokenSource? _matchCts;

    private bool _isDrawingRoi;
    private Point _roiStart;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        WireEvents();
        StatusText = "就绪";
    }

    #region Bindable properties

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private int _imageWidth = 1;
    public int ImageWidth { get => _imageWidth; set => Set(ref _imageWidth, value); }

    private int _imageHeight = 1;
    public int ImageHeight { get => _imageHeight; set => Set(ref _imageHeight, value); }

    private BitmapSource? _sourceBitmap;
    public BitmapSource? SourceBitmap { get => _sourceBitmap; set => Set(ref _sourceBitmap, value); }

    public ObservableCollection<MatchResult> Results { get; } = new();

    private double _roiLeft;
    public double RoiLeft { get => _roiLeft; set => Set(ref _roiLeft, value); }

    private double _roiTop;
    public double RoiTop { get => _roiTop; set => Set(ref _roiTop, value); }

    private double _roiWidth;
    public double RoiWidth { get => _roiWidth; set => Set(ref _roiWidth, value); }

    private double _roiHeight;
    public double RoiHeight { get => _roiHeight; set => Set(ref _roiHeight, value); }

    private string _statusText = "";
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    private string _imageSizeText = "\u2014";
    public string ImageSizeText { get => _imageSizeText; set => Set(ref _imageSizeText, value); }

    private string _templateSizeText = "\u2014";
    public string TemplateSizeText { get => _templateSizeText; set => Set(ref _templateSizeText, value); }

    private string _matchMsText = "\u2014";
    public string MatchMsText { get => _matchMsText; set => Set(ref _matchMsText, value); }

    private bool _isShapeMode;
    /// <summary>Bound to the 形状匹配 checkbox: true = shape/edge NCC, false = grayscale NCC.</summary>
    public bool IsShapeMode { get => _isShapeMode; set => Set(ref _isShapeMode, value); }

    #endregion

    private void WireEvents()
    {
        BtnOpen.Click += async (_, _) => await OpenImageAsync();
        BtnCreateTemplate.Click += (_, _) => StartCreateTemplate();
        BtnMatch.Click += async (_, _) => await RunMatchAsync();
        BtnClear.Click += (_, _) => ClearResults();
    }

    private async Task OpenImageAsync()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "图像文件|*.bmp;*.png;*.jpg;*.jpeg;*.tif;*.tiff|所有文件|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        await Task.Run(() => _matcher.LoadSource(dlg.FileName));

        var mat = _matcher.Source;
        SourceBitmap = MatToBitmapSource(mat);
        ImageWidth = mat.Width;
        ImageHeight = mat.Height;
        ImageSizeText = $"{mat.Width} \u00d7 {mat.Height}";
        TemplateSizeText = "\u2014";
        MatchMsText = "\u2014";
        Results.Clear();
        ClearRoi();
        StatusText = $"已加载图像: {dlg.FileName}";
    }

    private void StartCreateTemplate()
    {
        if (SourceBitmap == null)
        {
            MessageBox.Show("请先打开图像。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _isDrawingRoi = true;
        StatusText = "在图像上拖拽绘制模板区域";
    }

    private async Task RunMatchAsync()
    {
        if (_matcher.Template == null)
        {
            MessageBox.Show("请先创建模板。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        BtnMatch.IsEnabled = false;
        _matchCts?.Cancel();
        _matchCts = new CancellationTokenSource();

        if (!int.TryParse((CmbPyramid.SelectedItem as ComboBoxItem)?.Content?.ToString(), out int pyramid))
            pyramid = 4;

        double start = Parse(TbAngleStart.Text, -180);
        double end = Parse(TbAngleEnd.Text, 180);
        double step = Parse(TbAngleStep.Text, 1);
        double threshold = Parse(TbThreshold.Text, 0.5);
        double overlap = Parse(TbOverlap.Text, 0.25);
        int topN = (int)Parse(TbTopN.Text, 64);

        StatusText = "正在匹配...";

        List<MatchResult> results;
        try
        {
            results = await Task.Run(() => _matcher.Match(pyramid, start, end, step, threshold, overlap, topN), _matchCts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "匹配已取消";
            BtnMatch.IsEnabled = true;
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"匹配失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "匹配失败";
            BtnMatch.IsEnabled = true;
            return;
        }

        // Report ONLY the native matching time (excludes template cache construction
        // and WPF box drawing, which the native layer already separates out).
        Results.Clear();
        foreach (var r in results) Results.Add(r);
        StatusText = $"匹配完成: {results.Count} 个结果, 匹配耗时 {_matcher.LastMatchMs:F1} ms (阈={threshold}, 重叠={overlap}, TopN={topN})";
        MatchMsText = $"{_matcher.LastMatchMs:F1} ms";
        BtnMatch.IsEnabled = true;
    }

    private void ClearResults()
    {
        _matchCts?.Cancel();
        Results.Clear();
        ClearRoi();
        StatusText = "结果已清除";
    }

    private void ClearRoi()
    {
        RoiLeft = 0; RoiTop = 0; RoiWidth = 0; RoiHeight = 0;
    }

    private static double Parse(string text, double fallback)
    {
        return double.TryParse(text, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : fallback;
    }

    private static System.Windows.Media.Imaging.BitmapSource MatToBitmapSource(OpenCvSharp.Mat mat)
    {
        var bytes = mat.ImEncode(".png");
        using var ms = new System.IO.MemoryStream(bytes);
        var bmp = new System.Windows.Media.Imaging.BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = ms;
        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bmp.EndInit();
        return bmp;
    }

    #region Canvas / mouse interaction

    private void ImageGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawingRoi || SourceBitmap == null) return;
        _roiStart = e.GetPosition(ImageGrid);
        RoiLeft = _roiStart.X;
        RoiTop = _roiStart.Y;
        RoiWidth = 0;
        RoiHeight = 0;
        ImageGrid.CaptureMouse();
    }

    private void ImageGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawingRoi || e.LeftButton != MouseButtonState.Pressed || SourceBitmap == null) return;
        var pt = e.GetPosition(ImageGrid);
        double x = Math.Min(_roiStart.X, pt.X);
        double y = Math.Min(_roiStart.Y, pt.Y);
        RoiLeft = Math.Max(0, x);
        RoiTop = Math.Max(0, y);
        RoiWidth = Math.Abs(pt.X - _roiStart.X);
        RoiHeight = Math.Abs(pt.Y - _roiStart.Y);
    }

    private void ImageGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawingRoi || SourceBitmap == null) return;
        _isDrawingRoi = false;
        ImageGrid.ReleaseMouseCapture();

        int x = (int)Math.Max(0, RoiLeft);
        int y = (int)Math.Max(0, RoiTop);
        int w = (int)Math.Min(ImageWidth - x, RoiWidth);
        int h = (int)Math.Min(ImageHeight - y, RoiHeight);

        if (w < 8 || h < 8)
        {
            ClearRoi();
            StatusText = "模板区域太小";
            return;
        }

        _matcher.SetTemplateFromRoi(new OpenCvSharp.Rect(x, y, w, h));
        StatusText = $"模板已创建: {w}x{h}";
        TemplateSizeText = $"{_matcher.Template.Width} \u00d7 {_matcher.Template.Height}";
    }

    #endregion

    protected override void OnClosing(CancelEventArgs e)
    {
        _matchCts?.Cancel();
        _matcher.Dispose();
        base.OnClosing(e);
    }
}
