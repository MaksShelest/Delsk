namespace Simulation.Core;

internal sealed class EquipmentState(long id, long owner, Cell position, long storage,
    EquipmentTypeDefinition type, int level)
{
    public long Id { get; } = id;
    public long Owner { get; } = owner;
    public Cell Position { get; set; } = position;
    public EquipmentMotion? Motion { get; set; }
    public ExtractionRoute? Route { get; set; }
    public long MovedAtTick { get; set; }
    public long Storage { get; } = storage;
    public EquipmentTypeDefinition Type { get; } = type;
    public int Level { get; set; } = level;
    public EquipmentLevelDefinition Characteristics => Type.Levels.Single(l => l.Level == Level);
    public EmploymentGroup Group { get; } = new(id, owner, 0,
        type.Levels.Single(l => l.Level == level).CrewCapacity,
        type.Levels.Single(l => l.Level == level).WageMinor, EmploymentRole.Extraction);
    public EquipmentMode Mode { get; set; } = EquipmentMode.Personal;
    public string Reason { get; set; } = "PersonalMode";
    public BatchState? Batch { get; set; }
    public long Completed { get; set; }
    public long PersonalJobId { get; set; }
}

public sealed partial class SimulationEngine
{
    private readonly Dictionary<long, EquipmentState> _equipment = [];

