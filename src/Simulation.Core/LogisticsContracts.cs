using System.Collections.Immutable;

namespace Simulation.Core;

public readonly record struct LogisticsId(long Value);
public record LogisticsSettings(long PurchasePriceMinor = 20000, long CargoCapacity = 10,
    int MoveTicksPerCell = 1, int MinimumCrew = 2, int CrewCapacity = 2, long WageMinor = 25);
public record LogisticsTripView(long Id, long ContractId, string ItemId, long Quantity, string State,
    long PreparedTick, long LoadTick, long ArrivalTick, long EndTick, Cell Position,
    ImmutableArray<Cell> Path, int PathIndex, int SegmentTicks, long CommittedWageMinor,
    long WageBudgetMinor, long WagesPaidMinor, string StockHold, string ReturnHold, string WageHold)
{
    public string CargoHold => $"logistics-cargo:{Id}";
}
public record LogisticsView(LogisticsId Id, ParticipantId OwnerId, EnterpriseId EnterpriseId,
    InventoryId SourceInventory, InventoryId CargoInventory, EmploymentGroupId GroupId,
    bool Enabled, long ContractId, string ReasonCode, long CompletedTrips, LogisticsTripView? Trip);
internal record SavedLogistics(LogisticsView View, EmploymentView Group);
internal sealed class LogisticsState(long id, long owner, long enterprise, long source, long cargo, LogisticsSettings settings)
{
    public long Id { get; } = id;
    public long Owner { get; } = owner;
    public long Enterprise { get; } = enterprise;
    public long Source { get; } = source;
    public long Cargo { get; } = cargo;
    public EmploymentGroup Group { get; } = new(id, owner, 0, settings.CrewCapacity, settings.WageMinor, EmploymentRole.Logistics);
    public bool Enabled { get; set; }
    public long Contract { get; set; }
    public string Reason { get; set; } = "NoDestination";
    public long Completed { get; set; }
    public LogisticsTripView? Trip { get; set; }
    public LogisticsView View() => new(new(Id), new(Owner), new(Enterprise), new(Source), new(Cargo), new(Id),
        Enabled, Contract, Reason, Completed, Trip);
}
