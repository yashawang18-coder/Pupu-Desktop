using System.Globalization;

namespace Pupu.Behavior;

public enum OwnerInteractionKind
{
    Feeding,
    Walk,
    CleanLitter,
    Grooming,
    WandPlay,
    LaserPlay,
    PosePlay,
    Magic
}

public sealed record OwnerInteractionContext(
    double Fullness,
    double Energy,
    double Cleanliness,
    double LitterLevel,
    string ContextKey = "general");

public sealed record InteractionParticipationDecision(
    bool Accepted,
    double Probability,
    string ReasonCode);

public sealed class OwnerInteractionParticipationEvaluator
{
    public InteractionParticipationDecision Evaluate(
        PersonalityBehaviorState state,
        OwnerInteractionKind kind,
        OwnerInteractionContext context,
        double roll)
    {
        var temperament = state.Temperament;
        var runtime = state.Runtime;
        var relationship = state.Relationship;
        var energy = Math.Clamp(context.Energy / 100, 0, 1);
        var fullness = Math.Clamp(context.Fullness / 100, 0, 1);
        var preference = ReadPreference(state, kind, context.ContextKey);

        var probability = kind switch
        {
            OwnerInteractionKind.Feeding =>
                0.38
                + (1 - fullness) * 0.52
                + runtime.Safety * 0.12
                - runtime.Stress * 0.24
                - runtime.Fatigue * 0.08,
            OwnerInteractionKind.Walk =>
                0.18
                + energy * 0.28
                + runtime.PlayDesire * 0.22
                + runtime.Curiosity * 0.16
                + temperament.Playful * 0.12
                + temperament.Independent * 0.08
                - runtime.Fatigue * 0.30
                - runtime.Stress * 0.30,
            OwnerInteractionKind.CleanLitter =>
                0.54
                + Math.Clamp(context.LitterLevel / 100, 0, 1) * 0.20
                + (1 - Math.Clamp(context.Cleanliness / 100, 0, 1)) * 0.10
                + runtime.Safety * 0.08
                - runtime.Stress * 0.20,
            OwnerInteractionKind.Grooming =>
                0.24
                + relationship.Trust * 0.20
                + relationship.TouchAcceptance * 0.24
                + runtime.Safety * 0.16
                - temperament.Sensitive * 0.12
                - runtime.Stress * 0.34,
            OwnerInteractionKind.WandPlay or OwnerInteractionKind.LaserPlay =>
                0.18
                + energy * 0.18
                + runtime.PlayDesire * 0.30
                + temperament.Playful * 0.18
                + runtime.Curiosity * 0.10
                - runtime.Fatigue * 0.28
                - runtime.Stress * 0.32,
            OwnerInteractionKind.PosePlay =>
                0.52
                + runtime.Safety * 0.12
                + relationship.Trust * 0.10
                - runtime.Stress * 0.22
                - runtime.Fatigue * 0.08,
            OwnerInteractionKind.Magic =>
                0.48
                + temperament.Mischievous * 0.18
                + temperament.Playful * 0.12
                + runtime.Curiosity * 0.12
                + runtime.Safety * 0.08
                - runtime.Stress * 0.28
                - runtime.Fatigue * 0.16,
            _ => 0.50
        };
        probability = Math.Clamp(probability + preference * 0.35, 0.08, 0.96);
        var reason = RefusalReason(kind, context, runtime, probability);
        return new InteractionParticipationDecision(
            Math.Clamp(roll, 0, 1) <= probability,
            probability,
            reason);
    }

