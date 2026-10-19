using System.Globalization;
using System.Text.Json;
using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class PlatformProbe
{
    private int _physicalCheckedTicks;
    private string? _physicalHash;
    private readonly List<long> _physicalRestoredTicks=[];

    private void CheckPhysicalAutomation()
    {
        var definition=JsonSerializer.Deserialize<ScenarioDefinition>(ResourceText("Fixture.physical-automation.json"),SimulationJson.Options)!;
        var tape=ResourceText("Fixture.physical.commands.jsonl").Split('\n',StringSplitOptions.RemoveEmptyEntries)
            .Select(line=>JsonSerializer.Deserialize<RecordedCommand>(line,SimulationJson.Options)!).ToArray();
        var hashes=ResourceText("Fixture.physical.checkpoints.csv").Split('\n',StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(line=>line.Trim().Split(','))
            .ToDictionary(row=>long.Parse(row[0],CultureInfo.InvariantCulture),row=>row[1]);
        var session=new GameSession(new(definition));session.ConfigurePlayback(tape,hashes.Keys.Max(),"physical-automation");
        string path=ProjectSettings.GlobalizePath($"user://platform-physical-{SimulationEngine.RulesVersion}.json");
        bool partialLoad=false,partialReceipt=false,movingEquipment=false;
        while(session.Engine.Tick<hashes.Keys.Max())
        {
            session.Step();long tick=session.Engine.Tick;string hash=session.Engine.ComputeStateHash();
            if(hash!=hashes[tick])throw new InvalidOperationException($"Physical hash differs from runner at tick {tick}");
            if(tick is 14 or 18 or 25 or 33 or 42 or 76)
            {
                var world=session.Engine.Inspect();var trip=world.Logistics.SingleOrDefault()?.Trip;
                partialLoad|=trip is {State:"Loading",Freight:{} f} && f.Loaded>0 && f.Loaded<trip.Quantity;
                partialReceipt|=trip?.Freight is {} freight && freight.Received>0 && freight.Received<trip.Quantity;
                movingEquipment|=world.Equipment.Any(e=>e.Motion is not null);
                CheckpointStore.Save(session,path);session=CheckpointStore.Load(path).Session;
                if(session.Engine.Tick!=tick || session.Engine.ComputeStateHash()!=hash)
                    throw new InvalidOperationException("Physical checkpoint changed goods, reservations or progress");
                _physicalRestoredTicks.Add(tick);
            }
            _physicalCheckedTicks++;
        }
        var final=session.Engine.Inspect();
        if(!partialLoad || !partialReceipt || !movingEquipment || final.Logistics.Single().CompletedTrips<2 ||
            final.Inventories.Any(i=>i.ReservedCapacity!=0) || final.Accounts.Where(a=>a.Kind=="Participant").Any(a=>a.ReservedMinor!=0))
            throw new InvalidOperationException("Physical probe did not exercise partial handling, movement and released holds");
        _physicalHash=session.Engine.ComputeStateHash();
        GD.Print($"DELSK_PHYSICAL_PASS ticks={_physicalCheckedTicks} hash={_physicalHash}");
    }
}
