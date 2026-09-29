# -*- coding: utf-8 -*-
p = r'D:\wqz\code\GrayMatch\GrayMatch.Wpf\MainWindow.xaml.cs'
s = open(p, 'rb').read().decode('utf-8')
n = s.count('SldThreshold.Value = 0.80;')
print('occurs:', n)
s = s.replace('SldThreshold.Value = 0.80;', 'SldThreshold.Value = 0.50;')
open(p, 'wb').write(s.encode('utf-8'))
print('threshold default -> 0.50 (both call sites)')
