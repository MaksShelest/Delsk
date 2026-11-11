namespace Simulation.Core;

/// <summary>
/// Одноразовая программа провинции. Деньги не кладутся в кошелёк заранее:
/// казна получает целевое зачисление и тут же оплачивает участок, стройку или резерв.
/// Материалы приходят отдельной поставкой и учитываются в ProvincialSupplied.
/// </summary>
public sealed record ProvincialSupportRules(int Version, bool Enabled = true)
{
    public const int CurrentVersion = 1;
    public bool Applies => Version == CurrentVersion && Enabled;
    public void Validate()
    {
        if (Version != CurrentVersion) throw new ArgumentException("Provincial support version is not supported");
    }
}

public sealed record ProvincialSupplyLine(string ItemId, long Quantity, long UnitMinor, long Remaining, long ConstructionQuantity);

public sealed record ProvincialSupportState(
    long ActorId, string IndustryId, string Status,
    long ParcelReserved, long ParcelUsed,
    long ConstructionReserved, long ConstructionUsed,
    long OperatingReserved, long OperatingUsed,
    long SupplyValueMinor, long SupplyInventory, bool SupplyCollected,
    long SubsidizedConstructionId,
    MaterialCost[] RecoveryExcluded, ProvincialSupplyLine[] Supply, bool OutputSold = false);

/// <summary>Своя проекция программы. Чужие суммы сюда не входят.</summary>
public sealed record ProvincialProgramView(
    string IndustryId, string Status,
    long ParcelReserved, long ParcelRemaining,
    long ConstructionReserved, long ConstructionRemaining,
    long OperatingReserved, long OperatingRemaining,
    long SupplyValueMinor, bool SupplyCollected, string NextStep, MaterialCost[] Restricted);

public sealed partial class SimulationEngine
{
    private readonly Dictionary<long, ProvincialSupportState> _provincial = new();
    private readonly Dictionary<string, long> _provincialTotals = new(StringComparer.Ordinal);

    public ProvincialProgramView? ProvincialProgramOf(ParticipantId actor)
    {
        if (EconomyRules.ProvincialSupport is not { Applies: true } || ! _specializations.TryGetValue(actor.Value, out var chosen)) return null;
        if (!_provincial.TryGetValue(actor.Value, out var state))
        {
            var offered = BuildQuote(actor.Value, chosen.IndustryId);
            return ViewOf(offered with { Status = "Offered" }, "accept");
        }
        return ViewOf(state, NextOf(state));
    }

    private ProvincialSupportState[]? CaptureProvincialSupport() =>
        _provincial.Count == 0 ? null : _provincial.Values.OrderBy(s => s.ActorId).Select(CopyProgram).ToArray();

    private static ProvincialSupportState CopyProgram(ProvincialSupportState state) =>
        state with { RecoveryExcluded = state.RecoveryExcluded.ToArray(), Supply = state.Supply.ToArray() };

