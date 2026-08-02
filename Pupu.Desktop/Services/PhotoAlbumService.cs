using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pupu.Desktop.Models;

namespace Pupu.Desktop.Services;

public sealed class PhotoAlbumService
{
    private const int CurrentSchemaVersion = 2;
    private static readonly HashSet<string> SupportedExtensions = new(
        new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif" },
        StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private static readonly EnumerationOptions PhotoEnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
    };
    private static readonly Regex DirectoryDatePattern = new(
        @"(?<!\d)(?<year>(?:19|20)\d{2})\s*(?:年|[-_.])\s*(?<month>\d{1,2})(?:\s*(?:月|[-_.])\s*(?<day>\d{1,2})\s*日?)?(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private sealed record EffectiveAlbum(PhotoSubAlbum Album, bool IsDiscovered);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _catalogPath;

    public PhotoAlbumService(string? catalogPath = null)
    {
        _catalogPath = string.IsNullOrWhiteSpace(catalogPath)
            ? StoragePaths.AlbumsFile
            : Path.GetFullPath(catalogPath);
    }

    public async Task<PhotoAlbumCatalog> LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return await LoadWithoutLockAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PhotoAlbumCatalog> LinkRootAsync(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("请选择相册根目录。", nameof(directory));
        var fullPath = Path.GetFullPath(directory.Trim());
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"相册根目录不存在：{fullPath}");

        await _gate.WaitAsync();
        try
        {
            var catalog = await LoadWithoutLockAsync();
            catalog.RootDirectory = fullPath;
            catalog.UpdatedAt = DateTimeOffset.Now;
            await SaveWithoutLockAsync(catalog);
            return Clone(catalog);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PhotoSubAlbum> AddSubAlbumAsync(PhotoSubAlbum album)
    {
        ArgumentNullException.ThrowIfNull(album);
        await _gate.WaitAsync();
        try
        {
            var catalog = await LoadWithoutLockAsync();
            if (string.IsNullOrWhiteSpace(catalog.RootDirectory))
                throw new InvalidOperationException("请先链接一个本地相册根目录。");

            var normalized = NormalizeAlbum(album, catalog.RootDirectory);
            if (catalog.Albums.Any(x => string.Equals(
                    x.Name,
                    normalized.Name,
                    StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"已经有名为“{normalized.Name}”的子相册。");

            catalog.Albums.Add(normalized);
            catalog.UpdatedAt = DateTimeOffset.Now;
            await SaveWithoutLockAsync(catalog);
            return Clone(normalized);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteSubAlbumAsync(Guid albumId)
    {
        if (albumId == Guid.Empty) return;
        await _gate.WaitAsync();
        try
        {
            var catalog = await LoadWithoutLockAsync();
            catalog.Albums.RemoveAll(x => x.Id == albumId);
            catalog.UpdatedAt = DateTimeOffset.Now;
            await SaveWithoutLockAsync(catalog);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SavePhotoDescriptionAsync(
        string photoPath,
        string? description)
    {
        if (string.IsNullOrWhiteSpace(photoPath))
            throw new ArgumentException("请选择一张照片。", nameof(photoPath));

        await _gate.WaitAsync();
        try
        {
            var catalog = await LoadWithoutLockAsync();
            if (string.IsNullOrWhiteSpace(catalog.RootDirectory))
                throw new InvalidOperationException("请先链接一个本地相册根目录。");

            var root = Path.GetFullPath(catalog.RootDirectory);
            var fullPath = Path.GetFullPath(photoPath);
            if (!IsInside(root, fullPath) ||
                !SupportedExtensions.Contains(Path.GetExtension(fullPath)))
                throw new InvalidDataException("照片必须位于已链接的相册根目录中。");

            var relativePath = NormalizeRelativePath(
                Path.GetRelativePath(root, fullPath));
            var normalizedDescription = NormalizeText(description, 1000);
            var existing = catalog.PhotoDescriptions.FirstOrDefault(x =>
                string.Equals(
                    NormalizeRelativePath(x.RelativePath),
                    relativePath,
                    StringComparison.OrdinalIgnoreCase));
            if (normalizedDescription.Length == 0)
            {
                if (existing is not null)
                    catalog.PhotoDescriptions.Remove(existing);
            }
            else if (existing is null)
            {
                catalog.PhotoDescriptions.Add(new PhotoDescriptionEntry
                {
                    RelativePath = relativePath,
                    Description = normalizedDescription,
                    UpdatedAt = DateTimeOffset.Now
                });
            }
            else
            {
                existing.RelativePath = relativePath;
                existing.Description = normalizedDescription;
                existing.UpdatedAt = DateTimeOffset.Now;
            }

            catalog.UpdatedAt = DateTimeOffset.Now;
            await SaveWithoutLockAsync(catalog);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveRelationshipStageOverrideAsync(string? relationshipStage)
    {
        await _gate.WaitAsync();
        try
        {
            var catalog = await LoadWithoutLockAsync();
            catalog.ProfilePresentation.RelationshipStageOverride =
                NormalizeText(relationshipStage, 24);
            catalog.UpdatedAt = DateTimeOffset.Now;
            await SaveWithoutLockAsync(catalog);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<PhotoAlbumSnapshot>> GetSnapshotsAsync()
    {
        var catalog = await LoadAsync();
        return await Task.Run(() => BuildSnapshots(catalog));
    }

    private static IReadOnlyList<PhotoAlbumSnapshot> BuildSnapshots(PhotoAlbumCatalog catalog)
    {
        var result = new List<PhotoAlbumSnapshot>();
        if (string.IsNullOrWhiteSpace(catalog.RootDirectory))
            return result;

        var root = Path.GetFullPath(catalog.RootDirectory);
        result.Add(BuildSnapshot(
            Guid.Empty,
            true,
            false,
            "全部照片",
            root,
            ".",
            string.Empty,
            null,
            null,
            string.Empty));

        foreach (var effective in GetEffectiveAlbums(catalog, root)
                     .OrderBy(x => x.Album.CreatedAt)
                     .ThenBy(x => x.Album.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var album = effective.Album;
            string directory;
            try
            {
                directory = ResolveInsideRoot(root, album.RelativeDirectory);
            }
            catch (InvalidDataException)
            {
                directory = Path.Combine(
                    root,
                    "__invalid_album_path__",
                    album.Id.ToString("N"));
            }
            result.Add(BuildSnapshot(
                album.Id,
                false,
                effective.IsDiscovered,
                album.Name,
                directory,
                album.RelativeDirectory,
                album.Theme,
                album.StartDate,
                album.EndDate,
                album.GrowthStage));
        }
        return result;
    }

    public async Task<IReadOnlyList<AlbumPhotoReference>> SearchAsync(PhotoAlbumSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var catalog = await LoadAsync();
        return await Task.Run(() => Search(catalog, query));
    }

    private static IReadOnlyList<AlbumPhotoReference> Search(
        PhotoAlbumCatalog catalog,
        PhotoAlbumSearchQuery query)
    {
        if (string.IsNullOrWhiteSpace(catalog.RootDirectory))
            return Array.Empty<AlbumPhotoReference>();

        var root = Path.GetFullPath(catalog.RootDirectory);
        var effectiveAlbums = GetEffectiveAlbums(catalog, root)
            .Select(x => x.Album)
            .ToList();
        var albums = new List<PhotoSubAlbum>();
        if (query.AlbumId is null || query.AlbumId == Guid.Empty)
        {
            albums.Add(new PhotoSubAlbum
            {
                Id = Guid.Empty,
                Name = "全部照片",
                RelativeDirectory = ".",
                CreatedAt = DateTimeOffset.MinValue
            });
        }
        else
        {
            albums.AddRange(effectiveAlbums.Where(x => x.Id == query.AlbumId));
        }
        var descriptions = catalog.PhotoDescriptions
            .GroupBy(
                x => NormalizeRelativePath(x.RelativePath),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(item => item.UpdatedAt).First().Description,
                StringComparer.OrdinalIgnoreCase);

        var result = new List<AlbumPhotoReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var album in albums)
        {
            string directory;
            try
            {
                directory = ResolveInsideRoot(root, album.RelativeDirectory);
            }
            catch (InvalidDataException)
            {
                continue;
            }
            foreach (var photo in ScanPhotoFiles(directory))
            {
                if (!seen.Add(photo.FullName)) continue;
                var relativePath = NormalizeRelativePath(
                    Path.GetRelativePath(root, photo.FullName));
                var childMetadata = FindMostSpecificAlbum(
                    effectiveAlbums,
                    root,
                    photo.FullName);
                var albumName = childMetadata?.Name ?? album.Name;
                var theme = childMetadata?.Theme ?? album.Theme;
                var growthStage = childMetadata?.GrowthStage ?? album.GrowthStage;
                var capturedAt =
                    childMetadata?.StartDate ??
                    album.StartDate ??
                    photo.LastWriteTime;
                var description = descriptions.GetValueOrDefault(relativePath) ?? string.Empty;
                if (!Matches(
                        query,
                        photo.Name,
                        relativePath,
                        albumName,
                        theme,
                        growthStage,
                        description,
                        capturedAt))
                    continue;
                result.Add(new AlbumPhotoReference
                {
                    AlbumId = childMetadata?.Id ?? album.Id,
                    AlbumName = albumName,
                    FullPath = photo.FullName,
                    RelativePath = relativePath,
                    FileName = photo.Name,
                    Theme = theme,
                    GrowthStage = growthStage,
                    Description = description,
                    CapturedAt = capturedAt
                });
            }
        }
        return result
            .OrderByDescending(x => x.CapturedAt)
            .ThenBy(x => x.FileName, StringComparer.CurrentCultureIgnoreCase)
            .Take(1000)
            .ToList();
    }

    public static ParsedAlbumDirectoryMetadata ParseDirectoryMetadata(
        string? directoryName)
    {
        var name = string.Join(
            ' ',
            (directoryName ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var match = DirectoryDatePattern.Match(name);
        DateTime? start = null;
        DateTime? end = null;
        if (match.Success &&
            int.TryParse(match.Groups["year"].Value, out var year) &&
            int.TryParse(match.Groups["month"].Value, out var month) &&
            month is >= 1 and <= 12)
        {
            if (int.TryParse(match.Groups["day"].Value, out var day) &&
                day >= 1 &&
                day <= DateTime.DaysInMonth(year, month))
            {
                start = new DateTime(year, month, day);
                end = start;
            }
            else
            {
                start = new DateTime(year, month, 1);
                end = new DateTime(year, month, DateTime.DaysInMonth(year, month));
            }
        }

        var theme = match.Success ? name.Remove(match.Index, match.Length) : name;
        theme = Regex.Replace(theme, @"^[\s_\-—·|/\\()\[\]【】]+|[\s_\-—·|/\\()\[\]【】]+$", string.Empty);
        if (theme.Length == 0 && !match.Success) theme = name;
        return new ParsedAlbumDirectoryMetadata(theme, start, end);
    }

    private static IReadOnlyList<EffectiveAlbum> GetEffectiveAlbums(
        PhotoAlbumCatalog catalog,
        string root)
    {
        var result = catalog.Albums
            .Select(x => new EffectiveAlbum(Clone(x), false))
            .ToList();
        var indexedDirectories = new HashSet<string>(
            catalog.Albums.Select(x => NormalizeRelativePath(x.RelativeDirectory)),
            StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root)) return result;

        IEnumerable<string> directories;
        try
        {
            directories = Directory
                .EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
                .Where(path =>
                {
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        return !attributes.HasFlag(FileAttributes.ReparsePoint) &&
                               !attributes.HasFlag(FileAttributes.System);
                    }
                    catch (IOException) { return false; }
                    catch (UnauthorizedAccessException) { return false; }
                })
                .ToList();
        }
        catch (IOException)
        {
            return result;
        }
        catch (UnauthorizedAccessException)
        {
            return result;
        }

        foreach (var directory in directories)
        {
            var relative = NormalizeRelativePath(Path.GetRelativePath(root, directory));
            if (!indexedDirectories.Add(relative)) continue;
            var name = Path.GetFileName(directory);
            var parsed = ParseDirectoryMetadata(name);
            DateTimeOffset createdAt;
            try { createdAt = new DirectoryInfo(directory).CreationTimeUtc; }
            catch { createdAt = DateTimeOffset.MinValue; }
            result.Add(new EffectiveAlbum(
                new PhotoSubAlbum
                {
                    Id = StableDirectoryId(relative),
                    Name = name,
                    RelativeDirectory = relative,
                    Theme = parsed.Theme,
                    StartDate = parsed.StartDate,
                    EndDate = parsed.EndDate,
                    CreatedAt = createdAt
                },
                true));
        }
        return result;
    }

    private static Guid StableDirectoryId(string relativeDirectory)
    {
        var digest = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                NormalizeRelativePath(relativeDirectory).ToUpperInvariant()));
        return new Guid(digest.AsSpan(0, 16));
    }

    private static PhotoAlbumSnapshot BuildSnapshot(
        Guid albumId,
        bool isRoot,
        bool isDiscovered,
        string name,
        string directory,
        string relativeDirectory,
        string theme,
        DateTime? startDate,
        DateTime? endDate,
        string growthStage)
    {
        if (!Directory.Exists(directory))
        {
            return new PhotoAlbumSnapshot
            {
                AlbumId = albumId,
                IsRoot = isRoot,
                IsDiscovered = isDiscovered,
                Name = name,
                DirectoryPath = directory,
                RelativeDirectory = relativeDirectory,
                Theme = theme,
                StartDate = startDate,
                EndDate = endDate,
                GrowthStage = growthStage,
                IsAvailable = false
            };
        }

        var files = ScanPhotoFiles(directory)
            .OrderByDescending(x => x.LastWriteTimeUtc)
            .ThenBy(x => x.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new PhotoAlbumSnapshot
        {
            AlbumId = albumId,
            IsRoot = isRoot,
            IsDiscovered = isDiscovered,
            Name = name,
            DirectoryPath = directory,
            RelativeDirectory = relativeDirectory,
            Theme = theme,
            StartDate = startDate,
            EndDate = endDate,
            GrowthStage = growthStage,
            IsAvailable = true,
            PhotoCount = files.Count,
            CoverPath = files.FirstOrDefault()?.FullName
        };
    }

    private async Task<PhotoAlbumCatalog> LoadWithoutLockAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_catalogPath)!);
        if (!File.Exists(_catalogPath))
            return new PhotoAlbumCatalog();

        try
        {
            await using var stream = File.Open(
                _catalogPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            var catalog = await JsonSerializer.DeserializeAsync<PhotoAlbumCatalog>(stream, JsonOptions)
                          ?? new PhotoAlbumCatalog();
            NormalizeCatalog(catalog);
            return catalog;
        }
        catch (JsonException)
        {
            var backup = _catalogPath +
                         $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(_catalogPath, backup, false);
            return new PhotoAlbumCatalog();
        }
    }

    private async Task SaveWithoutLockAsync(PhotoAlbumCatalog catalog)
    {
        NormalizeCatalog(catalog);
        Directory.CreateDirectory(Path.GetDirectoryName(_catalogPath)!);
        var temporary = _catalogPath + ".tmp";
        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, catalog, JsonOptions);
            await stream.FlushAsync();
        }
        File.Move(temporary, _catalogPath, true);
    }

    private static void NormalizeCatalog(PhotoAlbumCatalog catalog)
    {
        catalog.SchemaVersion = CurrentSchemaVersion;
        catalog.RootDirectory = string.IsNullOrWhiteSpace(catalog.RootDirectory)
            ? string.Empty
            : Path.GetFullPath(catalog.RootDirectory.Trim());
        catalog.ProfilePresentation ??= new ProfilePresentationSettings();
        catalog.ProfilePresentation.RelationshipStageOverride =
            NormalizeText(catalog.ProfilePresentation.RelationshipStageOverride, 24);
        catalog.Albums ??= new List<PhotoSubAlbum>();
        catalog.Albums = catalog.Albums
            .Where(x => x is not null)
            .Select(x => string.IsNullOrWhiteSpace(catalog.RootDirectory)
                ? NormalizeAlbumWithoutRoot(x)
                : NormalizeAlbum(x, catalog.RootDirectory))
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .Take(200)
            .ToList();
        catalog.PhotoDescriptions ??= new List<PhotoDescriptionEntry>();
        catalog.PhotoDescriptions = catalog.PhotoDescriptions
            .Where(x => x is not null)
            .Select(x => new PhotoDescriptionEntry
            {
                RelativePath = NormalizeRelativePath(x.RelativePath),
                Description = NormalizeText(x.Description, 1000),
                UpdatedAt = x.UpdatedAt == default ? DateTimeOffset.Now : x.UpdatedAt
            })
            .Where(x => x.RelativePath.Length > 0 && x.Description.Length > 0)
            .GroupBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(item => item.UpdatedAt).First())
            .Take(10000)
            .ToList();
    }

    private static PhotoSubAlbum NormalizeAlbum(PhotoSubAlbum source, string root)
    {
        var normalized = NormalizeAlbumWithoutRoot(source);
        _ = ResolveInsideRoot(root, normalized.RelativeDirectory);
        return normalized;
    }

    private static PhotoSubAlbum NormalizeAlbumWithoutRoot(PhotoSubAlbum source)
    {
        var start = source.StartDate?.Date;
        var end = source.EndDate?.Date;
        if (start is not null && end is not null && end < start)
            throw new InvalidDataException("子相册结束日期不能早于开始日期。");
        var relative = string.IsNullOrWhiteSpace(source.RelativeDirectory)
            ? "."
            : source.RelativeDirectory.Trim();
        if (Path.IsPathRooted(relative))
            throw new InvalidDataException("子相册目录必须是相对于根相册的路径。");
        return new PhotoSubAlbum
        {
            Id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id,
            Name = NormalizeText(source.Name, 48, "未命名子相册"),
            RelativeDirectory = relative,
            Theme = NormalizeText(source.Theme, 60),
            StartDate = start,
            EndDate = end,
            GrowthStage = NormalizeText(source.GrowthStage, 40),
            CreatedAt = source.CreatedAt == default ? DateTimeOffset.Now : source.CreatedAt
        };
    }

    private static string ResolveInsideRoot(string root, string relativeDirectory)
    {
        if (Path.IsPathRooted(relativeDirectory))
            throw new InvalidDataException("子相册目录不能使用绝对路径。");
        var fullRoot = Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var resolved = Path.GetFullPath(Path.Combine(fullRoot, relativeDirectory));
        if (string.Equals(resolved, fullRoot, StringComparison.OrdinalIgnoreCase))
            return resolved;
        var prefix = fullRoot + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("子相册目录不能离开相册根目录。");
        return resolved;
    }

    private static IEnumerable<FileInfo> ScanPhotoFiles(string directory)
    {
        if (!Directory.Exists(directory)) return Enumerable.Empty<FileInfo>();
        try
        {
            return Directory
                .EnumerateFiles(directory, "*", PhotoEnumerationOptions)
                .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
                .Select(path =>
                {
                    try { return new FileInfo(path); }
                    catch (IOException) { return null; }
                    catch (UnauthorizedAccessException) { return null; }
                })
                .Where(x => x is not null)
                .Cast<FileInfo>()
                .ToList();
        }
        catch (IOException)
        {
            return Enumerable.Empty<FileInfo>();
        }
        catch (UnauthorizedAccessException)
        {
            return Enumerable.Empty<FileInfo>();
        }
    }

    private static PhotoSubAlbum? FindMostSpecificAlbum(
        IEnumerable<PhotoSubAlbum> albums,
        string root,
        string photoPath)
    {
        return albums
            .Select(album =>
            {
                try { return (Album: album, Directory: ResolveInsideRoot(root, album.RelativeDirectory)); }
                catch (InvalidDataException) { return (Album: album, Directory: string.Empty); }
            })
            .Where(x => x.Directory.Length > 0 && IsInside(x.Directory, photoPath))
            .OrderByDescending(x => x.Directory.Length)
            .Select(x => x.Album)
            .FirstOrDefault();
    }

    private static bool IsInside(string directory, string file)
    {
        var prefix = Path.GetFullPath(directory).TrimEnd(
                         Path.DirectorySeparatorChar,
                         Path.AltDirectorySeparatorChar) +
                     Path.DirectorySeparatorChar;
        return Path.GetFullPath(file).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Matches(
        PhotoAlbumSearchQuery query,
        string fileName,
        string relativePath,
        string albumName,
        string theme,
        string growthStage,
        string description,
        DateTime capturedAt)
    {
        var keyword = query.Keyword.Trim();
        if (keyword.Length > 0 &&
            !new[] { fileName, relativePath, albumName, theme, growthStage, description }
                .Any(value => value.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)))
            return false;
        var requestedTheme = query.Theme.Trim();
        if (requestedTheme.Length > 0 &&
            !theme.Contains(requestedTheme, StringComparison.CurrentCultureIgnoreCase))
            return false;
        if (query.StartDate is { } start && capturedAt.Date < start.Date) return false;
        if (query.EndDate is { } end && capturedAt.Date > end.Date) return false;
        return true;
    }

    private static string NormalizeText(string? value, int maximumLength, string fallback = "")
    {
        var normalized = string.Join(
            ' ',
            (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0) normalized = fallback;
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }

    private static string NormalizeRelativePath(string? value) =>
        (value ?? string.Empty)
        .Trim()
        .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static PhotoAlbumCatalog Clone(PhotoAlbumCatalog source) => new()
    {
        SchemaVersion = source.SchemaVersion,
        RootDirectory = source.RootDirectory,
        Albums = source.Albums.Select(Clone).ToList(),
        PhotoDescriptions = source.PhotoDescriptions.Select(x => new PhotoDescriptionEntry
        {
            RelativePath = x.RelativePath,
            Description = x.Description,
            UpdatedAt = x.UpdatedAt
        }).ToList(),
        ProfilePresentation = new ProfilePresentationSettings
        {
            RelationshipStageOverride = source.ProfilePresentation.RelationshipStageOverride
        },
        UpdatedAt = source.UpdatedAt
    };

    private static PhotoSubAlbum Clone(PhotoSubAlbum source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        RelativeDirectory = source.RelativeDirectory,
        Theme = source.Theme,
        StartDate = source.StartDate,
        EndDate = source.EndDate,
        GrowthStage = source.GrowthStage,
        CreatedAt = source.CreatedAt
    };
}
