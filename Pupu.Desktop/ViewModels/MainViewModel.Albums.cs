using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Microsoft.Win32;
using Pupu.Behavior;
using Pupu.Desktop.Models;
using Pupu.Desktop.Services;

namespace Pupu.Desktop.ViewModels;

public sealed class PhotoAlbumCardItem
{
    public required Guid AlbumId { get; init; }
    public required bool IsRoot { get; init; }
    public required bool IsDiscovered { get; init; }
    public required string Name { get; init; }
    public required string DirectoryPath { get; init; }
    public required string Metadata { get; init; }
    public required string Availability { get; init; }
    public required int PhotoCount { get; init; }
    public string CoverPath { get; init; } = string.Empty;
    public object? CoverImage { get; init; }
}

public sealed class AlbumPhotoItem
{
    public required string FileName { get; init; }
    public required string FullPath { get; init; }
    public required string AlbumName { get; init; }
    public required string Metadata { get; init; }
    public string Description { get; init; } = string.Empty;
    public object? Thumbnail { get; init; }
}

public sealed class AlbumExperienceItem
{
    public required AlbumExperienceRecord Record { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required string Metadata { get; init; }
    public required string Tags { get; init; }
    public required string Images { get; init; }
    public required string Permissions { get; init; }
}

public sealed partial class MainViewModel
{
    private sealed record AlbumConversationMemory(
        string Context,
        IReadOnlyList<ModelImageInput> Images,
        IReadOnlyList<AlbumExperienceSearchResult> Matches,
        int InjectedExperienceCount)
    {
        public static AlbumConversationMemory Empty { get; } =
            new(
                string.Empty,
                Array.Empty<ModelImageInput>(),
                Array.Empty<AlbumExperienceSearchResult>(),
                0);
    }

    private const string AutomaticRelationshipStage = "自动（根据长期关系）";
    private const long MaximumAlbumImageBytes = 6L * 1024 * 1024;
    private static readonly IReadOnlyDictionary<string, string> VisionImageMimeTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp"
        };
    private static readonly string[] AlbumConversationCues =
    {
        "照片", "相册", "图片", "看图", "回忆", "以前", "那次", "那天",
        "小时候", "成长", "去年", "今年", "春节", "圣诞", "生日"
    };
    private static readonly string[] AlbumKeywordNoise =
    {
        "帮我", "给我", "想看", "看看", "看一看", "找一下", "找找", "寻找",
        "照片", "相册", "图片", "看图", "回忆", "以前", "那次", "那天",
        "还记得", "记得", "朴朴", "pupu", "Pupu", "我们", "主人",
        "是什么", "在哪里", "有没有", "可以", "一下", "的", "吗", "呢", "呀"
    };

    private readonly PhotoAlbumService _photoAlbums = new();
    private readonly AlbumExperienceService _albumExperiences = new();
    private readonly ConversationSessionStore _conversationSession = new();
    private readonly SemaphoreSlim _albumLoadGate = new(1, 1);
    private static readonly ConcurrentDictionary<string, object?> ThumbnailCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<PhotoAlbumCardItem> _albumCards = new();
    private readonly ObservableCollection<AlbumPhotoItem> _albumPhotos = new();
    private readonly ObservableCollection<AlbumPhotoItem> _albumSearchResults = new();
    private readonly ObservableCollection<AlbumExperienceItem> _recentExperienceMatches = new();
    private AlbumExperienceSettings _experienceSettings = new();
    private AlbumExperienceItem? _selectedExperience;
    private string _experienceIndexStatus = "经历索引尚未加载。";
    private string _lastExperienceQuery = "尚无经历检索";
    private int _lastExperienceHitCount;
    private int _lastExperienceLlmCount;
    private int _lastExperienceImageCount;
    private bool _lastExperienceRuleUsed;
    private string _lastExperienceBehaviorSuggestion = "尚无相册经历行为建议";
    private bool _albumLoadStarted;
    private bool _albumsLoaded;
    private string _albumRootPath = string.Empty;
    private string _albumStatus = "相册索引尚未加载。照片始终保留在主人选择的原文件夹中。";
    private string _newAlbumName = string.Empty;
    private string _newAlbumRelativeDirectory = string.Empty;
    private string _newAlbumTheme = string.Empty;
    private DateTime? _newAlbumStartDate;
    private DateTime? _newAlbumEndDate;
    private string _newAlbumGrowthStage = string.Empty;
    private PhotoAlbumCardItem? _selectedAlbumCard;
    private AlbumPhotoItem? _selectedAlbumPhoto;
    private string _selectedPhotoDescription = string.Empty;
    private object? _selectedPhotoPreview;
    private string _albumSearchKeyword = string.Empty;
    private string _albumSearchTheme = string.Empty;
    private DateTime? _albumSearchStartDate;
    private DateTime? _albumSearchEndDate;
    private string _selectedRelationshipStage = AutomaticRelationshipStage;
    private string _profilePresentationStatus = "好感阶段默认随长期关系自动变化。";
    private object? _petProfilePortrait;
    private ICommand? _selectAlbumRootCommand;
    private ICommand? _addSubAlbumCommand;
    private ICommand? _deleteSubAlbumCommand;
    private ICommand? _refreshAlbumsCommand;
    private ICommand? _openAlbumDirectoryCommand;
    private ICommand? _searchAlbumPhotosCommand;
    private ICommand? _savePhotoDescriptionCommand;
    private ICommand? _saveRelationshipStageCommand;
    private ICommand? _saveExperienceSettingsCommand;
    private ICommand? _rebuildExperienceIndexCommand;
    private ICommand? _searchExperiencesCommand;

    public ObservableCollection<PhotoAlbumCardItem> AlbumCards
    {
        get
        {
            BeginAlbumLoad();
            return _albumCards;
        }
    }

    public ObservableCollection<AlbumPhotoItem> AlbumSearchResults
    {
        get
        {
            BeginAlbumLoad();
            return _albumSearchResults;
        }
    }

    public ObservableCollection<AlbumPhotoItem> AlbumPhotos
    {
        get
        {
            BeginAlbumLoad();
            return _albumPhotos;
        }
    }

    public ObservableCollection<AlbumExperienceItem> RecentExperienceMatches
    {
        get
        {
            BeginAlbumLoad();
            return _recentExperienceMatches;
        }
    }

    public AlbumExperienceItem? SelectedExperience
    {
        get => _selectedExperience;
        set => SetField(ref _selectedExperience, value);
    }

    public bool ExperienceScanImages
    {
        get => _experienceSettings.ScanImages;
        set
        {
            if (_experienceSettings.ScanImages == value) return;
            _experienceSettings.ScanImages = value;
            OnPropertyChanged();
        }
    }

