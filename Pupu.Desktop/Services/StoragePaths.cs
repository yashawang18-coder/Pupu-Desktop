using System.IO;

namespace Pupu.Desktop.Services;

public static class StoragePaths
{
    public const string DataRootEnvironmentVariable = "PUPU_DATA_ROOT";

    public static readonly string RootDirectory = ResolveRootDirectory();

    public static readonly string MemoryDirectory = Path.Combine(RootDirectory, "memory");
    public static readonly string LogDirectory = Path.Combine(RootDirectory, "logs");
    public static readonly string AssetDirectory = Path.Combine(RootDirectory, "assets");
    public static readonly string ProfileMediaDirectory = Path.Combine(RootDirectory, "profile-media");
    public static readonly string ProfileFile = Path.Combine(MemoryDirectory, "profile.json");
    public static readonly string StateFile = Path.Combine(MemoryDirectory, "state.json");
    public static readonly string EventsFile = Path.Combine(MemoryDirectory, "events.md");
    public static readonly string LegacyEventsFile = Path.Combine(MemoryDirectory, "events.jsonl");
    public static readonly string EditableMemoryFile = Path.Combine(MemoryDirectory, "pupu-memory.md");
    public static readonly string CodexRequestFile = Path.Combine(MemoryDirectory, "codex-iteration-request.md");
    public static readonly string CodexProjectPathFile = Path.Combine(MemoryDirectory, "codex-project-path.txt");
    public static readonly string CorrectionsFile = Path.Combine(MemoryDirectory, "corrections.json");
    public static readonly string SummaryFile = Path.Combine(MemoryDirectory, "summary.json");
    public static readonly string AlbumsFile = Path.Combine(MemoryDirectory, "albums.json");
    public static readonly string AlbumExperiencesFile = Path.Combine(MemoryDirectory, "album-experiences.json");
    public static readonly string BehaviorPolicyFile = Path.Combine(MemoryDirectory, "behavior-rules.json");
    public static readonly string ConversationFile = Path.Combine(MemoryDirectory, "conversation.json");
    public static readonly string ModelApiSettingsFile = Path.Combine(RootDirectory, "model-api.json");
    public static readonly string PersonalityBehaviorV2File = Path.Combine(MemoryDirectory, "personality-behavior-v2.json");
    public static readonly string BehaviorDecisionLog = Path.Combine(LogDirectory, "behavior-decisions.jsonl");
    public static readonly string ErrorLog = Path.Combine(LogDirectory, "pupu-error.log");

    public static string ProfileAvatarFile(string? fileName) =>
        string.IsNullOrWhiteSpace(fileName)
            ? string.Empty
            : Path.Combine(ProfileMediaDirectory, Path.GetFileName(fileName));

    private static string ResolveRootDirectory()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
        return string.IsNullOrWhiteSpace(overrideRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PupuDesktop")
            : Path.GetFullPath(overrideRoot.Trim());
    }
}
