using System.Collections.Immutable;

namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private void StartRoadMove(ActorState actor,OwnedVehicle vehicle,CommandEnvelope c)
    {
        RequireIdle(actor);
        if(RoadVehicleWorking(vehicle.Id)||VehicleTransferring(vehicle.CargoInventory.Value)||vehicle.Installation is not null)throw new RuleViolation("VehicleBusy");
        var state=Traffic(vehicle.Id);if(state.Progress>0)throw new RuleViolation("VehicleMoving");
        var path=VehiclePath(vehicle,c.Action.Target);CancelTrafficRoute(vehicle.Id);
        actor.HeadingX=actor.HeadingZ=0;
        if(path.Length==1)return;
        actor.Job=new(_nextId++,"Move",Tick,checked(Tick+path.Length*10L),path,"",0,"",c.CommandId);
        Emit("MovementStarted",actor.Id,actor.Job.Id,command:c.CommandId);
    }
    private void AdvanceRoadMove(ActorState actor,OwnedVehicle vehicle,PersonalJob job)
    {
        bool arrived=false;
        try{arrived=TrafficTravel(vehicle,job.Path[^1]);}catch(RuleViolation ex){var t=Traffic(vehicle.Id);_traffic[vehicle.Id]=t with{Reason=ex.ReasonCode};}
        // The job is an intention; the persisted traffic trajectory is its actual physical progress.
        job.CompletionTick=checked(Tick+1);
        if(!arrived)return;
        _completedPersonalJobs.Add(job.Id);actor.Job=null;
        Emit("MovementArrived",actor.Id,job.Id,command:job.CommandId);
    }
    private TrafficVehicle Traffic(long vehicle)
    {
        if(!_traffic.TryGetValue(vehicle,out var t))
        {
            t=new(vehicle,[],StoppedSince:Tick,Priority:(long)(_roadRandom!.Next()&long.MaxValue));_traffic.Add(vehicle,t);
            if(_equipment.TryGetValue(vehicle,out var equipment))Emit("EquipmentTrafficRegistered",equipment.Owner,equipment.Id,reason:equipment.Position.X+","+equipment.Position.Z);
        }
        return t;
    }
    private void CancelTrafficRoute(long vehicle)
    {
        var t=Traffic(vehicle);if(t.Progress!=0)throw new RuleViolation("VehicleMoving");
        _traffic[vehicle]=t with{Path=[],Index=0,Progress=0,Resume=null,YieldTo=0,Reason="Parked"};
    }
    private Cell[] PublicCorridorPath(Cell source,Cell target,Func<Cell,bool> allow)
        => FindRoadPath(source,target,c=>PublicRoadCell(c)&&allow(c),c=>1);
    private Cell[] FindRoadPath(Cell source,Cell target,Func<Cell,bool> allow,Func<Cell,int> cost)
    {
        var queue=new PriorityQueue<Cell,(long,int)>();var distances=new Dictionary<Cell,long>{{source,0}};
        var previous=new Dictionary<Cell,Cell>();int ordinal=0;queue.Enqueue(source,(0,ordinal++));
        while(queue.TryDequeue(out var cell,out var priority))
        {
            if(priority.Item1!=distances[cell])continue;
            if(cell==target){var path=new List<Cell>{cell};while(cell!=source){cell=previous[cell];path.Add(cell);}path.Reverse();return path.ToArray();}
            foreach(var next in RoadNeighbors(cell))
            {
                if(!allow(next))continue;long distance=checked(priority.Item1+cost(next));
                if(distances.TryGetValue(next,out var old)&&old<=distance)continue;
                distances[next]=distance;previous[next]=cell;queue.Enqueue(next,(distance,ordinal++));
            }
        }
        return [];
    }
    private bool WorkBlocks(long vehicle,Cell cell)=>_roadWorks.Values.Any(w=>w.Vehicle!=vehicle&&w.Batch?.Cell==cell);
    private bool ReservedByOther(long vehicle,Cell cell)=>RoadBodies().Any(v=>v.Id!=vehicle&&VehicleCargo(v).Position==cell)||
        _dealerStock.Any(v=>v.Position==cell)||_traffic.Values.Any(t=>t.VehicleId!=vehicle&&t.Progress>0&&t.Index<t.Path.Length-1&&t.Path[t.Index+1]==cell);
    private Cell[] VehiclePath(OwnedVehicle v,Cell target,bool avoidVehicles=false)=>VehiclePath(Body(v),target,avoidVehicles);
    private Cell[] VehiclePath(RoadBody v,Cell target,bool avoidVehicles=false)
    {
        var source=VehicleCargo(v).Position;var current=Traffic(v.Id);
        bool corridorOnly=_config.Layout!.RoadCorridorCells.Contains(source)&&_config.Layout.RoadCorridorCells.Contains(target);
        bool Allow(Cell c)=>_map.Walkable(c)&&!WorkBlocks(v.Id,c)&&(!avoidVehicles||!ReservedByOther(v.Id,c))&&
            (!corridorOnly||PublicRoadCell(c))&&
            (PublicRoadCell(c)||c==source||c==target||!_roadWorks.Values.Any(w=>w.Batch is {} batch&&Manhattan(c,batch.Cell)<=2))&&
            !_bypasses.Any(b=>b.Closing&&b.Path.Skip(1).SkipLast(1).Contains(c)&&c!=source&&!b.Users.Contains(v.Id));
        int Cost(Cell c)=>RoadLevel(c)>0?80/Roads!.Stages.Single(s=>s.Level==RoadLevel(c)).Speed+1:12;
        var path=FindRoadPath(source,target,Allow,Cost);
        if(path.Length==0)throw new RuleViolation("NoSafeVehiclePath");
        return path;
    }
    private bool MayTurn(OwnedVehicle v,Cell cell)=>MayTurn(Body(v),cell);
    private bool MayTurn(RoadBody v,Cell cell)=>!PublicRoadCell(cell)||_config.Layout!.TurnCells.Contains(cell)||
        !_config.Layout.RoadCenterCells.Contains(cell)||RoadNeighbors(cell).Count(c=>RoadLevel(c)>0)>=3;
    private void TurnVehicle(ActorState actor,CommandEnvelope c)
    {
        RequireIdle(actor);var v=OwnVehicle(actor,c.Action.VehicleId);RequireActionLocation(actor,c.Action);
        if(Roads is null||v.Driver!=actor.Id||!MayTurn(v,actor.Position)||Traffic(v.Id).Progress!=0||
            RoadNeighbors(actor.Position).All(c=>!_map.Walkable(c)||ReservedByOther(v.Id,c)))throw new RuleViolation("NoFreeTurn");
        actor.FacingX=-v.FacingX;actor.FacingZ=-v.FacingZ;actor.HeadingX=actor.HeadingZ=0;
        SyncDrivenVehicle(actor);Emit("VehicleTurned",actor.Id,v.Id,command:c.CommandId);
    }
    private bool TrafficTravel(OwnedVehicle vehicle,Cell target,int quanta=10,bool quotaAlreadyCounted=false)=>TrafficTravel(Body(vehicle),target,quanta,quotaAlreadyCounted);
    private bool TrafficTravel(RoadBody vehicle,Cell target,int quanta=10,bool quotaAlreadyCounted=false)
    {
        var t=Traffic(vehicle.Id);var at=VehicleCargo(vehicle).Position;
        if(at==target&&t.Progress==0){_traffic[vehicle.Id]=t with{Path=[],Index=0,Reason="Arrived",WaitingSince=0};return true;}
        if(t.Path.IsDefaultOrEmpty||(t.Resume??t.Path[^1])!=target)
        {
            if(t.Progress!=0)return false;
            t=t with{Path=[..VehiclePath(vehicle,target)],Index=0,Reason="Moving",Resume=null,YieldTo=0};_traffic[vehicle.Id]=t;
        }
        for(int q=0;q<quanta;q++)
        {
            t=Traffic(vehicle.Id);at=VehicleCargo(vehicle).Position;
            long stamp=_stepping?Tick:Tick+1;
            if(!quotaAlreadyCounted)
            {
                if(t.AdvancedTick==stamp&&t.AdvancedQuanta>=TravelQuantaPerTick)return false;
                t=t with{AdvancedTick=stamp,AdvancedQuanta=t.AdvancedTick==stamp?t.AdvancedQuanta+1:1};_traffic[vehicle.Id]=t;
            }
            if(t.Index>=t.Path.Length-1)
            {
                if(t.Resume is {} resume)
                {
                    var other=FindBody(t.YieldTo);
                    if(other is not null&&(Manhattan(VehicleCargo(other).Position,at)<3||
                        _traffic.TryGetValue(other.Id,out var priority)&&priority.Path.Skip(priority.Index).Contains(t.Path[0])))
                    {_traffic[vehicle.Id]=t with{Reason="YieldingInBay"};return false;}
                    _traffic[vehicle.Id]=t with{Path=[..VehiclePath(vehicle,resume)],Index=0,Resume=null,YieldTo=0,Reason="Resuming"};continue;
                }
                return at==target;
            }
            var next=t.Path[t.Index+1];int dx=next.X-at.X,dz=next.Z-at.Z;
            if(t.Progress==0&&_bypasses.Any(b=>b.Closing&&b.Path.Skip(1).SkipLast(1).Contains(next)&&!b.Users.Contains(vehicle.Id)))
            {TryReplan(vehicle,target);return false;}
            if(t.Progress==0&&(WorkBlocks(vehicle.Id,next)||ReservedByOther(vehicle.Id,next)||NarrowEntryBlocked(vehicle.Id,at,next)))
            {
                long since=t.WaitingSince==0?Tick:t.WaitingSince;
                _traffic[vehicle.Id]=t with{WaitingSince=since,Reason=WorkBlocks(vehicle.Id,next)?"PaverBypass":"TrafficWaiting"};
                if(WorkBlocks(vehicle.Id,next))TryReplan(vehicle,target);
                else if(_dealerStock.Any(s=>s.Position==next)||RoadBodies().Any(v=>v.Id!=vehicle.Id&&VehicleCargo(v).Position==next&&
                    (!_traffic.TryGetValue(v.Id,out var moving)||moving.Index>=moving.Path.Length-1&&moving.Resume is null)))
                {
                    try{_traffic[vehicle.Id]=Traffic(vehicle.Id) with{Path=[..VehiclePath(vehicle,target,true)],Index=0,Reason="Detour"};}
                    catch(RuleViolation){TryTrafficYield(vehicle,target,next);}
                }
                else TryTrafficYield(vehicle,target,next,NarrowBlockingVehicle(vehicle.Id,at,next));
                return false;
            }
            if(t.Progress==0&&dx==-vehicle.FacingX&&dz==-vehicle.FacingZ&&!MayTurn(vehicle,at))
            {if(!TryForwardTurn(vehicle,target)){_traffic[vehicle.Id]=t with{Reason="NoFreeTurn"};return false;}continue;}
            int speed=RoadBodySpeed(at,next,vehicle);int progress=checked(t.Progress+speed);
            AccrueRoadTravel(vehicle,at,Math.Min(speed,800-t.Progress));
            for(int i=0;i<_bypasses.Count;i++)
            {
                var b=_bypasses[i];if(!b.Closing&&!b.Users.Contains(vehicle.Id)&&b.Path.Skip(1).SkipLast(1).Contains(next))
                    _bypasses[i]=b with{Users=[..b.Users.Append(vehicle.Id).Order()]};
            }
            vehicle=vehicle with{FacingX=dx,FacingZ=dz};
            if(_vehicles.TryGetValue(vehicle.Id,out var owned))_vehicles[vehicle.Id]=owned with{FacingX=dx,FacingZ=dz};
            else _traffic[vehicle.Id]=t=t with{FacingX=dx,FacingZ=dz};
            if(vehicle.Driver!=0){_actors[vehicle.Driver].FacingX=dx;_actors[vehicle.Driver].FacingZ=dz;}
            if(progress>=800)
            {
                // Both cells and the edge were exclusive until arrival at the next centre.
                progress-=800;VehicleCargo(vehicle).Position=next;
                if(_equipment.TryGetValue(vehicle.Id,out var equipment))equipment.Position=next;
                Emit("TrafficCellEntered",vehicle.Owner.Value,vehicle.Id,quantity:speed,reason:at.X+","+at.Z+">"+next.X+","+next.Z);
                if(vehicle.Driver!=0)
                {
                    var actor=_actors[vehicle.Driver];actor.Position=next;actor.OffsetX=actor.OffsetZ=TravelCenter;
                    actor.FacingX=dx;actor.FacingZ=dz;_economy.Inventories[actor.Inventory].Position=next;
                    foreach(var transfer in _transfers.Values.Where(t=>t.Owner.Value==actor.Id).ToArray())CancelTransfer(transfer,"LeftTransferLocation");
                }
                t=t with{Index=t.Index+1,Progress=0,WaitingSince=0,StoppedSince=0,Reason="Moving"};
                // Carry fractional distance only along the same reserved straight edge on the next quantum.
                // Discarding the sub-quantum remainder is conservative and never exceeds a speed cap.
                if(t.Index==t.Path.Length-1)t=t with{Reason=t.Resume is null?"Arrived":"YieldingInBay",StoppedSince=Tick};
            }
            else t=t with{Progress=progress,WaitingSince=0,StoppedSince=0,Reason="Moving"};
            _traffic[vehicle.Id]=t;
        }
        t=Traffic(vehicle.Id);return t.Resume is null&&t.Index==t.Path.Length-1&&VehicleCargo(vehicle).Position==target;
    }
    private void TryReplan(RoadBody v,Cell target)
    {
        var t=Traffic(v.Id);if(t.Progress!=0)return;
        try{_traffic[v.Id]=t with{Path=[..VehiclePath(v,target)],Index=0,Reason="Detour"};}
        catch(RuleViolation){_traffic[v.Id]=t with{Reason="WaitingForSafePath"};}
    }
    private bool NarrowEntryBlocked(long vehicle,Cell from,Cell next)
    {
        foreach(var b in _bypasses)
        {
            if(b.Path.Length<3)continue;
            Cell exit;
            if(from==b.Path[0]&&next==b.Path[1])exit=b.Path[^1];
            else if(from==b.Path[^1]&&next==b.Path[^2])exit=b.Path[0];
            else continue;
            var route=Traffic(vehicle);
            if(route.Path.Skip(route.Index+1).Contains(exit)&&ReservedByOther(vehicle,exit))return true;
            var inside=b.Path.Skip(1).SkipLast(1).ToHashSet();
            if(RoadBodies().Any(v=>v.Id!=vehicle&&inside.Contains(VehicleCargo(v).Position))||
                _traffic.Values.Any(t=>t.VehicleId!=vehicle&&t.Progress>0&&t.Index<t.Path.Length-1&&inside.Contains(t.Path[t.Index+1])))return true;
        }
        return false;
    }
    private long NarrowBlockingVehicle(long vehicle,Cell from,Cell next)
    {
        foreach(var b in _bypasses)
        {
            Cell exit;
            if(from==b.Path[0]&&next==b.Path[1])exit=b.Path[^1];
            else if(from==b.Path[^1]&&next==b.Path[^2])exit=b.Path[0];
            else continue;
            return RoadBodies().FirstOrDefault(v=>v.Id!=vehicle&&VehicleCargo(v).Position==exit)?.Id??0;
        }
        return 0;
    }
    private bool TryForwardTurn(RoadBody v,Cell target)
    {
        var at=VehicleCargo(v).Position;
        // Reach a free designated turn or lateral bay facing forward, turn there, then resume.
        foreach(var first in RoadNeighbors(at).Where(c=>PublicRoadCell(c)&&!ReservedByOther(v.Id,c)&&!WorkBlocks(v.Id,c)&&
            (c.X-at.X!=-v.FacingX||c.Z-at.Z!=-v.FacingZ)))
        {
            foreach(var turn in _config.Layout!.RoadCorridorCells.Where(c=>c!=at&&MayTurn(v,c)&&!ReservedByOther(v.Id,c)&&!WorkBlocks(v.Id,c)).OrderBy(c=>Manhattan(at,c)).Take(32))
            {
                var toTurn=PublicCorridorPath(first,turn,c=>c!=at&&!ReservedByOther(v.Id,c)&&!WorkBlocks(v.Id,c));
                if(toTurn.Length==0)continue;
                var onward=PublicCorridorPath(turn,target,c=>!ReservedByOther(v.Id,c)&&!WorkBlocks(v.Id,c));if(onward.Length==0)continue;
                var t=Traffic(v.Id);_traffic[v.Id]=t with{Path=[at,..toTurn,..onward.Skip(1)],Index=0,Reason="ForwardTurn"};return true;
            }
        }
        return false;
    }
    private void TryTrafficYield(RoadBody v,Cell target,Cell blocked,long blocker=0)
    {
        var t=Traffic(v.Id);var other=blocker!=0?FindBody(blocker):RoadBodies().FirstOrDefault(o=>o.Id!=v.Id&&VehicleCargo(o).Position==blocked);
        if(other is null)return;
        var ot=Traffic(other.Id);var at=VehicleCargo(v).Position;
        if(ot.Resume is not null&&ot.YieldTo==v.Id)return;
        bool opposing=ot.Index<ot.Path.Length-1&&(ot.Path[ot.Index+1]==at||blocker!=0&&ot.Path.Skip(ot.Index+1).Contains(at));
        if(!opposing&&Tick-t.WaitingSince<2)return;
        bool priority=t.WaitingSince<(ot.WaitingSince==0?Tick:ot.WaitingSince)||
            t.WaitingSince==(ot.WaitingSince==0?Tick:ot.WaitingSince)&&t.Priority<ot.Priority;
        if(opposing&&priority){if(ot.Progress==0)TryBay(other,ot.Path[^1],v.Id);return;}
        TryBay(v,target,other.Id);
    }
    private bool TryBay(OwnedVehicle v,Cell resume,long other)=>TryBay(Body(v),resume,other);
    private bool TryBay(RoadBody v,Cell resume,long other)
    {
        var t=Traffic(v.Id);if(t.Progress!=0||t.Resume is not null)return false;
        var at=VehicleCargo(v).Position;
        var bays=_config.Layout!.RoadCorridorCells.Where(c=>!_config.Layout.RoadCenterCells.Contains(c)&&PublicRoadCell(c)&&
            !_bypasses.Any(b=>b.Path.Contains(c))&&!ReservedByOther(v.Id,c)&&!WorkBlocks(v.Id,c)).OrderBy(c=>Manhattan(at,c)).ThenBy(c=>c.Z).ThenBy(c=>c.X);
        foreach(var bay in bays.Take(32))
        {
            var path=PublicCorridorPath(at,bay,c=>!ReservedByOther(v.Id,c)&&!WorkBlocks(v.Id,c));
            if(path.Length<2||path[1].X-at.X==-v.FacingX&&path[1].Z-at.Z==-v.FacingZ&&!MayTurn(v,at))continue;
            _traffic[v.Id]=t with{Path=[..path],Index=0,Resume=resume,YieldTo=other,Reason="YieldingToBay"};
            Emit("TrafficYielded",v.Owner.Value,v.Id,reason:bay.X+","+bay.Z);return true;
        }
        return false;
    }
    private void ManualRoadTravel(ActorState actor)
    {
        var v=DrivenVehicle(actor.Id)!;var t=Traffic(v.Id);
        if(RoadVehicleWorking(v.Id)||VehicleTransferring(v.CargoInventory.Value)||v.Installation is not null)return;
        long stamp=_stepping?Tick:Tick+1;
        if(t.AdvancedTick==stamp&&t.AdvancedQuanta>=TravelQuantaPerTick)return;
        t=t with{AdvancedTick=stamp,AdvancedQuanta=t.AdvancedTick==stamp?t.AdvancedQuanta+1:1};_traffic[v.Id]=t;
        if(t.Progress>0||t.Resume is not null||t.Index<t.Path.Length-1)
        {TrafficTravel(v,t.Resume??t.Path[^1],1,true);return;}
        int x=actor.HeadingX,z=actor.HeadingZ;
        if(x==0&&z==0)return;
        // Axis steering is deliberate. Diagonal input retains the current road axis.
        if(x!=0&&z!=0){if(v.FacingX!=0)z=0;else x=0;}
        var at=VehicleCargo(v).Position;var next=new Cell(at.X+x,at.Z+z);
        if(!_map.Walkable(next)){_traffic[v.Id]=t with{Reason="ObstacleBraking"};return;}
        if(x==-v.FacingX&&z==-v.FacingZ){_traffic[v.Id]=t with{Reason="TurnRequired"};return;}
        if(RoadLevel(at)>0&&RoadLevel(next)==0)
        {
            int held=t.ExitX==x&&t.ExitZ==z?t.ExitQuanta+1:1;
            t=t with{ExitX=x,ExitZ=z,ExitQuanta=held,Reason="RoadExitPending"};_traffic[v.Id]=t;
            if(held<=Roads!.ExitHoldTicks*TravelQuantaPerTick)return;
        }
        try{TrafficTravel(v,next,1,true);}catch(RuleViolation){_traffic[v.Id]=t with{Reason="ObstacleBraking"};}
    }
    private bool RoadVehicleWorking(long vehicle)=>_roadWorks.TryGetValue(vehicle,out var w)&&w.Batch is not null;
    public TravelState VehicleTravelOf(long vehicle)
    {
        var v=FindBody(vehicle)??throw new ArgumentException("Unknown moving body");int x=TravelCenter,z=TravelCenter;
        if(_vehicles.TryGetValue(vehicle,out var owned)){x=owned.OffsetX;z=owned.OffsetZ;}
        if(Roads is not null&&_traffic.TryGetValue(vehicle,out var t)&&t.Progress>0&&t.Index<t.Path.Length-1)
        {
            var at=t.Path[t.Index];var next=t.Path[t.Index+1];x=TravelCenter+(next.X-at.X)*t.Progress*TravelUnitsPerCell/800;
            z=TravelCenter+(next.Z-at.Z)*t.Progress*TravelUnitsPerCell/800;
        }
        return new(x,z,0,0,v.FacingX,v.FacingZ);
    }
    private bool RoadVehicleBusy(long vehicle)=>ServiceVehicleBusy(vehicle)||Roads is not null&&(RoadVehicleWorking(vehicle)||_roadWorks.TryGetValue(vehicle,out var w)&&w.Enabled||
        _traffic.TryGetValue(vehicle,out var t)&&(t.Progress>0||t.Resume is not null||t.Index<t.Path.Length-1));
    private void AdvanceTrafficAssistance()
    {
        if(Roads is null)return;
        foreach(var v in _vehicles.Values.OrderBy(v=>v.Id).ToArray())
        {
            var t=Traffic(v.Id);var at=VehicleCargo(v).Position;
            if(RoadVehicleWorking(v.Id)||VehicleLogistics(v.Id)?.Trip is not null||v.Driver!=0&&_actors[v.Driver].Job is not null)continue;
            if(v.Driver!=0)for(int q=0;q<TravelQuantaPerTick;q++)ManualRoadTravel(_actors[v.Driver]);
            t=Traffic(v.Id);at=VehicleCargo(v).Position;
            if(t.Resume is not null||t.Assistance)
            {
                if(v.Driver==0)try{TrafficTravel(v,t.Resume??t.Path[^1]);}catch(RuleViolation){ }
                t=Traffic(v.Id);
                if(t.Resume is null&&t.Index==t.Path.Length-1&&t.Progress==0)_traffic[v.Id]=t with{Assistance=false};
                continue;
            }
            if(t.Progress>0||t.Index<t.Path.Length-1)continue;
            if(!_config.Layout!.RoadCenterCells.Contains(at)||v.Driver!=0&&(_actors[v.Driver].HeadingX!=0||_actors[v.Driver].HeadingZ!=0))continue;
            long since=t.StoppedSince==0?Tick:t.StoppedSince;_traffic[v.Id]=t=t with{StoppedSince=since};
            if(Tick-since==Roads!.WarningTicks){Emit("TrafficPassageWarning",v.Owner.Value,v.Id);_traffic[v.Id]=t with{Reason="PassageWarning"};}
            if(Tick-since>=Roads.ReleaseTicks&&TryBay(v,at,0))
            {var clearing=Traffic(v.Id);_traffic[v.Id]=clearing with{Resume=null,YieldTo=0,Assistance=true,Reason="ClearingPassage"};}
        }
    }
}
