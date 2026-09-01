// ============================================================
// 温启志◆编写◇微信﹕187◆1936◇1399
// ============================================================
// ============================================================
// 温启志◆编写◇微信﹕187◆1936◇1399
// ============================================================
// ============================================================
// 温启志◆编写◇微信︕187◆1936◇1399
// ============================================================
using OpenCvSharp.Extensions;
using System.ComponentModel;

namespace GrayMatch;

public partial class Form1 : Form
{
    private readonly RotatedTemplateMatcher _matcher = new();
    private Bitmap? _sourceBitmap;
    private Bitmap? _templateBitmap;
    private readonly BindingList<MatchResult> _results = new();

    private bool _isDrawingRoi;
    private System.Drawing.Point _roiStart;
    private Rectangle _roiRect;
    private CancellationTokenSource? _matchCts;

    // UI controls
    private Panel _leftPanel = null!;
    private Button _btnOpen = null!;
    private Button _btnCreateTemplate = null!;
    private Button _btnMatch = null!;
    private Button _btnClear = null!;
    private NumericUpDown _numPyramid = null!;
    private NumericUpDown _numAngleStart = null!;
    private NumericUpDown _numAngleEnd = null!;
    private NumericUpDown _numAngleStep = null!;
    private NumericUpDown _numThreshold = null!;
    private NumericUpDown _numOverlap = null!;
    private NumericUpDown _numTopN = null!;
    private Panel _canvasPanel = null!;
    private DataGridView _dataGridView = null!;
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _statusLabel = null!;

    public Form1()
    {
        InitializeComponent();
        BuildUi();
        WireEvents();
        this.Text = "旋转不变 NCC 匹配器 — C# 演示 · " + CodeMeta.Signature;
        this.ClientSize = new Size(1280, 820);
        this.DoubleBuffered = true;
    }

    private void BuildUi()
    {
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = SystemColors.Control
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        this.Controls.Add(mainLayout);

        // Left panel
        _leftPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            AutoScroll = true,
            Padding = new Padding(6)
        };
        mainLayout.Controls.Add(_leftPanel, 0, 0);

        int y = 8;
        _btnOpen = CreateButton("打开图像", ref y);
        _btnCreateTemplate = CreateButton("创建模板", ref y, 6);
        _btnMatch = CreateButton("执行匹配", ref y, 6);
        _btnClear = CreateButton("清除结果", ref y, 6);

        y += 12;
        _numPyramid = CreateNumeric("金字塔", 1, 6, 4, 0, ref y);
        _numAngleStart = CreateNumeric("起始", -180, 180, -180, 1, ref y);
        _numAngleEnd = CreateNumeric("终止", -180, 180, 180, 1, ref y);
        _numAngleStep = CreateNumeric("角度步", 0.1m, 90, 1, 1, ref y);
        _numThreshold = CreateNumeric("NCC阈值", 0.0m, 1.0m, 0.5m, 2, ref y);
        _numOverlap = CreateNumeric("最大重叠", 0.0m, 1.0m, 0.25m, 2, ref y);
        _numTopN = CreateNumeric("TopN", 1, 1000, 10, 0, ref y);


        // Right panel
        var rightPanel = new Panel { Dock = DockStyle.Fill };
        mainLayout.Controls.Add(rightPanel, 1, 0);

