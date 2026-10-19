using System.Globalization;
using System.Text.Json;
using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class PlatformProbe
{
    private int _tradeCheckedTicks;
    private string? _tradeHash;
    private readonly List<long> _tradeRestoredTicks=[];
    private void CheckParticipantTrade()
    {
        var d=JsonSerializer.Deserialize<ScenarioDefinition>(ResourceText("Fixture.trade.json"),SimulationJson.Options)!;
        var tape=ResourceText("Fixture.trade.commands.jsonl").Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(l=>JsonSerializer.Deserialize<RecordedCommand>(l,SimulationJson.Options)!).ToArray();
        var hashes=ResourceText("Fixture.trade.checkpoints.csv").Split('\n',StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l=>l.Trim().Split(','))
            .ToDictionary(r=>long.Parse(r[0],CultureInfo.InvariantCulture),r=>r[1]);
        var s=new GameSession(new(d));s.ConfigurePlayback(tape,hashes.Keys.Max(),"participant-trade");
        bool counter=false,partial=false,unsigned=false,signed=false;
        string path=ProjectSettings.GlobalizePath($"user://platform-trade-{SimulationEngine.RulesVersion}.json");
        while(s.Engine.Tick<hashes.Keys.Max())
        {
            s.Step();long tick=s.Engine.Tick;string hash=s.Engine.ComputeStateHash();
            if(hash!=hashes[tick])throw new InvalidOperationException($"Participant trade hash differs at {tick}");
            counter|=s.Engine.InspectNegotiations().Any(n=>n.Status==NegotiationStatus.Proposed && n.Current.Version==2);
            partial|=s.Engine.TransfersOf(new(2)).Length>0;
            unsigned|=s.Engine.InspectSupport().Proposals.Any(p=>p.Status==SupportProposalStatus.AcceptedAwaitingResources);
            signed|=s.Engine.InspectSupport().Commitments.Length>0;
            if(s.Engine.ObserveController(new(3)).Negotiations.Length!=0)throw new InvalidOperationException("Private negotiation leaked");
            if(tick is 32 or 33 or 34 or 35 or 38 or 51 or 69)
            {
                CheckpointStore.Save(s,path);s=CheckpointStore.Load(path).Session;
                if(s.Engine.ComputeStateHash()!=hash)throw new InvalidOperationException("Trade checkpoint changed terms or reservations");
                _tradeRestoredTicks.Add(tick);
            }
            _tradeCheckedTicks++;
        }
        if(!counter || !partial || !unsigned || !signed || s.Engine.InspectSupport().Commitments.Single().Status!=PoliticalCommitmentStatus.Executed)
            throw new InvalidOperationException("Trade probe missed critical lifecycle states");
        _tradeHash=s.Engine.ComputeStateHash();GD.Print($"DELSK_TRADE_PASS ticks={_tradeCheckedTicks} hash={_tradeHash}");
    }
}
