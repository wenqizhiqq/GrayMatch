// ============================================================
// 切换图片卡顿修复：把「切图」与「模板匹配」彻底解耦
// 你本机 GrayMatch.Wpf/MainWindow.xaml.cs 只需改这 2 处。
// 改完切图 = 仅解码+显示（毫秒级），匹配只在点「开始查找」时跑。
// ============================================================

// ---------- 改动 1：LstImages_SelectionChanged（删掉自动匹配）----------
// 之前（卡顿根因）：
//     int mySeq = ++_opSeq;
//     await LoadSourceFromPathAsync(path, token);
//     if (_matcher.Template != null)
//         await RunMatchAsync(mySeq, token);   // ← 每次切图都跑完整 360° NCC 扫描
//
// 改成（切图只载入显示，不自动匹配）：
private async void LstImages_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    int idx = LstImages.SelectedIndex;
    if (idx < 0 || idx >= _imageFiles.Count) return;
    string path = _imageFiles[idx];

    _selCts?.Cancel();
    _selCts = new CancellationTokenSource();
    var token = _selCts.Token;
    try { await Task.Delay(150, token); }          // 150ms 防抖，快速点选只认最后一次
    catch (OperationCanceledException) { return; }

    int mySeq = ++_opSeq;
    await LoadSourceFromPathAsync(path, token);     // 仅载入 + 显示，不触发匹配
    // 切图与模板解耦：匹配请用左侧「开始查找」。
    // 若以后想重新自动匹配，把下面一行加回来即可（但切图会再变慢）：
    // if (_matcher.Template != null && _opSeq == mySeq && !token.IsCancellationRequested)
    //     await RunMatchAsync(mySeq, token);
}


// ---------- 改动 2：RunMatchAsync 的 no-defect 分支，去掉多余的整图重绘 ----------
// 匹配后绿框由 XAML 里 Results 的 ItemsControl 绑定自动画，不需要重绘位图。
// 原来每次匹配都 RefreshDisplayBitmap() 把整张彩色图逐行 Marshal.Copy 一遍（1600x1200≈5.7MB），纯浪费。
// 只在「上一轮画过红（缺陷）且本轮没画」时才重绘清红。

// 在字段区加一行：
private bool _paintedRed;

// RunMatchAsync 里：
// 缺陷分支（保留，并标记画过红）：
if (_defectEnabled)
{
    var defects = await Task.Run(() => _matcher.DetectDefects(results), token);
    if (token.IsCancellationRequested || _opSeq != seq) { BtnMatch.IsEnabled = true; return; }
    Defects.AddRange(defects);
    DefectSummaryText = BuildDefectSummary(defects);
    RefreshDisplayBitmap(defects);   // 画红
    _paintedRed = true;              // ← 标记：这轮画了红
    StatusText = $"查找完成：共找到 {results.Count} 个目标，其中 {defects.Count} 处有缺陷，用时 {_matcher.LastMatchMs:F1} 毫秒";
}
else
{
    DefectSummaryText = "-";
    // 只有上一轮画过红、这轮没画，才需要清红；否则不重绘整张图
    if (_paintedRed) { RefreshDisplayBitmap(); _paintedRed = false; }
    StatusText = $"查找完成：共找到 {results.Count} 个目标，用时 {_matcher.LastMatchMs:F1} 毫秒";
}
