using System.Collections.Immutable;
using System.Text.Json.Serialization;
namespace Simulation.Core;

public sealed record ServiceExecutionRules(int LateBasisPoints=2000,int ForceCloseAfterTicks=1200)
{
    public void Validate(){if(LateBasisPoints is <0 or >10000||ForceCloseAfterTicks<1)throw new ArgumentException("Invalid service execution rules");}
}
public sealed record ServiceReceipt(long Tick,long Quantity,long GrossMinor,long PaidMinor,long LateRefundMinor);
public sealed record ServiceRun(long Order,InventoryId ReturnInventory,string Stage="AwaitingPickup",long Loaded=0,long Delivered=0,
    long Returned=0,long Released=0,long TripQuantity=0,long TripNumber=0,long WorkRemainder=0,long StageStartedTick=0,
    long LastWorkedTick=0,string Cancellation="",long GrossSettledMinor=0,long PaidMinor=0,long RefundedMinor=0,
    long LateGrossMinor=0,long LateRefundMinor=0,ImmutableArray<ServiceReceipt> Receipts=default,string Reason="AwaitingDriver")
{
    public long Cargo=>checked(Loaded-Delivered-Returned);
    public string CargoHold=>"service-cargo:"+Order;
    public string ReturnHold=>"service-return:"+Order;
}
public sealed record ServiceCrew(long Vehicle,bool Enabled,EmploymentView Group);
public sealed record ServiceExecutionState(ImmutableArray<ServiceRun> Runs,ImmutableArray<ServiceCrew> Crews);
public sealed record ServiceRunView(ServiceRun Run,long Vehicle,Cell Position,Cell Target,long SourceRemaining,long FeeRemaining,
    bool Automatic,int Headcount,string Reason,bool AtTarget=false);
public sealed record ServiceExecutionObservation(ImmutableArray<ServiceRunView> Runs,ImmutableArray<ServiceCrew> Crews,ServiceExecutionRules Rules);
