using Simulation.Core;
namespace Agents.Algorithmic;
public sealed partial class AlgorithmicController
{
    private ControllerDecision? ServiceExecutionDecision(ControllerObservation o)
    {
        if(o.Services?.Execution is not {} execution||o.Fleet is not {} fleet)return null;
        var order=o.Services.OwnOrders.FirstOrDefault(x=>x.Carrier==Actor&&x.State=="Accepted");
        if(order is null)return null;
        var run=execution.Runs.Single(r=>r.Run.Order==order.Id);
        if(run.Automatic)return null;
        if(o.Participant.Busy||run.Run.Stage is "Loading" or "Unloading" or "UnloadingReturn")return Wait("ServicePhysicalWork");
        var car=fleet.Vehicles.Single(v=>v.Vehicle.Id==run.Vehicle);
        GameAction action;
        var driving=fleet.Vehicles.FirstOrDefault(v=>v.Vehicle.Driver==Actor.Value);
        if(driving is not null&&driving.Vehicle.Id!=car.Vehicle.Id)action=new(ActionKind.LeaveVehicle,VehicleId:driving.Vehicle.Id);
        else if(car.Vehicle.Driver==0)
            action=o.Participant.Position==car.Position?new(ActionKind.BoardVehicle,VehicleId:car.Vehicle.Id):new(ActionKind.MoveTo,Target:car.Position);
        else if(!run.AtTarget)action=new(ActionKind.NavigateDeliveryService,ServiceId:order.Id);
        else action=new(run.Run.Cargo>0?ActionKind.UnloadDeliveryService:ActionKind.PickupDeliveryService,ServiceId:order.Id);
        return Choose(o,[One("ExecuteDeliveryService",order.Id.ToString(),2000000,action)]);
    }
}
