def patch_file(path, repls, crlf=True):
    with open(path, 'rb') as f:
        data = f.read()
    text = data.decode('gbk', errors='replace')
    for old, new in repls:
        if crlf:
            old = old.replace('\n', '\r\n')
            new = new.replace('\n', '\r\n')
        if old not in text:
            print(f'FAIL: pattern not found in {path}:')
            print(repr(old[:80]))
            return False
        text = text.replace(old, new, 1)
    with open(path + '.new', 'wb') as f:
        f.write(text.encode('gbk', errors='replace'))
    print(f'OK: {path} patched -> {path}.new')
    return True

xaml_cs = [
    (
        '    private CancellationTokenSource? _matchCts;',
        '    private CancellationTokenSource? _matchCts;\n'
        '    private readonly DispatcherTimer _autoMatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };\n'
        '    private bool _autoMatchDirty;'
    ),
    (
        '    private async Task RunMatchAsync()\n'
        '    {\n'
        '        if (!_matcher.HasSource)\n'
        '        {\n'
        '            MessageBox.Show("请先打开一张图片。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);\n'
        '            return;\n'
        '        }\n\n'
        '        if (_matcher.Template == null)\n'
        '        {\n'
        '            MessageBox.Show("请先创建模板。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);\n'
        '            return;\n'
        '        }',
        '    private async Task RunMatchAsync(bool silent = false)\n'
        '    {\n'
        '        _autoMatchDirty = false;\n'
        '        if (!_matcher.HasSource)\n'
        '        {\n'
        '            if (!silent) MessageBox.Show("请先打开一张图片。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);\n'
        '            return;\n'
        '        }\n\n'
        '        if (_matcher.Template == null)\n'
        '        {\n'
        '            if (!silent) MessageBox.Show("请先创建模板。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);\n'
        '            return;\n'
        '        }'
    ),
    (
        '        ChkDense.Checked += ChkDense_Changed;\n'
        '        ChkDense.Unchecked += ChkDense_Changed;\n'
        '    }',
        '        ChkDense.Checked += ChkDense_Changed;\n'
        '        ChkDense.Unchecked += ChkDense_Changed;\n\n'
        '        _autoMatchTimer.Tick += AutoMatchTimer_Tick;\n'
        '    }'
    ),
    (
        '        if (TbPyramidVal != null) TbPyramidVal.Text = SldPyramid.Value.ToString("0");\n'
        '        UpdateInfluenceFactors();\n'
        '        SaveSettings();\n'
        '    }\n\n'
        '    #endregion',
        '        if (TbPyramidVal != null) TbPyramidVal.Text = SldPyramid.Value.ToString("0");\n'
        '        UpdateInfluenceFactors();\n'
        '        SaveSettings();\n'
        '        ScheduleAutoMatch();\n'
        '    }\n\n'
        '    private void ScheduleAutoMatch()\n'
        '    {\n'
        '        _autoMatchDirty = true;\n'
        '        if (!_autoMatchTimer.IsEnabled)\n'
        '            _autoMatchTimer.Start();\n'
        '    }\n\n'
        '    private void AutoMatchTimer_Tick(object sender, EventArgs e)\n'
        '    {\n'
        '        if (!_autoMatchDirty) { _autoMatchTimer.Stop(); return; }\n'
        '        if (!BtnMatch.IsEnabled) return; // 正在匹配（手动或上一轮自动），等下一拍\n'
        '        if (!_matcher.HasSource || _matcher.Template == null) { _autoMatchDirty = false; _autoMatchTimer.Stop(); return; }\n'
        '        _autoMatchDirty = false;\n'
        '        _ = RunMatchAsync(silent: true);\n'
        '    }\n\n'
        '    #endregion'
    ),
]

defect_cs = [
    (
        '    private void DefectParam_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyDefectParams();',
        '    private void DefectParam_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)\n'
        '    {\n'
        '        ApplyDefectParams();\n'
        '        ScheduleAutoMatch();\n'
        '    }'
    ),
]

patch_file('GrayMatch.Wpf/MainWindow.xaml.cs', xaml_cs, crlf=True)
patch_file('GrayMatch.Wpf/MainWindow.Defect.cs', defect_cs, crlf=False)
