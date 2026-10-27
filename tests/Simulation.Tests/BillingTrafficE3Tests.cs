using Simulation.Core;
using Simulation.Hosting;
using Xunit;
using static Simulation.Tests.BillingE3Tests;

namespace Simulation.Tests;

public class BillingTrafficE3Tests
{
    [Fact]public void ExistingAutomaticFreightAccruesOnceOnItsActualCargoBodyAndRestoresInTransit()
    {
        var s=new GameSession(new(RoadFreightE2Tests.Definition() with{Economy=Profile().World.Rules}));Step(s,new(ActionKind.SetAutoPay,Enabled:false));
        var car=FleetE1Tests.Buy(s,"muravey70");StorageC1Tests.Harvest(s,"wood",30);
        var source=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==9).InputId;StorageC1Tests.Put(s,source,"wood",30);
        FleetE1Tests.Board(s,car);StorageC1Tests.Move(s,new(4,4));Step(s,new(ActionKind.LeaveVehicle));Step(s,new(ActionKind.AssignVehicle,VehicleId:car.Id,Enterprise:new(8)));
        var logistics=Assert.Single(s.Engine.Observe(new(1)).OwnLogistics);var destination=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==10).InputId;
        Step(s,new(ActionKind.SetTransportRoute,Logistics:logistics.Id,Source:source,Destination:destination,ItemId:"wood",Enabled:true));
        int bound=1200;while(s.Engine.Observe(new(1)).OwnLogistics.Single().Trip is null&&bound-->0)s.Step();Assert.True(bound>0);
        Step(s,new(ActionKind.SetLogisticsEnabled,Logistics:logistics.Id,Direction:LogisticsDirection.Output,Enabled:false));bool restored=false;
        while(s.Engine.Observe(new(1)).OwnLogistics.Single().Trip is {} trip&&bound-->0)
        {
            Assert.Equal(trip.Position,StorageC1Tests.Inventory(s,car.CargoInventory).Position);
            if(!restored&&trip.PathIndex>2){s=CheckpointStore.Decode(CheckpointStore.Encode(s));restored=true;}s.Step();
        }
        Assert.True(bound>0&&restored);Assert.Equal(30,FleetE1Tests.Stock(s,destination,"wood"));Assert.Equal(0,FleetE1Tests.Stock(s,car.CargoInventory,"wood"));Assert.Single(s.Engine.InspectFleet()!.Vehicles);
        long distance=s.Engine.EventsSince(0).Where(e=>e.Type=="BillingDistance"&&e.EntityId==car.Id).Sum(e=>e.Quantity);Assert.True(distance>800);
        Assert.Equal(distance*SimulationEngine.RoadAccrualScale/800,TotalUnits(s));Exact(s,"freight-accounting");
    }
    [Fact]public void ExistingMiningMachineUsesSameTollAndActualStorageWithoutCreatingFleetDuplicate()
    {
        var d=Prepared();var source=d.ResourceNodes.Single(r=>r.ItemId=="wood") with{Position=new(0,3),Rules=new(Public:false,InitialOwner:2,Capacity:2,RegenerationQuantity:0)};var target=new Cell(20,3);
        d=d with{ResourceNodes=[..d.ResourceNodes.Select(r=>r.ItemId=="wood"?source:r),source with{Position=target,Rules=new(Public:false,InitialOwner:2)},source with{Position=new(21,3),Rules=new(Public:true)}],
            Layout=d.Layout! with{ResourceDistricts=[..d.Layout!.ResourceDistricts.Select(r=>r.Item=="wood"?r with{Cells=[source.Position,target,new(21,3)],PublicCells=[new(21,3)]}:r)]}};
        var s=new GameSession(new(d));Step(s,new(ActionKind.SetAutoPay,Enabled:false),2);StorageC1Tests.Move(s,source.Position,2);
        Step(s,new(ActionKind.BuyEquipment,Target:source.Position,EquipmentTypeId:d.EquipmentTypes!.Single(t=>t.ItemId=="wood").Id),2);var machine=s.Engine.Observe(new(2)).OwnEquipment.Single();
        Step(s,new(ActionKind.SetExtractionRoute,Equipment:machine.Id,Stops:[target],Enabled:true),2);Step(s,new(ActionKind.SetEquipmentMode,Equipment:machine.Id,Mode:EquipmentMode.Automatic),2);
        int bound=700;bool restored=false;while(s.Engine.Observe(new(2)).OwnEquipment.Single().Position!=target&&bound-->0)
        {
            var e=s.Engine.Observe(new(2)).OwnEquipment.Single();Assert.Equal(e.Position,StorageC1Tests.Inventory(s,e.StorageId).Position);
            if(!restored&&e.Position.X>=10){s=CheckpointStore.Decode(CheckpointStore.Encode(s));restored=true;}s.Step();
        }
        Assert.True(bound>0&&restored);s.AdvanceTo(s.Engine.Tick+20);Assert.True(FleetE1Tests.Stock(s,machine.StorageId,"wood")>0);Assert.Single(s.Engine.Inspect().Equipment);Assert.Empty(s.Engine.InspectFleet()!.Vehicles);
        long distance=s.Engine.EventsSince(0).Where(e=>e.Type=="BillingDistance"&&e.EntityId==machine.Id.Value).Sum(e=>e.Quantity);Assert.True(distance>800);Assert.Equal(distance*SimulationEngine.RoadAccrualScale/800,TotalUnits(s,2));Exact(s,"mining-accounting");
    }
}
