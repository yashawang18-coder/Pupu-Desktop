using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pupu.Application;
using Pupu.Behavior;

namespace Pupu.Desktop.Services;

public sealed class AssetPackService : IAssetPackService
{
    private const string ManifestName = "pupu-assets.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    private readonly Dictionary<string, BitmapImage> _sheets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapImage> _actionFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AssetActionGroupStatus> _actionGroupStatuses = new();

    private AssetPackService(AssetPackManifest manifest, string activeDirectory, bool customPack)
    {
        Manifest = manifest;
        ActiveDirectory = activeDirectory;
        IsCustomPack = customPack;
        ValidateAndLoad();
    }

    public AssetPackManifest Manifest { get; }
    public string ActiveDirectory { get; }
    public bool IsCustomPack { get; }
    public int CellSize => Manifest.CellSize;
    public string? FallbackWarning { get; private set; }
    public string DisplayStatus =>
        $"{Manifest.Name} {Manifest.Version} · {(IsCustomPack ? "本地可编辑" : "应用内置")} · {ActiveDirectory}" +
        (FallbackWarning is null ? string.Empty : $" · {FallbackWarning}");
    public string CompatibilityStatus =>
        Manifest.ActionGroups.Count == 0
            ? "schema 1 旧图集模式 · 已生成只读兼容动作组"
            : $"schema {Manifest.SchemaVersion} · {Manifest.ActionGroups.Count} 个清单驱动动作组 · intro/loop/exit 可执行";
    public IReadOnlyList<AssetActionGroupStatus> ActionGroupStatuses => _actionGroupStatuses;

    public static AssetPackService Load()
    {
        var customManifest = Path.Combine(StoragePaths.AssetDirectory, ManifestName);
        var packagedDirectory = Path.Combine(AppContext.BaseDirectory, "Assets");
        var packagedManifest = Path.Combine(packagedDirectory, ManifestName);
        var packaged = LoadFrom(packagedManifest, false);
        if (File.Exists(customManifest))
        {
            try
            {
                var custom = LoadFrom(customManifest, true);
                if (string.Equals(
                        custom.Manifest.Version,
                        packaged.Manifest.Version,
                        StringComparison.OrdinalIgnoreCase))
                    return custom;
                packaged.FallbackWarning =
                    $"检测到历史本地素材包 {custom.Manifest.Version}，已使用新版 {packaged.Manifest.Version}；打开可编辑素材目录会刷新为当前版";
                return packaged;
            }
            catch (Exception ex)
            {
                packaged.FallbackWarning = $"自定义素材无效，已回退内置素材：{ex.Message}";
                return packaged;
            }
        }

        return packaged;
    }

    private static AssetPackService LoadFrom(string manifestPath, bool customPack)
    {
        var activeDirectory = Path.GetDirectoryName(manifestPath)
                              ?? throw new InvalidDataException("素材清单目录无效。");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("找不到 pupu 素材清单。", manifestPath);

        var manifest = JsonSerializer.Deserialize<AssetPackManifest>(File.ReadAllText(manifestPath), JsonOptions)
                       ?? throw new InvalidDataException("pupu 素材清单为空或格式错误。");
        return new AssetPackService(manifest, activeDirectory, customPack);
    }

    public object GetSheet(string id) =>
        _sheets.TryGetValue(id, out var sheet)
            ? sheet
            : throw new KeyNotFoundException($"素材包没有注册图集：{id}");

