// ============================================================
// 温启志◆编写◇微信﹕187◆1936◇1399
// ============================================================
// ============================================================
// 温启志◆编写◇微信﹕187◆1936◇1399
// ============================================================
// ============================================================
// 温启志◆编写◇微信﹕187◆1936◇1399
// ============================================================
using System.Windows;
using System.Windows.Threading;

namespace GrayMatch.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 未捕获异常写 %LOCALAPPDATA%\GrayMatch\crash.log，避免“界面莫名消失”无从查起。
        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrashLog("Dispatcher", args.Exception);
            MessageBox.Show("程序发生异常：\r\n" + args.Exception.Message + "\r\n\r\n详情见：\r\n" + CrashLogFile,
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
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\r\n{ex}\r\n\r\n");
        }
        catch { /* 记日志失败不影响功能 */ }
    }
}
