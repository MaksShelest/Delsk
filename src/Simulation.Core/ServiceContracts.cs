using System.Collections.Immutable;
namespace Simulation.Core;

public sealed record ServiceRules(int ResponseTicks=10,long MinimumFeeMinor=500,long FixedFeeMinor=200,
    long BlockFeeMinor=10,long VolumeBlock=10,long DistanceBlock=10,int CostMarkupBasisPoints=12000,
    long DriverWageMinor=20,int MinimumBufferTicks=600,int BufferBasisPoints=5000)
{
    public void Validate()
    {
        if(ResponseTicks is <1 or >1000||MinimumFeeMinor<1||FixedFeeMinor<0||BlockFeeMinor<0||VolumeBlock<1||DistanceBlock<1||
            CostMarkupBasisPoints is <10000 or >100000||DriverWageMinor<1||MinimumBufferTicks<1||BufferBasisPoints is <1 or >100000)
            throw new ArgumentException("Invalid delivery service rules");
    }
}
public sealed record ServiceTerms(long VehicleId,InventoryId Source,InventoryId Destination,string ItemId,long Quantity,long PurchaseId=0,long ContractId=0);
public sealed record ServiceQuote(ServiceTerms Terms,long Volume,long QuantityPerTrip,long Trips,long LoadedDistance,
    long TravelTicks,long HandlingTicks,long ExtraStaffTicks,long EtaTicks,long BufferTicks,long FeeMinor,long RoadUnits,
    long ExpectedIncomingVolume,long DestinationCapacity,long DestinationOccupiedVolume,
    Cell VehiclePosition,Cell SourcePosition,Cell DestinationPosition,ImmutableArray<Cell> Approach,
    ImmutableArray<Cell> Loaded,ImmutableArray<Cell> EmptyReturn,string Token)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public InventoryId? ReturnInventory {get;init;}
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public Cell? ReturnPosition {get;init;}
    public long CargoRightsDeadline {get;init;}
    public long EstimatedDeadline(long acceptTick)=>checked(acceptTick+EtaTicks+BufferTicks);
}
public sealed record ServiceQuoteResult(ServiceQuote? Quote,string Reason);
public sealed record ServiceListing(long VehicleId,ParticipantId Carrier,string ModelId,long CapacityVolume,Cell Position,bool Available);
public sealed record ServicePoint(InventoryId Inventory,ParticipantId Owner,Cell Position,BuildingKind Kind,bool CanSource,bool CanReceive)
{[System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? PointType {get;init;}};
public sealed record ServiceOrder(long Id,ParticipantId Customer,ParticipantId Carrier,ServiceQuote Quote,
    long CreatedTick,long RespondByTick,string State="Pending",long AcceptedTick=0,long DeadlineTick=0,long EscrowAccount=0,
    long ClosedTick=0,string Reason="WaitingCarrier",ServiceQuote? ProposedQuote=null,bool CustomerViewed=false,bool CarrierViewed=false)
{
    public bool Active=>State is "Pending" or "Requote" or "Accepted";
    public string MoneyHold=>"service-money:"+Id;
    public string StockHold=>"service-stock:"+Id;
}
public sealed record ServicesState(ImmutableArray<long> ListedVehicles,ImmutableArray<ServiceOrder> Orders)
{[System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public ServiceExecutionState? Execution {get;init;}}
public sealed record ServiceObservation(ImmutableArray<ServiceListing> Catalog,ImmutableArray<long> OwnListings,
    ImmutableArray<ServiceOrder> OwnOrders,ImmutableArray<ServicePoint> OwnPoints,ServiceRules Rules)
{[System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public ServiceExecutionObservation? Execution {get;init;}}
