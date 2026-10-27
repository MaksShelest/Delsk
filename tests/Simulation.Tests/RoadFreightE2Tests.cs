using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class RoadFreightE2Tests
{
    private static ScenarioDefinition Definition()
    {
        var s=FleetE1Tests.FreightDefinition();var layout=s.Layout!;
        Cell[] cells=[new(36,4),new(37,4),new(36,5),new(37,5),new(36,6),new(37,6)];
        var centre=layout.RoadCenterCells.Concat(new[]{new Cell(36,2),new Cell(36,3)}).Distinct().ToArray();
        var corridor=centre.Concat(Enumerable.Range(0,40).Select(x=>new Cell(x,2))).Distinct().ToArray();
        return s with{Id="e2-freight",StartingTreasuryMinor=500000,Economy=RoadE2Tests.Profile().World.Rules,
            Buildings=[..s.Buildings!.Where(b=>b.Id!=10),new(10,1,new(36,4),"warehouse")],
            Plots=[..s.Plots!,..cells.Select((c,n)=>new PlotDefinition(150+n,c,10000,OwnerId:1))],
            Layout=layout with{IndustrialLots=[..layout.IndustrialLots,new("lot-3",cells,new(36,3))],RoadCenterCells=centre,RoadCorridorCells=corridor,InitialCityDirtCells=centre,
                TurnCells=[new(4,1),new(12,1),new(20,1),new(28,1),new(36,1)]}};
    }
    [Fact]public void ExistingFreightCarriesOneRealCargoThroughPaverBypassAndRestoresEveryStage()
    {
        var s=new GameSession(new(Definition()));var paver=RoadE2Tests.Paver(s);RoadE2Tests.Step(s,new(ActionKind.LeaveVehicle));
        RoadE2Tests.Step(s,RoadE2Tests.Work(paver,mode:EquipmentMode.Automatic));while(s.Engine.InspectRoads()!.Works.Single().Batch is null)s.Step();
        RoadE2Tests.Step(s,RoadE2Tests.Work(paver,mode:EquipmentMode.Automatic,enabled:false));
        var car=FleetE1Tests.Buy(s,"muravey70");StorageC1Tests.Harvest(s,"wood",30);
        var source=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==9).InputId;StorageC1Tests.Put(s,source,"wood",30);
        FleetE1Tests.Board(s,car);StorageC1Tests.Move(s,new(4,4));RoadE2Tests.Step(s,new(ActionKind.LeaveVehicle));
        RoadE2Tests.Step(s,new(ActionKind.AssignVehicle,VehicleId:car.Id,Enterprise:new(8)));
        var logistics=Assert.Single(s.Engine.Observe(new(1)).OwnLogistics);var destination=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==10).InputId;
        RoadE2Tests.Step(s,new(ActionKind.SetTransportRoute,Logistics:logistics.Id,Source:source,Destination:destination,ItemId:"wood",Enabled:true));
        int bound=1000;while(s.Engine.Observe(new(1)).OwnLogistics.Single().Trip is null&&bound-->0)s.Step();Assert.True(bound>0);
        RoadE2Tests.Step(s,new(ActionKind.SetLogisticsEnabled,Logistics:logistics.Id,Direction:LogisticsDirection.Output,Enabled:false));
        var stages=new HashSet<string>();bool bypass=false;bound=1400;
        while(s.Engine.Observe(new(1)).OwnLogistics.Single().Trip is {} trip&&bound-->0)
        {
            Assert.Equal(trip.Position,StorageC1Tests.Inventory(s,car.CargoInventory).Position);
            Assert.NotEqual(new Cell(10,1),trip.Position);bypass|=trip.Position.Z==2&&trip.Position.X is >=9 and <=11;
            if(stages.Add(trip.State))RoadE2Tests.Exact(s);else s.Step();
        }
        Assert.True(bound>0);Assert.True(bypass);Assert.Contains("Outbound",stages);Assert.Contains("ReturningEmpty",stages);
        Assert.Equal(30,FleetE1Tests.Stock(s,destination,"wood"));Assert.Equal(0,FleetE1Tests.Stock(s,car.CargoInventory,"wood"));
        Assert.Equal(car.CargoInventory,s.Engine.Observe(new(1)).OwnLogistics.Single().CargoInventory);RoadE2Tests.Exact(s);
    }
}
