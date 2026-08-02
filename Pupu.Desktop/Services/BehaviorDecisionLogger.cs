using System.IO;
using System.Text;
using System.Text.Json;
using Pupu.Behavior;

namespace Pupu.Desktop.Services;

public sealed class BehaviorDecisionLogger
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly Action<Exception, string>? _reportRecoverableException;

    public BehaviorDecisionLogger(Action<Exception, string>? reportRecoverableException = null)
    {
        _reportRecoverableException = reportRecoverableException;
    }

    public async Task AppendAsync(BehaviorDecision decision)
    {
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(StoragePaths.LogDirectory);
            var payload = JsonSerializer.Serialize(new
            {
                at = decision.At,
                selected_behavior_id = decision.SelectedBehaviorId,
                reason = decision.Reason,
                eligibility = decision.Eligibility.Select(x => new
                {
                    behavior_id = x.BehaviorId,
                    eligible = x.IsEligible,
                    reasons = x.Reasons
                }),
                candidates = decision.Candidates.Select(x => new
                {
                    behavior_id = x.BehaviorId,
                    base_weight = x.BaseWeight,
                    temperament_affinity = x.TemperamentAffinity,
                    runtime_state_fit = x.RuntimeStateFit,
                    relationship_fit = x.RelationshipFit,
                    learned_preference = x.LearnedPreference,
                    context_fit = x.ContextFit,
                    cooldown_penalty = x.CooldownPenalty,
                    repetition_penalty = x.RepetitionPenalty,
                    interruption_cost = x.InterruptionCost,
                    seeded_jitter = x.SeededJitter,
                    final_score = x.FinalScore,
                    selected = x.Selected
                })
            }, _json);
            await File.AppendAllTextAsync(
                StoragePaths.BehaviorDecisionLog,
                payload + Environment.NewLine,
                new UTF8Encoding(false));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendArbitrationAsync(BehaviorArbitrationResult result)
    {
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(StoragePaths.LogDirectory);
            var payload = JsonSerializer.Serialize(new
            {
                at = result.At,
                kind = "behavior_arbitration",
                behavior_id = result.Request.BehaviorId,
                source = result.Request.Source.ToString(),
                priority = result.Request.Priority.ToString(),
                accepted = result.Accepted,
                reason_code = result.ReasonCode,
                explanation = result.Explanation
            }, _json);
            await File.AppendAllTextAsync(
                StoragePaths.BehaviorDecisionLog,
                payload + Environment.NewLine,
                new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            _reportRecoverableException?.Invoke(ex, "behavior arbitration log");
        }
        finally
        {
            _gate.Release();
        }
    }
}
