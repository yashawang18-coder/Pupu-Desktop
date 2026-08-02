namespace Pupu.Behavior;

public enum BehaviorMotionKind
{
    Stationary,
    Locomotion,
    Teleport,
    Flight
}

public enum BehaviorPresentationPhase
{
    Enter,
    Loop,
    Exit,
    Settle
}

/// <summary>
/// Model-neutral output of the Agent core. It contains behavior semantics only:
/// no atlas row, bitmap, WPF type, bone name or engine-specific animation id.
/// </summary>
public sealed record BehaviorPresentationIntent
{
    public required string BehaviorId { get; init; }
    public BehaviorPresentationPhase Phase { get; init; } =
        BehaviorPresentationPhase.Enter;
    public BehaviorMotionKind Motion { get; init; } =
        BehaviorMotionKind.Stationary;
    public string Direction { get; init; } = "current";
    public double NormalizedSpeed { get; init; } = 1;
    public bool Loop { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record BehaviorPresentationResolution<TPresentation>(
    BehaviorPresentationIntent Intent,
    TPresentation Presentation,
    string AdapterId,
    string SourceLabel);

/// <summary>
/// Replaceable boundary between Agent semantics and a concrete visual model.
/// A sprite, 2D skeletal, 3D skeletal or procedural adapter can implement the
/// same contract without changing behavior, memory, personality or arbitration.
/// </summary>
public interface IBehaviorPresentationResolver<TPresentation>
{
    string AdapterId { get; }
    bool TryResolve(
        BehaviorPresentationIntent intent,
        out BehaviorPresentationResolution<TPresentation>? resolution);
}

public sealed class DictionaryBehaviorPresentationResolver<TPresentation>
    : IBehaviorPresentationResolver<TPresentation>
{
    private readonly IReadOnlyDictionary<string, TPresentation> _presentations;
    private readonly TPresentation _fallback;

    public DictionaryBehaviorPresentationResolver(
        string adapterId,
        IReadOnlyDictionary<string, TPresentation> presentations,
        TPresentation fallback)
    {
        AdapterId = string.IsNullOrWhiteSpace(adapterId)
            ? "presentation.dictionary"
            : adapterId.Trim();
        _presentations = presentations ??
            throw new ArgumentNullException(nameof(presentations));
        _fallback = fallback;
    }

    public string AdapterId { get; }

    public bool TryResolve(
        BehaviorPresentationIntent intent,
        out BehaviorPresentationResolution<TPresentation>? resolution)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var found = _presentations.TryGetValue(
            intent.BehaviorId,
            out var presentation);
        resolution = new BehaviorPresentationResolution<TPresentation>(
            intent,
            found ? presentation! : _fallback,
            AdapterId,
            found ? intent.BehaviorId : "fallback");
        return found;
    }
}

/// <summary>
/// Read-only decision-state boundary consumed by the Agent. The returned value
/// is a caller-owned snapshot; it is never the persistence model itself.
/// </summary>
public interface IAgentDecisionStatePort
{
    PersonalityBehaviorState ReadDecisionState();
}

/// <summary>
/// Read-only memory boundary consumed by dialogue and recall. Persistence,
/// migration and editable files remain behind the port and are independent
/// from personality/state scoring and presentation technology.
/// </summary>
public interface IAgentMemoryPort
{
    AgentMemorySnapshot ReadAgentMemory();
}

public sealed record AgentMemorySnapshot
{
    public IReadOnlyList<string> RecentEpisodes { get; init; } =
        Array.Empty<string>();
    public IReadOnlyList<string> RelationshipFacts { get; init; } =
        Array.Empty<string>();
    public IReadOnlyList<string> HabitSummaries { get; init; } =
        Array.Empty<string>();
}

/// <summary>
/// Platform-neutral facade for behavior choice and conversational proposals.
/// The desktop shell supplies perceptions and executes accepted intents; the
/// kernel owns personality/memory reads and the sole BehaviorArbitrator.
/// </summary>
public sealed class PetAgentKernel
{
    private readonly IAgentDecisionStatePort _decisionState;
    private readonly IAgentMemoryPort _memory;
    private readonly BehaviorArbitrator _arbitrator;
    private IPetAgent _agent;

    public PetAgentKernel(
        IAgentDecisionStatePort decisionState,
        IAgentMemoryPort memory,
        BehaviorArbitrator arbitrator,
        IPetAgent agent)
    {
        _decisionState = decisionState ??
            throw new ArgumentNullException(nameof(decisionState));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _arbitrator = arbitrator ?? throw new ArgumentNullException(nameof(arbitrator));
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
    }

    public BehaviorArbitrator Arbitrator => _arbitrator;

    public void ReplaceAgent(IPetAgent agent) =>
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));

    public BehaviorDecision Decide(
        IEnumerable<BehaviorDefinition> definitions,
        BehaviorContext context,
        BehaviorArbitrationContext arbitrationContext,
        BehaviorSelectionOptions? options = null) =>
        _arbitrator.SelectAutonomous(
            definitions,
            _decisionState.ReadDecisionState(),
            context,
            arbitrationContext,
            options);

    public PetAgentResult Handle(PetAgentEvent agentEvent, PetAgentContext context)
    {
        var decisionState = _decisionState.ReadDecisionState();
        var memory = _memory.ReadAgentMemory();
        var mergedContext = new PetAgentContext
        {
            CurrentStateSummary = context.CurrentStateSummary,
            Temperament = decisionState.Temperament.Clone(),
            RelationshipSummary = context.RelationshipSummary,
            RecentConversation = context.RecentConversation,
            LongTermMemorySummaries = context.LongTermMemorySummaries
                .Concat(memory.RecentEpisodes)
                .Concat(memory.RelationshipFacts)
                .Concat(memory.HabitSummaries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(24)
                .ToList(),
            AlbumExperienceSummaries = context.AlbumExperienceSummaries,
            CurrentBehaviorId = context.CurrentBehaviorId,
            ArbitrationSummary = context.ArbitrationSummary
        };
        return _agent.Handle(agentEvent, mergedContext);
    }
}