        _canvasPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
            BorderStyle = BorderStyle.Fixed3D
        };
        rightPanel.Controls.Add(_canvasPanel);

        _dataGridView = new DataGridView
        {
            Dock = DockStyle.Bottom,
            Height = 230,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoGenerateColumns = false,
            DataSource = _results
        };
        _dataGridView.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Index", HeaderText = "#", Width = 40 });
        _dataGridView.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Score", HeaderText = "NCC 分数", Width = 80 });
        _dataGridView.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "CenterX", HeaderText = "中心 X", Width = 80 });
        _dataGridView.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "CenterY", HeaderText = "中心 Y", Width = 80 });
        _dataGridView.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Angle", HeaderText = "角度°", Width = 60 });
        _dataGridView.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "TemplateWidth", HeaderText = "模板 W", Width = 70 });
        _dataGridView.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "TemplateHeight", HeaderText = "模板 H", Width = 70 });
        _dataGridView.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "LeftTopX", HeaderText = "左上 X", Width = 70 });
        _dataGridView.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "LeftTopY", HeaderText = "左上 Y", Width = 70 });
        _dataGridView.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Level", HeaderText = "层级", Width = 60 });
        rightPanel.Controls.Add(_dataGridView);

        _statusStrip = new StatusStrip { Dock = DockStyle.Bottom };
        _statusLabel = new ToolStripStatusLabel { Text = "就绪 · 温启志◆编写◇微信﹕187◆1936◇1399" };
        _statusStrip.Items.Add(_statusLabel);
        this.Controls.Add(_statusStrip);
    }

    private Button CreateButton(string text, ref int y, int extraTop = 0)
    {
        y += extraTop;
        var btn = new Button
        {
            Text = text,
            Location = new Point(8, y),
            Size = new Size(120, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _leftPanel.Controls.Add(btn);
        y += 32;
        return btn;
    }

    private NumericUpDown CreateNumeric(string label, decimal min, decimal max, decimal value, int decimals, ref int y)
    {
        var lbl = new Label
        {
            Text = label,
            Location = new Point(8, y),
            Size = new Size(120, 16)
        };
        _leftPanel.Controls.Add(lbl);
        y += 18;

        var nud = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            DecimalPlaces = decimals,
            Increment = decimals == 0 ? 1 : (decimals == 1 ? 0.1m : 0.01m),
            Location = new Point(8, y),
            Size = new Size(120, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _leftPanel.Controls.Add(nud);
        y += 28;
        return nud;
    }

    private void WireEvents()
    {
        _btnOpen.Click += async (_, _) => await OpenImageAsync();
        _btnCreateTemplate.Click += (_, _) => StartCreateTemplate();
        _btnMatch.Click += async (_, _) => await RunMatchAsync();
        _btnClear.Click += (_, _) => ClearResults();

        _canvasPanel.Paint += CanvasPanel_Paint;
        _canvasPanel.MouseDown += CanvasPanel_MouseDown;
        _canvasPanel.MouseMove += CanvasPanel_MouseMove;
        _canvasPanel.MouseUp += CanvasPanel_MouseUp;
        _dataGridView.SelectionChanged += DataGridView_SelectionChanged;
        this.FormClosing += (_, _) => _matchCts?.Cancel();
    }

    private async Task OpenImageAsync()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "图像文件|*.bmp;*.png;*.jpg;*.jpeg;*.tif;*.tiff|所有文件|*.*"
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        await Task.Run(() => _matcher.LoadSource(dlg.FileName));
        _sourceBitmap?.Dispose();
        _sourceBitmap = _matcher.Source.ToBitmap();
        _templateBitmap?.Dispose();
        _templateBitmap = null;
        _results.Clear();
        _roiRect = Rectangle.Empty;
        _statusLabel.Text = $"已加载图像: {dlg.FileName}";
        _canvasPanel.Invalidate();
    }

    private void StartCreateTemplate()
    {
        if (_sourceBitmap == null)
        {
            MessageBox.Show("请先打开图像。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _isDrawingRoi = true;
        _statusLabel.Text = "在图像上拖拽绘制模板区域";
    }

    private async Task RunMatchAsync()
    {
        if (_matcher.Template == null)
        {
            MessageBox.Show("请先创建模板。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _btnMatch.Enabled = false;
        _matchCts?.Cancel();
        _matchCts = new CancellationTokenSource();
        var token = _matchCts.Token;

        int pyramid = (int)_numPyramid.Value;
        double start = (double)_numAngleStart.Value;
        double end = (double)_numAngleEnd.Value;
        double step = (double)_numAngleStep.Value;
        double threshold = (double)_numThreshold.Value;
        double overlap = (double)_numOverlap.Value;
        int topN = (int)_numTopN.Value;

        _statusLabel.Text = "正在匹配...";
        var sw = System.Diagnostics.Stopwatch.StartNew();

        List<MatchResult> results;
        try
        {
            // 密集模式现为默认行为：始终开启（关闭 24 种子上限），规则阵列也能全检出
            results = await Task.Run(() => _matcher.Match(pyramid, start, end, step, threshold, overlap, topN, 1), token);
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "匹配已取消";
            _btnMatch.Enabled = true;
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"匹配失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLabel.Text = "匹配失败";
            _btnMatch.Enabled = true;
            return;
        }

        sw.Stop();
        _results.RaiseListChangedEvents = false;
        _results.Clear();
        foreach (var r in results) _results.Add(r);
        _results.RaiseListChangedEvents = true;
        _results.ResetBindings();

        _statusLabel.Text = $"匹配完成: {results.Count} 个结果, {sw.ElapsedMilliseconds} ms (阈={threshold}, 重叠={overlap}, TopN={topN})";
        _canvasPanel.Invalidate();
        _btnMatch.Enabled = true;
    }

    private void ClearResults()
    {
        _matchCts?.Cancel();
        _results.Clear();
        _roiRect = Rectangle.Empty;
        _templateBitmap?.Dispose();
        _templateBitmap = null;
        _canvasPanel.Invalidate();
        _statusLabel.Text = "结果已清除";
    }

    #region Canvas Interaction

    private void CanvasPanel_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.Black);
        if (_sourceBitmap == null) return;

        var imgRect = GetImageDisplayRect();
        g.DrawImage(_sourceBitmap, imgRect);

        // ROI / template highlight
        if (_roiRect.Width > 0 && _roiRect.Height > 0)
        {
            DrawRotatedRect(g, _roiRect, 0, Color.Lime, 2);
        }

        // Results
        foreach (var r in _results)
        {
            var rect = new RectangleF(
                (float)r.LeftTopX,
                (float)r.LeftTopY,
                r.TemplateWidth,
                r.TemplateHeight);
            DrawRotatedRect(g, rect, (float)r.Angle, Color.Lime, 2);
            DrawCross(g, (float)r.CenterX, (float)r.CenterY, Color.Red, 6);
        }
    }

    private RectangleF GetImageDisplayRect()
    {
        if (_sourceBitmap == null) return RectangleF.Empty;
        var panel = _canvasPanel.ClientRectangle;
        double imgRatio = (double)_sourceBitmap.Width / _sourceBitmap.Height;
        double panelRatio = (double)panel.Width / panel.Height;

        float w, h, x, y;
        if (imgRatio > panelRatio)
        {
            w = panel.Width - 2;
            h = (float)(w / imgRatio);
            x = 1;
            y = (panel.Height - h) / 2f;
        }
        else
        {
            h = panel.Height - 2;
            w = (float)(h * imgRatio);
            x = (panel.Width - w) / 2f;
            y = 1;
        }
        return new RectangleF(x, y, w, h);
    }

    private PointF ImageToPanel(PointF imagePt)
    {
        var rect = GetImageDisplayRect();
        float scaleX = rect.Width / (_sourceBitmap?.Width ?? 1);
        float scaleY = rect.Height / (_sourceBitmap?.Height ?? 1);
        return new PointF(rect.X + imagePt.X * scaleX, rect.Y + imagePt.Y * scaleY);
    }

    private PointF PanelToImage(PointF panelPt)
    {
        var rect = GetImageDisplayRect();
        float scaleX = rect.Width / (_sourceBitmap?.Width ?? 1);
        float scaleY = rect.Height / (_sourceBitmap?.Height ?? 1);
        return new PointF((panelPt.X - rect.X) / scaleX, (panelPt.Y - rect.Y) / scaleY);
    }

    private void DrawRotatedRect(Graphics g, RectangleF rect, float angle, Color color, float penWidth)
    {
        using var pen = new Pen(color, penWidth);
        var center = ImageToPanel(new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f));
        float w = rect.Width * GetImageDisplayRect().Width / (_sourceBitmap?.Width ?? 1);
        float h = rect.Height * GetImageDisplayRect().Height / (_sourceBitmap?.Height ?? 1);

        var state = g.Save();
        g.TranslateTransform(center.X, center.Y);
        g.RotateTransform(angle);
        g.DrawRectangle(pen, -w / 2f, -h / 2f, w, h);
        g.Restore(state);
    }

    private void DrawCross(Graphics g, float centerX, float centerY, Color color, int radius)
    {
        using var pen = new Pen(color, 1);
        var center = ImageToPanel(new PointF(centerX, centerY));
        g.DrawLine(pen, center.X - radius, center.Y, center.X + radius, center.Y);
        g.DrawLine(pen, center.X, center.Y - radius, center.X, center.Y + radius);
    }

    private void CanvasPanel_MouseDown(object? sender, MouseEventArgs e)
    {
        if (!_isDrawingRoi || _sourceBitmap == null) return;
        _roiStart = e.Location;
        _roiRect = new Rectangle(e.Location, Size.Empty);
    }

    private void CanvasPanel_MouseMove(object? sender, MouseEventArgs e)
    {
        if (!_isDrawingRoi || e.Button != MouseButtons.Left || _sourceBitmap == null) return;
        int x = Math.Min(_roiStart.X, e.X);
        int y = Math.Min(_roiStart.Y, e.Y);
        int w = Math.Abs(e.X - _roiStart.X);
        int h = Math.Abs(e.Y - _roiStart.Y);
        _roiRect = new Rectangle(x, y, w, h);
        _canvasPanel.Invalidate();
    }

    private void CanvasPanel_MouseUp(object? sender, MouseEventArgs e)
    {
        if (!_isDrawingRoi || _sourceBitmap == null) return;
        _isDrawingRoi = false;

        var imgTopLeft = PanelToImage(new PointF(_roiRect.Left, _roiRect.Top));
        var imgBottomRight = PanelToImage(new PointF(_roiRect.Right, _roiRect.Bottom));
        int x = (int)Math.Max(0, imgTopLeft.X);
        int y = (int)Math.Max(0, imgTopLeft.Y);
        int w = (int)Math.Min(_sourceBitmap.Width - x, imgBottomRight.X - x);
        int h = (int)Math.Min(_sourceBitmap.Height - y, imgBottomRight.Y - y);

        if (w < 8 || h < 8)
        {
            _roiRect = Rectangle.Empty;
            _canvasPanel.Invalidate();
            _statusLabel.Text = "模板区域太小";
            return;
        }

        _roiRect = new Rectangle(x, y, w, h);
        _matcher.SetTemplateFromRoi(new OpenCvSharp.Rect(x, y, w, h));
        _templateBitmap?.Dispose();
        _templateBitmap = _matcher.Template.ToBitmap();
        _statusLabel.Text = $"模板已创建: {_roiRect.Width}x{_roiRect.Height}";
        _canvasPanel.Invalidate();
    }

    private void DataGridView_SelectionChanged(object? sender, EventArgs e)
    {
        _canvasPanel.Invalidate();
    }

    #endregion
}
