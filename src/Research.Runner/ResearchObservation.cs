using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Research-only disk observer and cooperative host control. Never mutates Core.</summary>
internal sealed class ResearchObservation : IDisposable
{
    private readonly string _directory;
    private readonly GameSession _session;
    private readonly ScenarioDefinition _scenario;
    private readonly long _target;
    private readonly FileStream _frames;
    private readonly StreamWriter _index, _events, _decisions;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _eventSequence, _lastTick = -1, _requests;
    private DateTimeOffset _lastAdvanced = DateTimeOffset.UtcNow;
    public ResearchObservation(string directory, GameSession session, ScenarioDefinition scenario, long target)
    {
        _directory = directory; _session = session; _scenario = scenario; _target = target;
        Directory.CreateDirectory(directory);
        _frames = new(Path.Combine(directory, "world-frames.jsonl"), FileMode.Create, FileAccess.Write, FileShare.Read);
        _index = Writer("world-index.jsonl"); _events = Writer("live-events.jsonl"); _decisions = Writer("live-decisions.jsonl");
        File.WriteAllText(Path.Combine(directory,"world-scenario.json"), JsonSerializer.Serialize(scenario, SimulationJson.Options));
        session.ControllerDecided += Decision;
        Capture(); Progress("running");
    }
    private StreamWriter Writer(string name) => new(new FileStream(Path.Combine(_directory,name),FileMode.Create,FileAccess.Write,FileShare.Read),new UTF8Encoding(false)) {AutoFlush=true};
    private void Decision(ControllerTurn turn)
    {
        _decisions.WriteLine(JsonSerializer.Serialize(turn, SimulationJson.Options));
        if(turn.Decision.ModelTrace?.Resolution=="Requested")_requests++;
    }
    public static ScenarioDefinition ApplySetup(ScenarioDefinition scenario, string path)
    {
        if(new FileInfo(path).Length>65536)throw new ArgumentException("Research setup exceeds 64KiB");
        var setup=JsonSerializer.Deserialize<ResearchSetup>(File.ReadAllText(path),SimulationJson.Options)??throw new ArgumentException("Empty research setup");
        if(setup.Participants.Length is <2 or >16 || setup.Participants.Select(p=>p.Id).Distinct().Count()!=setup.Participants.Length)
            throw new ArgumentException("Use 1 human and 1..15 distinct NPC participants");
        var result=scenario with {Participants=setup.Participants};
        ScenarioValidation.Validate(result);return result;
    }
    public void Boundary()
    {
        while(true)
        {
            string action="running";
            string path=Path.Combine(_directory,"control.json");
            if(File.Exists(path))
            {
                using var doc=JsonDocument.Parse(File.ReadAllText(path));
                action=doc.RootElement.GetProperty("action").GetString()??"running";
            }
            if(action=="cancelled")throw new ResearchCancelledException();
            if(action!="paused")return;
            Progress("paused");Thread.Sleep(100);
        }
    }
    public void Capture()
    {
        if(_session.Engine.WaitingForBallots)return; // Publish only the completed voting boundary.
        var tick=_session.Engine.Tick;
        if(tick==_lastTick)return;
        var state=_session.Engine.Inspect();
        var frame=new {
            Tick=tick, StateHash=_session.Engine.ComputeStateHash(),
            Frame=Presentation.Read(_scenario,_session.Engine,_scenario.Participants[0].Id),
            Economy=_session.Engine.PublicEconomy(),
            Actors=state.Participants.Select(p=>new {p.Id,p.Position,p.Busy,
                CashMinor=state.Accounts.Single(a=>a.Id==p.AccountId).BalanceMinor,
                Inventory=state.Inventories.Single(i=>i.Id==p.InventoryId)}),
            state.Enterprises, state.EmploymentGroups,
            Resources=_session.Engine.ResourceSites(), state.Elections
        };
        var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(frame,SimulationJson.Options)+"\n");
        long offset=_frames.Position;_frames.Write(bytes);_frames.Flush();
        _index.WriteLine(JsonSerializer.Serialize(new {Tick=tick,Offset=offset,Length=bytes.Length},SimulationJson.Options));
        foreach(var e in _session.Engine.EventsSince(_eventSequence))
        {
            _events.WriteLine(JsonSerializer.Serialize(e,SimulationJson.Options));
            _eventSequence=Math.Max(_eventSequence,e.Sequence);
        }
        _lastTick=tick;_lastAdvanced=DateTimeOffset.UtcNow;
    }
    public void Progress(string status)
    {
        var value=new { Status=status, Tick=_session.Engine.Tick,Target=_target,
            Percent=_target==0?100:Math.Round(100d*_session.Engine.Tick/_target,2),
            ElapsedSeconds=_clock.Elapsed.TotalSeconds,
            TicksPerSecond=_clock.Elapsed.TotalSeconds>0?_session.Engine.Tick/_clock.Elapsed.TotalSeconds:0,
            LastAdvancedAt=_lastAdvanced,HeartbeatAt=DateTimeOffset.UtcNow,
            ModelRequests=_requests,
            WaitingForModel=_session.WaitingForModel,WaitingForBallots=_session.Engine.WaitingForBallots};
        string path=Path.Combine(_directory,"progress.json");
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(value,SimulationJson.Options));
        File.Move(path+".tmp",path,true);
    }
    public void Dispose()
    {
        _session.ControllerDecided-=Decision;
        _frames.Dispose();_index.Dispose();_events.Dispose();_decisions.Dispose();
    }
}
internal record ResearchSetup(ParticipantDefinition[] Participants);
internal sealed class ResearchCancelledException : Exception;
