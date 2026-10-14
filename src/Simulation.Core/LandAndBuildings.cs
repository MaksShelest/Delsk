namespace Simulation.Core;

internal sealed class PlotState(PlotDefinition definition)
{
    public PlotDefinition Definition { get; } = definition;
    public long? Owner { get; set; } = definition.OwnerId;
    public long? ReservedBy { get; set; }
    public long? Until { get; set; }
    public long? Building { get; set; }
}
internal sealed class BuildingState(long id, long owner, Cell position, BuildingTypeDefinition type,
    int level, long input, long? output, Cell[] footprint)
{
    public long Id { get; } = id;
    public long Owner { get; } = owner;
    public Cell Position { get; } = position;
    public BuildingTypeDefinition Type { get; } = type;
    public int Level { get; set; } = level;
    public long Input { get; } = input;
    public long? Output { get; } = output;
    public Cell[] Footprint { get; } = footprint;
}

public sealed partial class SimulationEngine
{
    private readonly Dictionary<long, PlotState> _plots = [];
    private readonly Dictionary<Cell, PlotState> _plotsByCell = [];
    private readonly Dictionary<string, BuildingTypeDefinition> _buildingTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<long, BuildingState> _buildings = [];

    private static bool IsManagement(ActionKind kind) => kind is ActionKind.SetProductionEnabled or
        ActionKind.ReservePlot or ActionKind.BuyPlot or ActionKind.CancelPlotReservation or ActionKind.Build or
        ActionKind.UpgradeBuilding or ActionKind.SetRecipe or ActionKind.SetWage;

    private void InitializeLandAndBuildings()
    {
        foreach (var type in _config.BuildingTypes ?? []) _buildingTypes.Add(type.Id, type);
        foreach (var definition in _config.Plots ?? [])
        {
            var plot = new PlotState(definition); _plots.Add(definition.Id, plot); _plotsByCell.Add(definition.Position, plot);
        }
        foreach (var definition in (_config.Buildings ?? []).OrderBy(b => b.Id))
        {
            var type = _buildingTypes[definition.TypeId];
            var level = type.Levels.Single(l => l.Level == definition.Level);
            long input = _nextId++; long? output = type.Kind == BuildingKind.Production ? _nextId++ : null;
            var tx = _economy.Begin(0);
            tx.CreateInventory(input, definition.OwnerId, definition.Position, level.InputCapacity);
            if (output.HasValue) tx.CreateInventory(output.Value, definition.OwnerId, definition.Position, level.OutputCapacity);
            tx.Commit();
            var footprint = ScenarioValidation.FootprintCells(definition.Position, type);
            var building = new BuildingState(definition.Id, definition.OwnerId, definition.Position, type,
                definition.Level, input, output, footprint);
            _buildings.Add(building.Id, building);
            foreach (var cell in footprint) _plotsByCell[cell].Building = building.Id;
            if (output.HasValue) AddProductionBuilding(building, level, definition.Staff, definition.Enabled);
        }
    }

    private void AddProductionBuilding(BuildingState b, BuildingLevelDefinition level, int staff, bool enabled)
    {
        var recipe = _config.Recipes.Single(r => r.Id == level.RecipeId);
        var definition = new EnterpriseDefinition(b.Id, b.Owner, b.Position, recipe.Id, level.InputCapacity,
            level.OutputCapacity, staff, level.StaffCapacity, level.WageMinor, enabled, b.Type.Id, b.Level, level.WorkRateMultiplier);
        _enterprises.Add(b.Id, new(definition, b.Input, b.Output!.Value, recipe));
    }

    private PlotState AccessiblePlot(ActorState actor, PlotId id)
    {
        RequireIdle(actor);
        if (!_plots.TryGetValue(id.Value, out var plot)) throw new RuleViolation("NotAvailable");
        if (plot.Definition.IsCorridor) throw new RuleViolation("ProtectedCorridor");
        if (plot.Definition.ShopParticipantId.HasValue && plot.Definition.ShopParticipantId != actor.Id)
            throw new RuleViolation("NotAvailable");
        if (actor.Position != plot.Definition.Position) throw new RuleViolation("NotAtLocation");
        return plot;
    }

