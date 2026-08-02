using System.Text.RegularExpressions;

namespace Pupu.Behavior;

public enum BehaviorArbitrationSource
{
    DecorativeIdle,
    MouseAttention,
    Autonomous,
    ContinuousEffect,
    Touch,
    PanelCommand,
    DialogueCommand,
    OwnerAnchor,
    OwnerForced,
    MemoryRecall
}

public enum BehaviorPriority
{
    DecorativeIdle = 100,
    MouseAttention = 200,
    AutonomousMovement = 300,
    MemoryRecall = 400,
    ContinuousEffect = 500,
    TouchFeedback = 600,
    ExplicitCommand = 700,
    OwnerAnchor = 800,
    OwnerForced = 900
}

[Flags]
public enum BehaviorStateBlockers
{
    None = 0,
    Caged = 1 << 0,
    Traveling = 1 << 1,
    Sleeping = 1 << 2,
    Toilet = 1 << 3,
    Magic = 1 << 4,
    Movement = 1 << 5,
    TouchReaction = 1 << 6,
    Feeding = 1 << 7,
    Playing = 1 << 8,
    Petrified = 1 << 9
}

public sealed class BehaviorArbitrationRequest
{
    public required string BehaviorId { get; init; }
    public required BehaviorArbitrationSource Source { get; init; }
    public required BehaviorPriority Priority { get; init; }
    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.Now;
    public TimeSpan MinimumDuration { get; init; } = TimeSpan.Zero;
    public TimeSpan Cooldown { get; init; } = TimeSpan.Zero;
    public bool Interruptible { get; init; } = true;
    public bool ForceInterrupt { get; init; }
    public bool ObservationOnly { get; init; }
    public BehaviorStateBlockers ForbiddenStates { get; init; }
    public BehaviorStateBlockers AllowedStates { get; init; }
    public string CooldownKey { get; init; } = string.Empty;
}

public sealed class BehaviorArbitrationContext
{
    public string CurrentBehaviorId { get; init; } = string.Empty;
    public BehaviorPriority CurrentPriority { get; init; } = BehaviorPriority.DecorativeIdle;
    public DateTimeOffset CurrentStartedAt { get; init; } = DateTimeOffset.MinValue;
    public TimeSpan CurrentMinimumDuration { get; init; } = TimeSpan.Zero;
    public bool CurrentInterruptible { get; init; } = true;
    public BehaviorStateBlockers ActiveStates { get; init; }
}

public sealed record BehaviorLeaseSnapshot(
    string BehaviorId,
    BehaviorPriority Priority,
    DateTimeOffset StartedAt,
    TimeSpan MinimumDuration,
    bool Interruptible);

public sealed class BehaviorArbitrationResult
{
    public required BehaviorArbitrationRequest Request { get; init; }
    public bool Accepted { get; init; }
    public required string ReasonCode { get; init; }
    public required string Explanation { get; init; }
    public DateTimeOffset At => Request.RequestedAt;

    public string Display =>
        $"{At:HH:mm:ss} · {(Accepted ? "接受" : "拒绝")} · {Request.BehaviorId} · " +
        $"{Request.Source}/{Request.Priority} · {Explanation}";
}

public sealed class BehaviorSelectionOptions
{
    public BehaviorArbitrationSource Source { get; init; } =
        BehaviorArbitrationSource.Autonomous;
    public BehaviorPriority ActivePriority { get; init; } =
        BehaviorPriority.AutonomousMovement;
    public BehaviorPriority PassivePriority { get; init; } =
        BehaviorPriority.DecorativeIdle;
    public BehaviorStateBlockers ForbiddenStates { get; init; }
    public bool CommitAdmission { get; init; }
    public TimeSpan? MinimumDurationOverride { get; init; }
    public TimeSpan? CooldownOverride { get; init; }
    public bool? InterruptibleOverride { get; init; }
    public string CooldownKey { get; init; } = string.Empty;
}

/// <summary>
/// The only behavior decision authority. It owns hard eligibility, utility
/// scoring, hysteresis/selection, lifecycle admission and request cooldowns.
/// Renderers and model implementations receive the accepted semantic behavior
/// afterwards and never participate in the decision.
/// </summary>
public sealed class BehaviorArbitrator
{
    private readonly object _sync = new();
    private readonly Dictionary<string, DateTimeOffset> _lastAccepted =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly BehaviorScorer _scorer;
    private readonly SelectionPolicy _selectionPolicy;
    private readonly IRandomSource _random;
    private readonly List<BehaviorHistoryEntry> _history = new();
    private BehaviorLeaseSnapshot? _currentLease;