    public string EnsureEditableCopy()
    {
        var packagedDirectory = Path.Combine(AppContext.BaseDirectory, "Assets");
        Directory.CreateDirectory(StoragePaths.AssetDirectory);
        var packagedManifestPath = Path.Combine(packagedDirectory, ManifestName);
        var packaged = JsonSerializer.Deserialize<AssetPackManifest>(File.ReadAllText(packagedManifestPath), JsonOptions)
                       ?? throw new InvalidDataException("内置素材清单无效。");

        var editableManifestPath = Path.Combine(StoragePaths.AssetDirectory, ManifestName);
        var refreshPack = true;
        if (File.Exists(editableManifestPath))
        {
            try
            {
                var existing = JsonSerializer.Deserialize<AssetPackManifest>(File.ReadAllText(editableManifestPath), JsonOptions);
                refreshPack = existing?.CellSize != packaged.CellSize ||
                              !string.Equals(existing.Version, packaged.Version, StringComparison.OrdinalIgnoreCase);
            }
            catch { refreshPack = true; }
        }
        if (refreshPack) File.Copy(packagedManifestPath, editableManifestPath, true);
        else CopyUnlessPresent(packagedManifestPath, editableManifestPath);
        foreach (var atlas in packaged.Atlases.Values)
            CopyPackagedAsset(
                ResolveInside(packagedDirectory, atlas.File),
                ResolveInside(StoragePaths.AssetDirectory, atlas.File),
                refreshPack);
        foreach (var actionGroup in packaged.ActionGroups.Values)
        {
            if (string.IsNullOrWhiteSpace(actionGroup.Source.File)) continue;
            CopyPackagedAsset(
                ResolveInside(packagedDirectory, actionGroup.Source.File),
                ResolveInside(StoragePaths.AssetDirectory, actionGroup.Source.File),
                refreshPack);
        }

        return $"已准备可编辑素材目录：{StoragePaths.AssetDirectory}。替换 PNG 或修改清单后重启 pupu 生效。";
    }

    private void ValidateAndLoad()
    {
        if (Manifest.SchemaVersion is < 1 or > 2)
            throw new InvalidDataException($"不支持的素材清单版本：{Manifest.SchemaVersion}");
        if (Manifest.CellSize != 256)
            throw new InvalidDataException("当前高清动画引擎要求素材单格为 256×256；旧素材会自动回退到内置新版。");
        foreach (var required in AssetGridContract.MinimumRows)
        {
            if (!Manifest.Atlases.TryGetValue(required.Key, out var definition))
                throw new InvalidDataException($"素材清单缺少图集：{required.Key}");
            if (definition.Columns != AssetGridContract.RequiredColumns ||
                definition.Rows < required.Value)
                throw new InvalidDataException(
                    $"图集 {required.Key} 网格不足：需要 {AssetGridContract.RequiredColumns}×{required.Value}。");
            var path = ResolveInside(ActiveDirectory, definition.File);
            if (!File.Exists(path)) throw new FileNotFoundException($"找不到图集 {required.Key}。", path);
            var image = LoadBitmap(path);
            var expectedWidth = definition.Columns * Manifest.CellSize;
            var expectedHeight = definition.Rows * Manifest.CellSize;
            if (image.PixelWidth != expectedWidth || image.PixelHeight != expectedHeight)
                throw new InvalidDataException(
                    $"图集 {required.Key} 是 {image.PixelWidth}×{image.PixelHeight}，清单要求 {expectedWidth}×{expectedHeight}。");
            _sheets[required.Key] = image;
        }
        foreach (var (state, definition) in Manifest.CoinStates)
        {
            if (!Manifest.Atlases.TryGetValue(definition.Atlas, out var atlas))
                throw new InvalidDataException($"银币状态 {state} 引用了未知图集：{definition.Atlas}");
            if (definition.Row < 0 || definition.Row >= atlas.Rows ||
                definition.Frames.Count == 0 ||
                definition.Frames.Any(frame => frame < 0 || frame >= atlas.Columns))
                throw new InvalidDataException($"银币状态 {state} 的行或帧超出图集范围。");
        }
        NormalizeAndValidateActionGroups();
    }

