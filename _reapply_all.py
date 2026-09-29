# -*- coding: utf-8 -*-
"""一次性重新应用 WebP/GIF 读取 + 默认参数 + 持久化防回写 + 加载兜底。"""
import io, sys

XAMLCS = r'D:\wqz\code\GrayMatch\GrayMatch.Wpf\MainWindow.xaml.cs'
MATCHER = r'D:\wqz\code\GrayMatch\GrayMatch\RotatedTemplateMatcher.cs'


def load(p):
    return open(p, 'rb').read().decode('utf-8')


def save(p, s):
    open(p, 'wb').write(s.encode('utf-8'))


def rep1(s, old, new, tag):
    n = s.count(old)
    if n != 1:
        print(f'  !! {tag}: count={n}')
        return s, False
    print(f'  ok {tag}')
    return s.replace(old, new, 1), True


ok_all = True

# ============ MainWindow.xaml.cs ============
s = load(XAMLCS)
print('MainWindow.xaml.cs:')

# 1) 打开文件夹时纳入 .webp
s, ok = rep1(s,
    'var exts = new HashSet<string> { ".bmp", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".gif" };',
    'var exts = new HashSet<string> { ".bmp", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".gif", ".webp" };',
    'webp-extension'); ok_all &= ok

# 2) 构造函数默认参数：密集模式勾选 + 阈值 0.80
s, ok = rep1(s,
    '        UpdateInfluenceFactors();\r\n        Title = "',
    ('        UpdateInfluenceFactors();\r\n'
     '\r\n'
     '        // Defaults: dense mode ON, NCC similarity threshold 0.80.\r\n'
     '        // Set explicitly here (not only in XAML) so the defaults cannot be lost.\r\n'
     '        // ApplyPersistedSettings() later overrides them with the user-saved values.\r\n'
     '        SldThreshold.Value = 0.80;\r\n'
     '        TbThresholdVal.Text = "0.80";\r\n'
     '        ChkDense.IsChecked = true;\r\n'
     '\r\n'
     '        Title = "'),
    'ctor-defaults'); ok_all &= ok

# 3) 持久化防回写
s, ok = rep1(s,
    '    private bool _suppressAutoMatch;\r\n',
    ('    private bool _suppressAutoMatch;\r\n'
     '    private bool _loadingSettings;   // 正在应用已保存参数：期间禁止回写，避免把默认值冲掉\r\n'),
    'loading-flag'); ok_all &= ok

s, ok = rep1(s,
    '    private void SaveSettings()\r\n    {\r\n',
    ('    private void SaveSettings()\r\n'
     '    {\r\n'
     '        // 应用已保存参数的过程中控件会触发 ValueChanged/Checked，\r\n'
     '        // 不拦住就会拿“半应用”的状态回写文件，反而破坏已保存的参数。\r\n'
     '        if (_loadingSettings) return;\r\n'),
    'save-guard'); ok_all &= ok

s, ok = rep1(s,
    '            _suppressAutoMatch = true;\r\n            if (!File.Exists(SettingsFile)) return;\r\n',
    ('            _suppressAutoMatch = true;\r\n'
     '            _loadingSettings = true;\r\n'
     '            if (!File.Exists(SettingsFile)) return;\r\n'),
    'loading-begin'); ok_all &= ok

s, ok = rep1(s,
    '        finally { _suppressAutoMatch = false; }\r\n',
    '        finally { _suppressAutoMatch = false; _loadingSettings = false; }\r\n',
    'loading-end'); ok_all &= ok

# 4) 默认参数按钮同步为 0.80
s, ok = rep1(s, '        SldThreshold.Value = 0.50;', '        SldThreshold.Value = 0.80;', 'btn-defaults'); ok_all &= ok
s, ok = rep1(s, '                Threshold = SldThreshold?.Value ?? 0.50,', '                Threshold = SldThreshold?.Value ?? 0.80,', 'save-fallback'); ok_all &= ok

# 5) 加载兜底：OpenCV 失败 → WPF 解码
s, ok = rep1(s,
    'await Task.Run(() => _matcher.LoadSource(path), token);',
    ('await Task.Run(() =>\r\n'
     '            {\r\n'
     '                // 先让 OpenCV 读；读不出来（GIF、以及 OpenCV 未编译 WebP 的场景）\r\n'
     '                // 再用 WPF 解码器兜底，保证 webp/gif 也能正常载入识别。\r\n'
     '                try\r\n'
     '                {\r\n'
     '                    _matcher.LoadSource(path);\r\n'
     '                }\r\n'
     '                catch (Exception)\r\n'
     '                {\r\n'
     '                    using var decoded = TryDecodeWithWpf(path);\r\n'
     '                    if (decoded == null) throw;\r\n'
     '                    _matcher.LoadSource(decoded, path);\r\n'
     '                }\r\n'
     '            }, token);'),
    'load-fallback'); ok_all &= ok