    public BehaviorArbitrator(
        BehaviorScorer? scorer = null,
        IRandomSource? random = null,
        SelectionPolicy? selectionPolicy = null)
    {
        _scorer = scorer ?? new BehaviorScorer();
        _random = random ?? new SystemRandomSource();
        _selectionPolicy = selectionPolicy ?? new SelectionPolicy();
    }

    public IReadOnlyList<BehaviorHistoryEntry> History
    {
        get
        {
            lock (_sync) return _history.ToList();
        }
    }

    public BehaviorLeaseSnapshot? CurrentLease
    {
        get
        {
            lock (_sync) return _currentLease;
        }
    }

    public void ResetCurrent(
        DateTimeOffset now,
        string behaviorId = "idle.side_lie",
        TimeSpan? minimumDuration = null)
    {
        lock (_sync)
        {
            _currentLease = new BehaviorLeaseSnapshot(
                behaviorId,
                BehaviorPriority.DecorativeIdle,
                now,
                minimumDuration ?? TimeSpan.Zero,
                true);
        }
    }

    public void RestoreCurrent(
        string behaviorId,
        BehaviorPriority priority,
        DateTimeOffset startedAt,
        TimeSpan minimumDuration,
        bool interruptible)
    {
        if (string.IsNullOrWhiteSpace(behaviorId))
            throw new ArgumentException("Behavior id is required.", nameof(behaviorId));
        lock (_sync)
        {
            _currentLease = new BehaviorLeaseSnapshot(
                behaviorId.Trim(),
                priority,
                startedAt,
                minimumDuration,
                interruptible);
        }
    }

    public bool ReleaseCurrent(string? expectedBehaviorId = null)
    {
        lock (_sync)
        {
            if (_currentLease is null) return false;
            if (!string.IsNullOrWhiteSpace(expectedBehaviorId) &&
                !string.Equals(
                    _currentLease.BehaviorId,
                    expectedBehaviorId,
                    StringComparison.OrdinalIgnoreCase))
                return false;
            _currentLease = null;
            return true;
        }
    }

    public BehaviorDecision SelectAutonomous(
        IEnumerable<BehaviorDefinition> definitions,
        PersonalityBehaviorState state,
        BehaviorContext context,
        BehaviorArbitrationContext? arbitrationContext = null,
        BehaviorSelectionOptions? options = null)
    {
        lock (_sync)
            return SelectAutonomousCore(
                definitions,
                state,
                context,
                arbitrationContext,
                options);
    }

    private BehaviorDecision SelectAutonomousCore(
        IEnumerable<BehaviorDefinition> definitions,
        PersonalityBehaviorState state,
        BehaviorContext context,
        BehaviorArbitrationContext? arbitrationContext,
        BehaviorSelectionOptions? options)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        options ??= new BehaviorSelectionOptions();
        arbitrationContext ??= new BehaviorArbitrationContext
        {
            CurrentBehaviorId = context.CurrentBehaviorId,
            CurrentStartedAt = context.CurrentBehaviorStartedAt,
            CurrentInterruptible = context.CurrentBehaviorInterruptible
        };

        var definitionList = definitions.ToList();
        var eligibility = new List<BehaviorEligibility>(definitionList.Count);
        var eligible = new List<BehaviorDefinition>(definitionList.Count);
        foreach (var definition in definitionList)
        {
            var hardGate = EvaluateEligibility(definition, state, context);
            var reasons = hardGate.Reasons.ToList();
            if (hardGate.IsEligible)
            {
                var preflightAdmission = EvaluateCore(
                    CreateSelectionRequest(definition, context.Now, options),
                    arbitrationContext,
                    commit: false);
                if (!preflightAdmission.Accepted)
                    reasons.Add($"arbitration:{preflightAdmission.ReasonCode}");
            }

            var item = new BehaviorEligibility
            {
                BehaviorId = definition.BehaviorId,
                IsEligible = reasons.Count == 0,
                Reasons = reasons
            };
            eligibility.Add(item);
            if (item.IsEligible) eligible.Add(definition);
        }

