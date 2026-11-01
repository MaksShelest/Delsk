using Simulation.Core;
using Simulation.Hosting;
using Xunit;
namespace Simulation.Tests;

/// <summary>Funded presentation fixture on unchanged designed geography; all purchases/work/motion use Core commands.</summary>
public class H2RoadSceneTests
{
    [Theory][InlineData("small_river")][InlineData("large_districts")]
    public void BuildRealCornerAndRetainTheActiveBypassOnBothDesignedMaps(string key)
    {
        var d=OrdinaryNewGame.Resolve(MapDesignTests.Template(key),NpcEconomyG1Tests.Profile(),new(3),vnextBalance:MapDesignTests.Balance()).Scenario;
        var factory=d.Layout!.IndustrialLots.First().Cells[0];
        d=d with{Participants=[..d.Participants.Select(p=>p with{CashMinor=10_000_000})],
            Plots=[..d.Plots!.Select(p=>p.Position==factory?p with{OwnerId=1}:p)],Buildings=[new(8,1,factory,"materials",Staff:5)],
            Elections=(d.Elections??new()) with{PeriodTicks=20000}};
        var s=new GameSession(new(d));var paver=FleetE1Tests.Buy(s,"trakt_d1");
        var source=s.Engine.ResourceSites().First(r=>r.ItemId=="stone"&&r.Public).Position;
        StorageC1Tests.Move(s,source);StorageC1Tests.Step(s,new(ActionKind.Harvest,ItemId:"stone",Quantity:3));StorageC1Tests.Idle(s);
        var plant=s.Engine.Observe(new(1)).OwnBuildings.Single();StorageC1Tests.Put(s,plant.InputId,"stone",3);
        RoadE2Tests.Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(8),Enabled:true));
        int productionGuard=100;while(FleetE1Tests.Stock(s,plant.OutputId!.Value,"gravel")<2&&productionGuard-->0)s.Step();Assert.True(productionGuard>0);
        RoadE2Tests.Step(s,new(ActionKind.SetProductionEnabled,Enterprise:new(8),Enabled:false));
        StorageC1Tests.Move(s,plant.Position);RoadE2Tests.Step(s,new(ActionKind.Transfer,Source:plant.OutputId.Value,Destination:StorageC1Tests.Carrier(s),ItemId:"gravel",Quantity:2));StorageC1Tests.Idle(s);
        StorageC1Tests.Put(s,paver.CargoInventory,"gravel",2);FleetE1Tests.Board(s,paver);StorageC1Tests.Move(s,new(5,5));
        RoadE2Tests.Step(s,RoadE2Tests.Work(paver,1,target:new(5,5)));Assert.NotEmpty(s.Engine.InspectRoads()!.Bypasses);
        Evidence(s,key+"-work");int guard=50;while(!s.Engine.InspectRoads()!.Cells.Any(c=>c.Cell==new Cell(5,5))&&guard-->0)s.Step();Assert.True(guard>0);s.Step();
        StorageC1Tests.Move(s,new(6,5));RoadE2Tests.Step(s,new(ActionKind.LeaveVehicle));Evidence(s,key+"-corner");
    }
    internal static void Evidence(GameSession s,string name)
    {if(Environment.GetEnvironmentVariable("DELSK_H2_EVIDENCE") is {} dir){Directory.CreateDirectory(dir);CheckpointStore.Save(s,Path.Combine(dir,name+".save.json"));File.WriteAllText(Path.Combine(dir,name+".tick"),s.Engine.Tick.ToString());}}
}