    private static double ReadPreference(
        PersonalityBehaviorState state,
        OwnerInteractionKind kind,
        string context)
    {
        var (behaviorIds, interactionType) = kind switch
        {
            OwnerInteractionKind.Feeding => (new[] { "care.feed" }, "feed"),
            OwnerInteractionKind.Walk => (new[] { "walk.harnessed" }, "walk"),
            OwnerInteractionKind.CleanLitter => (new[] { "care.clean_litter" }, "clean_litter"),
            OwnerInteractionKind.Grooming => (new[] { "care.groom" }, "groom"),
            OwnerInteractionKind.WandPlay => (new[] { "play.accept_toy" }, "wand"),
            OwnerInteractionKind.LaserPlay => (new[] { "play.laser.wiggle_chase" }, "laser"),
            OwnerInteractionKind.PosePlay => (new[] { "play.roll" }, "owner_play"),
            OwnerInteractionKind.Magic => (
                new[]
                {
                    "magic.accio_broom",
                    "magic.apparate",
                    "magic.petrificus_totalus",
                    "magic.scourgify"
                },
                "magic"),
            _ => (new[] { "interaction" }, "owner")
        };
        var keys = behaviorIds.SelectMany(behaviorId => new[]
            {
                PreferenceKey.Create(behaviorId, interactionType, context),
                PreferenceKey.Create(behaviorId, interactionType, "general")
            })
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return keys.Where(state.LearnedPreferences.ContainsKey)
            .Select(key => state.LearnedPreferences[key].EffectiveWeight(DateTimeOffset.Now))
            .DefaultIfEmpty(0)
            .Average();
    }

    private static string RefusalReason(
        OwnerInteractionKind kind,
        OwnerInteractionContext context,
        RuntimeState runtime,
        double probability)
    {
        if (runtime.Stress >= 0.64) return "need_space";
        if (runtime.Fatigue >= 0.72 &&
            (kind is OwnerInteractionKind.Walk
                or OwnerInteractionKind.WandPlay
                or OwnerInteractionKind.LaserPlay
                or OwnerInteractionKind.Magic))
            return "sleepy";
        if (kind == OwnerInteractionKind.Feeding && context.Fullness >= 88) return "full";
        if (kind == OwnerInteractionKind.Walk && context.Energy <= 24) return "low_energy";
        if ((kind is OwnerInteractionKind.WandPlay or OwnerInteractionKind.LaserPlay) &&
            runtime.PlayDesire <= 0.28)
            return "not_playful";
        if (kind == OwnerInteractionKind.Grooming) return "not_touching_now";
        if (kind == OwnerInteractionKind.CleanLitter &&
            context.LitterLevel <= 8 &&
            context.Cleanliness >= 88)
            return "no_need";
        return probability < 0.42 ? "not_now" : "chose_other";
    }
}

public enum SeasonalOccasion
{
    None,
    Christmas,
    Halloween,
    SpringFestival,
    OwnerBirthday
}

public static class DailySpecialRules
{
    public static SeasonalOccasion HolidayFor(DateTimeOffset now)
    {
        var date = now.Date;
        if (date.Month == 12 && date.Day == 25) return SeasonalOccasion.Christmas;
        if (date.Month == 10 && date.Day == 31) return SeasonalOccasion.Halloween;
        return IsSpringFestival(date)
            ? SeasonalOccasion.SpringFestival
            : SeasonalOccasion.None;
    }

    public static bool IsOwnerBirthday(DateTimeOffset now, DateTime? ownerBirthday) =>
        ownerBirthday is { } birthday &&
        now.Month == birthday.Month &&
        now.Day == birthday.Day;

    public static int? OwnerAgeOnBirthday(DateTimeOffset now, DateTime? ownerBirthday)
    {
        if (!IsOwnerBirthday(now, ownerBirthday) || ownerBirthday is not { } birthday)
            return null;
        var age = now.Year - birthday.Year;
        return age is >= 0 and <= 150 ? age : null;
    }

    public static bool CanTriggerAutonomousMagic(
        DateTimeOffset? lastAutonomousMagicAt,
        DateTimeOffset now) =>
        lastAutonomousMagicAt is null ||
        lastAutonomousMagicAt.Value.Date != now.Date;

    public static bool WasTriggeredToday(DateTimeOffset? lastTriggeredAt, DateTimeOffset now) =>
        lastTriggeredAt is { } last && last.Date == now.Date;

    private static bool IsSpringFestival(DateTime date)
    {
        try
        {
            var calendar = new ChineseLunisolarCalendar();
            return calendar.GetMonth(date) == 1 &&
                   calendar.GetDayOfMonth(date) == 1;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