        var candidates = eligible
            .Select(definition =>
                _scorer.Score(definition, state, context, _history, _random))
            .OrderByDescending(item => item.FinalScore)
            .ThenBy(item => item.BehaviorId, StringComparer.Ordinal)
            .ToList();
        if (candidates.Count == 0)
        {
            var blocked = string.Join(
                "; ",
                eligibility.Select(item =>
                    $"{item.BehaviorId}={string.Join(',', item.Reasons)}"));
            return new BehaviorDecision
            {
                SelectedBehaviorId = context.CurrentBehaviorId,
                Candidates = candidates,
                Eligibility = eligibility,
                At = context.Now,
                Deferred = true,
                Reason =
                    "BehaviorArbitrator 没有可切换候选；保持当前动作并延后重试。 " +
                    blocked
            };
        }

        var selected = _selectionPolicy.Select(candidates, _random);
        selected.Selected = true;
        BehaviorArbitrationResult? admission = null;
        if (options.CommitAdmission)
        {
            var definition = definitionList.First(item =>
                string.Equals(
                    item.BehaviorId,
                    selected.BehaviorId,
                    StringComparison.Ordinal));
            admission = EvaluateCore(
                CreateSelectionRequest(definition, context.Now, options),
                arbitrationContext,
                commit: true);
            if (!admission.Accepted)
            {
                return new BehaviorDecision
                {
                    SelectedBehaviorId = context.CurrentBehaviorId,
                    Candidates = candidates,
                    Eligibility = eligibility,
                    At = context.Now,
                    Deferred = true,
                    Admission = admission,
                    Reason =
                        "BehaviorArbitrator 在同一快照内未能提交已选行为；" +
                        $"保持当前动作：{admission.ReasonCode}"
                };
            }
        }

