using System.Collections.Immutable;
namespace Simulation.Core;

/// <summary>Saved, authoritative nominal speeds and distance conversion per economic second.</summary>
public sealed record MovementNominalSpeeds(int RunningRoad,int RunningOffroad,int VehicleOffroad);
public sealed record MovementRules(int Version=1,int SpeedDivisor=32,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] int SpeedMultiplier=0,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] MovementNominalSpeeds? NominalSpeeds=null)
{
    public static MovementRules H2 {get;}=new();
    public static MovementRules I1 {get;}=new(2,40,3,new(10,8,10));
    public void Validate()
    {
        if(this!=H2&&this!=I1)throw new ArgumentException("Unsupported movement conversion");
    }
    // 596700 = 100 * lcm(3, 9, 27, 39, 51). Every I1 pedestrian/model/road
    // edge and every quantum is integral; no repeated ceil loss at cell centres.
    [System.Text.Json.Serialization.JsonIgnore] public int TimeScale=>Version==2?596_700:MovementTiming.TimeUnitsPerTick;
    [System.Text.Json.Serialization.JsonIgnore] public int Multiplier=>SpeedMultiplier==0?1:SpeedMultiplier;
    public int RunningSpeed(bool road)=>road?NominalSpeeds?.RunningRoad??20:NominalSpeeds?.RunningOffroad??16;
    public int OffroadVehicleSpeed()=>NominalSpeeds?.VehicleOffroad??8;
    public long EdgeTime(int speed)=>checked(((long)SpeedDivisor*TimeScale+speed*Multiplier-1)/(speed*Multiplier));
    public long WholeTicks(long time)=>checked((time+TimeScale-1)/TimeScale);
    public long UnmodelledEdgeTime(int legacyTicks)=>checked((long)legacyTicks*TimeScale*2/5);
}
public static class MovementTiming
{
    public const int TimeUnitsPerTick=1_000_000;
    public const int LegacySpeedDivisor=80;
    public static long EdgeTimeUnits(int speed,int divisor)=>checked(((long)divisor*TimeUnitsPerTick+speed-1)/speed);
    public static long WholeTicks(long time)=>checked((time+TimeUnitsPerTick-1)/TimeUnitsPerTick);
    public static Cell[] Path(ControllerMap map,Cell source,Cell destination)
        =>new GridMap(map.Width,map.Height,map.BlockedCells.ToArray()).FindPath(source,destination);
    public static long? TryPersonalTicks(ControllerObservation o,Cell source,Cell target)
    {try{return PersonalTicks(o,source,target);}catch(RuleViolation){return null;}}
    public static long? TryFreightTicks(ControllerObservation o,Cell source,Cell target,VehicleModel? model=null,long vehicle=0)
    {try{return FreightTicks(o,source,target,model,vehicle);}catch(RuleViolation){return null;}}
    public static long PersonalTicks(ControllerObservation o,Cell source,Cell destination)
    {
        if(o.Map.MovementSpeedDivisor==0)return checked((Path(o.Map,source,destination).Length-1L)*o.Map.MoveTicksPerCell);
        long units=0;
        var rules=o.Map.Movement??MovementRules.H2;
        var driven=o.Fleet?.Vehicles.FirstOrDefault(v=>v.Vehicle.Driver==o.Participant.Id.Value);
        var model=driven?.Model;
        var path=model is null?Path(o.Map,source,destination):VehiclePath(o,source,destination,model,driven!.Vehicle.Id);
        var levels=o.Roads?.Cells.ToDictionary(c=>c.Cell,c=>c.Level);
        for(int n=0;n<path.Length-1;n++)
        {
            if(o.Fleet is null&&rules.Version!=2){units=checked(units+rules.UnmodelledEdgeTime(o.Map.MoveTicksPerCell));continue;}
            int a=levels?.GetValueOrDefault(path[n])??0,b=levels?.GetValueOrDefault(path[n+1])??0;
            int speed=model is null?rules.RunningSpeed(a>0&&b>0):Math.Min(model.Speed,a>0&&b>0?o.Roads!.Rules.Stages.Single(s=>s.Level==Math.Min(a,b)).Speed:rules.OffroadVehicleSpeed());
            units=checked(units+rules.EdgeTime(speed));
        }
        return rules.WholeTicks(units);
    }
    public static Cell[] VehiclePath(ControllerObservation o,Cell source,Cell target,VehicleModel model,long vehicle=0,Cell? facing=null)
    {
        if(o.Roads is null)return Path(o.Map,source,target);
        var rules=o.Map.Movement??MovementRules.H2;
        var grid=new GridMap(o.Map.Width,o.Map.Height,o.Map.BlockedCells);var levels=o.Roads.Cells.ToDictionary(c=>c.Cell,c=>c.Level);
        var corridor=o.Map.MovementCorridor.IsDefault?new HashSet<Cell>():o.Map.MovementCorridor.ToHashSet();
        var publicCells=o.Map.MovementPublicRoad.IsDefault?corridor:o.Map.MovementPublicRoad.ToHashSet();
        var bypass=o.Roads.Bypasses;bool publicCell(Cell c)=>publicCells.Contains(c);
        bool corridorOnly=corridor.Contains(source)&&corridor.Contains(target);
        bool Allow(Cell c)=>grid.Walkable(c)&&!bypass.Any(b=>b.VehicleId!=vehicle&&b.WorkCell==c)&&(!corridorOnly||publicCell(c))&&
            (publicCell(c)||c==source||c==target||!bypass.Any(b=>Math.Abs(c.X-b.WorkCell.X)+Math.Abs(c.Z-b.WorkCell.Z)<=2))&&
            !bypass.Any(b=>b.Closing&&!b.Users.Contains(vehicle)&&b.Path.Skip(1).SkipLast(1).Contains(c)&&c!=source);
        int Speed(Cell a,Cell b)=>Math.Min(model.Speed,levels.GetValueOrDefault(a)>0&&levels.GetValueOrDefault(b)>0?o.Roads.Rules.Stages.Single(s=>s.Level==Math.Min(levels[a],levels[b])).Speed:rules.OffroadVehicleSpeed());
        var body=o.Fleet?.Vehicles.FirstOrDefault(v=>v.Vehicle.Id==vehicle)?.Vehicle;
        var heading=facing??(body is null?new Cell(0,0):new Cell(body.FacingX,body.FacingZ));
        var restricted=o.Map.MovementRestrictedTurns.IsDefault?new HashSet<Cell>():o.Map.MovementRestrictedTurns.ToHashSet();
        var path=RouteSearch.FindOriented(source,target,Allow,(a,b)=>rules.EdgeTime(Speed(a,b)),c=>!restricted.Contains(c),heading);
        if(path.Length==0)throw new RuleViolation("NoSafeVehiclePath");return path;
    }
    public static long FreightTicks(ControllerObservation o,Cell source,Cell target,VehicleModel? model=null,long vehicle=0)
    {
        var rules=o.Map.Movement??MovementRules.H2;
        var path=model is null?Path(o.Map,source,target):VehiclePath(o,source,target,model,vehicle);
        var levels=o.Roads?.Cells.ToDictionary(c=>c.Cell,c=>c.Level);long total=0;
        for(int n=0;n<path.Length-1;n++)
        {
            int a=levels?.GetValueOrDefault(path[n])??0,b=levels?.GetValueOrDefault(path[n+1])??0;
            long edge;
            if(model is null&&rules.Version!=2)edge=rules.UnmodelledEdgeTime(o.LogisticsRules.MoveTicksPerCell);
            else
            {
                int maximum=model?.Speed??Math.Max(1,80/o.LogisticsRules.MoveTicksPerCell);
                int limit=a>0&&b>0?o.Roads!.Rules.Stages.Single(s=>s.Level==Math.Min(a,b)).Speed:rules.OffroadVehicleSpeed();
                edge=rules.EdgeTime(Math.Min(maximum,limit));
            }
            total=checked(total+edge);
        }
        return rules.WholeTicks(total);
    }

}
public sealed partial class SimulationEngine
{
    public bool UsesAcceleratedMovement=>_config.Economy?.Movement is not null;
    private MovementRules Movement=>_config.Economy?.Movement??MovementRules.H2;
    private int MotionTimeScale=>Movement.TimeScale;
    private long RoadMotionRate(int speed)=>checked(speed*(Movement.Version==2?60L:25L));
    private long PersonalEdgeTimeUnits(ActorState actor,Cell from,Cell to)
        =>Fleet is null&&Movement.Version!=2?Movement.UnmodelledEdgeTime(_config.MoveTicksPerCell):Movement.EdgeTime(FleetSpeed(from,to,DrivenVehicle(actor.Id) is {} v?Model(v.ModelId):null));
    private long PersonalPathTicks(ActorState actor,Cell[] path)
        =>UsesAcceleratedMovement?Movement.WholeTicks(path.Zip(path.Skip(1)).Sum(p=>PersonalEdgeTimeUnits(actor,p.First,p.Second))):path.Zip(path.Skip(1)).Sum(p=>(long)PersonalMoveTicks(actor,p.First,p.Second));
    private long FreightEdgeUnits(LogisticsState l,Cell a,Cell b)
    {
        if(l.Vehicle is null&&Movement.Version!=2)return Movement.UnmodelledEdgeTime(Logistics.MoveTicksPerCell);
        int maximum=l.Vehicle?.Speed??Math.Max(1,80/Logistics.MoveTicksPerCell);
        return Movement.EdgeTime(VehicleSurfaceSpeed(a,b,maximum));
    }
    private long EquipmentEdgeTime(Cell a,Cell b)=>Movement.Version==2?Movement.EdgeTime(VehicleSurfaceSpeed(a,b,Math.Max(1,80/_config.MoveTicksPerCell))):Movement.UnmodelledEdgeTime(_config.MoveTicksPerCell);
    private (int Index,int Segment) AdvanceTimedPath(IReadOnlyList<Cell> path,int index,int segment,Func<Cell,Cell,long> duration)
    {
        long budget=MotionTimeScale;
        while(index<path.Count-1&&budget>0)
        {
            if(!_map.Walkable(path[index+1]))break;
            long required=duration(path[index],path[index+1]),spent=Math.Min(budget,required-segment);
            budget-=spent;segment=checked(segment+(int)spent);if(segment<required)break;
            segment=0;++index;
        }
        return(index,segment);
    }
    private void AdvanceFastPersonalMove(ActorState actor,PersonalJob job)
    {
        long budget=MotionTimeScale;
        while(job.PathIndex<job.Path.Length-1&&budget>0)
        {
            var at=job.Path[job.PathIndex];var next=job.Path[job.PathIndex+1];
            if(!_map.Walkable(next)){job.CompletionTick=checked(Tick+1);return;}
            long required=job.TimedEdges is {} timing?(timing[job.PathIndex]==0?timing[job.PathIndex]=PersonalEdgeTimeUnits(actor,at,next):timing[job.PathIndex]):PersonalEdgeTimeUnits(actor,at,next),spent=Math.Min(budget,required-job.SegmentTicks);budget-=spent;job.SegmentTicks=checked(job.SegmentTicks+(int)spent);
            if(job.SegmentTicks<required)break;
            job.SegmentTicks=0;++job.PathIndex;actor.Position=next;actor.OffsetX=actor.OffsetZ=TravelCenter;_economy.Inventories[actor.Inventory].Position=next;SyncDrivenVehicle(actor);
        }
        if(job.PathIndex==job.Path.Length-1){job.CompletionTick=Tick;CompleteMovement(actor.Id,job.Id);}
    }
}
