namespace Pupu.Behavior;

public sealed class RuntimeStateDynamics
{
    public const double MaximumEventDelta = 0.18;
    public const double MaximumFiveMinuteDelta = 0.12;
    public static readonly TimeSpan MaximumResumeRecovery = TimeSpan.FromHours(2);

    public void AdvanceActive(
        PersonalityBehaviorState state,
        TimeSpan elapsed,
        bool deepNight)
    {
        var minutes = Math.Clamp(elapsed.TotalMinutes, 0, 5);
        if (minutes <= 0) return;

        var runtime = state.Runtime;
        var temperament = state.Temperament;
        var stressRecovery = 0.0032
                             + runtime.Safety * 0.0018
                             + (1 - temperament.Sensitive) * 0.0012;
        var fatigueRate = deepNight ? 0.0028 : 0.0012;
        var nightActivation = deepNight ? 0.0015 + temperament.Playful * 0.0015 : 0;

        ApplyBounded(runtime, RuntimeDimension.Stress, -stressRecovery * minutes, MaximumFiveMinuteDelta);
        ApplyBounded(runtime, RuntimeDimension.Fatigue, fatigueRate * minutes, MaximumFiveMinuteDelta);
        ApplyBounded(
            runtime,
            RuntimeDimension.Arousal,
            ((deepNight ? -0.001 : 0.0008) + nightActivation - runtime.Fatigue * 0.0012) * minutes,
            MaximumFiveMinuteDelta);

        // Limited coupling: stress reduces current willingness without turning
        // a transient state into a temperament change.
        Approach(runtime, RuntimeDimension.SocialDesire,
            0.50 + temperament.Affectionate * 0.16 - runtime.Stress * 0.22,
            minutes * 0.008);
        Approach(runtime, RuntimeDimension.PlayDesire,
            0.48 + temperament.Playful * 0.22 - runtime.Fatigue * 0.26 - runtime.Stress * 0.20,
            minutes * 0.008);
        Approach(runtime, RuntimeDimension.Curiosity,
            0.48 + temperament.Playful * 0.10 + temperament.Mischievous * 0.08 - runtime.Stress * 0.18,
            minutes * 0.006);
        Approach(runtime, RuntimeDimension.Safety,
            0.72 + state.Relationship.Trust * 0.14 - runtime.Stress * 0.12,
            minutes * 0.006);

        runtime.LastUpdatedAt = runtime.LastUpdatedAt.Add(elapsed);
        runtime.LastActiveAt = runtime.LastUpdatedAt;
        runtime.SuspendedAt = null;
        runtime.Clamp();
    }

    public void MarkSuspended(PersonalityBehaviorState state, DateTimeOffset at)
    {
        state.Runtime.SuspendedAt = at;
        state.Runtime.LastUpdatedAt = at;
    }

    public void RestoreAfterResume(PersonalityBehaviorState state, DateTimeOffset now)
    {
        var suspendedAt = state.Runtime.SuspendedAt ?? state.Runtime.LastUpdatedAt;
        var recoveryWindow = now - suspendedAt;
        if (recoveryWindow < TimeSpan.Zero) recoveryWindow = TimeSpan.Zero;
        recoveryWindow = recoveryWindow > MaximumResumeRecovery
            ? MaximumResumeRecovery
            : recoveryWindow;

        // Resume is one bounded recovery operation. It never replays every
        // missed minute and never creates hunger, litter or attention debt.
        var hours = recoveryWindow.TotalHours;
        ApplyBounded(state.Runtime, RuntimeDimension.Stress, -0.035 * hours, 0.07);
        ApplyBounded(state.Runtime, RuntimeDimension.Fatigue, -0.018 * hours, 0.04);
        ApplyBounded(state.Runtime, RuntimeDimension.Safety, 0.018 * hours, 0.04);
        state.Runtime.LastUpdatedAt = now;
        state.Runtime.LastActiveAt = now;
        state.Runtime.SuspendedAt = null;
        state.Runtime.Clamp();
    }

    public void ApplyEventDelta(
        PersonalityBehaviorState state,
        RuntimeDimension dimension,
        double requestedDelta)
    {
        ApplyBounded(state.Runtime, dimension, requestedDelta, MaximumEventDelta);
        state.Runtime.Clamp();
    }

    private static void Approach(
        RuntimeState state,
        RuntimeDimension dimension,
        double target,
        double rate)
    {
        var current = Read(state, dimension);
        var delta = (Math.Clamp(target, 0, 1) - current) * Math.Clamp(rate, 0, 0.08);
        ApplyBounded(state, dimension, delta, MaximumFiveMinuteDelta);
    }

    private static void ApplyBounded(
        RuntimeState state,
        RuntimeDimension dimension,
        double requestedDelta,
        double cap)
    {
        var delta = Math.Clamp(requestedDelta, -Math.Abs(cap), Math.Abs(cap));
        Write(state, dimension, Read(state, dimension) + delta);
    }

    private static double Read(RuntimeState value, RuntimeDimension dimension) => dimension switch
    {
        RuntimeDimension.Arousal => value.Arousal,
        RuntimeDimension.Stress => value.Stress,
        RuntimeDimension.SocialDesire => value.SocialDesire,
        RuntimeDimension.PlayDesire => value.PlayDesire,
        RuntimeDimension.Curiosity => value.Curiosity,
        RuntimeDimension.Fatigue => value.Fatigue,
        _ => value.Safety
    };

    private static void Write(RuntimeState value, RuntimeDimension dimension, double result)
    {
        result = Math.Clamp(result, 0, 1);
        switch (dimension)
        {
            case RuntimeDimension.Arousal: value.Arousal = result; break;
            case RuntimeDimension.Stress: value.Stress = result; break;
            case RuntimeDimension.SocialDesire: value.SocialDesire = result; break;
            case RuntimeDimension.PlayDesire: value.PlayDesire = result; break;
            case RuntimeDimension.Curiosity: value.Curiosity = result; break;
            case RuntimeDimension.Fatigue: value.Fatigue = result; break;
            default: value.Safety = result; break;
        }
    }
}