        _history.Add(new BehaviorHistoryEntry
        {
            BehaviorId = selected.BehaviorId,
            SelectedAt = context.Now
        });
        if (_history.Count > 80) _history.RemoveRange(0, _history.Count - 80);
        return new BehaviorDecision
        {
            SelectedBehaviorId = selected.BehaviorId,
            Candidates = candidates,
            Eligibility = eligibility,
            At = context.Now,
            Admission = admission,
            Reason =
                $"BehaviorArbitrator passed {candidates.Count}/{eligibility.Count}; " +
                $"selected from one scored candidate set: {selected.Explain()}"
        };
    }

    public BehaviorArbitrationResult Evaluate(
        BehaviorArbitrationRequest request,
        BehaviorArbitrationContext context)
    {
        lock (_sync) return EvaluateCore(request, context, commit: true);
    }

    public BehaviorEligibility InspectEligibility(
        BehaviorDefinition definition,
        PersonalityBehaviorState state,
        BehaviorContext context)
    {
        lock (_sync) return EvaluateEligibility(definition, state, context);
    }

    /// <summary>
    /// Restores the lease and request cooldown when a platform adapter could
    /// not execute an accepted proposal. Only the exact admission timestamp is
    /// reverted, so a newer decision can never be overwritten.
    /// </summary>
    public void RollbackAdmission(
        BehaviorArbitrationResult admission,
        BehaviorLeaseSnapshot? previousLease)
    {
        ArgumentNullException.ThrowIfNull(admission);
        lock (_sync)
        {
            if (!admission.Accepted) return;
            var request = admission.Request;
            var cooldownKey = string.IsNullOrWhiteSpace(request.CooldownKey)
                ? request.BehaviorId
                : request.CooldownKey;
            if (_lastAccepted.TryGetValue(cooldownKey, out var acceptedAt) &&
                acceptedAt == request.RequestedAt)
                _lastAccepted.Remove(cooldownKey);
            if (_currentLease is not null &&
                string.Equals(
                    _currentLease.BehaviorId,
                    request.BehaviorId,
                    StringComparison.OrdinalIgnoreCase) &&
                _currentLease.StartedAt == request.RequestedAt)
                _currentLease = previousLease;
        }
    }

    internal static BehaviorEligibility EvaluateEligibility(
        BehaviorDefinition definition,
        PersonalityBehaviorState state,
        BehaviorContext context)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        var reasons = new List<string>();
        var autonomous = context.RequestSource == BehaviorRequestSource.Autonomous;
        if (definition.RequiresMovement && !context.EnvironmentAllowsMovement)
            reasons.Add("movement_not_allowed");
        if (autonomous &&
            !string.IsNullOrWhiteSpace(context.CurrentBehaviorId) &&
            definition.BehaviorId != context.CurrentBehaviorId)
        {
            if (!context.CurrentBehaviorInterruptible)
                reasons.Add("current_action_not_interruptible");
            else if (context.CurrentBehaviorStartedAt != DateTimeOffset.MinValue)
            {
                var current = BehaviorCatalog.Find(context.CurrentBehaviorId);
                var dwell = current?.MinimumDwell ?? TimeSpan.FromSeconds(45);
                if (current?.InteractionType == "autonomous" &&
                    dwell < context.MinimumAutonomousDwell)
                    dwell = context.MinimumAutonomousDwell;
                if (context.Now - context.CurrentBehaviorStartedAt < dwell)
                    reasons.Add("minimum_dwell");
            }
        }

        if (autonomous && definition.IsOwnerInitiative)
        {
            if (!context.AllowOwnerInitiative) reasons.Add("owner_initiative_disabled");
            if (!context.UserRespondedToLastInitiative)
                reasons.Add("previous_initiative_unanswered");
            if (context.InitiativeCooldownActive) reasons.Add("initiative_cooldown");
        }

        if (autonomous)
        {
            if (context.IsDeepNight && definition.IsHighDisruption)
                reasons.Add("deep_night_high_disruption");
            if ((context.DoNotDisturb || context.MeetingMode || context.FullScreen) &&
                (definition.IsHighDisruption || definition.IsOwnerInitiative))
                reasons.Add("quiet_environment");
            if (state.Runtime.Stress >= 0.78 && definition.IsHighDisruption)
                reasons.Add("stress_safety_limit");
            if (state.Runtime.Fatigue >= 0.88 && definition.IsHighDisruption)
                reasons.Add("fatigue_safety_limit");
            if (state.Runtime.Safety <= 0.18 && definition.IsHighDisruption)
                reasons.Add("low_safety");
        }

        foreach (var required in definition.RequiredSignals)
        {
            if (!context.Signals.TryGetValue(required.Key, out var signal) ||
                signal < required.Value)
                reasons.Add($"missing_signal:{required.Key}");
        }

        return new BehaviorEligibility
        {
            BehaviorId = definition.BehaviorId,
            IsEligible = reasons.Count == 0,
            Reasons = reasons
        };
    }

    private BehaviorArbitrationResult EvaluateCore(
        BehaviorArbitrationRequest request,
        BehaviorArbitrationContext context,
        bool commit)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(request.BehaviorId))
            return Reject(request, "invalid_behavior", "行为标识为空");

        var forbidden = context.ActiveStates & request.ForbiddenStates & ~request.AllowedStates;
        if (forbidden != BehaviorStateBlockers.None)
            return Reject(
                request,
                "state_forbidden",
                $"当前状态禁止该行为：{DescribeStates(forbidden)}");

        var cooldownKey = string.IsNullOrWhiteSpace(request.CooldownKey)
            ? request.BehaviorId
            : request.CooldownKey;
        if (!request.ForceInterrupt &&
            request.Cooldown > TimeSpan.Zero &&
            _lastAccepted.TryGetValue(cooldownKey, out var lastAccepted) &&
            request.RequestedAt - lastAccepted < request.Cooldown)
        {
            var remaining = request.Cooldown - (request.RequestedAt - lastAccepted);
            return Reject(
                request,
                "request_cooldown",
                $"请求仍在冷却中，约 {Math.Ceiling(remaining.TotalSeconds):0} 秒后可重试");
        }

        var currentBehaviorId =
            _currentLease?.BehaviorId ?? context.CurrentBehaviorId;
        var currentPriority =
            _currentLease?.Priority ?? context.CurrentPriority;
        var currentStartedAt =
            _currentLease?.StartedAt ?? context.CurrentStartedAt;
        var currentMinimumDuration =
            _currentLease?.MinimumDuration ?? context.CurrentMinimumDuration;
        var currentInterruptible =
            _currentLease?.Interruptible ?? context.CurrentInterruptible;
        var hasDifferentCurrent =
            !request.ObservationOnly &&
            !string.IsNullOrWhiteSpace(currentBehaviorId) &&
            !string.Equals(
                currentBehaviorId,
                request.BehaviorId,
                StringComparison.OrdinalIgnoreCase);
        if (hasDifferentCurrent && !request.ForceInterrupt)
        {
            if (request.Priority < currentPriority)
                return Reject(
                    request,
                    "lower_priority",
                    $"优先级低于当前行为 {currentBehaviorId}（{currentPriority}）");

            if (!currentInterruptible)
                return Reject(
                    request,
                    "current_not_interruptible",
                    $"当前行为 {currentBehaviorId} 处于不可打断阶段");

            if (currentStartedAt != DateTimeOffset.MinValue &&
                request.RequestedAt - currentStartedAt < currentMinimumDuration)
            {
                var remaining =
                    currentMinimumDuration -
                    (request.RequestedAt - currentStartedAt);
                return Reject(
                    request,
                    "minimum_duration",
                    $"当前行为保护期尚未结束，约 {Math.Ceiling(remaining.TotalSeconds):0} 秒");
            }
        }

        if (commit)
        {
            _lastAccepted[cooldownKey] = request.RequestedAt;
            if (!request.ObservationOnly)
            {
                _currentLease = new BehaviorLeaseSnapshot(
                    request.BehaviorId,
                    request.Priority,
                    request.RequestedAt,
                    request.MinimumDuration,
                    request.Interruptible);
            }
        }
        return new BehaviorArbitrationResult
        {
            Request = request,
            Accepted = true,
            ReasonCode = request.ObservationOnly ? "observation_accepted" : "accepted",
            Explanation = request.ForceInterrupt
                ? "主人强制请求已接管当前行为"
                : request.ObservationOnly
                    ? "只更新轻量信号，不抢占当前动作"
                    : "优先级、保护期、冷却和状态检查均通过"
        };
    }

    private static BehaviorArbitrationRequest CreateSelectionRequest(
        BehaviorDefinition definition,
        DateTimeOffset now,
        BehaviorSelectionOptions options)
    {
        var passive =
            definition.IsPassive &&
            !definition.RequiresMovement &&
            !definition.BehaviorId.StartsWith("play.", StringComparison.Ordinal);
        return new BehaviorArbitrationRequest
        {
            BehaviorId = definition.BehaviorId,
            Source = definition.ArbitrationSource ?? options.Source,
            Priority = definition.ArbitrationPriority ??
                (passive ? options.PassivePriority : options.ActivePriority),
            RequestedAt = now,
            MinimumDuration =
                options.MinimumDurationOverride ?? definition.MinimumDwell,
            Cooldown = options.CooldownOverride ?? definition.Cooldown,
            Interruptible =
                options.InterruptibleOverride ??
                definition.Interruptible ??
                !definition.RequiresMovement,
            ForbiddenStates = options.ForbiddenStates,
            CooldownKey = options.CooldownKey
        };
    }

    private static BehaviorArbitrationResult Reject(
        BehaviorArbitrationRequest request,
        string reasonCode,
        string explanation) =>
        new()
        {
            Request = request,
            Accepted = false,
            ReasonCode = reasonCode,
            Explanation = explanation
        };

    private static string DescribeStates(BehaviorStateBlockers states)
    {
        var labels = new List<string>();
        if (states.HasFlag(BehaviorStateBlockers.Caged)) labels.Add("关笼子");
        if (states.HasFlag(BehaviorStateBlockers.Traveling)) labels.Add("外出旅游");
        if (states.HasFlag(BehaviorStateBlockers.Sleeping)) labels.Add("睡眠");
        if (states.HasFlag(BehaviorStateBlockers.Toilet)) labels.Add("如厕");
        if (states.HasFlag(BehaviorStateBlockers.Magic)) labels.Add("魔法");
        if (states.HasFlag(BehaviorStateBlockers.Movement)) labels.Add("移动");
        if (states.HasFlag(BehaviorStateBlockers.TouchReaction)) labels.Add("触摸反应");
        if (states.HasFlag(BehaviorStateBlockers.Feeding)) labels.Add("进食");
        if (states.HasFlag(BehaviorStateBlockers.Playing)) labels.Add("玩耍");
        if (states.HasFlag(BehaviorStateBlockers.Petrified)) labels.Add("石化");
        return labels.Count == 0 ? "未知" : string.Join("、", labels);
    }
}

