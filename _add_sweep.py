# -*- coding: utf-8 -*-
"""临时：加入参数扫描自测（GRAYMATCH_SWEEP=1 时启动即跑），用完即删。"""
p = r'D:\wqz\code\GrayMatch\GrayMatch.Wpf\MainWindow.xaml.cs'
s = open(p, 'rb').read().decode('utf-8')

# 1) 启动钩子
old = '        _ = LoadPersistedStateAsync();\r\n'
n = s.count(old)
print('hook anchor:', n)
assert n == 1, n
s = s.replace(old, '        _ = LoadPersistedStateAsync();\r\n'
                   '\r\n'
                   '        if (Environment.GetEnvironmentVariable("GRAYMATCH_SWEEP") == "1") SweepTest();\r\n', 1)

# 2) 扫描方法（插在 BtnMatchDefaults_Click 之前）
anchor = '    private void BtnMatchDefaults_Click(object sender, RoutedEventArgs e)\r\n'
n2 = s.count(anchor)
print('sweep anchor:', n2)
assert n2 == 1, n2

sweep = ('    private void SweepTest()\r\n'
         '    {\r\n'
         '        try\r\n'
         '        {\r\n'
         '            var tplPath = System.IO.Path.Combine(AppDataDir, "template.png");\r\n'
         '            if (!File.Exists(tplPath)) { DiagLog("SWEEP: no template"); return; }\r\n'
         '            using var tpl = OpenCvSharp.Cv2.ImRead(tplPath, OpenCvSharp.ImreadModes.Grayscale);\r\n'
         '            if (tpl == null || tpl.Empty()) { DiagLog("SWEEP: template unreadable"); return; }\r\n'
         '            _matcher.SetTemplate(tpl);\r\n'
         '            DiagLog($"SWEEP template {tpl.Width}x{tpl.Height}");\r\n'
         '\r\n'
         '            var folder = File.ReadAllText(LastFolderFile).Trim();\r\n'
         '            var files = Directory.GetFiles(folder, "*.*")\r\n'
         '                .Where(f => { var e = Path.GetExtension(f).ToLowerInvariant();\r\n'
         '                              return e == ".webp" || e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".bmp"; })\r\n'
         '                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();\r\n'
         '\r\n'
         '            foreach (var f in files)\r\n'
         '            {\r\n'
         '                try\r\n'
         '                {\r\n'
         '                    try { _matcher.LoadSource(f); }\r\n'
         '                    catch\r\n'
         '                    {\r\n'
         '                        using var dec = TryDecodeWithWpf(f);\r\n'
         '                        if (dec == null) throw;\r\n'
         '                        _matcher.LoadSource(dec, f);\r\n'
         '                    }\r\n'
         '                }\r\n'
         '                catch (Exception ex)\r\n'
         '                {\r\n'
         '                    DiagLog($"SWEEP {Path.GetFileName(f)}: LOAD FAIL {ex.GetType().Name}");\r\n'
         '                    continue;\r\n'
         '                }\r\n'
         '\r\n'
         '                var src = _matcher.Source;\r\n'
         '                foreach (double thr in new[] { 0.8, 0.6, 0.5, 0.4 })\r\n'
         '                {\r\n'
         '                    foreach (double ov in new[] { 0.10, 0.50 })\r\n'
         '                    {\r\n'
         '                        var r1 = _matcher.Match(4, -180, 180, 1, thr, ov, 200, 1);\r\n'
         '                        var r0 = _matcher.Match(4, -180, 180, 1, thr, ov, 200, 0);\r\n'
         '                        DiagLog($"SWEEP {Path.GetFileName(f)} ({src.Width}x{src.Height}) thr={thr:F2} ov={ov:F2} " +\r\n'
         '                                $"dense={r1.Count} sparse={r0.Count}");\r\n'
         '                    }\r\n'
         '                }\r\n'
         '            }\r\n'
         '        }\r\n'
         '        catch (Exception ex) { DiagLog("SWEEP ERROR " + ex); }\r\n'
         '    }\r\n'
         '\r\n')
s = s.replace(anchor, sweep + anchor, 1)

open(p, 'wb').write(s.encode('utf-8'))
print('sweep hook added')
