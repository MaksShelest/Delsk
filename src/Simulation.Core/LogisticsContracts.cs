using System.Collections.Immutable;

namespace Simulation.Core;

public readonly record struct LogisticsId(long Value);
public record LogisticsUpgradeDefinition(long PriceMinor, ImmutableArray<MaterialCost> Materials,
    long CargoCapacity, int MinimumCrew, int CrewCapacity);
public record LogisticsSettings(long PurchasePriceMinor = 20000, long CargoCapacity = 10,
    int MoveTicksPerCell = 1, int MinimumCrew = 2, int CrewCapacity = 2, long WageMinor = 25,
    LogisticsUpgradeDefinition? Upgrade = null);
public enum LogisticsDirection { Output, Inbound }
public record LogisticsJobView(string Key, long ContractId, InventoryId OwnDestination, InventoryId InboundSource,
    bool Enabled, long LastServed);
public record LogisticsTripView(long Id, long ContractId, string ItemId, long Quantity, string State,
    long PreparedTick, long LoadTick, long ArrivalTick, long EndTick, Cell Position,
    ImmutableArray<Cell> Path, int PathIndex, int SegmentTicks, long CommittedWageMinor,
    long WageBudgetMinor, long WagesPaidMinor, string StockHold, string ReturnHold, string WageHold,
    InventoryId OwnDestination = default, InventoryId InboundSource = default, string JobKey = "")
{
    public string CargoHold => $"logistics-cargo:{Id}";
    public string DestinationHold => $"logistics-destination:{Id}";
}
public record LogisticsView(LogisticsId Id, ParticipantId OwnerId, EnterpriseId EnterpriseId,
    InventoryId SourceInventory, InventoryId CargoInventory, EmploymentGroupId GroupId,
    bool Enabled, long ContractId, string ReasonCode, long CompletedTrips, LogisticsTripView? Trip, int Level,
    InventoryId OwnDestination, InventoryId InboundSource, bool InboundEnabled,
    ImmutableArray<LogisticsJobView> Queue, long DispatchCount);
internal record SavedLogistics(LogisticsView View, EmploymentView Group);
internal sealed class LogisticsState(long id, long owner, long enterprise, long source, long cargo,
    LogisticsSettings settings, int level = 1)
{
    public long Id { get; } = id;
    public long Owner { get; } = owner;
    public long Enterprise { get; } = enterprise;
    public long Source { get; } = source;
    public long Cargo { get; } = cargo;
    public int Level { get; set; } = level;
    public long CargoCapacity => Level switch { 1 => settings.CargoCapacity,
        2 when settings.Upgrade is { } upgrade => upgrade.CargoCapacity,
        _ => throw new InvalidOperationException("Invalid logistics level") };
    public int MinimumCrew => Level switch { 1 => settings.MinimumCrew,
        2 when settings.Upgrade is { } upgrade => upgrade.MinimumCrew,
        _ => throw new InvalidOperationException("Invalid logistics level") };
    public int CrewCapacity => Level switch { 1 => settings.CrewCapacity,
        2 when settings.Upgrade is { } upgrade => upgrade.CrewCapacity,
        _ => throw new InvalidOperationException("Invalid logistics level") };
    public EmploymentGroup Group { get; } = new(id, owner, 0,
        level == 2 && settings.Upgrade is { } upgrade ? upgrade.CrewCapacity : settings.CrewCapacity,
        settings.WageMinor, EmploymentRole.Logistics);
    public bool Enabled { get; set; }
    public long Contract { get; set; }
    public long Destination { get; set; }
    public long InboundSource { get; set; }
    public bool InboundEnabled { get; set; }
    public List<LogisticsJobView> Queue { get; } = [];
    public long DispatchCount { get; set; }
    public string Reason { get; set; } = "NoDestination";
    public long Completed { get; set; }
    public LogisticsTripView? Trip { get; set; }
    public LogisticsView View() => new(new(Id), new(Owner), new(Enterprise), new(Source), new(Cargo), new(Id),
        Enabled, Contract, Reason, Completed, Trip, Level, new(Destination), new(InboundSource), InboundEnabled,
        [.. Queue.OrderBy(j => j.Key, StringComparer.Ordinal)], DispatchCount);
}