    private void NormalizeAndValidateActionGroups()
    {
        Manifest.ActionGroups ??= new Dictionary<string, AssetActionGroupDefinition>(
            StringComparer.OrdinalIgnoreCase);
        if (Manifest.ActionGroups.Count == 0)
        {
            foreach (var (atlasId, atlas) in Manifest.Atlases)
            {
                for (var row = 0; row < atlas.Rows; row++)
                {
                    var label = row < atlas.RowActions.Count
                        ? atlas.RowActions[row]
                        : $"第 {row} 行";
                    _actionGroupStatuses.Add(new AssetActionGroupStatus
                    {
                        GroupId = $"legacy.{atlasId}.{row}",
                        BehaviorId = $"legacy.{atlasId}.{row}",
                        SourceLabel = $"旧图集 {atlasId}:{row} · {label}",
                        FrameCount = atlas.Columns,
                        FrameDurationMs = 600,
                        LoopMode = AssetLoopModes.Loop,
                        FallbackLabel = "旧图集直接播放",
                        Validation = "schema 1 兼容组；节奏由现有 AnimationSequence 提供",
                        TriggerLabel = "旧素材包未声明触发条件；由现有行为映射决定",
                        Frames = Enumerable.Range(0, atlas.Columns).ToList(),
                        FrameDurationsMs = Enumerable.Repeat(600, atlas.Columns).ToList(),
                        AtlasId = atlasId,
                        Row = row,
                        File = atlas.File,
                        SourceType = AssetActionSourceKinds.AtlasRow
                    });
                }
            }
            return;
        }

        foreach (var (key, group) in Manifest.ActionGroups)
        {
            var capacity = ResolveSourceCapacity(group.Source);
            group.Normalize(key, Manifest.CellSize, capacity);
            var validation = ValidateActionGroupSource(group, out var sourceLabel);
            _actionGroupStatuses.Add(new AssetActionGroupStatus
            {
                GroupId = group.GroupId,
                BehaviorId = group.BehaviorId,
                SourceLabel = sourceLabel,
                FrameCount = group.FrameCount,
                FrameDurationMs = group.FrameDurationMs,
                LoopMode = group.LoopMode,
                FallbackLabel = string.IsNullOrWhiteSpace(group.Fallback)
                    ? "无需 fallback"
                    : $"fallback → {group.Fallback}",
                Validation = validation,
                TriggerLabel = group.TriggerConditions.Count == 0
                    ? "触发：由行为 ID 的现有规则决定"
                    : $"触发：{string.Join("；", group.TriggerConditions)}",
                Frames = group.Frames.ToList(),
                FrameDurationsMs = group.FrameDurationsMs.ToList(),
                AtlasId = group.Source.Atlas,
                Row = group.Source.Row,
                File = group.Source.File,
                SourceType = group.Source.Type,
                IntroFrames = group.Intro.Frames.ToList(),
                LoopFrames = group.Loop.Frames.ToList(),
                ExitFrames = group.Exit.Frames.ToList()
            });
        }
    }

    private int ResolveSourceCapacity(AssetActionSourceDefinition? source)
    {
        if (source is null) return 1;
        if (string.Equals(source.Type, AssetActionSourceKinds.AtlasRow, StringComparison.OrdinalIgnoreCase) &&
            Manifest.Atlases.TryGetValue(source.Atlas, out var atlas))
            return atlas.Columns;
        if (source.Columns > 0 && source.Rows > 0)
            return source.Columns * source.Rows;
        return 1;
    }

