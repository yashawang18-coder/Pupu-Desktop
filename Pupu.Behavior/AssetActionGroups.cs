using System.Text.Json.Serialization;

namespace Pupu.Behavior;

public static class AssetActionSourceKinds
{
    public const string AtlasRow = "atlasRow";
    public const string SpriteStrip = "spriteStrip";
    public const string SingleFile = "singleFile";
}

public static class AssetLoopModes
{
    public const string Once = "once";
    public const string Loop = "loop";
    public const string PingPong = "pingPong";
    public const string Hold = "hold";
}

/// <summary>
/// Runtime atlas requirements shared by the desktop loader and regression tests.
/// Keep this contract independent from a specific generated asset-pack version so
/// build-time and startup-time validation cannot silently drift apart.
/// </summary>
public static class AssetGridContract
{
    public const int RequiredColumns = 8;

    public static IReadOnlyDictionary<string, int> MinimumRows { get; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["core"] = 6,
            ["life"] = 8,
            ["directions"] = 4,
            ["touch"] = 6,
            ["routines"] = 8,
            ["walkModes"] = 8,
            ["activity"] = 8,
            ["lifeEquipment"] = 3,
            ["motion"] = 10,
            ["gazeCoin"] = 3,
            ["litter"] = 4,
            ["specials"] = 5,
            ["seasonal"] = 4
        };
}

public sealed class AssetAtlasDefinition
{
    public string File { get; set; } = string.Empty;
    public int Columns { get; set; } = 8;
    public int Rows { get; set; }
    public List<string> RowActions { get; set; } = new();
}

public sealed class CoinAssetStateDefinition
{
    public string Atlas { get; set; } = "gazeCoin";
    public int Row { get; set; } = 2;
    public List<int> Frames { get; set; } = new();
    public List<int> FrameDurations { get; set; } = new();
}

