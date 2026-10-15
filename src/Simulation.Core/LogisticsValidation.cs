namespace Simulation.Core;

public static partial class ScenarioValidation
{
    private static void ValidateLogisticsUpgrade(ScenarioDefinition s)
    {
        if (s.Logistics?.Upgrade is not { } upgrade) return;
        var settings = s.Logistics!;
        if (upgrade.PriceMinor <= 0 || upgrade.CargoCapacity < settings.CargoCapacity ||
            upgrade.MinimumCrew < settings.MinimumCrew || upgrade.CrewCapacity < settings.CrewCapacity ||
            upgrade.CrewCapacity < upgrade.MinimumCrew || upgrade.CrewCapacity > 1000 ||
            upgrade.Materials.IsDefault || upgrade.Materials.Length > s.Items.Length ||
            upgrade.Materials.Any(m => m is null || m.Quantity <= 0 || !s.Items.Any(i => i.Id == m.ItemId)) ||
            upgrade.Materials.Select(m => m.ItemId).Distinct(StringComparer.Ordinal).Count() != upgrade.Materials.Length)
            throw new ArgumentException("Invalid logistics upgrade");
        try
        {
            _ = checked(upgrade.CrewCapacity * settings.WageMinor *
                Math.Max(2L, 2L * (s.MapWidth * s.MapHeight - 1) * settings.MoveTicksPerCell));
            foreach (var m in upgrade.Materials) _ = checked(m.Quantity * s.Items.Single(i => i.Id == m.ItemId).UnitVolume);
        }
        catch (OverflowException ex) { throw new ArgumentException("Logistics upgrade arithmetic overflow", ex); }
    }
}

