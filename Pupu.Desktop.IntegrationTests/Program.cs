using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Pupu.Application;
using Pupu.Behavior;
using Pupu.Desktop.Models;
using Pupu.Desktop.Services;
using Pupu.Desktop.ViewModels;

namespace Pupu.Desktop.IntegrationTests;

internal static class Program
{
    private const string OwnerMessage = "朴朴，今天陪我说句话。";
    private const string CalmReply = "收到。我会安静地陪着你。";
    private const string EnergeticReply = "好呀！我们马上开心地聊起来！";
    private const string CalmPrompt = "说话要安静克制，先回应事实，不要卖萌或使用感叹号。";
    private const string EnergeticPrompt = "说话要热情活泼，明显表达开心，并使用感叹号。";
    private const string OwnerMemory = "主人不开心时，希望我先安静陪在旁边。";

    [STAThread]
    public static async Task<int> Main()
    {
        var dataRoot = Path.Combine(
            Path.GetTempPath(),
            $"PupuDesktop-ChatIntegration-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("PUPU_DATA_ROOT", dataRoot);
        Directory.CreateDirectory(dataRoot);

        try
        {
            var handler = new MockModelApiHandler();
            var credentialStore = new TestCredentialStore();
            var modelApi = new ModelApiService(
                new PetSpeechComposer(),
                handler,
                credentialStore);
            await modelApi.SaveAsync(
                new ModelApiSettings
                {
                    Enabled = true,
                    Provider = ModelProvider.Custom,
                    ApiFormat = ModelApiFormat.OpenAiChat,
                    Endpoint = "http://127.0.0.1/pupu-integration/chat/completions",
                    Model = "pupu-integration-model",
                    ConversationTurns = 8
                },
                "pupu-integration-key");

            using var viewModel = new MainViewModel(
                new TestPresentationHost(),
                new TestAssetPackService(),
                modelApi,
                new TestCodexIterationService(),
                new TestDesktopEnvironmentProbe(),
                new TestClock(new DateTimeOffset(2026, 8, 5, 10, 0, 0, TimeSpan.FromHours(8))),
                new SeededRandomSource(20260805));

            await WaitUntilAsync(() => viewModel.IsReady, "view model initialization");
            viewModel.PetSelfReference = "本喵";
            viewModel.OwnerPersonalityPrompt = CalmPrompt;
            viewModel.SavePetProfileCommand.Execute(null);
            await WaitUntilAsync(
                () => viewModel.PetProfileSaveStatus.Contains("已保存并立即生效", StringComparison.Ordinal),
                "profile and owner prompt persistence");

            viewModel.EditableMemoryText = viewModel.EditableMemoryText.Replace(
                $"## 主人自由编辑的长期记忆{Environment.NewLine}",
                $"## 主人自由编辑的长期记忆{Environment.NewLine}- {OwnerMemory}{Environment.NewLine}",
                StringComparison.Ordinal);
            viewModel.SaveEditableMemoryCommand.Execute(null);
            await WaitUntilAsync(
                () => viewModel.EditableMemoryStatus.Contains("已保存并应用到下一次对话", StringComparison.Ordinal),
                "owner memory persistence");
            viewModel.ChatInput = OwnerMessage;
            Assert(viewModel.SendChatCommand.CanExecute(null),
                "SendChatCommand was disabled for a ready non-empty chat input.");
            viewModel.SendChatCommand.Execute(null);

            await WaitUntilAsync(
                () => viewModel.ChatMessages.Any(message =>
                    message.Role == "owner" && message.Text == OwnerMessage),
                "owner message enqueue");
            await WaitUntilAsync(
                () => !viewModel.IsChatBusy &&
                      viewModel.ChatMessages.Any(message =>
                          message.Role == "pupu" && message.Text == CalmReply),
                "calm role prompt reply propagation");

            viewModel.OwnerPersonalityPrompt = EnergeticPrompt;
            var previousProfileStatus = viewModel.PetProfileSaveStatus;
            viewModel.SavePetProfileCommand.Execute(null);
            await WaitUntilAsync(
                () => !string.Equals(
                          viewModel.PetProfileSaveStatus,
                          previousProfileStatus,
                          StringComparison.Ordinal) &&
                      viewModel.PetProfileSaveStatus.Contains("已保存并立即生效", StringComparison.Ordinal),
                "updated owner prompt persistence");
            viewModel.ChatInput = OwnerMessage;
            viewModel.SendChatCommand.Execute(null);
            await WaitUntilAsync(
                () => !viewModel.IsChatBusy &&
                      viewModel.ChatMessages.Any(message =>
                          message.Role == "pupu" && message.Text == EnergeticReply),
                "energetic role prompt reply propagation");

            Assert(handler.RequestCount == 2,
                "The model transport did not receive both same-question requests.");
            Assert(handler.Authorization == "Bearer pupu-integration-key",
                "The request did not use the isolated credential store.");
            Assert(handler.RequestBodies.All(body => JsonContainsString(body, OwnerMessage)),
                "The owner message did not reach the serialized model request.");
            Assert(handler.RequestBodies.All(body => JsonContainsString(body, "你只扮演档案中的桌面宠物")),
                "The pet identity system prompt did not reach the model request.");
            Assert(handler.RequestBodies.All(body => JsonContainsString(body, "宠物自称为“本喵”")),
                "The saved pet self-reference did not reach the model request.");
            Assert(JsonContainsString(handler.RequestBodies[0], CalmPrompt) &&
                   JsonContainsString(handler.RequestBodies[1], EnergeticPrompt),
                "Changing the saved owner role prompt did not change the next model request.");
            Assert(handler.RequestBodies.All(body => JsonContainsString(body, OwnerMemory)),
                "The saved editable long-term memory did not reach the model request.");
            Assert(viewModel.IsBubbleVisible &&
                   viewModel.BubbleText.Contains(EnergeticReply, StringComparison.Ordinal),
                "The changed-prompt model reply did not reach the desktop bubble state.");
            Assert(viewModel.ModelApiStatus.Contains("角色边界检查", StringComparison.Ordinal),
                "The model success status was not exposed to the panel.");

            var conversationPath = Path.Combine(dataRoot, "memory", "conversation.json");
            await WaitUntilAsync(() => File.Exists(conversationPath), "conversation persistence");
            var conversation = await File.ReadAllTextAsync(conversationPath);
            Assert(JsonContainsString(conversation, OwnerMessage) &&
                   JsonContainsString(conversation, CalmReply) &&
                   JsonContainsString(conversation, EnergeticReply),
                "Both prompt-differentiated exchanges were not persisted to the isolated conversation store.");

            Console.WriteLine("[PASS] same question -> changed owner role prompt -> visibly different reply and bubble");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FAIL] desktop chat integration: {ex}");
            return 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable("PUPU_DATA_ROOT", null);
            await Task.Delay(150);
            try { Directory.Delete(dataRoot, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, string operation)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (!predicate())
        {
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException($"Timed out while waiting for {operation}.");
            await Task.Delay(40);
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

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class MockModelApiHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public List<string> RequestBodies { get; } = new();
        public string Authorization { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            var requestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            RequestBodies.Add(requestBody);
            Authorization = request.Headers.Authorization?.ToString() ?? string.Empty;
            var reply = JsonContainsString(requestBody, EnergeticPrompt)
                ? EnergeticReply
                : CalmReply;
            var json = JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content = reply } } }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class TestCredentialStore : IModelCredentialStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        public bool Exists(string target) => _values.ContainsKey(target);
        public string? Read(string target) =>
            _values.TryGetValue(target, out var value) ? value : null;
        public void Write(string target, string secret) => _values[target] = secret;
        public void Delete(string target) => _values.Remove(target);
    }

    private sealed class TestUiTimer(TimeSpan interval) : IUiTimer
    {
        public TimeSpan Interval { get; set; } = interval;
        public event EventHandler? Tick;
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
        public void Fire() => Tick?.Invoke(this, EventArgs.Empty);
    }

    private sealed class TestPresentationHost : IDesktopPresentationHost
    {
        public IUiTimer CreateTimer(TimeSpan interval) => new TestUiTimer(interval);
        public object CropImage(object? source, int x, int y, int width, int height) => new();
        public object LoadImage(string? path, int decodePixelWidth) => new();
        public string? SelectImageFile(string title) => null;
        public void ShowActionPreview(
            string title,
            IReadOnlyList<object> frames,
            IReadOnlyList<int> frameDurations,
            bool loop) { }
        public bool Confirm(string title, string message) => false;
        public void ReportRecoverableException(Exception exception, string context) =>
            throw new InvalidOperationException($"Unexpected recoverable error in {context}.", exception);
        public void Shutdown() { }
    }

    private sealed class TestAssetPackService : IAssetPackService
    {
        private readonly object _sheet = new();
        public AssetPackManifest Manifest { get; } = new()
        {
            SchemaVersion = 2,
            Name = "integration-test-pack",
            Version = "test",
            CellSize = 256
        };
        public int CellSize => 256;
        public string DisplayStatus => "integration-test-pack";
        public string CompatibilityStatus => "schema 2 integration test";
        public IReadOnlyList<AssetActionGroupStatus> ActionGroupStatuses { get; } =
            Array.Empty<AssetActionGroupStatus>();
        public object GetSheet(string id) => _sheet;
        public string EnsureEditableCopy() => string.Empty;
        public ResolvedAssetAnimation? ResolveActionGroup(string groupId) => null;
        public IReadOnlyList<object> CreatePreviewFrames(
            AssetActionGroupStatus status,
            int maximumFrames = 24) => Array.Empty<object>();
        public object CreateActionFrame(string groupId, int frame) => new();
        public object CreateCoinStateFrame(CoinAssetStateDefinition definition) => new();
    }

    private sealed class TestCodexIterationService : ICodexIterationService
    {
        public Task<string> LoadProjectPathAsync() => Task.FromResult(string.Empty);
        public Task<string> CreateIterationRequestAsync(
            string ownerRequest,
            string localPetContext,
            string projectPath) => Task.FromResult(string.Empty);
    }

    private sealed class TestDesktopEnvironmentProbe : IDesktopEnvironmentProbe
    {
        public bool IsForegroundApplicationFullScreen() => false;
    }

    private sealed class TestClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; } = now;
    }
}
