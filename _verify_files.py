# -*- coding: utf-8 -*-
import hashlib

files = {
    r'GrayMatch.Wpf/MainWindow.Zoom.cs': ['ImageScaleTransform', 'ImageTranslateTransform', 'ResetView'],
    r'GrayMatch.Wpf/MainWindow.xaml': ['Grid.RenderTransform', 'ImageArea_LostMouseCapture'],
    r'GrayMatch.Wpf/MainWindow.xaml.cs': ['.webp', 'TryDecodeWithWpf', 'SldThreshold.Value = 0.80'],
    r'GrayMatch/RotatedTemplateMatcher.cs': ['LoadSource(Mat decoded'],
    r'GrayMatch.Wpf/App.xaml.cs': ['CrashLogFile'],
}

for p, needles in files.items():
    d = open(p, 'rb').read()
    t = d.decode('utf-8', 'replace')
    flags = ', '.join(f'{n}={"Y" if n in t else "N"}' for n in needles)
    print(f'{p:42} {len(d):7d}  md5={hashlib.md5(d).hexdigest()[:12]}  {flags}')
