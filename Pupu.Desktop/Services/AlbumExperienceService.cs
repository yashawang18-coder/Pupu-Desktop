using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pupu.Behavior;
using Pupu.Desktop.Models;

namespace Pupu.Desktop.Services;

/// <summary>
/// Versioned, local-only index for album photos, owner-authored posts and
/// lightweight travel stories. The original album catalog and source files are
/// never rewritten by this service.
/// </summary>
public sealed class AlbumExperienceService
{
    public const int CurrentSchemaVersion = 1;

    private static readonly HashSet<string> ImageExtensions = new(
        new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif" },
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> TextExtensions = new(
        new[] { ".md", ".markdown", ".json" },
        StringComparer.OrdinalIgnoreCase);
    private static readonly EnumerationOptions Enumeration = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
    };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private static readonly Regex DateToken = new(
        @"(?<!\d)(?<year>(?:19|20)\d{2})[-./年](?<month>\d{1,2})(?:[-./月](?<day>\d{1,2})日?)?(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WordToken = new(
        @"[\p{L}\p{N}_.-]{2,32}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _buildSync = new();
    private readonly string _indexPath;
    private CancellationTokenSource? _activeBuildCancellation;
    private int _buildGeneration;

    public AlbumExperienceService(string? indexPath = null)
    {
        _indexPath = string.IsNullOrWhiteSpace(indexPath)
            ? StoragePaths.AlbumExperiencesFile
            : Path.GetFullPath(indexPath);
    }

    public async Task<AlbumExperienceIndex> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadWithoutLockAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AlbumExperienceIndex> SaveSettingsAsync(
        AlbumExperienceSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();
        lock (_buildSync)
        {
            _activeBuildCancellation?.Cancel();
            _buildGeneration++;
        }
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var index = await LoadWithoutLockAsync(cancellationToken);
            index.Settings = Clone(settings);
            index.UpdatedAt = DateTimeOffset.Now;
            await SaveWithoutLockAsync(index, cancellationToken);
            return Clone(index);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AlbumExperienceIndex> EnsureFreshAsync(
        PhotoAlbumCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var current = await LoadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(catalog.RootDirectory) ||
            !Directory.Exists(catalog.RootDirectory))
            return current;

        var fingerprint = await Task.Run(
            () => ComputeContentFingerprint(catalog, current.Settings, cancellationToken),
            cancellationToken);
        var rootFingerprint = FingerprintText(Path.GetFullPath(catalog.RootDirectory));
        if (current.SchemaVersion == CurrentSchemaVersion &&
            string.Equals(current.RootFingerprint, rootFingerprint, StringComparison.Ordinal) &&
            string.Equals(current.ContentFingerprint, fingerprint, StringComparison.Ordinal))
            return current;

        return await RebuildAsync(catalog, cancellationToken);
    }

    public async Task<AlbumExperienceIndex> RebuildAsync(
        PhotoAlbumCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        CancellationTokenSource linked;
        int generation;
        lock (_buildSync)
        {
            _activeBuildCancellation?.Cancel();
            _activeBuildCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            linked = _activeBuildCancellation;
            generation = ++_buildGeneration;
        }

        var previous = await LoadAsync(cancellationToken);
        var settings = Clone(previous.Settings);
        settings.Normalize();
        var preserved = previous.Records
            .Where(x => x.SourceType is
                AlbumExperienceSourceTypes.TravelEvent or
                AlbumExperienceSourceTypes.Manual)
            .Select(Clone)
            .ToList();
        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var building = new AlbumExperienceBuildStatus
        {
            State = "building",
            StartedAt = startedAt,
            Message = "正在后台扫描图片描述、Markdown 和 JSON 经历…"
        };

        var built = await Task.Run(
            () => BuildIndex(
                catalog,
                settings,
                preserved,
                building,
                linked.Token),
            linked.Token);
        stopwatch.Stop();
        linked.Token.ThrowIfCancellationRequested();
        lock (_buildSync)
        {
            if (generation != _buildGeneration)
                throw new OperationCanceledException("较新的经历索引扫描已开始。");
        }

        built.BuildStatus.State = "ready";
        built.BuildStatus.CompletedAt = DateTimeOffset.Now;
        built.BuildStatus.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
        built.BuildStatus.ExperienceCount = built.Records.Count;
        built.BuildStatus.Message =
            $"索引完成：{built.Records.Count} 条经历，扫描 {built.BuildStatus.ScannedFileCount} 个文件，" +
            $"{built.BuildStatus.ErrorCount} 个文件或引用被跳过。";
        built.UpdatedAt = DateTimeOffset.Now;

        await _gate.WaitAsync(linked.Token);
        try
        {
            lock (_buildSync)
            {
                if (generation != _buildGeneration)
                    throw new OperationCanceledException("较新的经历索引扫描已开始。");
            }
            await SaveWithoutLockAsync(built, linked.Token);
        }
        finally
        {
            _gate.Release();
        }
        return Clone(built);
    }

    public async Task<IReadOnlyList<AlbumExperienceSearchResult>> SearchAsync(
        PhotoAlbumCatalog catalog,
        AlbumExperienceSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(query);
        var index = await EnsureFreshAsync(catalog, cancellationToken);
        var maximum = Math.Clamp(
            query.MaximumResults <= 0 ? index.Settings.MaximumResults : query.MaximumResults,
            1,
            10);
        return await Task.Run(
            () => Search(index, query, maximum, cancellationToken),
            cancellationToken);
    }

    public async Task<IReadOnlyList<AlbumExperienceSearchResult>> SearchLoadedAsync(
        AlbumExperienceIndex index,
        AlbumExperienceSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(query);
        var maximum = Math.Clamp(
            query.MaximumResults <= 0 ? index.Settings.MaximumResults : query.MaximumResults,
            1,
            10);
        return await Task.Run(
            () => Search(index, query, maximum, cancellationToken),
            cancellationToken);
    }

    public async Task AddTravelExperienceAsync(
        string destination,
        string story,
        DateTimeOffset at,
        bool recalled,
        CancellationToken cancellationToken = default)
    {
        lock (_buildSync)
        {
            _activeBuildCancellation?.Cancel();
            _buildGeneration++;
        }
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var index = await LoadWithoutLockAsync(cancellationToken);
            if (!index.Settings.IncludeTravelEvents) return;
            var normalizedStory = NormalizeOneLine(story, 360);
            if (normalizedStory.Length == 0) return;
            var record = new AlbumExperienceRecord
            {
                Id = StableId(
                    AlbumExperienceSourceTypes.TravelEvent,
                    $"{at:O}|{destination}|{normalizedStory}"),
                Title = $"从{NormalizeOneLine(destination, 48)}回来",
                Body = normalizedStory,
                Summary = Summarize(normalizedStory, 220),
                Date = at,
                Tags = new List<string> { "旅行", recalled ? "召回" : "按时返回" },
                Mood = "curious",
                BehaviorId = "rest.window",
                Importance = 0.58,
                IncludeInConversation = true,
                IncludeInBehaviorDecision = true,
                AllowLlm = true,
                AllowRules = true,
                UpdatedAt = at,
                SourceType = AlbumExperienceSourceTypes.TravelEvent,
                SourceStatus = "ready"
            };
            index.Records.RemoveAll(x =>
                string.Equals(x.Id, record.Id, StringComparison.OrdinalIgnoreCase));
            index.Records.Add(record);
            index.Records = index.Records
                .OrderByDescending(x => x.Date ?? x.UpdatedAt)
                .Take(50000)
                .ToList();
            index.BuildStatus.ExperienceCount = index.Records.Count;
            index.UpdatedAt = DateTimeOffset.Now;
            await SaveWithoutLockAsync(index, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static string BuildLlmContext(
        IEnumerable<AlbumExperienceSearchResult> matches,
        int maximumResults)
    {
        var selected = matches
            .Where(x => x.Record.AllowLlm && x.Record.IncludeInConversation)
            .Take(Math.Clamp(maximumResults, 1, 3))
            .ToList();
        if (selected.Count == 0) return string.Empty;

        var lines = selected.Select((match, index) =>
        {
            var record = match.Record;
            var parts = new List<string>
            {
                $"标题 {NormalizeOneLine(record.Title, 72)}",
                $"摘要 {NormalizeOneLine(record.Summary, 220)}"
            };
            if (record.Date is { } date) parts.Add($"日期 {date:yyyy-MM-dd}");
            if (record.Tags.Count > 0)
                parts.Add($"标签 {string.Join("、", record.Tags.Take(8).Select(x => NormalizeOneLine(x, 24)))}");
            if (!string.IsNullOrWhiteSpace(record.Mood))
                parts.Add($"情绪 {NormalizeOneLine(record.Mood, 24)}");
            if (!string.IsNullOrWhiteSpace(record.BehaviorId))
                parts.Add($"行为关联 {NormalizeOneLine(record.BehaviorId, 48)}");
            parts.Add($"来源类型 {NormalizeOneLine(record.SourceType, 32)}");
            if (!string.IsNullOrWhiteSpace(record.Body))
                parts.Add($"有限原文片段 {NormalizeOneLine(record.Body, 120)}");
            return $"- 经历{index + 1}：{string.Join("；", parts)}。";
        });
        var context =
            "【主人授权参与本轮对话的本地相册经历】" + Environment.NewLine +
            string.Join(Environment.NewLine, lines) + Environment.NewLine +
            "这些内容是经过限量和摘要的本地记录。不要猜测未提供的画面内容；不要声称看见了未附带的图片；不要要求或输出本地文件路径。";
        return new ModelContextPrivacyFilter().Prepare(context, 2600);
    }

    public static string ComposeRuleReply(AlbumExperienceRecord? record)
    {
        if (record is null)
            return "我在相册里翻了翻，暂时没找到能对上这句话的记录。也许换个日期、标签或名字再问我。";
        var when = record.Date is { } date ? $"{date:yyyy年M月d日}" : "那时候";
        var tags = record.Tags.Count > 0
            ? $"，标签里写着{string.Join("、", record.Tags.Take(3))}"
            : string.Empty;
        var mood = string.IsNullOrWhiteSpace(record.Mood)
            ? string.Empty
            : $"，当时的心情是{record.Mood}";
        var photo = record.ImageRelativePaths.Count > 0
            ? "我记得那张照片或那次记录，但不会乱猜照片里没写过的细节。"
            : "我记得那次记录。";
        return $"{photo}{when}：{NormalizeOneLine(record.Summary, 150)}{tags}{mood}。";
    }

    public static IReadOnlyList<string> ResolveAuthorizedImagePaths(
        string rootDirectory,
        IEnumerable<AlbumExperienceSearchResult> matches,
        int maximumImages)
    {
        var result = new List<string>();
        if (maximumImages <= 0 || string.IsNullOrWhiteSpace(rootDirectory))
            return result;
        string root;
        try { root = Path.GetFullPath(rootDirectory); }
        catch { return result; }
        foreach (var relative in matches
                     .Where(x => x.Record.AllowLlm)
                     .SelectMany(x => x.Record.ImageRelativePaths))
        {
            if (result.Count >= Math.Clamp(maximumImages, 0, 2)) break;
            if (!TryResolveExistingFile(root, relative, ImageExtensions, out var fullPath))
                continue;
            if (!result.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                result.Add(fullPath);
        }
        return result;
    }

    public static bool LooksLikeExperienceQuery(string? text)
    {
        var value = text ?? string.Empty;
        if (value.Length == 0) return false;
        var cues = new[]
        {
            "照片", "相册", "图片", "看图", "回忆", "以前", "那次", "那天",
            "小时候", "成长", "去年", "今年", "生日", "旅行", "旅游",
            "发帖", "朋友圈", "记录"
        };
        return cues.Any(x => value.Contains(x, StringComparison.CurrentCultureIgnoreCase)) ||
               DateToken.IsMatch(value);
    }

    private AlbumExperienceIndex BuildIndex(
        PhotoAlbumCatalog catalog,
        AlbumExperienceSettings settings,
        List<AlbumExperienceRecord> preserved,
        AlbumExperienceBuildStatus status,
        CancellationToken cancellationToken)
    {
        status.UsedBackgroundWorker = true;
        var index = new AlbumExperienceIndex
        {
            SchemaVersion = CurrentSchemaVersion,
            Settings = Clone(settings),
            BuildStatus = status
        };
        if (string.IsNullOrWhiteSpace(catalog.RootDirectory))
        {
            index.Records = preserved;
            index.BuildStatus.Message = "尚未链接相册根目录；已保留手工和旅行经历。";
            return index;
        }

        string root;
        try { root = Path.GetFullPath(catalog.RootDirectory); }
        catch
        {
            index.Records = preserved;
            index.BuildStatus.ErrorCount++;
            index.BuildStatus.Message = "相册根目录无效；已保留手工和旅行经历。";
            return index;
        }
        index.RootFingerprint = FingerprintText(root);
        if (!Directory.Exists(root))
        {
            index.Records = preserved;
            index.BuildStatus.ErrorCount++;
            index.BuildStatus.Message = "相册根目录当前不可用；旧相册文件未改动。";
            return index;
        }

        var records = new List<AlbumExperienceRecord>(preserved);
        var files = EnumerateCandidateFiles(root, settings, cancellationToken).ToList();
        status.ScannedFileCount = files.Count;
        var descriptions = catalog.PhotoDescriptions
            .GroupBy(
                x => NormalizeRelativePath(x.RelativePath),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(item => item.UpdatedAt).First(),
                StringComparer.OrdinalIgnoreCase);

        if (settings.ScanImages)
        {
            foreach (var file in files.Where(x => ImageExtensions.Contains(x.Extension)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    records.Add(BuildPhotoRecord(root, file, catalog, descriptions));
                }
                catch (IOException) { status.ErrorCount++; }
                catch (UnauthorizedAccessException) { status.ErrorCount++; }
                catch (InvalidDataException) { status.ErrorCount++; }
            }
        }

        if (settings.ScanTextFiles)
        {
            foreach (var file in files.Where(x => TextExtensions.Contains(x.Extension)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file.Length <= 0 || file.Length > 2L * 1024 * 1024)
                {
                    status.ErrorCount++;
                    continue;
                }
                try
                {
                    var record = file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                        ? ParseJsonRecord(root, file.FullName)
                        : ParseMarkdownRecord(root, file.FullName);
                    if (record is not null)
                    {
                        records.Add(record);
                        if (record.SourceStatus.StartsWith("partial:", StringComparison.Ordinal))
                            status.ErrorCount++;
                    }
                }
                catch (IOException) { status.ErrorCount++; }
                catch (UnauthorizedAccessException) { status.ErrorCount++; }
                catch (JsonException) { status.ErrorCount++; }
                catch (InvalidDataException) { status.ErrorCount++; }
            }
        }

        index.ContentFingerprint = ComputeContentFingerprint(
            catalog,
            settings,
            cancellationToken);
        index.Records = records
            .Select(NormalizeRecord)
            .Where(x => x.Title.Length > 0 || x.Summary.Length > 0)
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(item => item.UpdatedAt).First())
            .OrderByDescending(x => x.Date ?? x.UpdatedAt)
            .Take(50000)
            .ToList();
        return index;
    }

    private static AlbumExperienceRecord BuildPhotoRecord(
        string root,
        FileInfo file,
        PhotoAlbumCatalog catalog,
        IReadOnlyDictionary<string, PhotoDescriptionEntry> descriptions)
    {
        var relative = NormalizeRelativePath(Path.GetRelativePath(root, file.FullName));
        if (!TryResolveExistingFile(root, relative, ImageExtensions, out _))
            throw new InvalidDataException("图片不在相册根目录中。");
        descriptions.TryGetValue(relative, out var description);
        var album = FindAlbum(catalog.Albums, relative);
        var directoryName = Path.GetFileName(Path.GetDirectoryName(relative)) ?? string.Empty;
        var parsed = PhotoAlbumService.ParseDirectoryMetadata(directoryName);
        var albumName = album?.Name ??
                        (directoryName.Length == 0 ? "全部照片" : directoryName);
        var theme = album?.Theme ?? parsed.Theme;
        var date = album?.StartDate ?? parsed.StartDate ?? file.LastWriteTime;
        var summary = description?.Description;
        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = $"{albumName}中的照片“{Path.GetFileNameWithoutExtension(file.Name)}”";
            if (!string.IsNullOrWhiteSpace(theme)) summary += $"，主题是{theme}";
        }
        return new AlbumExperienceRecord
        {
            Id = StableId(AlbumExperienceSourceTypes.PhotoDescription, relative),
            Title = Path.GetFileNameWithoutExtension(file.Name),
            Body = description?.Description ?? string.Empty,
            Summary = Summarize(summary, 220),
            ImageRelativePaths = new List<string> { relative },
            SourceRelativePath = relative,
            Date = new DateTimeOffset(date),
            Tags = new[] { theme, album?.GrowthStage ?? string.Empty }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            Mood = string.Empty,
            BehaviorId = string.Empty,
            Importance = description is null ? 0.35 : 0.55,
            IncludeInConversation = true,
            IncludeInBehaviorDecision = false,
            AllowLlm = true,
            AllowRules = true,
            UpdatedAt = description?.UpdatedAt ?? file.LastWriteTimeUtc,
            SourceType = AlbumExperienceSourceTypes.PhotoDescription,
            AlbumName = albumName,
            Theme = theme,
            GrowthStage = album?.GrowthStage ?? string.Empty,
            SourceStatus = "ready"
        };
    }

    public static AlbumExperienceRecord? ParseMarkdownRecord(
        string rootDirectory,
        string markdownPath)
    {
        var root = Path.GetFullPath(rootDirectory);
        var source = Path.GetFullPath(markdownPath);
        if (!IsInsideOrEqual(root, source))
            throw new InvalidDataException("Markdown 来源不能离开相册根目录。");
        var text = File.ReadAllText(source, Encoding.UTF8);
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var body = text.Replace("\r", string.Empty, StringComparison.Ordinal);
        if (body.StartsWith("---\n", StringComparison.Ordinal))
        {
            var end = body.IndexOf("\n---\n", 4, StringComparison.Ordinal);
            if (end >= 0)
            {
                ParseFrontmatter(body[4..end], metadata);
                body = body[(end + 5)..];
            }
        }
        body = body.Trim();
        var relative = NormalizeRelativePath(Path.GetRelativePath(root, source));
        var updatedAt = new DateTimeOffset(File.GetLastWriteTime(source));
        var date = ParseDate(metadata.GetValueOrDefault("date")) ??
                   ParseDate(Path.GetFileNameWithoutExtension(source)) ??
                   updatedAt;
        var title = NormalizeOneLine(
            metadata.GetValueOrDefault("title"),
            96);
        if (title.Length == 0)
            title = Path.GetFileNameWithoutExtension(source);
        var record = new AlbumExperienceRecord
        {
            Id = StableId(AlbumExperienceSourceTypes.MarkdownPost, relative),
            Title = title,
            Body = NormalizeMultiline(body, 4000),
            Summary = Summarize(
                metadata.GetValueOrDefault("summary") ?? body,
                220),
            SourceRelativePath = relative,
            Date = date,
            Tags = ParseList(metadata.GetValueOrDefault("tags")),
            Mood = NormalizeOneLine(metadata.GetValueOrDefault("mood"), 24),
            BehaviorId = NormalizeOneLine(metadata.GetValueOrDefault("behavior"), 64),
            Importance = ParseImportance(metadata.GetValueOrDefault("importance"), 0.5),
            IncludeInConversation = true,
            IncludeInBehaviorDecision = false,
            AllowLlm = true,
            AllowRules = true,
            UpdatedAt = updatedAt,
            SourceType = AlbumExperienceSourceTypes.MarkdownPost,
            AlbumName = Path.GetFileName(Path.GetDirectoryName(source)) ?? string.Empty
        };
        ApplyVisibility(record, metadata.GetValueOrDefault("visibility"));
        ApplyBoolean(metadata, "includeInConversation", x => record.IncludeInConversation = x);
        ApplyBoolean(metadata, "includeInBehaviorDecision", x => record.IncludeInBehaviorDecision = x);
        ApplyBoolean(metadata, "allowLlm", x => record.AllowLlm = x);
        ApplyBoolean(metadata, "allowRules", x => record.AllowRules = x);
        var imageIssues = AddSafeImageReferences(
            record,
            root,
            Path.GetDirectoryName(source)!,
            ParseList(metadata.GetValueOrDefault("images")));
        record.SourceStatus = imageIssues == 0 ? "ready" : $"partial:{imageIssues}_image_reference_skipped";
        return NormalizeRecord(record);
    }

    public static AlbumExperienceRecord? ParseJsonRecord(
        string rootDirectory,
        string jsonPath)
    {
        var root = Path.GetFullPath(rootDirectory);
        var source = Path.GetFullPath(jsonPath);
        if (!IsInsideOrEqual(root, source))
            throw new InvalidDataException("JSON 来源不能离开相册根目录。");
        using var document = JsonDocument.Parse(File.ReadAllText(source, Encoding.UTF8));
        if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
        var element = document.RootElement;
        var title = ReadString(element, "title");
        var body = ReadString(element, "body");
        if (body.Length == 0) body = ReadString(element, "content");
        var summary = ReadString(element, "summary");
        if (title.Length == 0 && body.Length == 0 && summary.Length == 0)
            return null;

        var relative = NormalizeRelativePath(Path.GetRelativePath(root, source));
        var updatedAt = ReadDate(element, "updatedAt") ??
                        new DateTimeOffset(File.GetLastWriteTime(source));
        var record = new AlbumExperienceRecord
        {
            Id = ReadString(element, "id"),
            Title = title.Length == 0 ? Path.GetFileNameWithoutExtension(source) : title,
            Body = NormalizeMultiline(body, 4000),
            Summary = Summarize(summary.Length == 0 ? body : summary, 220),
            SourceRelativePath = relative,
            Date = ReadDate(element, "date") ??
                   ParseDate(Path.GetFileNameWithoutExtension(source)) ??
                   updatedAt,
            Tags = ReadStringList(element, "tags"),
            Mood = ReadString(element, "mood"),
            BehaviorId = ReadString(element, "behaviorId"),
            Importance = ReadDouble(element, "importance", 0.5),
            IncludeInConversation = ReadBoolean(element, "includeInConversation", true),
            IncludeInBehaviorDecision = ReadBoolean(element, "includeInBehaviorDecision", false),
            AllowLlm = ReadBoolean(element, "allowLlm", true),
            AllowRules = ReadBoolean(element, "allowRules", true),
            UpdatedAt = updatedAt,
            SourceType = AlbumExperienceSourceTypes.JsonPost,
            AlbumName = ReadString(element, "albumName"),
            Theme = ReadString(element, "theme"),
            GrowthStage = ReadString(element, "growthStage")
        };
        if (record.BehaviorId.Length == 0)
            record.BehaviorId = ReadString(element, "behavior");
        var visibility = ReadString(element, "visibility");
        ApplyVisibility(record, visibility);
        var images = ReadStringList(element, "imageRelativePaths");
        if (images.Count == 0) images = ReadStringList(element, "images");
        var imageIssues = AddSafeImageReferences(
            record,
            root,
            Path.GetDirectoryName(source)!,
            images);
        record.SourceStatus = imageIssues == 0 ? "ready" : $"partial:{imageIssues}_image_reference_skipped";
        if (record.Id.Length == 0)
            record.Id = StableId(AlbumExperienceSourceTypes.JsonPost, relative);
        return NormalizeRecord(record);
    }

    private static IReadOnlyList<AlbumExperienceSearchResult> Search(
        AlbumExperienceIndex index,
        AlbumExperienceSearchQuery query,
        int maximum,
        CancellationToken cancellationToken)
    {
        var terms = ExtractTerms(query.Text).ToList();
        var results = new List<AlbumExperienceSearchResult>();
        foreach (var record in index.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (query.ForLlm && (!record.AllowLlm || !record.IncludeInConversation)) continue;
            if (query.ForRules &&
                (!index.Settings.AllowRuleMode || !record.AllowRules || !record.IncludeInConversation))
                continue;
            if (query.ForBehavior &&
                (!index.Settings.AllowBehaviorDecision || !record.IncludeInBehaviorDecision))
                continue;
            if (query.StartDate is { } start &&
                record.Date is { } dateBefore &&
                dateBefore.Date < start.Date)
                continue;
            if (query.EndDate is { } end &&
                record.Date is { } dateAfter &&
                dateAfter.Date > end.Date)
                continue;
            if (query.Tags.Count > 0 &&
                !query.Tags.All(tag => record.Tags.Any(x =>
                    x.Contains(tag, StringComparison.CurrentCultureIgnoreCase))))
                continue;
            if (!ContainsIfRequested(record.Mood, query.Mood) ||
                !ContainsIfRequested(record.AlbumName, query.AlbumName) ||
                !ContainsIfRequested(record.Theme, query.Theme) ||
                !ContainsIfRequested(record.GrowthStage, query.GrowthStage) ||
                !ContainsIfRequested(record.BehaviorId, query.BehaviorId))
                continue;

            var score = Score(record, query.Text, terms);
            if (terms.Count > 0 && score <= 0) continue;
            results.Add(new AlbumExperienceSearchResult(record, score));
        }
        return results
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Record.Importance)
            .ThenByDescending(x => x.Record.Date ?? x.Record.UpdatedAt)
            .Take(maximum)
            .ToList();
    }

    private async Task<AlbumExperienceIndex> LoadWithoutLockAsync(
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_indexPath)!);
        if (!File.Exists(_indexPath)) return new AlbumExperienceIndex();
        try
        {
            await using var stream = File.Open(
                _indexPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            var index = await JsonSerializer.DeserializeAsync<AlbumExperienceIndex>(
                            stream,
                            Json,
                            cancellationToken) ??
                        new AlbumExperienceIndex();
            NormalizeIndex(index);
            return index;
        }
        catch (JsonException)
        {
            var backup = _indexPath + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(_indexPath, backup, false);
            return new AlbumExperienceIndex
            {
                BuildStatus = new AlbumExperienceBuildStatus
                {
                    State = "error",
                    ErrorCount = 1,
                    Message = "旧经历索引无法解析，已保留备份并等待重建。"
                }
            };
        }
    }

