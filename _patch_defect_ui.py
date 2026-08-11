# -*- coding: utf-8 -*-
"""One-shot patch: wire the defect-parameter panel into MainWindow.xaml.cs."""
import io, sys

p = r'D:\wqz\code\GrayMatch\GrayMatch.Wpf\MainWindow.xaml.cs'
s = io.open(p, encoding='utf-8').read()
orig = s

# ---- 1) reset button wiring -------------------------------------------------
old = "        BtnClear.Click += (_, _) => ClearResults();\n"
new = ("        BtnClear.Click += (_, _) => ClearResults();\n"
       "        BtnDefectReset.Click += (_, _) => ResetDefectParams();\n")
assert s.count(old) == 1, ('wire', s.count(old))
s = s.replace(old, new)

# ---- 2) pass the panel values into DetectDefects + time it ------------------
old = """            var defects = await Task.Run(() => _matcher.DetectDefects(results), _matchCts.Token);
            foreach (var d in defects) Defects.Add(d);
            DefectSummaryText = BuildDefectSummary(defects);
            StatusText = $"匹配完成: {results.Count} 个结果, 缺陷 {defects.Count} 处, 匹配耗时 {_matcher.LastMatchMs:F1} ms";"""
new = """            var dp = ReadDefectParams();
            var swDefect = System.Diagnostics.Stopwatch.StartNew();
            var defects = await Task.Run(
                () => _matcher.DetectDefects(results, dp.Diff, dp.MinAreaFrac, dp.Global,
                                             dp.EdgeTol, dp.EdgeGrad, dp.Erode, dp.Dilate),
                _matchCts.Token);
            swDefect.Stop();
            foreach (var d in defects) Defects.Add(d);
            DefectSummaryText = BuildDefectSummary(defects);
            StatusText = $"匹配完成: {results.Count} 个结果, 缺陷 {defects.Count} 处, " +
                         $"匹配 {_matcher.LastMatchMs:F1} ms, 缺陷检测 {swDefect.Elapsed.TotalMilliseconds:F1} ms";"""
assert s.count(old) == 1, ('call', s.count(old))
s = s.replace(old, new)

# ---- 3) params reader + reset ----------------------------------------------
old = """    private void ChkDefect_Changed(object sender, RoutedEventArgs e)
    {"""
new = '''    /// <summary>Snapshot of the defect panel, already clamped and unit-converted.</summary>
    private readonly record struct DefectParams(
        double Diff, double MinAreaFrac, double Global,
        int EdgeTol, double EdgeGrad, int Erode, int Dilate);

    /// <summary>
    /// Reads the defect-parameter textboxes. Every value is clamped into the range the detector
    /// can actually use, so a typo cannot silently produce a nonsense mask (a 99-px erode kernel
    /// would wipe out every defect). The panel takes 最小面积占比 as a percentage for
    /// readability; the detector wants a fraction of the template area.
    /// </summary>
    private DefectParams ReadDefectParams()
    {
        double diff = Math.Clamp(Parse(TbDefectDiff.Text, 45), 1, 254);
        double minAreaPct = Math.Clamp(Parse(TbDefectMinArea.Text, 0.4), 0.001, 50);
        double global = Math.Clamp(Parse(TbDefectGlobal.Text, 28), 1, 254);
        int edgeTol = (int)Math.Clamp(Parse(TbDefectEdgeTol.Text, 3), 0, 30);
        double edgeGrad = Math.Clamp(Parse(TbDefectEdgeGrad.Text, 30), 1, 254);
        int erode = (int)Math.Clamp(Parse(TbDefectErode.Text, 2), 0, 15);
        int dilate = (int)Math.Clamp(Parse(TbDefectDilate.Text, 3), 0, 15);
        return new DefectParams(diff, minAreaPct / 100.0, global, edgeTol, edgeGrad, erode, dilate);
    }

    private void ResetDefectParams()
    {
        TbDefectDiff.Text = "45";
        TbDefectEdgeTol.Text = "3";
        TbDefectEdgeGrad.Text = "30";
        TbDefectErode.Text = "2";
        TbDefectDilate.Text = "3";
        TbDefectMinArea.Text = "0.4";
        TbDefectGlobal.Text = "28";
        StatusText = "缺陷参数已恢复默认";
    }

    private void ChkDefect_Changed(object sender, RoutedEventArgs e)
    {'''
assert s.count(old) == 1, ('methods', s.count(old))
s = s.replace(old, new)

# ---- 4) ensure `using System;` is present (Math.Clamp) ----------------------
if 'using System;' not in s:
    s = s.replace('using Microsoft', 'using System;\nusing Microsoft', 1)

assert s != orig
io.open(p, 'w', encoding='utf-8').write(s)
print('PATCHED', p, len(orig), '->', len(s))
