using Pupu.Behavior;
using Pupu.Desktop.Models;
using Pupu.Desktop.Services;

namespace Pupu.Application;

public readonly record struct DesktopPoint(double X, double Y);

public interface IUiTimer : IDisposable
{
    TimeSpan Interval { get; set; }
    event EventHandler? Tick;
    void Start();
    void Stop();
}

public interface IDesktopPresentationHost
{
    IUiTimer CreateTimer(TimeSpan interval);
    object? CropImage(object? source, int x, int y, int width, int height);
    object? LoadImage(string? path, int decodePixelWidth);
    string? SelectImageFile(string title);
    void ShowActionPreview(
        string title,
        IReadOnlyList<object> frames,
        IReadOnlyList<int> frameDurations,
        bool loop);
    bool Confirm(string title, string message);
    void ReportRecoverableException(Exception exception, string context);
    void Shutdown();
}

public interface IDesktopEnvironmentProbe
{
    bool IsForegroundApplicationFullScreen();
}

public interface ICodexIterationService
{
    Task<string> LoadProjectPathAsync();
    Task<string> CreateIterationRequestAsync(
        string ownerRequest,
        string localPetContext,
        string projectPath);
}

public interface IModelApiService : IDisposable
{
    Task<ModelApiSettings> LoadAsync();
    Task SaveAsync(ModelApiSettings settings, string? apiKey);
    bool HasStoredApiKey(ModelApiSettings settings);
    void DeleteStoredApiKey(ModelApiSettings settings);
    Task<string> SendAsync(
        ModelApiSettings settings,
        PersonalityBehaviorState state,
        string identity,
        string memoryContext,
        string ownerMessage,
        CancellationToken cancellationToken = default);
    Task<string> SendAsync(
        ModelApiSettings settings,
        PersonalityBehaviorState state,
        string identity,
        string memoryContext,
        string ownerMessage,
        IReadOnlyList<ChatMessage>? history,
        IReadOnlyList<ModelImageInput>? images,
        CancellationToken cancellationToken = default);
    Task TestAsync(
        ModelApiSettings settings,
        PersonalityBehaviorState state,
        string identity,
        CancellationToken cancellationToken = default);
}

public interface IModelCredentialStore
{
    bool Exists(string target);
    string? Read(string target);
    void Write(string target, string secret);
    void Delete(string target);
}

public interface IAssetPackService
{
    AssetPackManifest Manifest { get; }
    int CellSize { get; }
    string DisplayStatus { get; }
    string CompatibilityStatus { get; }
    IReadOnlyList<AssetActionGroupStatus> ActionGroupStatuses { get; }
    object GetSheet(string id);
    string EnsureEditableCopy();
    ResolvedAssetAnimation? ResolveActionGroup(string groupId);
    IReadOnlyList<object> CreatePreviewFrames(
        AssetActionGroupStatus status,
        int maximumFrames = 24);
    object? CreateActionFrame(string groupId, int frame);
    object? CreateCoinStateFrame(CoinAssetStateDefinition definition);
}

public sealed class AssetActionGroupStatus
{
    public required string GroupId { get; init; }
    public required string BehaviorId { get; init; }
    public required string SourceLabel { get; init; }
    public required int FrameCount { get; init; }
    public required int FrameDurationMs { get; init; }
    public required string LoopMode { get; init; }
    public required string FallbackLabel { get; init; }
    public required string Validation { get; init; }
    public required string TriggerLabel { get; init; }
    public required IReadOnlyList<int> Frames { get; init; }
    public required IReadOnlyList<int> FrameDurationsMs { get; init; }
    public required string AtlasId { get; init; }
    public required int Row { get; init; }
    public required string File { get; init; }
    public required string SourceType { get; init; }
    public IReadOnlyList<int> IntroFrames { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> LoopFrames { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> ExitFrames { get; init; } = Array.Empty<int>();

    public string TimingLabel =>
        $"{FrameCount} 帧 · {FrameDurationMs} ms 基准 · {LoopMode} · " +
        $"I/L/E {IntroFrames.Count}/{LoopFrames.Count}/{ExitFrames.Count}";
}

public sealed class ResolvedAssetAnimation
{
    public required object Sheet { get; init; }
    public required int Row { get; init; }
    public required int FrameWidth { get; init; }
    public required int FrameHeight { get; init; }
    public required int[] Frames { get; init; }
    public required int[] FrameDurationsMs { get; init; }
    public required bool Loop { get; init; }
    public required bool Vertical { get; init; }
    public required bool AtlasRowSource { get; init; }
    public required string SourceLabel { get; init; }
    public string GroupId { get; init; } = string.Empty;
    public string BehaviorId { get; init; } = string.Empty;
    public string LoopMode { get; init; } = AssetLoopModes.Loop;
    public int[] IntroFrames { get; init; } = Array.Empty<int>();
    public int[] LoopFrames { get; init; } = Array.Empty<int>();
    public int[] ExitFrames { get; init; } = Array.Empty<int>();
    public string[] CompatiblePostures { get; init; } = Array.Empty<string>();
}
