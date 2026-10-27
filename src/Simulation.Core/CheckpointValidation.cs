namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private void ValidateCheckpointState()
    {
        if (Tick < 0 || Tick > _config.MaxTicks || _nextId < 1000 ||
            !_actors.Keys.Order().SequenceEqual(_config.Participants.Select(p => p.Id).Order()) ||
            !_flows.Keys.Order(StringComparer.Ordinal).SequenceEqual(_items.Keys.Order(StringComparer.Ordinal)) ||
            _events.Count == 0 || _eventSequence != _events.Count)
            throw new CheckpointException("InvalidWorldStructure");
        var commits = _events.Where(e => e.Type == "TickCommitted").ToArray();
        if (commits.LongLength != Tick || commits.Where((e, i) => e.Tick != i + 1L || e.Phase != 9).Any() ||
            !HasCommittedBoundary() ||
            _events.Where((e, i) => e.Sequence != i + 1L || e.Tick < 0 || e.Tick > Tick || e.Phase is < 0 or > 9 ||
                (i > 0 && e.Tick < _events[i - 1].Tick) || (e.ActorId.HasValue && !_actors.ContainsKey(e.ActorId.Value.Value))).Any())
            throw new CheckpointException("UncommittedJournal");
        long lastGrowth = checked((Tick / Labor.StepTicks * Labor.StepTicks) / Labor.GrowthPeriodTicks);
        if (_lastGrowthPeriod != lastGrowth || _population != ExpectedPopulation(lastGrowth))
            throw new CheckpointException("InvalidPopulationSchedule");
        if (_economy.Accounts[_treasury].Kind != "Treasury" || _economy.Accounts[_wages].Kind != "WagesSink" ||
            _economy.Accounts[_capital].Kind != "CapitalBudget" ||
            _actors.Values.Select(a => a.Account).Distinct().Count() != _actors.Count ||
            _actors.Values.Select(a => a.Inventory).Distinct().Count() != _actors.Count ||
            _economy.Accounts.Count(a => a.Value.Kind == "Participant") != _actors.Count)
            throw new CheckpointException("InvalidAccounts");
        var balances = _economy.Accounts.Keys.ToDictionary(id => id, _ => 0L);
        balances[_treasury]=_config.StartingTreasuryMinor;
        if (_travelQuantum is < 0 or >= TravelQuantaPerTick) throw new CheckpointException("InvalidTravel");
        foreach (var actor in _actors.Values)
            if (actor.OffsetX is < 0 or >= TravelUnitsPerCell || actor.OffsetZ is < 0 or >= TravelUnitsPerCell ||
                actor.HeadingX is < -1 or > 1 || actor.HeadingZ is < -1 or > 1 ||
                actor.FacingX is < -1 or > 1 || actor.FacingZ is < -1 or > 1 ||
                (actor.FacingX == 0 && actor.FacingZ == 0) ||
                (actor.Job is { Kind: "Move" } && (actor.OffsetX != TravelCenter || actor.OffsetZ != TravelCenter ||
                    actor.HeadingX != 0 || actor.HeadingZ != 0)))
                throw new CheckpointException("InvalidTravel");
        foreach (var a in _actors.Values)
        {
            if (_economy.Accounts[a.Account].Kind != "Participant" || _economy.Inventories[a.Inventory].Owner != a.Id ||
                _economy.Inventories[a.Inventory].Capacity != _config.Participants.Single(p => p.Id == a.Id).InventoryCapacity)
                throw new CheckpointException("InvalidParticipantReference");
            balances[a.Account] = _config.Participants.Single(p => p.Id == a.Id).CashMinor;
        }
        long incoming = 0, outgoing = 0;
        foreach (var l in _economy.Ledger)
        {
            if (l.Tick < 0 || l.Tick > Tick || l.AmountMinor <= 0 || l.DebitAccount == l.CreditAccount)
                throw new CheckpointException("InvalidLedger");
            if (l.DebitAccount == 0) incoming = checked(incoming + l.AmountMinor);
            else balances[l.DebitAccount] = checked(balances[l.DebitAccount] - l.AmountMinor);
            if (l.CreditAccount == 0) outgoing = checked(outgoing + l.AmountMinor);
            else balances[l.CreditAccount] = checked(balances[l.CreditAccount] + l.AmountMinor);
        }
        if (incoming != _economy.ExternalIn || outgoing != _economy.ExternalOut ||
            _economy.InitialMoney != checked(_config.StartingTreasuryMinor+_config.Participants.Sum(p => p.CashMinor)) ||
            balances.Any(p => p.Value != _economy.Accounts[p.Key].Balance)) throw new CheckpointException("LedgerBalanceMismatch");
        foreach (var i in _economy.Inventories.Values)
            if (i.Id < 1 || !_actors.ContainsKey(i.Owner) || !_map.Walkable(i.Position))
                throw new CheckpointException("InvalidInventoryReference");
        foreach (var g in EmploymentGroups())
            if (g.AssessedAtTick < 0 || g.AssessedAtTick > Tick || g.PendingWage < 0 || g.PendingAttractionWage < 0 ||
                g.PayEffectiveTick <= Tick || g.AttractionEffectiveTick <= Tick)
                throw new CheckpointException("InvalidPendingWage");
        foreach (var c in _seenCommands.Values)
            if (string.IsNullOrWhiteSpace(c.CommandId) || c.CommandId.Length > 160 || c.Action is null ||
                !_actors.ContainsKey(c.ActorId.Value) || c.LocalSequence <= 0 || c.IssuedAtTick < 0 || c.IssuedAtTick > Tick ||
                (c.NotBeforeTick <= c.IssuedAtTick && !IsImmediateBallot(c)) || c.NotBeforeTick > _config.MaxTicks || c.ExpiresAtTick < c.NotBeforeTick ||
                !Enum.IsDefined(c.Action.Kind)) throw new CheckpointException("InvalidStoredCommand");
        if (_pending.Select(c => c.CommandId).Distinct().Count() != _pending.Count ||
            _pending.Any(c => _results.ContainsKey(c.CommandId)) ||
            !_seenCommands.Keys.Order(StringComparer.Ordinal).SequenceEqual(_pending.Select(c => c.CommandId)
                .Concat(_results.Keys).Order(StringComparer.Ordinal)) ||
            _results.Values.Any(r => r.AppliedTick < 1 || r.AppliedTick > Tick || !Enum.IsDefined(r.Status)))
            throw new CheckpointException("InvalidCommandQueue");
        foreach (var e in ProductionUnits())
        {
            if (!_actors.ContainsKey(e.Definition.OwnerId) || e.Definition.RecipeId != e.Recipe.Id ||
                e.Definition.Position != _economy.Inventories[e.Input].Position ||
                e.Definition.Position != _economy.Inventories[e.Output].Position ||
                _economy.Inventories[e.Input].Owner != e.Definition.OwnerId || _economy.Inventories[e.Output].Owner != e.Definition.OwnerId ||
                e.Input == e.Output || e.Completed < 0 || e.EnergyPaid < 0)
                throw new CheckpointException("InvalidProductionReference");
            if (e.Batch is { } batch)
            {
                int full = e.Definition.BuildingTypeId == "" ? e.Recipe.RequiredStaff : e.Group.Capacity;
                if (batch.RecipeId!=e.Recipe.Id || batch.Inputs.IsDefault || !batch.Inputs.SequenceEqual(e.Recipe.Ingredients) || batch.WorkRequired != checked(e.Recipe.FullStaffTicks * (ParallelWorkshops ? 100 : full)) || batch.WorkDone <= 0 ||
                    batch.EnergyPaid != (ParallelWorkshops ? checked((long)((Int128)_config.Economy!.Workshops!.ServiceMinorPerFullTick * batch.WorkDone / 100)) : checked((long)((Int128)e.Recipe.EnergyPerBatchMinor * batch.WorkDone / batch.WorkRequired))))
                    throw new CheckpointException("InvalidProductionProgress");
            }
        }
        var expectedCapacity = new Dictionary<(long Inventory, string Hold), long>();
        var expectedStock = new Dictionary<(long Inventory, string Hold), StockReservation>();
        var expectedMoney = new Dictionary<(long Account, string Hold), long>();
        foreach (var actor in _actors.Values)
        {
            if (actor.Job is not { } j) continue;
            if (j.Id < 1000 || j.StartTick < 1 || j.StartTick > Tick || j.CompletionTick <= Tick ||
                !_seenCommands.ContainsKey(j.CommandId)) throw new CheckpointException("InvalidPersonalJob");
            if (j.Kind == "Move" && Roads is not null && DrivenVehicle(actor.Id) is {} movingVehicle)
            {
                if(j.Path.Length<2 || j.Path.Any(c=>!_map.Walkable(c)) || !_traffic.TryGetValue(movingVehicle.Id,out var traffic) && actor.Position!=j.Path[0] || traffic is not null && traffic.Path.Length>0 && traffic.Path[^1]!=(traffic.Resume??j.Path[^1]) && traffic.Resume!=j.Path[^1])throw new CheckpointException("InvalidRoadMove");
            }
            else if (j.Kind == "Move")
            {
                if (j.Path.Length < 2 || j.PathIndex < 0 || j.PathIndex >= j.Path.Length - 1 ||
                    j.SegmentTicks < 0 || j.SegmentTicks >= PersonalMoveTicks(actor,j.Path[j.PathIndex],j.Path[j.PathIndex+1]) || j.Path[j.PathIndex] != actor.Position ||
                    j.Path.Any(c => !_map.Walkable(c)) || j.Path.Zip(j.Path.Skip(1)).Any(p =>
                        Math.Abs(p.First.X - p.Second.X) + Math.Abs(p.First.Z - p.Second.Z) != 1) ||
                    j.CompletionTick != checked(j.StartTick + j.Path.Zip(j.Path.Skip(1)).Sum(p=>(long)PersonalMoveTicks(actor,p.First,p.Second))) ||
                    Tick - j.StartTick != checked(j.Path.Take(j.PathIndex+1).Zip(j.Path.Skip(1).Take(j.PathIndex)).Sum(p=>(long)PersonalMoveTicks(actor,p.First,p.Second)) + j.SegmentTicks))
                    throw new CheckpointException("InvalidPathProgress");
            }
            else if (IsHarvest(j))
            {
                if (j.Quantity <= 0 || j.Path.Length != 0 || !_config.ResourceNodes.Any(n => n.ItemId == j.Item && n.Position == actor.Position))
                    throw new CheckpointException("InvalidHarvestJob");
                long duration = j.Kind != "EquipmentHarvest" ? checked(j.Quantity * _config.HarvestTicksPerUnit) :
                    checked(((j.Quantity - 1) / _equipment[j.EquipmentId].Characteristics.CycleQuantity + 1) *
                        _equipment[j.EquipmentId].Characteristics.CycleTicks);
                if (j.CompletionTick != checked(j.StartTick + duration)) throw new CheckpointException("InvalidHarvestTime");
                if (j.Kind == "HarvestLoop" && (j.Quantity != 1 || j.EquipmentId != 0 ||
                    j.CapacityHold != $"harvest-loop:{j.Id}" ||
                    _seenCommands[j.CommandId].ActorId.Value != actor.Id ||
                    _seenCommands[j.CommandId].Action.Kind != ActionKind.StartHarvesting ||
                    _seenCommands[j.CommandId].Action.ItemId != j.Item ||
                    !_results.TryGetValue(j.CommandId, out var start) || start.Status != ResultStatus.Accepted))
                    throw new CheckpointException("InvalidHarvestLoop");
                expectedCapacity.Add((actor.Inventory, j.CapacityHold), checked(j.Quantity * _items[j.Item].UnitVolume));
            }
            else throw new CheckpointException("UnknownPersonalJob");
        }
        foreach (var e in ProductionUnits())
            if (e.Batch is { } b) expectedCapacity.Add((e.Output, b.Hold), checked(e.Recipe.OutputQuantity * _items[e.Recipe.OutputItem].UnitVolume));
        foreach (var e in _equipment.Values)
            if (e.Batch is { } b)
            {
                if (b.WorkRequired != checked(e.Characteristics.CycleTicks * e.Group.Capacity) || b.EnergyPaid != 0)
                    throw new CheckpointException("InvalidExtractionProgress");
                expectedCapacity.Add((e.Storage, b.Hold), checked(e.Characteristics.CycleQuantity * _items[e.Type.ItemId].UnitVolume));
            }
        foreach (var o in _offers.Values)
        {
            if (o.Remaining < 0 || o.Remaining > o.Definition.Quantity || o.Cutoff <= Tick ||
                ((o.Requests.Count + o.SupplyRequests.Count) > 0 && o.Cutoff is null)) throw new CheckpointException("InvalidTradeWindow");
            long budget = checked(o.Remaining * o.Definition.UnitPriceMinor);
            if (budget > 0) expectedMoney.Add((_capital, o.BudgetHold), budget);
            foreach (var r in o.Requests.Values)
            {
                if (r.InventoryId != _actors[r.ActorId].Inventory || r.Quantity <= 0 || !_results.ContainsKey(r.CommandId))
                    throw new CheckpointException("InvalidSaleRequest");
                expectedStock.Add((r.InventoryId, r.Reservation), new(o.Definition.ItemId, r.Quantity));
            }
        }
        AddExpectedDevelopmentHolds(expectedCapacity);
        AddExpectedTransferHolds(expectedCapacity,expectedStock);
        AddExpectedSupportHolds(expectedStock);
        AddExpectedMarketHolds(expectedStock, expectedMoney);
        AddExpectedDeliveryHolds(expectedCapacity, expectedMoney);
        AddExpectedLogisticsHolds(expectedCapacity, expectedStock, expectedMoney);AddExpectedFleetHolds(expectedMoney);AddExpectedRoadHolds(expectedMoney);
        foreach (var inv in _economy.Inventories.Values)
        {
            foreach (var h in inv.CapacityHolds)
                if (!expectedCapacity.Remove((inv.Id, h.Key), out long value) || h.Value != value)
                    throw new CheckpointException("OrphanCapacityReservation");
            foreach (var h in inv.Reservations)
                if (!expectedStock.Remove((inv.Id, h.Key), out var value) || h.Value != value)
                    throw new CheckpointException("OrphanStockReservation");
        }
        foreach (var a in _economy.Accounts.Values)
            foreach (var h in a.Holds)
                if (!expectedMoney.Remove((a.Id, h.Key), out long value) || h.Value != value)
                    throw new CheckpointException("OrphanMoneyReservation");
        if (expectedCapacity.Count + expectedStock.Count + expectedMoney.Count != 0) throw new CheckpointException("MissingReservation");
        long maximum = _economy.Accounts.Keys.Concat(_economy.Inventories.Keys).Concat(_enterprises.Keys).Concat(_equipment.Keys)
            .Concat(_roadOrders.Select(o=>o.Id)).Concat(_vehicles.Keys).Concat(_dealerStock.Select(s=>s.Id)).Concat(_platforms.Keys).Concat(_modernizations.Keys).Concat(_additionalWorkshops.Keys).Concat(_workshopInstallations.Keys).Concat(_offers.Keys).Concat(_logistics.Keys).Concat(_logistics.Values.Where(l => l.Trip is not null).Select(l => l.Trip!.Id))
            .Concat(_buildings.Keys).Concat(_buyOffers.Keys).Concat(_deliveryContracts.Select(d => d.Id)).Concat(_marketOffers.Keys).Concat(_marketAllocations.Select(a => a.Id)).Concat(_completedPersonalJobs).Concat(_events.Select(e => e.EntityId))
            .Concat(_actors.Values.Where(a => a.Job != null).Select(a => a.Job!.Id)).DefaultIfEmpty(0).Max();
        if (_nextId <= maximum) throw new CheckpointException("NextIdWouldDuplicateEntity");
    }
}