    private string ValidateActionGroupSource(
        AssetActionGroupDefinition group,
        out string sourceLabel)
    {
        var source = group.Source;
        if (source.Type == AssetActionSourceKinds.AtlasRow)
        {
            if (!Manifest.Atlases.TryGetValue(source.Atlas, out var atlas) ||
                source.Row < 0 ||
                source.Row >= atlas.Rows)
            {
                sourceLabel = $"旧图集引用无效：{source.Atlas}:{source.Row}";
                return string.IsNullOrWhiteSpace(group.Fallback)
                    ? "无效来源且没有 fallback；运行时使用原硬编码序列"
                    : $"无效来源；使用 {group.Fallback}";
            }
            sourceLabel = $"正式图集 {source.Atlas}:{source.Row}";
            return "通过：正式图集行、帧数、分段和节奏可读取";
        }

        if (string.IsNullOrWhiteSpace(source.File))
        {
            sourceLabel = $"{source.Type} 未填写文件";
            return string.IsNullOrWhiteSpace(group.Fallback)
                ? "缺少文件；运行时使用原硬编码序列"
                : $"缺少文件；使用 {group.Fallback}";
        }
        try
        {
            var path = ResolveInside(ActiveDirectory, source.File);
            sourceLabel = $"{source.Type} {source.File}";
            if (!File.Exists(path))
                return string.IsNullOrWhiteSpace(group.Fallback)
                    ? "文件不存在；运行时使用原硬编码序列"
                    : $"文件不存在；使用 {group.Fallback}";
            var image = LoadBitmap(path);
            _actionFiles[group.GroupId] = image;
            if (source.Type == AssetActionSourceKinds.SingleFile)
                return image.PixelWidth > 0 && image.PixelHeight > 0
                    ? "通过：单文件 PNG 可读取"
                    : "图片尺寸无效";
            var requiredWidth = source.Vertical
                ? source.FrameWidth
                : source.FrameWidth * (group.Frames.Max() + 1);
            var requiredHeight = source.Vertical
                ? source.FrameHeight * (group.Frames.Max() + 1)
                : source.FrameHeight;
            return image.PixelWidth >= requiredWidth && image.PixelHeight >= requiredHeight
                ? "通过：独立动作文件网格可读取"
                : $"动作文件尺寸不足：至少 {requiredWidth}×{requiredHeight}";
        }
        catch (Exception ex)
        {
            sourceLabel = $"{source.Type} {source.File}";
            return $"来源校验失败：{ex.Message}";
        }
    }

    public ResolvedAssetAnimation? ResolveActionGroup(string groupId)
    {
        if (!Manifest.ActionGroups.TryGetValue(groupId, out var group)) return null;
        BitmapSource? sheet = null;
        if (group.Source.Type == AssetActionSourceKinds.AtlasRow)
        {
            if (_sheets.TryGetValue(group.Source.Atlas, out var atlasSheet))
                sheet = atlasSheet;
        }
        else
        {
            if (_actionFiles.TryGetValue(group.GroupId, out var actionSheet))
                sheet = actionSheet;
        }
        if (sheet is null && !string.IsNullOrWhiteSpace(group.Fallback))
            return ResolveActionGroup(group.Fallback);
        if (sheet is null) return null;
        return new ResolvedAssetAnimation
        {
            Sheet = sheet,
            Row = group.Source.Type == AssetActionSourceKinds.AtlasRow ? group.Source.Row : 0,
            FrameWidth = group.Source.Type == AssetActionSourceKinds.AtlasRow
                ? Manifest.CellSize
                : group.Source.FrameWidth,
            FrameHeight = group.Source.Type == AssetActionSourceKinds.AtlasRow
                ? Manifest.CellSize
                : group.Source.FrameHeight,
            Frames = group.Frames.ToArray(),
            FrameDurationsMs = group.FrameDurationsMs.ToArray(),
            Loop = group.IsLooping,
            Vertical = group.Source.Vertical,
            AtlasRowSource = group.Source.Type == AssetActionSourceKinds.AtlasRow,
            SourceLabel = group.Source.Type == AssetActionSourceKinds.AtlasRow
                ? $"{group.Source.Atlas}:{group.Source.Row}"
                : group.Source.File,
            GroupId = group.GroupId,
            BehaviorId = group.BehaviorId,
            LoopMode = group.LoopMode,
            IntroFrames = group.Intro.Frames.ToArray(),
            LoopFrames = group.Loop.Frames.ToArray(),
            ExitFrames = group.Exit.Frames.ToArray(),
            CompatiblePostures = group.CompatiblePostures.ToArray()
        };
    }

