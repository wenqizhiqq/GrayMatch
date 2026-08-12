using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace GrayMatch.Wpf;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly RotatedTemplateMatcher _matcher = new();
    private CancellationTokenSource? _matchCts;

    private bool _isDrawingRoi;
    private Point _roiStart;
    private int _templateW;
    private int _templateH;
    private bool _defectEnabled;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        WireEvents();
        LoadComputerConfig();
        UpdateInfluenceFactors();
        StatusText = "已经准备好了，可以开始";
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
    public ObservableCollection<DefectResult> Defects { get; } = new();

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

    private string _defectSummaryText = "-";
    public string DefectSummaryText { get => _defectSummaryText; set => Set(ref _defectSummaryText, value); }

    private string _computerConfigText = "\u2014";
    public string ComputerConfigText { get => _computerConfigText; set => Set(ref _computerConfigText, value); }

    private string _influenceFactorsText = "\u2014";
    public string InfluenceFactorsText { get => _influenceFactorsText; set => Set(ref _influenceFactorsText, value); }

    #endregion

    private void WireEvents()
    {
        BtnOpen.Click += async (_, _) => await OpenImageAsync();
        BtnCreateTemplate.Click += (_, _) => StartCreateTemplate();
        BtnMatch.Click += async (_, _) => await RunMatchAsync();
        BtnClear.Click += (_, _) => ClearResults();

        TbAngleStart.TextChanged += (_, _) => UpdateInfluenceFactors();
        TbAngleEnd.TextChanged += (_, _) => UpdateInfluenceFactors();
        TbAngleStep.TextChanged += (_, _) => UpdateInfluenceFactors();
        TbThreshold.TextChanged += (_, _) => UpdateInfluenceFactors();
        TbOverlap.TextChanged += (_, _) => UpdateInfluenceFactors();
        TbTopN.TextChanged += (_, _) => UpdateInfluenceFactors();
        CmbPyramid.SelectionChanged += (_, _) => UpdateInfluenceFactors();
        ChkDefect.Checked += ChkDefect_Changed;
        ChkDefect.Unchecked += ChkDefect_Changed;
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
        _templateW = 0;
        _templateH = 0;
        Results.Clear();
        Defects.Clear();
        DefectSummaryText = "-";
        ClearRoi();
        UpdateInfluenceFactors();
        StatusText = $"图片已读入：{dlg.FileName}";
    }

    private void StartCreateTemplate()
    {
        if (SourceBitmap == null)
        {
            MessageBox.Show("请先打开图像。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _isDrawingRoi = true;
        StatusText = "在图片上按住鼠标拖一个框，框住要找的目标（松手即成为模板）";
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

        StatusText = "正在查找，请稍候...";

        List<MatchResult> results;
        try
        {
            results = await Task.Run(() => _matcher.Match(pyramid, start, end, step, threshold, overlap, topN), _matchCts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消查找";
            BtnMatch.IsEnabled = true;
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"匹配失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "查找失败了";
            BtnMatch.IsEnabled = true;
            return;
        }

        // Report ONLY the native matching time (excludes template cache construction
        // and WPF box drawing, which the native layer already separates out).
        Results.Clear();
        foreach (var r in results) Results.Add(r);

        Defects.Clear();
        if (_defectEnabled)
        {
            var defects = await Task.Run(() => _matcher.DetectDefects(results), _matchCts.Token);
            foreach (var d in defects) Defects.Add(d);
            DefectSummaryText = BuildDefectSummary(defects);
            StatusText = $"查找完成：共找到 {results.Count} 个目标，其中 {defects.Count} 处有缺陷，用时 {_matcher.LastMatchMs:F1} 毫秒";
        }
        else
        {
            DefectSummaryText = "-";
            StatusText = $"查找完成：共找到 {results.Count} 个目标，用时 {_matcher.LastMatchMs:F1} 毫秒";
        }
        MatchMsText = $"{_matcher.LastMatchMs:F1} ms";
        BtnMatch.IsEnabled = true;
    }

    private void ClearResults()
    {
        _matchCts?.Cancel();
        Results.Clear();
        Defects.Clear();
        DefectSummaryText = "-";
        ClearRoi();
        StatusText = "结果已清空，绿框已去掉";
    }

    private void ClearRoi()
    {
        RoiLeft = 0; RoiTop = 0; RoiWidth = 0; RoiHeight = 0;
    }

    private void ChkDefect_Changed(object sender, RoutedEventArgs e)
    {
        _defectEnabled = ChkDefect.IsChecked == true;
        DefectSummaryText = "-";
        StatusText = _defectEnabled ? "已开启缺陷检查" : "已关闭缺陷检查";
    }

    private static string BuildDefectSummary(System.Collections.Generic.List<DefectResult> defects)
    {
        if (defects == null || defects.Count == 0) return "未发现缺陷";
        var counts = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var d in defects)
        {
            counts.TryGetValue(d.Type, out int n);
            counts[d.Type] = n + 1;
        }
        var parts = new System.Collections.Generic.List<string>();
        foreach (var kv in counts) parts.Add(kv.Key + ": " + kv.Value);
        return "缺陷 " + defects.Count + " 处 (" + string.Join(", ", parts) + ")";
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
            StatusText = "框选的模板太小了，请框大一点";
            return;
        }

        _matcher.SetTemplateFromRoi(new OpenCvSharp.Rect(x, y, w, h));
        StatusText = $"模板已做好，大小 {w}×{h}";
        TemplateSizeText = $"{_matcher.Template.Width} \u00d7 {_matcher.Template.Height}";
        _templateW = _matcher.Template.Width;
        _templateH = _matcher.Template.Height;
        UpdateInfluenceFactors();
    }

    #endregion

    #region Computer config & influence factors

    [StructLayout(LayoutKind.Sequential)]
    private sealed class MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
        public MemoryStatusEx() { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>(); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx lpBuffer);

    private void LoadComputerConfig()
    {
        var sb = new StringBuilder();
        try
        {
            sb.AppendLine("操作系统: " + RuntimeInformation.OSDescription.Trim());
            sb.AppendLine("系统架构: " + RuntimeInformation.ProcessArchitecture + (Environment.Is64BitProcess ? " (64位进程)" : " (32位进程)"));
            sb.AppendLine("处理器: " + (Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "未知"));
            sb.AppendLine("逻辑核心数: " + Environment.ProcessorCount);

            ulong totalPhys = 0;
            try
            {
                var ms = new MemoryStatusEx();
                if (GlobalMemoryStatusEx(ms)) totalPhys = ms.ullTotalPhys;
            }
            catch { }
            if (totalPhys > 0)
                sb.AppendLine("物理内存: " + (totalPhys / 1024.0 / 1024.0 / 1024.0).ToString("F1") + " GB");

            string ocv = "4.8.0";
            try
            {
                var m = typeof(OpenCvSharp.Cv2).GetMethod("GetVersionString", Type.EmptyTypes);
                if (m != null) ocv = (string?)m.Invoke(null, null) ?? ocv;
            }
            catch { }
            sb.AppendLine("OpenCV: " + ocv + " (OpenCvSharp4 4.8.0)");
            sb.AppendLine("运行框架: " + RuntimeInformation.FrameworkDescription);
            sb.AppendLine("并行计算: OpenMP x " + Environment.ProcessorCount + " 线程");
        }
        catch (Exception ex)
        {
            sb.Clear();
            sb.AppendLine("配置读取失败: " + ex.Message);
        }
        ComputerConfigText = sb.ToString().TrimEnd();
    }

    private void UpdateInfluenceFactors()
    {
        var sb = new StringBuilder();
        double start = Parse(TbAngleStart.Text, -180);
        double end = Parse(TbAngleEnd.Text, 180);
        double step = Parse(TbAngleStep.Text, 1);
        int pyramid = 4;
        if (int.TryParse((CmbPyramid.SelectedItem as ComboBoxItem)?.Content?.ToString(), out int p)) pyramid = p;

        int angleCount = (step > 1e-9 && end >= start)
            ? (int)Math.Floor((end - start) / step) + 1 : 0;

        sb.AppendLine("角度扫描数: " + angleCount);
        sb.AppendLine("金字塔层级: " + pyramid);
        sb.AppendLine("图像尺寸: " + (ImageWidth > 1 ? $"{ImageWidth} x {ImageHeight}" : "未加载"));
        sb.AppendLine("模板尺寸: " + (_templateW > 0 ? $"{_templateW} x {_templateH}" : "未创建"));
        sb.AppendLine("");
        sb.AppendLine("影响速度的可能因素:");
        sb.AppendLine("• 金字塔层级↑ -> 越快 (粗层先筛种子)");
        sb.AppendLine("• 角度范围/步长↑ -> 越快但精度↓");
        sb.AppendLine("• 图像/模板越大 -> 计算量越大、越慢");
        sb.AppendLine("• 核心数↑ -> OpenMP 并行越快");

        InfluenceFactorsText = sb.ToString().TrimEnd();
    }

    #endregion

    protected override void OnClosing(CancelEventArgs e)
    {
        _matchCts?.Cancel();
        _matcher.Dispose();
        base.OnClosing(e);
    }
}
