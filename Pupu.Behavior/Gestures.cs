namespace Pupu.Behavior;

public enum GestureKind
{
    Touch,
    Stroke,
    Hold,
    LiftIntent,
    Drag,
    RapidTap,
    Release
}

public sealed class GestureEvent
{
    public Guid SessionId { get; set; }
    public GestureKind Kind { get; set; }
    public DateTimeOffset At { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double DurationMilliseconds { get; set; }
    public double DragDistance { get; set; }
    public double ClicksPerSecond { get; set; }
    public int RecentTapCount { get; set; }
    public string CurrentBehaviorId { get; set; } = "unknown";
    public string InteractionRegion { get; set; } = "body";
    public InteractionIntent Intent { get; set; } = InteractionIntent.TouchPet;
    public List<string> RecentInteractionHistory { get; set; } = new();
}

public sealed class GestureInterpreter
{
    private readonly IClock _clock;
    private readonly Queue<DateTimeOffset> _recentTaps = new();
    private readonly Queue<(DateTimeOffset At, string Kind)> _recentInteractions = new();
    private DateTimeOffset? _downAt;
    private double _downX;
    private double _downY;
    private double _maximumDistance;
    private InteractionRegionHit _downRegion = InteractionRegionHit.Default;

    public GestureInterpreter(IClock clock) => _clock = clock;

    public void PointerDown(double x, double y, InteractionRegionHit? region = null)
    {
        _downAt = _clock.Now;
        _downX = x;
        _downY = y;
        _maximumDistance = 0;
        _downRegion = region ?? InteractionRegionHit.Default;
    }

    public void PointerMove(double x, double y)
    {
        if (_downAt is null) return;
        _maximumDistance = Math.Max(
            _maximumDistance,
            Math.Sqrt(Math.Pow(x - _downX, 2) + Math.Pow(y - _downY, 2)));
    }

