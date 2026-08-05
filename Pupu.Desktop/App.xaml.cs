using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Pupu.Application;
using Pupu.Behavior;
using Pupu.Desktop.Diagnostics;
using Pupu.Desktop.Services;

namespace Pupu.Desktop;

public partial class App : System.Windows.Application
{
    private bool _errorDialogShown;
    private readonly object _smokeErrorGate = new();
    private readonly List<string> _smokeErrors = new();
    private DesktopSmokeOptions? _smokeOptions;
    private DeterministicModelApiHandler? _smokeApiHandler;

    protected override void OnStartup(StartupEventArgs e)
    {
        _smokeOptions = DesktopSmokeOptions.Parse(e.Args);
        if (_smokeOptions is not null)
        {
            Environment.SetEnvironmentVariable(
                StoragePaths.DataRootEnvironmentVariable,
                _smokeOptions.DataRoot);
            _smokeApiHandler = new DeterministicModelApiHandler();
        }
        base.OnStartup(e);
        WriteStartupMarker();
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteException(args.Exception, "unobserved task");
            RecordSmokeError(args.Exception);
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                WriteException(exception, "app domain fatal");
                RecordSmokeError(exception);
            }
        };
        DispatcherUnhandledException += (_, args) =>
        {
            var recoverable = !IsFatal(args.Exception);
            WriteException(args.Exception, "UI dispatcher");
            if (recoverable) args.Handled = true;
            ShowError(args.Exception, recoverable);
        };
        if (_smokeOptions is not null)
        {
            Dispatcher.BeginInvoke(
                new Action(RunSmokeTest),
                DispatcherPriority.ApplicationIdle);
        }
    }

    internal static IModelApiService CreateModelApiService()
    {
        if (Current is App { _smokeApiHandler: { } handler })
        {
            return new ModelApiService(
                new PetSpeechComposer(),
                handler,
                new InMemoryModelCredentialStore());
        }
        return new ModelApiService(new PetSpeechComposer());
    }

    internal static void ReportRecoverableException(Exception exception, string context)
    {
        WriteException(exception, context);
        if (Current is App app) app.ShowError(exception, recoverable: true);
    }

    private void ShowError(Exception exception, bool recoverable)
    {
        if (_smokeOptions is not null)
        {
            RecordSmokeError(exception);
            return;
        }
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

    private async void RunSmokeTest()
    {
        var options = _smokeOptions!;
        DesktopSmokeResult result;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(50));
            var window = await WaitForMainWindowAsync(timeout.Token);
            result = await DesktopSmokeTestRunner.RunAsync(
                window,
                _smokeApiHandler!,
                GetSmokeErrors,
                timeout.Token);
        }
        catch (Exception ex)
        {
            WriteException(ex, "desktop smoke test");
            result = DesktopSmokeResult.Failed(ex.Message);
        }

        try
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(options.ResultPath)
                ?? throw new InvalidOperationException("Smoke result directory is invalid."));
            await File.WriteAllTextAsync(
                options.ResultPath,
                JsonSerializer.Serialize(
                    result,
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            WriteException(ex, "write desktop smoke result");
            Shutdown(1);
            return;
        }

        Shutdown(result.Passed ? 0 : 1);
    }

    private async Task<MainWindow> WaitForMainWindowAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (MainWindow is global::Pupu.Desktop.MainWindow window && window.IsLoaded)
                return window;
            await Task.Delay(50, cancellationToken);
        }
        throw new TimeoutException("The Pupu main window did not load.");
    }

    private IReadOnlyList<string> GetSmokeErrors()
    {
        lock (_smokeErrorGate)
            return _smokeErrors.ToArray();
    }

    private void RecordSmokeError(Exception exception)
    {
        if (_smokeOptions is null) return;
        lock (_smokeErrorGate)
            _smokeErrors.Add($"{exception.GetType().Name}: {exception.Message}");
    }

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
