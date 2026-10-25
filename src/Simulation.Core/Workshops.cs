using System.Collections.Immutable;

namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private readonly Dictionary<long, EnterpriseState> _additionalWorkshops = [];
    private readonly Dictionary<long, long> _workshopParents = [];
    private readonly Dictionary<long, long> _workshopTurns = [];
    private readonly Dictionary<long, WorkshopInstallation> _workshopInstallations = [];
    private long _workshopStartSerial;
    private bool ParallelWorkshops => _config.Economy?.Workshops is not null;
    private IEnumerable<EnterpriseState> ProductionUnits() => _enterprises.Values.Concat(_additionalWorkshops.Values);
    private long ProductionParent(long id) => _workshopParents.GetValueOrDefault(id, id);
    private IEnumerable<EnterpriseState> UnitsFor(long enterprise) => ProductionUnits().Where(e => ProductionParent(e.Definition.Id) == enterprise);
    private EnterpriseState ProductionUnit(long id) => _enterprises.GetValueOrDefault(id) ?? _additionalWorkshops[id];
    private EnterpriseState OwnProductionUnit(ActorState actor, GameAction action)
    {
        if (!_enterprises.TryGetValue(action.Enterprise.Value, out var parent) || parent.Definition.OwnerId != actor.Id || Demolished(parent.Definition.Id))
            throw new RuleViolation("NotAvailable");
        long id = action.WorkshopId == 0 ? parent.Definition.Id : action.WorkshopId;
        if (ProductionParent(id) != parent.Definition.Id || (!_enterprises.ContainsKey(id) && !_additionalWorkshops.ContainsKey(id)))
            throw new RuleViolation("NotAvailable");
        return ProductionUnit(id);
    }
    private ImmutableArray<WorkshopView> WorkshopViews(long enterprise) => !ParallelWorkshops ? default :
        [.. UnitsFor(enterprise).OrderBy(e => e.Definition.Id).Select(e => new WorkshopView(e.Definition.Id, new(e.Group.Id), e.Recipe.Id,
            e.Enabled, e.Group.Headcount, e.Reason, e.Batch?.Id ?? 0, e.Batch?.WorkDone ?? 0, e.Batch?.WorkRequired ?? 0,
            e.Completed, e.EnergyPaid, e.Batch?.Terms?.WageMinor, WorkshopRules.Rate(e.Group.Headcount)))];
    private ImmutableArray<WorkshopInstallation> InstallationViews(long enterprise) => !ParallelWorkshops ? default :
        [.. _workshopInstallations.Values.Where(w => w.Enterprise.Value == enterprise).OrderBy(w => w.Id)];
    private int ReadyWorkshopSlots(BuildingState b) => b.Type.Levels.Single(l => l.Level == b.Level).WorkshopSlots;

    private void OutfitWorkshop(ActorState actor, CommandEnvelope c)
    {
        var parent = OwnProductionUnit(actor, c.Action with { WorkshopId = 0 });
        if (!ParallelWorkshops || !_buildings.TryGetValue(parent.Definition.Id, out var b) || b.Type.WorkshopOutfit is not { } outfit)
            throw new RuleViolation("NotAvailable");
        RequireIdle(actor); RequireActionLocation(actor, c.Action);
        if(PreparingDevelopment(b.Id))throw new RuleViolation("PreparingDevelopment");
        if(StorageWorkAt(b.Position))throw new RuleViolation("ConstructionInProgress");
        if (UnitsFor(b.Id).Count() + _workshopInstallations.Values.Count(w => w.Enterprise.Value == b.Id) >= ReadyWorkshopSlots(b))
            throw new RuleViolation("NoReadyWorkshopSlot");
        long id = _nextId, next = checked(id + 1), end = checked(Tick + outfit.Ticks);
        var consumed = PrepareMaterialFlows(outfit.Materials.ToArray());
        var tx = _economy.Begin(Tick);
        foreach (var m in outfit.Materials) tx.RemoveFreeStock(b.Input, m.ItemId, m.Quantity);
        if (outfit.PriceMinor > 0) tx.Pay(actor.Account, _treasury, outfit.PriceMinor, "WorkshopOutfit");
        tx.Commit(); _nextId = next;
        _workshopInstallations.Add(id, new(id, new(b.Id), Tick, end, outfit.PriceMinor, outfit.Materials, c.CommandId));
        foreach (var pair in consumed) _flows[pair.Key].ConstructionInput = pair.Value;
        Emit("WorkshopOutfitStarted", actor.Id, b.Id, quantity: id, money: outfit.PriceMinor, command: c.CommandId);
        EmitMaterialConsumption(actor.Id, b.Id, outfit.Materials.ToArray(), c.CommandId);
    }
    private void AdvanceWorkshopInstallations()
    {
        foreach (var w in _workshopInstallations.Values.Where(w => w.EndTick <= Tick).OrderBy(w => w.Id).ToArray())
        {
            var parent = _enterprises[w.Enterprise.Value];
            var definition = parent.Definition with { Id = w.Id, Staff = 0, Capacity = 5, Enabled = false };
            _additionalWorkshops.Add(w.Id, new(definition, parent.Input, parent.Output, parent.Recipe));
            _workshopParents.Add(w.Id, parent.Definition.Id); _workshopInstallations.Remove(w.Id);
            Emit("WorkshopOutfitCompleted", parent.Definition.OwnerId, parent.Definition.Id, quantity: w.Id);
        }
    }
    private void AssignWorkshopWorkers(ActorState actor, CommandEnvelope c)
    {
        if (!ParallelWorkshops) throw new RuleViolation("NotAvailable");
        var to = OwnProductionUnit(actor, c.Action);
        long fromId = c.Action.Group.Value;
        if ((!_enterprises.ContainsKey(fromId) && !_additionalWorkshops.ContainsKey(fromId)) ||
            ProductionParent(fromId) != c.Action.Enterprise.Value || fromId == to.Definition.Id)
            throw new RuleViolation("NotAvailable");
        var from = ProductionUnit(fromId);
        long q = c.Action.Quantity;
        if (q <= 0 || q > from.Group.Headcount || q > to.Group.Capacity - to.Group.Headcount) throw new RuleViolation("InvalidStaffTransfer");
        // Only existing employees of this enterprise move; no hiring or population creation.
        from.Group.Headcount -= (int)q; to.Group.Headcount += (int)q;
        Emit("EmploymentChanged", actor.Id, fromId, quantity: -q, command: c.CommandId);
        Emit("EmploymentChanged", actor.Id, to.Group.Id, quantity: q, command: c.CommandId);
    }
    private RecipeDefinition[] ProductionRecipes(EnterpriseState parent) => UnitsFor(ProductionParent(parent.Definition.Id))
        .Select(e => e.Recipe).DistinctBy(r => r.Id).OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();
    private bool ProductionUses(EnterpriseState parent, string item) => ProductionRecipes(parent).Any(r => r.Uses(item));
    private string[] ProductionOutputs(EnterpriseState parent)
    {
        var recipes=ProductionRecipes(parent).AsEnumerable();
        if(ParallelWorkshops&&_buildings.TryGetValue(parent.Definition.Id,out var b))
            recipes=b.Type.Levels.Where(l=>l.Level<=b.Level).SelectMany(l=>l.UnlockedRecipes).Distinct(StringComparer.Ordinal).Select(id=>_config.Recipes.Single(r=>r.Id==id));
        return recipes.Select(r=>r.OutputItem).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    public object? InspectWorkshops()=>CaptureWorkshops();
    private WorkshopCheckpoint? CaptureWorkshops() => !ParallelWorkshops ? null : new(_workshopStartSerial,
        _workshopTurns.OrderBy(t => t.Key).Select(t => new WorkshopTurn(t.Key, t.Value)).ToArray(),
        _additionalWorkshops.Values.OrderBy(e => e.Definition.Id).Select(e => new SavedWorkshop(ProductionParent(e.Definition.Id), SaveProductionUnit(e))).ToArray(),
        _workshopInstallations.Values.OrderBy(w => w.Id).ToArray());
    private static SavedEnterprise SaveProductionUnit(EnterpriseState e) => new(e.Definition, e.Input, e.Output, e.Recipe.Id,
        e.Enabled, e.Reason, SaveBatch(e.Batch), e.Completed, e.EnergyPaid, e.Group.View());
    private void RestoreWorkshops(WorkshopCheckpoint? saved)
    {
        if (ParallelWorkshops != (saved is not null)) throw new CheckpointException("MissingWorkshopState");
        if (saved is null) return;
        _workshopStartSerial = saved.StartSerial;
        foreach (var t in saved.Turns) _workshopTurns.Add(t.Id, t.LastStart);
        foreach (var w in saved.Additional)
        {
            var v = w.Unit;
            var unit = new EnterpriseState(v.Definition, v.Input, v.Output, _config.Recipes.Single(r => r.Id == v.RecipeId))
                { Enabled = v.Enabled, Reason = v.Reason, Batch = LoadBatch(v.Batch), Completed = v.Completed, EnergyPaid = v.EnergyPaid };
            LoadGroup(unit.Group, v.Group); _additionalWorkshops.Add(v.Definition.Id, unit); _workshopParents.Add(v.Definition.Id, w.Enterprise);
        }
        foreach (var w in saved.Installations) _workshopInstallations.Add(w.Id, w);
    }
    private void ValidateWorkshops()
    {
        if (!ParallelWorkshops)
        {
            if (_additionalWorkshops.Count + _workshopInstallations.Count + _workshopTurns.Count != 0 || _workshopStartSerial != 0)
                throw new CheckpointException("UnexpectedWorkshopState");
            return;
        }
        if (_workshopStartSerial < 0 || _workshopTurns.Any(t => t.Value <= 0 || t.Value > _workshopStartSerial || !ProductionUnits().Any(e => e.Definition.Id == t.Key)) ||
            _workshopTurns.Values.Distinct().Count() != _workshopTurns.Count || _workshopTurns.Values.DefaultIfEmpty(0).Max() != _workshopStartSerial ||
            _workshopStartSerial != _events.Where(e => e.Type == "ProductionBatchStarted").Select(e => e.EntityId).Distinct().LongCount())
            throw new CheckpointException("InvalidWorkshopTurns");
        if (EmploymentGroups().Select(g=>g.Id).Distinct().Count()!=EmploymentGroups().Count() || _workshopInstallations.Keys.Any(id=>ProductionUnits().Any(e=>e.Definition.Id==id))) throw new CheckpointException("DuplicateWorkshopIdentity");
        foreach (var e in ProductionUnits())
        {
            long parentId = ProductionParent(e.Definition.Id);
            if (!_enterprises.TryGetValue(parentId, out var parent) || e.Group.Capacity != 5 || e.Definition.WorkRateMultiplier != 1 ||
                parent.Input != e.Input || parent.Output != e.Output || parent.Definition.OwnerId != e.Definition.OwnerId || parent.Definition.Position != e.Definition.Position ||
                Demolished(parentId) && (e.Enabled || e.Batch is not null || e.Group.Headcount != 0)) throw new CheckpointException("InvalidWorkshopReference");
            if (e != parent && (!_buildings.ContainsKey(parentId) || !_events.Any(v => v.Type == "WorkshopOutfitCompleted" && v.EntityId == parentId && v.Quantity == e.Definition.Id)))
                throw new CheckpointException("UnpaidWorkshop");
            if (_buildings.TryGetValue(parentId, out var b) && !b.Type.Levels.Where(l => l.Level <= b.Level).SelectMany(l => l.UnlockedRecipes).Contains(e.Recipe.Id, StringComparer.Ordinal))
                throw new CheckpointException("InvalidWorkshopRecipe");
            if (e.Batch is { } batch && (batch.Terms is not { } terms || terms.WageMinor < 0 || terms.ServiceMinorPerFullTick != _config.Economy!.Workshops!.ServiceMinorPerFullTick ||
                ScenarioValidation.Hash(terms.Recipe) != ScenarioValidation.Hash(e.Recipe) || batch.WorkRequired != checked(e.Recipe.FullStaffTicks * 100) || !_events.Any(v=>v.Type=="WorkshopBatchStarted"&&v.EntityId==batch.Id&&v.ItemId==e.Recipe.Id&&v.AmountMinor==terms.WageMinor&&v.Quantity==terms.ServiceMinorPerFullTick&&v.ReasonCode==e.Definition.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))))
                throw new CheckpointException("InvalidWorkshopBatchTerms");
        }
        foreach (var b in _buildings.Values.Where(b => b.Type.Kind == BuildingKind.Production))
            if (UnitsFor(b.Id).Count() + _workshopInstallations.Values.Count(w => w.Enterprise.Value == b.Id) > ReadyWorkshopSlots(b))
                throw new CheckpointException("TooManyWorkshops");
        foreach (var w in _workshopInstallations.Values)
        {
            if (!_buildings.TryGetValue(w.Enterprise.Value, out var b) || Demolished(b.Id) || b.Type.WorkshopOutfit is not { } outfit ||
                w.StartTick < 1 || w.StartTick > Tick || w.EndTick <= Tick || w.EndTick != checked(w.StartTick + outfit.Ticks) ||
                w.PriceMinor != outfit.PriceMinor || !w.Materials.SequenceEqual(outfit.Materials) ||
                !_results.TryGetValue(w.CommandId, out var result) || result.Status != ResultStatus.Accepted ||
                !_events.Any(e => e.Type == "WorkshopOutfitStarted" && e.EntityId == b.Id && e.Quantity == w.Id && e.CommandId == w.CommandId && e.AmountMinor == w.PriceMinor))
                throw new CheckpointException("InvalidWorkshopInstallation");
        }
    }
}

