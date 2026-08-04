using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pupu.Behavior;
using Pupu.Application;
using Pupu.Desktop.Models;

namespace Pupu.Desktop.Services;

public sealed class ModelApiService : IModelApiService
{
    private const string LegacyCredentialTarget = "PupuDesktop/ModelApi";
    private const int MaximumRetries = 2;
    private static readonly TimeSpan MaximumRetryAfter = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly HttpClient _httpClient;
    private readonly PetSpeechComposer _speech;
    private readonly IModelCredentialStore _credentialStore;
    private readonly ModelContextPrivacyFilter _contextPrivacy = new();
    private readonly ModelProtocolAdapter _protocol = new();
    private string _activeCredentialTarget = LegacyCredentialTarget;
    private bool _allowLegacyCredentialFallback = true;
    private bool _disposed;

    public ModelApiService(
        PetSpeechComposer speech,
        HttpMessageHandler? handler = null,
        IModelCredentialStore? credentialStore = null)
    {
        _speech = speech;
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
        _credentialStore = credentialStore ?? new WindowsModelCredentialStore();
        _httpClient.Timeout = TimeSpan.FromSeconds(45);
    }

    public async Task<ModelApiSettings> LoadAsync()
    {
        if (!File.Exists(StoragePaths.ModelApiSettingsFile))
        {
            var created = new ModelApiSettings();
            ModelProtocolAdapter.ApplyProviderDefaults(created);
            ActivateCredentialTarget(created, legacySettings: true);
            return created;
        }
        try
        {
            var bytes = await File.ReadAllBytesAsync(StoragePaths.ModelApiSettingsFile);
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            var providerWasExplicit = HasJsonProperty(root, "provider");
            var formatWasExplicit = HasJsonProperty(root, "apiFormat");
            var settings = JsonSerializer.Deserialize<ModelApiSettings>(bytes, Json)
                           ?? new ModelApiSettings();
            if (!providerWasExplicit)
                settings.Provider = ModelProtocolAdapter.InferLegacyProvider(settings.Endpoint);
            if (!formatWasExplicit)
                settings.ApiFormat = ModelProtocolAdapter.InferLegacyApiFormat(settings.Endpoint);
            ModelProtocolAdapter.ApplyProviderDefaults(settings);
            ActivateCredentialTarget(
                settings,
                legacySettings: !providerWasExplicit || !formatWasExplicit);
            return settings;
        }
        catch (JsonException)
        {
            var fallback = new ModelApiSettings();
            ModelProtocolAdapter.ApplyProviderDefaults(fallback);
            ActivateCredentialTarget(fallback, legacySettings: true);
            return fallback;
        }
    }

    public async Task SaveAsync(ModelApiSettings settings, string? apiKey)
    {
        ModelProtocolAdapter.ApplyProviderDefaults(settings);
        ValidateSettings(
            settings,
            requireEnabled: false,
            allowIncompleteWhenDisabled: true);
        Directory.CreateDirectory(StoragePaths.RootDirectory);
        var temporary = StoragePaths.ModelApiSettingsFile + ".tmp";
        await using (var stream = new FileStream(
                         temporary,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         8192,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, settings, Json);
            await stream.FlushAsync();
        }
        File.Move(temporary, StoragePaths.ModelApiSettingsFile, true);
        _activeCredentialTarget = CredentialTargetFor(settings);
        _allowLegacyCredentialFallback = false;
        if (!string.IsNullOrWhiteSpace(apiKey))
            _credentialStore.Write(_activeCredentialTarget, apiKey.Trim());
    }

    public bool HasStoredApiKey() =>
        _credentialStore.Exists(_activeCredentialTarget) ||
        (_allowLegacyCredentialFallback &&
         _credentialStore.Exists(LegacyCredentialTarget));

