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
            print(repr(old[:120]))
            return False
        text = text.replace(old, new, 1)
    with open(path + '.new', 'wb') as f:
        f.write(text.encode('gbk', errors='replace'))
    print(f'OK: {path} patched -> {path}.new')
    return True

# ---------- MainWindow.xaml ----------
xaml_path = 'GrayMatch.Wpf/MainWindow.xaml'
with open(xaml_path, 'r', encoding='utf-8') as f:
    xaml = f.read()
old_xaml = '''                            ToolTip="层级越高越快，但过小模板或角度大时容易漏检。"/>


                    <CheckBox x:Name="ChkDense"'''
new_xaml = '''                            ToolTip="层级越高越快，但过小模板或角度大时容易漏检。"/>

                    <Button x:Name="BtnMatchDefaults" Content="恢复默认匹配参数" Height="24" Margin="0,4,0,4" FontSize="10.5"
                            Click="BtnMatchDefaults_Click"
                            ToolTip="将上方所有匹配参数恢复为推荐默认值（-45~45°, 步长2, 阈值0.30, 重叠0.25, TopN20, 金字塔3）"/>

                    <CheckBox x:Name="ChkDense"'''
if old_xaml not in xaml:
    print('FAIL: xaml anchor not found')
else:
    xaml = xaml.replace(old_xaml, new_xaml, 1)
    with open(xaml_path + '.new', 'w', encoding='utf-8') as f:
        f.write(xaml)
    print('OK: MainWindow.xaml patched -> .new')

# ---------- MainWindow.xaml.cs ----------
xaml_cs = [
    (
        '    private readonly DispatcherTimer _autoMatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };\n'
        '    private bool _autoMatchDirty;',
        '    private readonly DispatcherTimer _autoMatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };\n'
        '    private bool _autoMatchDirty;\n'
        '    private bool _suppressAutoMatch;'
    ),
    (
        '    private void MatchParam_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)\n'
        '    {\n'
        '        if (TbAngleStartVal != null) TbAngleStartVal.Text = SldAngleStart.Value.ToString("0");',
        '    private void MatchParam_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)\n'
        '    {\n'
        '        if (_suppressAutoMatch) return;\n'
        '        if (TbAngleStartVal != null) TbAngleStartVal.Text = SldAngleStart.Value.ToString("0");'
    ),
    (
        '        _ = RunMatchAsync(silent: true);\n'
        '    }\n\n'
        '    #endregion',
        '        _ = RunMatchAsync(silent: true);\n'
        '    }\n\n'
        '    private void BtnMatchDefaults_Click(object sender, RoutedEventArgs e)\n'
        '    {\n'
        '        _suppressAutoMatch = true;\n'
        '        SldAngleStart.Value = -45;\n'
        '        SldAngleEnd.Value = 45;\n'
        '        SldAngleStep.Value = 2;\n'
        '        SldThreshold.Value = 0.30;\n'
        '        SldOverlap.Value = 0.25;\n'
        '        SldTopN.Value = 20;\n'
        '        SldPyramid.Value = 3;\n'
        '        _suppressAutoMatch = false;\n'
        '        ScheduleAutoMatch();\n'
        '    }\n\n'
        '    #endregion'
    ),
    (
        '    private void ApplyPersistedSettings()\n'
        '    {\n'
        '        try\n'
        '        {\n'
        '            if (!File.Exists(SettingsFile)) return;',
        '    private void ApplyPersistedSettings()\n'
        '    {\n'
        '        try\n'
        '        {\n'
        '            _suppressAutoMatch = true;\n'
        '            if (!File.Exists(SettingsFile)) return;'
    ),
    (
        '            if (s.Contour && _matcher.Template != null) RefreshTemplateVisuals();\n'
        '        }\n'
        '        catch { /* ignore */ }\n'
        '    }',
        '            if (s.Contour && _matcher.Template != null) RefreshTemplateVisuals();\n'
        '        }\n'
        '        catch { /* ignore */ }\n'
        '        finally { _suppressAutoMatch = false; }\n'
        '    }'
    ),
]

patch_file('GrayMatch.Wpf/MainWindow.xaml.cs', xaml_cs, crlf=True)

# ---------- MainWindow.Defect.cs ----------
defect_cs = [
    (
        '    private void DefectParam_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)\n'
        '    {\n'
        '        ApplyDefectParams();\n'
        '        ScheduleAutoMatch();\n'
        '    }',
        '    private void DefectParam_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)\n'
        '    {\n'
        '        if (_suppressAutoMatch) return;\n'
        '        ApplyDefectParams();\n'
        '        ScheduleAutoMatch();\n'
        '    }'
    ),
    (
        '        SldDefectDiff.Value = 45;\n'
        '        SldDefectEdgeTol.Value = 0;\n'
        '        SldDefectEdgeGrad.Value = 30;\n'
        '        SldDefectErode.Value = 2;\n'
        '        SldDefectDilate.Value = 3;\n'
        '        SldDefectMinArea.Value = 0.4;\n'
        '        SldDefectGlobal.Value = 28;\n'
        '        ApplyDefectParams();\n'
        '        StatusText = "缂洪櫡鍙傛暟宸蹭慨澶嶉粯璁ゅ�";',
        '        SldDefectDiff.Value = 45;\n'
        '        SldDefectEdgeTol.Value = 0;\n'
        '        SldDefectEdgeGrad.Value = 30;\n'
        '        SldDefectErode.Value = 2;\n'
        '        SldDefectDilate.Value = 3;\n'
        '        SldDefectMinArea.Value = 0.4;\n'
        '        SldDefectGlobal.Value = 28;\n'
        '        ApplyDefectParams();\n'
        '        ScheduleAutoMatch();\n'
        '        StatusText = "缂洪櫡鍙傛暟宸蹭慨澶嶉粯璁ゅ�";'
    ),
]

patch_file('GrayMatch.Wpf/MainWindow.Defect.cs', defect_cs, crlf=False)
