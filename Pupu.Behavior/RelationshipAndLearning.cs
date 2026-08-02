namespace Pupu.Behavior;

public sealed class RelationshipUpdater
{
    public const double DailyPerDimensionCap = 0.035;

    public void Apply(
        PersonalityBehaviorState state,
        DateTimeOffset now,
        double trust = 0,
        double familiarity = 0,
        double touchAcceptance = 0,
        double initiativeAcceptance = 0)
    {
        var date = DateOnly.FromDateTime(now.LocalDateTime.Date);
        if (state.DailyRelationshipChange.Date != date)
            state.DailyRelationshipChange = new DailyRelationshipChange { Date = date };

        state.Relationship.Trust += BoundedDelta(
            state.DailyRelationshipChange.Trust, trust, out var usedTrust);
        state.DailyRelationshipChange.Trust = usedTrust;
        state.Relationship.Familiarity += BoundedDelta(
            state.DailyRelationshipChange.Familiarity, familiarity, out var usedFamiliarity);
        state.DailyRelationshipChange.Familiarity = usedFamiliarity;
        state.Relationship.TouchAcceptance += BoundedDelta(
            state.DailyRelationshipChange.TouchAcceptance, touchAcceptance, out var usedTouch);
        state.DailyRelationshipChange.TouchAcceptance = usedTouch;
        state.Relationship.InitiativeAcceptance += BoundedDelta(
            state.DailyRelationshipChange.InitiativeAcceptance, initiativeAcceptance, out var usedInitiative);
        state.DailyRelationshipChange.InitiativeAcceptance = usedInitiative;
        state.Relationship.Clamp();
    }

    private static double BoundedDelta(double used, double requested, out double newUsed)
    {
        var lower = -DailyPerDimensionCap - used;
        var upper = DailyPerDimensionCap - used;
        var applied = Math.Clamp(requested, lower, upper);
        newUsed = used + applied;
        return applied;
    }
}

public sealed class PreferenceLearningEngine
{
    public const int MinimumHabitSamples = 6;
    public const int MinimumHabitDays = 3;

    public void Observe(
        PersonalityBehaviorState state,
        string behaviorId,
        string interactionType,
        string context,
        double signal,
        DateTimeOffset at,
        string source = "interaction")
    {
        state.PreferenceEvidence.Add(new PreferenceEvidence
        {
            At = at,
            BehaviorId = behaviorId,
            InteractionType = interactionType,
            Context = context,
            Signal = Math.Clamp(signal, -1, 1),
            Source = source
        });
        state.PreferenceEvidence = state.PreferenceEvidence.OrderBy(x => x.At).TakeLast(600).ToList();
        ConsolidateKey(state, behaviorId, interactionType, context, at);
    }

    public LearnedPreference Correct(
        PersonalityBehaviorState state,
        string behaviorId,
        string interactionType,
        string context,
        int feedback,
        DateTimeOffset at)
    {
        feedback = Math.Sign(feedback);
        var preference = GetOrCreate(state, behaviorId, interactionType, context, at);
        preference.Weight = Math.Clamp(preference.EffectiveWeight(at) + feedback * 0.12, -0.65, 0.65);
        preference.Confidence = Math.Clamp(preference.Confidence + 0.18, 0, 1);
        preference.EvidenceCount++;
        preference.EvidenceDates.Add(DateOnly.FromDateTime(at.LocalDateTime.Date));
        preference.EvidenceDates = preference.EvidenceDates.Distinct().Order().TakeLast(30).ToList();
        preference.UpdatedAt = at;
        preference.LastReinforcedAt = at;
        preference.Clamp();
        Observe(state, behaviorId, interactionType, context, feedback, at, "owner_feedback");
        return preference;
    }

