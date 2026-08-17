src = 'GrayMatch.Wpf/MainWindow.Defect.cs'
with open(src, 'rb') as f:
    data = f.read()
text = data.decode('gbk', errors='replace')

repls = [
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
        '        SldDefectGlobal.Value = 28;\n'
        '        ApplyDefectParams();',
        '        SldDefectGlobal.Value = 28;\n'
        '        ApplyDefectParams();\n'
        '        ScheduleAutoMatch();'
    ),
]
for old, new in repls:
    if old not in text:
        print('FAIL:', repr(old[:60]))
    else:
        text = text.replace(old, new, 1)
        print('OK')

with open(src + '.new', 'wb') as f:
    f.write(text.encode('gbk', errors='replace'))
print('wrote', src + '.new')