public enum LocalInteractionIntent
{
    None,
    QuietForAWhile,
    AllowSelfPlay,
    FoodAnchor,
    ToyAnchor,
    Cage,
    ReleaseCage,
    Travel,
    RecallTravel
}

public enum InteractionAnchorKind
{
    Food,
    Toy
}

public sealed record InteractionAnchor(
    InteractionAnchorKind Kind,
    double X,
    double Y,
    DateTimeOffset CreatedAt);

public enum CoinPointerAction
{
    None,
    RefreshColor,
    Flip
}

public static class CoinPointerGestureClassifier
{
    public static CoinPointerAction Classify(bool dragged, int clickCount)
    {
        if (dragged) return CoinPointerAction.None;
        return clickCount >= 2
            ? CoinPointerAction.Flip
            : CoinPointerAction.RefreshColor;
    }
}

public sealed record LocalInteractionCommand(
    LocalInteractionIntent Intent,
    string Destination = "",
    TimeSpan? Duration = null);

/// <summary>
/// Deterministic local command recognition. Model output is deliberately not an
/// input to this parser, so an LLM can phrase a reply but cannot execute state.
/// </summary>
public sealed class LocalInteractionCommandParser
{
    private static readonly Regex TravelDuration = new(
        "(?<number>\\d+(?:\\.\\d+)?)\\s*(?<unit>小时|钟头|分钟|分)",
        RegexOptions.Compiled);
    private static readonly Regex TravelDestination = new(
        "(?:去|送去|到)(?<destination>[^，。,.!?！？]{1,20}?)(?:旅游|旅行|玩(?:一会儿)?|转转)",
        RegexOptions.Compiled);

