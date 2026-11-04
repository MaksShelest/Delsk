using System.Collections.Immutable;

namespace Simulation.Core;

/// <summary>Opt-in physical steering profile. Old resolved worlds keep their original rules.</summary>
public sealed record VehicleControlRules(int Version=1,int ExitHoldQuanta=5,int LaneOffset=200)
{
    public static VehicleControlRules I2 {get;}=new();
    public void Validate()
    {if(this!=I2)throw new ArgumentException("Unsupported vehicle control profile");}
    public static int TurnQuanta(int nominalSpeed,bool road,bool reverse)
    {
        int length=road?reverse?640:320:0;
        return Math.Max(reverse?4:2,(length*800*10+60*nominalSpeed*1000-1)/(60*nominalSpeed*1000));
    }
    public static long PathTurnQuanta(IReadOnlyList<Cell> path,Func<Cell,bool> road,Func<Cell,int> speed,Cell? initial=null)
    {
        long total=0;var previous=initial??(path.Count>1?new(path[1].X-path[0].X,path[1].Z-path[0].Z):new Cell(0,0));
        for(int n=0;n<path.Count-1;n++)
        {
            var direction=new Cell(path[n+1].X-path[n].X,path[n+1].Z-path[n].Z);
            if(previous!=new Cell(0,0)&&previous!=direction)total=checked(total+TurnQuanta(speed(path[n]),road(path[n]),direction.X==-previous.X&&direction.Z==-previous.Z));
            previous=direction;
        }
        return total;
    }
}

// Coordinates are thousandths of a cell relative to the existing cargo position.
// Bearing: north=0, east=4000, south=8000, west=12000. No floating point in Core.
public sealed record VehicleSteering(int LaneX,int LaneZ,int Bearing,
    int TurnFrom=0,int TurnDelta=0,int TurnQuantum=0,int TurnQuanta=0,
    int StartLaneX=0,int StartLaneZ=0,int DistanceRemainder=0,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<Cell> ReservedCells=default,
    int ClearanceX=0,int ClearanceZ=0);

