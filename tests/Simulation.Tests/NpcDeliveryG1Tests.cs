using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;
namespace Simulation.Tests;
public class NpcDeliveryG1Tests
{
    [Fact]public void CustomerOrdersUsefulTransportAndCarrierPhysicallyDeliversUsingOrdinaryCommands()
    {
        var d=ServiceF2Tests.Definition();d=d with{Participants=[..d.Participants.Select(p=>p.Id==1?p with{InventoryCapacity=10}:p)],Buildings=[..d.Buildings!.Select(b=>b.Id==9?b with{Position=new(7,6)}:b)]};
        var s=new GameSession(new(d));var source=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==9).InputId;var dest=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==8).InputId;
        for(int n=0;n<20;n++){StorageC1Tests.Harvest(s,"wood",5);StorageC1Tests.Put(s,source,"wood",5);}
        var v=FleetE1Tests.Buy(s,"vector200",2);ServiceF2Tests.Step(s,new(ActionKind.SetDeliveryService,VehicleId:v.Id,Enabled:true));
        var customer=NpcEconomyG1Tests.Controller(1);var observation=s.Engine.ObserveController(new(1),true);Assert.Contains(observation.EconomicOpportunities!.ServiceQuotes,q=>q.Terms.Destination==dest);
        var decision=customer.Decide(observation,[]);var action=Assert.Single(decision.Actions);Assert.True(action.Kind==ActionKind.RequestDeliveryService,decision.Goal+";"+string.Join(";",observation.EconomicOpportunities.ServiceQuotes.Select(q=>$"{q.Terms.Destination.Value}:{q.Terms.Quantity}/{q.FeeMinor}/{q.LoadedDistance}/{q.DestinationCapacity};walk={observation.Map.MoveTicksPerCell}")));ServiceF2Tests.Step(s,action,1);
        var carrier=NpcEconomyG1Tests.Controller(2);NpcEconomyG1Tests.Drive(s,carrier,()=>s.Engine.InspectServices()!.Orders.Single().State=="Completed");
        Assert.Equal(action.Quantity,ServiceF2Tests.Run(s).Delivered);Assert.Equal(action.Quantity,FleetE1Tests.Stock(s,dest,"wood"));Assert.Contains(s.Engine.Ledger(),l=>l.Reason.StartsWith("ServicePayment:"));
        Assert.Equal(s.Engine.ComputeStateHash(),CheckpointStore.Decode(CheckpointStore.Encode(s)).Engine.ComputeStateHash());
    }
    [Theory][InlineData(NpcDifficulty.Easy)][InlineData(NpcDifficulty.Medium)][InlineData(NpcDifficulty.Hard)]public void OwnerRecoversUnstaffedAutomationThroughActualPersonalDriving(NpcDifficulty difficulty)
    {
        var(s,v,o)=ServiceF2Tests.Prepare(population:0);ServiceF2Tests.Step(s,new(ActionKind.SetServiceAutomation,VehicleId:v.Id,Enabled:true));s.AdvanceTo(s.Engine.Tick+35);
        var c=NpcEconomyG1Tests.Controller(2,difficulty:difficulty);NpcEconomyG1Tests.Drive(s,c,()=>s.Engine.InspectServices()!.Orders.Single().State=="Completed");
        Assert.Equal(30,ServiceF2Tests.Run(s).Delivered);Assert.DoesNotContain(s.Engine.Ledger(),l=>l.Reason.StartsWith("ServiceDriverWages:"));Assert.Contains(s.CommandTape,r=>r.Command.Action.Kind==ActionKind.SetServiceAutomation&&!r.Command.Action.Enabled);
    }
    [Fact]public void OverflowIsPhysicallyCollectedAndSoldBeforeItsDeadline()
    {
        var(s,v,o)=ServiceF2Tests.Prepare(150);ServiceF2Tests.Board(s,v);for(int n=0;n<5;n++){ServiceF2Tests.Load(s,o.Id);ServiceF2Tests.Deliver(s,o.Id);}
        var before=s.Engine.ObserveController(new(1),true).EconomicOpportunities!.Overflow.Sum(p=>p.Lot.Quantity);Assert.True(before>0);
        var c=NpcEconomyG1Tests.Controller(1);NpcEconomyG1Tests.Drive(s,c,()=>s.Engine.ObserveController(new(1),true).EconomicOpportunities!.Overflow.Sum(p=>p.Lot.Quantity)<before);
        Assert.Contains(s.CommandTape,r=>r.AuthenticatedActor.Value==1&&r.Command.Action.Kind==ActionKind.TakeOverflow);Assert.DoesNotContain(s.Engine.EventsSince(0),e=>e.Type=="OverflowLost");
        var restored=AlgorithmicController.Restore(c.Capture());Assert.Equal(c.Capture().StateJson,restored.Capture().StateJson);Assert.Equal(s.Engine.ComputeStateHash(),CheckpointStore.Decode(CheckpointStore.Encode(s)).Engine.ComputeStateHash());
    }
}
