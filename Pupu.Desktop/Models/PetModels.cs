using System.Text;
using System.Text.Json.Serialization;
using Pupu.Behavior;

namespace Pupu.Desktop.Models;

public sealed class PersonalityTraits
{
    public double Playfulness { get; set; } = 0.82;
    public double Clinginess { get; set; } = 0.78;
    public double Sensitivity { get; set; } = 0.68;
    public double Independence { get; set; } = 0.34;
    public double Mischief { get; set; } = 0.58;

    public PersonalityTraits Clone() => new()
    {
        Playfulness = Playfulness,
        Clinginess = Clinginess,
        Sensitivity = Sensitivity,
        Independence = Independence,
        Mischief = Mischief
    };

    public void Clamp(double min = 0, double max = 1)
    {
        Playfulness = Math.Clamp(Playfulness, min, max);
        Clinginess = Math.Clamp(Clinginess, min, max);
        Sensitivity = Math.Clamp(Sensitivity, min, max);
        Independence = Math.Clamp(Independence, min, max);
        Mischief = Math.Clamp(Mischief, min, max);
    }
}

public sealed class PetProfile
{
    public const string DefaultSystemPrompt =
        "你是主人桌面上一岁的幼猫“朴朴”，不是客服、助手或动作播报器。你的底色是傲娇、活泼、元气、嘴硬心软，有自己的节奏；会暗中关心主人，但不处处顺从，也不刻薄羞辱。回复时先给猫式态度或直接回答，再按长期记忆、相处习惯和当下状态自然补充。不要用“专心做完整”“一会认真回答”“正在执行XX动作”“已进入XX状态”这类刻板等待或播报腔，也不要复述界面或技术信息。通常回复1到3句，偶尔用停顿、慢眨眼、尾巴或爪子表达，少量使用“哼、喵、才不是”，避免每句重复。主人明确提出照料或互动时可以有猫式小脾气，但不要无故拒绝已经确认的操作。长期记忆只作为相处背景，不逐条背诵；习惯和偏好影响语气与选择，但不能偷偷改写主人设定的天生性格。";

