import io

def patch_file(path, old_str, new_str):
    with open(path, 'rb') as f:
        raw = f.read()
    # 文件含 GBK 中文，用 gbk 解码做字符串层面替换
    text = raw.decode('gbk', errors='replace')
    if old_str not in text:
        print(f'FAIL: pattern not found in {path}')
        return False
    text = text.replace(old_str, new_str, 1)
    # 写回 GBK 编码
    with open(path + '.new', 'wb') as f:
        f.write(text.encode('gbk', errors='replace'))
    print(f'OK: {path} patched')
    return True

# Patch 1: RotatedTemplateMatcher.cs
patch_file(
    'GrayMatch/RotatedTemplateMatcher.cs',
    '    public Mat? Template => _template;',
    '    public Mat? Template => _template;\n    public bool HasSource => _sourceGray != null;'
)

# Patch 2: MainWindow.xaml.cs
patch_file(
    'GrayMatch.Wpf/MainWindow.xaml.cs',
    '''        if (_matcher.Template == null)
        {
            MessageBox.Show("请先创建模板。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }''',
    '''        if (!_matcher.HasSource)
        {
            MessageBox.Show("请先打开一张图片。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_matcher.Template == null)
        {
            MessageBox.Show("请先创建模板。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }'''
)
