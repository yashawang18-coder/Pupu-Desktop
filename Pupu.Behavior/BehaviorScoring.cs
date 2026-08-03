namespace Pupu.Behavior;

public enum TemperamentDimension
{
    Playful,
    Affectionate,
    Sensitive,
    Independent,
    Mischievous
}

public enum RuntimeDimension
{
    Arousal,
    Stress,
    SocialDesire,
    PlayDesire,
    Curiosity,
    Fatigue,
    Safety
}

public enum RelationshipDimension
{
    Trust,
    Familiarity,
    TouchAcceptance,
    InitiativeAcceptance
}

public sealed class BehaviorDefinition
{
    public required string BehaviorId { get; init; }
    public string InteractionType { get; init; } = "autonomous";
    public double BaseWeight { get; init; }
    public Dictionary<TemperamentDimension, double> TemperamentAffinity { get; init; } = new();
    public Dictionary<RuntimeDimension, double> RuntimeFit { get; init; } = new();
    public Dictionary<RelationshipDimension, double> RelationshipFit { get; init; } = new();
    public Dictionary<string, double> ContextAffinity { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> RequiredSignals { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
    public TimeSpan MinimumDwell { get; init; } = TimeSpan.FromSeconds(45);
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromSeconds(30);
    public double RepetitionSuppression { get; init; } = 0.34;
    public double SwitchHysteresis { get; init; } = 0.30;
    public double InterruptionCost { get; init; } = 0.45;
    public double JitterAmplitude { get; init; } = 0.10;
    public bool IsPassive { get; init; }
    public bool IsHighDisruption { get; init; }
    public bool RequiresMovement { get; init; }
    public bool IsOwnerInitiative { get; init; }
    public BehaviorArbitrationSource? ArbitrationSource { get; init; }
    public BehaviorPriority? ArbitrationPriority { get; init; }
    public bool? Interruptible { get; init; }
}

public enum BehaviorRequestSource
{
    Autonomous,
    Owner,
    Touch,
    GalleryPreview
}

public sealed class BehaviorContext
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.Now;
    public string CurrentBehaviorId { get; set; } = string.Empty;
    public DateTimeOffset CurrentBehaviorStartedAt { get; set; } = DateTimeOffset.MinValue;
    public bool CurrentBehaviorInterruptible { get; set; } = true;
    public bool IsDeepNight { get; set; }
    public bool DoNotDisturb { get; set; }
    public bool MeetingMode { get; set; }
    public bool FullScreen { get; set; }
    public bool EnvironmentAllowsMovement { get; set; } = true;
    public bool UserRespondedToLastInitiative { get; set; } = true;
    public bool AllowOwnerInitiative { get; set; } = true;
    public bool InitiativeCooldownActive { get; set; }
    public BehaviorRequestSource RequestSource { get; set; } = BehaviorRequestSource.Autonomous;
    public TimeSpan MinimumAutonomousDwell { get; set; } = TimeSpan.FromSeconds(75);
    public string ContextKey { get; set; } = "general";
    public string LocationKey { get; set; } = "desktop";
    public string TimeBucket { get; set; } = "day";
    public Dictionary<string, double> Signals { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public bool QuietMode => IsDeepNight || DoNotDisturb || MeetingMode || FullScreen;
}

public sealed class BehaviorHistoryEntry
{
    public string BehaviorId { get; set; } = string.Empty;
    public DateTimeOffset SelectedAt { get; set; }
}

public sealed class BehaviorScoreBreakdown
{
    public string BehaviorId { get; set; } = string.Empty;
    public double BaseWeight { get; set; }
    public double TemperamentAffinity { get; set; }
    public double RuntimeStateFit { get; set; }
    public double RelationshipFit { get; set; }
    public double LearnedPreference { get; set; }
    public double ContextFit { get; set; }
    public double CooldownPenalty { get; set; }
    public double RepetitionPenalty { get; set; }
    public double InterruptionCost { get; set; }
    public double SeededJitter { get; set; }
    public double FinalScore { get; set; }
    public bool Selected { get; set; }

    public string Explain() =>
        $"{BehaviorId}: base={BaseWeight:+0.000;-0.000;0.000}, " +
        $"temperament={TemperamentAffinity:+0.000;-0.000;0.000}, " +
        $"runtime={RuntimeStateFit:+0.000;-0.000;0.000}, " +
        $"relationship={RelationshipFit:+0.000;-0.000;0.000}, " +
        $"learned={LearnedPreference:+0.000;-0.000;0.000}, " +
        $"context={ContextFit:+0.000;-0.000;0.000}, " +
        $"cooldown=-{CooldownPenalty:0.000}, repetition=-{RepetitionPenalty:0.000}, " +
        $"interruption=-{InterruptionCost:0.000}, jitter={SeededJitter:+0.000;-0.000;0.000}, " +
        $"final={FinalScore:0.000}";
}

public sealed class BehaviorDecision
{
    public required string SelectedBehaviorId { get; init; }
    public required IReadOnlyList<BehaviorScoreBreakdown> Candidates { get; init; }
    public required IReadOnlyList<BehaviorEligibility> Eligibility { get; init; }
    public required string Reason { get; init; }
    public DateTimeOffset At { get; init; }
    public bool Deferred { get; init; }
    public BehaviorArbitrationResult? Admission { get; init; }
}

public sealed class BehaviorScorer
{
    public BehaviorScoreBreakdown Score(
        BehaviorDefinition definition,
        PersonalityBehaviorState state,
        BehaviorContext context,
        IReadOnlyList<BehaviorHistoryEntry> history,
        IRandomSource random)
    {
        var temperament = definition.TemperamentAffinity.Sum(pair =>
            pair.Value * (ReadTemperament(state.Temperament, pair.Key) - 0.5));
        var runtime = definition.RuntimeFit.Sum(pair =>
            pair.Value * ReadRuntime(state.Runtime, pair.Key));
        var relationship = definition.RelationshipFit.Sum(pair =>
            pair.Value * (ReadRelationship(state.Relationship, pair.Key) - 0.5));
        var learned = ReadLearnedPreference(definition, state, context);
        var contextFit = ScoreContext(definition, context);
        var last = history.LastOrDefault(x => x.BehaviorId == definition.BehaviorId);
        var cooldownPenalty = last is null
            ? 0
            : Math.Max(0, 1 - (context.Now - last.SelectedAt).TotalMilliseconds /
                Math.Max(1, definition.Cooldown.TotalMilliseconds)) * 2.4;
        var recentRepetitions = history.TakeLast(6).Count(x => x.BehaviorId == definition.BehaviorId);
        var repetitionPenalty = recentRepetitions * definition.RepetitionSuppression;
        var interruption = ScoreInterruption(definition, context);
        var jitter = (random.NextDouble() * 2 - 1) * definition.JitterAmplitude;
        if (definition.BehaviorId == context.CurrentBehaviorId)
            contextFit += definition.SwitchHysteresis;

        var result = new BehaviorScoreBreakdown
        {
            BehaviorId = definition.BehaviorId,
            BaseWeight = definition.BaseWeight,
            TemperamentAffinity = temperament,
            RuntimeStateFit = runtime,
            RelationshipFit = relationship,
            LearnedPreference = learned,
            ContextFit = contextFit,
            CooldownPenalty = cooldownPenalty,
            RepetitionPenalty = repetitionPenalty,
            InterruptionCost = interruption,
            SeededJitter = jitter
        };
        result.FinalScore =
            result.BaseWeight
            + result.TemperamentAffinity
            + result.RuntimeStateFit
            + result.RelationshipFit
            + result.LearnedPreference
            + result.ContextFit
            - result.CooldownPenalty
            - result.RepetitionPenalty
            - result.InterruptionCost
            + result.SeededJitter;
        return result;
    }

    private static double ReadLearnedPreference(
        BehaviorDefinition definition,
        PersonalityBehaviorState state,
        BehaviorContext context)
    {
        var keys = new[]
        {
            PreferenceKey.Create(definition.BehaviorId, definition.InteractionType, context.ContextKey),
            PreferenceKey.Create(definition.BehaviorId, definition.InteractionType, context.TimeBucket),
            PreferenceKey.Create(definition.BehaviorId, definition.InteractionType, context.LocationKey),
            PreferenceKey.Create(definition.BehaviorId, definition.InteractionType, "general")
        }.Distinct();
        var explicitValues = keys
            .Where(state.LearnedPreferences.ContainsKey)
            .Select(key => state.LearnedPreferences[key].EffectiveWeight(context.Now))
            .ToList();
        if (explicitValues.Count == 0)
        {
            explicitValues = state.LearnedPreferences.Values
                .Where(x =>
                    string.Equals(x.BehaviorId, definition.BehaviorId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.InteractionType, definition.InteractionType, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.EffectiveWeight(context.Now))
                .ToList();
        }
        var derivedValues = keys
            .Where(state.DerivedHabitPreferences.ContainsKey)
            .Select(key => state.DerivedHabitPreferences[key].EffectiveWeight)
            .ToList();
        var explicitWeight = explicitValues.Count == 0 ? 0 : explicitValues.Average();
        var derivedWeight = derivedValues.Count == 0 ? 0 : derivedValues.Average();
        // Indexed derived memory and explicit owner feedback have a shared
        // ceiling. Raw events are never traversed by the decision path.
        return Math.Clamp(explicitWeight + derivedWeight, -0.65, 0.65);
    }

    private static double ScoreContext(BehaviorDefinition definition, BehaviorContext context)
    {
        var result = 0d;
        foreach (var pair in definition.ContextAffinity)
        {
            if (context.Signals.TryGetValue(pair.Key, out var signal))
                result += pair.Value * signal;
        }
        if (context.IsDeepNight)
            result += definition.IsPassive ? 0.15 : 0;
        return result;
    }

    private static double ScoreInterruption(BehaviorDefinition definition, BehaviorContext context)
    {
        if (string.IsNullOrEmpty(context.CurrentBehaviorId) ||
            definition.BehaviorId == context.CurrentBehaviorId)
            return 0;
        return definition.InterruptionCost;
    }

    private static double ReadTemperament(TemperamentBaseline value, TemperamentDimension dimension) =>
        dimension switch
        {
            TemperamentDimension.Playful => value.Playful,
            TemperamentDimension.Affectionate => value.Affectionate,
            TemperamentDimension.Sensitive => value.Sensitive,
            TemperamentDimension.Independent => value.Independent,
            _ => value.Mischievous
        };

    private static double ReadRuntime(RuntimeState value, RuntimeDimension dimension) =>
        dimension switch
        {
            RuntimeDimension.Arousal => value.Arousal,
            RuntimeDimension.Stress => value.Stress,
            RuntimeDimension.SocialDesire => value.SocialDesire,
            RuntimeDimension.PlayDesire => value.PlayDesire,
            RuntimeDimension.Curiosity => value.Curiosity,
            RuntimeDimension.Fatigue => value.Fatigue,
            _ => value.Safety
        };

    private static double ReadRelationship(RelationshipState value, RelationshipDimension dimension) =>
        dimension switch
        {
            RelationshipDimension.Trust => value.Trust,
            RelationshipDimension.Familiarity => value.Familiarity,
            RelationshipDimension.TouchAcceptance => value.TouchAcceptance,
            _ => value.InitiativeAcceptance
        };
}

public sealed class BehaviorSelector
{
    private readonly BehaviorArbitrator _arbitrator;

    public BehaviorSelector(BehaviorArbitrator arbitrator) =>
        _arbitrator = arbitrator ??
            throw new ArgumentNullException(nameof(arbitrator));

    public IReadOnlyList<BehaviorHistoryEntry> History => _arbitrator.History;

    public BehaviorDecision Select(
        IEnumerable<BehaviorDefinition> definitions,
        PersonalityBehaviorState state,
        BehaviorContext context)
        => _arbitrator.SelectAutonomous(definitions, state, context);
}

public static class BehaviorCatalog
{
    private static readonly IReadOnlyList<BehaviorDefinition> AutonomousDefinitions =
        BuildAutonomousDefinitions();
    private static readonly IReadOnlyList<BehaviorDefinition> TouchDefinitions =
        BuildTouchDefinitions();

    public static IReadOnlyList<BehaviorDefinition> Autonomous => AutonomousDefinitions;
    public static IReadOnlyList<BehaviorDefinition> TouchResponses => TouchDefinitions;

    public static BehaviorDefinition? Find(string behaviorId) =>
        AutonomousDefinitions.Concat(TouchDefinitions)
            .FirstOrDefault(x => x.BehaviorId == behaviorId);

    private static IReadOnlyList<BehaviorDefinition> BuildAutonomousDefinitions() => new[]
    {
        Def("idle.side_lie", 0.48, passive: true,
            t: T((TemperamentDimension.Playful, -0.35), (TemperamentDimension.Independent, 0.10)),
            r: R((RuntimeDimension.Fatigue, 0.55), (RuntimeDimension.Stress, 0.15), (RuntimeDimension.PlayDesire, -0.55)),
            dwell: 120),
        Def("idle.prone_observe", 0.42, passive: true,
            t: T((TemperamentDimension.Sensitive, 0.25)),
            r: R((RuntimeDimension.Curiosity, 0.45), (RuntimeDimension.Stress, 0.25)),
            dwell: 120),
        Def("idle.sploot", 0.34, passive: true,
            t: T((TemperamentDimension.Playful, -0.12), (TemperamentDimension.Independent, 0.18)),
            r: R((RuntimeDimension.Fatigue, 0.62), (RuntimeDimension.Stress, -0.35), (RuntimeDimension.Safety, 0.48)),
            c: C(("daytime", 0.18)), dwell: 150, cooldown: 210),
        Def("self.groom", 0.08, passive: true,
            t: T((TemperamentDimension.Independent, 1.00), (TemperamentDimension.Sensitive, 0.20)),
            r: R((RuntimeDimension.Safety, 0.45), (RuntimeDimension.Stress, -0.25)),
            dwell: 75, cooldown: 600),
        Def("self.paw_nibble", 0.025, passive: true,
            t: T((TemperamentDimension.Independent, 0.75), (TemperamentDimension.Playful, 0.18)),
            r: R((RuntimeDimension.Safety, 0.38), (RuntimeDimension.Stress, -0.30)),
            dwell: 45, cooldown: 900),
        Def("routine.toilet", 0.10, passive: true,
            r: R((RuntimeDimension.Stress, -0.20), (RuntimeDimension.Safety, 0.12)),
            c: C(("toilet_due", 3.10)), required: C(("toilet_due", 0.5)),
            dwell: 12, cooldown: 1200,
            arbitrationSource: BehaviorArbitrationSource.ContinuousEffect,
            arbitrationPriority: BehaviorPriority.ContinuousEffect,
            interruptible: false),
        Def("rest.bed", 0.03, passive: true,
            t: T((TemperamentDimension.Playful, -0.22), (TemperamentDimension.Sensitive, 0.08)),
            r: R((RuntimeDimension.Fatigue, 1.85), (RuntimeDimension.Arousal, -0.90),
                (RuntimeDimension.Stress, -0.30), (RuntimeDimension.Safety, 0.65)),
            c: C(("deep_night", 0.18), ("daytime", 0.06)),
            dwell: 420, cooldown: 900),
        Def("rest.far", 0.12, passive: true,
            t: T((TemperamentDimension.Independent, 1.10)),
            r: R((RuntimeDimension.Fatigue, 0.70), (RuntimeDimension.Stress, 0.35), (RuntimeDimension.Safety, -0.25))),
        Def("rest.near_owner", 0.14, passive: true,
            t: T((TemperamentDimension.Affectionate, 1.05), (TemperamentDimension.Independent, -0.55)),
            r: R((RuntimeDimension.Fatigue, 0.65), (RuntimeDimension.SocialDesire, 0.55), (RuntimeDimension.Stress, -0.70)),
            rel: L((RelationshipDimension.Trust, 0.55))),
        Def("rest.sleep", 0.08, passive: true,
            t: T((TemperamentDimension.Playful, -0.25)),
            r: R((RuntimeDimension.Fatigue, 1.55), (RuntimeDimension.Arousal, -0.65), (RuntimeDimension.Stress, 0.15)),
            dwell: 180, cooldown: 240),
        Def("rest.sleep.curled", 0.12, passive: true,
            t: T((TemperamentDimension.Playful, -0.20), (TemperamentDimension.Sensitive, 0.10)),
            r: R((RuntimeDimension.Fatigue, 1.70), (RuntimeDimension.Arousal, -0.75), (RuntimeDimension.Stress, -0.15), (RuntimeDimension.Safety, 0.45)),
            c: C(("daytime", 0.70), ("deep_night", -0.28)),
            dwell: 360, cooldown: 720),
        Def("rest.sleep.belly_up", 0.02, passive: true,
            t: T((TemperamentDimension.Affectionate, 0.20), (TemperamentDimension.Sensitive, -0.12)),
            r: R((RuntimeDimension.Fatigue, 1.55), (RuntimeDimension.Arousal, -0.72), (RuntimeDimension.Stress, -0.55), (RuntimeDimension.Safety, 0.85)),
            rel: L((RelationshipDimension.Trust, 0.35)),
            c: C(("daytime", 0.62), ("deep_night", -0.30)),
            dwell: 300, cooldown: 720),
        Def("rest.sleep.side", 0.10, passive: true,
            t: T((TemperamentDimension.Playful, -0.18)),
            r: R((RuntimeDimension.Fatigue, 1.62), (RuntimeDimension.Arousal, -0.70), (RuntimeDimension.Stress, -0.20), (RuntimeDimension.Safety, 0.55)),
            c: C(("daytime", 0.66), ("deep_night", -0.25)),
            dwell: 360, cooldown: 720),
        Def("play.roll", 0.02,
            t: T((TemperamentDimension.Playful, 1.20)),
            r: R((RuntimeDimension.PlayDesire, 1.30), (RuntimeDimension.Arousal, 0.35), (RuntimeDimension.Fatigue, -1.20), (RuntimeDimension.Stress, -1.10)),
            cooldown: 75),
        Def("play.tail_chase", -0.02, high: true,
            t: T((TemperamentDimension.Playful, 1.15), (TemperamentDimension.Mischievous, 0.55)),
            r: R((RuntimeDimension.PlayDesire, 1.15), (RuntimeDimension.Arousal, 0.50), (RuntimeDimension.Fatigue, -1.25), (RuntimeDimension.Stress, -1.05)),
            cooldown: 100),
        Def("play.pounce", -0.04, high: true,
            t: T((TemperamentDimension.Playful, 0.95), (TemperamentDimension.Mischievous, 0.85)),
            r: R((RuntimeDimension.PlayDesire, 1.25), (RuntimeDimension.Curiosity, 0.50), (RuntimeDimension.Fatigue, -1.10), (RuntimeDimension.Stress, -1.15)),
            cooldown: 95),
        Def("play.accept_toy", -0.10, high: true,
            t: T((TemperamentDimension.Playful, 1.20)),
            r: R((RuntimeDimension.PlayDesire, 1.25), (RuntimeDimension.Fatigue, -1.15), (RuntimeDimension.Stress, -1.20)),
            c: C(("toy_available", 1.8)), required: C(("toy_available", 0.2)), cooldown: 80),
        Def("play.laser.wiggle_chase", -0.08, high: true,
            t: T((TemperamentDimension.Playful, 1.25), (TemperamentDimension.Mischievous, 0.25)),
            r: R((RuntimeDimension.PlayDesire, 1.35), (RuntimeDimension.Arousal, 0.35), (RuntimeDimension.Fatigue, -1.15), (RuntimeDimension.Stress, -1.10)),
            c: C(("laser_available", 1.95)), required: C(("laser_available", 0.2)), dwell: 30, cooldown: 90),
        Def("play.laser.paw", -0.05, high: true,
            t: T((TemperamentDimension.Playful, 0.95), (TemperamentDimension.Independent, 0.12)),
            r: R((RuntimeDimension.PlayDesire, 1.15), (RuntimeDimension.Curiosity, 0.75), (RuntimeDimension.Fatigue, -0.95), (RuntimeDimension.Stress, -1.05)),
            c: C(("laser_available", 1.80)), required: C(("laser_available", 0.2)), dwell: 25, cooldown: 75),
        Def("explore.short_walk", 0.02, movement: true,
            t: T((TemperamentDimension.Playful, 0.85), (TemperamentDimension.Independent, 0.45)),
            r: R((RuntimeDimension.Curiosity, 1.15), (RuntimeDimension.Fatigue, -1.05), (RuntimeDimension.Stress, -0.75)),
            dwell: 18, cooldown: 55),
        Def("explore.mouse_track", -0.12,
            t: T((TemperamentDimension.Playful, 1.15)),
            r: R((RuntimeDimension.Curiosity, 1.20), (RuntimeDimension.Fatigue, -0.75), (RuntimeDimension.Stress, -0.65)),
            c: C(("mouse_nearby", 1.4)), required: C(("mouse_nearby", 0.12)), dwell: 12, cooldown: 35),
        Def("magic.accio_broom", -0.04, interaction: "magic", high: true, movement: true,
            t: T((TemperamentDimension.Playful, 0.62), (TemperamentDimension.Mischievous, 0.78)),
            r: R((RuntimeDimension.Curiosity, 0.78), (RuntimeDimension.Safety, 0.38), (RuntimeDimension.Fatigue, -0.82), (RuntimeDimension.Stress, -1.10)),
            c: C(("daily_magic_available", 0.62)), required: C(("daily_magic_available", 0.5)), dwell: 60, cooldown: 3600,
            arbitrationSource: BehaviorArbitrationSource.ContinuousEffect,
            arbitrationPriority: BehaviorPriority.ContinuousEffect,
            interruptible: false),
        Def("magic.apparate", -0.02, interaction: "magic", high: true, movement: true,
            t: T((TemperamentDimension.Playful, 0.42), (TemperamentDimension.Mischievous, 0.96)),
            r: R((RuntimeDimension.Curiosity, 0.88), (RuntimeDimension.Safety, 0.36), (RuntimeDimension.Fatigue, -0.62), (RuntimeDimension.Stress, -1.08)),
            c: C(("daily_magic_available", 0.62)), required: C(("daily_magic_available", 0.5)), dwell: 18, cooldown: 3600,
            arbitrationSource: BehaviorArbitrationSource.ContinuousEffect,
            arbitrationPriority: BehaviorPriority.ContinuousEffect,
            interruptible: false),
        Def("magic.petrificus_totalus", -0.08, interaction: "magic", high: true,
            t: T((TemperamentDimension.Playful, 0.36), (TemperamentDimension.Mischievous, 0.82)),
            r: R((RuntimeDimension.Curiosity, 0.66), (RuntimeDimension.Safety, 0.52), (RuntimeDimension.Fatigue, -0.48), (RuntimeDimension.Stress, -1.12)),
            c: C(("daily_magic_available", 0.62)), required: C(("daily_magic_available", 0.5)), dwell: 45, cooldown: 3600,
            arbitrationSource: BehaviorArbitrationSource.ContinuousEffect,
            arbitrationPriority: BehaviorPriority.ContinuousEffect,
            interruptible: false),
        Def("magic.scourgify", -0.06, interaction: "magic", high: true, movement: true,
            t: T((TemperamentDimension.Independent, 0.20), (TemperamentDimension.Mischievous, 0.72)),
            r: R((RuntimeDimension.Curiosity, 0.72), (RuntimeDimension.Safety, 0.46), (RuntimeDimension.Fatigue, -0.68), (RuntimeDimension.Stress, -1.08)),
            c: C(("daily_magic_available", 0.62)), required: C(("daily_magic_available", 0.5)), dwell: 20, cooldown: 3600,
            arbitrationSource: BehaviorArbitrationSource.ContinuousEffect,
            arbitrationPriority: BehaviorPriority.ContinuousEffect,
            interruptible: false),
        Def("social.approach", 0.02, initiative: true,
            t: T((TemperamentDimension.Affectionate, 1.20), (TemperamentDimension.Independent, -0.50)),
            r: R((RuntimeDimension.SocialDesire, 1.35), (RuntimeDimension.Stress, -1.10)),
            rel: L((RelationshipDimension.Trust, 0.50), (RelationshipDimension.InitiativeAcceptance, 0.65)),
            cooldown: 240),
        Def("social.purr", -0.02, initiative: true,
            t: T((TemperamentDimension.Affectionate, 1.10)),
            r: R((RuntimeDimension.SocialDesire, 0.90), (RuntimeDimension.Safety, 0.65), (RuntimeDimension.Stress, -1.30)),
            rel: L((RelationshipDimension.Trust, 0.70)), cooldown: 180),
        Def("social.knead", -0.06, initiative: true,
            t: T((TemperamentDimension.Affectionate, 1.15)),
            r: R((RuntimeDimension.SocialDesire, 0.80), (RuntimeDimension.Safety, 0.55), (RuntimeDimension.Stress, -1.25)),
            rel: L((RelationshipDimension.Trust, 0.60)), cooldown: 220),
        Def("social.respond_call", -0.20,
            t: T((TemperamentDimension.Affectionate, 0.95)),
            r: R((RuntimeDimension.SocialDesire, 0.75), (RuntimeDimension.Stress, -1.0)),
            rel: L((RelationshipDimension.Trust, 0.45)),
            c: C(("owner_call", 2.2)), required: C(("owner_call", 0.2)), cooldown: 45),
        Def("social.ask_attention", -0.08, initiative: true,
            t: T((TemperamentDimension.Affectionate, 1.05), (TemperamentDimension.Independent, -0.90)),
            r: R((RuntimeDimension.SocialDesire, 1.15), (RuntimeDimension.Stress, -1.15)),
            rel: L((RelationshipDimension.InitiativeAcceptance, 0.85)),
            cooldown: 1200),
        Def("social.ask_play", -0.12, initiative: true,
            t: T((TemperamentDimension.Playful, 1.10), (TemperamentDimension.Affectionate, 0.25)),
            r: R((RuntimeDimension.PlayDesire, 1.25), (RuntimeDimension.Stress, -1.20), (RuntimeDimension.Fatigue, -0.90)),
            rel: L((RelationshipDimension.InitiativeAcceptance, 0.60)),
            cooldown: 1500),
        Def("social.ask_walk", -0.14, initiative: true,
            t: T((TemperamentDimension.Playful, 0.92), (TemperamentDimension.Affectionate, 0.38)),
            r: R((RuntimeDimension.PlayDesire, 1.02), (RuntimeDimension.Curiosity, 0.48),
                (RuntimeDimension.Stress, -1.15), (RuntimeDimension.Fatigue, -0.95)),
            rel: L((RelationshipDimension.InitiativeAcceptance, 0.72)),
            c: C(("daytime", 0.16)), dwell: 18, cooldown: 1800),
        Def("independent.patrol", 0.00, movement: true,
            t: T((TemperamentDimension.Independent, 1.25), (TemperamentDimension.Playful, 0.25)),
            r: R((RuntimeDimension.Curiosity, 0.85), (RuntimeDimension.Fatigue, -0.90), (RuntimeDimension.Stress, -0.35)),
            dwell: 20, cooldown: 70),
        Def("vigilance.observe", 0.02, passive: true,
            t: T((TemperamentDimension.Sensitive, 0.80)),
            r: R((RuntimeDimension.Stress, 0.65), (RuntimeDimension.Curiosity, 0.30), (RuntimeDimension.Safety, -0.20))),
        Def("vigilance.guard", -0.15,
            t: T((TemperamentDimension.Sensitive, 1.00)),
            r: R((RuntimeDimension.Stress, 1.15), (RuntimeDimension.Arousal, 0.55), (RuntimeDimension.Safety, -0.75)),
            cooldown: 60),
        Def("avoid.quiet_place", -0.18, movement: true,
            t: T((TemperamentDimension.Sensitive, 0.90)),
            r: R((RuntimeDimension.Stress, 1.45), (RuntimeDimension.Safety, -1.15)),
            dwell: 60, cooldown: 90),
        Def("mischief.bat_object", -0.08, high: true,
            t: T((TemperamentDimension.Mischievous, 1.45), (TemperamentDimension.Playful, 0.35)),
            r: R((RuntimeDimension.PlayDesire, 0.80), (RuntimeDimension.Curiosity, 0.55), (RuntimeDimension.Fatigue, -1.00), (RuntimeDimension.Stress, -1.20)),
            cooldown: 180),
        Def("mischief.hide", -0.05,
            t: T((TemperamentDimension.Mischievous, 1.20), (TemperamentDimension.Independent, 0.25)),
            r: R((RuntimeDimension.Curiosity, 0.70), (RuntimeDimension.Fatigue, -0.70), (RuntimeDimension.Stress, -0.70)),
            cooldown: 150),
        Def("mischief.detour", -0.04, movement: true,
            t: T((TemperamentDimension.Mischievous, 1.10), (TemperamentDimension.Playful, 0.40)),
            r: R((RuntimeDimension.Curiosity, 0.75), (RuntimeDimension.Fatigue, -0.85), (RuntimeDimension.Stress, -0.85)),
            dwell: 18, cooldown: 120)
    };

    private static IReadOnlyList<BehaviorDefinition> BuildTouchDefinitions() => new[]
    {
        Def("touch.enjoy", 0.10, interaction: "touch",
            t: T((TemperamentDimension.Affectionate, 1.05), (TemperamentDimension.Sensitive, -0.35)),
            r: R((RuntimeDimension.Stress, -1.35), (RuntimeDimension.Safety, 0.70)),
            rel: L((RelationshipDimension.Trust, 0.70), (RelationshipDimension.TouchAcceptance, 0.95)),
            c: C(("stroke", 0.75), ("touch", 0.35))),
        Def("touch.curiosity", 0.05, interaction: "touch",
            t: T((TemperamentDimension.Playful, 0.85)),
            r: R((RuntimeDimension.Curiosity, 1.0), (RuntimeDimension.Stress, -0.70)),
            c: C(("touch", 0.55))),
        Def("touch.tolerate", 0.08, interaction: "touch",
            r: R((RuntimeDimension.Stress, -0.30)),
            rel: L((RelationshipDimension.TouchAcceptance, 0.35))),
        Def("touch.warning", -0.24, interaction: "touch",
            t: T((TemperamentDimension.Sensitive, 0.75)),
            r: R((RuntimeDimension.Stress, 1.35), (RuntimeDimension.Arousal, 0.45)),
            c: C(("boundary_pressure", 1.05), ("petting_load", 0.35)),
            required: C(("boundary_pressure", 0.25))),
        Def("touch.avoid", -0.38, interaction: "touch", movement: true,
            t: T((TemperamentDimension.Sensitive, 0.65)),
            r: R((RuntimeDimension.Stress, 1.45), (RuntimeDimension.Safety, -0.95)),
            c: C(("boundary_pressure", 0.85), ("escape_pressure", 0.65)),
            required: C(("boundary_pressure", 0.62))),
        Def("touch.run_away", -0.72, interaction: "touch", movement: true,
            t: T((TemperamentDimension.Sensitive, 0.55)),
            r: R((RuntimeDimension.Stress, 1.85), (RuntimeDimension.Arousal, 0.55), (RuntimeDimension.Safety, -1.10)),
            c: C(("escape_pressure", 1.35)),
            required: C(("escape_pressure", 0.92)), dwell: 20, cooldown: 40)
    };

    private static BehaviorDefinition Def(
        string id,
        double baseWeight,
        string interaction = "autonomous",
        bool passive = false,
        bool high = false,
        bool movement = false,
        bool initiative = false,
        Dictionary<TemperamentDimension, double>? t = null,
        Dictionary<RuntimeDimension, double>? r = null,
        Dictionary<RelationshipDimension, double>? rel = null,
        Dictionary<string, double>? c = null,
        Dictionary<string, double>? required = null,
        int dwell = 45,
        int cooldown = 30,
        BehaviorArbitrationSource? arbitrationSource = null,
        BehaviorPriority? arbitrationPriority = null,
        bool? interruptible = null) => new()
    {
        BehaviorId = id,
        InteractionType = interaction,
        BaseWeight = baseWeight,
        TemperamentAffinity = t ?? new(),
        RuntimeFit = r ?? new(),
        RelationshipFit = rel ?? new(),
        ContextAffinity = c ?? new(StringComparer.OrdinalIgnoreCase),
        RequiredSignals = required ?? new(StringComparer.OrdinalIgnoreCase),
        MinimumDwell = TimeSpan.FromSeconds(dwell),
        Cooldown = TimeSpan.FromSeconds(cooldown),
        IsPassive = passive,
        IsHighDisruption = high,
        RequiresMovement = movement,
        IsOwnerInitiative = initiative,
        ArbitrationSource = arbitrationSource,
        ArbitrationPriority = arbitrationPriority,
        Interruptible = interruptible
    };

    private static Dictionary<TemperamentDimension, double> T(
        params (TemperamentDimension, double)[] pairs) => pairs.ToDictionary(x => x.Item1, x => x.Item2);
    private static Dictionary<RuntimeDimension, double> R(
        params (RuntimeDimension, double)[] pairs) => pairs.ToDictionary(x => x.Item1, x => x.Item2);
    private static Dictionary<RelationshipDimension, double> L(
        params (RelationshipDimension, double)[] pairs) => pairs.ToDictionary(x => x.Item1, x => x.Item2);
    private static Dictionary<string, double> C(
        params (string, double)[] pairs) => pairs.ToDictionary(x => x.Item1, x => x.Item2, StringComparer.OrdinalIgnoreCase);
}

public sealed record GroomingOutcome(
    double HappinessDelta,
    double CleanlinessDelta,
    double StressDelta,
    double Acceptance,
    string Explanation);

public sealed class ContextualInteractionEvaluator
{
    public GroomingOutcome EvaluateGrooming(
        PersonalityBehaviorState state,
        string context,
        DateTimeOffset now)
    {
        var keys = new[]
        {
            PreferenceKey.Create("care.groom", "groom", context),
            PreferenceKey.Create("care.groom", "groom", "general")
        };
        var preference = keys
            .Where(state.LearnedPreferences.ContainsKey)
            .Select(x => state.LearnedPreferences[x].EffectiveWeight(now))
            .DefaultIfEmpty(0)
            .Average();
        var acceptance = Math.Clamp(
            0.38
            + state.Relationship.Trust * 0.26
            + state.Relationship.TouchAcceptance * 0.22
            + preference * 0.40
            - state.Temperament.Sensitive * 0.18
            - state.Runtime.Stress * 0.42,
            0.05,
            0.95);
        var happiness = -1.0 + acceptance * 6.0;
        var stress = (0.50 - acceptance) * 0.09 + state.Temperament.Sensitive * 0.015;
        return new GroomingOutcome(
            happiness,
            10,
            stress,
            acceptance,
            $"sensitive={state.Temperament.Sensitive:0.00}, stress={state.Runtime.Stress:0.00}, " +
            $"trust={state.Relationship.Trust:0.00}, groom_preference={preference:0.00}, acceptance={acceptance:0.00}");
    }
}
