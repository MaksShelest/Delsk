using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Simulation.Core;

public sealed record VehicleModel(string Id, string Name, int Speed, long CapacityVolume, long PriceMinor,
    long HandlingMax, bool MaterialsOnly = false);
public sealed record HandlingUpgrade(int Level, long Throughput, long PriceMinor, ImmutableArray<MaterialCost> Materials,
    long Ticks, int Staff = 1, long ServiceMinor = 0);
public sealed record FleetRules(ImmutableArray<VehicleModel> Models, ImmutableArray<HandlingUpgrade> Kits,
    ImmutableArray<HandlingUpgrade> Platforms, int SupplyTicks = 500, int MinimumStockTicks = 2500,
    int PurchaseWindowTicks = 5, long WageMinor = 20);
public sealed record DealerVehicle(long Id, string ModelId, Cell Position, long DeliveredTick);
public sealed record VehiclePurchase(long VehicleId, ParticipantId Owner, string ModelId, long Tick, long PriceMinor, string CommandId);
public sealed record VehiclePurchaseRequest(long VehicleId, ParticipantId Actor, long CutoffTick, string CommandId)
{
    public string MoneyHold => "vehicle-purchase:" + CommandId;
}
public sealed record HandlingInstallation(int Level, long StartedTick, long CompletionTick);
// Position belongs solely to CargoInventory; neither an actor nor logistics owns a copy of the vehicle.
public sealed record OwnedVehicle(long Id, ParticipantId Owner, string ModelId, InventoryId CargoInventory,
    long Driver = 0, int HandlingLevel = 0, HandlingInstallation? Installation = null,
    int OffsetX = SimulationEngine.TravelCenter, int OffsetZ = SimulationEngine.TravelCenter,
    int FacingX = 0, int FacingZ = -1);
public sealed record VehicleView(OwnedVehicle Vehicle, VehicleModel Model, Cell Position, string State, LogisticsId Logistics);
public sealed record LoadingPlatform(long Id, long Building, int Level, HandlingInstallation? Installation, long LastWorkedTick, EmploymentView Group);
public sealed record FleetState(ulong RandomState, long NextSupplyTick, ImmutableArray<DealerVehicle> Stock,
    ImmutableArray<OwnedVehicle> Vehicles, ImmutableArray<VehiclePurchaseRequest> Requests,
    ImmutableArray<VehiclePurchase> Purchases, ImmutableArray<LoadingPlatform> Platforms);
public sealed record FleetObservation(Cell Dealer, ImmutableArray<VehicleModel> Models, ImmutableArray<DealerVehicle> Stock,
    ImmutableArray<VehicleView> Vehicles, ImmutableArray<VehiclePurchaseRequest> OwnRequests,
    ImmutableArray<LoadingPlatform> Platforms, ImmutableArray<HandlingUpgrade> Kits, ImmutableArray<HandlingUpgrade> PlatformUpgrades);
internal sealed class PlatformState(long id, long building, long owner, long wage)
{
    public long Id { get; } = id;
    public long Building { get; } = building;
    public int Level { get; set; } = 1;
    public HandlingInstallation? Installation { get; set; }
    public long LastWorkedTick { get; set; }
    public EmploymentGroup Group { get; } = new(id, owner, 0, 1, wage, EmploymentRole.Handling);
    public LoadingPlatform View() => new(Id, Building, Level, Installation, LastWorkedTick, Group.View());
}
