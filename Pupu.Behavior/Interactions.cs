namespace Pupu.Behavior;

public sealed class InteractionSession
{
    internal InteractionSession(
        Guid id,
        string behaviorId,
        string interactionType,
        string context,
        string animationSource,
        DateTimeOffset? startedAt = null)
    {
        Id = id;
        BehaviorId = behaviorId;
        InteractionType = interactionType;
        Context = context;
        AnimationSource = animationSource;
        StartedAt = startedAt ?? DateTimeOffset.Now;
        LastActivityAt = StartedAt;
    }

    public Guid Id { get; }
    public string BehaviorId { get; }
    public string InteractionType { get; }
    public string Context { get; }
    public string AnimationSource { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset LastActivityAt { get; internal set; }
    public bool UserResponded { get; internal set; }
    public string Outcome { get; internal set; } = "active";
    public int EventCount { get; internal set; }
    public double CompletionRatio { get; internal set; }
    public List<AppliedEffect> AppliedEffects { get; } = new();
    public bool IsTerminal { get; internal set; }
}

public sealed class InteractionLifecycle
{
    private readonly IClock _clock;
    private readonly Func<InteractionRecord, Task> _sink;

    public InteractionLifecycle(IClock clock, Func<InteractionRecord, Task> sink)
    {
        _clock = clock;
        _sink = sink;
    }

    public async Task<InteractionSession> StartAsync(
        string behaviorId,
        string interactionType,
        string context,
        string animationSource)
    {
        var session = new InteractionSession(
            Guid.NewGuid(), behaviorId, interactionType, context, animationSource, _clock.Now);
        await WriteAsync(session, InteractionLifecycleStage.InteractionStarted, 0);
        return session;
    }

    public async Task ProgressAsync(
        InteractionSession session,
        double completionRatio,
        IEnumerable<AppliedEffect>? newlyAppliedEffects = null)
    {
        EnsureActive(session);
        session.CompletionRatio = Math.Clamp(
            Math.Max(session.CompletionRatio, completionRatio), 0, 0.999);
        if (newlyAppliedEffects is not null)
            session.AppliedEffects.AddRange(newlyAppliedEffects);
        session.LastActivityAt = _clock.Now;
        session.EventCount++;
        await WriteAsync(session, InteractionLifecycleStage.InteractionProgressed, session.CompletionRatio);
    }

    public async Task CompleteAsync(
        InteractionSession session,
        IEnumerable<AppliedEffect>? finalEffects = null)
    {
        EnsureActive(session);
        if (finalEffects is not null) session.AppliedEffects.AddRange(finalEffects);
        session.CompletionRatio = 1;
        session.IsTerminal = true;
        session.Outcome = "completed";
        await WriteAsync(session, InteractionLifecycleStage.InteractionCompleted, 1);
    }

    public async Task InterruptAsync(InteractionSession session, string reason)
    {
        if (session.IsTerminal) return;
        session.IsTerminal = true;
        session.Outcome = reason;
        await WriteAsync(
            session,
            InteractionLifecycleStage.InteractionInterrupted,
            session.CompletionRatio,
            interruptReason: reason);
    }

    public async Task FailAsync(InteractionSession session, Exception exception)
    {
        if (session.IsTerminal) return;
        session.IsTerminal = true;
        session.Outcome = "failed";
        await WriteAsync(
            session,
            InteractionLifecycleStage.InteractionFailed,
            session.CompletionRatio,
            failureReason: exception.GetType().Name + ": " + exception.Message);
    }

    private Task WriteAsync(
        InteractionSession session,
        InteractionLifecycleStage stage,
        double completionRatio,
        string? interruptReason = null,
        string? failureReason = null) => _sink(new InteractionRecord
    {
        InteractionId = session.Id,
        At = _clock.Now,
        Stage = stage,
        BehaviorId = session.BehaviorId,
        InteractionType = session.InteractionType,
        Context = session.Context,
        AnimationSource = session.AnimationSource,
        CompletionRatio = completionRatio,
        InterruptReason = interruptReason,
        FailureReason = failureReason,
        AppliedEffects = session.AppliedEffects.ToList()
    });

    private static void EnsureActive(InteractionSession session)
    {
        if (session.IsTerminal)
            throw new InvalidOperationException($"Interaction {session.Id} is already terminal.");
    }
}

public sealed class ScheduledAction : IDisposable
{
    private readonly CancellationTokenSource _source;

