namespace Pupu.Behavior;

public enum BehaviorProposalState
{
    Queued,
    Deferred,
    Executing,
    Completed,
    Rejected,
    Expired,
    Cancelled,
    Failed
}

public sealed class BehaviorProposal
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string BehaviorId { get; init; }
    public required BehaviorArbitrationSource Source { get; init; }
    public required BehaviorPriority Priority { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset ExpiresAt { get; init; } = DateTimeOffset.Now.AddSeconds(30);
    public bool Cancellable { get; init; } = true;
    public bool AllowDelay { get; init; } = true;
    public string Reason { get; init; } = string.Empty;
    public TimeSpan MinimumDuration { get; init; } = TimeSpan.Zero;
    public TimeSpan Cooldown { get; init; } = TimeSpan.Zero;
    public bool Interruptible { get; init; } = true;
    public bool ForceInterrupt { get; init; }
    public bool ObservationOnly { get; init; }
    public BehaviorStateBlockers ForbiddenStates { get; init; }
    public BehaviorStateBlockers AllowedStates { get; init; }
    public string CooldownKey { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Data { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class BehaviorProposalRecord
{
    public required BehaviorProposal Proposal { get; init; }
    public BehaviorProposalState State { get; set; } = BehaviorProposalState.Queued;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public string ResultCode { get; set; } = "queued";
    public string Explanation { get; set; } = "等待统一执行器处理";

    public string Display =>
        $"{UpdatedAt:HH:mm:ss} · {State} · {Proposal.BehaviorId} · " +
        $"{Proposal.Source}/{Proposal.Priority} · {Explanation}";
}

public sealed class BehaviorProposalQueue
{
    private readonly object _sync = new();
    private readonly List<BehaviorProposalRecord> _pending = new();
    private readonly List<BehaviorProposalRecord> _history = new();

    public BehaviorProposalRecord Enqueue(BehaviorProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (string.IsNullOrWhiteSpace(proposal.BehaviorId))
            throw new ArgumentException("行为提案必须包含 BehaviorId。", nameof(proposal));
        var record = new BehaviorProposalRecord { Proposal = proposal };
        lock (_sync)
        {
            _pending.Add(record);
            Trim();
        }
        return record;
    }

    public bool Cancel(Guid id, DateTimeOffset now, string reason)
    {
        lock (_sync)
        {
            var record = _pending.FirstOrDefault(item => item.Proposal.Id == id);
            if (record is null || !record.Proposal.Cancellable) return false;
            _pending.Remove(record);
            Complete(record, BehaviorProposalState.Cancelled, now, "cancelled", reason);
            return true;
        }
    }

    public IReadOnlyList<BehaviorProposalRecord> Snapshot()
    {
        lock (_sync)
            return _pending
                .OrderByDescending(item => item.Proposal.Priority)
                .ThenBy(item => item.Proposal.CreatedAt)
                .ToList();
    }

    public IReadOnlyList<BehaviorProposalRecord> History()
    {
        lock (_sync) return _history.ToList();
    }

    internal BehaviorProposalRecord? TakeNext(DateTimeOffset now)
    {
        lock (_sync)
        {
            foreach (var expired in _pending
                         .Where(item => item.Proposal.ExpiresAt <= now)
                         .ToList())
            {
                _pending.Remove(expired);
                Complete(
                    expired,
                    BehaviorProposalState.Expired,
                    now,
                    "expired",
                    "提案超过有效期，未执行");
            }

            var next = _pending
                .OrderByDescending(item => item.Proposal.Priority)
                .ThenBy(item => item.Proposal.CreatedAt)
                .FirstOrDefault();
            if (next is not null) _pending.Remove(next);
            return next;
        }
    }

    internal void Requeue(
        BehaviorProposalRecord record,
        DateTimeOffset now,
        string resultCode,
        string explanation)
    {
        lock (_sync)
        {
            record.State = BehaviorProposalState.Deferred;
            record.UpdatedAt = now;
            record.ResultCode = resultCode;
            record.Explanation = explanation;
            _pending.Add(record);
            Trim();
        }
    }

    internal void Finish(
        BehaviorProposalRecord record,
        BehaviorProposalState state,
        DateTimeOffset now,
        string resultCode,
        string explanation)
    {
        lock (_sync) Complete(record, state, now, resultCode, explanation);
    }

    private void Complete(
        BehaviorProposalRecord record,
        BehaviorProposalState state,
        DateTimeOffset now,
        string resultCode,
        string explanation)
    {
        record.State = state;
        record.UpdatedAt = now;
        record.ResultCode = resultCode;
        record.Explanation = explanation;
        _history.Insert(0, record);
        if (_history.Count > 80) _history.RemoveRange(80, _history.Count - 80);
    }

    private void Trim()
    {
        if (_pending.Count <= 100) return;
        foreach (var overflow in _pending
                     .OrderBy(item => item.Proposal.Priority)
                     .ThenBy(item => item.Proposal.CreatedAt)
                     .Take(_pending.Count - 100)
                     .ToList())
        {
            _pending.Remove(overflow);
            Complete(
                overflow,
                BehaviorProposalState.Cancelled,
                DateTimeOffset.Now,
                "queue_capacity",
                "提案队列已满，低优先级提案被取消");
        }
    }
}

public sealed record BehaviorProposalExecutionResult(
    BehaviorProposalRecord? Record,
    BehaviorArbitrationResult? Arbitration,
    bool Executed);

/// <summary>
/// Platform-neutral queue consumer. It is the only path from a proposal to a
/// platform handler, and it always evaluates BehaviorArbitrator first.
/// </summary>
public sealed class BehaviorProposalExecutor
{
    private readonly BehaviorProposalQueue _queue;
    private readonly BehaviorArbitrator _arbitrator;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BehaviorProposalExecutor(
        BehaviorProposalQueue queue,
        BehaviorArbitrator arbitrator)
    {
        _queue = queue;
        _arbitrator = arbitrator;
    }

    public async Task<BehaviorProposalExecutionResult> ProcessNextAsync(
        DateTimeOffset now,
        BehaviorArbitrationContext context,
        Func<BehaviorArbitrationResult, Task> observeDecision,
        Func<BehaviorProposal, CancellationToken, Task<bool>> execute,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var record = _queue.TakeNext(now);
            if (record is null)
                return new BehaviorProposalExecutionResult(null, null, false);

            var proposal = record.Proposal;
            var previousLease = _arbitrator.CurrentLease;
            var request = new BehaviorArbitrationRequest
            {
                BehaviorId = proposal.BehaviorId,
                Source = proposal.Source,
                Priority = proposal.Priority,
                RequestedAt = now,
                MinimumDuration = proposal.MinimumDuration,
                Cooldown = proposal.Cooldown,
                Interruptible = proposal.Interruptible,
                ForceInterrupt = proposal.ForceInterrupt,
                ObservationOnly = proposal.ObservationOnly,
                ForbiddenStates = proposal.ForbiddenStates,
                AllowedStates = proposal.AllowedStates,
                CooldownKey = proposal.CooldownKey
            };
            var arbitration = _arbitrator.Evaluate(request, context);
            await observeDecision(arbitration);
            if (!arbitration.Accepted)
            {
                var transient = arbitration.ReasonCode is
                    "current_not_interruptible" or
                    "minimum_duration" or
                    "lower_priority";
                if (transient && proposal.AllowDelay && proposal.ExpiresAt > now)
                    _queue.Requeue(
                        record,
                        now,
                        arbitration.ReasonCode,
                        $"延迟：{arbitration.Explanation}");
                else
                    _queue.Finish(
                        record,
                        BehaviorProposalState.Rejected,
                        now,
                        arbitration.ReasonCode,
                        arbitration.Explanation);
                return new BehaviorProposalExecutionResult(record, arbitration, false);
            }

            record.State = BehaviorProposalState.Executing;
            record.UpdatedAt = now;
            record.ResultCode = "executing";
            record.Explanation = "仲裁通过，进入统一平台执行器";
            try
            {
                var executed = await execute(proposal, cancellationToken);
                _queue.Finish(
                    record,
                    executed ? BehaviorProposalState.Completed : BehaviorProposalState.Failed,
                    DateTimeOffset.Now,
                    executed ? "completed" : "handler_declined",
                    executed ? "统一执行器已完成行为" : "平台执行器没有可用映射");
                if (!executed)
                    _arbitrator.RollbackAdmission(arbitration, previousLease);
                return new BehaviorProposalExecutionResult(record, arbitration, executed);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _arbitrator.RollbackAdmission(arbitration, previousLease);
                _queue.Finish(
                    record,
                    BehaviorProposalState.Cancelled,
                    DateTimeOffset.Now,
                    "cancelled",
                    "执行期间取消");
                throw;
            }
            catch (Exception ex)
            {
                _arbitrator.RollbackAdmission(arbitration, previousLease);
                _queue.Finish(
                    record,
                    BehaviorProposalState.Failed,
                    DateTimeOffset.Now,
                    "execution_failed",
                    $"平台执行失败：{ex.GetType().Name}");
                return new BehaviorProposalExecutionResult(record, arbitration, false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