    private void ReservePlot(ActorState actor, CommandEnvelope c)
    {
        var plot = AccessiblePlot(actor, c.Action.Plot);
        if (plot.Owner.HasValue) throw new RuleViolation("PlotOwned");
        if (plot.ReservedBy.HasValue) throw new RuleViolation("PlotReserved");
        if (_plots.Values.Count(p => p.ReservedBy == actor.Id) >= _config.MaxActivePlotReservations)
            throw new RuleViolation("ReservationLimit");
        long until = checked(Tick + _config.LandReservationTicks);
        // Exactly 10% for integral prices; round fractional minor units upward without overflow.
        long fee = checked((long)(((Int128)plot.Definition.PriceMinor + 9) / 10));
        var tx = _economy.Begin(Tick); tx.Pay(actor.Account, _treasury, fee, "PlotReservation"); tx.Commit();
        plot.ReservedBy = actor.Id; plot.Until = until;
        Emit("PlotReserved", actor.Id, plot.Definition.Id, money: fee, reason: until.ToString(System.Globalization.CultureInfo.InvariantCulture), command: c.CommandId);
    }

    private void BuyPlot(ActorState actor, CommandEnvelope c)
    {
        var plot = AccessiblePlot(actor, c.Action.Plot);
        if (plot.Owner.HasValue) throw new RuleViolation("PlotOwned");
        if (plot.ReservedBy.HasValue && plot.ReservedBy != actor.Id) throw new RuleViolation("PlotReserved");
        var tx = _economy.Begin(Tick); tx.Pay(actor.Account, _treasury, plot.Definition.PriceMinor, "PlotPurchase"); tx.Commit();
        plot.Owner = actor.Id; plot.ReservedBy = null; plot.Until = null;
        Emit("PlotPurchased", actor.Id, plot.Definition.Id, money: plot.Definition.PriceMinor, command: c.CommandId);
    }

    private void CancelPlotReservation(ActorState actor, CommandEnvelope c)
    {
        // A setting cancellation needs no physical trip; there is no refund or transfer of ownership.
        if (!_plots.TryGetValue(c.Action.Plot.Value, out var plot) || plot.ReservedBy != actor.Id)
            throw new RuleViolation("NotAvailable");
        plot.ReservedBy = null; plot.Until = null;
        Emit("PlotReservationCancelled", actor.Id, plot.Definition.Id, command: c.CommandId);
    }

    private void ExpirePlotReservations()
    {
        foreach (var plot in _plots.Values.OrderBy(p => p.Definition.Id))
            if (plot.Until.HasValue && plot.Until <= Tick)
            {
                long actor = plot.ReservedBy!.Value; plot.ReservedBy = null; plot.Until = null;
                Emit("PlotReservationExpired", actor, plot.Definition.Id);
            }
    }

    private void Build(ActorState actor, CommandEnvelope c)
    {
        RequireIdle(actor);
        if (!_buildingTypes.TryGetValue(c.Action.BuildingTypeId, out var type)) throw new RuleViolation("UnknownBuildingType");
        if (actor.Position != c.Action.Target) throw new RuleViolation("NotAtLocation");
        var footprint = ScenarioValidation.FootprintCells(c.Action.Target, type);
        foreach (var cell in footprint)
        {
            if (!_plotsByCell.TryGetValue(cell, out var plot) || plot.Definition.IsCorridor) throw new RuleViolation("InvalidFootprint");
            if (plot.Owner != actor.Id) throw new RuleViolation("LandNotOwned");
            if (plot.Building.HasValue) throw new RuleViolation("LandOccupied");
            if (plot.Definition.ShopParticipantId.HasValue && (type.Kind != BuildingKind.Shop || plot.Definition.ShopParticipantId != actor.Id))
                throw new RuleViolation("WrongBuildingSite");
        }
        if (type.Kind == BuildingKind.Shop && _plotsByCell[c.Action.Target].Definition.ShopParticipantId != actor.Id)
            throw new RuleViolation("WrongBuildingSite");
        var level = type.Levels.Single(l => l.Level == 1);
        long id = _nextId, input = checked(id + 1);
        long? output = type.Kind == BuildingKind.Production ? checked(id + 2) : null;
        long next = checked(id + (output.HasValue ? 3 : 2));
        var consumed = PrepareMaterialFlows(level.Materials);
        var tx = _economy.Begin(Tick);
        foreach (var material in level.Materials) tx.RemoveFreeStock(actor.Inventory, material.ItemId, material.Quantity);
        if (level.PriceMinor > 0) tx.Pay(actor.Account, _treasury, level.PriceMinor, "Construction");
        tx.CreateInventory(input, actor.Id, c.Action.Target, level.InputCapacity);
        if (output.HasValue) tx.CreateInventory(output.Value, actor.Id, c.Action.Target, level.OutputCapacity);
        var building = new BuildingState(id, actor.Id, c.Action.Target, type, 1, input, output, footprint);
        tx.Commit();
        _nextId = next; _buildings.Add(id, building);
        foreach (var cell in footprint) _plotsByCell[cell].Building = id;
        foreach (var pair in consumed) _flows[pair.Key].ConstructionInput = pair.Value;
        if (output.HasValue) AddProductionBuilding(building, level, 0, false); // Buying capacity never creates workers.
        Emit("BuildingConstructed", actor.Id, id, money: level.PriceMinor, reason: type.Id, command: c.CommandId);
        EmitMaterialConsumption(actor.Id, id, level.Materials, c.CommandId);
    }

