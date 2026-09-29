# -*- coding: utf-8 -*-
p = r'D:\wqz\code\GrayMatch\GrayMatch.Wpf\MainWindow.xaml'
s = open(p, 'rb').read().decode('utf-8')
old = '                          SizeChanged="ImageGrid_SizeChanged">'
print('found:', s.count(old))
s = s.replace(old, '>', 1)
open(p, 'wb').write(s.encode('utf-8'))
print('removed SizeChanged handler from ImageGrid')