    public LocalInteractionCommand Parse(string? input)
    {
        var text = (input ?? string.Empty).Trim();
        if (text.Length == 0) return new(LocalInteractionIntent.None);

        if (ContainsAny(text, "释放", "放出来", "出笼子", "别关了"))
            return new(LocalInteractionIntent.ReleaseCage);
        if (ContainsAny(
                text,
                "召回",
                "叫回来",
                "回来吧",
                "旅行回来",
                "旅游回来",
                "结束旅行",
                "结束旅游"))
            return new(LocalInteractionIntent.RecallTravel);
        if (ContainsAny(text, "进笼子", "关笼子", "先关起来", "关起来"))
            return new(LocalInteractionIntent.Cage);
        if (ContainsAny(text, "安静一会", "安静一会儿", "先安静", "别打扰我"))
            return new(LocalInteractionIntent.QuietForAWhile);
        if (ContainsAny(text, "自己玩吧", "自己玩一会", "自己去玩"))
            return new(LocalInteractionIntent.AllowSelfPlay);
        if (ContainsAny(text, "来吃一下", "吃一下", "放点吃的", "饭碗", "冻干"))
            return new(LocalInteractionIntent.FoodAnchor);
        if (ContainsAny(text, "陪我玩", "玩一下", "逗猫棒", "激光笔"))
            return new(LocalInteractionIntent.ToyAnchor);
        if (ContainsAny(text, "旅游", "旅行") &&
            ContainsAny(text, "去", "送", "出发", "让朴朴"))
        {
            var destination = TravelDestination.Match(text) is { Success: true } destinationMatch
                ? destinationMatch.Groups["destination"].Value.Trim()
                : string.Empty;
            return new(
                LocalInteractionIntent.Travel,
                destination,
                ReadTravelDuration(text));
        }

        return new(LocalInteractionIntent.None);
    }

    private static TimeSpan? ReadTravelDuration(string text)
    {
        var match = TravelDuration.Match(text);
        if (!match.Success ||
            !double.TryParse(match.Groups["number"].Value, out var number))
            return null;
        var duration = match.Groups["unit"].Value is "分钟" or "分"
            ? TimeSpan.FromMinutes(number)
            : TimeSpan.FromHours(number);
        return TimeSpan.FromMinutes(
            Math.Clamp(duration.TotalMinutes, 15, TimeSpan.FromHours(24).TotalMinutes));
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(value.Contains);
}
