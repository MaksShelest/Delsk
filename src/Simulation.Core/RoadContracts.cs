using System.Collections.Immutable;

namespace Simulation.Core;

public sealed record RoadStageRule(int Level, int Speed, long PriceMinor, int Ticks, ImmutableArray<MaterialCost> Materials);
public sealed record RoadRules(ImmutableArray<RoadStageRule> Stages, long WageMinor=20,
    int ExitHoldTicks=1, int WarningTicks=30, int ReleaseTicks=60);
public sealed record RoadContribution(Cell Cell, int Level, ParticipantId Builder, long Tick, long VehicleId, long PriceMinor);
public sealed record RoadCell(Cell Cell, int Level, ImmutableArray<RoadContribution> Contributions);
public sealed record RoadBatch(Cell Cell, int Level, long StartedTick, long Work, ImmutableArray<Cell> Bypass);
public sealed record RoadWork(long VehicleId, EquipmentMode Mode, bool Enabled, int Level, ImmutableArray<Cell> Route,
    int Cursor, RoadBatch? Batch, EmploymentView Group, string Reason);
public sealed record RoadBypass(long VehicleId, Cell WorkCell, ImmutableArray<Cell> Path, bool Closing, ImmutableArray<long> Users);
public sealed record RoadOrder(long Id, Cell Cell, int Level, long Reward, long CreatedTick,
    long Assignee=0, long AcceptedTick=0, long PaidTick=0);
public sealed record TrafficVehicle(long VehicleId, ImmutableArray<Cell> Path, int Index=0, int Progress=0,
    long WaitingSince=0, long StoppedSince=0, int ExitX=0, int ExitZ=0, int ExitQuanta=0,
    string Reason="Parked", Cell? Resume=null, long Priority=0, long YieldTo=0,
    long AdvancedTick=0, int AdvancedQuanta=0, bool Assistance=false, int FacingX=0, int FacingZ=-1);
public sealed record RoadsState(ulong RandomState, long NextOrderTick, ImmutableArray<RoadCell> Cells,
    ImmutableArray<RoadWork> Works, ImmutableArray<RoadBypass> Bypasses,
    ImmutableArray<RoadOrder> Orders, ImmutableArray<TrafficVehicle> Traffic);
public sealed record RoadObservation(ImmutableArray<RoadCell> Cells, ImmutableArray<RoadWork> OwnWorks,
    ImmutableArray<RoadBypass> Bypasses, ImmutableArray<RoadOrder> Orders, ImmutableArray<TrafficVehicle> OwnTraffic,
    RoadRules Rules);
internal sealed class RoadWorkState(long vehicle, long owner, long wage)
{
    public long Vehicle {get;}=vehicle;
    public EquipmentMode Mode {get;set;}=EquipmentMode.Off;
    public bool Enabled {get;set;}
    public int Level {get;set;}=1;
    public ImmutableArray<Cell> Route {get;set;}=[];
    public int Cursor {get;set;}
    public RoadBatch? Batch {get;set;}
    public string Reason {get;set;}="UserPaused";
    public EmploymentGroup Group {get;}=new(vehicle,owner,0,2,wage,EmploymentRole.RoadWorks);
    public RoadWork View()=>new(Vehicle,Mode,Enabled,Level,Route,Cursor,Batch,Group.View(),Reason);
}
