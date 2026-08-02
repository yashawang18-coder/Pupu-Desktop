using System.IO;
using System.Reflection;
using System.Windows;
using Pupu.Desktop.Services;

namespace Pupu.Desktop;

public partial class App : System.Windows.Application
{
    private bool _errorDialogShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        WriteStartupMarker();
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteException(args.Exception, "unobserved task");
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                WriteException(exception, "app domain fatal");
        };
        DispatcherUnhandledException += (_, args) =>
        {
            var recoverable = !IsFatal(args.Exception);
            WriteException(args.Exception, "UI dispatcher");
            if (recoverable) args.Handled = true;
            ShowError(args.Exception, recoverable);
        };
    }

    internal static void ReportRecoverableException(Exception exception, string context)
    {
        WriteException(exception, context);
        if (Current is App app) app.ShowError(exception, recoverable: true);
    }

    private void ShowError(Exception exception, bool recoverable)
    {
        if (_errorDialogShown) return;
        _errorDialogShown = true;
        var action = recoverable
            ? "异常已被拦截，Pupu 会继续运行。若动作停住，可在右键一级菜单选择“停下”。"
            : "这是无法安全恢复的系统级异常，应用可能需要关闭。";
        MessageBox.Show(
            $"系统诊断（不是宠物发言）\n\n{exception.Message}\n\n{action}\n日志：{StoragePaths.ErrorLog}",
            "Pupu · 系统状态",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static bool IsFatal(Exception exception) =>
        exception is OutOfMemoryException or AccessViolationException or BadImageFormatException;

    private static void WriteException(Exception exception, string context)
    {
        try
        {
            Directory.CreateDirectory(StoragePaths.LogDirectory);
            File.AppendAllText(
                StoragePaths.ErrorLog,
                $"[{DateTimeOffset.Now:O}] {context}\n{exception}\n\n");
        }
        catch
        {
            // Logging must never turn a recoverable problem into an exit.
        }
    }

    private static void WriteStartupMarker()
    {
        try
        {
            Directory.CreateDirectory(StoragePaths.LogDirectory);
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
            File.AppendAllText(
                StoragePaths.ErrorLog,
                $"[{DateTimeOffset.Now:O}] START Pupu {version} · Windows {Environment.OSVersion.Version}\n");
        }
        catch
        {
            // A startup marker is diagnostic only and must never block the pet.
        }
    }
}
