using System.Text.Json;
using System.Text.Json.Serialization;
using Simulation.Core;

namespace Agents.Llm;

/// <summary>Saved composition metadata for ordinary games. No credentials or running tasks.</summary>
public record LocalGameModel(OllamaSettings Adapter, ModelRuntimeSettings Runtime, HybridSettings Policy,
    string TimingPolicy="pause-at-decision-v1", string RuntimeName="ollama-http", int MaximumRetries=1)
{
    public static LocalGameModel Default => new(new(),new(3,30000),new(30,2,2,120,2));
    public void Validate()
    {
        Runtime.Validate();Policy.Validate();
        if(TimingPolicy!="pause-at-decision-v1" || RuntimeName!="ollama-http" || MaximumRetries!=1 || Policy.ApplyDelayTicks!=2 || Policy.DecisionTtlTicks!=2)
            throw new ArgumentException("Invalid ordinary model timing policy");
        using var validation=new OllamaAdapter(Adapter);
    }
    public static LocalGameModel? FromCheckpoint(ControllerCheckpoint checkpoint)
        => checkpoint.Kind==HybridController.PolicyKind ? JsonSerializer.Deserialize<HybridState>(checkpoint.StateJson,SimulationJson.Options)?.GameModel : null;
}

public sealed partial class HybridController
{
    public string? WaitingFailure => new[]{_state.Slot,_state.BallotSlot}.Where(s=>s is {Interrupted:false})
        .Select(s=>s!.Reply??_runtime.Peek(s.Request)).FirstOrDefault(r=>r is not null && r.Outcome!="Completed")?.Outcome;
    public bool CanRetry => new[]{_state.Slot,_state.BallotSlot}.Any(s=>s is {Interrupted:false,Retries:0} && (_runtime.Peek(s.Request)??s.Reply) is {Outcome:not "Completed"});
    /// <summary>At the unchanged host barrier only. Old callbacks retain their own Work identity.</summary>
    public bool RetryWaiting()
    {
        DecisionSlot? Retry(DecisionSlot? s)
        {
            if(s is not {Interrupted:false,Retries:0} || (_runtime.Peek(s.Request)??s.Reply) is not {Outcome:not "Completed"} || !_runtime.Retry(s.Request))return s;
            return s with {Status="Pending",Reply=null,Retries=1};
        }
        var a=Retry(_state.Slot);var b=Retry(_state.BallotSlot);bool changed=a!=_state.Slot || b!=_state.BallotSlot;
        _state=_state with {Slot=a,BallotSlot=b};return changed;
    }
    public void ContinueAlgorithmically()
    {
        DecisionSlot? Cancel(DecisionSlot? s){if(s is null)return null;_runtime.Cancel(s.Request);return s with {Interrupted=true,InterruptionReason="UserFallback"};}
        _state=_state with {Settings=_state.Settings with {Enabled=false},Slot=Cancel(_state.Slot),BallotSlot=Cancel(_state.BallotSlot)};
    }
}
