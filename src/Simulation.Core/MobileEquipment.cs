namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    public bool UsesMobileEquipment => Fleet?.EquipmentDealerVersion == 1;
    private bool MobileMachine(long id) => UsesMobileEquipment && _equipment.ContainsKey(id);
    private bool MobileMachineWorking(long id) => MobileMachine(id) && _equipment[id] is { } e &&
        (e.Deployed == true || e.Batch is not null || e.PersonalJobId != 0 || e.Motion is not null);

    private string MobileEquipmentAccess(ActorState actor,GameAction action)
    {
        long id=action.Kind==ActionKind.BoardVehicle?action.VehicleId:action.Equipment.Value;
        if (!UsesMobileEquipment || !_equipment.TryGetValue(id,out var e)) return "Allowed";
        if(e.Owner!=actor.Id)return "NotAvailable";
        if(action.Kind==ActionKind.PlaceEquipment)return EquipmentPlacementReason(new(actor.Id),new(id));
        if(action.Kind==ActionKind.PackEquipment)
            return e.Deployed!=true?"EquipmentNotPlaced":e.Mode!=EquipmentMode.Off||e.PersonalJobId!=0||e.Motion is not null||EquipmentTransferBusy(e)||ServicePointBusy(e.Storage)?"EquipmentBusy":"Allowed";
        if(action.Kind==ActionKind.BoardVehicle && e.Deployed==true)return "EquipmentAlreadyPlaced";
        if(action.Kind==ActionKind.SetEquipmentMode && action.Mode!=EquipmentMode.Off && e.Deployed!=true)return "EquipmentNotPlaced";
        if(action.Kind==ActionKind.StartHarvesting)
            return e.Deployed!=true?"EquipmentNotPlaced":e.Mode==EquipmentMode.Automatic||e.PersonalJobId!=0||e.Batch is not null?"EquipmentBusy":EquipmentFull(e)?"NoOutputSpace":"Allowed";
        return "Allowed";
    }

    /// <summary>A pure participant-safe explanation; the command repeats these checks.</summary>
    public string EquipmentPlacementReason(ParticipantId owner, EquipmentId id)
    {
        if (!UsesMobileEquipment || !_equipment.TryGetValue(id.Value, out var e) || e.Owner != owner.Value)
            return "NotAvailable";
        if (e.Deployed == true) return "EquipmentAlreadyPlaced";
        if (e.Motion is not null || e.Batch is not null || e.PersonalJobId != 0 || EquipmentTransferBusy(e) ||
            RoadVehicleBusy(e.Id) || _actors[e.Owner].Job is not null || ServicePointBusy(e.Storage)) return "EquipmentBusy";
        if (!_resources.TryGetValue((e.Position,e.Type.ItemId), out var site)) return "WrongResourceSite";
        if (site.Owner != e.Owner || site.Rules.Public) return "NoResourceRights";
        if (_equipment.Values.Any(other => other.Id != e.Id && other.Position == e.Position) ||
            RoadBodies().Any(body => body.Id != e.Id && VehicleCargo(body).Position == e.Position) ||
            PedestrianAt(e.Position,e.Owner) || WorkBlocks(e.Id,e.Position) || TurnReservedByOther(e.Id,e.Position)) return "EquipmentSpaceBlocked";
        if (_traffic.TryGetValue(e.Id,out var t) && (HasRoadProgress(t) || IsSteering(t) ||
            t.Steering is { } steering && (steering.LaneX != 0 || steering.LaneZ != 0))) return "VehicleNotCentered";
        return "Allowed";
    }

    private void PlaceEquipment(ActorState actor, CommandEnvelope c)
    {
        var e = OwnEquipment(actor,c.Action.Equipment);
        RequireIdle(actor); RequireActionLocation(actor,c.Action);
        string reason = EquipmentPlacementReason(new(actor.Id),new(e.Id));
        if (reason != "Allowed") throw new RuleViolation(reason);
        var v = _vehicles[e.Id];
        if (v.Driver != 0 && v.Driver != actor.Id) throw new RuleViolation("VehicleBusy");
        actor.HeadingX = actor.HeadingZ = 0;
        _vehicles[e.Id] = v with { Driver=0 };
        e.Deployed = true; e.Mode = EquipmentMode.Off; e.Reason = "UserPaused";
        Emit("EquipmentPlaced",actor.Id,e.Id,reason:e.Position.X+","+e.Position.Z,command:c.CommandId);
    }

    private void PackEquipment(ActorState actor, CommandEnvelope c)
    {
        var e = OwnEquipment(actor,c.Action.Equipment);
        RequireIdle(actor); RequireActionLocation(actor,c.Action);
        if (!UsesMobileEquipment || e.Deployed != true) throw new RuleViolation("EquipmentNotPlaced");
        if (e.Mode != EquipmentMode.Off || e.PersonalJobId != 0 || e.Motion is not null ||
            EquipmentTransferBusy(e) || ServicePointBusy(e.Storage)) throw new RuleViolation("EquipmentBusy");
        // Only an unfinished reserved cycle is cancelled. Finished goods and the numeric crew stay put.
        if (e.Batch is { } batch)
        {
            var tx = _economy.Begin(Tick); tx.ReleaseCapacity(e.Storage,batch.Hold); tx.Commit();
            _resourceClaims.Remove(batch.Id); e.Batch = null;
            Emit("EquipmentCycleCancelled",actor.Id,e.Id,command:c.CommandId);
        }
        e.Deployed = false; e.Reason = "EquipmentPacked";
        if (e.Route is { } route) e.Route = route with { Enabled=false };
        Emit("EquipmentPacked",actor.Id,e.Id,command:c.CommandId);
    }

    private long PersonalHarvestInventory(ActorState actor,PersonalJob job) =>
        job.EquipmentId!=0 && MobileMachine(job.EquipmentId) ? _equipment[job.EquipmentId].Storage : actor.Inventory;
}
