namespace Pupu.Behavior;

public enum PerceptionPriority
{
    Background = 0,
    Normal = 1,
    Important = 2,
    Safety = 3
}

public sealed class PerceptionEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string Source { get; set; } = "unknown";
    public string Kind { get; set; } = "unknown";
    public double Confidence { get; set; } = 1;
    public TimeSpan Ttl { get; set; } = TimeSpan.FromSeconds(3);
    public string DeduplicationKey { get; set; } = string.Empty;
    public PerceptionPriority Priority { get; set; } = PerceptionPriority.Normal;
    public double Intensity { get; set; } = 1;
    public Dictionary<string, string> Metadata { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public bool IsAlive(DateTimeOffset now) =>
        now >= Timestamp && now - Timestamp <= Ttl;
}

public sealed class PerceptionEventProcessor
{
    private readonly Dictionary<string, PerceptionEvent> _active =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Queue<DateTimeOffset>> _repetitions =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<PerceptionEvent> Active => _active.Values;

    public PerceptionEvent? Accept(PerceptionEvent incoming, DateTimeOffset now)
    {
        if (incoming.Timestamp > now.AddSeconds(2) || !incoming.IsAlive(now)) return null;
        incoming.Confidence = Math.Clamp(incoming.Confidence, 0, 1);
        incoming.Intensity = Math.Clamp(incoming.Intensity, 0, 1.5);
        incoming.DeduplicationKey = string.IsNullOrWhiteSpace(incoming.DeduplicationKey)
            ? $"{incoming.Source}:{incoming.Kind}"
            : incoming.DeduplicationKey.Trim();

        Expire(now);
        var history = GetHistory(incoming.DeduplicationKey);
        while (history.Count > 0 && now - history.Peek() > TimeSpan.FromSeconds(12))
            history.Dequeue();
        history.Enqueue(now);

        // Repeated harmless stimuli are habituated, never amplified.
        var habituation = incoming.Priority >= PerceptionPriority.Important
            ? 1
            : 1d / (1 + Math.Max(0, history.Count - 1) * 0.22);
        incoming.Intensity *= habituation;

        if (_active.TryGetValue(incoming.DeduplicationKey, out var existing) &&
            now - existing.Timestamp < TimeSpan.FromMilliseconds(180))
        {
            existing.Timestamp = incoming.Timestamp;
            existing.Ttl = existing.Ttl > incoming.Ttl ? existing.Ttl : incoming.Ttl;
            existing.Confidence = Math.Max(existing.Confidence, incoming.Confidence);
            existing.Intensity = Math.Max(existing.Intensity, incoming.Intensity);
            if (incoming.Priority > existing.Priority) existing.Priority = incoming.Priority;
            foreach (var pair in incoming.Metadata) existing.Metadata[pair.Key] = pair.Value;
            return existing;
        }

        _active[incoming.DeduplicationKey] = incoming;
        return incoming;
    }

    public IReadOnlyList<PerceptionEvent> Snapshot(DateTimeOffset now)
    {
        Expire(now);
        return _active.Values
            .OrderByDescending(x => x.Priority)
            .ThenByDescending(x => x.Confidence * x.Intensity)
            .ThenBy(x => x.Timestamp)
            .ToList();
    }

    public double Signal(string kind, DateTimeOffset now)
    {
        Expire(now);
        return Math.Clamp(_active.Values
            .Where(x => string.Equals(x.Kind, kind, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Confidence * x.Intensity)
            .DefaultIfEmpty(0)
            .Max(), 0, 1.5);
    }

    private Queue<DateTimeOffset> GetHistory(string key)
    {
        if (_repetitions.TryGetValue(key, out var history)) return history;
        history = new Queue<DateTimeOffset>();
        _repetitions[key] = history;
        return history;
    }

    private void Expire(DateTimeOffset now)
    {
        foreach (var key in _active
                     .Where(x => !x.Value.IsAlive(now))
                     .Select(x => x.Key)
                     .ToList())
            _active.Remove(key);
    }
}