    private void RestoreProvincialSupport(ProvincialSupportState[]? saved)
    {
        _provincial.Clear();
        IndexProvincialSupply();
        if (_flows.Values.Any(f => f.ProvincialSupplied != _provincialTotals.GetValueOrDefault(f.Id)))
            throw new CheckpointException("InvalidProvincialFlow");
        if (saved is null)
        {
            if (_provincialTotals.Values.Any(n => n != 0)) throw new CheckpointException("UnexpectedProvincialSupport");
            return;
        }
        if (EconomyRules.ProvincialSupport is not { Applies: true }) throw new CheckpointException("UnexpectedProvincialSupport");
        var seen = new HashSet<long>();
        foreach (var row in saved)
        {
            if (row is null || !seen.Add(row.ActorId) || !_actors.ContainsKey(row.ActorId)) throw new CheckpointException("InvalidProvincialSupport");
            ProvincialSupportState expected;
            try { expected = BuildQuote(row.ActorId, row.IndustryId); }
            catch (RuleViolation) { throw new CheckpointException("InvalidProvincialSupport"); }
            if (row.ParcelReserved != expected.ParcelReserved || row.ParcelUsed is < 0 || row.ParcelUsed > row.ParcelReserved ||
                row.ConstructionReserved != expected.ConstructionReserved || row.ConstructionUsed is < 0 || row.ConstructionUsed > row.ConstructionReserved ||
                row.OperatingReserved != expected.OperatingReserved || row.OperatingUsed is < 0 || row.OperatingUsed > row.OperatingReserved ||
                row.SupplyValueMinor != expected.SupplyValueMinor || row.Supply is null || row.RecoveryExcluded is null ||
                row.Supply.Length != expected.Supply.Length)
                throw new CheckpointException("InvalidProvincialSupport");
            for (int i = 0; i < row.Supply.Length; i++)
            {
                var line = row.Supply[i]; var quote = expected.Supply[i];
                if (line.ItemId != quote.ItemId || line.Quantity != quote.Quantity || line.UnitMinor != quote.UnitMinor ||
                    line.ConstructionQuantity != quote.ConstructionQuantity || line.Remaining < 0 || line.Remaining > line.Quantity)
                    throw new CheckpointException("InvalidProvincialSupport");
            }
            bool done = row.ParcelUsed == row.ParcelReserved && row.ConstructionUsed == row.ConstructionReserved &&
                row.OperatingUsed == row.OperatingReserved && row.SupplyCollected;
            if (row.Status != (done ? "Completed" : "Open")) throw new CheckpointException("InvalidProvincialSupport");
            if (row.ConstructionUsed == 0 && (row.SubsidizedConstructionId != 0 || row.RecoveryExcluded.Length != 0 || row.OperatingUsed != 0))
                throw new CheckpointException("InvalidProvincialSupport");
            if (row.ConstructionUsed != 0 && !KnownConstruction(row.SubsidizedConstructionId)) throw new CheckpointException("InvalidProvincialSupport");
            foreach (var excluded in row.RecoveryExcluded)
            {
                var line = row.Supply.SingleOrDefault(s => s.ItemId == excluded.ItemId);
                if (line is null || excluded.Quantity <= 0 || excluded.Quantity > line.ConstructionQuantity) throw new CheckpointException("InvalidProvincialSupport");
            }
            long delivered = _events.Where(e => e.Type == "ProvincialSupplyDelivered" && e.ActorId?.Value == row.ActorId).Sum(e => e.Quantity);
            long expectedDelivery = row.SupplyCollected ? row.Supply.Sum(s => s.Quantity) : 0;
            if (delivered != expectedDelivery) throw new CheckpointException("InvalidProvincialSupport");
            bool sold = _events.Any(e => e.Type == "ProvincialOutputPurchased" && e.ActorId?.Value == row.ActorId);
            if (sold != row.OutputSold) throw new CheckpointException("InvalidProvincialSupport");
            if (row.SupplyCollected)
            {
                if (!_economy.Inventories.TryGetValue(row.SupplyInventory, out var inventory) || inventory.Owner != row.ActorId || inventory.Position != ImportTerminal)
                    throw new CheckpointException("InvalidProvincialSupport");
            }
            else if (row.SupplyInventory != 0) throw new CheckpointException("InvalidProvincialSupport");
            _provincial.Add(row.ActorId, CopyProgram(row));
        }
    }

    private bool KnownConstruction(long id) =>
        _buildings.ContainsKey(id) || _storageConstruction.ContainsKey(id) || _demolitionReceipts.Any(r => r.Target == id);

    private void IndexProvincialSupply()
    {
        _provincialTotals.Clear();
        foreach (var e in _events.Where(e => e.Type == "ProvincialSupplyDelivered"))
            _provincialTotals[e.ItemId] = checked(_provincialTotals.GetValueOrDefault(e.ItemId) + e.Quantity);
    }

    private void AcceptProvincialSupport(ActorState actor, CommandEnvelope command)
    {
        if (EconomyRules.ProvincialSupport is not { Applies: true }) throw new RuleViolation("ProvincialSupportDisabled");
        if (!_specializations.TryGetValue(actor.Id, out var chosen)) throw new RuleViolation("SpecializationRequired");
        if (command.Action.BuildingTypeId.Length > 0 && command.Action.BuildingTypeId != chosen.IndustryId) throw new RuleViolation("UnknownSpecialization");
        if (_provincial.ContainsKey(actor.Id) || chosen.SupportSlotsRemaining != 1) throw new RuleViolation("ProvincialSupportUsed");
        var state = BuildQuote(actor.Id, chosen.IndustryId);
        _specializations[actor.Id] = chosen with { SupportSlotsRemaining = 0 };
        _provincial[actor.Id] = state;
        Emit("ProvincialSupportAccepted", actor.Id, item: state.IndustryId,
            money: checked(state.ParcelReserved + state.ConstructionReserved + state.OperatingReserved + state.SupplyValueMinor),
            reason: "Open", command: command.CommandId);
    }