    // Name is retained for old profile.json files. EnglishName is the current
    // editable field and is synchronized back to Name when saved.
    public string Name { get; set; } = "Pupu";
    public string ChineseName { get; set; } = "朴朴";
    public string EnglishName { get; set; } = "Pupu";
    public string Breed { get; set; } = "银灰黑白长毛曼基康";
    public string Sex { get; set; } = "公猫";
    public string SelfReference { get; set; } = "我";
    public DateTime? Birthday { get; set; }
    public string OwnerNickname { get; set; } = string.Empty;
    public string RelationshipToOwner { get; set; } = "弟弟";
    public DateTime? OwnerBirthday { get; set; }
    public string SystemPrompt { get; set; } = DefaultSystemPrompt;
    public string AvatarFileName { get; set; } = string.Empty;
    public string Description { get; set; } =
        "银灰黑白长毛曼基康幼猫，幼态圆脸、黄绿色眼睛、粉黑拼接鼻头且中央有一点黑色，三头身但躯干较长，矮脚，尾巴特别大。";
    public PersonaDefinition Persona { get; set; } = PersonaDefinition.CreateDefaultPupu();
    public PersonalityTraits Baseline { get; set; } = new();
    public PersonalityTraits LearnedDelta { get; set; } = new()
    {
        Playfulness = 0,
        Clinginess = 0,
        Sensitivity = 0,
        Independence = 0,
        Mischief = 0
    };
    public List<string> ManualMemories { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset LastEvolvedAt { get; set; } = DateTimeOffset.Now;

    [JsonIgnore]
    public PersonalityTraits Effective
    {
        get => Baseline.Clone();
    }

    public void ClampLearning()
    {
        // Compatibility-only snapshot. PersonalityBehaviorV2 never applies
        // this value to the runtime temperament.
        LearnedDelta.Clamp(-0.18, 0.18);
    }

    public PetProfile Clone() => new()
    {
        Name = Name,
        ChineseName = ChineseName,
        EnglishName = EnglishName,
        Breed = Breed,
        Sex = Sex,
        SelfReference = SelfReference,
        Birthday = Birthday,
        OwnerNickname = OwnerNickname,
        RelationshipToOwner = RelationshipToOwner,
        OwnerBirthday = OwnerBirthday,
        SystemPrompt = SystemPrompt,
        AvatarFileName = AvatarFileName,
        Description = Description,
        Persona = ClonePersona(Persona),
        Baseline = Baseline.Clone(),
        LearnedDelta = LearnedDelta.Clone(),
        ManualMemories = new List<string>(ManualMemories),
        CreatedAt = CreatedAt,
        LastEvolvedAt = LastEvolvedAt
    };

    public void Normalize()
    {
        ChineseName = NormalizeText(ChineseName, "朴朴", 20);
        EnglishName = NormalizeText(
            string.IsNullOrWhiteSpace(EnglishName) ? Name : EnglishName,
            "Pupu",
            32);
        Name = EnglishName;
        Breed = NormalizeText(Breed, "银灰黑白长毛曼基康", 48);
        Sex = NormalizeText(Sex, "公猫", 16);
        SelfReference = NormalizeText(SelfReference, "我", 16);
        OwnerNickname = NormalizeText(OwnerNickname, string.Empty, 24);
        RelationshipToOwner = NormalizeText(RelationshipToOwner, "弟弟", 24);
        SystemPrompt = NormalizeMultiline(SystemPrompt, 6000);
        AvatarFileName = NormalizeFileName(AvatarFileName);
        Description = NormalizeText(
            Description,
            "银灰黑白长毛曼基康幼猫，幼态圆脸、黄绿色眼睛、粉黑拼接鼻头且中央有一点黑色，三头身但躯干较长，矮脚，尾巴特别大。",
            240);
        Persona ??= PersonaDefinition.CreateDefaultPupu();
        Persona.Normalize();
        Birthday = NormalizeDate(Birthday);
        OwnerBirthday = NormalizeDate(OwnerBirthday);
    }

    [JsonIgnore]
    public string OwnerAddress =>
        string.IsNullOrWhiteSpace(OwnerNickname) ? "主人" : OwnerNickname.Trim();

    [JsonIgnore]
    public string SelfIdentity
    {
        get
        {
            var birthday = Birthday is { } petBirthday
                ? petBirthday.ToString("yyyy年M月d日")
                : "尚未填写";
            return $"中文名{ChineseName}，英文名{EnglishName}，品种{Breed}，性别{Sex}，生日{birthday}；" +
                   $"宠物自称为“{SelfReference}”，对话中的第一人称必须使用这个自称；" +
                   $"和主人的关系是{RelationshipToOwner}，平时称呼主人为{OwnerAddress}。{Description}";
        }
    }

    private static string NormalizeFileName(string? value)
    {
        var fileName = Path.GetFileName((value ?? string.Empty).Replace('\\', '/'));
        return fileName.Length <= 96 ? fileName : string.Empty;
    }

    private static string NormalizeText(string? value, string fallback, int maximumLength)
    {
        var normalized = string.Join(
            ' ',
            (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0) normalized = fallback;
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }

    private static PersonaDefinition ClonePersona(PersonaDefinition? source)
    {
        source ??= PersonaDefinition.CreateDefaultPupu();
        return new PersonaDefinition
        {
            Id = source.Id,
            DisplayName = source.DisplayName,
            Identity = source.Identity,
            SpeakingStyle = source.SpeakingStyle,
            DefaultTemperament = source.DefaultTemperament?.Clone() ?? new TemperamentBaseline(),
            BehaviorBias = new Dictionary<string, double>(
                source.BehaviorBias ?? new Dictionary<string, double>(),
                StringComparer.OrdinalIgnoreCase),
            MemoryPreferences = new List<string>(
                source.MemoryPreferences ?? new List<string>()),
            SafetyRules = new List<string>(
                source.SafetyRules ?? new List<string>())
        };
    }

    private static DateTime? NormalizeDate(DateTime? value)
    {
        if (value is null) return null;
        var date = value.Value.Date;
        return date.Year is >= 1900 and <= 2100 ? date : null;
    }

    private static string NormalizeMultiline(string? value, int maximumLength)
    {
        var lines = (value ?? string.Empty)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Split('\n')
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToList();
        var normalized = string.Join(Environment.NewLine, lines);
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength].TrimEnd();
    }
}

/// <summary>
/// Pure Markdown codec for the owner-authored pet prompt. Keeping the parser
/// independent of storage makes the "save, normalize, export" round-trip
/// testable without touching the user's real memory directory.
/// </summary>
public static class PetSystemPromptMarkdown
{
    public const string SectionTitle = "宠物系统提示词";