    public bool HasStoredApiKey(ModelApiSettings settings)
    {
        var target = CredentialTargetFor(settings);
        return _credentialStore.Exists(target) ||
               (_allowLegacyCredentialFallback &&
                string.Equals(target, _activeCredentialTarget, StringComparison.Ordinal) &&
                _credentialStore.Exists(LegacyCredentialTarget));
    }

    public void DeleteStoredApiKey()
    {
        _credentialStore.Delete(_activeCredentialTarget);
        if (_allowLegacyCredentialFallback)
            _credentialStore.Delete(LegacyCredentialTarget);
    }

    public void DeleteStoredApiKey(ModelApiSettings settings)
    {
        var target = CredentialTargetFor(settings);
        _credentialStore.Delete(target);
        if (_allowLegacyCredentialFallback &&
            string.Equals(target, _activeCredentialTarget, StringComparison.Ordinal))
            _credentialStore.Delete(LegacyCredentialTarget);
    }

    public async Task<string> SendAsync(
        ModelApiSettings settings,
        PersonalityBehaviorState state,
        string identity,
        string memoryContext,
        string ownerMessage,
        CancellationToken cancellationToken = default)
        => await SendCoreAsync(
            settings,
            state,
            identity,
            memoryContext,
            ownerMessage,
            history: null,
            images: null,
            requireEnabled: true,
            enforcePetBoundary: true,
            cancellationToken: cancellationToken);

    public async Task<string> SendAsync(
        ModelApiSettings settings,
        PersonalityBehaviorState state,
        string identity,
        string memoryContext,
        string ownerMessage,
        IReadOnlyList<ChatMessage>? history,
        IReadOnlyList<ModelImageInput>? images,
        CancellationToken cancellationToken = default)
        => await SendCoreAsync(
            settings,
            state,
            identity,
            memoryContext,
            ownerMessage,
            history,
            images,
            requireEnabled: true,
            enforcePetBoundary: true,
            cancellationToken: cancellationToken);

