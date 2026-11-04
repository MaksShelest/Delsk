using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class MobileEquipmentI4NpcTests
{
    [Fact] public void AlgorithmicOwnerBoardsDrivesPlacesAndStartsTheSameCityMachine()
    {
        var d=MobileEquipmentI4Tests.Prepared() with { Buildings=[] };
        d=d with {ResourceNodes=d.ResourceNodes.Select(n=>n.Rules!.Public?n:n with {Rules=n.Rules with {InitialOwner=2}}).ToArray()};
        var s=new GameSession(new(d));var v=MobileEquipmentI4Tests.Buy(s,actor:2);
        var controller=NpcEconomyG1Tests.Controller(2);
        NpcEconomyG1Tests.Drive(s,controller,()=>s.Engine.Observe(new(2)).OwnEquipment.Single().CompletedCycles>0,600);
        var e=s.Engine.Observe(new(2)).OwnEquipment.Single();Assert.True(e.Deployed);Assert.Equal(v.Id,e.Id.Value);
        Assert.Contains(s.CommandTape,c=>c.Command.ActorId.Value==2&&c.Command.Action.Kind==ActionKind.BoardVehicle);
        Assert.Contains(s.CommandTape,c=>c.Command.ActorId.Value==2&&c.Command.Action.Kind==ActionKind.PlaceEquipment);
        Assert.Contains(s.Engine.EventsSince(0),evt=>evt.Type=="TrafficCellEntered"&&evt.EntityId==v.Id);
        MobileEquipmentI4Tests.Exact(s);NpcEconomyG1Tests.SameCheckpoint(controller);
    }
    [Fact] public void AlgorithmicPurchaseUsesTheFiniteDealerAndNeverBuysAtTheResourceNode()
    {
        var d=MobileEquipmentI4Tests.Prepared(cash:60000);
        d=d with { Buildings=d.Buildings!.Select(b=>b with {Staff=5}).ToArray() };
        var s=new GameSession(new(d));var wood=d.ResourceNodes.Single(n=>n.ItemId=="wood"&&n.Rules!.Public).Position;
        FleetE1Tests.Move(s,wood);FleetE1Tests.Step(s,new(ActionKind.Harvest,ItemId:"wood",Quantity:40));StorageC1Tests.Idle(s);
        var input=s.Engine.Observe(new(1)).OwnEnterprises.Single().InputId;
        FleetE1Tests.Move(s,StorageC1Tests.Inventory(s,input).Position);
        FleetE1Tests.Step(s,new(ActionKind.Transfer,Source:StorageC1Tests.Carrier(s),Destination:input,ItemId:"wood",Quantity:40));StorageC1Tests.Idle(s);
        var enterprise=s.Engine.Observe(new(1)).OwnEnterprises.Single();
        FleetE1Tests.Step(s,new(ActionKind.SetProductionEnabled,Enterprise:enterprise.Id,Enabled:true));
        int bound=30;while(s.Engine.Observe(new(1)).OwnEnterprises.Single().CompletedBatches==0&&bound-->0)s.Step();Assert.True(bound>0);
        var controller=new AlgorithmicController(new(1,"wood",DecisionIntervalTicks:1,TradeEnabled:false,LogisticsEnabled:false,PoliticsEnabled:false,CoherentEconomy:true),42,[1,2]);
        NpcEconomyG1Tests.Drive(s,controller,()=>s.Engine.Observe(new(1)).OwnEquipment.Length>0,600);
        Assert.DoesNotContain(s.CommandTape,c=>c.Command.Action.Kind==ActionKind.BuyEquipment);
        var bought=Assert.Single(s.Engine.Observe(new(1)).OwnEquipment);
        Assert.Contains(s.CommandTape,c=>c.Command.Action.Kind==ActionKind.BuyVehicle&&c.Command.Action.VehicleId==bought.Id.Value);
        Assert.Equal(15000,s.Engine.Ledger().Where(l=>l.Reason=="VehiclePurchase").Sum(l=>l.AmountMinor));
        MobileEquipmentI4Tests.Exact(s);
    }
    [Fact] public void ExplicitHistoricalMovementResolutionReproducesTheH3Profile()
    {
        var map=MapDesignTests.Template("compact_valley");var p=NpcEconomyG1Tests.Profile();
        var old=OrdinaryNewGame.Resolve(map,p,new(3,MovementVersion:1),vnextBalance:MapDesignTests.Balance()).Scenario;
        Assert.Equal(MovementRules.H2,old.Economy!.Movement);Assert.Null(old.Economy.VehicleControl);
        Assert.Equal(1000000,old.MaxTicks);Assert.Equal(250000,old.Economy.Fleet!.Models.Single(m=>m.Id=="muravey70").PriceMinor);
        Assert.Equal(MovementRules.I1,OrdinaryNewGame.Resolve(map,p,new(3),vnextBalance:MapDesignTests.Balance()).Scenario.Economy!.Movement);
    }
}
