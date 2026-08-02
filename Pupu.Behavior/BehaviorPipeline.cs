namespace Pupu.Behavior;

public sealed class BehaviorEligibility
{
    public required string BehaviorId { get; init; }
    public bool IsEligible { get; init; }
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
}

public sealed class EligibilityFilter
{
    private readonly BehaviorArbitrator _arbitrator;

    public EligibilityFilter(BehaviorArbitrator arbitrator) =>
        _arbitrator = arbitrator ??
            throw new ArgumentNullException(nameof(arbitrator));

    public BehaviorEligibility Evaluate(
        BehaviorDefinition definition,
        PersonalityBehaviorState state,
        BehaviorContext context) =>
        _arbitrator.InspectEligibility(definition, state, context);
}

public sealed class UtilityScoring
{
    private readonly BehaviorScorer _scorer;

    public UtilityScoring(BehaviorScorer? scorer = null) =>
        _scorer = scorer ?? new BehaviorScorer();

    public BehaviorScoreBreakdown Score(
        BehaviorDefinition definition,
        PersonalityBehaviorState state,
        BehaviorContext context,
        IReadOnlyList<BehaviorHistoryEntry> history,
        IRandomSource random) =>
        _scorer.Score(definition, state, context, history, random);
}

public sealed class SelectionPolicy
{
    public double TopBand { get; init; } = 0.32;
    public double Temperature { get; init; } = 0.14;

    public BehaviorScoreBreakdown Select(
        IReadOnlyList<BehaviorScoreBreakdown> orderedCandidates,
        IRandomSource random)
    {
        if (orderedCandidates.Count == 0)
            throw new InvalidOperationException("SelectionPolicy requires at least one eligible candidate.");
        var best = orderedCandidates.Max(x => x.FinalScore);
        var top = orderedCandidates
            .Where(x => best - x.FinalScore <= TopBand)
            .OrderByDescending(x => x.FinalScore)
            .ThenBy(x => x.BehaviorId, StringComparer.Ordinal)
            .ToList();
        if (top.Count == 1) return top[0];
        var weights = top
            .Select(x => Math.Exp((x.FinalScore - best) / Math.Max(0.03, Temperature)))
            .ToList();
        var total = weights.Sum();
        var roll = random.NextDouble() * total;
        for (var index = 0; index < top.Count; index++)
        {
            roll -= weights[index];
            if (roll <= 0) return top[index];
        }
        return top[^1];
    }
}