    private async Task<string> SendCoreAsync(
        ModelApiSettings settings,
        PersonalityBehaviorState state,
        string identity,
        string memoryContext,
        string ownerMessage,
        IReadOnlyList<ChatMessage>? history,
        IReadOnlyList<ModelImageInput>? images,
        bool requireEnabled,
        bool enforcePetBoundary,
        CancellationToken cancellationToken)
    {
        ModelProtocolAdapter.ApplyProviderDefaults(settings);
        ValidateSettings(
            settings,
            requireEnabled,
            allowIncompleteWhenDisabled: false);
        ValidateImageCapability(settings, images);
        var apiKey = ReadApiKey(settings);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("尚未保存模型 API 密钥。");

        // This is the final boundary before any local memory is serialized into
        // a remote request. Callers may keep full-fidelity local data, but the
        // model receives only a bounded, path-free context.
        var safeMemoryContext = _contextPrivacy.Prepare(memoryContext);
        var prompt = _speech.BuildSystemPrompt(state, identity, safeMemoryContext);
        var requestJson = _protocol.BuildRequestJson(
            settings,
            prompt,
            ownerMessage,
            history,
            images);

        for (var attempt = 0; ; attempt++)
        {
            using var request = CreateRequest(settings.Endpoint, apiKey, requestJson);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var reply = _protocol.ExtractReply(responseText);
                if (!enforcePetBoundary)
                    return string.IsNullOrWhiteSpace(reply)
                        ? throw new InvalidOperationException("模型服务已连接，但没有返回文字。")
                        : reply.Trim();
                if (_speech.TryNormalizePetReply(reply, out var safeReply))
                    return safeReply;
                throw new InvalidOperationException("模型回复不符合朴朴角色边界，已拦截。");
            }

            var retryDelay = attempt < MaximumRetries
                ? RetryDelayFor(response, attempt)
                : null;
            if (retryDelay is not null)
            {
                await Task.Delay(retryDelay.Value, cancellationToken);
                continue;
            }

            var serverMessage = ExtractSafeServerMessage(responseText);
            throw new InvalidOperationException(serverMessage.Length == 0
                ? $"模型服务返回 HTTP {(int)response.StatusCode}。请在面板检查地址、模型名或密钥。"
                : $"模型服务返回 HTTP {(int)response.StatusCode}：{serverMessage}");
        }
    }

    public async Task TestAsync(
        ModelApiSettings settings,
        PersonalityBehaviorState state,
        string identity,
        CancellationToken cancellationToken = default)
    {
        _ = await SendCoreAsync(
            settings,
            state,
            identity,
            string.Empty,
            "只用一句很短的话向主人打招呼。",
            history: null,
            images: null,
            requireEnabled: false,
            enforcePetBoundary: false,
            cancellationToken: cancellationToken);
    }

    private static HttpRequestMessage CreateRequest(
        string endpoint,
        string apiKey,
        string requestJson)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        return request;
    }

    private string? ReadApiKey(ModelApiSettings settings)
    {
        var target = CredentialTargetFor(settings);
        var apiKey = _credentialStore.Read(target);
        if (!string.IsNullOrWhiteSpace(apiKey)) return apiKey;
        if (_allowLegacyCredentialFallback &&
            string.Equals(target, _activeCredentialTarget, StringComparison.Ordinal))
            return _credentialStore.Read(LegacyCredentialTarget);
        return null;
    }

    private void ActivateCredentialTarget(
        ModelApiSettings settings,
        bool legacySettings)
    {
        _activeCredentialTarget = CredentialTargetFor(settings);
        _allowLegacyCredentialFallback = legacySettings;
        if (!legacySettings ||
            string.Equals(_activeCredentialTarget, LegacyCredentialTarget, StringComparison.Ordinal) ||
            _credentialStore.Exists(_activeCredentialTarget))
            return;

        var legacyKey = _credentialStore.Read(LegacyCredentialTarget);
        if (string.IsNullOrWhiteSpace(legacyKey)) return;
        try
        {
            _credentialStore.Write(_activeCredentialTarget, legacyKey);
        }
        catch (Win32Exception)
        {
            // The old credential remains a read-only fallback for this process.
        }
    }

    private static string CredentialTargetFor(ModelApiSettings settings)
    {
        var endpoint = ModelProtocolAdapter.NormalizeEndpoint(
            settings.Provider,
            settings.ApiFormat,
            settings.Endpoint);
        var material =
            $"{settings.Provider}\n{endpoint.Trim().TrimEnd('/')}";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return $"PupuDesktop/ModelApi/{settings.Provider}/{Convert.ToHexString(digest)[..24]}";
    }

    private static TimeSpan? RetryDelayFor(
        HttpResponseMessage response,
        int retryIndex)
    {
        if (response.StatusCode != HttpStatusCode.TooManyRequests &&
            (int)response.StatusCode < 500)
            return null;

        var retryAfter = response.Headers.RetryAfter;
        TimeSpan delay;
        if (retryAfter?.Delta is { } delta)
        {
            delay = delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }
        else if (retryAfter?.Date is { } retryDate)
        {
            delay = retryDate - DateTimeOffset.UtcNow;
            if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        }
        else
        {
            delay = TimeSpan.FromMilliseconds(400 * Math.Pow(2, retryIndex));
        }

        // Never retry earlier than Retry-After. If the server requests a much
        // longer pause, fail this foreground call instead of blocking the UI.
        return delay <= MaximumRetryAfter ? delay : null;
    }

    private static bool HasJsonProperty(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object &&
        root.EnumerateObject().Any(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

    private static void ValidateImageCapability(
        ModelApiSettings settings,
        IReadOnlyList<ModelImageInput>? images)
    {
        if (images is null || images.Count == 0 ||
            !settings.VisionEnabled || !settings.SendAlbumImages)
            return;
        var preset = ModelProtocolAdapter.GetPreset(settings.Provider);
        if (!preset.SupportsVision)
            throw new InvalidOperationException(
                $"{preset.DisplayName} 当前预设未声明视觉输入能力；请选择支持视觉的模型或使用 Custom 配置。");
    }

    private static string ExtractSafeServerMessage(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText)) return string.Empty;
        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            string? message = null;
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object &&
                    error.TryGetProperty("message", out var nested) &&
                    nested.ValueKind == JsonValueKind.String)
                    message = nested.GetString();
                else if (error.ValueKind == JsonValueKind.String)
                    message = error.GetString();
            }
            if (message is null &&
                root.TryGetProperty("message", out var direct) &&
                direct.ValueKind == JsonValueKind.String)
                message = direct.GetString();
            if (string.IsNullOrWhiteSpace(message)) return string.Empty;

            var safe = string.Join(
                ' ',
                message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            safe = RedactCredentialLikeText(safe);
            return safe.Length <= 240 ? safe : safe[..240].TrimEnd() + "…";
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string RedactCredentialLikeText(string value)
    {
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < words.Length; index++)
        {
            var word = words[index].Trim(',', '.', ':', ';', '"', '\'', '(', ')');
            if (word.StartsWith("sk-", StringComparison.OrdinalIgnoreCase) &&
                word.Length >= 12)
                words[index] = words[index].Replace(word, "[已隐藏密钥]", StringComparison.Ordinal);
        }
        return string.Join(' ', words);
    }

    private static void ValidateSettings(
        ModelApiSettings settings,
        bool requireEnabled,
        bool allowIncompleteWhenDisabled)
    {
        ModelProtocolAdapter.ApplyProviderDefaults(settings);
        if (requireEnabled && !settings.Enabled)
            throw new InvalidOperationException("模型对话尚未启用。");
        if (!settings.Enabled && !requireEnabled && allowIncompleteWhenDisabled)
            return;
        if (string.IsNullOrWhiteSpace(settings.Model))
            throw new InvalidOperationException("请填写模型名称。");
        var preset = ModelProtocolAdapter.GetPreset(settings.Provider);
        if (settings.ApiFormat == ModelApiFormat.OpenAiResponses &&
            !preset.SupportsResponses)
            throw new InvalidOperationException(
                $"{preset.DisplayName} 当前预设不支持 OpenAI Responses 格式。");
        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint))
            throw new InvalidOperationException("模型 API 地址无效。");
        var loopback = endpoint.IsLoopback;
        if (!loopback && endpoint.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("远程模型 API 必须使用 HTTPS；本机服务可以使用 localhost。");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }
}

internal sealed class WindowsModelCredentialStore : IModelCredentialStore
{
    public bool Exists(string target) => WindowsCredentialVault.Exists(target);
    public string? Read(string target) => WindowsCredentialVault.Read(target);
    public void Write(string target, string secret) => WindowsCredentialVault.Write(target, secret);
    public void Delete(string target) => WindowsCredentialVault.Delete(target);
}

internal static class WindowsCredentialVault
{
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public static bool Exists(string target) => Read(target) is not null;

    public static string? Read(string target)
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (!CredRead(target, CredentialTypeGeneric, 0, out var pointer))
        {
            if (Marshal.GetLastWin32Error() == ErrorNotFound) return null;
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                return string.Empty;
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public static void Write(string target, string secret)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows 凭据管理器仅在 Windows 上可用。");
        var bytes = Encoding.Unicode.GetBytes(secret);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = target,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredentialPersistLocalMachine,
                UserName = Environment.UserName
            };
            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    public static void Delete(string target)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!CredDelete(target, CredentialTypeGeneric, 0) &&
            Marshal.GetLastWin32Error() != ErrorNotFound)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential userCredential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(
        string target,
        uint type,
        uint reservedFlag,
        out IntPtr credentialPointer);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr credential);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }
}
