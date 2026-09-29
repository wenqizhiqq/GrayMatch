# -*- coding: utf-8 -*-
import io

checks = [
    (r'GrayMatch.Wpf/MainWindow.xaml', ['ImageTranslateTransform', 'Grid.RenderTransform', 'ImageArea_LostMouseCapture']),
    (r'GrayMatch.Wpf/MainWindow.xaml.cs', ['.webp', 'TryDecodeWithWpf', '_loadingSettings', 'SldThreshold.Value = 0.80', 'LoadSource(decoded']),
    (r'GrayMatch/RotatedTemplateMatcher.cs', ['LoadSource(Mat decoded', 'originPath']),
    (r'GrayMatch.Wpf/App.xaml.cs', ['CrashLogFile']),
    (r'GrayMatch.Wpf/MainWindow.Zoom.cs', ['ImageScaleTransform', 'RenderTransform']),
]

for path, needles in checks:
    d = open(path, 'rb').read().decode('utf-8', 'replace')
    print(f'== {path}  ({len(d)} chars)')
    for n in needles:
        print(f'   {n!r:38} -> {n in d}')