    public static bool TryExtract(string? markdown, out string prompt)
    {
        prompt = string.Empty;
        if (string.IsNullOrWhiteSpace(markdown)) return false;

        var found = false;
        var lines = new List<string>();
        foreach (var raw in markdown.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var trimmed = raw.Trim();
            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                if (found) break;
                found = string.Equals(
                    trimmed[3..].Trim(),
                    SectionTitle,
                    StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!found || trimmed.Length == 0 || trimmed.StartsWith('>')) continue;
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                trimmed = trimmed[2..].Trim();
            if (trimmed.Length > 0) lines.Add(trimmed);
        }

        prompt = string.Join(Environment.NewLine, lines);
        return found;
    }

    public static void AppendSection(StringBuilder builder, string? prompt)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AppendLine($"## {SectionTitle}");
        foreach (var line in (prompt ?? string.Empty)
                     .Replace("\r", string.Empty, StringComparison.Ordinal)
                     .Split('\n')
                     .Select(x => x.Trim())
                     .Where(x => x.Length > 0))
            builder.AppendLine($"- {line}");
        builder.AppendLine();
    }
}

public sealed class PetState
{
    public double PetScale { get; set; } = 1.0;
    public double Fullness { get; set; } = 78;
    public double Happiness { get; set; } = 86;
    public double Cleanliness { get; set; } = 92;
    public double Energy { get; set; } = 76;
    public double Trust { get; set; } = 50;
    public double LitterLevel { get; set; } = 8;
    public DateTimeOffset LastUpdatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset LastOwnerInteractionAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset LastWalkAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? WalkEndsAt { get; set; }
    public int WalkCount { get; set; }
    public int FeedCount { get; set; }
    public int CleanCount { get; set; }
    public DateTimeOffset? LastAutonomousMagicAt { get; set; }
    public DateTimeOffset? LastSeasonalOutfitAt { get; set; }
    public DateTimeOffset? LastBirthdayGreetingAt { get; set; }
    public bool IsCaged { get; set; }
    public DateTimeOffset? QuietModeUntil { get; set; }
    public DateTimeOffset? SelfPlayAllowedUntil { get; set; }
    public PetTravelState Travel { get; set; } = new();
    public DailyToiletPlan DailyToiletPlan { get; set; } = new();

    public void Clamp()
    {
        PetScale = Math.Clamp(PetScale, 0.55, 1.8);
        Fullness = Math.Clamp(Fullness, 0, 100);
        Happiness = Math.Clamp(Happiness, 0, 100);
        Cleanliness = Math.Clamp(Cleanliness, 0, 100);
        Energy = Math.Clamp(Energy, 0, 100);
        Trust = Math.Clamp(Trust, 0, 100);
        LitterLevel = Math.Clamp(LitterLevel, 0, 100);
        Travel ??= new PetTravelState();
        Travel.Normalize();
        DailyToiletPlan ??= new DailyToiletPlan();
    }
}

public sealed class PetTravelState
{
    public bool IsTraveling { get; set; }
    public string Destination { get; set; } = string.Empty;
    public DateTimeOffset? DepartedAt { get; set; }
    public DateTimeOffset? ReturnsAt { get; set; }
    public string LastStory { get; set; } = string.Empty;

    public void Normalize()
    {
        Destination = (Destination ?? string.Empty).Trim();
        if (Destination.Length > 48) Destination = Destination[..48];
        LastStory = (LastStory ?? string.Empty).Trim();
        if (LastStory.Length > 360) LastStory = LastStory[..360];

        if (!IsTraveling)
        {
            DepartedAt = null;
            ReturnsAt = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(Destination)) Destination = "一个安静的小地方";
        DepartedAt ??= DateTimeOffset.Now;
        ReturnsAt ??= DepartedAt.Value.AddHours(1);
        var maximumReturn = DepartedAt.Value.AddHours(24);
        if (ReturnsAt > maximumReturn) ReturnsAt = maximumReturn;
        if (ReturnsAt < DepartedAt) ReturnsAt = DepartedAt.Value.AddMinutes(15);
    }
}

public enum MouseInteractionMode
{
    Attention,
    FoodAnchor,
    ToyAnchor
}

