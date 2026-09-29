# -*- coding: utf-8 -*-
"""按实测结果定默认值：阈值 0.50（0.8 对当前模板/图片一个都找不到），并清理自测钩子。"""
import sys

XAMLCS = r'D:\wqz\code\GrayMatch\GrayMatch.Wpf\MainWindow.xaml.cs'
XAML = r'D:\wqz\code\GrayMatch\GrayMatch.Wpf\MainWindow.xaml'
ok_all = True


def rep1(s, old, new, tag):
    global ok_all
    n = s.count(old)
    if n != 1:
        print(f'  !! {tag}: count={n}')
        ok_all = False
        return s
    print(f'  ok {tag}')
    return s.replace(old, new, 1)


# ---------- 1) 清理自测钩子 ----------
s = open(XAMLCS, 'rb').read().decode('utf-8')

s = rep1(s, '        _ = LoadPersistedStateAsync();\r\n'
            '\r\n'
            '        if (Environment.GetEnvironmentVariable("GRAYMATCH_SWEEP") == "1") SweepTest();\r\n',
         '        _ = LoadPersistedStateAsync();\r\n', 'remove-hook')

i = s.find('    private void SweepTest()\r\n')
assert i > 0, 'sweep method not found'
j = s.find('    private void BtnMatchDefaults_Click(object sender, RoutedEventArgs e)\r\n', i)
assert j > i
s = s[:i] + s[j:]

s = rep1(s, '        DiagLog($"MATCH thr={threshold:F2} overlap={overlap:F2} dense={ChkDense.IsChecked} topN={topN} pyramid={pyramid} -> {results.Count} hits");\r\n',
         '', 'remove-match-log')

i = s.find('    internal static void DiagLog(string message)\r\n')
assert i > 0, 'DiagLog not found'
j = s.find('    private void BtnMatchDefaults_Click(object sender, RoutedEventArgs e)\r\n', i)
assert j > i
s = s[:i] + s[j:]

# ---------- 2) 阈值默认改 0.50（实测 0.8 在该模板/图片上 0 命中） ----------
s = rep1(s, '        SldThreshold.Value = 0.80;', '        SldThreshold.Value = 0.50;', 'ctor-threshold')
s = rep1(s, '        TbThresholdVal.Text = "0.80";', '        TbThresholdVal.Text = "0.50";', 'ctor-threshold-text')
s = rep1(s, '        public double Threshold { get; set; } = 0.80;', '        public double Threshold { get; set; } = 0.50;', 'settings-threshold')
s = rep1(s, '                Threshold = SldThreshold?.Value ?? 0.80,', '                Threshold = SldThreshold?.Value ?? 0.50,', 'save-threshold')
s = rep1(s, '        SldThreshold.Value = 0.80;', '        SldThreshold.Value = 0.50;', 'btn-threshold')

open(XAMLCS, 'wb').write(s.encode('utf-8'))

# ---------- 3) XAML 阈值默认 0.50 + 提示文案 ----------
t = open(XAML, 'rb').read().decode('utf-8')
t = rep1(t, '<Slider x:Name="SldThreshold" Style="{StaticResource ModernSlider}" Minimum="0" Maximum="1" Value="0.80" TickFrequency="0.01"',
         '<Slider x:Name="SldThreshold" Style="{StaticResource ModernSlider}" Minimum="0" Maximum="1" Value="0.50" TickFrequency="0.01"', 'xaml-threshold')
t = rep1(t, '<TextBlock x:Name="TbThresholdVal" Text="0.80" Style="{StaticResource ValueText}"/>',
         '<TextBlock x:Name="TbThresholdVal" Text="0.50" Style="{StaticResource ValueText}"/>', 'xaml-threshold-text')
t = rep1(t, 'ToolTip="越高越严格，匹配数量越少。反光/细笔画图建议 0.40 以上。"/>',
         'ToolTip="越高越严格，匹配数量越少。有遮挡/重叠的目标：阈值越高越容易整片漏掉，实测 0.80 常常一个都找不到，建议 0.40~0.60 起调。"/>', 'xaml-threshold-tip')
t = rep1(t, 'ToolTip="将上方所有匹配参数恢复为推荐默认值（-180~180°, 步长1, 阈值0.80, 重叠0.10, TopN200, 金字塔4, 密集模式勾选）"',
         'ToolTip="将上方所有匹配参数恢复为推荐默认值（-180~180°, 步长1, 阈值0.50, 重叠0.10, TopN200, 金字塔4, 密集模式勾选）"', 'xaml-defaults-tip')
open(XAML, 'wb').write(t.encode('utf-8'))

print('DONE' if ok_all else 'SOME FAILED')
sys.exit(0 if ok_all else 1)
