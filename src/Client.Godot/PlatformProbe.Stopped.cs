using System.Text.Json;
using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class PlatformProbe
{
    private void CheckStoppedElections()
    {
        var definition=JsonSerializer.Deserialize<ScenarioDefinition>(ResourceText("Fixture.stopped.json"),SimulationJson.Options)!;
        var tape=ResourceText("Fixture.stopped.commands.jsonl").Split('\n',StringSplitOptions.RemoveEmptyEntries)
            .Select(s=>JsonSerializer.Deserialize<RecordedCommand>(s,SimulationJson.Options)!).ToArray();
        var session=new GameSession(new(definition));session.ConfigurePlayback(tape,10);int guard=0;
        while(session.Engine.Tick<10 && ++guard<30)
        {
            session.Step();
            if(!session.Engine.WaitingForBallots)continue;
            string hash=session.Engine.ComputeStateHash();session.Engine.Step();session.Engine.MoveTravelQuantum();
            if(session.Engine.ComputeStateHash()!=hash)throw new InvalidOperationException("Godot advanced frozen election economy");
            string path=ProjectSettings.GlobalizePath("user://platform-stopped.json");CheckpointStore.Save(session,path);
            var loaded=CheckpointStore.Load(path).Session;
            if(loaded.Engine.ComputeStateHash()!=hash || !loaded.Engine.WaitingForBallots)throw new InvalidOperationException("Godot lost waiting election snapshot");
            _stoppedRestoredTicks.Add(session.Engine.Tick);session=loaded;long tick=session.Engine.Tick;session.Step();
            if(session.Engine.Tick!=tick || session.Engine.WaitingForBallots)throw new InvalidOperationException("Godot ballot processing required another economic tick");
        }
        _stoppedHash=session.Engine.ComputeStateHash();
        if(session.Engine.Tick!=10 || _stoppedHash!=ResourceText("Fixture.stopped.final.sha256").Trim() || !_stoppedRestoredTicks.SequenceEqual(new long[]{3,6}))
            throw new InvalidOperationException("Godot stopped-election replay/hash differs from runner");
    }
}
