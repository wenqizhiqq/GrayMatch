# -*- coding: utf-8 -*-
p = r'D:\wqz\code\GrayMatch\GrayMatch.Wpf\MainWindow.xaml.cs'
s = open(p, 'rb').read().decode('utf-8')
ok = True


def rep1(s, old, new, tag):
    global ok
    n = s.count(old)
    if n != 1:
        print(f'  !! {tag}: count={n}')
        ok = False
        return s
    print(f'  ok {tag}')
    return s.replace(old, new, 1)


s = rep1(s, '        SldOverlap.Value = 0.25;', '        SldOverlap.Value = 0.10;', 'btn-overlap')
s = rep1(s, '        SldTopN.Value = 10;', '        SldTopN.Value = 200;', 'btn-topn')
s = rep1(s, '                TopN = SldTopN?.Value ?? 10,', '                TopN = SldTopN?.Value ?? 200,', 'save-topn-fallback')
s = rep1(s, '        public double TopN { get; set; } = 10;', '        public double TopN { get; set; } = 200;', 'topn-default')

open(p, 'wb').write(s.encode('utf-8'))
print('OK' if ok else 'FAILED')