    public IReadOnlyList<GestureEvent> PointerUp(
        double x,
        double y,
        string currentBehaviorId,
        bool windowDrag = false)
    {
        var now = _clock.Now;
        var downAt = _downAt ?? now;
        PointerMove(x, y);
        var duration = Math.Max(0, (now - downAt).TotalMilliseconds);
        var distance = _maximumDistance;
        _downAt = null;

        while (_recentTaps.Count > 0 && now - _recentTaps.Peek() > TimeSpan.FromSeconds(2.2))
            _recentTaps.Dequeue();
        while (_recentInteractions.Count > 0 &&
               now - _recentInteractions.Peek().At > TimeSpan.FromSeconds(6))
            _recentInteractions.Dequeue();

        GestureKind primary;
        if (windowDrag)
            primary = GestureKind.Drag;
        else if (duration >= 650 && _downRegion.SupportsLift)
            primary = GestureKind.LiftIntent;
        else if (distance >= 12)
            primary = GestureKind.Drag;
        else if (duration >= 650)
            primary = GestureKind.Hold;
        else if (distance >= 4)
            primary = GestureKind.Stroke;
        else
        {
            _recentTaps.Enqueue(now);
            primary = _recentTaps.Count >= 3 ? GestureKind.RapidTap : GestureKind.Touch;
        }

        var spanSeconds = _recentTaps.Count > 1
            ? Math.Max(0.1, (now - _recentTaps.Peek()).TotalSeconds)
            : Math.Max(0.25, duration / 1000);
        var frequency = _recentTaps.Count / spanSeconds;
        var history = _recentInteractions.Select(x => x.Kind).ToList();
        var primaryEvent = new GestureEvent
        {
            Kind = primary,
            At = now,
            X = x,
            Y = y,
            DurationMilliseconds = duration,
            DragDistance = distance,
            ClicksPerSecond = frequency,
            RecentTapCount = _recentTaps.Count,
            CurrentBehaviorId = currentBehaviorId,
            InteractionRegion = _downRegion.RegionId,
            Intent = windowDrag ? InteractionIntent.MoveWindow :
                primary == GestureKind.LiftIntent ? InteractionIntent.LiftPet :
                primary == GestureKind.Stroke ? InteractionIntent.StrokePet :
                primary == GestureKind.Hold ? InteractionIntent.HoldPet :
                InteractionIntent.TouchPet,
            RecentInteractionHistory = history
        };
        var releaseEvent = new GestureEvent
        {
            Kind = GestureKind.Release,
            At = now,
            X = x,
            Y = y,
            DurationMilliseconds = duration,
            DragDistance = distance,
            ClicksPerSecond = frequency,
            RecentTapCount = _recentTaps.Count,
            CurrentBehaviorId = currentBehaviorId,
            InteractionRegion = _downRegion.RegionId,
            Intent = InteractionIntent.Release,
            RecentInteractionHistory = history
        };

        _recentInteractions.Enqueue((now, primary.ToString().ToLowerInvariant()));
        while (_recentInteractions.Count > 8) _recentInteractions.Dequeue();
        return new[] { primaryEvent, releaseEvent };
    }
}

public sealed class GestureStateUpdater
{
    public void Apply(PersonalityBehaviorState state, GestureEvent gesture)
    {
        if (gesture.Kind == GestureKind.Release) return;
        var temperament = state.Temperament;
        var relationship = state.Relationship;
        var trustBuffer = relationship.Trust * 0.22 + relationship.TouchAcceptance * 0.18;
        var sensitivity = 0.35 + temperament.Sensitive * 0.95;

        switch (gesture.Kind)
        {
            case GestureKind.Touch:
                state.Runtime.Arousal += 0.025;
                state.Runtime.Curiosity += 0.035;
                state.Runtime.SocialDesire -= 0.015;
                state.Runtime.Stress += Math.Max(-0.006, 0.012 * sensitivity - trustBuffer * 0.025);
                break;
            case GestureKind.Stroke:
                state.Runtime.Arousal -= 0.025;
                state.Runtime.Safety += 0.035 + trustBuffer * 0.04;
                state.Runtime.Stress -= 0.035 + trustBuffer * 0.035;
                break;
            case GestureKind.Hold:
                state.Runtime.Arousal += 0.035;
                state.Runtime.Stress += Math.Max(0.012, 0.05 * sensitivity - trustBuffer * 0.025);
                state.Runtime.Safety -= 0.025;
                break;
            case GestureKind.LiftIntent:
                state.Runtime.Arousal += 0.045;
                state.Runtime.Stress += Math.Max(0.015, 0.055 * sensitivity - trustBuffer * 0.028);
                state.Runtime.Safety -= 0.030;
                break;
            case GestureKind.Drag:
                state.Runtime.Arousal += 0.10;
                state.Runtime.Stress += Math.Max(0.035, 0.12 * sensitivity - trustBuffer * 0.035);
                state.Runtime.Safety -= 0.08;
                break;
            case GestureKind.RapidTap:
                var frequencyFactor = Math.Clamp(gesture.ClicksPerSecond / 5, 0.35, 1.5);
                var recentPenalty = Math.Clamp(state.RecentOvertouchCount * 0.008, 0, 0.10);
                state.Runtime.Arousal += 0.08 * frequencyFactor;
                state.Runtime.Stress += Math.Max(
                    0.025,
                    0.10 * sensitivity * frequencyFactor + recentPenalty - trustBuffer * 0.08);
                state.Runtime.Safety -= 0.055 * frequencyFactor;
                state.RecentOvertouchCount++;
                state.LastOvertouchAt = gesture.At;
                break;
        }
        state.Runtime.Clamp();
        state.RecentOvertouchCount = Math.Clamp(state.RecentOvertouchCount, 0, 20);
    }

    public int BoundedRapidTapTolerance(PersonalityBehaviorState state)
    {
        var raw = 5
                  + state.Relationship.Trust * 2.0
                  + state.Relationship.TouchAcceptance * 2.0
                  - state.Temperament.Sensitive * 2.2
                  - state.Runtime.Stress * 2.6
                  - Math.Min(2, state.RecentOvertouchCount * 0.2);
        return Math.Clamp((int)Math.Round(raw), 3, 10);
    }
}
