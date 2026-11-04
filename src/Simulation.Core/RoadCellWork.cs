using System.Collections.Immutable;

namespace Simulation.Core;

public sealed record RoadMaterialRequirement(string ItemId,long Required,long Available);
/// <summary>Participant-safe quote. All prices and work come from the saved road rules.</summary>
public sealed record RoadCellQuote(long VehicleId,Cell Cell,int CurrentLevel,RoadStageRule? Next,
    ImmutableArray<RoadMaterialRequirement> Materials,long? ExpectedTicks,int Staff,string Reason)
{
    public bool CanStart=>Reason is "Allowed" or "NoStaff";
}

public sealed partial class SimulationEngine
{
    // Selection is only a query. Confirmation pins the existing saved work route to one cell.
    public RoadCellQuote QuoteRoadCell(ParticipantId participant,long vehicle,Cell cell,EquipmentMode mode)
    {
        if(Roads is null||!_actors.TryGetValue(participant.Value,out var actor)||
            !_vehicles.TryGetValue(vehicle,out var v)||v.Owner!=participant)
            return new(vehicle,cell,0,null,[],null,0,"NotAvailable");
        int current=RoadLevel(cell);var next=Roads.Stages.FirstOrDefault(s=>s.Level==current+1);
        var work=_roadWorks.GetValueOrDefault(vehicle);var cargo=VehicleCargo(v);
        int staff=mode==EquipmentMode.Personal?1:work?.Group.Headcount??0;
        var materials=next is null?ImmutableArray<RoadMaterialRequirement>.Empty:
            [..next.Materials.Select(m=>new RoadMaterialRequirement(m.ItemId,m.Quantity,cargo.Free(m.ItemId)))];
        string reason;
        var access=AccessFor(participant,new(ActionKind.StartRoadCell,VehicleId:vehicle,Mode:mode));
        var traffic=_traffic.GetValueOrDefault(vehicle);
        if(!access.Allowed)reason=access.ReasonCode;
        else if(v.ModelId!="trakt_d1")reason="PaverRequired";
        else if(actor.Job is not null||HasTransfer(actor.Id)||HasSale(actor.Id))reason="ParticipantBusy";
        else if(work?.Batch is not null||work is {Enabled:true})reason="RoadBatchInProgress";
        else if(v.Installation is not null||VehicleTransferring(cargo.Id)||VehicleLogistics(vehicle) is not null)reason="VehicleBusy";
        else if(mode is not (EquipmentMode.Personal or EquipmentMode.Automatic))reason="InvalidMode";
        else if(mode==EquipmentMode.Personal&&v.Driver!=actor.Id||mode==EquipmentMode.Automatic&&v.Driver!=0)reason="PaverOperatorRequired";
        else if(!PublicRoadCell(cell))reason="ProtectedRoadCell";
        else if(next is null)reason="RoadAlreadyComplete";
        else if(cargo.Position!=cell)reason="MovePaverToWork";
        else if(traffic is not null&&(HasRoadProgress(traffic)||IsSteering(traffic)))reason="VehicleMoving";
        else if(_roadWorks.Values.Any(w=>w.Vehicle!=vehicle&&w.Batch?.Cell==cell))reason="RoadCellBusy";
        else if(UsesFreeVehicleControl&&!CanCenterPaver(v))reason="WorkSpaceOccupied";
        else if(RoadWorkBypass(cell).IsDefaultOrEmpty)reason="NoSafeBypass";
        else if(materials.Any(m=>m.Available<m.Required))reason="NoRoadMaterials";
        else if(_economy.Accounts[actor.Account].Available<next.PriceMinor)reason="NoMoney";
        else reason=staff==0?"NoStaff":"Allowed";
        return new(vehicle,cell,current,next,materials,next is null||staff==0?null:(2L*next.Ticks+staff-1)/staff,staff,reason);
    }
    private void StartRoadCell(ActorState actor,CommandEnvelope command)
    {
        var a=command.Action;var quote=QuoteRoadCell(new(actor.Id),a.VehicleId,a.Target,a.Mode);
        if(!quote.CanStart)throw new RuleViolation(quote.Reason);
        // Exact stage prevents a stale card from silently ordering a different upgrade.
        if(a.EquipmentLevel!=quote.Next!.Level)throw new RuleViolation("PreviousRoadStageRequired");
        SetRoadWork(actor,command with{Action=a with{Stops=[a.Target],Enabled=true}});
        actor.HeadingX=actor.HeadingZ=0;
        if(UsesFreeVehicleControl)_roadWorks[a.VehicleId].Reason="PaverPositioning";
    }
    private void SetRoadCellEnabled(ActorState actor,CommandEnvelope command)
    {
        var vehicle=OwnVehicle(actor,command.Action.VehicleId);
        if(!_roadWorks.TryGetValue(vehicle.Id,out var work))throw new RuleViolation("NotAvailable");
        bool cancel=!command.Action.Enabled&&work.Batch is null;
        SetRoadWork(actor,command with{Action=command.Action with{Mode=cancel?EquipmentMode.Off:work.Mode,
            EquipmentLevel=work.Level,Stops=cancel?[]:work.Route}});
    }
    private static bool PaverPositioning(RoadWorkState work)=>work.Reason is "PaverPositioning" or "WorkSpaceOccupied" or "PaverPositioningPaused" or "PaverPositioningNoStaff" or "PaverPositioningNoMoney";
    private bool CanCenterPaver(OwnedVehicle vehicle)
    {
        var body=Body(vehicle);var t=_traffic.GetValueOrDefault(vehicle.Id)??new(vehicle.Id,[]);
        var s=Steering(body,t);
        var sweep=s with{TurnFrom=s.Bearing,TurnDelta=0,StartLaneX=s.LaneX,StartLaneZ=s.LaneZ};
        return CanSteer(body,SteeringFootprint(body,sweep,true),sweep,true);
    }
    private bool AdvancePaverPositioning(RoadWorkState work,OwnedVehicle vehicle)
    {
        if(work.Mode==EquipmentMode.Automatic&&work.Group.Headcount==0){work.Reason="PaverPositioningNoStaff";return false;}
        if(!CanCenterPaver(vehicle)){work.Reason="WorkSpaceOccupied";return false;}
        var body=Body(vehicle);var t=Traffic(vehicle.Id);var s=Steering(body,t);
        if(s.LaneX==0&&s.LaneZ==0){work.Reason="Ready";return true;}
        long wage=work.Mode==EquipmentMode.Automatic?checked(work.Group.Headcount*work.Group.Wage):0;
        var actor=_actors[work.Group.Owner];
        if(_economy.Accounts[actor.Account].Available<wage){work.Reason="PaverPositioningNoMoney";return false;}
        // Bounded lateral motion inside the already occupied cell, billed as actual movement.
        int Approach(int value)=>value-Math.Clamp(value,-40,40);
        var changed=ChargeSteering(body,s,s with{LaneX=Approach(s.LaneX),LaneZ=Approach(s.LaneZ)});
        WriteSteering(body,t with{Steering=changed,Reason="PaverPositioning"});
        if(work.Mode==EquipmentMode.Automatic)
        {
            if(wage>0){var tx=_economy.Begin(Tick);tx.Pay(actor.Account,_wages,wage,"RoadWages");tx.Commit();}
            Emit("RoadWorkTravelled",actor.Id,vehicle.Id,quantity:work.Group.Headcount,money:wage);
        }
        work.Reason="PaverPositioning";return false;
    }
}
