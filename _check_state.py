# -*- coding: utf-8 -*-
import re

c = open(r'D:\wqz\code\GrayMatch\GrayMatch.Wpf\MainWindow.xaml.cs', 'rb').read().decode('utf-8', 'replace')
print('C# SldThreshold.Value = 0.80 :', c.count('SldThreshold.Value = 0.80'))
print('C# SldThreshold.Value = 0.50 :', c.count('SldThreshold.Value = 0.50'))
print('C# Threshold prop = 0.80     :', c.count('public double Threshold { get; set; } = 0.80;'))
print('C# SweepTest left            :', c.count('SweepTest'))
print('C# DiagLog left              :', c.count('DiagLog'))
print('C# webp / WPF fallback       :', c.count('".webp"'), c.count('TryDecodeWithWpf'))
print('C# _loadingSettings          :', c.count('_loadingSettings'))

x = open(r'D:\wqz\code\GrayMatch\GrayMatch.Wpf\MainWindow.xaml', 'rb').read().decode('utf-8', 'replace')
m = re.search(r'<Slider x:Name="SldThreshold"[^>]*>', x)
print('XAML slider:', m.group(0)[:130] if m else 'NOT FOUND')
m2 = re.search(r'<TextBlock x:Name="TbThresholdVal"[^/]*/>', x)
print('XAML label :', m2.group(0) if m2 else 'NOT FOUND')
print('XAML Viewbox:', 'x:Name="ImageViewport"' in x and 'Stretch="Uniform"' in x)
print('XAML dense default:', re.search(r'<CheckBox x:Name="ChkDense"[^>]*IsChecked="(\w+)"', x).group(1))