    public bool ExperienceScanTextFiles
    {
        get => _experienceSettings.ScanTextFiles;
        set
        {
            if (_experienceSettings.ScanTextFiles == value) return;
            _experienceSettings.ScanTextFiles = value;
            OnPropertyChanged();
        }
    }

    public bool ExperienceAllowConversation
    {
        get => _experienceSettings.AllowConversation;
        set
        {
            if (_experienceSettings.AllowConversation == value) return;
            _experienceSettings.AllowConversation = value;
            OnPropertyChanged();
        }
    }

    public bool ExperienceAllowBehavior
    {
        get => _experienceSettings.AllowBehaviorDecision;
        set
        {
            if (_experienceSettings.AllowBehaviorDecision == value) return;
            _experienceSettings.AllowBehaviorDecision = value;
            OnPropertyChanged();
        }
    }

    public bool ExperienceAllowSendImages
    {
        get => _experienceSettings.AllowSendImagesToLlm;
        set
        {
            if (_experienceSettings.AllowSendImagesToLlm == value) return;
            _experienceSettings.AllowSendImagesToLlm = value;
            OnPropertyChanged();
        }
    }

    public bool ExperienceAllowRules
    {
        get => _experienceSettings.AllowRuleMode;
        set
        {
            if (_experienceSettings.AllowRuleMode == value) return;
            _experienceSettings.AllowRuleMode = value;
            OnPropertyChanged();
        }
    }

    public bool ExperienceIncludeTravelEvents
    {
        get => _experienceSettings.IncludeTravelEvents;
        set
        {
            if (_experienceSettings.IncludeTravelEvents == value) return;
            _experienceSettings.IncludeTravelEvents = value;
            OnPropertyChanged();
        }
    }

    public int ExperienceMaximumResults
    {
        get => _experienceSettings.MaximumResults;
        set
        {
            var normalized = Math.Clamp(value, 1, 10);
            if (_experienceSettings.MaximumResults == normalized) return;
            _experienceSettings.MaximumResults = normalized;
            OnPropertyChanged();
        }
    }