    private void CollectProvincialSupply(ActorState actor, CommandEnvelope command)
    {
        if (EconomyRules.ProvincialSupport is not { Applies: true }) throw new RuleViolation("ProvincialSupportDisabled");
        if (!_provincial.TryGetValue(actor.Id, out var state) || state.Status != "Open") throw new RuleViolation("ProvincialSupportUsed");
        if (state.SupplyCollected) throw new RuleViolation("ProvincialSupplyCollected");
        if (Content is null || _config.CapitalDemand is null) throw new RuleViolation("NotAvailable");
        RequireIdle(actor);
        RequireActionLocation(actor, command.Action);
        long id = _nextId++;
        long value = state.SupplyValueMinor;
        var tx = _economy.Begin(Tick);
        tx.CreateInventory(id, actor.Id, ImportTerminal, 1000000000);
        foreach (var line in state.Supply) tx.AddStock(id, line.ItemId, line.Quantity);
        // Поставка не проходит через импортный остаток и не становится свободной эмиссией в кошельке.
        tx.CreditExternal(_treasury, value, "ProvincialSupportAppropriation");
        tx.DebitExternal(_treasury, value, "ProvincialMaterialSupply");
        tx.Commit();
        foreach (var line in state.Supply)
        {
            _flows[line.ItemId].ProvincialSupplied = checked(_flows[line.ItemId].ProvincialSupplied + line.Quantity);
            _provincialTotals[line.ItemId] = checked(_provincialTotals.GetValueOrDefault(line.ItemId) + line.Quantity);
            Emit("ProvincialSupplyDelivered", actor.Id, id, line.ItemId, line.Quantity, checked(line.Quantity * line.UnitMinor), command: command.CommandId);
        }
        _provincial[actor.Id] = Finish(state with { SupplyInventory = id, SupplyCollected = true });
    }

    private long ParcelGrant(long actor)
    {
        if (!_provincial.TryGetValue(actor, out var state) || state.Status != "Open" || state.ParcelUsed != 0) return 0;
        return state.ParcelReserved;
    }

    private long ConstructionGrant(long actor, string industry, bool firstBuilding, int level, long price)
    {
        if (!firstBuilding || level != 1) return 0;
        if (!_provincial.TryGetValue(actor, out var state) || state.Status != "Open" || state.ConstructionUsed != 0 || state.IndustryId != industry) return 0;
        if (state.ConstructionReserved != price) throw new RuleViolation("NotAvailable");
        return state.ConstructionReserved;
    }

    private long OperatingGrant(StorageConstruction construction)
    {
        if (construction.BuildingId != 0 || !_provincial.TryGetValue(construction.Owner, out var state)) return 0;
        if (state.Status != "Open" || state.OperatingUsed != 0 || state.SubsidizedConstructionId != construction.Id) return 0;
        return state.OperatingReserved;
    }

    private void Appropriate(EconomyTransaction tx, long actorAccount, long amount, string grantReason)
    {
        tx.CreditExternal(_treasury, amount, "ProvincialSupportAppropriation");
        tx.Pay(_treasury, actorAccount, amount, grantReason);
    }

