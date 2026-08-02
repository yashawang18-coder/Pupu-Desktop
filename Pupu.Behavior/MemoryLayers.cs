namespace Pupu.Behavior;

public sealed class MemoryMaintenanceEngine
{
    public const int MinimumSamples = 6;
    public const int MinimumDistinctDays = 3;
    public const int MinimumOpportunities = 6;
    public const double MaximumDerivedInfluence = 0.42;

    public RawInteractionEvent AddRawEvent(
        PersonalityBehaviorState state,
        Guid sessionId,
        string behaviorId,
        string interactionType,
        string context,
        double signal,
        double opportunity,
        double outcomeQuality,
        DateTimeOffset at,
        string source = "interaction")
    {
        var item = new RawInteractionEvent
        {
            SessionId = sessionId == Guid.Empty ? Guid.NewGuid() : sessionId,
            BehaviorId = behaviorId,
            InteractionType = interactionType,
            Context = context,
            Signal = Math.Clamp(signal, -1, 1),
            Opportunity = Math.Clamp(opportunity, 0, 1),
            OutcomeQuality = Math.Clamp(outcomeQuality, 0, 1),
            At = at,
            Source = source
        };
        state.RawInteractionEvents.Add(item);
        RebuildKey(state, item.BehaviorId, item.InteractionType, item.Context, at);
        return item;
    }

    public EpisodicMemory? ConsolidateSession(
        PersonalityBehaviorState state,
        Guid sessionId,
        DateTimeOffset now)
    {
        var events = state.RawInteractionEvents
            .Where(x => x.SessionId == sessionId && !x.IsDeleted &&
                        !state.DeletedEvidenceIds.Contains(x.Id))
            .OrderBy(x => x.At)
            .ToList();
        if (events.Count == 0) return null;
        var existing = state.EpisodicMemories.FirstOrDefault(x =>
            x.EvidenceIds.Any(id => events.Any(e => e.Id == id)));
        var episode = existing ?? new EpisodicMemory();
        episode.EvidenceIds = events.Select(x => x.Id).Distinct().ToList();
        episode.BehaviorId = events.GroupBy(x => x.BehaviorId).OrderByDescending(x => x.Count()).First().Key;
        episode.InteractionType = events.GroupBy(x => x.InteractionType).OrderByDescending(x => x.Count()).First().Key;
        episode.Context = events.GroupBy(x => x.Context).OrderByDescending(x => x.Count()).First().Key;
        episode.StartedAt = events[0].At;
        episode.EndedAt = events[^1].At;
        episode.OutcomeQuality = events.Average(x => x.OutcomeQuality);
        if (existing is null) state.EpisodicMemories.Add(episode);
        RebuildKey(state, episode.BehaviorId, episode.InteractionType, episode.Context, now);
        return episode;
    }

    public bool DeleteEvidence(
        PersonalityBehaviorState state,
        Guid evidenceId,
        DateTimeOffset now)
    {
        var item = state.RawInteractionEvents.FirstOrDefault(x => x.Id == evidenceId);
        if (item is null) return false;
        item.IsDeleted = true;
        state.DeletedEvidenceIds.Add(evidenceId);
        foreach (var episode in state.EpisodicMemories.Where(x => x.EvidenceIds.Contains(evidenceId)))
            episode.IsDeleted = true;
        RebuildKey(state, item.BehaviorId, item.InteractionType, item.Context, now);
        return true;
    }

    public void Maintain(PersonalityBehaviorState state, DateTimeOffset now)
    {
        foreach (var group in state.RawInteractionEvents
                     .Where(x => !x.IsDeleted && !state.DeletedEvidenceIds.Contains(x.Id))
                     .GroupBy(x => PreferenceKey.Create(x.BehaviorId, x.InteractionType, x.Context)))
        {
            var last = group.OrderByDescending(x => x.At).First();
            RebuildKey(state, last.BehaviorId, last.InteractionType, last.Context, now);
        }

        state.RawInteractionEvents = state.RawInteractionEvents
            .Where(x => !x.IsDeleted && !state.DeletedEvidenceIds.Contains(x.Id))
            .OrderBy(x => x.At)
            .TakeLast(1600)
            .ToList();
        state.EpisodicMemories = state.EpisodicMemories
            .Where(x => !x.IsDeleted && x.EvidenceIds.Any(id => !state.DeletedEvidenceIds.Contains(id)))
            .OrderBy(x => x.IsPinned ? 1 : 0)
            .ThenBy(x => x.EndedAt)
            .TakeLast(240)
            .ToList();
        state.Clamp();
    }

