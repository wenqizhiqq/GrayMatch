src2 = 'GrayMatch.Wpf/MainWindow.xaml.cs'
with open(src2, 'rb') as f:
    data = f.read()

old_str = ('        if (_matcher.Template == null)\r\n'
           '        {\r\n'
           '            MessageBox.Show("请先创建模板。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);\r\n'
           '            return;\r\n'
           '        }')
new_str = ('        if (!_matcher.HasSource)\r\n'
           '        {\r\n'
           '            MessageBox.Show("请先打开一张图片。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);\r\n'
           '            return;\r\n'
           '        }\r\n\r\n'
           '        if (_matcher.Template == null)\r\n'
           '        {\r\n'
           '            MessageBox.Show("请先创建模板。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);\r\n'
           '            return;\r\n'
           '        }')

old_b = old_str.encode('gbk')
new_b = new_str.encode('gbk')

if old_b not in data:
    print('FAIL: pattern not found')
else:
    data = data.replace(old_b, new_b, 1)
    with open(src2 + '.new', 'wb') as f:
        f.write(data)
    print('OK: MainWindow.xaml.cs patched')
