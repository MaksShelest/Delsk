namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    // A spatial view over an existing cargo inventory, never another owned vehicle or inventory.
    private sealed record RoadBody(long Id,ParticipantId Owner,InventoryId CargoInventory,long Driver,int Speed,int FacingX,int FacingZ);
    private RoadBody Body(OwnedVehicle v)=>new(v.Id,v.Owner,v.CargoInventory,v.Driver,Model(v.ModelId).Speed,v.FacingX,v.FacingZ);
    private RoadBody Body(EquipmentState e)
    {
        _traffic.TryGetValue(e.Id,out var t);
        return new(e.Id,new(e.Owner),new(e.Storage),0,Math.Max(1,80/_config.MoveTicksPerCell),t?.FacingX??0,t?.FacingZ??-1);
    }
    private RoadBody? FindBody(long id)=>_vehicles.TryGetValue(id,out var v)?Body(v):_equipment.TryGetValue(id,out var e)?Body(e):null;
    private IEnumerable<RoadBody> RoadBodies()=>_vehicles.Values.Select(Body).Concat(_equipment.Values.Select(Body));
    private Inventory VehicleCargo(RoadBody body)=>_economy.Inventories[body.CargoInventory.Value];
    private int RoadBodySpeed(Cell from,Cell to,RoadBody body)
    {int a=RoadLevel(from),b=RoadLevel(to);return Math.Min(body.Speed,a>0&&b>0?Roads!.Stages[Math.Min(a,b)-1].Speed:8);}
    private void AdvanceRoadEquipment(EquipmentState e,EquipmentMotion motion)
    {
        long wage=checked(e.Group.Headcount*e.Group.Wage);
        if(_economy.Accounts[_actors[e.Owner].Account].Available<wage){e.Reason="NoMoney";return;}
        var before=Traffic(e.Id);var position=e.Position;bool arrived;
        try{arrived=TrafficTravel(Body(e),motion.Path[^1]);}
        catch(RuleViolation ex){e.Reason=ex.ReasonCode;return;}
        var after=Traffic(e.Id);e.Reason=after.Reason;
        bool worked=before.Progress!=after.Progress||position!=e.Position;
        if(worked)
        {
            var tx=_economy.Begin(Tick);if(wage>0)tx.Pay(_actors[e.Owner].Account,_wages,wage,"EquipmentTravelWages");tx.Commit();
            if(wage>0)Emit("EquipmentTravelWagesPaid",e.Owner,e.Id,money:wage);
            e.MovedAtTick=Tick;
        }
        if(arrived){e.Motion=null;e.Reason="Ready";Emit("EquipmentMovementArrived",e.Owner,e.Id);}
    }
}