    private Dictionary<string, long> PrepareMaterialFlows(MaterialCost[] materials) => materials.ToDictionary(m => m.ItemId,
        m => checked(_flows[m.ItemId].ConstructionInput + m.Quantity), StringComparer.Ordinal);
    private void EmitMaterialConsumption(long actor, long building, MaterialCost[] materials, string command)
    {
        foreach (var material in materials)
            Emit("ConstructionMaterialConsumed", actor, building, material.ItemId, material.Quantity, command: command);
    }

    private BuildingState OwnBuilding(ActorState actor, BuildingId id)
    {
        if (!_buildings.TryGetValue(id.Value, out var b) || b.Owner != actor.Id) throw new RuleViolation("NotAvailable");
        if (_enterprises.TryGetValue(b.Id, out var e) && (e.Enabled || e.Batch is not null)) throw new RuleViolation("ProductionNotSafe");
        return b;
    }

    private void UpgradeBuilding(ActorState actor, CommandEnvelope c)
    {
        var b = OwnBuilding(actor, c.Action.Building);
        var level = b.Type.Levels.SingleOrDefault(l => l.Level == checked(b.Level + 1)) ?? throw new RuleViolation("MaxLevel");
        var consumed = PrepareMaterialFlows(level.Materials);
        var tx = _economy.Begin(Tick);
        foreach (var material in level.Materials)
        {
            long fromInput = Math.Min(material.Quantity, _economy.Inventories[b.Input].Free(material.ItemId));
            if (fromInput > 0) tx.RemoveFreeStock(b.Input, material.ItemId, fromInput);
            long remainder = checked(material.Quantity - fromInput);
            if (remainder > 0)
            {
                if (!b.Output.HasValue) throw new RuleViolation("NoStock");
                tx.RemoveFreeStock(b.Output.Value, material.ItemId, remainder);
            }
        }
        if (level.PriceMinor > 0) tx.Pay(actor.Account, _treasury, level.PriceMinor, "BuildingUpgrade");
        if (_enterprises.TryGetValue(b.Id, out var upgrading))
        {
            var recipe = _config.Recipes.Single(r => r.Id == level.RecipeId);
            _ = checked(level.StaffCapacity * upgrading.Group.Wage + recipe.EnergyPerBatchMinor);
            if (upgrading.Group.PendingWage is { } pending)
                _ = checked(level.StaffCapacity * pending + recipe.EnergyPerBatchMinor);
        }
        tx.SetCapacity(b.Input, level.InputCapacity);
        if (b.Output.HasValue) tx.SetCapacity(b.Output.Value, level.OutputCapacity);
        tx.Commit();
        b.Level = level.Level;
        foreach (var pair in consumed) _flows[pair.Key].ConstructionInput = pair.Value;
        if (_enterprises.TryGetValue(b.Id, out var e))
        {
            e.Recipe = _config.Recipes.Single(r => r.Id == level.RecipeId);
            e.Definition = e.Definition with { Level = b.Level, RecipeId = level.RecipeId,
                InputCapacity = level.InputCapacity, OutputCapacity = level.OutputCapacity,
                Capacity = level.StaffCapacity, WorkRateMultiplier = level.WorkRateMultiplier };
            e.Group.Capacity = level.StaffCapacity;
        }
        Emit("BuildingUpgraded", actor.Id, b.Id, money: level.PriceMinor, reason: b.Level.ToString(System.Globalization.CultureInfo.InvariantCulture), command: c.CommandId);
        EmitMaterialConsumption(actor.Id, b.Id, level.Materials, c.CommandId);
    }