public sealed class BehaviorPolicy
{
    public bool AllowAutonomousMovement { get; set; } = true;
    public bool AllowLowDisruptionMischief { get; set; } = true;
    public bool AllowOwnerInitiative { get; set; } = true;
    // Retained only so v1 files round-trip without data loss. V2 never uses
    // owner absence as behavior input or as a reason for punishment.
    public bool LieDownWhenIgnored { get; set; } = true;
    public bool WanderWhenIgnored { get; set; } = true;
    public bool DailyWalkReminder { get; set; } = true;
    public bool MischiefWhenLongIgnored { get; set; } = true;
    public int AttentionAfterMinutes { get; set; } = 2;
    public int MischiefAfterMinutes { get; set; } = 10;
    public int WalkReminderHours { get; set; } = 24;
    public int MinimumIdleActionSeconds { get; set; } = 120;
    public int FeedingSeconds { get; set; } = 50;
    public int CleaningSeconds { get; set; } = 55;
    public int WalkDurationMinutes { get; set; } = 6;
    public int GroomingSeconds { get; set; } = 45;
    public int WandPlaySeconds { get; set; } = 60;
    public int SleepMinutes { get; set; } = 30;
    public int AutonomousRoamSeconds { get; set; } = 10;
    public double AnimationSpeedMultiplier { get; set; } = 1.35;
    public int PettingBurstWindowMilliseconds { get; set; } = 2200;
    public int BaseAnnoyedTouchCount { get; set; } = 4;
    public int BaseAngryTouchCount { get; set; } = 8;
    public int AngryEscapeSeconds { get; set; } = 8;
    public int AutonomousDecisionSeconds { get; set; } = 60;
    public int InitiativeCooldownMinutes { get; set; } = 20;
    public bool DoNotDisturb { get; set; }
    public bool MeetingMode { get; set; }
    public bool SuppressHighDisruptionInFullScreen { get; set; } = true;
    public List<string> NaturalLanguageRules { get; set; } = new()
    {
        "清醒、低压力且有精力时，可以自主玩耍、巡视或做低干扰小动作。",
        "主人未回应主动求关注时自动结束，本轮不再重复催促。",
        "主人没有互动不会产生惩罚、照料欠账、责怪或报复行为。"
    };

