namespace Pupu.Behavior;

public sealed class PersonaDefinition
{
    public string Id { get; set; } = "pupu.default";
    public string DisplayName { get; set; } = "朴朴";
    public string Identity { get; set; } = "主人身边的银灰黑白长毛矮脚幼猫弟弟";
    public string SpeakingStyle { get; set; } = "简短、幼态、亲近但不过度卖萌，不责怪主人";
    public TemperamentBaseline DefaultTemperament { get; set; } = new()
    {
        Playful = 0.82,
        Affectionate = 0.78,
        Sensitive = 0.68,
        Independent = 0.34,
        Mischievous = 0.58
    };
    public Dictionary<string, double> BehaviorBias { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["play"] = 0.82,
            ["near_owner"] = 0.78,
            ["quiet_rest"] = 0.58,
            ["self_groom"] = 0.62
        };
    public List<string> MemoryPreferences { get; set; } = new()
    {
        "主人明确确认的身份与称呼",
        "具体行为偏好和纠正",
        "主人授权的相册经历摘要"
    };
    public List<string> SafetyRules { get; set; } = new()
    {
        "离线不惩罚主人",
        "模型不能直接执行行为",
        "模型不能直接写长期记忆"
    };

    public static PersonaDefinition CreateDefaultPupu() => new();

    public void Normalize()
    {
        Id = Normalize(Id, "pupu.default", 64);
        DisplayName = Normalize(DisplayName, "朴朴", 32);
        Identity = Normalize(Identity, "主人身边的本地桌面宠物", 240);
        SpeakingStyle = Normalize(SpeakingStyle, "简短、亲近、不责怪主人", 240);
        DefaultTemperament ??= new TemperamentBaseline();
        DefaultTemperament.Clamp();
        BehaviorBias = (BehaviorBias ?? new Dictionary<string, double>())
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToDictionary(
                item => item.Key.Trim(),
                item => Math.Clamp(item.Value, -1, 1),
                StringComparer.OrdinalIgnoreCase);
        MemoryPreferences = NormalizeValues(MemoryPreferences, 16, 120);
        SafetyRules = NormalizeValues(SafetyRules, 16, 120);
    }

    public string PromptSummary() =>
        $"Persona={Id}；身份={Identity}；说话风格={SpeakingStyle}；" +
        $"模型只增强回复，不执行行为，不直接写记忆。";

