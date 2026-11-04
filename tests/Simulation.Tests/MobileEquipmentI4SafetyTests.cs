using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class MobileEquipmentI4SafetyTests
{
    private static GameSession Session()=>new(new(MobileEquipmentI4Tests.Prepared()));
    private static EquipmentView Machine(GameSession s)=>s.Engine.Observe(new(1)).OwnEquipment.Single();
    private static void AtWorksite(GameSession s,OwnedVehicle vehicle)
    {
        FleetE1Tests.Board(s,vehicle);
        FleetE1Tests.Move(s,s.Engine.Configuration().ResourceNodes.Single(n=>n.ItemId=="wood"&&!n.Rules!.Public).Position);
        FleetE1Tests.Step(s,new(ActionKind.PlaceEquipment,Equipment:new(vehicle.Id)));
    }
    [Fact] public void WrongSitePublicLandAndForeignCommandsCannotDeployOrStartMining()
    {
        var s=Session();var v=MobileEquipmentI4Tests.Buy(s);var eid=new EquipmentId(v.Id);
        Assert.Equal("WrongResourceSite",s.Engine.EquipmentPlacementReason(new(1),eid));
        FleetE1Tests.Step(s,new(ActionKind.PlaceEquipment,Equipment:eid),false);
        FleetE1Tests.Step(s,new(ActionKind.SetEquipmentMode,Equipment:eid,Mode:EquipmentMode.Automatic),false);
        FleetE1Tests.Step(s,new(ActionKind.PlaceEquipment,Equipment:eid),false,actor:2);
        FleetE1Tests.Board(s,v);
        var publicSite=s.Engine.Configuration().ResourceNodes.Single(n=>n.ItemId=="wood"&&n.Rules!.Public).Position;
        FleetE1Tests.Move(s,publicSite);Assert.Equal("NoResourceRights",s.Engine.EquipmentPlacementReason(new(1),eid));
        FleetE1Tests.Step(s,new(ActionKind.PlaceEquipment,Equipment:eid),false);
        MobileEquipmentI4Tests.Exact(s);
    }
    [Fact] public void FullStorageStopsPersonalCyclesAndUnloadingAllowsAnExplicitRestart()
    {
        var s=Session();var v=MobileEquipmentI4Tests.Buy(s);AtWorksite(s,v);var id=new EquipmentId(v.Id);
        FleetE1Tests.Step(s,new(ActionKind.StartHarvesting,Equipment:id,ItemId:"wood",Quantity:2));
        StorageC1Tests.Idle(s);Assert.Equal("NoOutputSpace",Machine(s).ReasonCode);
        Assert.Equal(34,FleetE1Tests.Stock(s,v.CargoInventory,"wood"));
        Assert.Empty(s.Engine.ResourceSites().Where(r=>r.Reserved>0));
        var before=s.Engine.ComputeStateHash();var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));Assert.Equal(before,copy.Engine.ComputeStateHash());
        FleetE1Tests.Step(s,new(ActionKind.Transfer,Source:v.CargoInventory,Destination:StorageC1Tests.Carrier(s),ItemId:"wood",Quantity:4));
        StorageC1Tests.Idle(s);
        FleetE1Tests.Step(s,new(ActionKind.StartHarvesting,Equipment:id,ItemId:"wood",Quantity:2));
        FleetE1Tests.Step(s,new(ActionKind.SetEquipmentMode,Equipment:id,Mode:EquipmentMode.Off));
        Assert.False(s.Engine.Observe(new(1)).Participant.Busy);MobileEquipmentI4Tests.Exact(s);
    }
    [Fact] public void AutomaticWagesUseTheExistingCrewAndStoppingThenPackingCancelsOnlyUnfinishedWork()
    {
        var s=Session();var v=MobileEquipmentI4Tests.Buy(s);AtWorksite(s,v);var id=new EquipmentId(v.Id);
        FleetE1Tests.Step(s,new(ActionKind.SetEquipmentMode,Equipment:id,Mode:EquipmentMode.Automatic));
        int bound=30;while(Machine(s).CycleId==0&&bound-->0)s.Step();Assert.True(bound>0);
        var machine=Machine(s);var group=s.Engine.Observe(new(1)).OwnEmploymentGroups.Single(g=>g.Id==machine.GroupId);
        Assert.True(group.Headcount>0);Assert.Contains(s.Engine.Ledger(),l=>l.Reason=="Wages");
        MobileEquipmentI4Tests.Exact(s);
        FleetE1Tests.Step(s,new(ActionKind.SetEquipmentMode,Equipment:id,Mode:EquipmentMode.Off));
        machine=Machine(s);long goods=FleetE1Tests.Stock(s,v.CargoInventory,"wood");
        FleetE1Tests.Step(s,new(ActionKind.PackEquipment,Equipment:id));
        Assert.Equal(goods,FleetE1Tests.Stock(s,v.CargoInventory,"wood"));Assert.Equal(machine.StorageId,Machine(s).StorageId);
        Assert.Equal(group.Id,Machine(s).GroupId);Assert.Empty(s.Engine.ResourceSites().Where(r=>r.Reserved>0));
        FleetE1Tests.Step(s,new(ActionKind.BoardVehicle,VehicleId:v.Id));FleetE1Tests.Move(s,new(18,12));
        MobileEquipmentI4Tests.Exact(s);
    }
    [Fact] public void OneFiniteMachineIsAllocatedOnceAcrossTheSharedInclusiveWindow()
    {
        var winners=new HashSet<long>();
        for(ulong seed=1;seed<=8;seed++)
        {
            var s=new GameSession(new(MobileEquipmentI4Tests.Prepared(seed)));
            var f=s.Engine.FleetOf(new(1))!;var model=f.Models.Single(m=>m.EquipmentTypeId=="extract_wood"&&m.EquipmentLevel==1);
            var listing=f.Stock.Single(v=>v.ModelId==model.Id);
            FleetE1Tests.Step(s,new(ActionKind.BuyVehicle,VehicleId:listing.Id));
            s.AdvanceTo(5);FleetE1Tests.Step(s,new(ActionKind.BuyVehicle,VehicleId:listing.Id),actor:2);
            var v=Assert.Single(s.Engine.InspectFleet()!.Vehicles);winners.Add(v.Owner.Value);
            var machine=Assert.Single(s.Engine.Inspect().Equipment);Assert.Equal(v.Id,machine.Id.Value);
            Assert.Equal(15000,s.Engine.Ledger().Where(l=>l.Reason=="VehiclePurchase").Sum(l=>l.AmountMinor));
            Assert.All(s.Engine.Inspect().Participants,a=>Assert.Equal(0,s.Engine.Observe(a.Id).Account.ReservedMinor));
            MobileEquipmentI4Tests.Exact(s);
        }
        Assert.Equal(new long[]{1,2},winners.Order());
    }
    [Fact] public void AutomaticFullStorageWaitsWithoutWagesAndResumesAfterRealUnloading()
    {
        var s=Session();var v=MobileEquipmentI4Tests.Buy(s);AtWorksite(s,v);var id=new EquipmentId(v.Id);
        FleetE1Tests.Step(s,new(ActionKind.SetEquipmentMode,Equipment:id,Mode:EquipmentMode.Automatic));
        int budget=90;while(Machine(s).ReasonCode!="NoOutputSpace"&&budget-->0)s.Step();Assert.True(budget>0);
        long cycles=Machine(s).CompletedCycles,wages=s.Engine.Ledger().Where(l=>l.Reason=="Wages").Sum(l=>l.AmountMinor);
        s.AdvanceTo(s.Engine.Tick+5);Assert.Equal(wages,s.Engine.Ledger().Where(l=>l.Reason=="Wages").Sum(l=>l.AmountMinor));
        Assert.Equal(34,FleetE1Tests.Stock(s,v.CargoInventory,"wood"));
        FleetE1Tests.Step(s,new(ActionKind.Transfer,Source:v.CargoInventory,Destination:StorageC1Tests.Carrier(s),ItemId:"wood",Quantity:2));StorageC1Tests.Idle(s);
        budget=30;while(Machine(s).CompletedCycles==cycles&&budget-->0)s.Step();Assert.True(budget>0);
        Assert.True(s.Engine.Ledger().Where(l=>l.Reason=="Wages").Sum(l=>l.AmountMinor)>wages);
        MobileEquipmentI4Tests.Exact(s);
    }
    [Fact] public void LegacyResourcePurchaseAndItsCheckpointKeepTheHistoricalRules()
    {
        using var input=typeof(EquipmentTests).Assembly.GetManifestResourceStream("Fixture.equipment-cycle.json")!;
        var d=System.Text.Json.JsonSerializer.Deserialize<ScenarioDefinition>(input,SimulationJson.Options)!;
        var s=new GameSession(new(d));
        Assert.False(s.Engine.UsesMobileEquipment);
        // The pre-existing full legacy extraction test remains part of the physical suite.
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));
        Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());
    }
    [Fact] public void ManualHeadingUsesTheSharedWideBodySteeringQuantaAndRoadBilling()
    {
        var s=Session();var v=MobileEquipmentI4Tests.Buy(s);FleetE1Tests.Board(s,v);FleetE1Tests.Move(s,new(20,9));
        v=s.Engine.InspectFleet()!.Vehicles.Single();var before=StorageC1Tests.Inventory(s,v.CargoInventory).Position;
        s.SetTravelHeading(new(1),v.FacingX,v.FacingZ);I2Fixture.Quanta(s,15);
        Assert.Contains(s.Engine.EventsSince(0),e=>e.Type=="BillingDistance"&&e.EntityId==v.Id);
        Assert.NotEqual(before,StorageC1Tests.Inventory(s,v.CargoInventory).Position);
        MobileEquipmentI4Tests.Exact(s);s.SetTravelHeading(new(1),0,0);s.CompleteTravelTick();
        var at=s.Engine.Observe(new(1)).Participant.Position;
        FleetE1Tests.Step(s,new(ActionKind.TurnVehicle,VehicleId:v.Id));
        Assert.Equal(at,s.Engine.Observe(new(1)).Participant.Position);
        MobileEquipmentI4Tests.Exact(s);
        Assert.Equal(v.CargoInventory,Machine(s).StorageId);
    }
}