    public int ExperienceMaximumImages
    {
        get => _experienceSettings.MaximumImages;
        set
        {
            var normalized = Math.Clamp(value, 0, 2);
            if (_experienceSettings.MaximumImages == normalized) return;
            _experienceSettings.MaximumImages = normalized;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<int> ExperienceResultOptions { get; } =
        new[] { 1, 2, 3, 4, 5 };
    public IReadOnlyList<int> ExperienceImageOptions { get; } =
        new[] { 0, 1, 2 };

    public string ExperienceIndexStatus
    {
        get
        {
            BeginAlbumLoad();
            return _experienceIndexStatus;
        }
        private set => SetField(ref _experienceIndexStatus, value);
    }

    public string LastExperienceQuery
    {
        get => _lastExperienceQuery;
        private set => SetField(ref _lastExperienceQuery, value);
    }

    public int LastExperienceHitCount
    {
        get => _lastExperienceHitCount;
        private set => SetField(ref _lastExperienceHitCount, value);
    }

    public int LastExperienceLlmCount
    {
        get => _lastExperienceLlmCount;
        private set => SetField(ref _lastExperienceLlmCount, value);
    }

    public int LastExperienceImageCount
    {
        get => _lastExperienceImageCount;
        private set => SetField(ref _lastExperienceImageCount, value);
    }

    public bool LastExperienceRuleUsed
    {
        get => _lastExperienceRuleUsed;
        private set => SetField(ref _lastExperienceRuleUsed, value);
    }

    public string LastExperienceBehaviorSuggestion
    {
        get => _lastExperienceBehaviorSuggestion;
        private set => SetField(ref _lastExperienceBehaviorSuggestion, value);
    }

    public string ExperienceDebugStatus =>
        $"检索：{LastExperienceQuery} · 命中 {LastExperienceHitCount} · " +
        $"注入模型 {LastExperienceLlmCount} · 发送图片 {LastExperienceImageCount} · " +
        $"规则模式使用：{(LastExperienceRuleUsed ? "是" : "否")} · {LastExperienceBehaviorSuggestion}";

    public string AlbumRootPath
    {
        get
        {
            BeginAlbumLoad();
            return _albumRootPath;
        }
        private set => SetField(ref _albumRootPath, value);
    }

    public string AlbumStatus
    {
        get
        {
            BeginAlbumLoad();
            return _albumStatus;
        }
        private set => SetField(ref _albumStatus, value);
    }

    public string NewAlbumName
    {
        get => _newAlbumName;
        set => SetField(ref _newAlbumName, value);
    }

    public string NewAlbumRelativeDirectory
    {
        get => _newAlbumRelativeDirectory;
        set => SetField(ref _newAlbumRelativeDirectory, value);
    }

    public string NewAlbumTheme
    {
        get => _newAlbumTheme;
        set => SetField(ref _newAlbumTheme, value);
    }

    public DateTime? NewAlbumStartDate
    {
        get => _newAlbumStartDate;
        set => SetField(ref _newAlbumStartDate, value);
    }

    public DateTime? NewAlbumEndDate
    {
        get => _newAlbumEndDate;
        set => SetField(ref _newAlbumEndDate, value);
    }

    public string NewAlbumGrowthStage
    {
        get => _newAlbumGrowthStage;
        set => SetField(ref _newAlbumGrowthStage, value);
    }

    public PhotoAlbumCardItem? SelectedAlbumCard
    {
        get => _selectedAlbumCard;
        set
        {
            if (!SetField(ref _selectedAlbumCard, value)) return;
            _ = LoadSelectedAlbumPhotosAsync();
        }
    }

    public AlbumPhotoItem? SelectedAlbumPhoto
    {
        get => _selectedAlbumPhoto;
        set
        {
            if (!SetField(ref _selectedAlbumPhoto, value)) return;
            SelectedPhotoDescription = value?.Description ?? string.Empty;
            SelectedPhotoPreview = LoadThumbnail(value?.FullPath, 920);
        }
    }

    public string SelectedPhotoDescription
    {
        get => _selectedPhotoDescription;
        set => SetField(ref _selectedPhotoDescription, value);
    }

    public object? SelectedPhotoPreview
    {
        get => _selectedPhotoPreview;
        private set => SetField(ref _selectedPhotoPreview, value);
    }

    public string AlbumSearchKeyword
    {
        get => _albumSearchKeyword;
        set => SetField(ref _albumSearchKeyword, value);
    }

    public string AlbumSearchTheme
    {
        get => _albumSearchTheme;
        set => SetField(ref _albumSearchTheme, value);
    }

    public DateTime? AlbumSearchStartDate
    {
        get => _albumSearchStartDate;
        set => SetField(ref _albumSearchStartDate, value);
    }

    public DateTime? AlbumSearchEndDate
    {
        get => _albumSearchEndDate;
        set => SetField(ref _albumSearchEndDate, value);
    }

    public IReadOnlyList<string> RelationshipStageOptions { get; } = new[]
    {
        AutomaticRelationshipStage,
        "观察中",
        "熟悉",
        "亲近",
        "家人"
    };

    public string SelectedRelationshipStage
    {
        get
        {
            BeginAlbumLoad();
            return _selectedRelationshipStage;
        }
        set
        {
            if (!RelationshipStageOptions.Contains(value)) value = AutomaticRelationshipStage;
            if (!SetField(ref _selectedRelationshipStage, value)) return;
            OnPropertyChanged(nameof(RelationshipStageDisplay));
        }
    }

    public string ProfilePresentationStatus
    {
        get
        {
            BeginAlbumLoad();
            return _profilePresentationStatus;
        }
        private set => SetField(ref _profilePresentationStatus, value);
    }

    public string RelationshipStageDisplay
    {
        get
        {
            var automatic = CalculateAutomaticRelationshipStage();
            return SelectedRelationshipStage == AutomaticRelationshipStage
                ? $"{automatic} · 随长期关系自动变化"
                : $"{SelectedRelationshipStage} · 主人设定的展示阶段（底层关系仍继续自然变化）";
        }
    }

    public string AutomaticPersonalitySummary
    {
        get
        {
            if (!IsReady) return "正在根据长期相处记录整理性格摘要…";
            var temperament = _memory.Personality.Temperament;
            var traits = new[]
            {
                ("活泼", temperament.Playful),
                ("黏人", temperament.Affectionate),
                ("敏感", temperament.Sensitive),
                ("独立", temperament.Independent),
                ("淘气", temperament.Mischievous)
            };
            var strongest = traits
                .OrderByDescending(x => x.Item2)
                .Take(2)
                .Select(x => x.Item1)
                .ToArray();
            var habits = _memory.Personality.DerivedHabitPreferences.Values
                .Where(x => x.EffectiveWeight >= 0.08)
                .OrderByDescending(x => x.EffectiveWeight)
                .Select(x => FriendlyHabitLabel(x.BehaviorId))
                .Where(x => x.Length > 0)
                .Distinct()
                .Take(2)
                .ToArray();
            var habitText = habits.Length == 0
                ? "长期习惯还在慢慢形成"
                : $"长期相处中更常表现为{string.Join("、", habits)}";
            return $"整体是{string.Join("、", strongest)}的幼猫；{habitText}。这段摘要只读取已保存的性格和跨天行为，不改变天生设定。";
        }
    }

    public object? PetProfilePortrait
    {
        get
        {
            if (_petProfilePortrait is not null) return _petProfilePortrait;
            try
            {
                var cell = _assetPack.CellSize;
                _petProfilePortrait = _presentationHost.CropImage(
                    _assetPack.GetSheet("core"),
                    0,
                    0,
                    cell,
                    cell);
            }
            catch
            {
                _petProfilePortrait = null;
            }
            return _petProfilePortrait;
        }
    }

    public ICommand SelectAlbumRootCommand =>
        _selectAlbumRootCommand ??= AsyncCommand(SelectAlbumRootAsync);
    public ICommand AddSubAlbumCommand =>
        _addSubAlbumCommand ??= AsyncCommand(AddSubAlbumAsync);
    public ICommand DeleteSubAlbumCommand =>
        _deleteSubAlbumCommand ??= AsyncCommand(DeleteSelectedSubAlbumAsync);
    public ICommand RefreshAlbumsCommand =>
        _refreshAlbumsCommand ??= AsyncCommand(RefreshAlbumsAsync);
    public ICommand OpenAlbumDirectoryCommand =>
        _openAlbumDirectoryCommand ??= new RelayCommand(OpenSelectedAlbumDirectory);
    public ICommand SearchAlbumPhotosCommand =>
        _searchAlbumPhotosCommand ??= AsyncCommand(SearchAlbumPhotosAsync);
    public ICommand SavePhotoDescriptionCommand =>
        _savePhotoDescriptionCommand ??= AsyncCommand(SaveSelectedPhotoDescriptionAsync);
    public ICommand SaveRelationshipStageCommand =>
        _saveRelationshipStageCommand ??= AsyncCommand(SaveRelationshipStageAsync);
    public ICommand SaveExperienceSettingsCommand =>
        _saveExperienceSettingsCommand ??= AsyncCommand(SaveExperienceSettingsAsync);
    public ICommand RebuildExperienceIndexCommand =>
        _rebuildExperienceIndexCommand ??= AsyncCommand(RebuildExperienceIndexAsync);
    public ICommand SearchExperiencesCommand =>
        _searchExperiencesCommand ??= AsyncCommand(SearchExperiencesFromPanelAsync);

    private void BeginAlbumLoad()
    {
        if (_albumLoadStarted) return;
        _albumLoadStarted = true;
        _ = EnsureAlbumsLoadedAsync();
    }

    private async Task EnsureAlbumsLoadedAsync()
    {
        if (_albumsLoaded) return;
        await _albumLoadGate.WaitAsync();
        try
        {
            if (_albumsLoaded) return;
            await ReloadAlbumDataAsync();
            _albumsLoaded = true;
        }
        catch (Exception ex)
        {
            AlbumStatus = $"相册索引读取失败：{ex.Message}";
            _albumLoadStarted = false;
        }
        finally
        {
            _albumLoadGate.Release();
        }
    }

    private async Task ReloadAlbumDataAsync()
    {
        var selectedAlbumId = SelectedAlbumCard?.AlbumId;
        var catalog = await _photoAlbums.LoadAsync();
        AlbumRootPath = catalog.RootDirectory;
        await LoadExperienceIndexStateAsync(catalog);
        var savedStage = catalog.ProfilePresentation.RelationshipStageOverride;
        _selectedRelationshipStage = RelationshipStageOptions.Contains(savedStage)
            ? savedStage
            : AutomaticRelationshipStage;
        OnPropertyChanged(nameof(SelectedRelationshipStage));
        OnPropertyChanged(nameof(RelationshipStageDisplay));

        var snapshots = await _photoAlbums.GetSnapshotsAsync();
        var cards = await Task.Run(() => snapshots.Select(ToCard).ToList());
        _albumCards.Clear();
        foreach (var card in cards)
            _albumCards.Add(card);
        OnPropertyChanged(nameof(AlbumCards));
        SelectedAlbumCard = selectedAlbumId is { } id
            ? _albumCards.FirstOrDefault(x => x.AlbumId == id) ?? _albumCards.FirstOrDefault()
            : _albumCards.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(catalog.RootDirectory))
        {
            AlbumStatus = "尚未链接本地图片根目录。相册只保存路径与主题等索引，不会复制、移动或修改原图。";
            return;
        }
        var available = snapshots.Count(x => x.IsAvailable);
        var unavailable = snapshots.Count - available;
        var photos = snapshots.FirstOrDefault(x => x.IsRoot)?.PhotoCount ?? 0;
        var albumCount = Math.Max(0, snapshots.Count - 1);
        AlbumStatus = unavailable == 0
            ? $"已链接 {catalog.RootDirectory} · {albumCount} 个子相册 · 共发现 {photos} 张图片。"
            : $"已链接 {catalog.RootDirectory} · {unavailable} 个目录暂时失联；元数据已保留，不会删除。";
    }

    private async Task LoadExperienceIndexStateAsync(PhotoAlbumCatalog catalog)
    {
        try
        {
            var index = string.IsNullOrWhiteSpace(catalog.RootDirectory)
                ? await _albumExperiences.LoadAsync(_lifetimeCancellation.Token)
                : await _albumExperiences.EnsureFreshAsync(
                    catalog,
                    _lifetimeCancellation.Token);
            ApplyExperienceIndex(index);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch (OperationCanceledException)
        {
            ExperienceIndexStatus = "较新的经历索引扫描已接管，本次旧结果已丢弃。";
        }
        catch
        {
            ExperienceIndexStatus = "经历索引暂时不可用；原相册和普通聊天仍可继续使用。";
        }
    }

    private void ApplyExperienceIndex(AlbumExperienceIndex index)
    {
        _experienceSettings = index.Settings;
        OnPropertyChanged(nameof(ExperienceScanImages));
        OnPropertyChanged(nameof(ExperienceScanTextFiles));
        OnPropertyChanged(nameof(ExperienceAllowConversation));
        OnPropertyChanged(nameof(ExperienceAllowBehavior));
        OnPropertyChanged(nameof(ExperienceAllowSendImages));
        OnPropertyChanged(nameof(ExperienceAllowRules));
        OnPropertyChanged(nameof(ExperienceIncludeTravelEvents));
        OnPropertyChanged(nameof(ExperienceMaximumResults));
        OnPropertyChanged(nameof(ExperienceMaximumImages));
        ExperienceIndexStatus =
            $"{index.BuildStatus.Message} schema v{index.SchemaVersion} · " +
            $"耗时 {index.BuildStatus.ElapsedMilliseconds}ms · " +
            $"文件 {index.BuildStatus.ScannedFileCount} · 错误 {index.BuildStatus.ErrorCount} · " +
            $"后台扫描 {(index.BuildStatus.UsedBackgroundWorker ? "是" : "未运行")}";
    }

    private async Task SelectAlbumRootAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择主人和宠物的本地相册根目录",
            Multiselect = false
        };
        if (Directory.Exists(AlbumRootPath))
            dialog.InitialDirectory = AlbumRootPath;
        if (dialog.ShowDialog() != true) return;
        try
        {
            await _photoAlbums.LinkRootAsync(dialog.FolderName);
            _albumsLoaded = false;
            await EnsureAlbumsLoadedAsync();
        }
        catch (Exception ex)
        {
            AlbumStatus = $"无法链接相册根目录：{ex.Message}";
        }
    }

