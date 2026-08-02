using System.Text.Json.Serialization;

namespace Pupu.Behavior;

public static class PersonalityBehaviorSchema
{
    public const int CurrentVersion = 3;
    public const string LegacyMigrationId = "legacy-v1-to-personality-behavior-v2";
    public const string MemoryLayersMigrationId = "personality-behavior-v2-to-memory-layers-v3";
}

public sealed class TemperamentBaseline
{
    public double Playful { get; set; } = 0.82;
    public double Affectionate { get; set; } = 0.78;
    public double Sensitive { get; set; } = 0.68;
    public double Independent { get; set; } = 0.34;
    public double Mischievous { get; set; } = 0.58;

    public TemperamentBaseline Clone() => new()
    {
        Playful = Playful,
        Affectionate = Affectionate,
        Sensitive = Sensitive,
        Independent = Independent,
        Mischievous = Mischievous
    };

    public void Clamp()
    {
        Playful = Math.Clamp(Playful, 0, 1);
        Affectionate = Math.Clamp(Affectionate, 0, 1);
        Sensitive = Math.Clamp(Sensitive, 0, 1);
        Independent = Math.Clamp(Independent, 0, 1);
        Mischievous = Math.Clamp(Mischievous, 0, 1);
    }
}

public sealed class RuntimeState
{
    public double Arousal { get; set; } = 0.52;
    public double Stress { get; set; } = 0.12;
    public double SocialDesire { get; set; } = 0.56;
    public double PlayDesire { get; set; } = 0.62;
    public double Curiosity { get; set; } = 0.64;
    public double Fatigue { get; set; } = 0.24;
    public double Safety { get; set; } = 0.78;
    public DateTimeOffset LastUpdatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset LastActiveAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? SuspendedAt { get; set; }

    public void Clamp()
    {
        Arousal = Math.Clamp(Arousal, 0, 1);
        Stress = Math.Clamp(Stress, 0, 1);
        SocialDesire = Math.Clamp(SocialDesire, 0, 1);
        PlayDesire = Math.Clamp(PlayDesire, 0, 1);
        Curiosity = Math.Clamp(Curiosity, 0, 1);
        Fatigue = Math.Clamp(Fatigue, 0, 1);
        Safety = Math.Clamp(Safety, 0, 1);
    }

    // Compatibility wrapper. New code uses RuntimeStateDynamics so
    // temperament, bounded coupling and resume rules are applied together.
    public void AdvanceActiveTime(TimeSpan elapsed, bool deepNight)
    {
        var minutes = Math.Clamp(elapsed.TotalMinutes, 0, 5);
        Stress -= minutes * (deepNight ? 0.006 : 0.004);
        Fatigue += minutes * (deepNight ? 0.004 : 0.0015);
        Arousal += (deepNight ? -0.004 : 0.001) * minutes;
        SocialDesire += (0.52 - SocialDesire) * Math.Min(0.08, minutes * 0.01);
        PlayDesire += (0.56 - PlayDesire) * Math.Min(0.08, minutes * 0.008);
        Safety += (0.78 - Safety) * Math.Min(0.06, minutes * 0.008);
        LastUpdatedAt = LastUpdatedAt.Add(elapsed);
        LastActiveAt = LastUpdatedAt;
        Clamp();
    }
}

public sealed class RelationshipState
{
    public double Trust { get; set; } = 0.50;
    public double Familiarity { get; set; } = 0.28;
    public double TouchAcceptance { get; set; } = 0.50;
    public double InitiativeAcceptance { get; set; } = 0.50;

    public void Clamp()
    {
        Trust = Math.Clamp(Trust, 0, 1);
        Familiarity = Math.Clamp(Familiarity, 0, 1);
        TouchAcceptance = Math.Clamp(TouchAcceptance, 0, 1);
        InitiativeAcceptance = Math.Clamp(InitiativeAcceptance, 0, 1);
    }
}

public sealed class DailyRelationshipChange
{
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public double Trust { get; set; }
    public double Familiarity { get; set; }
    public double TouchAcceptance { get; set; }
    public double InitiativeAcceptance { get; set; }
}

