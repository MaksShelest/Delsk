using Agents.Algorithmic;
using Simulation.Core;
using Xunit;
using static Simulation.Tests.ServiceF2Tests;
namespace Simulation.Tests;
public class ServiceControlF2Tests
{
    [Fact]public void NpcDrivesItsOwnVehicleThroughOrdinaryActionsWithoutNumericDriver()
    {
        var(s,v,o)=Prepare(50);var npc=new AlgorithmicController(new(2,"wood",DecisionIntervalTicks:1),42,[1,2]);CommandResult? feedback=null;int bound=2000;
        while(s.Engine.InspectServices()!.Orders.Single().State!="Completed"&&bound-->0)
        {
            var observation=s.Engine.ObserveController(new(2));var copy=AlgorithmicController.Restore(npc.Capture());
            var decision=npc.Decide(observation,feedback is null?[]:[feedback]);Assert.Equal(decision.Actions,copy.Decide(observation,feedback is null?[]:[feedback]).Actions);
            string? id=null;if(decision.Actions.Length>0){var action=Assert.Single(decision.Actions);id=s.Send(new(2),action);npc.CommandSubmitted(id);}s.Step();if(id is not null){feedback=s.Engine.Result(id);Assert.Equal(ResultStatus.Accepted,feedback!.Status);}
        }
        Assert.True(bound>0);Assert.Equal(50,Run(s).Delivered);Assert.Empty(s.Engine.InspectServices()!.Execution!.Crews);Assert.DoesNotContain(s.Engine.Ledger(),l=>l.Reason.StartsWith("ServiceDriverWages:"));
        Assert.Contains(s.CommandTape,c=>c.Command.Action.Kind==ActionKind.BoardVehicle);Assert.Contains(s.CommandTape,c=>c.Command.Action.Kind==ActionKind.PickupDeliveryService);Exact(s,"npc-completed");
    }
    [Fact]public void NumericModeCannotCreateWorkersAndOwnerCanTakeOverStoppedVehicle()
    {
        var(s,v,o)=Prepare(population:0);Step(s,new(ActionKind.SetServiceAutomation,VehicleId:v.Id,Enabled:true));
        for(int n=0;n<12;n++)s.Step();Assert.Equal(0,s.Engine.InspectServices()!.Execution!.Crews.Single().Group.Headcount);Assert.Equal(v.CargoInventory,s.Engine.InspectFleet()!.Vehicles.Single().CargoInventory);Assert.Equal(0,Run(s).Loaded);
        Assert.DoesNotContain(s.Engine.Ledger(),l=>l.Reason.StartsWith("ServiceDriverWages:"));Step(s,new(ActionKind.SetServiceAutomation,VehicleId:v.Id,Enabled:false));Board(s,v);Load(s,o.Id);Deliver(s,o.Id);Assert.Equal(30,Run(s).Delivered);Exact(s,"no-workers-manual");
    }
    [Fact]public void OwnerTakesOverNumericVehicleInPlaceBetweenRoadCellCentres()
    {
        var(s,v,o)=Prepare();Step(s,new(ActionKind.SetServiceAutomation,VehicleId:v.Id,Enabled:true));
        Until(s,()=>s.Engine.InspectRoads()!.Traffic.Any(t=>t.VehicleId==v.Id&&t.Progress>0));Step(s,new(ActionKind.SetServiceAutomation,VehicleId:v.Id,Enabled:false));
        long wages=s.Engine.Ledger().Where(l=>l.Reason=="ServiceDriverWages:"+o.Id).Sum(l=>l.AmountMinor);Board(s,v);Exact(s,"manual-takeover");Load(s,o.Id);Deliver(s,o.Id);
        Assert.Equal(wages,s.Engine.Ledger().Where(l=>l.Reason=="ServiceDriverWages:"+o.Id).Sum(l=>l.AmountMinor));Assert.Equal(30,Run(s).Delivered);Exact(s,"takeover-completed");
    }
    [Fact]public void NumericHandlingWithoutWorkersCanBeStoppedAndTakenOverPersonally()
    {
        var(s,v,o)=Prepare(population:0);Board(s,v);Navigate(s,o.Id);Step(s,new(ActionKind.LeaveVehicle));Step(s,new(ActionKind.SetServiceAutomation,VehicleId:v.Id,Enabled:true));s.Step();
        Assert.Equal("Loading",Run(s).Stage);Assert.Equal(0,Run(s).Loaded);Step(s,new(ActionKind.SetServiceAutomation,VehicleId:v.Id,Enabled:false));
        Assert.Equal("AwaitingPickup",Run(s).Stage);Step(s,new(ActionKind.BoardVehicle,VehicleId:v.Id));Load(s,o.Id);Deliver(s,o.Id);Assert.Equal(30,Run(s).Delivered);Exact(s,"handling-takeover");
    }
    [Fact]public void BusyLeasePreventsConflictingTransfersAndRepeatPickupDoesNotDuplicateCargo()
    {
        var(s,v,o)=Prepare();Board(s,v);Load(s,o.Id);long cargo=Run(s).Cargo;
        Step(s,new(ActionKind.PickupDeliveryService,ServiceId:o.Id),accepted:false);Step(s,new(ActionKind.AssignVehicle,VehicleId:v.Id,Enterprise:new(8)),accepted:false);
        Step(s,new(ActionKind.Transfer,Source:v.CargoInventory,Destination:o.Quote.Terms.Source,ItemId:"wood",Quantity:1),accepted:false);Assert.Equal(cargo,Run(s).Cargo);
        Step(s,new(ActionKind.SetServiceAutomation,VehicleId:v.Id,Enabled:true),accepted:false);Deliver(s,o.Id);Exact(s,"exclusive-vehicle");
    }
}
