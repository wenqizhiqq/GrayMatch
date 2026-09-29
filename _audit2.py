# -*- coding: utf-8 -*-
import re

xaml = open(r'D:\wqz\code\GrayMatch\GrayMatch.Wpf\MainWindow.xaml', 'rb').read().decode('utf-8')
code = ''
for f in (r'D:\wqz\code\GrayMatch\GrayMatch.Wpf\MainWindow.Zoom.cs',
          r'D:\wqz\code\GrayMatch\GrayMatch.Wpf\MainWindow.xaml.cs',
          r'D:\wqz\code\GrayMatch\GrayMatch.Wpf\MainWindow.Defect.cs',
          r'D:\wqz\code\GrayMatch\GrayMatch.Wpf\MainWindow.FastCpp.cs',
          r'D:\wqz\code\GrayMatch\GrayMatch.Wpf\App.xaml.cs'):
    code += open(f, 'rb').read().decode('utf-8', 'replace')

pat = re.compile(r'(?:MouseWheel|MouseDown|MouseMove|MouseUp|LostMouseCapture|Loaded|SizeChanged|Click|ValueChanged|Checked|Unchecked|SelectionChanged|LayoutUpdated)="([A-Za-z_][A-Za-z0-9_]*)"')
missing = []
for m in pat.finditer(xaml):
    h = m.group(1)
    if ('void ' + h + '(') not in code and (h + '(') not in code:
        missing.append(h)
print('XAML handlers missing in code:', sorted(set(missing)) or 'none')

# 关键命名元素是否存在
for name in ('ImageViewport', 'ImageGrid', 'ImageScaleTransform', 'ImageTranslateTransform', 'SourceImage', 'ChkDense', 'SldThreshold'):
    print(f'  x:Name {name}:', f'x:Name="{name}"' in xaml)