    private async Task AddSubAlbumAsync()
    {
        if (string.IsNullOrWhiteSpace(NewAlbumName))
        {
            AlbumStatus = "请先填写子相册名称。";
            return;
        }
        try
        {
            await _photoAlbums.AddSubAlbumAsync(new PhotoSubAlbum
            {
                Name = NewAlbumName,
                RelativeDirectory = string.IsNullOrWhiteSpace(NewAlbumRelativeDirectory)
                    ? "."
                    : NewAlbumRelativeDirectory,
                Theme = NewAlbumTheme,
                StartDate = NewAlbumStartDate,
                EndDate = NewAlbumEndDate,
                GrowthStage = NewAlbumGrowthStage
            });
            NewAlbumName = string.Empty;
            NewAlbumRelativeDirectory = string.Empty;
            NewAlbumTheme = string.Empty;
            NewAlbumStartDate = null;
            NewAlbumEndDate = null;
            NewAlbumGrowthStage = string.Empty;
            _albumsLoaded = false;
            await EnsureAlbumsLoadedAsync();
        }
        catch (Exception ex)
        {
            AlbumStatus = $"无法保存子相册：{ex.Message}";
        }
    }

    private async Task DeleteSelectedSubAlbumAsync()
    {
        if (SelectedAlbumCard is null)
        {
            AlbumStatus = "请先选择一个子相册。";
            return;
        }
        if (SelectedAlbumCard.IsRoot)
        {
            AlbumStatus = "“全部照片”是根目录视图，不能作为子相册删除。";
            return;
        }
        if (SelectedAlbumCard.IsDiscovered)
        {
            AlbumStatus = "这是从真实子目录自动发现的卡片；不会从面板删除或改动本地文件夹。";
            return;
        }
        var confirmed = _presentationHost.Confirm(
            "删除子相册索引",
            $"只删除子相册“{SelectedAlbumCard.Name}”的索引吗？本地文件夹和照片不会被删除。");
        if (!confirmed) return;
        try
        {
            await _photoAlbums.DeleteSubAlbumAsync(SelectedAlbumCard.AlbumId);
            SelectedAlbumCard = null;
            _albumsLoaded = false;
            await EnsureAlbumsLoadedAsync();
        }
        catch (Exception ex)
        {
            AlbumStatus = $"无法删除子相册索引：{ex.Message}";
        }
    }

    private async Task RefreshAlbumsAsync()
    {
        try
        {
            ThumbnailCache.Clear();
            SelectedPhotoPreview = null;
            _albumsLoaded = false;
            await EnsureAlbumsLoadedAsync();
            await RebuildExperienceIndexAsync();
        }
        catch (Exception ex)
        {
            AlbumStatus = $"无法刷新相册：{ex.Message}";
        }
    }

    private void OpenSelectedAlbumDirectory()
    {
        var path = SelectedAlbumCard?.DirectoryPath;
        if (string.IsNullOrWhiteSpace(path)) path = AlbumRootPath;
        if (!Directory.Exists(path))
        {
            AlbumStatus = "这个目录当前不可用；相册元数据仍然保留。";
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AlbumStatus = $"无法打开目录：{ex.Message}";
        }
    }