    private static string Normalize(string? value, string fallback, int maximumLength)
    {
        var normalized = string.Join(
            ' ',
            (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0) normalized = fallback;
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static List<string> NormalizeValues(
        IEnumerable<string>? values,
        int maximumCount,
        int maximumLength) =>
        (values ?? Array.Empty<string>())
        .Select(value => Normalize(value, string.Empty, maximumLength))
        .Where(value => value.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(maximumCount)
        .ToList();
}

public enum PetAgentEventKind
{
    UserChat,
    LocalCommand,
    PanelBehavior,
    MouseAnchor,
    AlbumExperienceHit,
    TravelReturned,
    AutonomousTimer
}

public sealed class PetAgentEvent
{
    public required PetAgentEventKind Kind { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.Now;
    public string Text { get; init; } = string.Empty;
    public string BehaviorHint { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Data { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class PetAgentContext
{
    public string CurrentStateSummary { get; init; } = string.Empty;
    public TemperamentBaseline Temperament { get; init; } = new();
    public string RelationshipSummary { get; init; } = string.Empty;
    public IReadOnlyList<string> RecentConversation { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> LongTermMemorySummaries { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AlbumExperienceSummaries { get; init; } = Array.Empty<string>();
    public string CurrentBehaviorId { get; init; } = string.Empty;
    public string ArbitrationSummary { get; init; } = string.Empty;
}

public sealed class PetAgentMemoryCandidate
{
    public string Kind { get; init; } = "candidate";
    public string Summary { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public bool RequiresOwnerConfirmation { get; init; } = true;
}

public sealed class PetAgentResult
{
    public string ReplyText { get; init; } = string.Empty;
    public IReadOnlyList<BehaviorProposal> BehaviorProposals { get; init; } =
        Array.Empty<BehaviorProposal>();
    public IReadOnlyList<PetAgentMemoryCandidate> MemoryCandidates { get; init; } =
        Array.Empty<PetAgentMemoryCandidate>();
    public IReadOnlyList<string> Debug { get; init; } = Array.Empty<string>();
}

public interface IPetAgent
{
    PetAgentResult Handle(PetAgentEvent agentEvent, PetAgentContext context);
}

/// <summary>
/// Deterministic, API-free baseline. An optional LLM may enhance ReplyText
/// outside this class, but cannot mutate proposals or memory candidates.
/// </summary>
public sealed class RulePetAgent : IPetAgent
{
    private readonly PersonaDefinition _persona;

    public RulePetAgent(PersonaDefinition persona)
    {
        _persona = persona;
        _persona.Normalize();
    }

    public PetAgentResult Handle(PetAgentEvent agentEvent, PetAgentContext context)
    {
        ArgumentNullException.ThrowIfNull(agentEvent);
        ArgumentNullException.ThrowIfNull(context);
        var proposals = new List<BehaviorProposal>();
        if (!string.IsNullOrWhiteSpace(agentEvent.BehaviorHint))
        {
            var biasKey = agentEvent.BehaviorHint.StartsWith("play.", StringComparison.OrdinalIgnoreCase)
                ? "play"
                : agentEvent.BehaviorHint.StartsWith("rest.", StringComparison.OrdinalIgnoreCase)
                    ? "quiet_rest"
                    : "near_owner";
            var bias = _persona.BehaviorBias.GetValueOrDefault(biasKey, 0);
            proposals.Add(new BehaviorProposal
            {
                BehaviorId = agentEvent.BehaviorHint.Trim(),
                Source = agentEvent.Kind == PetAgentEventKind.AlbumExperienceHit
                    ? BehaviorArbitrationSource.MemoryRecall
                    : BehaviorArbitrationSource.Autonomous,
                Priority = agentEvent.Kind == PetAgentEventKind.AlbumExperienceHit
                    ? BehaviorPriority.MemoryRecall
                    : BehaviorPriority.AutonomousMovement,
                CreatedAt = agentEvent.At,
                ExpiresAt = agentEvent.At.AddSeconds(20 + Math.Max(0, bias) * 20),
                Reason = $"{agentEvent.Kind} 经 Persona 行为偏好验证",
                Cooldown = TimeSpan.FromSeconds(20),
                CooldownKey = $"pet-agent:{agentEvent.BehaviorHint}",
                ForbiddenStates =
                    BehaviorStateBlockers.Caged |
                    BehaviorStateBlockers.Traveling |
                    BehaviorStateBlockers.Sleeping |
                    BehaviorStateBlockers.Toilet |
                    BehaviorStateBlockers.Magic |
                    BehaviorStateBlockers.Movement |
                    BehaviorStateBlockers.TouchReaction |
                    BehaviorStateBlockers.Feeding |
                    BehaviorStateBlockers.Playing |
                    BehaviorStateBlockers.Petrified
            });
        }

        var reply = agentEvent.Kind switch
        {
            PetAgentEventKind.AlbumExperienceHit when context.AlbumExperienceSummaries.Count > 0 =>
                $"我记得这条记录：{context.AlbumExperienceSummaries[0]}",
            PetAgentEventKind.UserChat => $"我听见了。{_persona.DisplayName}会按现在的状态慢慢回应你。",
            PetAgentEventKind.TravelReturned => "我回来啦，先让我把这次小旅行收好。",
            _ => string.Empty
        };
        return new PetAgentResult
        {
            ReplyText = reply,
            BehaviorProposals = proposals,
            Debug = new[]
            {
                $"persona={_persona.Id}",
                $"event={agentEvent.Kind}",
                $"proposalCount={proposals.Count}",
                "backend=local-rules"
            }
        };
    }
}