    public IReadOnlyList<object> CreatePreviewFrames(
        AssetActionGroupStatus status,
        int maximumFrames = 24)
    {
        BitmapSource? sheet = null;
        var frameWidth = Manifest.CellSize;
        var frameHeight = Manifest.CellSize;
        var vertical = false;
        if (status.SourceType == AssetActionSourceKinds.AtlasRow)
        {
            if (_sheets.TryGetValue(status.AtlasId, out var atlasSheet))
                sheet = atlasSheet;
        }
        else
        {
            if (_actionFiles.TryGetValue(status.GroupId, out var actionSheet))
                sheet = actionSheet;
            if (Manifest.ActionGroups.TryGetValue(status.GroupId, out var group))
            {
                frameWidth = group.Source.FrameWidth;
                frameHeight = group.Source.FrameHeight;
                vertical = group.Source.Vertical;
            }
        }
        if (sheet is null) return Array.Empty<object>();
        var result = new List<object>();
        foreach (var frameNumber in status.Frames.Take(maximumFrames))
        {
            var x = vertical ? 0 : frameNumber * frameWidth;
            var y = status.SourceType == AssetActionSourceKinds.AtlasRow
                ? status.Row * frameHeight
                : vertical
                    ? frameNumber * frameHeight
                    : 0;
            if (x + frameWidth > sheet.PixelWidth || y + frameHeight > sheet.PixelHeight)
                continue;
            var frame = new CroppedBitmap(
                sheet,
                new Int32Rect(x, y, frameWidth, frameHeight));
            frame.Freeze();
            result.Add(frame);
        }
        return result;
    }

    public object? CreateActionFrame(string groupId, int frame)
    {
        var resolved = ResolveActionGroup(groupId);
        if (resolved is null || frame < 0) return null;
        var x = resolved.Vertical ? 0 : frame * resolved.FrameWidth;
        var y = resolved.AtlasRowSource
            ? resolved.Row * resolved.FrameHeight
            : resolved.Vertical
                ? frame * resolved.FrameHeight
                : 0;
        if (resolved.Sheet is not BitmapSource sheet ||
            x + resolved.FrameWidth > sheet.PixelWidth ||
            y + resolved.FrameHeight > sheet.PixelHeight)
            return null;
        var result = new CroppedBitmap(
            sheet,
            new Int32Rect(x, y, resolved.FrameWidth, resolved.FrameHeight));
        result.Freeze();
        return result;
    }

    public object? CreateCoinStateFrame(CoinAssetStateDefinition definition)
    {
        if (definition.Frames.Count == 0 ||
            !_sheets.TryGetValue(definition.Atlas, out var sheet))
            return null;
        var frame = definition.Frames[0];
        var x = frame * Manifest.CellSize;
        var y = definition.Row * Manifest.CellSize;
        if (x < 0 || y < 0 ||
            x + Manifest.CellSize > sheet.PixelWidth ||
            y + Manifest.CellSize > sheet.PixelHeight)
            return null;
        var result = new CroppedBitmap(
            sheet,
            new Int32Rect(x, y, Manifest.CellSize, Manifest.CellSize));
        result.Freeze();
        return result;
    }

    private static BitmapImage LoadBitmap(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static string ResolveInside(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) throw new InvalidDataException("素材文件必须使用相对路径。");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("素材文件不能位于素材目录之外。");
        return fullPath;
    }

    private static void CopyUnlessPresent(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (!File.Exists(destination)) File.Copy(source, destination);
    }

    private static void CopyPackagedAsset(string source, string destination, bool overwrite)
    {
        if (!overwrite)
        {
            CopyUnlessPresent(source, destination);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, true);
    }
}
