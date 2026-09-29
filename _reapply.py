# -*- coding: utf-8 -*-
import os, sys

root = r'D:\wqz\code\GrayMatch'

# ---------------- 1) 恢复 Zoom.cs ----------------
src = open(os.path.join(root, '_reapply_zoom.cs.txt'), 'rb').read().decode('utf-8')
src = src.replace('\r\n', '\n').replace('\n', '\r\n')
open(os.path.join(root, 'GrayMatch.Wpf', 'MainWindow.Zoom.cs'), 'wb').write(src.encode('utf-8'))
print('Zoom.cs restored:', os.path.getsize(os.path.join(root, 'GrayMatch.Wpf', 'MainWindow.Zoom.cs')))

# ---------------- 2) XAML：ScrollViewer -> Grid + RenderTransform ----------------
xp = os.path.join(root, 'GrayMatch.Wpf', 'MainWindow.xaml')
t = open(xp, 'rb').read().decode('utf-8')

old_open = (
'                <ScrollViewer x:Name="ImageViewport"\r\n'
'                              Background="#1D1D1F"\r\n'
'                              HorizontalScrollBarVisibility="Hidden"\r\n'
'                              VerticalScrollBarVisibility="Hidden"\r\n'
'                              HorizontalContentAlignment="Center"\r\n'
'                              VerticalContentAlignment="Center"\r\n'
'                              MouseWheel="ImageArea_MouseWheel"\r\n'
'                              MouseDown="ImageArea_MouseDown"\r\n'
'                              MouseMove="ImageArea_MouseMove"\r\n'
'                              MouseUp="ImageArea_MouseUp"\r\n'
'                              Loaded="ImageViewer_Loaded">\r\n')
new_open = (
'                <Grid x:Name="ImageViewport"\r\n'
'                      Background="#1D1D1F"\r\n'
'                      ClipToBounds="True"\r\n'
'                      MouseWheel="ImageArea_MouseWheel"\r\n'
'                      MouseDown="ImageArea_MouseDown"\r\n'
'                      MouseMove="ImageArea_MouseMove"\r\n'
'                      MouseUp="ImageArea_MouseUp"\r\n'
'                      LostMouseCapture="ImageArea_LostMouseCapture"\r\n'
'                      Loaded="ImageViewer_Loaded">\r\n')

old_tf = (
'                        <Grid.LayoutTransform>\r\n'
'                            <ScaleTransform x:Name="ImageScale" ScaleX="1" ScaleY="1"/>\r\n'
'                        </Grid.LayoutTransform>\r\n')
new_tf = (
'                        <!--\r\n'
'                            缩放/平移全部走渲染变换：逻辑尺寸 = 图像显示尺寸，\r\n'
'                            与视口滚动/居中布局完全解耦，中键拖动不会被 ScrollViewer 钳死。\r\n'
'                            变换原点在左上角 (0,0)，即“视口坐标 = 图像坐标 × 缩放 + 平移”。\r\n'
'                        -->\r\n'
'                        <Grid.RenderTransform>\r\n'
'                            <TransformGroup>\r\n'
'                                <ScaleTransform x:Name="ImageScaleTransform" ScaleX="1" ScaleY="1"/>\r\n'
'                                <TranslateTransform x:Name="ImageTranslateTransform" X="0" Y="0"/>\r\n'
'                            </TransformGroup>\r\n'
'                        </Grid.RenderTransform>\r\n')

old_close = '                </ScrollViewer>\r\n                <TextBlock Text="滚轮缩放 · 中键拖动平移 · 双击复位"'
new_close = '                </Grid>\r\n                <TextBlock Text="滚轮缩放 · 中键拖动平移 · 双击复位"'

for name, old, new in (('open', old_open, new_open), ('transform', old_tf, new_tf), ('close', old_close, new_close)):
    n = t.count(old)
    print(f'xaml {name}: {n}')
    if n != 1:
        sys.exit('ABORT xaml ' + name)
    t = t.replace(old, new, 1)

open(xp, 'wb').write(t.encode('utf-8'))
print('xaml patched:', os.path.getsize(xp))

# ---------------- 3) App.xaml.cs：崩溃日志 ----------------
ap = os.path.join(root, 'GrayMatch.Wpf', 'App.xaml.cs')
a = open(ap, 'rb').read().decode('utf-8')
old_app = ('using System.Windows;\r\n'
           '\r\n'
           'namespace GrayMatch.Wpf;\r\n'
           '\r\n'
           'public partial class App : Application\r\n'
           '{\r\n'
           '}\r\n')
n = a.count(old_app)
print('app anchor:', n)
if n != 1:
    sys.exit('ABORT app')
new_app = '''using System.Windows;
using System.Windows.Threading;

namespace GrayMatch.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 未捕获异常写 %LOCALAPPDATA%\\GrayMatch\\crash.log，避免“界面莫名消失”无从查起。
        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrashLog("Dispatcher", args.Exception);
            MessageBox.Show("程序发生异常：\\r\\n" + args.Exception.Message + "\\r\\n\\r\\n详情见：\\r\\n" + CrashLogFile,
                "GrayMatch 异常", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrashLog("AppDomain", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteCrashLog("Task", args.Exception);
            args.SetObserved();
        };
        base.OnStartup(e);
    }

    private static string CrashLogFile => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GrayMatch", "crash.log");

    private static void WriteCrashLog(string source, Exception? ex)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CrashLogFile)!);
            System.IO.File.AppendAllText(CrashLogFile,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\\r\\n{ex}\\r\\n\\r\\n");
        }
        catch { /* 记日志失败不影响功能 */ }
    }
}
'''.replace('\n', '\r\n')
open(ap, 'wb').write(a.replace(old_app, new_app, 1).encode('utf-8'))
print('App.xaml.cs patched:', os.path.getsize(ap))