    private EquipmentState OwnEquipment(ActorState actor, EquipmentId id)
    {
        if (!_equipment.TryGetValue(id.Value, out var e) || e.Owner != actor.Id) throw new RuleViolation("NotAvailable");
        return e;
    }
    private void BuyEquipment(ActorState actor, CommandEnvelope c)
    {
        RequireIdle(actor);
        var type = (_config.EquipmentTypes ?? []).SingleOrDefault(t => t.Id == c.Action.EquipmentTypeId)
            ?? throw new RuleViolation("NotAvailable");
        var level = type.Levels.SingleOrDefault(l => l.Level == c.Action.EquipmentLevel)
            ?? throw new RuleViolation("EquipmentLevelUnavailable");
        RequireActionLocation(actor, c.Action, "NotAtResourceNode");
        if (!_config.ResourceNodes.Any(n => n.ItemId == type.ItemId && n.Position == actor.Position))
            throw new RuleViolation("NotAtResourceNode");
        if (!_resources.TryGetValue((actor.Position,type.ItemId), out var resource) || !ResourceAccess(actor.Id, resource, true)) throw new RuleViolation("NoResourceRights");
        long id = _nextId, storage = checked(_nextId + 1), nextId = checked(_nextId + 2);
        var equipment = new EquipmentState(id, actor.Id, actor.Position, storage, type, level.Level);
        var tx = _economy.Begin(Tick);
        tx.Pay(actor.Account, _treasury, level.PriceMinor, "EquipmentPurchase");
        tx.CreateInventory(storage, actor.Id, actor.Position, level.StorageCapacity); tx.Commit();
        _nextId = nextId; _equipment.Add(id, equipment);
        Emit("EquipmentPurchased", actor.Id, id, money: level.PriceMinor, reason: type.Id, command: c.CommandId);
    }
    private void UpgradeEquipment(ActorState actor, CommandEnvelope c)
    {
        var e = OwnEquipment(actor, c.Action.Equipment); RequireIdle(actor);
        RequireActionLocation(actor, c.Action);
        if (e.Mode != EquipmentMode.Off || e.Batch is not null || e.PersonalJobId != 0 || e.Motion is not null)
            throw new RuleViolation("EquipmentNotSafe");
        var level = e.Type.Levels.SingleOrDefault(l => l.Level == e.Level + 1)
            ?? throw new RuleViolation("MaxEquipmentLevel");
        _ = checked(level.CrewCapacity * e.Group.Wage);
        if (e.Group.PendingWage is { } pending) _ = checked(level.CrewCapacity * pending);
        var tx = _economy.Begin(Tick);
        tx.Pay(actor.Account, _treasury, level.PriceMinor, "EquipmentUpgrade");
        tx.SetCapacity(e.Storage, level.StorageCapacity); tx.Commit();
        e.Level = level.Level; e.Group.Capacity = level.CrewCapacity;
        Emit("EquipmentUpgraded", actor.Id, e.Id, money: level.PriceMinor, command: c.CommandId);
    }
    private void SetEquipmentMode(ActorState actor, CommandEnvelope c)
    {
        var e = OwnEquipment(actor, c.Action.Equipment);
        if (!Enum.IsDefined(c.Action.Mode)) throw new RuleViolation("InvalidEquipmentMode");
        if (e.PersonalJobId != 0 || ((e.Batch is not null || e.Motion is not null) && c.Action.Mode == EquipmentMode.Personal))
            throw new RuleViolation("EquipmentBusy");
        e.Mode = c.Action.Mode;
        e.Reason = e.Mode switch { EquipmentMode.Off => "UserPaused", EquipmentMode.Personal => "PersonalMode", _ => "NoStaff" };
        Emit("EquipmentModeChanged", actor.Id, e.Id, reason: e.Mode.ToString(), command: c.CommandId);
    }
    private void StartEquipmentHarvest(ActorState actor, CommandEnvelope c)
    {
        var e = OwnEquipment(actor, c.Action.Equipment);
        if (e.Mode != EquipmentMode.Personal || e.Batch is not null || e.PersonalJobId != 0 || e.Motion is not null)
            throw new RuleViolation("EquipmentBusy");
        RequireActionLocation(actor, c.Action);
        if (c.Action.ItemId != e.Type.ItemId) throw new RuleViolation("WrongItem");
        var level = e.Characteristics;
        long cycles = checked((c.Action.Quantity - 1) / level.CycleQuantity + 1);
        long end = checked(Tick + checked(cycles * level.CycleTicks));
        RequireResource(actor.Id, e.Position, e.Type.ItemId, c.Action.Quantity, true);
        long id = _nextId, nextId = checked(_nextId + 1);
        string hold = $"equipment-personal:{id}";
        var tx = _economy.Begin(Tick);
        tx.ReserveCapacity(actor.Inventory, hold, c.Action.ItemId, c.Action.Quantity); tx.Commit();
        _nextId = nextId; e.PersonalJobId = id;
        ClaimResource(id, e.Position, c.Action.Quantity);
        actor.Job = new(id, "EquipmentHarvest", Tick, end, [], c.Action.ItemId, c.Action.Quantity, hold, c.CommandId, e.Id);
        Emit("PersonalEquipmentHarvestStarted", actor.Id, e.Id, c.Action.ItemId, c.Action.Quantity, command: c.CommandId);
    }
    private void AdvanceEquipment(EquipmentState e)
    {
        if (e.Mode != EquipmentMode.Automatic)
        { e.Reason = e.Mode == EquipmentMode.Off ? "UserPaused" : "PersonalMode"; return; }
        if (e.Motion is not null || e.MovedAtTick == Tick) return;
        var level = e.Characteristics;
        if (e.Batch is null && !EquipmentFull(e) && _resources[(e.Position,e.Type.ItemId)].Stock - ResourceReserved(e.Position,e.Type.ItemId) < level.CycleQuantity)
        { e.Reason = "ResourceDepleted"; ConsiderExtractionRoute(e); return; }
        if(e.Batch is null && EquipmentFull(e)){e.Reason="NoOutputSpace";return;}
        if (e.Group.Headcount < level.MinimumCrew) { e.Reason = "BelowMinimumCrew"; return; }
        try
        {
            bool starting = e.Batch is null;
            if (starting) RequireResource(e.Owner, e.Position, e.Type.ItemId, level.CycleQuantity, true);
            long required = e.Batch?.WorkRequired ?? checked(level.CycleTicks * level.CrewCapacity);
            long id = e.Batch?.Id ?? _nextId;
            long nextId = starting ? checked(_nextId + 1) : _nextId;
            string hold = e.Batch?.Hold ?? $"equipment-cycle:{id}";
            long done = e.Batch?.WorkDone ?? 0;
            long next = checked(done + Math.Min(required - done, e.Group.Headcount));
            long wage = checked(e.Group.Headcount * e.Group.Wage);
            bool complete = next == required;
            long harvested = complete ? checked(_flows[e.Type.ItemId].Harvested + level.CycleQuantity) : 0;
            long completed = complete ? checked(e.Completed + 1) : e.Completed;
            var tx = _economy.Begin(Tick);
            if (starting) tx.ReserveCapacity(e.Storage, hold, e.Type.ItemId, level.CycleQuantity);
            if (wage > 0) tx.Pay(_actors[e.Owner].Account, _wages, wage, "Wages");
            if (complete) { tx.ReleaseCapacity(e.Storage, hold); tx.AddStock(e.Storage, e.Type.ItemId, level.CycleQuantity); }
            tx.Commit();
            if (starting)
            {
                _nextId = nextId; e.Batch = new(id, hold, required);
                ClaimResource(id, e.Position, level.CycleQuantity);
                Emit("EquipmentCycleStarted", e.Owner, e.Id, e.Type.ItemId);
            }
            e.Batch!.WorkDone = next; e.Reason = "Working";
            if (wage > 0) Emit("WagesPaid", e.Owner, e.Group.Id, money: wage);
            if (complete)
            {
                ConsumeResource(id);
                _flows[e.Type.ItemId].Harvested = harvested; e.Completed = completed; e.Batch = null;
                Emit("EquipmentCycleCompleted", e.Owner, e.Id, e.Type.ItemId, level.CycleQuantity);
            }
        }
        catch (RuleViolation ex) { e.Reason = ex.ReasonCode == "NoCapacity" ? "NoOutputSpace" : ex.ReasonCode; }
    }
    private void AdvanceWork()
    {
        var groups = EmploymentGroups().OrderBy(g => g.Id).ToArray();
        if (ParallelWorkshops)
        {
            // Keep inter-role rotation; within each shared enterprise serve the least recently started line first.
            var rotated = groups.Skip((int)(Tick % Math.Max(1, groups.Length))).Concat(groups.Take((int)(Tick % Math.Max(1, groups.Length)))).ToArray();
            foreach (var parent in _enterprises.Keys.Order())
            {
                var slots = rotated.Select((g, i) => (g, i)).Where(x => x.g.Role == EmploymentRole.Production && ProductionParent(x.g.Id) == parent).Select(x => x.i).ToArray();
                var lines = slots.Select(i => rotated[i]).OrderBy(g => _workshopTurns.GetValueOrDefault(g.Id)).ThenBy(g => unchecked((ulong)g.Id ^ _config.Seed)).ToArray();
                for (int i = 0; i < slots.Length; ++i) rotated[slots[i]] = lines[i];
            }
            foreach (var g in rotated) AdvanceGroup(g);
            return;
        }
        for (int n = 0; n < groups.Length; ++n)
        {
            var g = groups[(n + (int)(Tick % groups.Length)) % groups.Length];
            AdvanceGroup(g);
        }
    }
    private void AdvanceGroup(EmploymentGroup g)
    {
        if (g.Role == EmploymentRole.Production) AdvanceProduction(ProductionUnit(g.Id));
        else if (g.Role == EmploymentRole.Extraction) AdvanceEquipment(_equipment[g.Id]);
        else AdvanceLogisticsWork(_logistics[g.Id]);
    }
    private long NextEquipmentCost(EquipmentState e, int staff) => checked(staff * (e.Group.PendingWage ?? e.Group.Wage));
    private string EquipmentVacancyReason(EquipmentState e)
    {
        if (e.Mode != EquipmentMode.Automatic) return e.Mode == EquipmentMode.Off ? "UserPaused" : "PersonalMode";
        if ((e.Group.PendingWage ?? e.Group.Wage) <= 0 || e.Group.AttractionWage <= 0) return "NoPaidOffer";
        if (e.Motion is not null || e.Route is { Enabled: true } route && !EquipmentFull(e) && route.Stops.Any(c=>
            _resources.TryGetValue((c,e.Type.ItemId),out var site) && site.Owner==e.Owner && site.Stock-ResourceReserved(c,e.Type.ItemId)>=e.Characteristics.CycleQuantity)) return "Ready";
        if (e.Batch is not null) return "Ready";
        try { RequireResource(e.Owner, e.Position, e.Type.ItemId, e.Characteristics.CycleQuantity, true); }
        catch (RuleViolation ex) { return ex.ReasonCode; }
        var inventory = _economy.Inventories[e.Storage];
        long free = checked(inventory.Capacity - inventory.Occupied(_items) - inventory.Incoming);
        return free < checked(e.Characteristics.CycleQuantity * _items[e.Type.ItemId].UnitVolume) ? "NoOutputSpace" : "Ready";
    }
    private EquipmentView[] EquipmentViews() => _equipment.Values.OrderBy(e => e.Id).Select(e => new EquipmentView(
        new(e.Id), new(e.Owner), e.Type.Id, e.Level, e.Position, new(e.Storage), new(e.Group.Id), e.Mode,
        e.Reason, e.Batch?.Id ?? 0, e.Batch?.WorkDone ?? 0, e.Batch?.WorkRequired ?? 0, e.Completed, e.PersonalJobId) { Motion = e.Motion, Route = e.Route }).ToArray();
    private EquipmentTypeDefinition[] CopyEquipmentCatalog() => (_config.EquipmentTypes ?? [])
        .OrderBy(t => t.Id, StringComparer.Ordinal).Select(t => t with { Levels = t.Levels.ToArray() }).ToArray();
    private void ValidateEquipmentInvariants()
    {
        foreach (var e in _equipment.Values)
        {
            if ((e.Motion is null && !_config.ResourceNodes.Any(n => n.Position == e.Position && n.ItemId == e.Type.ItemId)) ||
                _economy.Inventories[e.Storage].Position != e.Position || _economy.Inventories[e.Storage].Owner != e.Owner ||
                _economy.Inventories[e.Storage].Capacity != e.Characteristics.StorageCapacity ||
                e.Group.Capacity != e.Characteristics.CrewCapacity || e.Group.Role != EmploymentRole.Extraction ||
                e.Group.Id != e.Id || (e.Batch is not null && (e.Mode == EquipmentMode.Personal || e.PersonalJobId != 0)))
                throw new InvalidOperationException("Invalid equipment or employment ownership");
            if (e.Batch is { } batch && (batch.WorkDone <= 0 || batch.WorkDone >= batch.WorkRequired ||
                !_economy.Inventories[e.Storage].CapacityHolds.TryGetValue(batch.Hold, out long volume) ||
                volume != checked(e.Characteristics.CycleQuantity * _items[e.Type.ItemId].UnitVolume)))
                throw new InvalidOperationException("Invalid extraction work in progress");
            if (e.PersonalJobId != 0 && (e.Mode != EquipmentMode.Personal ||
                _actors[e.Owner].Job is not { Kind: "EquipmentHarvest" } job || job.Id != e.PersonalJobId || job.EquipmentId != e.Id))
                throw new InvalidOperationException("Personal machine operation lost its owner");
        }
        foreach (var actor in _actors.Values)
            if (actor.Job is { Kind: "EquipmentHarvest" } job &&
                (!_equipment.TryGetValue(job.EquipmentId, out var e) || e.PersonalJobId != job.Id))
                throw new InvalidOperationException("Personal job lost its machine");
    }
}
