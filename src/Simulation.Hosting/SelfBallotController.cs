using System.Collections.Immutable;
using System.Text.Json;
using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>Explicit headless human-slot policy: no economic actions, own block votes for self unless bound.</summary>
public sealed class SelfBallotController(ParticipantId actor) : IController
{
    public const string Kind="self-ballot-v1";
    public ParticipantId Actor {get;}=actor;
    public ControllerDecision Decide(ControllerObservation observation,ImmutableArray<CommandResult> results) => new("HumanSlot","ExplicitSelfBallotPolicy",[],false);
    public void CommandSubmitted(string id) => throw new InvalidOperationException("Ballot-only controller has no economic plan");
    public ControllerCheckpoint Capture()=>new(Kind,1,Actor,JsonSerializer.Serialize(Actor,SimulationJson.Options),[]);
    public static SelfBallotController Restore(ControllerCheckpoint state)
    {
        if(state.Kind!=Kind || state.Version!=1 || state.PendingCommandIds.Length!=0 || JsonSerializer.Deserialize<ParticipantId>(state.StateJson,SimulationJson.Options)!=state.Actor)
            throw new CheckpointException("InvalidSelfBallotPolicy");
        return new(state.Actor);
    }
}