public sealed class LearnedPreference
{
    public string BehaviorId { get; set; } = "idle.side_lie";
    public string InteractionType { get; set; } = "autonomous";
    public string Context { get; set; } = "general";
    public double Weight { get; set; }
    public double Confidence { get; set; }
    public int EvidenceCount { get; set; }
    public List<DateOnly> EvidenceDates { get; set; } = new();
    public bool IsHabitMemory { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset LastReinforcedAt { get; set; } = DateTimeOffset.Now;
    public int DecayHalfLifeDays { get; set; } = 60;

    [JsonIgnore]
    public string Key => PreferenceKey.Create(BehaviorId, InteractionType, Context);

    public double EffectiveWeight(DateTimeOffset now)
    {
        var days = Math.Max(0, (now - LastReinforcedAt).TotalDays);
        var decay = Math.Pow(0.5, days / Math.Max(7, DecayHalfLifeDays));
        return Math.Clamp(Weight * decay, -0.65, 0.65);
    }

    public void Clamp()
    {
        Weight = Math.Clamp(Weight, -0.65, 0.65);
        Confidence = Math.Clamp(Confidence, 0, 1);
        EvidenceCount = Math.Max(0, EvidenceCount);
        EvidenceDates = EvidenceDates.Distinct().Order().TakeLast(30).ToList();
        DecayHalfLifeDays = Math.Clamp(DecayHalfLifeDays, 14, 365);
    }
}

public static class PreferenceKey
{
    public static string Create(string behaviorId, string interactionType, string context) =>
        $"{Normalize(behaviorId)}|{Normalize(interactionType)}|{Normalize(context)}";

    public static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? "general" : value.Trim().ToLowerInvariant();
}

public sealed class PreferenceEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;
    public string BehaviorId { get; set; } = "idle.side_lie";
    public string InteractionType { get; set; } = "autonomous";
    public string Context { get; set; } = "general";
    public double Signal { get; set; }
    public string Source { get; set; } = "interaction";
    public Guid? SessionId { get; set; }
    public double Opportunity { get; set; } = 1;
    public double OutcomeQuality { get; set; } = 0.5;
    public bool IsDeleted { get; set; }
}

public sealed class HabitMemory
{
    public string BehaviorId { get; set; } = "idle.side_lie";
    public string InteractionType { get; set; } = "autonomous";
    public string Context { get; set; } = "general";
    public int SampleCount { get; set; }
    public int DistinctDays { get; set; }
    public double LearnedWeight { get; set; }
    public DateTimeOffset FormedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class ConfirmedProfileFact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset ConfirmedAt { get; set; } = DateTimeOffset.Now;
    public string Source { get; set; } = "owner";
    public bool IsDeleted { get; set; }
}

public sealed class RawInteractionEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;
    public string BehaviorId { get; set; } = "unknown";
    public string InteractionType { get; set; } = "unknown";
    public string Context { get; set; } = "general";
    public double Signal { get; set; }
    public double Opportunity { get; set; } = 1;
    public double OutcomeQuality { get; set; } = 0.5;
    public string Source { get; set; } = "interaction";
    public bool IsDeleted { get; set; }
}

