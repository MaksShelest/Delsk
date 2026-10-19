using System.Diagnostics;
using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;

namespace Simulation.Developer;

public sealed record DecisionLogEntry(long Tick,long Actor,string Observation,string Candidates,string Goal,string Reason,int PlanStep,
    string Actions,string Results,string? ModelRequest,string? ModelReply,long? Latency,string? Fallback);

/// <summary>Research-only bounded view over the existing authoritative traces.</summary>
public sealed class DeveloperTools : IDisposable
{
    private readonly GameSession _session;
    private readonly ParticipantId _human;
    private readonly Queue<DecisionLogEntry> _journal=[];
    private readonly int _capacity;
    private bool _humanSelf;
    public long? AdvanceTarget {get;private set;}
    public string AdvanceStatus {get;private set;}="Idle";
    public DecisionLogEntry[] Journal=>_journal.ToArray();
    public DeveloperTools(GameSession session,ParticipantId human,int capacity=256)
    {
        if(capacity is <1 or >1024)throw new ArgumentOutOfRangeException(nameof(capacity));
        _session=session;_human=human;_capacity=capacity;
        foreach(var turn in session.ControllerTurns.TakeLast(capacity))Record(turn);
        session.ControllerDecided+=Record;
    }
    private static string Text(object? value,int limit=16384)
    {string text=JsonSerializer.Serialize(value,SimulationJson.Options);return text.Length<=limit?text:text[..limit]+"…";}
    private void Record(ControllerTurn t)
    {
        var d=t.Decision;var m=d.ModelTrace;
        _journal.Enqueue(new(t.Tick,t.Actor.Value,Text(t.Observation),Text(d.Candidates),d.Goal,d.Reason,d.PlanStep,Text(d.Actions),Text(t.Feedback),
            m?.RequestJson is { } request ? request[..Math.Min(request.Length,16384)] : null,
            m?.ResponseJson is { } response ? response[..Math.Min(response.Length,4096)] : null,m?.LatencyMilliseconds,
            m?.Resolution=="FallbackResolved"?m.ReasonCode:null));
        while(_journal.Count>_capacity)_journal.Dequeue();
    }
    public ResearchModification Money(ParticipantId actor,long target)=>_session.DeveloperModify("Money",actor,target);
    public ResearchModification Stock(ParticipantId actor,InventoryId inventory,string item,long target)=>_session.DeveloperModify("Stock",actor,target,inventory,item);
    public ResearchModification WageExponent(int value)=>_session.DeveloperModify("WageExponent",_human,value);
    public void StartAdvance(long target,bool humanSelf=false)
    {
        if(target<=_session.Engine.Tick || target>_session.Engine.Configuration().MaxTicks)throw new ArgumentOutOfRangeException(nameof(target));
        if(humanSelf)_session.DeveloperModify("HumanSelfBallot",_human,1);
        _humanSelf=humanSelf;AdvanceTarget=target;AdvanceStatus="Running";
    }
    public void CancelAdvance(){AdvanceTarget=null;AdvanceStatus="Cancelled";}
    /// <summary>At most eight real steps; the eight-ms budget is checked between atomic steps. Stops at unresolved choices.</summary>
    public void PumpAdvance(int maximumSteps=8)
    {
        if(maximumSteps is <1 or >64)throw new ArgumentOutOfRangeException(nameof(maximumSteps));
        if(AdvanceTarget is not { } target)return;
        var watch=Stopwatch.StartNew();bool paused=_session.Paused;
        try
        {
            for(int i=0;i<maximumSteps && watch.ElapsedMilliseconds<8;i++)
            {
                if(_session.Engine.WaitingForBallots)
                {
                    var voting=_session.Engine.Observe(_human).Elections.Voting!;
                    if(_humanSelf && voting.Awaiting.Contains(_human))
                        _session.Send(_human,new(ActionKind.SetVoteChoice,Election:voting.Election,Candidate:voting.RequiredCandidate??_human));
                    _session.PumpVoting();
                    if(_session.Engine.WaitingForBallots){AdvanceStatus=_session.WaitingForModel?"WaitingForModel":"WaitingForBallot";return;}
                }
                if(_session.Engine.Observe(_human).Elections.Mode==ElectionMode.Victory){AdvanceStatus="Victory";AdvanceTarget=null;return;}
                if(_session.Engine.Tick>=target){AdvanceStatus="Completed";AdvanceTarget=null;return;}
                if(_session.WaitingForModel){AdvanceStatus="WaitingForModel";return;}
                _session.SetPaused(false);
                if(_session.Engine.TravelQuantum!=0)_session.CompleteTravelTick();else _session.Step();
                AdvanceStatus="Running";
            }
        }
        finally {_session.SetPaused(paused);}
    }
    public void Dispose()=>_session.ControllerDecided-=Record;
}