public sealed partial class SimulationEngine
{
    public bool UsesFreeVehicleControl=>_config.Economy?.VehicleControl is not null;
    private VehicleControlRules Control=>_config.Economy!.VehicleControl!;
    private static bool IsSteering(TrafficVehicle t)=>t.Steering is {TurnQuanta:>0};
    private static int Bearing(int x,int z)=>x>0?4000:z>0?8000:x<0?12000:0;
    private static int NormalizeBearing(int angle)=>(angle%16000+16000)%16000;
    private static Cell BearingVector(int angle)
    {
        // Sixteen fixed directions; interpolation also stays in integer arithmetic.
        Cell[] vectors=[new(0,-1000),new(383,-924),new(707,-707),new(924,-383),
            new(1000,0),new(924,383),new(707,707),new(383,924),new(0,1000),
            new(-383,924),new(-707,707),new(-924,383),new(-1000,0),
            new(-924,-383),new(-707,-707),new(-383,-924)];
        angle=NormalizeBearing(angle);var a=vectors[angle/1000];var b=vectors[(angle/1000+1)%16];
        int fraction=angle%1000;return new(a.X+(b.X-a.X)*fraction/1000,a.Z+(b.Z-a.Z)*fraction/1000);
    }
    private VehicleSteering Steering(RoadBody body,TrafficVehicle t)=>t.Steering??new(0,0,Bearing(body.FacingX,body.FacingZ));
    private bool AuthorizedVehicleStop(RoadBody body,Cell cell)=>ExecutingService(body.Id) is {} service&&
        (service.Quote.SourcePosition==cell||service.Quote.DestinationPosition==cell||service.Quote.ReturnPosition==cell)||
        VehicleLogistics(body.Id)?.Trip is {} trip&&(trip.Path[0]==cell||trip.Path[^1]==cell);
    private bool EquipmentStockAt(Cell cell)=>UsesMobileEquipment&&_dealerStock.Any(s=>s.Position==cell&&Model(s.ModelId).EquipmentTypeId is not null);
    private bool ControlCellAllowed(RoadBody body,Cell cell)=>_map.Walkable(cell)&&!EquipmentStockAt(cell)&&
        (!_plots.Values.Any(p=>p.Definition.Position==cell&&p.Owner is {} owner&&owner!=body.Owner.Value)||AuthorizedVehicleStop(body,cell))&&
        !_resources.Values.Any(r=>r.Definition.Position==cell&&!r.Rules.Public&&r.Owner!=body.Owner.Value&&!(r.Tenant==body.Owner.Value&&Tick<r.LeaseUntil));
    private bool PedestrianAt(Cell cell,long driver)=>_actors.Values.Any(a=>a.Id!=driver&&DrivenVehicle(a.Id) is null&&
        (a.Position==cell||a.Job is {Kind:"Move",SegmentTicks:>0} job&&job.PathIndex<job.Path.Length-1&&job.Path[job.PathIndex+1]==cell));
    private IEnumerable<(int X,int Z,int Ex,int Ez,int Radius)> SteeringBounds(RoadBody body,VehicleSteering s,bool center=false)
    {
        var at=VehicleCargo(body).Position;bool wide=WideBody(body);
        int halfLength=wide?430:400,halfWidth=wide?380:190;
        int radius=SteeringRadius(body,at);var from=BearingVector(s.TurnFrom);
        int sx=-from.Z*radius/1000,sz=from.X*radius/1000;
        //32 fixed samples plus4/1000 margin bound the piecewise-linear bearing
        // and quadratic clearance between samples. Other bodies use these same boxes.
        for(int n=0;n<=32;n++)
        {
            var f=BearingVector(s.TurnFrom+s.TurnDelta*n/32);
            int x=500-f.Z*radius/1000+(s.StartLaneX-sx)*(32-n)/32+s.ClearanceX*4*n*(32-n)/1024;
            int z=500+f.X*radius/1000+(s.StartLaneZ-sz)*(32-n)/32+s.ClearanceZ*4*n*(32-n)/1024;
            if(center){x=500+s.StartLaneX*(32-n)/32;z=500+s.StartLaneZ*(32-n)/32;}
            int norm=Math.Max(1,IntegerDistance(f.X,f.Z));
            int ex=(Math.Abs(f.X)*halfLength+Math.Abs(f.Z)*halfWidth+norm-1)/norm+4;
            int ez=(Math.Abs(f.Z)*halfLength+Math.Abs(f.X)*halfWidth+norm-1)/norm+4;
            yield return(at.X*1000+x,at.Z*1000+z,ex,ez,IntegerDistance(halfLength,halfWidth)+5);
        }
    }
    private ImmutableArray<Cell> SteeringFootprint(RoadBody body,VehicleSteering s,bool center=false)
    {
        var cells=new HashSet<Cell>();int Floor(int value)=>value>=0?value/1000:(value-999)/1000;
        foreach(var b in SteeringBounds(body,s,center))
            for(int z=Floor(b.Z-b.Ez);z<=Floor(b.Z+b.Ez);z++)
                for(int x=Floor(b.X-b.Ex);x<=Floor(b.X+b.Ex);x++)
                {
                    int dx=Math.Max(0,Math.Max(x*1000-b.X,b.X-(x+1)*1000));
                    int dz=Math.Max(0,Math.Max(z*1000-b.Z,b.Z-(z+1)*1000));
                    if((long)dx*dx+(long)dz*dz<=(long)b.Radius*b.Radius)cells.Add(new(x,z));
                }
        return [..cells.OrderBy(c=>c.Z).ThenBy(c=>c.X)];
    }
    private bool TurnReservedByOther(long vehicle,Cell cell)=>UsesFreeVehicleControl&&_traffic.Values.Any(t=>t.VehicleId!=vehicle&&IsSteering(t)&&
        t.Steering is {ReservedCells.IsDefaultOrEmpty:false} s&&s.ReservedCells.Contains(cell));
    private bool WideBody(RoadBody body)=>!_vehicles.TryGetValue(body.Id,out var vehicle)||vehicle.ModelId=="trakt_d1"||Model(vehicle.ModelId).EquipmentTypeId is not null;
    private bool LaneBody(RoadBody body)=>!WideBody(body);
    // A .76-cell body must fit inside a single cell while travelling straight.
    // Wide bodies keep exclusive reservations and a smaller right offset.
    private int SteeringRadius(RoadBody body,Cell cell)=>RoadLevel(cell)>0?Math.Min(Control.LaneOffset,WideBody(body)?100:Control.LaneOffset):0;
    private bool OpposingLaneBodies(long vehicle,long other,Cell cell)
    {
        if(!UsesFreeVehicleControl||FindBody(vehicle) is not {} a||FindBody(other) is not {} b||!LaneBody(a)||!LaneBody(b)||
            a.FacingX!=-b.FacingX||a.FacingZ!=-b.FacingZ||RoadLevel(cell)==0||
            RoadLevel(VehicleCargo(a).Position)==0||RoadLevel(VehicleCargo(b).Position)==0||
            _bypasses.Any(p=>p.Path.Contains(cell)||p.Path.Contains(VehicleCargo(a).Position)||p.Path.Contains(VehicleCargo(b).Position)))return false;
        return (!_traffic.TryGetValue(a.Id,out var ta)||!IsSteering(ta))&&(!_traffic.TryGetValue(b.Id,out var tb)||!IsSteering(tb));
    }
    private int LaneReservation(long vehicle)
    {
        if(!UsesFreeVehicleControl||FindBody(vehicle) is not {} body||!LaneBody(body)||!_traffic.TryGetValue(vehicle,out var t)||IsSteering(t)||
            t.Steering is not {} s||s.Bearing!=Bearing(body.FacingX,body.FacingZ)||
            s.LaneX!=-body.FacingZ*Control.LaneOffset||s.LaneZ!=body.FacingX*Control.LaneOffset)return 0;
        return Bearing(body.FacingX,body.FacingZ)/4000+1;
    }
    private bool ShareRoadCell(long vehicle,long other,Cell cell)=>OpposingLaneBodies(vehicle,other,cell)&&
        LaneReservation(vehicle)!=0&&LaneReservation(other)!=0;
    private static bool OppositeReservations(int a,int b)=>a!=0&&b!=0&&(a-b+4)%4==2;
    private void WriteSteering(RoadBody body,TrafficVehicle updated)
    {
        int before=LaneReservation(body.Id);_traffic[body.Id]=updated;int after=LaneReservation(body.Id);
        if(before!=after)
        {var at=VehicleCargo(body).Position;Emit("TrafficLaneState",body.Owner.Value,body.Id,quantity:after,reason:at.X+","+at.Z);}
    }
    private bool CanSteer(RoadBody body,IEnumerable<Cell> footprint,VehicleSteering maneuver,bool center=false)
    {
        var cells=footprint.ToHashSet();var at=VehicleCargo(body).Position;
        if(cells.Any(c=>!ControlCellAllowed(body,c)||WorkBlocks(body.Id,c)||TurnReservedByOther(body.Id,c))||
            _traffic.Values.Any(t=>t.VehicleId!=body.Id&&HasRoadProgress(t)&&t.Index<t.Path.Length-1&&cells.Contains(t.Path[t.Index+1])))return false;
        var bounds=SteeringBounds(body,maneuver,center).ToArray();
        bool Overlap(int x,int z,int ex,int ez)=>bounds.Any(b=>
        {
            int dx=Math.Abs(x-b.X),dz=Math.Abs(z-b.Z);
            if(dx>=b.Ex+ex||dz>=b.Ez+ez)return false;
            int nearX=Math.Max(0,dx-ex),nearZ=Math.Max(0,dz-ez);
            return (long)nearX*nearX+(long)nearZ*nearZ<(long)b.Radius*b.Radius;
        });
        foreach(var other in RoadBodies().Where(b=>b.Id!=body.Id))
        {
            var t=VehicleTravelOf(other.Id);var cell=VehicleCargo(other).Position;
            bool wide=WideBody(other);
            int fx=Math.Abs(t.FacingX),fz=Math.Abs(t.FacingZ);if(fx<=1&&fz<=1){fx*=1000;fz*=1000;}
            int length=wide?430:400,width=wide?380:190,norm=Math.Max(1,IntegerDistance(fx,fz));
            if(Overlap(cell.X*1000+t.OffsetX,cell.Z*1000+t.OffsetZ,(fx*length+fz*width+norm-1)/norm+1,(fz*length+fx*width+norm-1)/norm+1))return false;
        }
        foreach(var stock in _dealerStock.Where(v=>v.Position!=at))
            if(Overlap(stock.Position.X*1000+500,stock.Position.Z*1000+500,stock.ModelId=="trakt_d1"||Model(stock.ModelId).EquipmentTypeId is not null?380:190,stock.ModelId=="trakt_d1"||Model(stock.ModelId).EquipmentTypeId is not null?430:400))return false;
        foreach(var actor in _actors.Values.Where(a=>a.Id!=body.Driver&&DrivenVehicle(a.Id) is null))
        {
            if(Overlap(actor.Position.X*1000+actor.OffsetX,actor.Position.Z*1000+actor.OffsetZ,150,150))return false;
            if(actor.Job is {Kind:"Move",SegmentTicks:>0} job&&job.PathIndex<job.Path.Length-1&&cells.Contains(job.Path[job.PathIndex+1]))return false;
        }
        return true;
    }
    private bool PedestrianTrafficBlocked(Cell cell,long actor=0)=>EquipmentStockAt(cell)||UsesFreeVehicleControl&&
        (RoadBodies().Any(b=>VehicleCargo(b).Position==cell&&
            !(b.Owner.Value==actor&&b.Driver==0&&!RoadVehicleWorking(b.Id)&&(!_traffic.TryGetValue(b.Id,out var parked)||!HasRoadProgress(parked)&&!IsSteering(parked))))||_traffic.Values.Any(t=>
            HasRoadProgress(t)&&t.Index<t.Path.Length-1&&t.Path[t.Index+1]==cell)||TurnReservedByOther(0,cell));

