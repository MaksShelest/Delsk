using System.IO.Compression;
using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

internal static class H3ServiceAcceptance
{
    internal static void Check(string map,bool stopAtFirstCargo=false)
    {
        var d=OrdinaryNewGame.Resolve(MapDesignTests.Template(map),NpcEconomyG1Tests.Profile(),new(),vnextBalance:MapDesignTests.Balance()).Scenario;
        // This cohort is compared byte-for-byte with frozen H3 Movement1 worlds.
        d=d with{Economy=d.Economy! with{Movement=MovementRules.H2}};
        var source=d.Layout!.IndustrialLots[0].Cells[0];var target=d.Layout.IndustrialLots[1].Cells[0];
        // Explicit prepared exposure: money, personal capacity and two empty warehouses.
        // Exact geography, public resource stocks, full H2/G1 rules and timers stay intact.
        d=d with{Participants=d.Participants.Select(p=>p with{CashMinor=10_000_000,InventoryCapacity=1000}).ToArray(),
            Plots=d.Plots!.Select(p=>p.Position==source||p.Position==target?p with{OwnerId=1}:p).ToArray(),
            Buildings=[new(9,1,source,"warehouse"),new(10,1,target,"warehouse")],
            Elections=d.Elections! with{PeriodTicks=20000}};
        var s=new GameSession(new(d));var wood=d.ResourceNodes.First(n=>n.ItemId=="wood"&&n.Rules!.Public).Position;
        StorageC1Tests.Move(s,wood);StorageC1Tests.Step(s,new(ActionKind.Harvest,ItemId:"wood",Quantity:50));StorageC1Tests.Idle(s);
        var buildings=s.Engine.Observe(new(1)).OwnBuildings;
        var from=buildings.Single(b=>b.Id.Value==9).InputId;var to=buildings.Single(b=>b.Id.Value==10).InputId;
        StorageC1Tests.Put(s,from,"wood",50);var vehicle=FleetE1Tests.Buy(s,"sputnik20",2);
        ServiceF1Tests.Step(s,new(ActionKind.SetDeliveryService,VehicleId:vehicle.Id,Enabled:true),2);
        var order=ServiceF1Tests.Request(s,new(vehicle.Id,from,to,"wood",50));
        Assert.Equal(5,order.Quote.Trips);ServiceF2Tests.Exact(s,map+"-h3-pending",replay:false);
        ServiceF1Tests.Step(s,new(ActionKind.AcceptDeliveryService,ServiceId:order.Id),2);
        ServiceF2Tests.Exact(s,map+"-h3-escrow",replay:false);ServiceF2Tests.Board(s,vehicle);
        for(int trip=0;trip<5;trip++)
        {
            ServiceF2Tests.Load(s,order.Id);
            if(trip==0)
            {
                ServiceF2Tests.Exact(s,map+"-h3-loaded",replay:false);
                if(stopAtFirstCargo){Same(Frozen("loaded"),s);return;}
            }
            ServiceF2Tests.Deliver(s,order.Id);
        }
        Paid(s,vehicle,order,from,to);
        ServiceF2Tests.Exact(s,map+"-h3-five-trips");
    }
    internal static GameSession Frozen(string phase)
    {
        using var data=typeof(H3ServiceAcceptance).Assembly.GetManifestResourceStream("Fixture.h3-district-"+phase+".json.gz")!;
        using var zip=new GZipStream(data,CompressionMode.Decompress);using var reader=new StreamReader(zip);
        return CheckpointStore.Decode(reader.ReadToEnd());
    }
    internal static void Same(GameSession expected,GameSession actual)
    {
        Assert.Equal(expected.Engine.Tick,actual.Engine.Tick);
        Assert.Equal(expected.Engine.ComputeStateHash(),actual.Engine.ComputeStateHash());
        Assert.Equal(JsonSerializer.Serialize(expected.CommandTape,SimulationJson.Options),JsonSerializer.Serialize(actual.CommandTape,SimulationJson.Options));
    }
    internal static void Paid(GameSession s,OwnedVehicle vehicle,ServiceOrder order,InventoryId from,InventoryId to)
    {
        var run=s.Engine.InspectServices()!.Execution!.Runs.Single();
        Assert.Equal(50,run.Delivered);Assert.Equal(0,run.Cargo);
        Assert.Equal(order.Quote.FeeMinor,run.PaidMinor);Assert.Equal(5,run.TripNumber);
        Assert.Equal(50,FleetE1Tests.Stock(s,to,"wood"));Assert.Equal(0,FleetE1Tests.Stock(s,from,"wood"));
        Assert.Equal(0,FleetE1Tests.Stock(s,vehicle.CargoInventory,"wood"));
    }
    internal static void SettleDistrict()
    {
        var s=Frozen("loaded");var vehicle=s.Engine.FleetOf(new(2))!.Vehicles.Single().Vehicle;var order=s.Engine.InspectServices()!.Orders.Single();
        var buildings=s.Engine.Observe(new(1)).OwnBuildings;
        var from=buildings.Single(b=>b.Id.Value==9).InputId;var to=buildings.Single(b=>b.Id.Value==10).InputId;
        Assert.Equal(1,ServiceF2Tests.Run(s).TripNumber);Assert.True(ServiceF2Tests.Run(s).Cargo>0);
        for(int trip=0;trip<5;trip++){if(trip>0)ServiceF2Tests.Load(s,order.Id);ServiceF2Tests.Deliver(s,order.Id);}
        Paid(s,vehicle,order,from,to);
        ServiceF2Tests.Exact(s,"large_districts-h3-five-trips",replay:false);
        Same(Frozen("settled"),s);
    }
    internal static void ReplayDistrict()
    {
        var s=Frozen("settled");var replay=GameSession.Replay(s.Engine.Configuration(),s.CommandTape,s.Engine.Tick);
        Assert.Equal(s.Engine.ComputeStateHash(),replay.Engine.ComputeStateHash());
    }

}
public class H3CompactServiceTests{[Fact]public void FiveRealTripsAndDangerousRestores()=>H3ServiceAcceptance.Check("compact_valley");}
public class H3RiverServiceTests{[Fact]public void FiveRealTripsAndDangerousRestores()=>H3ServiceAcceptance.Check("small_river");}
public class H3DistrictServiceTests{[Fact]public void PreparationAndFirstCargoMatchTheActualSavedWorld()=>H3ServiceAcceptance.Check("large_districts",stopAtFirstCargo:true);}
public class H3DistrictSettlementTests{[Fact]public void FiveTripsSettleExactlyFromTheOriginalLoadedCargo()=>H3ServiceAcceptance.SettleDistrict();}
public class H3DistrictReplayTests{[Fact]public void EntirePaidTrajectoryReplaysFromTheOriginalStart()=>H3ServiceAcceptance.ReplayDistrict();}
public class H3RoadSceneTests
{
    [Fact]public void CompactPaverHasARealProtectedBypassAndExactContinuation()
    {H2RoadSceneTests.Capture("compact_valley",new(14,5),s=>RoadE2Tests.Exact(s,"h3-compact-active-bypass"));}
}