    public void UndoCorrection(
        PersonalityBehaviorState state,
        string behaviorId,
        string interactionType,
        string context,
        int feedback,
        DateTimeOffset at)
    {
        var key = PreferenceKey.Create(behaviorId, interactionType, context);
        if (!state.LearnedPreferences.TryGetValue(key, out var preference)) return;
        preference.Weight = Math.Clamp(
            preference.EffectiveWeight(at) - Math.Sign(feedback) * 0.12,
            -0.65,
            0.65);
        preference.Confidence = Math.Max(0, preference.Confidence - 0.12);
        preference.UpdatedAt = at;
        preference.LastReinforcedAt = at;
    }

    public void ConsolidateAll(PersonalityBehaviorState state, DateTimeOffset now)
    {
        foreach (var group in state.PreferenceEvidence.GroupBy(x =>
                     PreferenceKey.Create(x.BehaviorId, x.InteractionType, x.Context)))
        {
            var last = group.OrderByDescending(x => x.At).First();
            ConsolidateKey(state, last.BehaviorId, last.InteractionType, last.Context, now);
        }
    }

    private void ConsolidateKey(
        PersonalityBehaviorState state,
        string behaviorId,
        string interactionType,
        string context,
        DateTimeOffset now)
    {
        var key = PreferenceKey.Create(behaviorId, interactionType, context);
        var evidence = state.PreferenceEvidence
            .Where(x => PreferenceKey.Create(x.BehaviorId, x.InteractionType, x.Context) == key)
            .OrderBy(x => x.At)
            .ToList();
        var distinctDates = evidence
            .Select(x => DateOnly.FromDateTime(x.At.LocalDateTime.Date))
            .Distinct()
            .Order()
            .ToList();

        // A single day may shape short-term state, but it cannot form a
        // persistent habit no matter how many clicks happened that day.
        if (evidence.Count < MinimumHabitSamples || distinctDates.Count < MinimumHabitDays)
            return;

        var target = Math.Clamp(evidence.Average(x => x.Signal) * 0.45, -0.45, 0.45);
        var preference = GetOrCreate(state, behaviorId, interactionType, context, now);
        var current = preference.EffectiveWeight(now);
        // Contradictory evidence corrects slowly and cannot flip an established
        // preference in one consolidation.
        var alpha = Math.Sign(current) != 0 && Math.Sign(target) != Math.Sign(current) ? 0.08 : 0.16;
        preference.Weight = Math.Clamp(current * (1 - alpha) + target * alpha, -0.65, 0.65);
        preference.Confidence = Math.Clamp(
            0.25 + Math.Min(0.55, distinctDates.Count * 0.07) + Math.Min(0.2, evidence.Count * 0.01),
            0,
            1);
        preference.EvidenceCount = evidence.Count;
        preference.EvidenceDates = distinctDates.TakeLast(30).ToList();
        preference.IsHabitMemory = true;
        preference.UpdatedAt = now;
        preference.LastReinforcedAt = evidence.Max(x => x.At);
        preference.Clamp();

        var habit = state.HabitMemories.FirstOrDefault(x =>
            PreferenceKey.Create(x.BehaviorId, x.InteractionType, x.Context) == key);
        if (habit is null)
        {
            habit = new HabitMemory
            {
                BehaviorId = behaviorId,
                InteractionType = interactionType,
                Context = context,
                FormedAt = now
            };
            state.HabitMemories.Add(habit);
        }
        habit.SampleCount = evidence.Count;
        habit.DistinctDays = distinctDates.Count;
        habit.LearnedWeight = preference.Weight;
        habit.UpdatedAt = now;
    }

    private static LearnedPreference GetOrCreate(
        PersonalityBehaviorState state,
        string behaviorId,
        string interactionType,
        string context,
        DateTimeOffset now)
    {
        var key = PreferenceKey.Create(behaviorId, interactionType, context);
        if (state.LearnedPreferences.TryGetValue(key, out var existing)) return existing;
        var created = new LearnedPreference
        {
            BehaviorId = behaviorId,
            InteractionType = interactionType,
            Context = context,
            CreatedAt = now,
            UpdatedAt = now,
            LastReinforcedAt = now
        };
        state.LearnedPreferences[key] = created;
        return created;
    }
}
