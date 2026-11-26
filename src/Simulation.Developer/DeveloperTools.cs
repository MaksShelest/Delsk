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
    private ParticipantId? _advanceCandidate;
    private bool _continueAfterVictory;
    private int _initialElections;
    private readonly Stopwatch _advanceClock = new();
    public long? AdvanceTarget {get;private set;}
    public string AdvanceStatus {get;private set;}="Idle";
    public long? AdvanceStart { get; private set; }
    public long? AdvanceGoal { get; private set; }
    public string? AdvanceError { get; private set; }
    public double AdvanceElapsedSeconds => _advanceClock.Elapsed.TotalSeconds;
    public int AdvanceElections => AdvanceStart.HasValue ? _session.Engine.CompletedElections - _initialElections : 0;
    public DecisionLogEntry[] Journal=>_journal.ToArray();
    public DeveloperTools(GameSession session,ParticipantId human,int capacity=256)
    {
        if(capacity is <1 or >1024)throw new ArgumentOutOfRangeException(nameof(capacity));
        _session=session;_human=human;_capacity=capacity;
        foreach(var turn in session.ControllerTurnsTail(capacity))Record(turn);
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
    public ResearchModification IndustryExperience(ParticipantId actor,string industry,long target)=>_session.DeveloperModify("IndustryExperience",actor,target,item:industry);
    public ResearchModification WageExponent(int value)=>_session.DeveloperModify("WageExponent",_human,value);
    public void StartAdvance(long target, bool humanSelf)
    {
        ValidateAdvance(target, humanSelf ? _human : null);
        if (humanSelf) _session.DeveloperModify("HumanSelfBallot", _human, 1);
        StartAdvance(target, humanSelf ? _human : null);
    }
    public void StartAdvance(long target, ParticipantId? candidate = null, bool continueAfterVictory = false)
    {
        ValidateAdvance(target, candidate);
        _advanceCandidate = candidate;
        _continueAfterVictory = continueAfterVictory;
        _initialElections = _session.Engine.CompletedElections;
        AdvanceStart = _session.Engine.Tick;
        AdvanceTarget = AdvanceGoal = target;
        AdvanceError = null;
        AdvanceStatus = "Running";
        _advanceClock.Restart();
    }
    private void ValidateAdvance(long target, ParticipantId? candidate)
    {
        if (AdvanceTarget.HasValue) throw new InvalidOperationException("AdvanceAlreadyRunning");
        var definition = _session.Engine.Configuration();
        if (target <= _session.Engine.Tick || target > definition.MaxTicks) throw new ArgumentOutOfRangeException(nameof(target));
        if (candidate is { } id && (definition.Elections?.PauseForBallots != true || !definition.Participants.Any(p => p.Id == id.Value)))
            throw new ArgumentException("InvalidAdvanceCandidate");
    }
    private void EndAdvance(string status, string? error = null)
    {
        _advanceClock.Stop();
        _advanceCandidate = null;
        _continueAfterVictory = false;
        AdvanceTarget = null;
        AdvanceStatus = status;
        AdvanceError = error;
    }
    public void CancelAdvance() { if (AdvanceTarget.HasValue) EndAdvance("Cancelled"); }
    /// <summary>At most eight real steps; the eight-ms budget is checked between atomic steps. Stops at unresolved choices.</summary>
    public void PumpAdvance(int maximumSteps=8)
    {
        if(maximumSteps is <1 or >64)throw new ArgumentOutOfRangeException(nameof(maximumSteps));
        if(AdvanceTarget is not { } target)return;
        var watch=Stopwatch.StartNew();bool paused=_session.Paused;
        try
        {
            for(int i=0;;i++)
            {
                if(_session.Engine.WaitingForBallots)
                {
                    if (_advanceCandidate is { } candidate && _session.SubmitConfiguredBallot(_human, candidate) is { Status: ResultStatus.Rejected } rejection)
                    { EndAdvance("Failed", rejection.ReasonCode); return; }
                    _session.PumpVoting();
                    if(_session.Engine.WaitingForBallots)
                    {
                        if (_session.WaitingForModel) AdvanceStatus = "WaitingForModel";
                        else if (_session.Engine.Observe(_human).Elections.Voting!.Awaiting.Contains(_human)) AdvanceStatus = "WaitingForBallot";
                        else EndAdvance("Failed", "MissingNpcBallot");
                        return;
                    }
                }
                if (_session.Engine.PoliticalMode == ElectionMode.Victory)
                {
                    if (!_continueAfterVictory) { EndAdvance("Victory"); return; }
                    _session.ContinueResearchAfterVictory(_human);
                }
                if(_session.Engine.Tick>=target){EndAdvance("Completed");return;}
                if(_session.WaitingForModel){AdvanceStatus="WaitingForModel";return;}
                if (i >= maximumSteps || watch.ElapsedMilliseconds >= 8) return;
                long before = _session.Engine.Tick;
                _session.SetPaused(false);
                if(_session.Engine.TravelQuantum!=0)_session.CompleteTravelTick();else _session.Step();
                if (_session.Engine.Tick == before) { EndAdvance("Failed", "NoTickProgress"); return; }
                AdvanceStatus="Running";
            }
        }
        catch (Exception failure) when (failure is InvalidOperationException or ArgumentException or OverflowException)
        { EndAdvance("Failed", failure.Message); }
        finally {_session.SetPaused(paused);}
    }
    public void Dispose()=>_session.ControllerDecided-=Record;
}
