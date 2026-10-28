using System.Collections.Immutable;
namespace Simulation.Core;

public sealed record MovementGuide(Cell Target,ImmutableArray<Cell> Path,string Reason)
{public bool Allowed=>Reason=="Ready";}
public sealed partial class SimulationEngine
{
    /// <summary>Read-only route preview using the same map/vehicle path finder as MoveTo.
    /// Traffic dictionaries, reservations, random state and time are never changed.</summary>
    public MovementGuide PreviewMovement(ParticipantId participant,Cell target)
    {
        if(!_actors.TryGetValue(participant.Value,out var actor))return new(target,[],"NotAvailable");
        try
        {
            if(Roads is not null&&DrivenVehicle(actor.Id) is {} vehicle)
            {
                RequireIdle(actor);
                if(ServiceHandlingVehicle(vehicle.Id)||RoadVehicleWorking(vehicle.Id)||VehicleTransferring(vehicle.CargoInventory.Value)||vehicle.Installation is not null)throw new RuleViolation("VehicleBusy");
                if(_traffic.TryGetValue(vehicle.Id,out var current)&&current.Progress>0)throw new RuleViolation("VehicleMoving");
                return new(target,[..VehiclePath(Body(vehicle),target,preview:true)],"Ready");
            }
            if(actor.Job is not null&&!IsHarvest(actor.Job)||HasSale(actor.Id))RequireIdle(actor);
            return new(target,[.._map.FindPath(actor.Position,target)],"Ready");
        }
        catch(RuleViolation e){return new(target,[],e.ReasonCode);}
    }
}
