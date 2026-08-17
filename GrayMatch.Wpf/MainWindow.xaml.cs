using Microsoft.Win32;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

// OpenCvSharp 与 System.Windows 都导出 Window / Point，这里用别名消歧义，
// 让 MainWindow : Window 与鼠标事件的 Point 都指向 WPF 版本。
using Window = System.Windows.Window;
using Point = System.Windows.Point;

namespace GrayMatch.Wpf;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly RotatedTemplateMatcher _matcher = new();
    private CancellationTokenSource? _matchCts;
    private CancellationTokenSource? _selCts;
    private readonly SemaphoreSlim _loadSem = new(1, 1);

    private Point _roiStart;
    private int _templateW;
    private int _templateH;
    private bool _defectEnabled;
    private bool _paintedRed;

    private List<string> _imageFiles = new();
    private string? _lastFolder;

    // original color source, kept so defect pixels can be repainted red without re-decoding the file
    private Mat? _sourceColor;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        WireEvents();
        LoadComputerConfig();
        UpdateInfluenceFactors();
        StatusText = "已经准备好了，可以开始";
        _ = LoadPersistedStateAsync();
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

    private BitmapSource? _templateBitmap;
    public BitmapSource? TemplateBitmap { get => _templateBitmap; set => Set(ref _templateBitmap, value); }

    // 轮廓匹配时，模板边缘形状的绿色叠加图（与匹配结果绿框共用同一 XAML 坐标系，保证对齐/可见）。
    private BitmapSource? _templateContourOverlay;
    public BitmapSource? TemplateContourOverlay { get => _templateContourOverlay; set => Set(ref _templateContourOverlay, value); }

    public BulkObservableCollection<MatchResult> Results { get; } = new();
    public BulkObservableCollection<DefectResult> Defects { get; } = new();

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

    private string _imageSizeText = "—";
    public string ImageSizeText { get => _imageSizeText; set => Set(ref _imageSizeText, value); }

    private string _templateSizeText = "—";
    public string TemplateSizeText { get => _templateSizeText; set => Set(ref _templateSizeText, value); }

    private string _matchMsText = "—";
    public string MatchMsText { get => _matchMsText; set => Set(ref _matchMsText, value); }

    private string _defectSummaryText = "-";
    public string DefectSummaryText { get => _defectSummaryText; set => Set(ref _defectSummaryText, value); }

    private string _computerConfigText = "—";
    public string ComputerConfigText { get => _computerConfigText; set => Set(ref _computerConfigText, value); }

    private string _influenceFactorsText = "—";
    public string InfluenceFactorsText { get => _influenceFactorsText; set => Set(ref _influenceFactorsText, value); }

    #endregion

    #region Event wiring

    private void WireEvents()
    {
        BtnOpen.Click += async (_, _) => await OpenFolderAsync();
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
        ChkContour.Checked += ChkContour_Changed;
        ChkContour.Unchecked += ChkContour_Changed;
    }

    #endregion

    #region Open folder / image loading

    private async Task OpenFolderAsync(string? presetFolder = null)
    {
        string? folder = presetFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            var dlg = new OpenFolderDialog();
            if (dlg.ShowDialog() != true) return;
            folder = dlg.FolderName;
        }
        if (!Directory.Exists(folder)) return;

        _lastFolder = folder;
        SaveLastFolder();

        var exts = new HashSet<string> { ".bmp", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".gif" };
        _imageFiles = Directory.GetFiles(folder, "*.*")
            .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        LstImages.ItemsSource = _imageFiles.ConvertAll(Path.GetFileName);
        if (_imageFiles.Count > 0)
        {
            LstImages.SelectedIndex = 0; // triggers SelectionChanged -> load + auto-match (if template exists)
            StatusText = $"已打开文件夹：{folder}（共 {_imageFiles.Count} 张图片，点击切换即载入并自动匹配）";
        }
        else
        {
            LstImages.ItemsSource = null;
            StatusText = "该文件夹里没有图片";
        }
    }

    // 切图即载入；若已创建模板则自动匹配（满足「点击切换即自动查找」）。
    private async void LstImages_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await DebouncedSelectAsync();
    }

    private async Task DebouncedSelectAsync()
    {
        int idx = LstImages.SelectedIndex;
        if (idx < 0 || idx >= _imageFiles.Count) return;
        string path = _imageFiles[idx];

        _selCts?.Cancel();
        _selCts = new CancellationTokenSource();
        var token = _selCts.Token;
        try { await Task.Delay(150, token); }   // 防抖：快速点选只认最后一次
        catch (OperationCanceledException) { return; }

        await LoadSourceFromPathAsync(path, token);
        token.ThrowIfCancellationRequested();

        // 已创建模板时，切换图片后自动匹配；无模板则不弹窗、只载入。
        if (_matcher.Template != null)
        {
            await RunMatchAsync();
        }
    }

    private async Task LoadSourceFromPathAsync(string path, CancellationToken token)
    {
        await _loadSem.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            await Task.Run(() => _matcher.LoadSource(path), token);
            var mat = _matcher.Source;
            _sourceColor = mat.Clone();
            RefreshDisplayBitmap();
            ImageWidth = mat.Width;
            ImageHeight = mat.Height;
            ImageSizeText = $"{mat.Width} × {mat.Height}";

            if (_matcher.Template != null)
            {
                var t = _matcher.Template;
                TemplateSizeText = $"{t.Width} × {t.Height}";
                _templateW = t.Width;
                _templateH = t.Height;
            }
            else
            {
                TemplateSizeText = "—";
                _templateW = 0;
                _templateH = 0;
            }

            Results.Clear();
            Defects.Clear();
            DefectSummaryText = "-";
            MatchMsText = "—";
            ClearRoi();
            UpdateInfluenceFactors();
            StatusText = $"已载入：{Path.GetFileName(path)}";
        }
        catch (OperationCanceledException) { /* 被新选择取消，安静退出 */ }
        catch (Exception ex)
        {
            StatusText = $"载入失败：{ex.Message}";
        }
        finally
        {
            _loadSem.Release();
        }
    }

    #endregion

    #region Template creation

    private void StartCreateTemplate()
    {
        if (SourceBitmap == null)
        {
            MessageBox.Show("请先打开图像。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        // 任何时候都能画框；点「创建模板」才把当前框存成模板。
        int x = (int)Math.Max(0, RoiLeft);
        int y = (int)Math.Max(0, RoiTop);
        int w = (int)Math.Min(ImageWidth - x, RoiWidth);
        int h = (int)Math.Min(ImageHeight - y, RoiHeight);
        if (w < 8 || h < 8)
        {
            MessageBox.Show("请先在图片上拖一个框选好要找的目标，再点「创建模板」。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _matcher.SetTemplateFromRoi(new OpenCvSharp.Rect(x, y, w, h));
        var tpl = _matcher.Template!;
        TemplateSizeText = $"{tpl.Width} × {tpl.Height}";
        _templateW = tpl.Width;
        _templateH = tpl.Height;
        RefreshTemplateVisuals();   // 轮廓模式开时左侧显示绿色轮廓，关时只显示灰度模板
        _ = SaveTemplateAsync(tpl);
        UpdateInfluenceFactors();
        StatusText = $"模板已做好，大小 {w}×{h}（显示在左侧）。点「开始查找」在该图上找目标";
    }

    private async void ImageGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (SourceBitmap == null) return;     // 任何时候都允许画框，不依赖「创建模板」按钮
        _roiStart = e.GetPosition(ImageGrid);
        RoiLeft = _roiStart.X;
        RoiTop = _roiStart.Y;
        RoiWidth = 0;
        RoiHeight = 0;
        ImageGrid.CaptureMouse();
    }

    private void ImageGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (SourceBitmap == null || e.LeftButton != MouseButtonState.Pressed) return;
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
        if (SourceBitmap == null) return;
        ImageGrid.ReleaseMouseCapture();

        // 松手只是确认框选；模板要等用户点「创建模板」才生成。
        int w = (int)RoiWidth, h = (int)RoiHeight;
        if (w >= 8 && h >= 8)
            StatusText = "框已选好：点「创建模板」把它存为模板（再拖一次可重新框选）";
        else
            StatusText = "框太小，请重新拖一个更大的框，然后点「创建模板」";
    }

    #endregion

    #region Matching

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
        var token = _matchCts.Token;

        if (!int.TryParse((CmbPyramid.SelectedItem as ComboBoxItem)?.Content?.ToString(), out int pyramid))
            pyramid = 4;

        double start = Parse(TbAngleStart.Text, -180);
        double end = Parse(TbAngleEnd.Text, 180);
        double step = Parse(TbAngleStep.Text, 1);
        double threshold = Parse(TbThreshold.Text, 0.5);
        double overlap = Parse(TbOverlap.Text, 0.25);
        int topN = (int)Parse(TbTopN.Text, 200);

        StatusText = "正在查找，请稍候...";

        List<MatchResult> results;
        try
        {
            int dense = ChkDense.IsChecked == true ? 1 : 0;
            _matcher.UseContour = ChkContour.IsChecked == true;
            results = await Task.Run(() => _matcher.Match(pyramid, start, end, step, threshold, overlap, topN, dense), token);
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

        Results.Clear();
        Results.AddRange(results);

        Defects.Clear();
        // 绿色轮廓线改为 XAML 叠加层（与绿框共用坐标系），这里只负责红色缺陷像素与清屏。
        if (_defectEnabled)
        {
            var defects = await Task.Run(() => _matcher.DetectDefects(results), token);
            if (token.IsCancellationRequested) { BtnMatch.IsEnabled = true; return; }
            Defects.AddRange(defects);
            DefectSummaryText = BuildDefectSummary(defects);
            RefreshDisplayBitmap(defects);   // 画红
            _paintedRed = defects.Count > 0;
            StatusText = $"查找完成：共找到 {results.Count} 个目标，其中 {defects.Count} 处有缺陷，用时 {_matcher.LastMatchMs:F1} 毫秒";
        }
        else
        {
            DefectSummaryText = "-";
            // 上一轮画过红、本轮没画，才需要重绘清红（省一次大拷贝）。绿色轮廓由 XAML 叠加层处理。
            if (_paintedRed) RefreshDisplayBitmap(null);
            _paintedRed = false;
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
        RefreshDisplayBitmap(); // remove any painted red from a previous run
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

    private void ChkContour_Changed(object sender, RoutedEventArgs e)
    {
        bool on = ChkContour.IsChecked == true;
        _matcher.UseContour = on;
        // 重新生成轮廓（含当前参数），并刷新左侧模板预览与绿色叠加图
        _matcher.RecomputeContours();
        RefreshTemplateVisuals();
        StatusText = on ? "已开启轮廓匹配（用边缘梯度图，抗光照变化；目标会用绿色线条画出模板形状）" : "已关闭轮廓匹配（用灰度图）";
    }

    private static string BuildDefectSummary(List<DefectResult> defects)
    {
        if (defects == null || defects.Count == 0) return "未发现缺陷";
        var counts = new Dictionary<string, int>();
        foreach (var d in defects)
        {
            counts.TryGetValue(d.Type, out int n);
            counts[d.Type] = n + 1;
        }
        var parts = new List<string>();
        foreach (var kv in counts) parts.Add(kv.Key + ": " + kv.Value);
        return "缺陷 " + defects.Count + " 处 (" + string.Join(", ", parts) + ")";
    }

    #endregion

    #region Display bitmap

    private void RefreshDisplayBitmap(IReadOnlyList<DefectResult>? defects = null)
    {
        if (_sourceColor == null) return;
        int w = _sourceColor.Width, h = _sourceColor.Height;
        int ch = _sourceColor.Channels();
        if (ch < 3) return;

        int srcStride = (int)_sourceColor.Step();
        var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr24, null);
        wb.Lock();
        IntPtr back = wb.BackBuffer;
        int dstStride = wb.BackBufferStride;

        var row = new byte[w * 3];
        for (int y = 0; y < h; y++)
        {
            Marshal.Copy(_sourceColor.Data + y * srcStride, row, 0, w * 3);
            Marshal.Copy(row, 0, back + y * dstStride, w * 3);
        }

        bool hasOverlay = (defects != null && defects.Count > 0);
        if (hasOverlay)
        {
            var px = new byte[dstStride * h];
            Marshal.Copy(back, px, 0, px.Length);

            // 红色：缺陷像素
            if (defects != null && defects.Count > 0)
            {
                foreach (var d in defects)
                {
                    if (d.Pixels == null || d.Pw <= 0 || d.Ph <= 0) continue;
                    double phi = -d.Angle * System.Math.PI / 180.0;
                    double cosv = System.Math.Cos(phi), sinv = System.Math.Sin(phi);
                    double tw = d.Tw, th = d.Th;
                    for (int ly = 0; ly < d.Ph; ly++)
                    {
                        int baseOff = ly * d.Pw;
                        for (int lx = 0; lx < d.Pw; lx++)
                        {
                            if (d.Pixels[baseOff + lx] == 0) continue;
                            double ux = lx - tw / 2.0;
                            double uy = ly - th / 2.0;
                            int ix = (int)System.Math.Round(d.CenterX + (ux * cosv - uy * sinv));
                            int iy = (int)System.Math.Round(d.CenterY + (ux * sinv + uy * cosv));
                            if (ix < 0 || iy < 0 || ix >= w || iy >= h) continue;
                            int idx = iy * dstStride + ix * 3;
                            px[idx] = 0; px[idx + 1] = 0; px[idx + 2] = 255;
                        }
                    }
                }
            }

            Marshal.Copy(px, 0, back, px.Length);
        }

        wb.AddDirtyRect(new Int32Rect(0, 0, w, h));
        wb.Unlock();
        SourceBitmap = wb;
    }

    private static BitmapSource? MatToBitmapSource(Mat m)
    {
        if (m == null || m.Empty()) return null;
        int w = m.Width, h = m.Height;
        var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Gray8, null);
        wb.Lock();
        int srcStride = (int)m.Step();
        var row = new byte[w];
        for (int y = 0; y < h; y++)
        {
            Marshal.Copy(m.Data + y * srcStride, row, 0, w);
            Marshal.Copy(row, 0, wb.BackBuffer + y * wb.BackBufferStride, w);
        }
        wb.AddDirtyRect(new Int32Rect(0, 0, w, h));
        wb.Unlock();
        return wb;
    }

    /// <summary>
    /// 把模板边缘二值掩码渲染成一张「透明底 + 绿色线条」的小图（模板尺寸），
    /// 供 XAML 叠加层在每个匹配结果上按角度旋转显示。与绿框共用同一坐标系，保证对齐可见。
    /// 轮廓模式关闭或还没建模板时返回 null（不显示）。
    /// </summary>
    private BitmapSource? BuildContourOverlay()
    {
        var mask = _matcher.TemplateContourMask;
        int w = _matcher.TemplateContourW, h = _matcher.TemplateContourH;
        if (mask == null || w <= 0 || h <= 0) return null;
        var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        wb.Lock();
        int stride = wb.BackBufferStride;
        var px = new byte[stride * h];
        for (int y = 0; y < h; y++)
        {
            int baseOff = y * w;
            for (int x = 0; x < w; x++)
            {
                if (mask[baseOff + x] == 0) continue;
                int idx = y * stride + x * 4;
                px[idx] = 0; px[idx + 1] = 255; px[idx + 2] = 0; px[idx + 3] = 255; // 绿、不透明
            }
        }
        Marshal.Copy(px, 0, wb.BackBuffer, px.Length);
        wb.AddDirtyRect(new Int32Rect(0, 0, w, h));
        wb.Unlock();
        wb.Freeze();
        return wb;
    }

    /// <summary>
    /// 把灰度模板渲染为「灰度底 + 绿色轮廓线条」的预览图（Bgr24），用于左侧模板预览。
    /// 勾选轮廓匹配时显示，调参（阈值/平滑）时会实时刷新。
    /// </summary>
    private BitmapSource? BuildTemplateContourPreview()
    {
        var tpl = _matcher.Template;
        if (tpl == null || tpl.Empty()) return null;
        var mask = _matcher.TemplateContourMask;
        int w = tpl.Width, h = tpl.Height;
        var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr24, null);
        wb.Lock();
        int srcStride = (int)tpl.Step();
        int dstStride = wb.BackBufferStride;
        var px = new byte[dstStride * h];
        for (int y = 0; y < h; y++)
        {
            int srow = y * srcStride;
            int drow = y * dstStride;
            for (int x = 0; x < w; x++)
            {
                byte g = Marshal.ReadByte(tpl.Data + srow + x);
                int idx = drow + x * 3;
                px[idx] = g; px[idx + 1] = g; px[idx + 2] = g;
            }
        }
        if (mask != null)
        {
            for (int y = 0; y < h; y++)
            {
                int baseOff = y * w;
                int drow = y * dstStride;
                for (int x = 0; x < w; x++)
                {
                    if (mask[baseOff + x] == 0) continue;
                    int idx = drow + x * 3;
                    px[idx] = 0; px[idx + 1] = 255; px[idx + 2] = 0;   // 绿
                }
            }
        }
        Marshal.Copy(px, 0, wb.BackBuffer, px.Length);
        wb.AddDirtyRect(new Int32Rect(0, 0, w, h));
        wb.Unlock();
        wb.Freeze();
        return wb;
    }

    /// <summary>
    /// 根据是否勾选「轮廓匹配」刷新左侧模板预览：
    /// - 轮廓模式：显示灰度模板 + 绿色轮廓线条（TemplateBitmap），并重建用于匹配结果叠加的绿色轮廓图；
    /// - 灰度模式：只显示灰度模板，清空绿色轮廓叠加。
    /// 调参（阈值/平滑）时也会调用，使绿色轮廓实时刷新到模板图片上。
    /// </summary>
    private void RefreshTemplateVisuals()
    {
        var tpl = _matcher.Template;
        if (tpl == null)
        {
            TemplateBitmap = null;
            TemplateContourOverlay = null;
            return;
        }
        if (ChkContour.IsChecked == true)
        {
            TemplateBitmap = BuildTemplateContourPreview();
            TemplateContourOverlay = BuildContourOverlay();
        }
        else
        {
            TemplateBitmap = MatToBitmapSource(tpl);
            TemplateContourOverlay = null;
        }
    }

    /// <summary>
    /// 轮廓匹配参数（阈值/平滑）变化时调用：把参数推入 matcher，重新生成边缘掩码，
    /// 并实时刷新左侧模板预览的绿色轮廓。仅在已勾选轮廓匹配且已有模板时生效。
    /// </summary>
    private void ContourParam_Changed(object sender, RoutedEventArgs e)
    {
        // InitializeComponent 设置 Slider.Value 时会先触发 ValueChanged，此时命名字段可能还未赋值。
        if (SldContourBlur == null || SldContourThreshold == null) return;
        if (TbContourBlurVal == null || TbContourThresholdVal == null) return;

        double blur = SldContourBlur.Value;
        int thr = (int)SldContourThreshold.Value;
        // 同步显示数值
        TbContourBlurVal.Text = blur.ToString("0");
        TbContourThresholdVal.Text = thr.ToString();
        _matcher.ContourThreshold = thr;
        _matcher.ContourBlur = blur;
        if (ChkContour.IsChecked == true && _matcher.Template != null)
        {
            _matcher.RecomputeContours();
            RefreshTemplateVisuals();
            StatusText = $"轮廓参数已更新：平滑={blur}，阈值={thr}（越大边越少）";
        }
    }

    #endregion

    #region Persistence

    private static readonly string AppDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GrayMatch");
    private static readonly string LastFolderFile = Path.Combine(AppDataDir, "lastFolder.txt");
    private static readonly string TemplateFile = Path.Combine(AppDataDir, "template.png");

    private void SaveLastFolder()
    {
        try { Directory.CreateDirectory(AppDataDir); File.WriteAllText(LastFolderFile, _lastFolder ?? ""); }
        catch { /* ignore */ }
    }

    private async Task SaveTemplateAsync(Mat tpl)
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            // 模板是灰度图，直接存
            await Task.Run(() => tpl.SaveImage(TemplateFile));
        }
        catch { /* ignore */ }
    }

    private async Task LoadPersistedStateAsync()
    {
        try
        {
            if (File.Exists(TemplateFile))
            {
                using var t = Cv2.ImRead(TemplateFile, ImreadModes.Grayscale);
                if (t != null && !t.Empty())
                {
                    _matcher.SetTemplate(t);
                    TemplateSizeText = $"{t.Width} × {t.Height}";
                    _templateW = t.Width;
                    _templateH = t.Height;
                    RefreshTemplateVisuals();
                    UpdateInfluenceFactors();
                }
            }
            if (File.Exists(LastFolderFile))
            {
                var folder = File.ReadAllText(LastFolderFile).Trim();
                if (Directory.Exists(folder))
                    await OpenFolderAsync(folder);
            }
        }
        catch { /* ignore */ }
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

    private static double Parse(string text, double fallback)
    {
        return double.TryParse(text, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : fallback;
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
            ? (int)System.Math.Floor((end - start) / step) + 1 : 0;

        sb.AppendLine("角度扫描数: " + angleCount);
        sb.AppendLine("金字塔层级: " + pyramid);
        sb.AppendLine("图像尺寸: " + (ImageWidth > 1 ? $"{ImageWidth} x {ImageHeight}" : "未加载"));
        sb.AppendLine("模板尺寸: " + (_templateW > 0 ? $"{_templateW} x {_templateH}" : "未创建"));
        sb.AppendLine("");
        sb.AppendLine("影响速度的可能因素:");
        sb.AppendLine("? 金字塔层级↑ -> 越快 (粗层先筛种子)");
        sb.AppendLine("? 角度范围/步长↑ -> 越快但精度↓");
        sb.AppendLine("? 图像/模板越大 -> 计算量越大、越慢");
        sb.AppendLine("? 核心数↑ -> OpenMP 并行越快");

        InfluenceFactorsText = sb.ToString().TrimEnd();
    }

    #endregion

    protected override void OnClosing(CancelEventArgs e)
    {
        _matchCts?.Cancel();
        _selCts?.Cancel();
        _matcher.Dispose();
        base.OnClosing(e);
    }
}