    internal ScheduledAction(string behaviorId, CancellationTokenSource source)
    {
        BehaviorId = behaviorId;
        _source = source;
        Phase = ActionPhase.Entering;
        PhaseChangedAt = DateTimeOffset.Now;
    }

    public string BehaviorId { get; }
    public CancellationToken Token => _source.Token;
    public string StopReason { get; internal set; } = "cancelled";
    public bool IsCancellationRequested => _source.IsCancellationRequested;
    public ActionPhase Phase { get; internal set; }
    public DateTimeOffset PhaseChangedAt { get; internal set; }

    internal void Cancel(string reason)
    {
        StopReason = string.IsNullOrWhiteSpace(reason) ? "cancelled" : reason;
        Phase = ActionPhase.Interrupted;
        PhaseChangedAt = DateTimeOffset.Now;
        _source.Cancel();
    }

    public void Dispose() => _source.Dispose();
}

public enum ActionPhase
{
    Entering,
    Looping,
    Exiting,
    Completed,
    Interrupted
}

public sealed class ActionScheduler : IDisposable
{
    private ScheduledAction? _current;

    public ScheduledAction? Current => _current;

    public ScheduledAction Start(string behaviorId)
    {
        Stop("superseded");
        _current?.Dispose();
        _current = new ScheduledAction(behaviorId, new CancellationTokenSource());
        return _current;
    }

    public bool Stop(string reason)
    {
        if (_current is null) return false;
        if (!_current.IsCancellationRequested) _current.Cancel(reason);
        return true;
    }

    public void EnterLoop(ScheduledAction action)
    {
        if (!ReferenceEquals(_current, action) || action.IsCancellationRequested) return;
        action.Phase = ActionPhase.Looping;
        action.PhaseChangedAt = DateTimeOffset.Now;
    }

    public void BeginExit(ScheduledAction action)
    {
        if (!ReferenceEquals(_current, action) || action.IsCancellationRequested) return;
        action.Phase = ActionPhase.Exiting;
        action.PhaseChangedAt = DateTimeOffset.Now;
    }

    public void Complete(ScheduledAction action)
    {
        if (!ReferenceEquals(_current, action)) return;
        action.Phase = ActionPhase.Completed;
        action.PhaseChangedAt = DateTimeOffset.Now;
        _current.Dispose();
        _current = null;
    }

    public void Dispose()
    {
        Stop("shutdown");
        _current?.Dispose();
        _current = null;
    }
}

public sealed class InteractionSessionManager
{
    private readonly IClock _clock;
    private InteractionSession? _active;

    public InteractionSessionManager(IClock clock) => _clock = clock;
    public InteractionSession? Active => _active;
    public TimeSpan ContinuousTouchGap { get; init; } = TimeSpan.FromSeconds(2.4);

    public InteractionSession GetOrCreateTouch(
        string behaviorId,
        string context,
        string animationSource)
    {
        if (_active is not null &&
            !_active.IsTerminal &&
            _active.InteractionType == "touch" &&
            _clock.Now - _active.LastActivityAt <= ContinuousTouchGap)
        {
            _active.LastActivityAt = _clock.Now;
            _active.EventCount++;
            return _active;
        }
        EndActive("naturally_ended");
        _active = new InteractionSession(
            Guid.NewGuid(), behaviorId, "touch", context, animationSource, _clock.Now)
        {
            EventCount = 1
        };
        return _active;
    }

    public InteractionSession StartInitiative(
        string behaviorId,
        string context,
        string animationSource)
    {
        EndActive("superseded");
        _active = new InteractionSession(
            Guid.NewGuid(), behaviorId, "pet_initiative", context, animationSource, _clock.Now);
        return _active;
    }

    public void MarkUserResponse()
    {
        if (_active is null || _active.IsTerminal) return;
        _active.UserResponded = true;
        _active.LastActivityAt = _clock.Now;
        _active.Outcome = "user_responded";
    }

    public InteractionSession? EndActive(string outcome = "naturally_ended")
    {
        if (_active is null) return null;
        _active.IsTerminal = true;
        _active.Outcome = outcome;
        _active.LastActivityAt = _clock.Now;
        var ended = _active;
        _active = null;
        return ended;
    }
}
