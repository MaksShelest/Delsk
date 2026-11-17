using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;

// Controlled economic comparison. Other participants vote and otherwise stay idle.
internal sealed class I6C8DEconomyStudy(ScenarioDefinition scenario, StartingDirection direction, long horizon)
{
    private static int ExportProfiles(string[] args)
    {
        if (args.Length != 3)
            throw new ArgumentException("Usage: --export-i6c8-profiles MOBILE_PROFILE NEW_OUTPUT_DIRECTORY");
        string output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new ArgumentException("Output directory must be empty");
        var basis = VNextProvincialSupportProfile.ForOrdinaryMap(EconomyProfile.Load(args[1]));
        var candidate = VNextI6C8DBalanceProfile.Create(basis);
        Directory.CreateDirectory(output);
        foreach (var (name, profile) in new[] { ("i6c8c", basis), ("i6c8d", candidate) })
            File.WriteAllBytes(Path.Combine(output, name + ".json"), VNextSupportedProfile.Serialize(profile));
        return 0;
    }

    internal static int Run(string[] args)
    {
        try
        {
            if (args[0] == "--export-i6c8-profiles") return ExportProfiles(args);
            if (args.Length != 7 || StartingSpecializations.Direction(args[3]) is not { } direction ||
                !ulong.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed) ||
                args[5] is not ("raw" or "production") ||
                !long.TryParse(args[6], NumberStyles.None, CultureInfo.InvariantCulture, out long horizon) ||
                horizon is < 1 or > 36_000)
                throw new ArgumentException("Usage: --study-i6c8d SCENARIO PROFILE INDUSTRY SEED raw|production TICKS (1..36000)");
            var economy = EconomyProfile.Load(args[2]);
            var scenario = RunArtifacts.LoadScenario(args[1], economy);
            var balance = VNextBalanceReader.Load(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[2]))!, "balance.b1.json"));
            if (!balance.IsValid || balance.Sha256 != economy.VNext?.FullBalanceSha256)
                throw new ArgumentException("Full balance/profile mismatch");
            var resolved = OrdinaryNewGame.Resolve(scenario, economy, new(3, Seed: seed, StartingIndustry: direction.IndustryId), vnextBalance: balance.Balance);
            return new I6C8DEconomyStudy(resolved.Scenario, direction, horizon).Run(args[5]);
        }
        catch (Exception e) when (e is ArgumentException or IOException or InvalidOperationException or JsonException)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }
    }

    private readonly GameSession session = new(new(scenario));
    private readonly Stopwatch timer = Stopwatch.StartNew();
    private readonly List<object> samples = [];
    private readonly Dictionary<string, long> reasons = new();
    private readonly ParticipantId actor = new(1);
    private long? startupFinished;
    private string status = "complete";
    private int trips;
    private long lastSample = -1;
    private static int Distance(Cell a, Cell b) => Math.Abs(a.X-b.X) + Math.Abs(a.Z-b.Z);
    private SimulationEngine Engine => session.Engine;
    private ParticipantObservation View => Engine.Observe(actor);
    private Cell Terminal => scenario.CapitalDemand!.Terminal;
    private long UnitVolume(string item) => scenario.Items.Single(i => i.Id == item).UnitVolume;
    private long ItemPrice(string item) => scenario.Items.Single(i => i.Id == item).BasePriceMinor;
    private RecipeDefinition Recipe => scenario.Recipes.Single(r => r.Id == direction.RecipeId);
    private InventoryId Carrier => View.Participant.InventoryId;

    private int Run(string strategy)
    {
        session.AttachControllers(scenario.Participants.Select(p => (IController)new SelfBallotController(new(p.Id))).ToArray());
        session.TickCompleted += _ =>
        {
            if (Engine.Tick % 120 == 0) Sample();
        };
        Sample();
        try
        {
            if (strategy == "production") Produce();
            else SellRaw();
        }
        catch (StudyLimitException) { }
        catch (InvalidOperationException e) { status = e.Message; }
        Sample();
        var view = View;
        var state = Engine.Inspect();
        var events = Engine.EventsSince(0).Where(e => e.ActorId == actor).ToArray();
        var cash = view.Account.Id;
        var ledger = Engine.Ledger();
        var ownLedger = ledger.Where(l => l.CreditAccount == cash || l.DebitAccount == cash).ToArray();
        object Totals(IEnumerable<LedgerRecord> records) => records.GroupBy(l => l.Reason).ToDictionary(g => g.Key, g => g.Sum(l => l.CreditAccount == cash ? l.AmountMinor : -l.AmountMinor));
        var marketPeriods = state.CapitalDemand!.Periods
            .Select(p => p.Goods.Single(g => g.ItemId == direction.OutputItem).DemandQuantity).ToArray();
        var first = events.FirstOrDefault(e => IsSale(e.Type) && e.AmountMinor > 0);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            profile = scenario.EconomyProfileId, scenario = scenario.Id, scenario.Seed, direction.IndustryId, strategy, status,
            tick = state.Tick, firstSale = first?.Tick, firstSaleKind = first?.Type, firstSaleMinor = first?.AmountMinor, startupFinished,
            cash = view.Account, trips, commands = session.CommandTape.Length, reasons, totals = Totals(ownLedger),
            laterTotals = Totals(ownLedger.Where(l => startupFinished is null || l.Tick > startupFinished)),
            harvested = events.Where(e => e.Type == "HarvestCompleted").Sum(e => e.Quantity),
            produced = events.Where(e => e.Type == "ProductionCompleted").Sum(e => e.Quantity),
            ingredients = events.Where(e => e.Type == "ProductionBatchStarted").Sum(e => e.Quantity),
            sold = events.Where(e => IsSale(e.Type)).Sum(e => e.Quantity),
            settlement = state.Settlements.Where(s => s.Seller == actor).GroupBy(s => s.ItemId).ToDictionary(g => g.Key, g => new { units = g.Sum(s => s.Accepted), received = g.Sum(s => s.PaidMinor) }),
            workedTicks = events.Where(e => e.Type == "WagesPaid").Select(e => e.Tick).Distinct().Count(),
            orderPeriods = marketPeriods.Length, outputOrderUnits = marketPeriods.Sum(), periodsWithOutputOrders = marketPeriods.Count(n => n > 0),
            stateHash = Engine.ComputeStateHash(), seconds = timer.Elapsed.TotalSeconds, samples
        }, SimulationJson.Options));
        return status == "complete" ? 0 : 1;
    }

    private static bool IsSale(string type) => type is "CapitalSaleSettled" or "ProvincialOutputPurchased";
    private void Sample()
    {
        if (lastSample == Engine.Tick) return;
        lastSample = Engine.Tick;
        var state = Engine.Inspect();
        var person = state.Participants.Single(p => p.Id == actor);
        var cash = state.Accounts.Single(a => a.Id == person.AccountId);
        var capital = Engine.PublicEconomy().Participants.Single(p => p.Actor == actor).CapitalMinor;
        var enterprises = state.Enterprises.Where(e => e.OwnerId == actor).ToArray();
        samples.Add(new { tick = Engine.Tick, cash.BalanceMinor, cash.AvailableMinor, capital,
            completed = enterprises.Sum(e => e.CompletedBatches), enterprise = enterprises.Select(e => new { e.Level,e.Staff,e.ReasonCode }),
            xp = Engine.DevelopmentOf(actor)?.Experience.Where(x => x.Industry == direction.IndustryId) });
    }

    private void Step()
    {
        if (Engine.Tick >= horizon) throw new StudyLimitException();
        session.Step();
    }

    private void WaitUntil(Func<bool> condition)
    {
        while (!condition()) Step();
    }

    private void Idle() => WaitUntil(() => !View.Participant.Busy);
    private string Send(GameAction action, bool required = true)
    {
        if (Engine.Tick >= horizon) throw new StudyLimitException();
        if (Engine.WaitingForBallots && !session.PumpVoting())
            throw new InvalidOperationException("ElectionWaitingForBallots");
        var id = session.Send(actor, action);
        WaitUntil(() => Engine.Result(id) is not null);
        var result = Engine.Result(id)!;
        if (result.Status == ResultStatus.Rejected)
        {
            reasons[result.ReasonCode] = reasons.GetValueOrDefault(result.ReasonCode) + 1;
            if (required) throw new InvalidOperationException($"{action.Kind}/{result.ReasonCode} at {Engine.Tick}");
        }
        return result.ReasonCode;
    }

    private void Move(Cell cell)
    {
        if (View.Participant.Position == cell) return;
        Send(new(ActionKind.MoveTo, Target: cell));
        Idle();
        if (View.Participant.Position != cell) throw new InvalidOperationException($"Did not reach {cell}");
        trips++;
    }

    private void Transfer(InventoryId source, InventoryId destination, string item, long quantity)
    {
        Send(new(ActionKind.Transfer, ItemId: item, Quantity: quantity, Source: source, Destination: destination));
        Idle();
    }

    private long Stock(InventoryId inventory, string item) =>
        View.OwnInventories.Single(i => i.Id == inventory).Stock.Where(s => s.ItemId == item).Sum(s => s.Quantity - s.Reserved);

    private void PackSupply(InventoryId supply, InventoryId ground, Cell anchor)
    {
        long capacity = scenario.Participants[0].InventoryCapacity;
        var remaining = View.OwnInventories.Single(i => i.Id == supply).Stock.ToDictionary(s => s.ItemId, s => s.Quantity);
        while (remaining.Values.Any(n => n > 0))
        {
            long free = capacity;
            var loaded = new List<(string Item,long Count)>();
            Move(Terminal);
            foreach (var item in remaining.Keys.ToArray())
            {
                long count = Math.Min(remaining[item], free / UnitVolume(item));
                if (count == 0) continue;
                Transfer(supply, Carrier, item, count);
                remaining[item] -= count;
                free -= count * UnitVolume(item);
                loaded.Add((item,count));
            }
            if (loaded.Count == 0) throw new InvalidOperationException("Supply does not fit carrier");
            Move(anchor);
            foreach (var (item,count) in loaded) Transfer(Carrier, ground, item, count);
        }
    }

    private void Produce()
    {
        var lot = scenario.Layout!.IndustrialLots.Select(l => IndustrialParcels.TryFrame(l)!.Value)
            .OrderBy(l => Distance(l.Anchor, Terminal)).First();
        var anchor = lot.Anchor;
        var plot = scenario.Plots!.Single(p => p.Position == anchor);
        Send(new(ActionKind.AcceptProvincialSupport));
        Move(anchor);
        Send(new(ActionKind.BuyPlot, Plot: new(plot.Id)));
        Move(Terminal);
        Send(new(ActionKind.CollectProvincialSupply));
        var supply = View.OwnInventories.Single(i => i.Position == Terminal && i.Stock.Any(s => s.ItemId == "steel")).Id;
        var ground = new InventoryId(Engine.StorageOf(actor).Ground.Single(g => g.PlotId == plot.Id).InventoryId);
        PackSupply(supply, ground, anchor);
        Send(new(ActionKind.Build, BuildingTypeId: direction.IndustryId, Target: anchor));
        WaitUntil(() => View.OwnEnterprises.Length > 0);
        var enterprise = View.OwnEnterprises.Single();
        if (enterprise.RecipeId != Recipe.Id) Send(new(ActionKind.SetRecipe, Enterprise: enterprise.Id, RecipeId: Recipe.Id));
        foreach (var i in Recipe.Ingredients)
        {
            Transfer(ground, Carrier, i.ItemId, i.Quantity);
            Transfer(Carrier, enterprise.InputId, i.ItemId, i.Quantity);
        }
        Send(new(ActionKind.SetProductionEnabled, Enterprise: enterprise.Id, Enabled: true));
        WaitUntil(() => Stock(enterprise.OutputId, Recipe.OutputItem) >= Recipe.OutputQuantity);
        Transfer(enterprise.OutputId, Carrier, Recipe.OutputItem, Recipe.OutputQuantity);
        Move(Terminal);
        Sell(Recipe.OutputItem, true);
        startupFinished = Engine.Tick;
        while (true)
        {
            var ingredient = Recipe.Ingredients.Single();
            long capacity = scenario.Participants[0].InventoryCapacity;
            long batches = Math.Min(capacity / UnitVolume(ingredient.ItemId) / ingredient.Quantity,
                capacity / UnitVolume(Recipe.OutputItem) / Recipe.OutputQuantity);
            long input = batches * ingredient.Quantity;
            long output = batches * Recipe.OutputQuantity;
            Gather(ingredient.ItemId, input, anchor);
            Move(anchor);
            Transfer(Carrier, enterprise.InputId, ingredient.ItemId, input);
            WaitUntil(() => Stock(enterprise.OutputId, Recipe.OutputItem) >= output);
            Transfer(enterprise.OutputId, Carrier, Recipe.OutputItem, output);
            Move(Terminal);
            Sell(Recipe.OutputItem, false);
        }
    }

    private void SellRaw()
    {
        while (true)
        {
            var item = direction.InputItem;
            Gather(item, scenario.Participants[0].InventoryCapacity / UnitVolume(item), Terminal);
            Move(Terminal);
            Sell(item, false);
        }
    }

    private void Gather(string item, long amount, Cell destination)
    {
        while (amount > 0)
        {
            var view = View;
            var here = view.Participant.Position;
            var node = view.Resources.Where(n => n.ItemId == item && n.Stock > n.Reserved && (n.Owner is null || n.Owner == actor))
                .OrderBy(n => Distance(here, n.Position) + Distance(n.Position, destination)).FirstOrDefault()
                ?? throw new InvalidOperationException($"No available source of {item} at tick {Engine.Tick}");
            Move(node.Position);
            long quantity = Math.Min(amount, node.Stock - node.Reserved);
            if (Send(new(ActionKind.Harvest, Target: node.Position, ItemId: item, Quantity: quantity), false) != "Accepted") continue;
            Idle();
            amount -= quantity;
        }
    }

    private void Sell(string item, bool provincial)
    {
        while (Stock(Carrier, item) > 0)
        {
            var orders = View.CapitalOffers.Where(o => o.ItemId == item && o.RemainingQuantity > 0 && o.CreatedTick < Engine.Tick && o.ExpiresAtTick > Engine.Tick + 1)
                .OrderByDescending(o => o.UnitPriceMinor).ThenByDescending(o => o.RemainingQuantity).ToArray();
            if (orders.Length == 0)
            {
                if (provincial && Stock(Carrier, item) == Recipe.OutputQuantity &&
                    Send(new(ActionKind.SellProvincialOutput, ItemId: item, Quantity: Recipe.OutputQuantity), false) == "Accepted") return;
                // Wait for the next price/order publication.
                long next = Engine.Tick - Engine.Tick % scenario.CapitalDemand!.PeriodTicks + scenario.CapitalDemand.PeriodTicks + 1;
                WaitUntil(() => Engine.Tick >= next);
                continue;
            }
            var order = orders[0];
            long quantity = Math.Min(Stock(Carrier,item), order.RemainingQuantity);
            if (Send(new(ActionKind.SellAvailableToCapital, ItemId: item, Quantity: quantity, OfferId: order.Id), false) == "Accepted") Idle();
        }
    }

    private sealed class StudyLimitException : Exception;
}