    /// <summary>
    /// Столица не выставляет заказ на дорогой первый продукт. Провинция покупает ровно одну партию
    /// по базовой цене, один раз, и только пока открытого столичного заказа нет.
    /// </summary>
    private void SellProvincialOutput(ActorState actor, CommandEnvelope command)
    {
        if (!_provincial.TryGetValue(actor.Id, out var state) || state.OutputSold) throw new RuleViolation("ProvincialSupportUsed");
        var direction = StartingSpecializations.Direction(state.IndustryId) ?? throw new RuleViolation("UnknownSpecialization");
        var recipe = _config.Recipes.Single(r => r.Id == direction.RecipeId);
        if (command.Action.ItemId != recipe.OutputItem || command.Action.Quantity != recipe.OutputQuantity) throw new RuleViolation("WrongItem");
        if (!_buildings.TryGetValue(state.SubsidizedConstructionId, out var building) || Demolished(building.Id) ||
            !_enterprises.TryGetValue(building.Id, out var enterprise) || enterprise.Completed < 1)
            throw new RuleViolation("NotAvailable");
        if (_offers.Values.Any(offer => offer.Definition.ItemId == recipe.OutputItem && offer.Remaining > 0 && offer.CreatedTick < Tick && offer.ExpiresAtTick >= Tick))
            throw new RuleViolation("NotAvailable");
        RequireIdle(actor);
        RequireActionLocation(actor, command.Action);
        long price = checked(recipe.OutputQuantity * _items[recipe.OutputItem].BasePriceMinor);
        var tx = _economy.Begin(Tick);
        tx.RemoveFreeStock(actor.Inventory, recipe.OutputItem, recipe.OutputQuantity);
        tx.CreditExternal(_treasury, price, "ProvincialSupportAppropriation");
        tx.Pay(_treasury, actor.Account, price, "ProvincialOutputPurchase");
        tx.Commit();
        _flows[recipe.OutputItem].Exported = checked(_flows[recipe.OutputItem].Exported + recipe.OutputQuantity);
        _provincial[actor.Id] = state with { OutputSold = true };
        Emit("ProvincialOutputPurchased", actor.Id, entity: building.Id, item: recipe.OutputItem, quantity: recipe.OutputQuantity, money: price, command: command.CommandId);
    }

    private void MarkParcel(long actor)
    {
        var state = _provincial[actor];
        _provincial[actor] = Finish(state with { ParcelUsed = state.ParcelReserved });
    }

    private void MarkConstruction(long actor, long constructionId, MaterialCost[] materials)
    {
        var state = _provincial[actor];
        var excluded = new List<MaterialCost>();
        var lines = state.Supply.ToArray();
        foreach (var material in materials)
        {
            int index = Array.FindIndex(lines, line => line.ItemId == material.ItemId);
            if (index < 0) continue;
            long exclude = Math.Min(material.Quantity, lines[index].ConstructionQuantity);
            if (exclude > 0) excluded.Add(new(material.ItemId, exclude));
            long consumed = Math.Min(lines[index].Remaining, material.Quantity);
            lines[index] = lines[index] with { Remaining = lines[index].Remaining - consumed };
        }
        _provincial[actor] = Finish(state with
        {
            ConstructionUsed = state.ConstructionReserved,
            SubsidizedConstructionId = constructionId,
            RecoveryExcluded = excluded.OrderBy(m => m.ItemId, StringComparer.Ordinal).ToArray(),
            Supply = lines
        });
    }

    private void MarkOperating(long actor)
    {
        var state = _provincial[actor];
        _provincial[actor] = Finish(state with { OperatingUsed = state.OperatingReserved });
    }

    private void ConsumeProvincialInput(long actor, string item, long quantity)
    {
        if (!_provincial.TryGetValue(actor, out var state)) return;
        _provincial[actor] = state with
        {
            Supply = state.Supply.Select(line => line.ItemId == item
                ? line with { Remaining = Math.Max(0, checked(line.Remaining - quantity)) } : line).ToArray()
        };
    }

    private long ProvincialSellable(long actor, string item, long requested)
    {
        if (!_provincial.TryGetValue(actor, out var state)) return requested;
        long restricted = state.Supply.Where(line => line.ItemId == item).Sum(line => line.Remaining);
        if (restricted <= 0) return requested;
        long free = _economy.Inventories.Values.Where(inventory => inventory.Owner == actor).Sum(inventory => inventory.Free(item));
        return Math.Min(requested, Math.Max(0, checked(free - restricted)));
    }

    private void RequireProvincialSale(long actor, string item, long quantity)
    {
        if (quantity > ProvincialSellable(actor, item, quantity)) throw new RuleViolation("SubsidizedGoods");
    }

    private void ReleaseProvincialBuilding(long owner, long target)
    {
        if (!_provincial.TryGetValue(owner, out var state) || state.SubsidizedConstructionId != target || state.Status != "Open") return;
        if (state.OperatingUsed == 0) state = state with { OperatingUsed = state.OperatingReserved };
        _provincial[owner] = Finish(state);
    }

