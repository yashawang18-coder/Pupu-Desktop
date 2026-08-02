namespace Pupu.Behavior;

public enum DailyToiletSlotStatus
{
    Pending,
    Reserved,
    Completed,
    Skipped
}

public sealed class DailyToiletSlot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset ScheduledAt { get; set; }
    public DailyToiletSlotStatus Status { get; set; } = DailyToiletSlotStatus.Pending;
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class DailyToiletPlan
{
    public DateOnly LocalDate { get; set; }
    public int TargetCount { get; set; }
    public List<DailyToiletSlot> Slots { get; set; } = new();
}

public sealed record DailyToiletPlanPreparation(
    DailyToiletPlan Plan,
    bool Rebuilt);

public sealed class DailyToiletPlanner
{
    public DailyToiletPlanner(TimeSpan? dueWindow = null)
    {
        DueWindow = dueWindow ?? TimeSpan.FromMinutes(45);
    }

    public TimeSpan DueWindow { get; }

    public DailyToiletPlanPreparation EnsurePlan(
        DailyToiletPlan? existing,
        DateTimeOffset now,
        IRandomSource random)
    {
        var localDate = DateOnly.FromDateTime(now.DateTime);
        if (IsValidFor(existing, localDate))
            return new DailyToiletPlanPreparation(existing!, false);

        var targetCount = random.Next(2, 4);
        var dayStart = new DateTimeOffset(
            localDate.ToDateTime(TimeOnly.MinValue),
            now.Offset);
        var dayEnd = dayStart.AddDays(1).AddSeconds(-1);
        var earliest = dayStart.AddHours(8);
        if (earliest < now.AddMinutes(5)) earliest = now.AddMinutes(5);
        if (earliest > dayEnd) earliest = now;

        var availableSeconds = Math.Max(
            targetCount,
            (dayEnd - earliest).TotalSeconds);
        var bucketSeconds = availableSeconds / targetCount;
        var slots = new List<DailyToiletSlot>(targetCount);
        for (var index = 0; index < targetCount; index++)
        {
            var bucketStart = earliest.AddSeconds(bucketSeconds * index);
            var offset = bucketSeconds * (0.15 + random.NextDouble() * 0.70);
            var scheduledAt = bucketStart.AddSeconds(offset);
            if (scheduledAt > dayEnd) scheduledAt = dayEnd;
            slots.Add(new DailyToiletSlot
            {
                ScheduledAt = scheduledAt
            });
        }

        return new DailyToiletPlanPreparation(
            new DailyToiletPlan
            {
                LocalDate = localDate,
                TargetCount = targetCount,
                Slots = slots.OrderBy(x => x.ScheduledAt).ToList()
            },
            true);
    }

    public bool SkipPastPending(DailyToiletPlan plan, DateTimeOffset now)
    {
        var changed = false;
        foreach (var slot in plan.Slots.Where(x =>
                     x.Status == DailyToiletSlotStatus.Pending &&
                     x.ScheduledAt <= now))
        {
            slot.Status = DailyToiletSlotStatus.Skipped;
            slot.UpdatedAt = now;
            changed = true;
        }
        return changed;
    }

    public bool ExpireMissed(DailyToiletPlan plan, DateTimeOffset now)
    {
        var cutoff = now - DueWindow;
        var changed = false;
        foreach (var slot in plan.Slots.Where(x =>
                     x.Status == DailyToiletSlotStatus.Pending &&
                     x.ScheduledAt < cutoff))
        {
            slot.Status = DailyToiletSlotStatus.Skipped;
            slot.UpdatedAt = now;
            changed = true;
        }
        return changed;
    }

    public bool IsDue(DailyToiletPlan plan, DateTimeOffset now) =>
        FindDueSlot(plan, now) is not null;

    public bool TryReserveDueSlot(
        DailyToiletPlan plan,
        DateTimeOffset now,
        out string slotId)
    {
        var slot = FindDueSlot(plan, now);
        if (slot is null)
        {
            slotId = string.Empty;
            return false;
        }

        slot.Status = DailyToiletSlotStatus.Reserved;
        slot.UpdatedAt = now;
        slotId = slot.Id;
        return true;
    }

    public bool TryCompleteSlot(
        DailyToiletPlan plan,
        string slotId,
        DateTimeOffset now)
    {
        var slot = plan.Slots.FirstOrDefault(x =>
            string.Equals(x.Id, slotId, StringComparison.Ordinal));
        if (slot?.Status != DailyToiletSlotStatus.Reserved) return false;
        slot.Status = DailyToiletSlotStatus.Completed;
        slot.UpdatedAt = now;
        return true;
    }

    private DailyToiletSlot? FindDueSlot(
        DailyToiletPlan plan,
        DateTimeOffset now)
    {
        var cutoff = now - DueWindow;
        return plan.Slots
            .Where(x =>
                x.Status == DailyToiletSlotStatus.Pending &&
                x.ScheduledAt <= now &&
                x.ScheduledAt >= cutoff)
            .OrderBy(x => x.ScheduledAt)
            .FirstOrDefault();
    }

    private static bool IsValidFor(
        DailyToiletPlan? plan,
        DateOnly localDate) =>
        plan is not null &&
        plan.LocalDate == localDate &&
        plan.TargetCount is 2 or 3 &&
        plan.Slots is not null &&
        plan.Slots.Count == plan.TargetCount &&
        plan.Slots
            .Select(x => x.Id)
            .Distinct(StringComparer.Ordinal)
            .Count() == plan.TargetCount &&
        plan.Slots.All(x =>
            !string.IsNullOrWhiteSpace(x.Id) &&
            DateOnly.FromDateTime(x.ScheduledAt.DateTime) == localDate);
}