    private static int IntegerDistance(int dx,int dz)
    {
        long square=checked((long)dx*dx+(long)dz*dz),lo=0,hi=Math.Abs((long)dx)+Math.Abs((long)dz)+1;
        while(lo+1<hi){long mid=(lo+hi)/2;if(mid*mid<=square)lo=mid;else hi=mid;}
        return (int)lo;
    }
    private VehicleSteering ChargeSteering(RoadBody body,VehicleSteering before,VehicleSteering after)
    {
        int distance=IntegerDistance(after.LaneX-before.LaneX,after.LaneZ-before.LaneZ);
        int numerator=checked(distance*800+before.DistanceRemainder);
        int units=numerator/TravelUnitsPerCell;
        if(units>0)AccrueRoadTravel(body,VehicleCargo(body).Position,units);
        return after with{DistanceRemainder=numerator%TravelUnitsPerCell};
    }
    private void SetBodyFacing(RoadBody body,int x,int z)
    {
        if(_vehicles.TryGetValue(body.Id,out var v))_vehicles[body.Id]=v with{FacingX=x,FacingZ=z};
        else _traffic[body.Id]=Traffic(body.Id) with{FacingX=x,FacingZ=z};
        if(body.Driver!=0){_actors[body.Driver].FacingX=x;_actors[body.Driver].FacingZ=z;}
    }
    // Returns true when this quantum is spent steering or waiting for its swept space.
    private bool AdvanceSteering(RoadBody body,int x,int z)
    {
        var t=Traffic(body.Id);var steering=Steering(body,t);
        if(!IsSteering(t))
        {
            if(x==body.FacingX&&z==body.FacingZ)return false;
            int delta=NormalizeBearing(Bearing(x,z)-steering.Bearing);if(delta>=8000)delta-=16000;
            int speed=RoadBodySpeed(VehicleCargo(body).Position,VehicleCargo(body).Position,body);
            int quanta=VehicleControlRules.TurnQuanta(speed,RoadLevel(VehicleCargo(body).Position)>0,Math.Abs(delta)==8000);
            steering=steering with{TurnFrom=steering.Bearing,TurnDelta=delta,TurnQuantum=0,TurnQuanta=quanta,StartLaneX=steering.LaneX,StartLaneZ=steering.LaneZ};
            bool found=false;
            // Wide machinery can make a small, checked arc away from an adjacent wall.
            foreach(var clearance in new[]{new Cell(0,0),new(0,160),new(160,0),new(0,-160),new(-160,0)})
            {
                var candidate=steering with{ClearanceX=clearance.X,ClearanceZ=clearance.Z};
                int extra=(int)((4L*IntegerDistance(clearance.X,clearance.Z)*800*10+RoadMotionRate(speed)*1000-1)/(RoadMotionRate(speed)*1000));
                candidate=candidate with{TurnQuanta=steering.TurnQuanta+extra};
                candidate=candidate with{ReservedCells=SteeringFootprint(body,candidate)};
                if(!CanSteer(body,candidate.ReservedCells,candidate))continue;
                steering=candidate;found=true;break;
            }
            if(!found){_traffic[body.Id]=t with{Reason="TurnSpaceOccupied"};return true;}
        }
        // The footprint is reserved for the whole manoeuvre. Newly entering pedestrians
        // are checked as well; a blocked manoeuvre resumes from exactly this state.
        if(!CanSteer(body,steering.ReservedCells,steering)){_traffic[body.Id]=t with{Steering=steering,Reason="TurnSpaceOccupied"};return true;}
        int q=steering.TurnQuantum+1,total=steering.TurnQuanta;
        int bearing=NormalizeBearing(steering.TurnFrom+steering.TurnDelta*q/total);
        var from=BearingVector(steering.TurnFrom);var facing=BearingVector(bearing);
        int radius=SteeringRadius(body,VehicleCargo(body).Position);
        int sx=-from.Z*radius/1000,sz=from.X*radius/1000;
        var changed=steering with{Bearing=bearing,TurnQuantum=q,
            LaneX=-facing.Z*radius/1000+(steering.StartLaneX-sx)*(total-q)/total+steering.ClearanceX*4*q*(total-q)/(total*total),
            LaneZ=facing.X*radius/1000+(steering.StartLaneZ-sz)*(total-q)/total+steering.ClearanceZ*4*q*(total-q)/(total*total)};
        changed=ChargeSteering(body,steering,changed);
        bool finished=q==total;
        if(finished)
        {
            var final=BearingVector(bearing);SetBodyFacing(body,Math.Sign(final.X),Math.Sign(final.Z));
            changed=changed with{TurnFrom=0,TurnDelta=0,TurnQuantum=0,TurnQuanta=0,StartLaneX=0,StartLaneZ=0,ReservedCells=default,ClearanceX=0,ClearanceZ=0};
        }
        WriteSteering(body,Traffic(body.Id) with{Steering=changed,Reason=finished?"TurnCompleted":"VehicleTurning"});
        return true;
    }
    private long AlignLane(RoadBody body,long timeLeft)
    {
        var t=Traffic(body.Id);var steering=Steering(body,t);var at=VehicleCargo(body).Position;
        int radius=SteeringRadius(body,at);
        int tx=-body.FacingZ*radius,tz=body.FacingX*radius;
        int Approach(int value,int target)=>value+Math.Clamp(target-value,-40,40);
        var changed=steering with{LaneX=Approach(steering.LaneX,tx),LaneZ=Approach(steering.LaneZ,tz),Bearing=Bearing(body.FacingX,body.FacingZ)};
        int distance=IntegerDistance(changed.LaneX-steering.LaneX,changed.LaneZ-steering.LaneZ);
        if(distance==0){if(t.Steering is null)WriteSteering(body,t with{Steering=steering});return timeLeft;}
        var sweep=steering with{TurnFrom=steering.Bearing,TurnDelta=0,StartLaneX=steering.LaneX,StartLaneZ=steering.LaneZ};
        if(!CanSteer(body,SteeringFootprint(body,sweep),sweep))return timeLeft;
        long rate=RoadMotionRate(RoadBodySpeed(at,at,body));
        long needed=(distance*800L*MotionTimeScale+rate*1000-1)/(rate*1000);
        if(needed>timeLeft)return timeLeft;
        changed=ChargeSteering(body,steering,changed);WriteSteering(body,t with{Steering=changed});
        return timeLeft-needed;
    }
    private Cell ManualDirection(ActorState actor,RoadBody body)
    {
        int x=actor.HeadingX,z=actor.HeadingZ;
        if(x!=0&&z!=0){if(body.FacingX!=0)z=0;else x=0;}
        return new(x,z);
    }
    private bool FreeManualTarget(ActorState actor,RoadBody body,TrafficVehicle t,out Cell next)
    {
        var direction=ManualDirection(actor,body);var at=VehicleCargo(body).Position;
        next=new(at.X+direction.X,at.Z+direction.Z);
        if(direction==new Cell(0,0)||t.Assistance||t.Resume is not null||actor.Job is not null)return false;
        bool leaving=RoadLevel(at)>0&&RoadLevel(next)==0&&!(direction.X==-body.FacingX&&direction.Z==-body.FacingZ);
        if(leaving&&t.ExitQuanta<Control.ExitHoldQuanta)
        {next=new(at.X+body.FacingX,at.Z+body.FacingZ);return RoadLevel(next)>0&&ControlCellAllowed(body,next);}
        return ControlCellAllowed(body,next);
    }
    private void FreeManualRoadTravel(ActorState actor,OwnedVehicle vehicle)
    {
        var body=Body(vehicle);var t=Traffic(body.Id);
        if(RoadVehicleWorking(body.Id)||VehicleTransferring(body.CargoInventory.Value)||vehicle.Installation is not null)return;
        long stamp=_stepping?Tick:Tick+1;
        if(t.AdvancedTick==stamp&&t.AdvancedQuanta>=TravelQuantaPerTick)return;
        t=t with{AdvancedTick=stamp,AdvancedQuanta=t.AdvancedTick==stamp?t.AdvancedQuanta+1:1};
        var direction=ManualDirection(actor,body);var at=VehicleCargo(body).Position;
        var intended=new Cell(at.X+direction.X,at.Z+direction.Z);
        bool exit=direction!=new Cell(0,0)&&RoadLevel(at)>0&&RoadLevel(intended)==0&&!(direction.X==-body.FacingX&&direction.Z==-body.FacingZ);
        t=exit?t with{ExitX=direction.X,ExitZ=direction.Z,ExitQuanta=t.ExitX==direction.X&&t.ExitZ==direction.Z?Math.Min(Control.ExitHoldQuanta,t.ExitQuanta+1):1}:
            t with{ExitX=0,ExitZ=0,ExitQuanta=0};
        _traffic[body.Id]=t;
        if(IsSteering(t)){AdvanceSteering(body,direction.X,direction.Z);return;}
        if(HasRoadProgress(t)||t.Resume is not null)
        {TrafficTravel(body,t.Resume??t.Path[^1],1,true,true);return;}
        if(direction==new Cell(0,0)){_traffic[body.Id]=t with{Path=[],Index=0,Reason="Parked"};AlignLane(body,MotionTimeScale/TravelQuantaPerTick);return;}
        if(!FreeManualTarget(actor,body,t,out var next)){_traffic[body.Id]=t with{Reason="ObstacleBraking"};return;}
        if(exit&&t.ExitQuanta<Control.ExitHoldQuanta)_traffic[body.Id]=t with{Reason="RoadExitPending"};
        // A manual neighbour is validated directly. It never starts a distant route.
        _traffic[body.Id]=Traffic(body.Id) with{Path=[at,next],Index=0,Resume=null,YieldTo=0};
        TrafficTravel(body,next,1,true,true);
        if(exit&&t.ExitQuanta<Control.ExitHoldQuanta)_traffic[body.Id]=Traffic(body.Id) with{Reason="RoadExitPending"};
    }
}
