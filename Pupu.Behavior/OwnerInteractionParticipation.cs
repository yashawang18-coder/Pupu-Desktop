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
        double acceptanceTendency)
    {
        var runtime = state.Runtime;
        var tendency = Math.Clamp(acceptanceTendency, 0, 1);
        var reason = RefusalReason(kind, context, runtime, tendency);
        return new InteractionParticipationDecision(
            reason is null,
            tendency,
            reason ?? "accepted");
    }

    private static string? RefusalReason(
        OwnerInteractionKind kind,
        OwnerInteractionContext context,
        RuntimeState runtime,
        double acceptanceTendency)
    {
        // The owner-facing setting is the only soft acceptance control. At the
        // default 90%, only extreme, observable state can refuse an explicit
        // request; ordinary temperament, relationship and learned preference
        // never introduce a hidden random rejection.
        var stressLimit = 0.55 + acceptanceTendency * 0.42;
        var fatigueLimit = 0.60 + acceptanceTendency * 0.38;
        if (runtime.Stress >= stressLimit) return "need_space";
        if (runtime.Fatigue >= fatigueLimit &&
            (kind is OwnerInteractionKind.Walk
                or OwnerInteractionKind.WandPlay
                or OwnerInteractionKind.LaserPlay
                or OwnerInteractionKind.Magic))
            return "sleepy";
        if (kind == OwnerInteractionKind.Feeding &&
            context.Fullness >= 86 + acceptanceTendency * 10)
            return "full";
        if (kind == OwnerInteractionKind.Walk && context.Energy <= 8) return "low_energy";
        if (kind == OwnerInteractionKind.CleanLitter &&
            context.LitterLevel <= 8 &&
            context.Cleanliness >= 88)
            return "no_need";
        return null;
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