    private async Task LoadSelectedAlbumPhotosAsync()
    {
        var selected = SelectedAlbumCard;
        _albumPhotos.Clear();
        SelectedAlbumPhoto = null;
        OnPropertyChanged(nameof(AlbumPhotos));
        if (selected is null) return;

        try
        {
            var photos = await _photoAlbums.SearchAsync(new PhotoAlbumSearchQuery
            {
                AlbumId = selected.AlbumId
            });
            if (!ReferenceEquals(selected, SelectedAlbumCard) &&
                selected.AlbumId != SelectedAlbumCard?.AlbumId)
                return;
            var items = await Task.Run(() => photos
                .Take(240)
                .Select(photo => ToPhotoItem(photo, includeThumbnail: true))
                .ToList());
            if (selected.AlbumId != SelectedAlbumCard?.AlbumId) return;
            foreach (var item in items)
                _albumPhotos.Add(item);
            OnPropertyChanged(nameof(AlbumPhotos));
            if (_albumPhotos.Count > 0)
                SelectedAlbumPhoto = _albumPhotos[0];
            AlbumStatus = photos.Count > 240
                ? $"“{selected.Name}”共 {photos.Count} 张图片；面板先展示前 240 张。"
                : $"“{selected.Name}”已展示 {photos.Count} 张图片。选择照片即可预览并编辑描述。";
        }
        catch (Exception ex)
        {
            AlbumStatus = $"无法读取子相册照片：{ex.Message}";
        }
    }

    private async Task SaveSelectedPhotoDescriptionAsync()
    {
        var selected = SelectedAlbumPhoto;
        if (selected is null)
        {
            AlbumStatus = "请先选择一张照片。";
            return;
        }
        try
        {
            var path = selected.FullPath;
            await _photoAlbums.SavePhotoDescriptionAsync(
                path,
                SelectedPhotoDescription);
            await RebuildExperienceIndexAsync();
            await LoadSelectedAlbumPhotosAsync();
            SelectedAlbumPhoto = _albumPhotos.FirstOrDefault(x =>
                string.Equals(x.FullPath, path, StringComparison.OrdinalIgnoreCase));
            AlbumStatus = string.IsNullOrWhiteSpace(SelectedPhotoDescription)
                ? $"已清除“{selected.FileName}”的描述；原图未改动。"
                : $"已保存“{selected.FileName}”的描述，可用于后续检索和对话回忆。";
        }
        catch (Exception ex)
        {
            AlbumStatus = $"无法保存照片描述：{ex.Message}";
        }
    }

    private async Task SearchAlbumPhotosAsync()
    {
        await EnsureAlbumsLoadedAsync();
        if (AlbumSearchStartDate is { } start &&
            AlbumSearchEndDate is { } end &&
            end.Date < start.Date)
        {
            AlbumStatus = "检索结束日期不能早于开始日期。";
            return;
        }
        try
        {
            var photos = await _photoAlbums.SearchAsync(new PhotoAlbumSearchQuery
            {
                Keyword = AlbumSearchKeyword,
                Theme = AlbumSearchTheme,
                StartDate = AlbumSearchStartDate,
                EndDate = AlbumSearchEndDate,
                AlbumId = SelectedAlbumCard?.AlbumId
            });
            _albumSearchResults.Clear();
            foreach (var photo in photos.Take(300))
                _albumSearchResults.Add(ToPhotoItem(photo, includeThumbnail: false));
            OnPropertyChanged(nameof(AlbumSearchResults));
            AlbumStatus = $"检索到 {photos.Count} 张图片；面板最多展示前 300 条。";
        }
        catch (Exception ex)
        {
            AlbumStatus = $"无法检索相册：{ex.Message}";
        }
    }