public sealed partial class SimulationEngine
{
    private void ValidateLogisticsInvariants()
    {
        var facilities = new HashSet<long>(); var cargoIds = new HashSet<long>(); var tripIds = new HashSet<long>();
        foreach (var l in _logistics.Values)
        {
            if (l.Id < 1000 || !facilities.Add(l.Enterprise) || !cargoIds.Add(l.Cargo) ||
                !_enterprises.TryGetValue(l.Enterprise, out var enterprise) || l.Owner != enterprise.Definition.OwnerId ||
                l.Source != enterprise.Output || l.Group.Id != l.Id || l.Group.Owner != l.Owner ||
                l.Group.Role != EmploymentRole.Logistics || l.Group.Capacity != l.CrewCapacity ||
                !_economy.Inventories.TryGetValue(l.Cargo, out var cargo) || cargo.Owner != l.Owner ||
                cargo.Capacity != l.CargoCapacity || l.Completed < 0 || l.Contract < 0 || l.Destination < 0 ||
                (l.Contract != 0 && l.Destination != 0) ||
                (l.Destination != 0 && !IsOwnLogisticsStorage(l.Owner, l.Destination)) ||
                (l.Contract != 0 && !_deliveryContracts.Any(d => d.Id == l.Contract && d.Supplier.Value == l.Owner)))
                throw new InvalidOperationException("Invalid logistics system");
            var source = _economy.Inventories[l.Source];
            if (l.Trip is not { } t)
            {
                if (cargo.Stock.Count != 0 || cargo.Position != source.Position)
                    throw new InvalidOperationException("Idle transport retains cargo or foreign position");
                continue;
            }
            var d = _deliveryContracts.SingleOrDefault(d => d.Id == t.ContractId);
            bool own = t.OwnDestination.Value != 0;
            if (own ? t.ContractId != 0 || l.Contract != 0 || t.OwnDestination.Value != l.Destination ||
                    !IsOwnLogisticsStorage(l.Owner, t.OwnDestination.Value) || t.State == "ReturningCargo" :
                d is null || l.Destination != 0 || d.Supplier.Value != l.Owner || t.ContractId != l.Contract ||
                    t.ItemId != d.ItemId || t.Quantity > d.Quantity || t.ArrivalTick > d.DeadlineTick)
                throw new InvalidOperationException("Invalid logistics destination");
            Cell destination = own ? _economy.Inventories[t.OwnDestination.Value].Position : d!.Position;
            if (!tripIds.Add(t.Id) || t.Id < 1000 || t.Quantity <= 0 || !_items.ContainsKey(t.ItemId) ||
                t.Quantity > l.CargoCapacity / _items[t.ItemId].UnitVolume ||
                t.PreparedTick < 1 || t.PreparedTick > Tick || t.LoadTick != t.PreparedTick + 1 ||
                t.Path.IsDefaultOrEmpty || t.Path.Any(p => !_map.Walkable(p)) ||
                t.Path.Zip(t.Path.Skip(1)).Any(p => Math.Abs(p.First.X - p.Second.X) + Math.Abs(p.First.Z - p.Second.Z) != 1) ||
                t.CommittedWageMinor <= 0 || t.WageBudgetMinor != checked(l.Group.Capacity * t.CommittedWageMinor * (t.EndTick - t.LoadTick)) ||
                t.ArrivalTick != checked(t.LoadTick + Math.Max(1L, (t.Path.Length - 1L) * Logistics.MoveTicksPerCell)) ||
                t.EndTick != checked(t.ArrivalTick + t.ArrivalTick - t.LoadTick) ||
                t.WagesPaidMinor < 0 || t.WagesPaidMinor > t.WageBudgetMinor ||
                t.StockHold != $"logistics-stock:{t.Id}" || t.ReturnHold != $"logistics-return:{t.Id}" || t.WageHold != $"logistics-wage:{t.Id}" ||
                cargo.Position != t.Position || t.PathIndex < 0 || t.PathIndex >= t.Path.Length ||
                t.Path[t.PathIndex] != t.Position || t.SegmentTicks < 0 || t.SegmentTicks >= Logistics.MoveTicksPerCell ||
                t.State is not ("Loading" or "Outbound" or "ReturningEmpty" or "ReturningCargo"))
                throw new InvalidOperationException("Invalid logistics trip");
            bool outbound = t.State is "Loading" or "Outbound";
            if (t.Path[0] != (outbound ? source.Position : destination) || t.Path[^1] != (outbound ? destination : source.Position) ||
                (t.State == "Loading" ? Tick != t.PreparedTick || t.PathIndex != 0 || t.SegmentTicks != 0 || t.WagesPaidMinor != 0 :
                 outbound ? Tick < t.LoadTick || Tick >= t.ArrivalTick : Tick < t.ArrivalTick || Tick >= t.EndTick))
                throw new InvalidOperationException("Logistics path/time mismatch");
            long elapsed = t.State == "Loading" ? 0 : Tick - (outbound ? t.LoadTick : t.ArrivalTick);
            if (t.Path.Length > 1 && elapsed != checked((long)t.PathIndex * Logistics.MoveTicksPerCell + t.SegmentTicks))
                throw new InvalidOperationException("Logistics progress mismatch");
            long quantity = t.State is "Outbound" or "ReturningCargo" ? t.Quantity : 0;
            if (cargo.Stock.Count != (quantity > 0 ? 1 : 0) || cargo.Count(t.ItemId) != quantity || cargo.Reservations.Count != (quantity > 0 ? 1 : 0) ||
                (quantity > 0 && (!cargo.Reservations.TryGetValue(t.CargoHold, out var cargoHold) || cargoHold != new StockReservation(t.ItemId, quantity))) ||
                cargo.CapacityHolds.Count != 0 ||
                _economy.Accounts[_actors[l.Owner].Account].Holds.GetValueOrDefault(t.WageHold) != t.WageBudgetMinor - t.WagesPaidMinor ||
                _events.Where(e => e.Type == "LogisticsWagesPaid" && e.EntityId == t.Id).Sum(e => e.AmountMinor) != t.WagesPaidMinor)
                throw new InvalidOperationException("Logistics goods/payroll mismatch");
            if (t.State == "Loading" ? !source.Reservations.TryGetValue(t.StockHold, out var stock) || stock != new StockReservation(t.ItemId, t.Quantity) :
                source.Reservations.ContainsKey(t.StockHold)) throw new InvalidOperationException("Logistics source hold mismatch");
            long expectedReturn = quantity > 0 ? checked(t.Quantity * _items[t.ItemId].UnitVolume) : 0;
            if (source.CapacityHolds.GetValueOrDefault(t.ReturnHold) != expectedReturn)
                throw new InvalidOperationException("Logistics return capacity mismatch");
            if (own && _economy.Inventories[t.OwnDestination.Value].CapacityHolds.GetValueOrDefault(t.DestinationHold) !=
                (outbound ? checked(t.Quantity * _items[t.ItemId].UnitVolume) : 0))
                throw new InvalidOperationException("Logistics destination capacity mismatch");
        }
    }
    private void AddExpectedLogisticsHolds(Dictionary<(long Inventory, string Hold), long> capacity,
        Dictionary<(long Inventory, string Hold), StockReservation> stock, Dictionary<(long Account, string Hold), long> money)
    {
        foreach (var l in _logistics.Values.Where(l => l.Trip is not null))
        {
            var t = l.Trip!;
            if (t.OwnDestination.Value != 0 && t.State is "Loading" or "Outbound")
                capacity.Add((t.OwnDestination.Value, t.DestinationHold), checked(t.Quantity * _items[t.ItemId].UnitVolume));
            if (t.State == "Loading") stock.Add((l.Source, t.StockHold), new(t.ItemId, t.Quantity));
            if (t.State is "Outbound" or "ReturningCargo")
            {
                capacity.Add((l.Source, t.ReturnHold), checked(t.Quantity * _items[t.ItemId].UnitVolume));
                stock.Add((l.Cargo, t.CargoHold), new(t.ItemId, t.Quantity));
            }
            if (t.WageBudgetMinor > t.WagesPaidMinor) money.Add((_actors[l.Owner].Account, t.WageHold), t.WageBudgetMinor - t.WagesPaidMinor);
        }
    }
}