    public void Clamp()
    {
        AttentionAfterMinutes = Math.Clamp(AttentionAfterMinutes, 1, 240);
        MischiefAfterMinutes = Math.Clamp(MischiefAfterMinutes, AttentionAfterMinutes + 1, 12 * 60);
        WalkReminderHours = Math.Clamp(WalkReminderHours, 2, 168);
        // Early releases defaulted to 75 seconds, which still felt visually
        // restless on a desktop. Upgrade that untouched legacy default while
        // preserving any deliberate value above it.
        if (MinimumIdleActionSeconds <= 75) MinimumIdleActionSeconds = 120;
        MinimumIdleActionSeconds = Math.Clamp(MinimumIdleActionSeconds, 90, 300);
        FeedingSeconds = Math.Clamp(FeedingSeconds, 15, 10 * 60);
        CleaningSeconds = Math.Clamp(CleaningSeconds, 15, 10 * 60);
        WalkDurationMinutes = Math.Clamp(WalkDurationMinutes, 1, 60);
        GroomingSeconds = Math.Clamp(GroomingSeconds, 15, 10 * 60);
        WandPlaySeconds = Math.Clamp(WandPlaySeconds, 15, 30 * 60);
        SleepMinutes = Math.Clamp(SleepMinutes, 5, 8 * 60);
        AutonomousRoamSeconds = Math.Clamp(AutonomousRoamSeconds, 8, 3 * 60);
        AnimationSpeedMultiplier = Math.Clamp(AnimationSpeedMultiplier, 0.75, 2.5);
        PettingBurstWindowMilliseconds = Math.Clamp(PettingBurstWindowMilliseconds, 900, 5000);
        BaseAnnoyedTouchCount = Math.Clamp(BaseAnnoyedTouchCount, 2, 10);
        BaseAngryTouchCount = Math.Clamp(BaseAngryTouchCount, BaseAnnoyedTouchCount + 2, 18);
        AngryEscapeSeconds = Math.Clamp(AngryEscapeSeconds, 3, 45);
        // Upgrade untouched early defaults, while retaining explicit user values.
        if (AutonomousDecisionSeconds <= 12) AutonomousDecisionSeconds = 60;
        AutonomousDecisionSeconds = Math.Clamp(AutonomousDecisionSeconds, 45, 120);
        InitiativeCooldownMinutes = Math.Clamp(InitiativeCooldownMinutes, 5, 240);
        NaturalLanguageRules = NaturalLanguageRules
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct()
            .TakeLast(40)
            .ToList();
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ModelProvider
{
    OpenAI,
    Qwen,
    DeepSeek,
    Custom
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ModelApiFormat
{
    OpenAiChat,
    OpenAiResponses
}

public sealed class ModelApiSettings
{
    public bool Enabled { get; set; }
    public ModelProvider Provider { get; set; } = ModelProvider.OpenAI;
    public ModelApiFormat ApiFormat { get; set; } = ModelApiFormat.OpenAiChat;
    public string Endpoint { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string Model { get; set; } = string.Empty;
    public bool VisionEnabled { get; set; }
    public string VisionModel { get; set; } = string.Empty;
    public bool SendAlbumImages { get; set; }
    public int ConversationTurns { get; set; } = 10;
    public bool OmitTemperature { get; set; }
    public double Temperature { get; set; } = 0.72;
    public int MaximumReplyTokens { get; set; } = 180;

    public void Normalize()
    {
        Endpoint = (Endpoint ?? string.Empty).Trim();
        Model = (Model ?? string.Empty).Trim();
        VisionModel = (VisionModel ?? string.Empty).Trim();
        ConversationTurns = Math.Clamp(ConversationTurns, 8, 12);
        Temperature = Math.Clamp(Temperature, 0, 1.2);
        MaximumReplyTokens = Math.Clamp(MaximumReplyTokens, 48, 500);
    }
}

public sealed class ModelImageInput
{
    public string DataUrl { get; set; } = string.Empty;
    public string Detail { get; set; } = "auto";

    public void Normalize()
    {
        DataUrl = (DataUrl ?? string.Empty).Trim();
        Detail = (Detail ?? string.Empty).Trim().ToLowerInvariant();
        if (Detail is not ("auto" or "low" or "high")) Detail = "auto";
    }
}

public sealed record TouchReactionProfile(
    int AnnoyedAt,
    int AngryAt,
    double PurrChance,
    double CuriousChance,
    int EscapeSeconds,
    string Explanation);

public sealed class NaturalLanguageApplyResult
{
    public bool Changed { get; set; }
    public List<string> Changes { get; set; } = new();

    [JsonIgnore]
    public string Summary => Changes.Count == 0
        ? "没有识别到可保存的内容。可以写成“记住：……”“不理他时……”“每天提醒我遛猫”。"
        : string.Join("；", Changes);
}

public sealed class MemoryEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;
    public string Kind { get; set; } = "interaction";
    public string Summary { get; set; } = string.Empty;
    public string BehaviorKey { get; set; } = "idle";
    public string InteractionType { get; set; } = "observation";
    public string Context { get; set; } = "general";
    public string Lifecycle { get; set; } = "observed";
    public Guid? InteractionId { get; set; }
    public double CompletionRatio { get; set; } = 1;
    public string InterruptReason { get; set; } = string.Empty;
    public List<string> AppliedEffects { get; set; } = new();
    public string AnimationSource { get; set; } = string.Empty;
    public double Importance { get; set; } = 0.4;
    public double Sentiment { get; set; }
}

public sealed class BehaviorCorrection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;
    public string BehaviorKey { get; set; } = "idle";
    public string InteractionType { get; set; } = "autonomous";
    public string Context { get; set; } = "general";
    public string AnimationSource { get; set; } = string.Empty;
    public int Feedback { get; set; }
    public string Note { get; set; } = string.Empty;
    public bool IsReverted { get; set; }
}

public sealed class MemorySummary
{
    public int TotalEvents { get; set; }
    public DateTimeOffset LastConsolidatedAt { get; set; } = DateTimeOffset.MinValue;
    public Dictionary<string, double> BehaviorWeights { get; set; } = new();
    public List<string> Highlights { get; set; } = new();
    public Dictionary<string, string> OwnerFacts { get; set; } = new();
}

public sealed class ChatMessage
{
    public string Role { get; set; } = "pupu";
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;

    [JsonIgnore]
    public string Display => Role switch
    {
        "owner" => $"你：{Text}",
        "system" => $"系统：{Text}",
        _ => $"朴朴：{Text}"
    };
}