public sealed class EpisodicMemory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public List<Guid> EvidenceIds { get; set; } = new();
    public string BehaviorId { get; set; } = "unknown";
    public string InteractionType { get; set; } = "unknown";
    public string Context { get; set; } = "general";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset EndedAt { get; set; } = DateTimeOffset.Now;
    public double OutcomeQuality { get; set; } = 0.5;
    public bool IsPinned { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class DerivedHabitPreference
{
    public string BehaviorId { get; set; } = "unknown";
    public string InteractionType { get; set; } = "unknown";
    public string Context { get; set; } = "general";
    public double Weight { get; set; }
    public double Confidence { get; set; }
    public int SampleCount { get; set; }
    public int OpportunityCount { get; set; }
    public int DistinctDays { get; set; }
    public double ContextConsistency { get; set; }
    public int ContradictorySamples { get; set; }
    public List<Guid> EvidenceIds { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public bool IsPinned { get; set; }

    [JsonIgnore]
    public string Key => PreferenceKey.Create(BehaviorId, InteractionType, Context);

    public double EffectiveWeight =>
        Math.Clamp(Weight * Math.Clamp(Confidence, 0, 1), -0.42, 0.42);
}

public sealed class LegacyLearningSnapshot
{
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.Now;
    public Dictionary<string, double> LearnedTemperamentDeltas { get; set; } = new();
    public Dictionary<string, double> LegacyBehaviorWeights { get; set; } = new();
    public Dictionary<string, double> UnmappedBehaviorWeights { get; set; } = new();
    public string Note { get; set; } =
        "仅供查看。旧自动性格偏移不参与天生性格；无法安全映射的权重不参与运行决策。";
}

public sealed class PersonalityBehaviorState
{
    public int SchemaVersion { get; set; } = PersonalityBehaviorSchema.CurrentVersion;
    public TemperamentBaseline Temperament { get; set; } = new();
    public RuntimeState Runtime { get; set; } = new();
    public RelationshipState Relationship { get; set; } = new();
    public Dictionary<string, LearnedPreference> LearnedPreferences { get; set; } = new();
    public List<PreferenceEvidence> PreferenceEvidence { get; set; } = new();
    public List<HabitMemory> HabitMemories { get; set; } = new();
    public List<ConfirmedProfileFact> ConfirmedProfileFacts { get; set; } = new();
    public List<RawInteractionEvent> RawInteractionEvents { get; set; } = new();
    public List<EpisodicMemory> EpisodicMemories { get; set; } = new();
    public Dictionary<string, DerivedHabitPreference> DerivedHabitPreferences { get; set; } = new();
    public HashSet<Guid> DeletedEvidenceIds { get; set; } = new();
    public LegacyLearningSnapshot? LegacyLearningSnapshot { get; set; }
    public List<string> AppliedMigrations { get; set; } = new();
    public DailyRelationshipChange DailyRelationshipChange { get; set; } = new();
    public int RecentOvertouchCount { get; set; }
    public DateTimeOffset LastOvertouchAt { get; set; } = DateTimeOffset.MinValue;

    public void Clamp()
    {
        SchemaVersion = PersonalityBehaviorSchema.CurrentVersion;
        Temperament.Clamp();
        Runtime.Clamp();
        Relationship.Clamp();
        foreach (var preference in LearnedPreferences.Values) preference.Clamp();
        PreferenceEvidence = PreferenceEvidence
            .OrderBy(x => x.At)
            .TakeLast(600)
            .ToList();
        HabitMemories = HabitMemories
            .GroupBy(x => PreferenceKey.Create(x.BehaviorId, x.InteractionType, x.Context))
            .Select(x => x.OrderByDescending(v => v.UpdatedAt).First())
            .TakeLast(120)
            .ToList();
        ConfirmedProfileFacts = ConfirmedProfileFacts
            .Where(x => !x.IsDeleted)
            .GroupBy(x => PreferenceKey.Normalize(x.Key))
            .Select(x => x.OrderByDescending(v => v.ConfirmedAt).First())
            .TakeLast(160)
            .ToList();
        RawInteractionEvents = RawInteractionEvents
            .Where(x => !x.IsDeleted && !DeletedEvidenceIds.Contains(x.Id))
            .OrderBy(x => x.At)
            .TakeLast(1600)
            .ToList();
        EpisodicMemories = EpisodicMemories
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.EndedAt)
            .OrderBy(x => x.IsPinned ? 1 : 0)
            .TakeLast(240)
            .ToList();
        DerivedHabitPreferences = DerivedHabitPreferences.Values
            .GroupBy(x => x.Key)
            .Select(x => x.OrderByDescending(v => v.UpdatedAt).First())
            .TakeLast(160)
            .ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        DeletedEvidenceIds = DeletedEvidenceIds.TakeLast(2000).ToHashSet();
        RecentOvertouchCount = Math.Clamp(RecentOvertouchCount, 0, 20);
    }

    /// <summary>
    /// Creates the bounded, caller-owned state used by behavior scoring.
    /// Persistence-only evidence and editable memory are deliberately omitted,
    /// so the Agent cannot mutate the live memory store while making a choice.
    /// </summary>
    public PersonalityBehaviorState CreateDecisionSnapshot() => new()
    {
        SchemaVersion = SchemaVersion,
        Temperament = Temperament.Clone(),
        Runtime = new RuntimeState
        {
            Arousal = Runtime.Arousal,
            Stress = Runtime.Stress,
            SocialDesire = Runtime.SocialDesire,
            PlayDesire = Runtime.PlayDesire,
            Curiosity = Runtime.Curiosity,
            Fatigue = Runtime.Fatigue,
            Safety = Runtime.Safety,
            LastUpdatedAt = Runtime.LastUpdatedAt,
            LastActiveAt = Runtime.LastActiveAt,
            SuspendedAt = Runtime.SuspendedAt
        },
        Relationship = new RelationshipState
        {
            Trust = Relationship.Trust,
            Familiarity = Relationship.Familiarity,
            TouchAcceptance = Relationship.TouchAcceptance,
            InitiativeAcceptance = Relationship.InitiativeAcceptance
        },
        LearnedPreferences = LearnedPreferences.ToDictionary(
            pair => pair.Key,
            pair => new LearnedPreference
            {
                BehaviorId = pair.Value.BehaviorId,
                InteractionType = pair.Value.InteractionType,
                Context = pair.Value.Context,
                Weight = pair.Value.Weight,
                Confidence = pair.Value.Confidence,
                EvidenceCount = pair.Value.EvidenceCount,
                EvidenceDates = pair.Value.EvidenceDates.ToList(),
                IsHabitMemory = pair.Value.IsHabitMemory,
                CreatedAt = pair.Value.CreatedAt,
                UpdatedAt = pair.Value.UpdatedAt,
                LastReinforcedAt = pair.Value.LastReinforcedAt,
                DecayHalfLifeDays = pair.Value.DecayHalfLifeDays
            },
            StringComparer.OrdinalIgnoreCase),
        DerivedHabitPreferences = DerivedHabitPreferences.ToDictionary(
            pair => pair.Key,
            pair => new DerivedHabitPreference
            {
                BehaviorId = pair.Value.BehaviorId,
                InteractionType = pair.Value.InteractionType,
                Context = pair.Value.Context,
                Weight = pair.Value.Weight,
                Confidence = pair.Value.Confidence,
                SampleCount = pair.Value.SampleCount,
                OpportunityCount = pair.Value.OpportunityCount,
                DistinctDays = pair.Value.DistinctDays,
                ContextConsistency = pair.Value.ContextConsistency,
                ContradictorySamples = pair.Value.ContradictorySamples,
                EvidenceIds = pair.Value.EvidenceIds.ToList(),
                UpdatedAt = pair.Value.UpdatedAt,
                IsPinned = pair.Value.IsPinned
            },
            StringComparer.OrdinalIgnoreCase)
    };

    public static PersonalityBehaviorState SafeCompanionDefault() => new()
    {
        Runtime = new RuntimeState
        {
            Arousal = 0.38,
            Stress = 0.08,
            SocialDesire = 0.48,
            PlayDesire = 0.42,
            Curiosity = 0.48,
            Fatigue = 0.28,
            Safety = 0.85
        },
        Relationship = new RelationshipState()
    };
}

public sealed record AppliedEffect(string Name, double Delta, string Unit = "normalized");

public enum InteractionLifecycleStage
{
    InteractionStarted,
    InteractionProgressed,
    InteractionCompleted,
    InteractionInterrupted,
    InteractionFailed
}

public sealed class InteractionRecord
{
    public Guid InteractionId { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;
    public InteractionLifecycleStage Stage { get; set; }
    public string BehaviorId { get; set; } = "unknown";
    public string InteractionType { get; set; } = "unknown";
    public string Context { get; set; } = "general";
    public string AnimationSource { get; set; } = string.Empty;
    public double CompletionRatio { get; set; }
    public string? InterruptReason { get; set; }
    public string? FailureReason { get; set; }
    public List<AppliedEffect> AppliedEffects { get; set; } = new();
}