    private MaterialCost[] ProvincialRecovery(long owner, long target, MaterialCost[] invested)
    {
        MaterialCost[] Basis(MaterialCost[] source) => source
            .Select(m => new MaterialCost(m.ItemId, checked((long)((Int128)m.Quantity * DemolitionRules.RecoveryBasisPoints / 10000))))
            .Where(m => m.Quantity > 0).ToArray();
        if (!_provincial.TryGetValue(owner, out var state) || state.SubsidizedConstructionId != target || state.RecoveryExcluded.Length == 0)
            return Basis(invested);
        var reduced = invested.Select(m =>
        {
            long drop = state.RecoveryExcluded.Where(e => e.ItemId == m.ItemId).Sum(e => e.Quantity);
            return new MaterialCost(m.ItemId, Math.Max(0, checked(m.Quantity - drop)));
        }).ToArray();
        return Basis(reduced);
    }

    private ProvincialSupportState BuildQuote(long actor, string industry)
    {
        if (EconomyRules.ProvincialSupport is not { Applies: true }) throw new RuleViolation("ProvincialSupportDisabled");
        if (!StartingSpecializations.ChainReady(_config, industry) || !_buildingTypes.TryGetValue(industry, out var type))
            throw new RuleViolation("UnknownSpecialization");
        var direction = StartingSpecializations.Direction(industry)!;
        var level = type.Levels.Single(l => l.Level == 1);
        var recipe = _config.Recipes.Single(r => r.Id == direction.RecipeId);
        long operating = checked(level.StaffCapacity * level.WageMinor * recipe.FullStaffTicks + recipe.EnergyPerBatchMinor);
        var quantities = new Dictionary<string, (long Quantity, long Construction)>(StringComparer.Ordinal);
        void Add(string item, long quantity, bool construction)
        {
            quantities.TryGetValue(item, out var row);
            quantities[item] = (checked(row.Quantity + quantity), checked(row.Construction + (construction ? quantity : 0)));
        }
        foreach (var material in level.Materials) Add(material.ItemId, material.Quantity, true);
        foreach (var ingredient in recipe.Ingredients) Add(ingredient.ItemId, ingredient.Quantity, false);
        var lines = quantities.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
        {
            long unit = ProvincialUnit(pair.Key);
            if (unit <= 0 || pair.Value.Quantity <= 0) throw new RuleViolation("NotAvailable");
            return new ProvincialSupplyLine(pair.Key, pair.Value.Quantity, unit, pair.Value.Quantity, pair.Value.Construction);
        }).ToArray();
        return new(actor, industry, "Open", IndustrialParcelRules.PriceMinor, 0, level.PriceMinor, 0, operating, 0,
            lines.Sum(line => checked(line.Quantity * line.UnitMinor)), 0, false, 0, [], lines);
    }

    private long ProvincialUnit(string item) =>
        Content?.Imports.FirstOrDefault(supply => supply.ItemId == item)?.UnitPriceMinor ?? _items[item].BasePriceMinor;

    private static ProvincialSupportState Finish(ProvincialSupportState state)
    {
        bool done = state.ParcelUsed == state.ParcelReserved && state.ConstructionUsed == state.ConstructionReserved &&
            state.OperatingUsed == state.OperatingReserved && state.SupplyCollected;
        return state with { Status = done ? "Completed" : "Open" };
    }

    private string NextOf(ProvincialSupportState state)
    {
        if (state.ParcelUsed == 0) return "buy-parcel";
        if (!state.SupplyCollected) return "collect-supply";
        if (state.ConstructionUsed == 0) return "build";
        if (state.OperatingUsed == 0) return "wait-construction";
        if (_buildings.TryGetValue(state.SubsidizedConstructionId, out var building) && !Demolished(building.Id) &&
            _enterprises.TryGetValue(building.Id, out var enterprise))
        {
            if (enterprise.Completed == 0) return "produce";
            if (_economy.Inventories.Values.Any(inventory => inventory.Owner == state.ActorId && inventory.Count(enterprise.Recipe.OutputItem) > 0))
                return "sell";
        }
        return "done";
    }

    private static ProvincialProgramView ViewOf(ProvincialSupportState state, string next) => new(
        state.IndustryId, state.Status, state.ParcelReserved, state.ParcelReserved - state.ParcelUsed,
        state.ConstructionReserved, state.ConstructionReserved - state.ConstructionUsed,
        state.OperatingReserved, state.OperatingReserved - state.OperatingUsed,
        state.SupplyValueMinor, state.SupplyCollected, next,
        state.Supply.Where(line => line.Remaining > 0).Select(line => new MaterialCost(line.ItemId, line.Remaining)).ToArray());
}
