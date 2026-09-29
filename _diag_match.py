# -*- coding: utf-8 -*-
"""临时：在匹配后记录参数与命中数，用于评估“重叠目标各自检出”的效果。"""
p = r'D:\wqz\code\GrayMatch\GrayMatch.Wpf\MainWindow.xaml.cs'
s = open(p, 'rb').read().decode('utf-8')

old = '        Results.Clear();\r\n        Results.AddRange(results);\r\n'
n = s.count(old)
print('anchor:', n)
assert n == 1, n

new = ('        Results.Clear();\r\n'
       '        Results.AddRange(results);\r\n'
       '        DiagLog($"MATCH thr={threshold:F2} overlap={overlap:F2} dense={ChkDense.IsChecked} ' +
       'topN={topN} pyramid={pyramid} -> {results.Count} hits");\r\n')
s = s.replace(old, new, 1)

old2 = '    private void BtnMatchDefaults_Click(object sender, RoutedEventArgs e)\r\n'
n2 = s.count(old2)
assert n2 == 1, n2
helper = ('    internal static void DiagLog(string message)\r\n'
          '    {\r\n'
          '        try\r\n'
          '        {\r\n'
          '            System.IO.Directory.CreateDirectory(AppDataDir);\r\n'
          '            System.IO.File.AppendAllText(System.IO.Path.Combine(AppDataDir, "diag.log"),\r\n'
          '                $"[{DateTime.Now:HH:mm:ss.fff}] {message}\\r\\n");\r\n'
          '        }\r\n'
          '        catch { }\r\n'
          '    }\r\n'
          '\r\n')
s = s.replace(old2, helper + old2, 1)

open(p, 'wb').write(s.encode('utf-8'))
print('diag added')