    private async Task SaveWithoutLockAsync(
        AlbumExperienceIndex index,
        CancellationToken cancellationToken)
    {
        NormalizeIndex(index);
        Directory.CreateDirectory(Path.GetDirectoryName(_indexPath)!);
        var temporary = _indexPath + ".tmp";
        await using (var stream = new FileStream(
                         temporary,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         8192,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, index, Json, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temporary, _indexPath, true);
    }

    private IEnumerable<FileInfo> EnumerateCandidateFiles(
        string root,
        AlbumExperienceSettings settings,
        CancellationToken cancellationToken)
    {
        IEnumerable<string> paths;
        try { paths = Directory.EnumerateFiles(root, "*", Enumeration); }
        catch (IOException) { return Array.Empty<FileInfo>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<FileInfo>(); }
        var result = new List<FileInfo>();
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var extension = Path.GetExtension(path);
                if ((settings.ScanImages && ImageExtensions.Contains(extension)) ||
                    (settings.ScanTextFiles && TextExtensions.Contains(extension)))
                    result.Add(new FileInfo(path));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return result;
    }

    private string ComputeContentFingerprint(
        PhotoAlbumCatalog catalog,
        AlbumExperienceSettings settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(catalog.RootDirectory) ||
            !Directory.Exists(catalog.RootDirectory))
            return string.Empty;
        var builder = new StringBuilder();
        builder.Append(settings.ScanImages).Append('|').Append(settings.ScanTextFiles).AppendLine();
        foreach (var file in EnumerateCandidateFiles(
                     Path.GetFullPath(catalog.RootDirectory),
                     settings,
                     cancellationToken)
                     .OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.Append(NormalizeRelativePath(
                    Path.GetRelativePath(catalog.RootDirectory, file.FullName)))
                .Append('|')
                .Append(file.LastWriteTimeUtc.Ticks)
                .Append('|')
                .Append(file.Length)
                .AppendLine();
        }
        foreach (var description in catalog.PhotoDescriptions
                     .OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(NormalizeRelativePath(description.RelativePath))
                .Append('|')
                .Append(description.UpdatedAt.UtcDateTime.Ticks)
                .Append('|')
                .Append(FingerprintText(description.Description))
                .AppendLine();
        }
        foreach (var album in catalog.Albums.OrderBy(x => x.Id))
        {
            builder.Append(album.Id).Append('|')
                .Append(album.Name).Append('|')
                .Append(album.RelativeDirectory).Append('|')
                .Append(album.Theme).Append('|')
                .Append(album.GrowthStage).Append('|')
                .Append(album.StartDate?.Ticks ?? 0).Append('|')
                .Append(album.EndDate?.Ticks ?? 0).AppendLine();
        }
        return FingerprintText(builder.ToString());
    }