# 6) WPF 兜底解码器
helper = ('    /// <summary>\r\n'
          '    /// OpenCV 解码失败时的兜底读取：改用 WPF 自带解码器（WIC）。\r\n'
          '    /// OpenCV 对 WebP 的支持取决于编译选项、对 GIF 往往直接返回空图；\r\n'
          '    /// WIC 能解 WebP/GIF/BMP/PNG/JPEG/TIFF，这里统一转成 OpenCV 习惯的 BGR 三通道。\r\n'
          '    /// </summary>\r\n'
          '    private static OpenCvSharp.Mat? TryDecodeWithWpf(string path)\r\n'
          '    {\r\n'
          '        try\r\n'
          '        {\r\n'
          '            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);\r\n'
          '            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(\r\n'
          '                stream,\r\n'
          '                System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,\r\n'
          '                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);\r\n'
          '            if (decoder.Frames.Count == 0) return null;\r\n'
          '\r\n'
          '            var frame = decoder.Frames[0];\r\n'
          '            var bgra = new System.Windows.Media.Imaging.FormatConvertedBitmap(\r\n'
          '                frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);\r\n'
          '            int w = bgra.PixelWidth, h = bgra.PixelHeight;\r\n'
          '            if (w <= 0 || h <= 0) return null;\r\n'
          '\r\n'
          '            int stride = w * 4;\r\n'
          '            var buf = new byte[stride * h];\r\n'
          '            bgra.CopyPixels(buf, stride, 0);\r\n'
          '\r\n'
          '            var mat = new OpenCvSharp.Mat(h, w, OpenCvSharp.MatType.CV_8UC3);\r\n'
          '            var row = new byte[w * 3];\r\n'
          '            IntPtr dst = mat.Data;\r\n'
          '            int dstStride = (int)mat.Step();\r\n'
          '            for (int y = 0; y < h; y++)\r\n'
          '            {\r\n'
          '                int src = y * stride;\r\n'
          '                for (int x = 0; x < w; x++)\r\n'
          '                {\r\n'
          '                    row[x * 3] = buf[src + x * 4];         // B\r\n'
          '                    row[x * 3 + 1] = buf[src + x * 4 + 1]; // G\r\n'
          '                    row[x * 3 + 2] = buf[src + x * 4 + 2]; // R\r\n'
          '                }\r\n'
          '                Marshal.Copy(row, 0, dst + y * dstStride, row.Length);\r\n'
          '            }\r\n'
          '            return mat;\r\n'
          '        }\r\n'
          '        catch { return null; }\r\n'
          '    }\r\n'
          '\r\n')
s, ok = rep1(s, '    private void SaveSettings()\r\n    {\r\n', helper + '    private void SaveSettings()\r\n    {\r\n', 'wpf-decoder'); ok_all &= ok

# 7) lastFolder.txt 旧编码兼容
s, ok = rep1(s,
    '                var folder = File.ReadAllText(LastFolderFile).Trim();\r\n',
    ('                var folder = File.ReadAllText(LastFolderFile).Trim();\r\n'
     '                if (!Directory.Exists(folder))\r\n'
     '                {\r\n'
     '                    // 兼容历史版本写下的非 UTF-8（GBK）路径\r\n'
     '                    try\r\n'
     '                    {\r\n'
     '                        var raw = File.ReadAllBytes(LastFolderFile);\r\n'
     '                        var legacy = System.Text.Encoding.Default.GetString(raw).Trim();\r\n'
     '                        if (Directory.Exists(legacy)) folder = legacy;\r\n'
     '                    }\r\n'
     '                    catch { }\r\n'
     '                }\r\n'),
    'folder-encoding'); ok_all &= ok

save(XAMLCS, s)

# ============ RotatedTemplateMatcher.cs ============
print('RotatedTemplateMatcher.cs:')
m = load(MATCHER)
overload = ('    /// <summary>\r\n'
            '    /// 用“已经解码好的图像”装载图源。\r\n'
            '    /// OpenCV 读不出来的格式（GIF / 部分 WebP）由上层用 WPF（WIC）解码后走这里，\r\n'
            '    /// 保证匹配内核本身只依赖 OpenCvSharp。\r\n'
            '    /// </summary>\r\n'
            '    public void LoadSource(Mat decoded, string originPath)\r\n'
            '    {\r\n'
            '        if (decoded == null || decoded.Empty())\r\n'
            '            throw new ArgumentException("decoded image is empty.", nameof(decoded));\r\n'
            '\r\n'
            '        _dataLock.Wait();\r\n'
            '        try\r\n'
            '        {\r\n'
            '            DisposeSource();\r\n'
            '            _source = decoded.Clone();\r\n'
            '            if (_source.Channels() == 1)\r\n'
            '            {\r\n'
            '                _sourceGray = _source.Clone();\r\n'
            '            }\r\n'
            '            else\r\n'
            '            {\r\n'
            '                _sourceGray = new Mat();\r\n'
            '                Cv2.CvtColor(_source, _sourceGray, ColorConversionCodes.BGR2GRAY);\r\n'
            '            }\r\n'
            '            if (UseContour) _sourceContour = MakeContour(_sourceGray);\r\n'
            '        }\r\n'
            '        finally\r\n'
            '        {\r\n'
            '            _dataLock.Release();\r\n'
            '        }\r\n'
            '    }\r\n'
            '\r\n')
m, ok = rep1(m, '    public void SetSource(Mat image)\r\n', overload + '    public void SetSource(Mat image)\r\n', 'matcher-overload')
ok_all &= ok
save(MATCHER, m)

print('ALL APPLIED OK' if ok_all else 'SOME PATCHES FAILED')
sys.exit(0 if ok_all else 1)