public sealed class AssetPackManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "pupu 素材包";
    public string Version { get; set; } = "1";
    public int CellSize { get; set; } = 256;
    public Dictionary<string, AssetAtlasDefinition> Atlases { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, CoinAssetStateDefinition> CoinStates { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, AssetActionGroupDefinition> ActionGroups { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public AssetQualityRequirements QualityRequirements { get; set; } = new();
}

public sealed class AssetQualityRequirements
{
    public int CanvasWidth { get; set; } = 256;
    public int CanvasHeight { get; set; } = 256;
    public bool TransparentBackground { get; set; } = true;
    public int MinimumTransparentMargin { get; set; } = 20;
    public string ScalePolicy { get; set; } = "fixedBodyCoordinateSystem";
    public List<string> ScaleAnchors { get; set; } = new()
    {
        "head",
        "bodySkeleton",
        "footBaseline",
        "centerOfMass"
    };
    public List<string> RequiredCoinStates { get; set; } = new()
    {
        "normalColor",
        "normalFaded",
        "unhappyColor",
        "unhappyFaded",
        "back"
    };
    public List<string> MouseGazeRequirements { get; set; } = new();
    public List<string> KnownIssues { get; set; } = new();
}

/// <summary>
/// Platform-neutral action-group source. atlasRow keeps the 1.6.0 atlas path;
/// spriteStrip and singleFile reserve independent PNG sources without coupling
/// the manifest to WPF rendering types.
/// </summary>
public sealed class AssetActionSourceDefinition
{
    public string Type { get; set; } = AssetActionSourceKinds.AtlasRow;
    public string Atlas { get; set; } = string.Empty;
    public int Row { get; set; }
    public string File { get; set; } = string.Empty;
    public int Columns { get; set; }
    public int Rows { get; set; } = 1;
    public int FrameWidth { get; set; }
    public int FrameHeight { get; set; }
    public bool Vertical { get; set; }

    public void Normalize(int defaultCellSize)
    {
        Type = Type switch
        {
            AssetActionSourceKinds.AtlasRow => AssetActionSourceKinds.AtlasRow,
            AssetActionSourceKinds.SpriteStrip => AssetActionSourceKinds.SpriteStrip,
            AssetActionSourceKinds.SingleFile => AssetActionSourceKinds.SingleFile,
            _ when !string.IsNullOrWhiteSpace(Atlas) => AssetActionSourceKinds.AtlasRow,
            _ => AssetActionSourceKinds.SingleFile
        };
        Atlas = (Atlas ?? string.Empty).Trim();
        File = (File ?? string.Empty).Trim();
        Row = Math.Max(0, Row);
        Columns = Math.Max(0, Columns);
        Rows = Math.Max(1, Rows);
        FrameWidth = FrameWidth <= 0 ? defaultCellSize : FrameWidth;
        FrameHeight = FrameHeight <= 0 ? defaultCellSize : FrameHeight;
    }
}

public sealed class AssetActionSegmentDefinition
{
    public List<int> Frames { get; set; } = new();
    public string Next { get; set; } = string.Empty;

    public void Normalize(int sourceFrameCount)
    {
        Frames = (Frames ?? new List<int>())
            .Where(frame => frame >= 0 && frame < sourceFrameCount)
            .ToList();
        Next = (Next ?? string.Empty).Trim();
    }
}

public sealed class AssetMouseGazeSupportDefinition
{
    public bool Supported { get; set; }
    public List<string> Directions { get; set; } = new();
    public string UnsupportedFallback { get; set; } = "lightFeedback";

    public void Normalize()
    {
        Directions = (Directions ?? new List<string>())
            .Select(value => (value ?? string.Empty).Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        UnsupportedFallback = string.IsNullOrWhiteSpace(UnsupportedFallback)
            ? "lightFeedback"
            : UnsupportedFallback.Trim();
    }
}

public sealed class AssetInteractionSupportDefinition
{
    public bool Food { get; set; }
    public bool Toy { get; set; }
    public string FoodFallback { get; set; } = string.Empty;
    public string ToyFallback { get; set; } = string.Empty;

    public void Normalize()
    {
        FoodFallback = (FoodFallback ?? string.Empty).Trim();
        ToyFallback = (ToyFallback ?? string.Empty).Trim();
    }
}

public sealed class AssetActionDirectionVariant
{
    public AssetActionSourceDefinition Source { get; set; } = new();
    public List<int> Frames { get; set; } = new();

    public void Normalize(int cellSize, int sourceFrameCount)
    {
        Source ??= new AssetActionSourceDefinition();
        Source.Normalize(cellSize);
        Frames = (Frames ?? new List<int>())
            .Where(frame => frame >= 0 && frame < sourceFrameCount)
            .ToList();
    }
}

public sealed class AssetActionGroupDefinition
{
    public string GroupId { get; set; } = string.Empty;
    public string BehaviorId { get; set; } = string.Empty;
    public AssetActionSourceDefinition Source { get; set; } = new();
    public int FrameCount { get; set; }
    public int FrameDurationMs { get; set; } = 600;
    public List<int> FrameDurationsMs { get; set; } = new();
    public List<int> Frames { get; set; } = new();
    public string LoopMode { get; set; } = AssetLoopModes.Loop;
    public AssetActionSegmentDefinition Intro { get; set; } = new();
    public AssetActionSegmentDefinition Loop { get; set; } = new();
    public AssetActionSegmentDefinition Exit { get; set; } = new();
    public Dictionary<string, AssetActionDirectionVariant> Directions { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public List<string> CompatiblePostures { get; set; } = new();
    public AssetMouseGazeSupportDefinition MouseGaze { get; set; } = new();
    public AssetInteractionSupportDefinition Interactions { get; set; } = new();
    public string Fallback { get; set; } = string.Empty;
    public List<string> BehaviorTags { get; set; } = new();
    public List<string> TriggerConditions { get; set; } = new();

    [JsonIgnore]
    public bool IsLooping => LoopMode is AssetLoopModes.Loop or AssetLoopModes.PingPong;

    public void Normalize(string dictionaryKey, int cellSize, int sourceFrameCapacity = 0)
    {
        GroupId = string.IsNullOrWhiteSpace(GroupId) ? dictionaryKey : GroupId.Trim();
        BehaviorId = string.IsNullOrWhiteSpace(BehaviorId) ? GroupId : BehaviorId.Trim();
        Source ??= new AssetActionSourceDefinition();
        Source.Normalize(cellSize);
        Fallback = (Fallback ?? string.Empty).Trim();

        var requestedCount = FrameCount > 0
            ? FrameCount
            : Frames?.Count > 0
                ? Frames.Count
                : sourceFrameCapacity > 0
                    ? sourceFrameCapacity
                    : 1;
        FrameCount = Math.Clamp(requestedCount, 1, 512);
        var capacity = sourceFrameCapacity > 0 ? sourceFrameCapacity : FrameCount;
        Frames = (Frames ?? new List<int>())
            .Where(frame => frame >= 0 && frame < capacity)
            .Take(FrameCount)
            .ToList();
        if (Frames.Count == 0)
            Frames = Enumerable.Range(0, Math.Min(FrameCount, capacity)).ToList();
        FrameCount = Frames.Count;

        FrameDurationMs = Math.Clamp(FrameDurationMs <= 0 ? 600 : FrameDurationMs, 40, 10_000);
        FrameDurationsMs = (FrameDurationsMs ?? new List<int>())
            .Select(value => Math.Clamp(value <= 0 ? FrameDurationMs : value, 40, 10_000))
            .Take(FrameCount)
            .ToList();
        if (FrameDurationsMs.Count == 0)
            FrameDurationsMs = Enumerable.Repeat(FrameDurationMs, FrameCount).ToList();
        while (FrameDurationsMs.Count < FrameCount)
            FrameDurationsMs.Add(FrameDurationsMs[^1]);

        LoopMode = LoopMode switch
        {
            AssetLoopModes.Once => AssetLoopModes.Once,
            AssetLoopModes.Loop => AssetLoopModes.Loop,
            AssetLoopModes.PingPong => AssetLoopModes.PingPong,
            AssetLoopModes.Hold => AssetLoopModes.Hold,
            _ => AssetLoopModes.Loop
        };
        if (LoopMode == AssetLoopModes.PingPong && Frames.Count > 2)
        {
            var returnFrames = Frames.Skip(1).SkipLast(1).Reverse().ToList();
            Frames.AddRange(returnFrames);
            FrameDurationsMs.AddRange(
                FrameDurationsMs.Skip(1).SkipLast(1).Reverse().ToList());
            FrameCount = Frames.Count;
        }

        Intro ??= new AssetActionSegmentDefinition();
        Loop ??= new AssetActionSegmentDefinition();
        Exit ??= new AssetActionSegmentDefinition();
        Intro.Normalize(capacity);
        Loop.Normalize(capacity);
        Exit.Normalize(capacity);
        Directions ??= new Dictionary<string, AssetActionDirectionVariant>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var variant in Directions.Values)
            variant.Normalize(cellSize, capacity);
        CompatiblePostures = NormalizeValues(CompatiblePostures);
        BehaviorTags = NormalizeValues(BehaviorTags);
        TriggerConditions = NormalizeValues(TriggerConditions);
        MouseGaze ??= new AssetMouseGazeSupportDefinition();
        MouseGaze.Normalize();
        Interactions ??= new AssetInteractionSupportDefinition();
        Interactions.Normalize();
    }

    private static List<string> NormalizeValues(IEnumerable<string>? values) =>
        (values ?? Array.Empty<string>())
        .Select(value => (value ?? string.Empty).Trim())
        .Where(value => value.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}