    private async Task SaveExperienceSettingsAsync()
    {
        try
        {
            _experienceSettings.Normalize();
            var index = await _albumExperiences.SaveSettingsAsync(
                _experienceSettings,
                _lifetimeCancellation.Token);
            ApplyExperienceIndex(index);
            ExperienceIndexStatus =
                $"相册记忆设置已保存。{index.BuildStatus.Message}";
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch
        {
            ExperienceIndexStatus = "相册记忆设置未能保存；原相册索引没有改动。";
        }
    }

    private async Task RebuildExperienceIndexAsync()
    {
        try
        {
            _experienceSettings.Normalize();
            await _albumExperiences.SaveSettingsAsync(
                _experienceSettings,
                _lifetimeCancellation.Token);
            var catalog = await _photoAlbums.LoadAsync();
            if (string.IsNullOrWhiteSpace(catalog.RootDirectory))
            {
                ExperienceIndexStatus = "请先选择相册根目录，再重建经历索引。";
                return;
            }
            ExperienceIndexStatus = "正在后台重建经历索引；旧扫描结果不会覆盖新扫描。";
            var index = await _albumExperiences.RebuildAsync(
                catalog,
                _lifetimeCancellation.Token);
            ApplyExperienceIndex(index);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch (OperationCanceledException)
        {
            ExperienceIndexStatus = "本次扫描已取消；较新的重建请求正在接管。";
        }
        catch
        {
            ExperienceIndexStatus = "经历索引重建失败；原相册、旧 albums.json 和原图均未改动。";
        }
    }

    private async Task SearchExperiencesFromPanelAsync()
    {
        try
        {
            var catalog = await _photoAlbums.LoadAsync();
            var matches = await _albumExperiences.SearchAsync(
                catalog,
                new AlbumExperienceSearchQuery
                {
                    Text = AlbumSearchKeyword,
                    StartDate = AlbumSearchStartDate,
                    EndDate = AlbumSearchEndDate,
                    Theme = AlbumSearchTheme,
                    MaximumResults = ExperienceMaximumResults
                },
                _lifetimeCancellation.Token);
            UpdateRecentExperienceMatches(
                string.IsNullOrWhiteSpace(AlbumSearchKeyword)
                    ? "面板条件检索"
                    : AlbumSearchKeyword,
                matches);
            ExperienceIndexStatus = $"面板检索命中 {matches.Count} 条经历。";
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch
        {
            ExperienceIndexStatus = "经历检索失败；照片浏览仍可继续使用。";
        }
    }

    private async Task RestoreConversationHistoryAsync()
    {
        try
        {
            var history = await _conversationSession.LoadAsync(
                _modelApiSettings.ConversationTurns,
                _lifetimeCancellation.Token);
            ChatMessages.Clear();
            foreach (var message in history)
            {
                ChatMessages.Add(new ChatMessage
                {
                    Role = message.Role,
                    Text = message.Text,
                    At = message.At
                });
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch
        {
            // Conversation history is helpful but must never block the pet from
            // starting. The next successful exchange will retry the atomic save.
            ChatMessages.Clear();
        }
    }

    private async Task PersistConversationExchangeAsync(string ownerText, string petText)
    {
        try
        {
            await _conversationSession.AppendExchangeAsync(
                ownerText,
                petText,
                _modelApiSettings.ConversationTurns,
                _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch
        {
            ModelApiStatus = $"{ModelApiStatus} 本轮对话仍已显示，但短期会话未能写入本地。";
        }
    }

    private async Task<AlbumConversationMemory> BuildAlbumConversationMemoryAsync(
        string ownerMessage,
        bool includeLlmPayload,
        CancellationToken cancellationToken)
    {
        try
        {
            var catalog = await _photoAlbums.LoadAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var index = await _albumExperiences.EnsureFreshAsync(
                catalog,
                cancellationToken);
            _experienceSettings = index.Settings;
            if (!index.Settings.AllowConversation)
            {
                if (AlbumExperienceService.LooksLikeExperienceQuery(ownerMessage))
                {
                    UpdateRecentExperienceMatches(
                        ownerMessage,
                        Array.Empty<AlbumExperienceSearchResult>());
                    LastExperienceLlmCount = 0;
                    LastExperienceImageCount = 0;
                    OnPropertyChanged(nameof(ExperienceDebugStatus));
                }
                return AlbumConversationMemory.Empty;
            }

            var hasExplicitCue = AlbumExperienceService.LooksLikeExperienceQuery(ownerMessage);
            var hasIndexedMetadataCue = index.Records.Any(record =>
                record.Tags
                    .Concat(new[]
                    {
                        record.AlbumName,
                        record.Theme,
                        record.GrowthStage,
                        record.Mood
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Any(x => ownerMessage.Contains(
                        x,
                        StringComparison.CurrentCultureIgnoreCase)));
            if (!hasExplicitCue && !hasIndexedMetadataCue)
                return AlbumConversationMemory.Empty;

            var (startDate, endDate) = ParseAlbumDateRange(
                ownerMessage,
                _clock.Now.LocalDateTime.Date);
            var matches = await _albumExperiences.SearchLoadedAsync(
                index,
                new AlbumExperienceSearchQuery
                {
                    Text = ownerMessage,
                    StartDate = startDate,
                    EndDate = endDate,
                    MaximumResults = index.Settings.MaximumResults
                },
                cancellationToken);
            UpdateRecentExperienceMatches(ownerMessage, matches);
            if (matches.Count == 0)
            {
                LastExperienceLlmCount = 0;
                LastExperienceImageCount = 0;
                OnPropertyChanged(nameof(ExperienceDebugStatus));
                return new AlbumConversationMemory(
                    string.Empty,
                    Array.Empty<ModelImageInput>(),
                    matches,
                    0);
            }

            var llmMatches = includeLlmPayload
                ? matches
                .Where(x => x.Record.AllowLlm && x.Record.IncludeInConversation)
                .Take(Math.Clamp(index.Settings.MaximumResults, 1, 3))
                .ToList()
                : new List<AlbumExperienceSearchResult>();
            var context = AlbumExperienceService.BuildLlmContext(
                llmMatches,
                index.Settings.MaximumResults);
            var imagePaths =
                includeLlmPayload &&
                index.Settings.AllowSendImagesToLlm &&
                _modelApiSettings.VisionEnabled &&
                _modelApiSettings.SendAlbumImages
                    ? AlbumExperienceService.ResolveAuthorizedImagePaths(
                        catalog.RootDirectory,
                        llmMatches,
                        index.Settings.MaximumImages)
                    : Array.Empty<string>();
            var images = await BuildAlbumImageInputsAsync(
                imagePaths,
                cancellationToken);
            LastExperienceLlmCount = llmMatches.Count;
            LastExperienceImageCount = images.Count;
            OnPropertyChanged(nameof(ExperienceDebugStatus));
            return new AlbumConversationMemory(
                context,
                images,
                matches,
                llmMatches.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // A missing/removable photo folder must not break ordinary chat, and
            // exception text can contain a private local path, so it is not
            // forwarded to either the model or the user-facing bubble.
            return AlbumConversationMemory.Empty;
        }
    }

    private static async Task<IReadOnlyList<ModelImageInput>> BuildAlbumImageInputsAsync(
        IEnumerable<string> imagePaths,
        CancellationToken cancellationToken)
    {
        var result = new List<ModelImageInput>(2);
        foreach (var path in imagePaths)
        {
            if (result.Count >= 2) break;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var fullPath = Path.GetFullPath(path);
                var extension = Path.GetExtension(fullPath);
                if (!VisionImageMimeTypes.TryGetValue(extension, out var mimeType))
                    continue;
                var file = new FileInfo(fullPath);
                if (!file.Exists ||
                    file.Length <= 0 ||
                    file.Length > MaximumAlbumImageBytes)
                    continue;
                var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
                result.Add(new ModelImageInput
                {
                    DataUrl = $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}",
                    Detail = "low"
                });
            }
            catch (IOException)
            {
                // Removable and concurrently edited album files are skipped.
            }
            catch (UnauthorizedAccessException)
            {
                // An unreadable photo is not permission to widen access.
            }
        }
        return result;
    }

    private void UpdateRecentExperienceMatches(
        string query,
        IReadOnlyList<AlbumExperienceSearchResult> matches)
    {
        LastExperienceQuery = SafeAlbumMetadata(query, 120);
        LastExperienceHitCount = matches.Count;
        _recentExperienceMatches.Clear();
        foreach (var match in matches)
            _recentExperienceMatches.Add(ToExperienceItem(match.Record));
        SelectedExperience = _recentExperienceMatches.FirstOrDefault();
        OnPropertyChanged(nameof(RecentExperienceMatches));
        OnPropertyChanged(nameof(ExperienceDebugStatus));
    }

    private static AlbumExperienceItem ToExperienceItem(
        AlbumExperienceRecord record)
    {
        var date = record.Date?.ToString("yyyy-MM-dd") ?? "日期未设";
        var mood = string.IsNullOrWhiteSpace(record.Mood)
            ? "情绪未设"
            : $"情绪 {record.Mood}";
        var behavior = string.IsNullOrWhiteSpace(record.BehaviorId)
            ? "无行为关联"
            : $"行为 {record.BehaviorId}";
        return new AlbumExperienceItem
        {
            Record = record,
            Title = record.Title,
            Summary = record.Summary,
            Metadata = $"{date} · {mood} · {behavior} · {record.SourceType}",
            Tags = record.Tags.Count == 0
                ? "无标签"
                : string.Join("、", record.Tags),
            Images = record.ImageRelativePaths.Count == 0
                ? "无关联图片"
                : string.Join("、", record.ImageRelativePaths),
            Permissions =
                $"对话 {(record.IncludeInConversation ? "允许" : "关闭")} · " +
                $"行为 {(record.IncludeInBehaviorDecision ? "允许" : "关闭")} · " +
                $"LLM {(record.AllowLlm ? "允许" : "关闭")} · " +
                $"规则 {(record.AllowRules ? "允许" : "关闭")}"
        };
    }

    private async Task TryApplyExperienceBehaviorSuggestionAsync(
        AlbumExperienceRecord? record)
    {
        if (record is null ||
            !_experienceSettings.AllowBehaviorDecision ||
            !record.IncludeInBehaviorDecision)
        {
            LastExperienceBehaviorSuggestion = "本次经历未生成行为建议";
            OnPropertyChanged(nameof(ExperienceDebugStatus));
            return;
        }

        var suggestion = ExperienceBehaviorSuggestion(record);
        if (suggestion.Length == 0)
        {
            LastExperienceBehaviorSuggestion = "经历没有可映射的轻行为";
            OnPropertyChanged(nameof(ExperienceDebugStatus));
            return;
        }

        var agentResult = _agentKernel.Handle(
            new PetAgentEvent
            {
                Kind = PetAgentEventKind.AlbumExperienceHit,
                At = _clock.Now,
                Text = record.Summary,
                BehaviorHint = suggestion
            },
            new PetAgentContext
            {
                CurrentStateSummary = RuntimeStateSummary,
                Temperament = _memory.Personality.Temperament.Clone(),
                RelationshipSummary = RelationshipStateSummary,
                RecentConversation = ChatMessages
                    .TakeLast(4)
                    .Select(message => message.Text)
                    .ToList(),
                LongTermMemorySummaries = _memory.Summary.Highlights.Take(3).ToList(),
                AlbumExperienceSummaries = new[] { record.Summary },
                CurrentBehaviorId = _currentBehaviorKey,
                ArbitrationSummary = LastArbitrationResult
            });
        var proposal = agentResult.BehaviorProposals.FirstOrDefault();
        if (proposal is null)
        {
            LastExperienceBehaviorSuggestion = "PetAgent 未生成本地可执行提案";
            OnPropertyChanged(nameof(ExperienceDebugStatus));
            return;
        }
        var result = await SubmitBehaviorProposalAsync(proposal);
        LastExperienceBehaviorSuggestion = result?.State switch
        {
            BehaviorProposalState.Completed =>
                $"经历建议已由 PetAgent 验证、仲裁并执行：{suggestion}",
            BehaviorProposalState.Deferred =>
                $"经历建议进入等待队列：{suggestion} · {result.Explanation}",
            _ => $"经历建议未执行：{suggestion} · {result?.Explanation ?? "无结果"}"
        };
        OnPropertyChanged(nameof(ExperienceDebugStatus));
    }

    private static string ExperienceBehaviorSuggestion(
        AlbumExperienceRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.BehaviorId))
        {
            var configured = record.BehaviorId.Trim();
            if (configured is
                "celebrate.idle" or
                "play.wand" or
                "feed.snack" or
                "rest.window" or
                "rest.near_owner")
                return configured;
        }
        var searchable = string.Join(
            " ",
            record.Mood,
            record.Title,
            record.Summary,
            string.Join(" ", record.Tags));
        if (searchable.Contains("happy", StringComparison.OrdinalIgnoreCase) ||
            searchable.Contains("开心", StringComparison.CurrentCulture) ||
            searchable.Contains("庆祝", StringComparison.CurrentCulture) ||
            searchable.Contains("生日", StringComparison.CurrentCulture))
            return "celebrate.idle";
        if (searchable.Contains("play", StringComparison.OrdinalIgnoreCase) ||
            searchable.Contains("玩", StringComparison.CurrentCulture))
            return "play.wand";
        if (searchable.Contains("feed", StringComparison.OrdinalIgnoreCase) ||
            searchable.Contains("吃", StringComparison.CurrentCulture) ||
            searchable.Contains("零食", StringComparison.CurrentCulture))
            return "feed.snack";
        if (searchable.Contains("rest", StringComparison.OrdinalIgnoreCase) ||
            searchable.Contains("window", StringComparison.OrdinalIgnoreCase) ||
            searchable.Contains("休息", StringComparison.CurrentCulture) ||
            searchable.Contains("窗", StringComparison.CurrentCulture) ||
            searchable.Contains("睡", StringComparison.CurrentCulture))
            return "rest.window";
        return string.Empty;
    }

    private async Task TryIndexTravelExperienceAsync(
        string destination,
        string story,
        DateTimeOffset at,
        bool recalled)
    {
        try
        {
            await _albumExperiences.AddTravelExperienceAsync(
                destination,
                story,
                at,
                recalled,
                _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch
        {
            ExperienceIndexStatus =
                "旅行已经正常返回，但这次旅行经历未能追加到相册经历索引。";
        }
    }

    private static bool LooksLikeAlbumConversation(string text) =>
        AlbumConversationCues.Any(
            cue => text.Contains(cue, StringComparison.CurrentCultureIgnoreCase));

    private static bool IsGenericAlbumRequest(string text)
    {
        var cleaned = text;
        foreach (var noise in AlbumKeywordNoise)
            cleaned = cleaned.Replace(
                noise,
                string.Empty,
                StringComparison.CurrentCultureIgnoreCase);
        cleaned = Regex.Replace(cleaned, @"[\p{P}\p{S}\s\d年月日./-]+", string.Empty);
        return cleaned.Length < 2;
    }

    private static IEnumerable<string> ExtractAlbumKeywords(string text)
    {
        foreach (Match quote in Regex.Matches(text, "[“\"](?<value>[^”\"]{2,32})[”\"]"))
            yield return quote.Groups["value"].Value.Trim();

        var cleaned = Regex.Replace(
            text,
            @"(?:19|20)\d{2}(?:[年./-]\d{1,2})?(?:[月./-]\d{1,2})?日?",
            " ");
        foreach (var noise in AlbumKeywordNoise)
            cleaned = cleaned.Replace(
                noise,
                " ",
                StringComparison.CurrentCultureIgnoreCase);
        foreach (var part in Regex.Split(cleaned, @"[\p{P}\p{S}\s]+"))
        {
            var keyword = part.Trim();
            if (keyword.Length is >= 2 and <= 32)
                yield return keyword;
        }
    }

    private static (DateTime? Start, DateTime? End) ParseAlbumDateRange(
        string text,
        DateTime today)
    {
        if (text.Contains("今天", StringComparison.CurrentCulture))
            return (today, today);
        if (text.Contains("昨天", StringComparison.CurrentCulture))
        {
            var yesterday = today.AddDays(-1);
            return (yesterday, yesterday);
        }
        if (text.Contains("去年", StringComparison.CurrentCulture))
            return (new DateTime(today.Year - 1, 1, 1), new DateTime(today.Year - 1, 12, 31));
        if (text.Contains("今年", StringComparison.CurrentCulture))
            return (new DateTime(today.Year, 1, 1), new DateTime(today.Year, 12, 31));

        var full = Regex.Match(
            text,
            @"(?<year>(?:19|20)\d{2})[年./-](?<month>\d{1,2})(?:[月./-](?<day>\d{1,2})日?)?");
        if (full.Success &&
            int.TryParse(full.Groups["year"].Value, out var year) &&
            int.TryParse(full.Groups["month"].Value, out var month) &&
            month is >= 1 and <= 12)
        {
            if (int.TryParse(full.Groups["day"].Value, out var day) &&
                day >= 1 &&
                day <= DateTime.DaysInMonth(year, month))
            {
                var date = new DateTime(year, month, day);
                return (date, date);
            }
            return (
                new DateTime(year, month, 1),
                new DateTime(year, month, DateTime.DaysInMonth(year, month)));
        }

        var yearOnly = Regex.Match(text, @"(?<year>(?:19|20)\d{2})年");
        if (yearOnly.Success &&
            int.TryParse(yearOnly.Groups["year"].Value, out year))
            return (new DateTime(year, 1, 1), new DateTime(year, 12, 31));
        return (null, null);
    }

    private static int AlbumRelevanceScore(AlbumPhotoReference photo, string message)
    {
        var score = 0;
        if (message.Contains(photo.AlbumName, StringComparison.CurrentCultureIgnoreCase))
            score += 8;
        if (!string.IsNullOrWhiteSpace(photo.Theme) &&
            message.Contains(photo.Theme, StringComparison.CurrentCultureIgnoreCase))
            score += 6;
        if (!string.IsNullOrWhiteSpace(photo.GrowthStage) &&
            message.Contains(photo.GrowthStage, StringComparison.CurrentCultureIgnoreCase))
            score += 5;
        if (!string.IsNullOrWhiteSpace(photo.Description) &&
            message.Contains(photo.Description, StringComparison.CurrentCultureIgnoreCase))
            score += 7;
        if (message.Contains(
                Path.GetFileNameWithoutExtension(photo.FileName),
                StringComparison.CurrentCultureIgnoreCase))
            score += 3;
        return score;
    }

    private static string SafeAlbumMetadata(string value, int maximumLength)
    {
        var oneLine = string.Join(
            ' ',
            (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        oneLine = oneLine.Replace("\r", " ").Replace("\n", " ");
        return oneLine.Length <= maximumLength
            ? oneLine
            : oneLine[..maximumLength] + "…";
    }

    private async Task SaveRelationshipStageAsync()
    {
        var value = SelectedRelationshipStage == AutomaticRelationshipStage
            ? string.Empty
            : SelectedRelationshipStage;
        try
        {
            await _photoAlbums.SaveRelationshipStageOverrideAsync(value);
            ProfilePresentationStatus = value.Length == 0
                ? $"已恢复自动阶段：{CalculateAutomaticRelationshipStage()}。"
                : $"已将档案展示阶段设为“{value}”；底层关系数值仍会自然变化。";
            OnPropertyChanged(nameof(RelationshipStageDisplay));
        }
        catch (Exception ex)
        {
            ProfilePresentationStatus = $"无法保存好感阶段展示：{ex.Message}";
        }
    }

    private string CalculateAutomaticRelationshipStage()
    {
        var relationship = _memory.Personality.Relationship;
        var score = relationship.Trust * 0.42 +
                    relationship.Familiarity * 0.25 +
                    relationship.TouchAcceptance * 0.18 +
                    relationship.InitiativeAcceptance * 0.15;
        return score switch
        {
            < 0.35 => "观察中",
            < 0.55 => "熟悉",
            < 0.75 => "亲近",
            _ => "家人"
        };
    }

    private static string FriendlyHabitLabel(string behaviorId) =>
        behaviorId switch
        {
            var value when value.StartsWith("play.", StringComparison.Ordinal) => "主动玩耍",
            var value when value.StartsWith("explore.", StringComparison.Ordinal) => "好奇探索",
            var value when value.StartsWith("social.", StringComparison.Ordinal) => "主动亲近",
            var value when value.StartsWith("rest.sleep", StringComparison.Ordinal) => "安心睡在主人附近",
            var value when value.Contains("groom", StringComparison.Ordinal) => "自己梳理毛发",
            var value when value.StartsWith("magic.", StringComparison.Ordinal) => "偶尔施展魔法",
            var value when value.StartsWith("environment.", StringComparison.Ordinal) => "观察桌面环境",
            _ => string.Empty
        };

    private PhotoAlbumCardItem ToCard(PhotoAlbumSnapshot snapshot)
    {
        var range = snapshot.StartDate is null && snapshot.EndDate is null
            ? string.Empty
            : $"{snapshot.StartDate?.ToString("yyyy-MM-dd") ?? "未设"}—{snapshot.EndDate?.ToString("yyyy-MM-dd") ?? "未设"}";
        var metadata = new[]
            {
                snapshot.IsRoot
                    ? "本地根目录"
                    : snapshot.IsDiscovered
                        ? $"自动发现 · 目录 {snapshot.RelativeDirectory}"
                        : $"目录 {snapshot.RelativeDirectory}",
                snapshot.Theme.Length > 0 ? $"主题 {snapshot.Theme}" : string.Empty,
                snapshot.GrowthStage.Length > 0 ? $"成长阶段 {snapshot.GrowthStage}" : string.Empty,
                range
            }
            .Where(x => x.Length > 0);
        return new PhotoAlbumCardItem
        {
            AlbumId = snapshot.AlbumId,
            IsRoot = snapshot.IsRoot,
            IsDiscovered = snapshot.IsDiscovered,
            Name = snapshot.Name,
            DirectoryPath = snapshot.DirectoryPath,
            Metadata = string.Join(" · ", metadata),
            Availability = snapshot.IsAvailable
                ? $"{snapshot.PhotoCount} 张图片"
                : "目录暂时失联 · 元数据已保留",
            PhotoCount = snapshot.PhotoCount,
            CoverPath = snapshot.CoverPath ?? string.Empty,
            CoverImage = LoadThumbnail(snapshot.CoverPath, 260)
        };
    }

    private AlbumPhotoItem ToPhotoItem(
        AlbumPhotoReference photo,
        bool includeThumbnail)
    {
        var tags = new[] { photo.Theme, photo.GrowthStage }
            .Where(x => !string.IsNullOrWhiteSpace(x));
        return new AlbumPhotoItem
        {
            FileName = photo.FileName,
            FullPath = photo.FullPath,
            AlbumName = photo.AlbumName,
            Metadata = $"{photo.CapturedAt:yyyy-MM-dd} · {string.Join(" · ", tags)}".TrimEnd(' ', '·'),
            Description = photo.Description,
            Thumbnail = includeThumbnail ? LoadThumbnail(photo.FullPath, 220) : null
        };
    }

    private object? LoadThumbnail(string? path, int decodePixelWidth)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        string cacheKey;
        try
        {
            cacheKey = $"{Path.GetFullPath(path)}|{File.GetLastWriteTimeUtc(path).Ticks}|{decodePixelWidth}";
        }
        catch
        {
            return null;
        }
        if (ThumbnailCache.TryGetValue(cacheKey, out var cached))
            return cached;
        var image = _presentationHost.LoadImage(
            path,
            Math.Clamp(decodePixelWidth, 96, 1400));
        if (image is not null) ThumbnailCache[cacheKey] = image;
        return image;
    }
}
