namespace Pupu.Behavior;

public sealed class LegacyPersonalityData
{
    public TemperamentBaseline Baseline { get; set; } = new();
    public Dictionary<string, double> LearnedTemperamentDeltas { get; set; } = new();
    public Dictionary<string, double> BehaviorWeights { get; set; } = new();
    public double Trust { get; set; } = 0.5;
}

public sealed class PersonalityBehaviorMigrator
{
    private static readonly Dictionary<string, (string BehaviorId, string InteractionType)> SafeMappings =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["purr"] = ("social.purr", "touch"),
            ["curious_touch"] = ("touch.curiosity", "touch"),
            ["gentle_touch"] = ("touch.enjoy", "touch"),
            ["annoyed_touch"] = ("touch.warning", "touch"),
            ["angry_escape"] = ("touch.run_away", "touch"),
            ["overpet"] = ("touch.warning", "touch"),
            ["walk"] = ("explore.short_walk", "autonomous"),
            ["walk_harnessed"] = ("explore.short_walk", "walk"),
            ["walk_free"] = ("independent.patrol", "walk"),
            ["attention"] = ("social.ask_attention", "autonomous"),
            ["mischief"] = ("mischief.bat_object", "autonomous"),
            ["groom"] = ("care.groom", "groom"),
            ["play_wand"] = ("play.accept_toy", "play")
        };

    public PersonalityBehaviorState Migrate(
        PersonalityBehaviorState? existing,
        LegacyPersonalityData legacy,
        DateTimeOffset now)
    {
        var result = existing ?? PersonalityBehaviorState.SafeCompanionDefault();
        if (!result.AppliedMigrations.Contains(PersonalityBehaviorSchema.LegacyMigrationId))
        {
            result.Temperament = legacy.Baseline.Clone();
            result.Relationship.Trust = Math.Clamp(legacy.Trust, 0, 1);
            result.LegacyLearningSnapshot ??= new LegacyLearningSnapshot
            {
                CapturedAt = now,
                LearnedTemperamentDeltas = new Dictionary<string, double>(
                    legacy.LearnedTemperamentDeltas,
                    StringComparer.OrdinalIgnoreCase),
                LegacyBehaviorWeights = new Dictionary<string, double>(
                    legacy.BehaviorWeights,
                    StringComparer.OrdinalIgnoreCase)
            };

            foreach (var pair in legacy.BehaviorWeights)
            {
                if (!SafeMappings.TryGetValue(pair.Key, out var mapping))
                {
                    result.LegacyLearningSnapshot.UnmappedBehaviorWeights[pair.Key] = pair.Value;
                    continue;
                }

                var preference = new LearnedPreference
                {
                    BehaviorId = mapping.BehaviorId,
                    InteractionType = mapping.InteractionType,
                    Context = "legacy_migration",
                    Weight = Math.Clamp(pair.Value, -0.18, 0.18),
                    Confidence = 0.20,
                    CreatedAt = now,
                    UpdatedAt = now,
                    LastReinforcedAt = now,
                    DecayHalfLifeDays = 30
                };
                result.LearnedPreferences.TryAdd(preference.Key, preference);
            }

            result.AppliedMigrations.Add(PersonalityBehaviorSchema.LegacyMigrationId);
        }

        if (!result.AppliedMigrations.Contains(PersonalityBehaviorSchema.MemoryLayersMigrationId))
        {
            foreach (var evidence in result.PreferenceEvidence.Where(x => !x.IsDeleted))
            {
                var raw = new RawInteractionEvent
                {
                    Id = evidence.Id,
                    SessionId = evidence.SessionId ?? evidence.Id,
                    At = evidence.At,
                    BehaviorId = evidence.BehaviorId,
                    InteractionType = evidence.InteractionType,
                    Context = evidence.Context,
                    Signal = evidence.Signal,
                    Opportunity = evidence.Opportunity,
                    OutcomeQuality = evidence.OutcomeQuality,
                    Source = evidence.Source
                };
                if (result.RawInteractionEvents.All(x => x.Id != raw.Id))
                    result.RawInteractionEvents.Add(raw);
            }
            foreach (var habit in result.HabitMemories)
            {
                var key = PreferenceKey.Create(habit.BehaviorId, habit.InteractionType, habit.Context);
                result.DerivedHabitPreferences.TryAdd(key, new DerivedHabitPreference
                {
                    BehaviorId = habit.BehaviorId,
                    InteractionType = habit.InteractionType,
                    Context = habit.Context,
                    Weight = Math.Clamp(habit.LearnedWeight, -0.24, 0.24),
                    Confidence = 0.45,
                    SampleCount = habit.SampleCount,
                    OpportunityCount = habit.SampleCount,
                    DistinctDays = habit.DistinctDays,
                    ContextConsistency = 0.70,
                    UpdatedAt = now
                });
            }
            result.AppliedMigrations.Add(PersonalityBehaviorSchema.MemoryLayersMigrationId);
        }

        result.SchemaVersion = PersonalityBehaviorSchema.CurrentVersion;
        result.Clamp();
        return result;
    }
}
