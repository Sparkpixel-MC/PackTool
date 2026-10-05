using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace PackTool;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static readonly string CrashLog = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PackTool", "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogCrash("AppDomain.UnhandledException", args.ExceptionObject as Exception);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash("DispatcherUnhandledException", e.Exception);
        MessageBox.Show($"发生错误：{e.Exception.Message}\n\n详细信息已写入 {CrashLog}",
            "Resource-Sync PackTool", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true; // keep the window open instead of hard-crashing
    }

    private static void LogCrash(string source, Exception? e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLog)!);
            File.AppendAllText(CrashLog,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{e}\n\n");
        }
        catch
        {
            // logging must never crash the app
        }
    }
}