public static partial class ScenarioValidation
{
    private static void ValidateWorkshopContent(ScenarioDefinition s)
    {
        bool enabled = s.Economy?.Workshops is not null;
        foreach (var t in s.BuildingTypes ?? [])
        {
            if (!enabled && (t.WorkshopOutfit is not null || t.Levels.Any(l => l.WorkshopSlots != 0)))
                throw new ArgumentException("Workshop content requires explicit workshop rules");
            if (!enabled || t.Kind != BuildingKind.Production) continue;
            foreach (var l in t.Levels)
            {
                int cells = l.WorkshopSlots == 1 ? 1 : l.WorkshopSlots + 1;
                if (l.WorkshopSlots is < 1 or > 5 || (l.Footprint.IsDefault?t.Footprint.Length:l.Footprint.Length) < cells || l.StaffCapacity != 5 || l.WorkRateMultiplier != 1)
                    throw new ArgumentException("Invalid ready workshop places or staffing");
            }
            if (t.WorkshopOutfit is { } o && (o.PriceMinor <= 0 || o.Ticks is < 1 or > 1000000 || o.Materials.IsDefaultOrEmpty ||
                o.Materials.Any(m => m is null || m.Quantity <= 0 || !s.Items.Any(i => i.Id == m.ItemId)) ||
                o.Materials.Select(m => m.ItemId).Distinct(StringComparer.Ordinal).Count() != o.Materials.Length))
                throw new ArgumentException("Invalid workshop outfit");
        }
        if (enabled && s.Enterprises.Any(e => e.Capacity != 5 || e.WorkRateMultiplier != 1))
            throw new ArgumentException("Workshop staffing requires five real places per equipped workshop");
        if (enabled)
            foreach (var recipe in s.Recipes) _ = checked(recipe.FullStaffTicks * 100);
    }
}
