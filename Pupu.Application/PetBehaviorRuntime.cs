using Pupu.Behavior;

namespace Pupu.Application;

/// <summary>
/// Application-owned behavior composition. UI layers submit perceptions and
/// execute accepted presentation intents, but never construct a second
/// arbitrator, proposal queue, lease, or cooldown history.
/// </summary>
public sealed class PetBehaviorRuntime
{
    public PetBehaviorRuntime(
        IAgentDecisionStatePort decisionState,
        IAgentMemoryPort memory,
        PersonaDefinition persona,
        IRandomSource randomSource)
    {
        ArgumentNullException.ThrowIfNull(decisionState);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(persona);
        ArgumentNullException.ThrowIfNull(randomSource);

        Arbitrator = new BehaviorArbitrator(new BehaviorScorer(), randomSource);
        Kernel = new PetAgentKernel(
            decisionState,
            memory,
            Arbitrator,
            new RulePetAgent(persona));
        ProposalQueue = new BehaviorProposalQueue();
        ProposalExecutor = new BehaviorProposalExecutor(ProposalQueue, Arbitrator);
    }

    public BehaviorArbitrator Arbitrator { get; }
    public PetAgentKernel Kernel { get; }
    public BehaviorProposalQueue ProposalQueue { get; }
    public BehaviorProposalExecutor ProposalExecutor { get; }
}
