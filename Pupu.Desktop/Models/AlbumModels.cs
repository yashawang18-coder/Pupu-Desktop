namespace Pupu.Desktop.Models;

public sealed class PhotoAlbumCatalog
{
    public int SchemaVersion { get; set; } = 2;
    public string RootDirectory { get; set; } = string.Empty;
    public List<PhotoSubAlbum> Albums { get; set; } = new();
    public List<PhotoDescriptionEntry> PhotoDescriptions { get; set; } = new();
    public ProfilePresentationSettings ProfilePresentation { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class PhotoDescriptionEntry
{
    public string RelativePath { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed record ParsedAlbumDirectoryMetadata(
    string Theme,
    DateTime? StartDate,
    DateTime? EndDate);

public sealed class ProfilePresentationSettings
{
    public string RelationshipStageOverride { get; set; } = string.Empty;
}

public sealed class PhotoSubAlbum
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "新的子相册";
    public string RelativeDirectory { get; set; } = ".";
    public string Theme { get; set; } = string.Empty;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string GrowthStage { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class PhotoAlbumSnapshot
{
    public Guid AlbumId { get; init; }
    public bool IsRoot { get; init; }
    public bool IsDiscovered { get; init; }
    public required string Name { get; init; }
    public required string DirectoryPath { get; init; }
    public string RelativeDirectory { get; init; } = ".";
    public string Theme { get; init; } = string.Empty;
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public string GrowthStage { get; init; } = string.Empty;
    public bool IsAvailable { get; init; }
    public int PhotoCount { get; init; }
    public string? CoverPath { get; init; }
}

public sealed class AlbumPhotoReference
{
    public Guid AlbumId { get; init; }
    public required string AlbumName { get; init; }
    public required string FullPath { get; init; }
    public required string RelativePath { get; init; }
    public required string FileName { get; init; }
    public string Theme { get; init; } = string.Empty;
    public string GrowthStage { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public DateTime CapturedAt { get; init; }
}

public sealed class PhotoAlbumSearchQuery
{
    public string Keyword { get; init; } = string.Empty;
    public string Theme { get; init; } = string.Empty;
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public Guid? AlbumId { get; init; }
}

public static class AlbumExperienceSourceTypes
{
    public const string PhotoDescription = "photoDescription";
    public const string MarkdownPost = "markdownPost";
    public const string JsonPost = "jsonPost";
    public const string TravelEvent = "travelEvent";
    public const string Manual = "manual";
}

public sealed class AlbumExperienceRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> ImageRelativePaths { get; set; } = new();
    public string SourceRelativePath { get; set; } = string.Empty;
    public DateTimeOffset? Date { get; set; }
    public List<string> Tags { get; set; } = new();
    public string Mood { get; set; } = string.Empty;
    public string BehaviorId { get; set; } = string.Empty;
    public double Importance { get; set; } = 0.5;
    public bool IncludeInConversation { get; set; } = true;
    public bool IncludeInBehaviorDecision { get; set; }
    public bool AllowLlm { get; set; } = true;
    public bool AllowRules { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public string SourceType { get; set; } = AlbumExperienceSourceTypes.Manual;
    public string AlbumName { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public string GrowthStage { get; set; } = string.Empty;
    public string SourceStatus { get; set; } = "ready";
}

public sealed class AlbumExperienceSettings
{
    public bool ScanImages { get; set; } = true;
    public bool ScanTextFiles { get; set; } = true;
    public bool AllowConversation { get; set; } = true;
    public bool AllowBehaviorDecision { get; set; } = true;
    public bool AllowSendImagesToLlm { get; set; }
    public bool AllowRuleMode { get; set; } = true;
    public bool IncludeTravelEvents { get; set; } = true;
    public int MaximumResults { get; set; } = 3;
    public int MaximumImages { get; set; } = 2;

    public void Normalize()
    {
        MaximumResults = Math.Clamp(MaximumResults, 1, 10);
        MaximumImages = Math.Clamp(MaximumImages, 0, 2);
    }
}

public sealed class AlbumExperienceBuildStatus
{
    public string State { get; set; } = "notBuilt";
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public int ScannedFileCount { get; set; }
    public int ExperienceCount { get; set; }
    public int ErrorCount { get; set; }
    public bool UsedBackgroundWorker { get; set; }
    public string Message { get; set; } = "经历索引尚未构建。";
}

public sealed class AlbumExperienceIndex
{
    public int SchemaVersion { get; set; } = 1;
    public string RootFingerprint { get; set; } = string.Empty;
    public string ContentFingerprint { get; set; } = string.Empty;
    public AlbumExperienceSettings Settings { get; set; } = new();
    public List<AlbumExperienceRecord> Records { get; set; } = new();
    public AlbumExperienceBuildStatus BuildStatus { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class AlbumExperienceSearchQuery
{
    public string Text { get; init; } = string.Empty;
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public string Mood { get; init; } = string.Empty;
    public string AlbumName { get; init; } = string.Empty;
    public string Theme { get; init; } = string.Empty;
    public string GrowthStage { get; init; } = string.Empty;
    public string BehaviorId { get; init; } = string.Empty;
    public bool ForLlm { get; init; }
    public bool ForRules { get; init; }
    public bool ForBehavior { get; init; }
    public int MaximumResults { get; init; } = 3;
}

public sealed record AlbumExperienceSearchResult(
    AlbumExperienceRecord Record,
    double Score);
