using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

internal static class MobileEquipmentI4Yard
{
    internal static void Check(string map,int level=0)
    {
        var d=OrdinaryNewGame.Resolve(MapDesignTests.Template(map),MobileEquipmentI4Tests.Profile(),new(3),vnextBalance:MapDesignTests.Balance()).Scenario;
        // This diagnostic fixture is funded; static non-controller participants start away from approaches.
        // The separate ordinary walkthrough uses the real roster/controllers and the original 200 coins.
        d=d with {Participants=d.Participants.Select((p,n)=>p with {CashMinor=10000000,Position=n==0?p.Position:new Cell(1+2*n,d.MapHeight-1)}).ToArray()};
        var dealer=d.Layout!.CivicObjects.Single(c=>c.Kind=="dealership");
        var exit=MapDesignTests.Template(map).Layout!.InitialCityDirtCells
            .Where(c=>!d.Participants.Skip(1).Any(p=>p.Position==c))
            .OrderBy(c=>Math.Abs(c.X-dealer.Entry.X)+Math.Abs(c.Z-dealer.Entry.Z)).First();
        foreach(var model in d.Economy!.Fleet!.Models.Where(m=>m.EquipmentTypeId is not null&&(level==0||m.EquipmentLevel==level)))
        {
            string evidence=Environment.GetEnvironmentVariable("DELSK_I4_EVIDENCE")??"work/i4/yard-debug";Directory.CreateDirectory(evidence);
            string progress=Path.Combine(evidence,map+"-level"+level+"-departures.jsonl");
            File.AppendAllText(progress,System.Text.Json.JsonSerializer.Serialize(new{model=model.Id,status="started"},SimulationJson.Options)+"\n");
            var s=new GameSession(new(d));var item=d.EquipmentTypes!.Single(t=>t.Id==model.EquipmentTypeId).ItemId;
            var v=MobileEquipmentI4Tests.Buy(s,item,model.EquipmentLevel);
            FleetE1Tests.Board(s,v);FleetE1Tests.Step(s,new(ActionKind.MoveTo,Target:exit));
            int budget=180;while(s.Engine.Observe(new(1)).Participant.Busy&&budget-->0)s.Step();
            if(budget<=0)
            {
                string output=Environment.GetEnvironmentVariable("DELSK_I4_EVIDENCE")??"work/i4/yard-debug";Directory.CreateDirectory(output);
                CheckpointStore.Save(s,Path.Combine(output,map+"-"+model.Id+"-blocked.save.json"));
                File.WriteAllText(Path.Combine(output,map+"-blocked.json"),System.Text.Json.JsonSerializer.Serialize(new{vehicle=v,at=StorageC1Tests.Inventory(s,v.CargoInventory).Position,target=exit,roads=s.Engine.InspectRoads()},SimulationJson.Options));
            }
            Assert.True(budget>0,"Trapped city machine: "+map+"/"+model.Id);
            Assert.Equal(exit,StorageC1Tests.Inventory(s,v.CargoInventory).Position);
            Assert.Equal(21,s.Engine.FleetOf(new(1))!.Stock.Length);
            Assert.DoesNotContain(s.Engine.EventsSince(0),e=>e.Type=="DealerReplaced");
            File.AppendAllText(progress,System.Text.Json.JsonSerializer.Serialize(new{model=model.Id,status="arrived",tick=s.Engine.Tick},SimulationJson.Options)+"\n");
        }
    }
}

public class I4CompactYardTests { [Fact] public void EveryMachineReachesTheExistingCityRoads()=>MobileEquipmentI4Yard.Check("compact_valley"); }
public class I4RiverYardTests { [Fact] public void EveryMachineReachesTheExistingCityRoads()=>MobileEquipmentI4Yard.Check("small_river"); }
public class I4DistrictSmallYardTests { [Fact] public void EverySmallMachineReachesTheExistingCityRoads()=>MobileEquipmentI4Yard.Check("large_districts",1); }
public class I4DistrictLargeYardTests { [Fact] public void EveryLargeMachineReachesTheExistingCityRoads()=>MobileEquipmentI4Yard.Check("large_districts",2); }
