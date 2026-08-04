using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Pupu.Application;
using Pupu.Desktop.Models;

namespace Pupu.Desktop.Diagnostics;

internal sealed record DesktopSmokeOptions(string DataRoot, string ResultPath)
{
    public static DesktopSmokeOptions? Parse(IReadOnlyList<string> arguments)
    {
        if (!arguments.Any(value =>
                string.Equals(value, "--smoke-test", StringComparison.OrdinalIgnoreCase)))
            return null;

        var dataRoot = ReadValue(arguments, "--data-root") ?? Path.Combine(
            Path.GetTempPath(),
            $"PupuDesktop-Smoke-{Guid.NewGuid():N}");
        dataRoot = RequireAbsolutePath(dataRoot, "--data-root");
        var resultPath = ReadValue(arguments, "--smoke-result") ??
                         Path.Combine(dataRoot, "desktop-smoke-result.json");
        resultPath = RequireAbsolutePath(resultPath, "--smoke-result");
        return new DesktopSmokeOptions(dataRoot, resultPath);
    }

    private static string? ReadValue(IReadOnlyList<string> arguments, string key)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            if (!string.Equals(arguments[index], key, StringComparison.OrdinalIgnoreCase))
                continue;
            if (index + 1 >= arguments.Count ||
                arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"{key} requires a value.");
            return arguments[index + 1];
        }
        return null;
    }

    private static string RequireAbsolutePath(string path, string key)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException($"{key} must be an absolute path.");
        return Path.GetFullPath(path);
    }
}

internal sealed class InMemoryModelCredentialStore : IModelCredentialStore
{
    private readonly Dictionary<string, string> _secrets =
        new(StringComparer.Ordinal);

    public bool Exists(string target) => _secrets.ContainsKey(target);
    public string? Read(string target) =>
        _secrets.TryGetValue(target, out var secret) ? secret : null;
    public void Write(string target, string secret) => _secrets[target] = secret;
    public void Delete(string target) => _secrets.Remove(target);
}

internal sealed class DeterministicModelApiHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private string _lastRequestBody = string.Empty;
    private string _lastAuthorization = string.Empty;
    private int _requestCount;

    public const string Reply = "听见啦，朴朴把尾巴放在你旁边。";
    public int RequestCount => Volatile.Read(ref _requestCount);
    public string LastRequestBody
    {
        get { lock (_gate) return _lastRequestBody; }
    }
    public string LastAuthorization
    {
        get { lock (_gate) return _lastAuthorization; }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_gate)
        {
            _lastRequestBody = body;
            _lastAuthorization = request.Headers.Authorization?.ToString() ?? string.Empty;
        }
        Interlocked.Increment(ref _requestCount);
        var json = "{\"choices\":[{\"message\":{\"content\":\"" + Reply + "\"}}]}";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}

internal sealed class DesktopSmokeResult
{
    public bool Passed { get; init; }
    public string Status { get; init; } = string.Empty;
    public List<string> Steps { get; init; } = new();
    public int ModelRequestCount { get; init; }
    public string Reply { get; init; } = string.Empty;
    public List<string> Errors { get; init; } = new();

    public static DesktopSmokeResult Failed(string error) => new()
    {
        Passed = false,
        Status = "failed",
        Errors = new List<string> { error }
    };
}

internal static class DesktopSmokeTestRunner
{
    private const string OwnerMessage = "朴朴，陪我说句话。";