    public string? ResolveFact(
        PersonalityBehaviorState state,
        string key,
        IEnumerable<KeyValuePair<string, string>> inferredFacts)
    {
        var confirmed = state.ConfirmedProfileFacts
            .Where(x => !x.IsDeleted &&
                        string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.ConfirmedAt)
            .FirstOrDefault();
        if (confirmed is not null) return confirmed.Value;
        return inferredFacts.LastOrDefault(x =>
            string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
    }

    private static void RebuildKey(
        PersonalityBehaviorState state,
        string behaviorId,
        string interactionType,
        string context,
        DateTimeOffset now)
    {
        var key = PreferenceKey.Create(behaviorId, interactionType, context);
        var events = state.RawInteractionEvents
            .Where(x => !x.IsDeleted && !state.DeletedEvidenceIds.Contains(x.Id) &&
                        PreferenceKey.Create(x.BehaviorId, x.InteractionType, x.Context) == key)
            // A continuous touch session is one learning sample.
            .GroupBy(x => x.SessionId)
            .Select(group => new
            {
                At = group.Min(x => x.At),
                Signal = group.Average(x => x.Signal),
                Opportunity = group.Max(x => x.Opportunity),
                Outcome = group.Average(x => x.OutcomeQuality),
                EvidenceIds = group.Select(x => x.Id).ToList()
            })
            .OrderBy(x => x.At)
            .ToList();

        var dates = events.Select(x => DateOnly.FromDateTime(x.At.LocalDateTime.Date)).Distinct().ToList();
        var opportunities = (int)Math.Round(events.Sum(x => x.Opportunity));
        if (events.Count < MinimumSamples ||
            dates.Count < MinimumDistinctDays ||
            opportunities < MinimumOpportunities)
        {
            state.DerivedHabitPreferences.Remove(key);
            return;
        }

        var positive = events.Count(x => x.Signal > 0.1);
        var negative = events.Count(x => x.Signal < -0.1);
        var dominant = Math.Max(positive, negative);
        var consistency = dominant / (double)Math.Max(1, positive + negative);
        var outcome = events.Average(x => x.Outcome);
        var opportunityRate = Math.Clamp(events.Count / (double)Math.Max(1, opportunities), 0, 1);
        var rawTarget = events.Average(x => x.Signal) *
                        (0.45 + outcome * 0.35) *
                        (0.55 + consistency * 0.45) *
                        (0.70 + opportunityRate * 0.30);
        var target = Math.Clamp(rawTarget, -MaximumDerivedInfluence, MaximumDerivedInfluence);
        var previous = state.DerivedHabitPreferences.GetValueOrDefault(key);
        var alpha = previous is not null && Math.Sign(previous.Weight) != 0 &&
                    Math.Sign(previous.Weight) != Math.Sign(target) ? 0.10 : 0.22;
        var weight = previous is null
            ? target * 0.35
            : previous.Weight * (1 - alpha) + target * alpha;
        var confidence = Math.Clamp(
            0.18 + dates.Count * 0.07 + events.Count * 0.018 + consistency * 0.16,
            0,
            0.92);
        state.DerivedHabitPreferences[key] = new DerivedHabitPreference
        {
            BehaviorId = behaviorId,
            InteractionType = interactionType,
            Context = context,
            Weight = Math.Clamp(weight, -MaximumDerivedInfluence, MaximumDerivedInfluence),
            Confidence = confidence,
            SampleCount = events.Count,
            OpportunityCount = opportunities,
            DistinctDays = dates.Count,
            ContextConsistency = consistency,
            ContradictorySamples = Math.Min(positive, negative),
            EvidenceIds = events.SelectMany(x => x.EvidenceIds).Distinct().ToList(),
            UpdatedAt = now,
            IsPinned = previous?.IsPinned ?? false
        };
    }
}