    private static int AddSafeImageReferences(
        AlbumExperienceRecord record,
        string root,
        string sourceDirectory,
        IEnumerable<string> imageValues)
    {
        var issues = 0;
        foreach (var raw in imageValues)
        {
            var value = raw.Trim().Trim('"', '\'');
            if (value.Length == 0 || Path.IsPathRooted(value))
            {
                issues++;
                continue;
            }
            string? fullPath = null;
            foreach (var candidate in new[]
                     {
                         Path.Combine(sourceDirectory, value),
                         Path.Combine(root, value)
                     })
            {
                try
                {
                    var resolved = Path.GetFullPath(candidate);
                    if (IsInsideOrEqual(root, resolved) &&
                        File.Exists(resolved) &&
                        ImageExtensions.Contains(Path.GetExtension(resolved)))
                    {
                        fullPath = resolved;
                        break;
                    }
                }
                catch { }
            }
            if (fullPath is null)
            {
                issues++;
                continue;
            }
            var relative = NormalizeRelativePath(Path.GetRelativePath(root, fullPath));
            if (!record.ImageRelativePaths.Contains(relative, StringComparer.OrdinalIgnoreCase))
                record.ImageRelativePaths.Add(relative);
        }
        return issues;
    }

    private static void ParseFrontmatter(
        string frontmatter,
        IDictionary<string, string> metadata)
    {
        string? listKey = null;
        var listItems = new List<string>();
        foreach (var raw in frontmatter.Replace("\r", string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("- ", StringComparison.Ordinal) && listKey is not null)
            {
                listItems.Add(line[2..].Trim());
                continue;
            }
            if (listKey is not null)
            {
                metadata[listKey] = string.Join(",", listItems);
                listKey = null;
                listItems.Clear();
            }
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (value.Length == 0)
            {
                listKey = key;
                continue;
            }
            metadata[key] = value;
        }
        if (listKey is not null)
            metadata[listKey] = string.Join(",", listItems);
    }