    private void SetRecipe(ActorState actor, CommandEnvelope c)
    {
        if (!_enterprises.TryGetValue(c.Action.Enterprise.Value, out var e) || e.Definition.OwnerId != actor.Id)
            throw new RuleViolation("NotAvailable");
        if (e.Enabled || e.Batch is not null) throw new RuleViolation("ProductionNotSafe");
        bool unlocked = _buildings.TryGetValue(e.Definition.Id, out var b)
            ? b.Type.Levels.Any(l => l.Level <= b.Level && l.RecipeId == c.Action.RecipeId)
            : c.Action.RecipeId == e.Recipe.Id;
        if (!unlocked) throw new RuleViolation("RecipeLocked");
        var recipe = _config.Recipes.Single(r => r.Id == c.Action.RecipeId);
        _ = checked(e.Group.Capacity * e.Group.Wage + recipe.EnergyPerBatchMinor);
        if (e.Group.PendingWage is { } pending)
            _ = checked(e.Group.Capacity * pending + recipe.EnergyPerBatchMinor);
        e.Recipe = recipe; e.Definition = e.Definition with { RecipeId = recipe.Id };
        Emit("RecipeChanged", actor.Id, e.Definition.Id, reason: recipe.Id, command: c.CommandId);
    }

    private PlotView[] LandViews() => _plots.Values.OrderBy(p => p.Definition.Id).Select(p => new PlotView(
        new(p.Definition.Id), p.Definition.Position, p.Definition.PriceMinor, p.Definition.IsCorridor,
        p.Owner.HasValue ? new ParticipantId(p.Owner.Value) : null, p.ReservedBy.HasValue ? new ParticipantId(p.ReservedBy.Value) : null,
        p.Until, p.Building.HasValue ? new BuildingId(p.Building.Value) : null,
        p.Definition.ShopParticipantId.HasValue ? new ParticipantId(p.Definition.ShopParticipantId.Value) : null)).ToArray();
    private BuildingView[] BuildingViews() => _buildings.Values.OrderBy(b => b.Id).Select(b => new BuildingView(
        new(b.Id), new(b.Owner), b.Type.Id, b.Type.Kind, b.Level, b.Position, b.Footprint.ToArray(), new(b.Input),
        b.Output.HasValue ? new InventoryId(b.Output.Value) : null)).ToArray();
    private BuildingTypeDefinition[] CopyBuildingCatalog() => _buildingTypes.Values.OrderBy(t => t.Id, StringComparer.Ordinal)
        .Select(t => t with { Footprint = t.Footprint.ToArray(), Levels = t.Levels.Select(l => l with { Materials = l.Materials.ToArray() }).ToArray() }).ToArray();

    private void ValidateLandInvariants()
    {
        foreach (var plot in _plots.Values)
            if ((plot.ReservedBy.HasValue != plot.Until.HasValue) ||
                (plot.ReservedBy.HasValue && (plot.Owner.HasValue || plot.Until <= Tick || !_actors.ContainsKey(plot.ReservedBy.Value))) ||
                (plot.Owner.HasValue && !_actors.ContainsKey(plot.Owner.Value)) ||
                (plot.Definition.IsCorridor && (plot.Owner.HasValue || plot.ReservedBy.HasValue || plot.Building.HasValue)))
                throw new InvalidOperationException("I17: invalid plot ownership/reservation");
        foreach (var b in _buildings.Values)
        {
            if (b.Footprint.Any(c => !_plotsByCell.TryGetValue(c, out var p) || p.Owner != b.Owner || p.Building != b.Id))
                throw new InvalidOperationException("I17: building footprint lost");
            var level = b.Type.Levels.Single(l => l.Level == b.Level);
            if (_economy.Inventories[b.Input].Owner != b.Owner || _economy.Inventories[b.Input].Position != b.Position ||
                _economy.Inventories[b.Input].Capacity != level.InputCapacity ||
                (b.Output.HasValue && (_economy.Inventories[b.Output.Value].Owner != b.Owner ||
                    _economy.Inventories[b.Output.Value].Position != b.Position ||
                    _economy.Inventories[b.Output.Value].Capacity != level.OutputCapacity ||
                    !_enterprises.TryGetValue(b.Id, out var e) || e.Definition.Level != b.Level || e.Group.Headcount > level.StaffCapacity)))
                throw new InvalidOperationException("Invalid building inventory/level");
        }
        foreach (var p in _plots.Values.Where(p => p.Building.HasValue))
            if (!_buildings.TryGetValue(p.Building!.Value, out var b) || !b.Footprint.Contains(p.Definition.Position))
                throw new InvalidOperationException("Orphan building footprint");
    }
}