    public static async Task<DesktopSmokeResult> RunAsync(
        MainWindow window,
        DeterministicModelApiHandler handler,
        Func<IReadOnlyList<string>> readApplicationErrors,
        CancellationToken cancellationToken)
    {
        var steps = new List<string>();
        var viewModel = window.ViewModelForAutomation;
        await WaitUntilAsync(() => viewModel.IsReady, "view model initialization", cancellationToken);
        steps.Add("main-window-ready");

        viewModel.OpenControlPanelCommand.Execute(null);
        await WaitUntilAsync(
            () => window.IsControlPanelOpenForAutomation,
            "control panel opening",
            cancellationToken);
        steps.Add("control-panel-open");

        viewModel.ModelApiProvider = ModelProvider.Custom;
        viewModel.ModelApiRequestFormat = ModelApiFormat.OpenAiChat;
        viewModel.ModelApiEndpoint = "http://127.0.0.1/pupu-smoke/chat/completions";
        viewModel.ModelApiModel = "pupu-smoke-model";
        viewModel.ModelApiEnabled = true;
        viewModel.ModelApiKey = "pupu-smoke-key";
        viewModel.SaveModelApiCommand.Execute(null);
        await WaitUntilAsync(
            () => viewModel.ModelApiStatus.StartsWith("设置已保存", StringComparison.Ordinal),
            "model settings save",
            cancellationToken);
        steps.Add("isolated-model-settings-saved");

        viewModel.ChatInput = OwnerMessage;
        if (!viewModel.SendChatCommand.CanExecute(null))
            throw new InvalidOperationException("SendChatCommand was disabled after initialization.");
        viewModel.SendChatCommand.Execute(null);
        await WaitUntilAsync(
            () => viewModel.ChatMessages.Any(message =>
                message.Role == "owner" && message.Text == OwnerMessage),
            "owner chat enqueue",
            cancellationToken);
        await WaitUntilAsync(
            () => !viewModel.IsChatBusy &&
                  viewModel.ChatMessages.Any(message =>
                      message.Role == "pupu" && message.Text == DeterministicModelApiHandler.Reply),
            "mock model reply",
            cancellationToken);
        if (!viewModel.IsBubbleVisible ||
            !viewModel.BubbleText.Contains(DeterministicModelApiHandler.Reply, StringComparison.Ordinal))
            throw new InvalidOperationException("The model reply did not reach the desktop bubble.");
        if (handler.RequestCount != 1 ||
            !JsonContainsString(handler.LastRequestBody, OwnerMessage) ||
            handler.LastAuthorization != "Bearer pupu-smoke-key")
            throw new InvalidOperationException("The mock API did not receive the expected authenticated chat request.");
        steps.Add("chat-command-to-bubble");

        if (!viewModel.PetrificusTotalusCommand.CanExecute(null))
            throw new InvalidOperationException("Owner-forced magic command was disabled.");
        viewModel.PetrificusTotalusCommand.Execute(null);
        await WaitUntilAsync(
            () => viewModel.IsPetrified,
            "owner-forced petrification",
            cancellationToken);
        steps.Add("owner-forced-magic-active");

        if (!viewModel.ReleasePetrificationCommand.CanExecute(null))
            throw new InvalidOperationException("Petrification release command was disabled.");
        viewModel.ReleasePetrificationCommand.Execute(null);
        await WaitUntilAsync(
            () => !viewModel.IsPetrified,
            "petrification release",
            cancellationToken);
        steps.Add("owner-forced-magic-released");

        var errors = readApplicationErrors().ToList();
        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Application exception(s) were reported: " + string.Join(" | ", errors));

        return new DesktopSmokeResult
        {
            Passed = true,
            Status = "passed",
            Steps = steps,
            ModelRequestCount = handler.RequestCount,
            Reply = DeterministicModelApiHandler.Reply
        };
    }

    private static async Task WaitUntilAsync(
        Func<bool> predicate,
        string operation,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!predicate())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(15))
                throw new TimeoutException($"Timed out while waiting for {operation}.");
            await Task.Delay(50, cancellationToken);
        }
    }

    private static bool JsonContainsString(string json, string expected)
    {
        using var document = JsonDocument.Parse(json);
        return ContainsString(document.RootElement, expected);
    }

    private static bool ContainsString(JsonElement element, string expected)
    {
        if (element.ValueKind == JsonValueKind.String)
            return element.GetString()?.Contains(expected, StringComparison.Ordinal) == true;
        if (element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray().Any(value => ContainsString(value, expected));
        if (element.ValueKind == JsonValueKind.Object)
            return element.EnumerateObject().Any(property => ContainsString(property.Value, expected));
        return false;
    }
}