    private static void ApplyVisibility(AlbumExperienceRecord record, string? visibility)
    {
        switch ((visibility ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "conversation":
            case "chat":
                record.IncludeInConversation = true;
                record.IncludeInBehaviorDecision = false;
                break;
            case "behavior":
                record.IncludeInConversation = false;
                record.IncludeInBehaviorDecision = true;
                break;
            case "none":
            case "private":
                record.IncludeInConversation = false;
                record.IncludeInBehaviorDecision = false;
                record.AllowLlm = false;
                record.AllowRules = false;
                break;
            case "both":
                record.IncludeInConversation = true;
                record.IncludeInBehaviorDecision = true;
                break;
        }
    }

    private static void ApplyBoolean(
        IReadOnlyDictionary<string, string> metadata,
        string key,
        Action<bool> setter)
    {
        if (!metadata.TryGetValue(key, out var raw)) return;
        if (TryParseBoolean(raw, out var value)) setter(value);
    }

    private static PhotoSubAlbum? FindAlbum(
        IEnumerable<PhotoSubAlbum> albums,
        string photoRelativePath)
    {
        var photo = NormalizeRelativePath(photoRelativePath);
        return albums
            .Where(album =>
            {
                var directory = NormalizeRelativePath(album.RelativeDirectory).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
                return directory == "." ||
                       photo.StartsWith(
                           directory + Path.DirectorySeparatorChar,
                           StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(x => x.RelativeDirectory.Length)
            .FirstOrDefault();
    }

    private static double Score(
        AlbumExperienceRecord record,
        string rawQuery,
        IReadOnlyList<string> terms)
    {
        var score = record.Importance * 2;
        var fields = new[]
        {
            (record.Title, 8d),
            (record.Summary, 7d),
            (record.Body, 3d),
            (string.Join(" ", record.Tags), 7d),
            (record.Mood, 5d),
            (record.AlbumName, 6d),
            (record.Theme, 6d),
            (record.GrowthStage, 5d),
            (record.BehaviorId, 5d),
            (record.SourceRelativePath, 2d)
        };
        var query = rawQuery.Trim();
        if (query.Length >= 2)
        {
            foreach (var (value, weight) in fields)
            {
                if (!string.IsNullOrWhiteSpace(value) &&
                    value.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                    score += weight * 1.5;
            }
        }
        foreach (var term in terms)
        {
            foreach (var (value, weight) in fields)
            {
                if (!string.IsNullOrWhiteSpace(value) &&
                    value.Contains(term, StringComparison.CurrentCultureIgnoreCase))
                    score += weight;
            }
        }
        return score;
    }

    private static IEnumerable<string> ExtractTerms(string text)
    {
        var noise = new[]
        {
            "照片", "相册", "图片", "看图", "回忆", "以前", "那次", "那天",
            "还记得", "记得", "发帖", "朋友圈", "记录", "帮我", "给我",
            "看看", "看一看", "寻找", "找一下", "朴朴", "主人", "一下",
            "今年", "去年", "有没有", "是什么", "在哪里", "可以", "好吗",
            "吗", "呢", "呀", "的"
        };
        var cleaned = text ?? string.Empty;
        foreach (var item in noise)
            cleaned = cleaned.Replace(
                item,
                " ",
                StringComparison.CurrentCultureIgnoreCase);
        return WordToken.Matches(cleaned)
            .Select(x => x.Value.Trim())
            .Where(x => x.Length >= 2)
            .Distinct(StringComparer.CurrentCultureIgnoreCase);
    }

    private static bool ContainsIfRequested(string value, string requested) =>
        string.IsNullOrWhiteSpace(requested) ||
        (value ?? string.Empty).Contains(
            requested.Trim(),
            StringComparison.CurrentCultureIgnoreCase);

    private static AlbumExperienceRecord NormalizeRecord(AlbumExperienceRecord source)
    {
        source.Id = NormalizeOneLine(source.Id, 96);
        if (source.Id.Length == 0)
            source.Id = StableId(source.SourceType, source.SourceRelativePath + source.Title);
        source.Title = NormalizeOneLine(source.Title, 96);
        source.Body = NormalizeMultiline(source.Body, 4000);
        source.Summary = Summarize(
            string.IsNullOrWhiteSpace(source.Summary) ? source.Body : source.Summary,
            220);
        source.ImageRelativePaths ??= new List<string>();
        source.ImageRelativePaths = source.ImageRelativePaths
            .Select(NormalizeRelativePath)
            .Where(x => x.Length > 0 && !Path.IsPathRooted(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        source.SourceRelativePath = NormalizeRelativePath(source.SourceRelativePath);
        source.Tags ??= new List<string>();
        source.Tags = source.Tags
            .Select(x => NormalizeOneLine(x, 24))
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Take(20)
            .ToList();
        source.Mood = NormalizeOneLine(source.Mood, 24);
        source.BehaviorId = NormalizeOneLine(source.BehaviorId, 64);
        source.Importance = Math.Clamp(source.Importance, 0, 1);
        source.SourceType = NormalizeOneLine(source.SourceType, 32);
        if (source.SourceType.Length == 0)
            source.SourceType = AlbumExperienceSourceTypes.Manual;
        source.AlbumName = NormalizeOneLine(source.AlbumName, 64);
        source.Theme = NormalizeOneLine(source.Theme, 64);
        source.GrowthStage = NormalizeOneLine(source.GrowthStage, 40);
        source.SourceStatus = NormalizeOneLine(source.SourceStatus, 80);
        if (source.SourceStatus.Length == 0) source.SourceStatus = "ready";
        if (source.UpdatedAt == default) source.UpdatedAt = DateTimeOffset.Now;
        return source;
    }

    private static void NormalizeIndex(AlbumExperienceIndex index)
    {
        index.SchemaVersion = CurrentSchemaVersion;
        index.RootFingerprint ??= string.Empty;
        index.ContentFingerprint ??= string.Empty;
        index.Settings ??= new AlbumExperienceSettings();
        index.Settings.Normalize();
        index.Records ??= new List<AlbumExperienceRecord>();
        index.Records = index.Records
            .Where(x => x is not null)
            .Select(NormalizeRecord)
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(item => item.UpdatedAt).First())
            .Take(50000)
            .ToList();
        index.BuildStatus ??= new AlbumExperienceBuildStatus();
        index.BuildStatus.ExperienceCount = index.Records.Count;
    }

    private static bool TryResolveExistingFile(
        string root,
        string relativePath,
        IReadOnlySet<string> allowedExtensions,
        out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            return false;
        try
        {
            var resolved = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!IsInsideOrEqual(root, resolved) ||
                !allowedExtensions.Contains(Path.GetExtension(resolved)) ||
                !File.Exists(resolved))
                return false;
            fullPath = resolved;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsInsideOrEqual(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(path);
        return string.Equals(normalizedRoot, normalizedPath, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(
                   normalizedRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var value = raw.Trim().Trim('"', '\'');
        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var parsed))
            return parsed;
        var match = DateToken.Match(value);
        if (!match.Success ||
            !int.TryParse(match.Groups["year"].Value, out var year) ||
            !int.TryParse(match.Groups["month"].Value, out var month))
            return null;
        var day = int.TryParse(match.Groups["day"].Value, out var parsedDay)
            ? parsedDay
            : 1;
        try { return new DateTimeOffset(new DateTime(year, month, day)); }
        catch { return null; }
    }

    private static List<string> ParseList(string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        if (value.StartsWith('[') && value.EndsWith(']'))
            value = value[1..^1];
        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim('"', '\''))
            .Where(x => x.Length > 0)
            .ToList();
    }

    private static bool TryParseBoolean(string raw, out bool value)
    {
        var normalized = raw.Trim().Trim('"', '\'').ToLowerInvariant();
        if (normalized is "true" or "yes" or "1" or "是" or "允许")
        {
            value = true;
            return true;
        }
        if (normalized is "false" or "no" or "0" or "否" or "不允许")
        {
            value = false;
            return true;
        }
        value = false;
        return false;
    }

    private static double ParseImportance(string? raw, double fallback) =>
        double.TryParse(
            (raw ?? string.Empty).Trim().Trim('"', '\''),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? Math.Clamp(value, 0, 1)
            : fallback;

    private static string ReadString(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var property)) return string.Empty;
        return property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : property.ToString();
    }

    private static DateTimeOffset? ReadDate(JsonElement element, string name) =>
        TryGetProperty(element, name, out var property)
            ? ParseDate(property.ToString())
            : null;

    private static bool ReadBoolean(
        JsonElement element,
        string name,
        bool fallback)
    {
        if (!TryGetProperty(element, name, out var property)) return fallback;
        if (property.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return property.GetBoolean();
        return TryParseBoolean(property.ToString(), out var result) ? result : fallback;
    }

    private static double ReadDouble(
        JsonElement element,
        string name,
        double fallback)
    {
        if (!TryGetProperty(element, name, out var property)) return fallback;
        return property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var value)
            ? Math.Clamp(value, 0, 1)
            : ParseImportance(property.ToString(), fallback);
    }

    private static List<string> ReadStringList(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var property)) return new List<string>();
        if (property.ValueKind == JsonValueKind.Array)
            return property.EnumerateArray()
                .Select(x => x.ValueKind == JsonValueKind.String
                    ? x.GetString() ?? string.Empty
                    : x.ToString())
                .Where(x => x.Length > 0)
                .ToList();
        return ParseList(property.ToString());
    }

    private static bool TryGetProperty(
        JsonElement element,
        string name,
        out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            return true;
        }
        value = default;
        return false;
    }

    private static string Summarize(string? value, int maximumLength)
    {
        var oneLine = NormalizeOneLine(value, maximumLength);
        return oneLine;
    }

    private static string NormalizeOneLine(string? value, int maximumLength)
    {
        var normalized = string.Join(
            ' ',
            (value ?? string.Empty)
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength].TrimEnd() + "…";
    }

    private static string NormalizeMultiline(string? value, int maximumLength)
    {
        var normalized = (value ?? string.Empty)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength].TrimEnd() + "…";
    }

    private static string NormalizeRelativePath(string? value) =>
        (value ?? string.Empty)
        .Trim()
        .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static string StableId(string sourceType, string source)
    {
        var digest = SHA256.HashData(
            Encoding.UTF8.GetBytes($"{sourceType}|{source}".ToUpperInvariant()));
        return Convert.ToHexString(digest)[..32].ToLowerInvariant();
    }

    private static string FingerprintText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static AlbumExperienceSettings Clone(AlbumExperienceSettings source) => new()
    {
        ScanImages = source.ScanImages,
        ScanTextFiles = source.ScanTextFiles,
        AllowConversation = source.AllowConversation,
        AllowBehaviorDecision = source.AllowBehaviorDecision,
        AllowSendImagesToLlm = source.AllowSendImagesToLlm,
        AllowRuleMode = source.AllowRuleMode,
        IncludeTravelEvents = source.IncludeTravelEvents,
        MaximumResults = source.MaximumResults,
        MaximumImages = source.MaximumImages
    };

    private static AlbumExperienceRecord Clone(AlbumExperienceRecord source) => new()
    {
        Id = source.Id,
        Title = source.Title,
        Body = source.Body,
        Summary = source.Summary,
        ImageRelativePaths = new List<string>(source.ImageRelativePaths),
        SourceRelativePath = source.SourceRelativePath,
        Date = source.Date,
        Tags = new List<string>(source.Tags),
        Mood = source.Mood,
        BehaviorId = source.BehaviorId,
        Importance = source.Importance,
        IncludeInConversation = source.IncludeInConversation,
        IncludeInBehaviorDecision = source.IncludeInBehaviorDecision,
        AllowLlm = source.AllowLlm,
        AllowRules = source.AllowRules,
        UpdatedAt = source.UpdatedAt,
        SourceType = source.SourceType,
        AlbumName = source.AlbumName,
        Theme = source.Theme,
        GrowthStage = source.GrowthStage,
        SourceStatus = source.SourceStatus
    };

    private static AlbumExperienceIndex Clone(AlbumExperienceIndex source) => new()
    {
        SchemaVersion = source.SchemaVersion,
        RootFingerprint = source.RootFingerprint,
        ContentFingerprint = source.ContentFingerprint,
        Settings = Clone(source.Settings),
        Records = source.Records.Select(Clone).ToList(),
        BuildStatus = new AlbumExperienceBuildStatus
        {
            State = source.BuildStatus.State,
            StartedAt = source.BuildStatus.StartedAt,
            CompletedAt = source.BuildStatus.CompletedAt,
            ElapsedMilliseconds = source.BuildStatus.ElapsedMilliseconds,
            ScannedFileCount = source.BuildStatus.ScannedFileCount,
            ExperienceCount = source.BuildStatus.ExperienceCount,
            ErrorCount = source.BuildStatus.ErrorCount,
            UsedBackgroundWorker = source.BuildStatus.UsedBackgroundWorker,
            Message = source.BuildStatus.Message
        },
        UpdatedAt = source.UpdatedAt
    };
}
