using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>
/// Подсказка текущего шага и понятный список нехватки.
/// Только чтение PlayerView и каталога сценария: мир, цены и команды не меняются.
/// </summary>
public sealed record RequirementGap(string Kind, string ItemId, long Have, long Need, string NoteRu = "", string NoteEn = "");

public sealed record StarterStep(
    string Id,
    string TitleRu, string TitleEn,
    string DetailRu, string DetailEn,
    int X, int Z,
    string Focus,
    long Have, long Need,
    string ItemId,
    bool Count,
    string TargetKind,
    long TargetId,
    RequirementGap[] Gaps);

public static class FriendlyGuidance
{
    public const string Simple = "simple";
    public const string Advanced = "advanced";

    public static string Normalize(string? mode) => string.Equals(mode, Advanced, StringComparison.Ordinal) ? Advanced : Simple;

    public static IReadOnlyList<(string Group, string TitleRu, string TitleEn, string[] Keys)> Navigation(bool friendly) => friendly
        ? [
            ("home", "МОЁ ХОЗЯЙСТВО", "MY BUSINESS", ["overview", "holdings", "inventory", "fleet"]),
            ("actions", "ДЕЙСТВИЯ", "ACTIONS", ["quests", "purchases", "roads"]),
            ("more", "ДОПОЛНИТЕЛЬНО", "MORE", ["account", "negotiations", "services", "participants", "city", "map", "notices", "tutorial"])
        ]
        : [
            ("holdings", "ХОЗЯЙСТВО", "HOLDINGS", ["overview", "holdings", "inventory", "fleet", "services", "roads"]),
            ("economy", "ЭКОНОМИКА", "ECONOMY", ["account", "quests", "negotiations", "purchases"]),
            ("city", "ГОРОД", "CITY", ["participants", "city"]),
            ("information", "ИНФОРМАЦИЯ", "INFORMATION", ["map", "notices", "tutorial"])
        ];

    public static int Rank(ContextChoice choice)
    {
        if (choice.Command?.Kind is ActionKind.StopHarvest or ActionKind.StopTransfer) return 0;
        if (choice.Key.StartsWith("machine:stop:", StringComparison.Ordinal)) return 1;
        if (choice.Command?.Kind is ActionKind.StartHarvesting or ActionKind.Harvest) return 2;
        if (choice.Command?.Kind is ActionKind.BuyPlot) return 3;
        if (choice.Key == "exchange") return 4;
        if (choice.Key.StartsWith("market:", StringComparison.Ordinal)) return 5;
        if (choice.Key.StartsWith("object:equipment:", StringComparison.Ordinal) || choice.Command?.Kind is ActionKind.BoardVehicle or ActionKind.LeaveVehicle) return 6;
        if (choice.Key.StartsWith("local-transfer:", StringComparison.Ordinal)) return 7;
        if (choice.Key.StartsWith("card:", StringComparison.Ordinal) || choice.Key.StartsWith("object:", StringComparison.Ordinal) || choice.Key.StartsWith("road-work:", StringComparison.Ordinal)) return 8;
        if (choice.Command?.Kind is ActionKind.BuyVehicle) return 9;
        return 80;
    }

    public static bool ShowDisabled(ContextChoice choice) => Rank(choice) <= 4;

    public static string? Caption(ContextChoice choice, string language)
    {
        string L(string ru, string en) => PlayerText.Local(language, ru, en);
        if (choice.Key == "exchange") return L("Продать товары", "Sell goods");
        if (choice.Key == "market:0") return L("Посмотреть заказы", "View orders");
        if (choice.Key.StartsWith("object:equipment:", StringComparison.Ordinal)) return L("Управлять", "Control");
        if (choice.Command?.Kind is ActionKind.StopHarvest) return L("Остановить добычу", "Stop harvesting");
        if (choice.Command?.Kind is ActionKind.StartHarvesting or ActionKind.Harvest)
            return L("Добыть ", "Harvest ") + PlayerText.Item(language, choice.Command.ItemId);
        if (choice.Key.StartsWith("resource:info:", StringComparison.Ordinal)) return L("Посмотреть ресурс", "View resource");
        return null;
    }

    public static StarterStep Read(PlayerView view, ScenarioDefinition scenario, string language)
    {
        var me = view.Participant;
        var pack = view.Inventories.Single(i => i.Id == me.InventoryId);
        var produced = view.Buildings.Where(b => b.OwnerId == me.Id && b.Kind == BuildingKind.Production).OrderBy(b => b.Id.Value).ToArray();
        if (produced.Length > 0) return Production(view, scenario, language, produced, pack);
        var owned = view.Plots.Where(p => p.OwnerId == me.Id && p.BuildingId is null && !p.IsCorridor && !p.IsShopSite && p.ShopParticipantId is null && p.ExpansionFor is null)
            .OrderBy(p => Distance(me.Position, p.Position)).ThenBy(p => p.Id.Value).FirstOrDefault();
        if (owned is not null) return Build(view, scenario, language, owned, pack);
        var goods = Sellable(view, pack).FirstOrDefault();
        if (goods.Offer is not null) return Sell(language, goods.Item, goods.Have, goods.Offer, "sell");
        var held = pack.Stock.Where(s => s.Quantity > s.Reserved).OrderByDescending(s => s.Quantity - s.Reserved).ThenBy(s => s.ItemId, StringComparer.Ordinal).FirstOrDefault();
        if (held is not null) return WaitingOffer(view, scenario, language, held.ItemId, held.Quantity - held.Reserved);
        if (view.Harvest is { } job) return Harvesting(language, job, Free(pack, job.ItemId), me.Position);
        return Find(view, scenario, language, pack);
    }

    public static string Attention(PlayerView view, string language)
    {
        string L(string ru, string en) => PlayerText.Local(language, ru, en);
        var lines = new List<string>();
        if (view.PersonalAccount is { DebtMinor: > 0 } account)
            lines.Add(L("Долг: ", "Debt: ") + PlayerText.Money(account.DebtMinor));
        var pack = view.Inventories.Single(i => i.Id == view.Participant.InventoryId);
        if (pack.Capacity > 0 && pack.OccupiedVolume + pack.ReservedCapacity >= pack.Capacity)
            lines.Add(L("Рюкзак заполнен.", "Backpack is full."));
        foreach (var enterprise in view.Enterprises.OrderBy(e => e.Id.Value))
        {
            if (enterprise.Enabled && enterprise.ReasonCode is "" or "Ready" or "Working") continue;
            var building = view.Buildings.FirstOrDefault(b => b.Id.Value == enterprise.Id.Value);
            string name = building is null ? "#" + enterprise.Id.Value : PlayerText.Building(language, building.TypeId);
            lines.Add(name + ": " + Reason(language, enterprise.ReasonCode.Length == 0 ? "UserPaused" : enterprise.ReasonCode));
        }
        int overflow = view.Inventories.Sum(i => i.Overflow?.Length ?? 0);
        if (overflow > 0) lines.Add(L("Партий под риском потери: ", "Lots at risk: ") + overflow);
        return lines.Count == 0 ? L("Срочных проблем нет.", "No urgent problems.") : string.Join("\n", lines);
    }

    public static string ConstructionText(PlayerView view, ScenarioDefinition scenario, BuildingTypeDefinition type, BuildingLevelDefinition level, InventoryView materials, string language)
    {
        string L(string ru, string en) => PlayerText.Local(language, ru, en);
        var gaps = new List<RequirementGap>
        {
            new("money", "", view.AvailableCashMinor, level.PriceMinor)
        };
        foreach (var cost in level.Materials)
            gaps.Add(new("material", cost.ItemId, Free(materials, cost.ItemId), cost.Quantity));
        var cell = materials.Position;
        var plot = view.Plots.FirstOrDefault(p => p.Position == cell);
        if (type.Kind == BuildingKind.Production && view.Development is { } development && !development.BuildAnchors.IsDefaultOrEmpty && !development.BuildAnchors.Contains(cell))
            gaps.Add(new("note", "", 0, 0, Reason(language, "BuildAtContourAnchor"), Reason(language, "BuildAtContourAnchor")));
        var offsets = MapLots.Offsets(type, level.Level);
        var lot = scenario.Layout?.IndustrialLots.FirstOrDefault(l => l.Cells.Contains(cell));
        if (lot is not null && !MapLots.Covers(lot.Cells, cell, offsets))
        {
            string note = MapLots.TooSmall(language, PlayerText.Building(language, type.Id), offsets.Length, lot.Cells.Length);
            gaps.Add(new("note", "", 0, 0, note, note));
        }
        bool ready = gaps.All(g => g.Kind == "note" ? false : g.Have >= g.Need) && gaps.All(g => g.Kind != "note");
        string prefix = L("Цена каталога. Игра проверяет команду отдельно.", "Catalog price. The game still checks the command.");
        if (ready) prefix = L("По каталогу денег и материалов хватает.", "Catalog money and materials are enough.") + " " + prefix;
        if (plot?.OwnerId != view.Participant.Id)
            prefix = L("Нужен свой участок.", "You need your own plot.") + " " + prefix;
        return prefix + "\n" + Describe(gaps, language);
    }

    public static string Describe(IReadOnlyList<RequirementGap> gaps, string language)
    {
        string L(string ru, string en) => PlayerText.Local(language, ru, en);
        var lines = new List<string>();
        foreach (var gap in gaps)
        {
            long missing = Math.Max(0, gap.Need - gap.Have);
            lines.Add(gap.Kind switch
            {
                "money" when gap.ItemId == "energy" => L("Энергия партии по рецепту: свободно ", "Recipe batch energy: available ") + PlayerText.Money(gap.Have) + L(", нужно ", ", need ") + PlayerText.Money(gap.Need) + (missing > 0 ? L(", не хватает ", ", short ") + PlayerText.Money(missing) : ""),
                "money" => L("Деньги: свободно ", "Money: available ") + PlayerText.Money(gap.Have) + L(", нужно ", ", need ") + PlayerText.Money(gap.Need) + (missing > 0 ? L(", не хватает ", ", short ") + PlayerText.Money(missing) : ""),
                "material" => PlayerText.Item(language, gap.ItemId) + L(": есть ", ": have ") + gap.Have + L(", нужно ", ", need ") + gap.Need + (missing > 0 ? L(", не хватает ", ", short ") + missing : ""),
                "staff" => L("Работники: ", "Workers: ") + gap.Have + L(" из ", " of ") + gap.Need,
                "space" => L(gap.NoteRu, gap.NoteEn),
                _ => L(gap.NoteRu, gap.NoteEn)
            });
        }
        return string.Join("\n", lines.Where(l => l.Length > 0));
    }

    private static StarterStep Production(PlayerView view, ScenarioDefinition scenario, string language, BuildingView[] buildings, InventoryView pack)
    {
        string L(string ru, string en) => PlayerText.Local(language, ru, en);
        BuildingView building = buildings.FirstOrDefault(b => view.Enterprises.Any(e => e.Id.Value == b.Id.Value && (!e.Enabled || e.ReasonCode is not ("" or "Ready" or "Working")))) ?? buildings[0];
        var enterprise = view.Enterprises.FirstOrDefault(e => e.Id.Value == building.Id.Value);
        var recipe = enterprise is null ? null : scenario.Recipes.FirstOrDefault(r => r.Id == enterprise.RecipeId);
        var output = enterprise is null ? null : view.Inventories.FirstOrDefault(i => i.Id == enterprise.OutputId);
        if (recipe is not null && output is not null)
        {
            long have = Free(output, recipe.OutputItem);
            var offer = Live(view, recipe.OutputItem).OrderBy(o => Distance(view.Participant.Position, o.Position)).ThenBy(o => o.Id).FirstOrDefault();
            if (have > 0 && offer is not null) return Sell(language, recipe.OutputItem, have, offer, "sell-output") with { TargetKind = "building", TargetId = building.Id.Value };
        }
        var gaps = new List<RequirementGap>();
        if (recipe is not null && enterprise is not null)
        {
            var input = view.Inventories.FirstOrDefault(i => i.Id == enterprise.InputId) ?? pack;
            foreach (var ingredient in recipe.Ingredients.Where(i => i.ItemId.Length > 0 && i.Quantity > 0))
                gaps.Add(new("material", ingredient.ItemId, Free(input, ingredient.ItemId), ingredient.Quantity));
            if (recipe.EnergyPerBatchMinor > 0) gaps.Add(new("money", "energy", view.AvailableCashMinor, recipe.EnergyPerBatchMinor));
            if (enterprise.Staff < recipe.MinimumStaff) gaps.Add(new("staff", "", enterprise.Staff, recipe.MinimumStaff));
            var outInv = view.Inventories.FirstOrDefault(i => i.Id == enterprise.OutputId);
            if (outInv is not null && outInv.Capacity > 0 && outInv.OccupiedVolume + outInv.ReservedCapacity >= outInv.Capacity)
                gaps.Add(new("space", "", outInv.OccupiedVolume, outInv.Capacity, L("На складе продукции нет места.", "Finished-goods storage is full."), L("На складе продукции нет места.", "Finished-goods storage is full.")));
        }
        string reason = enterprise is null ? "" : Reason(language, enterprise.ReasonCode);
        if (enterprise is { BatchId: > 0, WorkRequired: > 0 })
            return Step("running", L("Партия уже идёт", "A batch is running"), reason, building.Position.X, building.Position.Z, "production", enterprise.WorkDone, enterprise.WorkRequired, "", true, "building", building.Id.Value, [..gaps]);
        if (enterprise is { Enabled: false } || (enterprise is not null && enterprise.ReasonCode is not ("" or "Ready" or "Working")))
            return Step("produce", L("Запустите производство", "Start production"), reason.Length == 0 ? L("Откройте предприятие и включите работу.", "Open the enterprise and start work.") : reason, building.Position.X, building.Position.Z, "production", 0, 1, "", false, "building", building.Id.Value, [..gaps]);
        if (enterprise is { CompletedBatches: 0 })
            return Step("produce", L("Запустите первую партию", "Start the first batch"), L("Сырьё, деньги и место берутся из текущего предприятия.", "Inputs, money and space come from this enterprise."), building.Position.X, building.Position.Z, "production", 0, 1, "", false, "building", building.Id.Value, [..gaps]);
        return Step("steady", L("Хозяйство работает", "The business is running"), L("Новая цель появится, когда производство остановится или появится продукция.", "A new goal appears when production stops or goods are ready."), building.Position.X, building.Position.Z, "production", enterprise?.CompletedBatches ?? 0, 0, "", false, "building", building.Id.Value, [..gaps]);
    }

    private static StarterStep Build(PlayerView view, ScenarioDefinition scenario, string language, PlotView plot, InventoryView pack)
    {
        string L(string ru, string en) => PlayerText.Local(language, ru, en);
        var type = (scenario.BuildingTypes ?? []).Where(t => t.Kind == BuildingKind.Production).OrderBy(t => Level(t).PriceMinor).ThenBy(t => t.Id, StringComparer.Ordinal).FirstOrDefault();
        var materials = MaterialsOf(view, plot, pack);
        var level = type is null ? null : Level(type);
        var gaps = new List<RequirementGap>();
        if (level is not null)
        {
            gaps.Add(new("money", "", view.AvailableCashMinor, level.PriceMinor));
            foreach (var cost in level.Materials) gaps.Add(new("material", cost.ItemId, Free(materials, cost.ItemId), cost.Quantity));
        }
        string name = type is null ? L("предприятие", "an enterprise") : PlayerText.Building(language, type.Id);
        string detail = type is null
            ? L("В каталоге этой партии нет предприятия.", "This game has no enterprise in the catalog.")
            : L("Каталог «", "Catalog \"") + name + L("», уровень 1. Это цена здания, не рыночная оценка материалов. Проверка клетки остаётся за игрой.", "\", level 1. This is the building price, not a market estimate of the materials. The game still checks the cell.");
        bool ready = level is not null && gaps.All(g => g.Have >= g.Need);
        return Step(ready ? "build" : "materials", ready ? L("Постройте ", "Build ") + name : L("Соберите материалы для ", "Gather materials for ") + name, detail, plot.Position.X, plot.Position.Z, "property", 0, 0, type?.Id ?? "", false, "ground", plot.Id.Value, [..gaps]);
    }

    private static StarterStep Find(PlayerView view, ScenarioDefinition scenario, string language, InventoryView pack)
    {
        string L(string ru, string en) => PlayerText.Local(language, ru, en);
        var site = view.Resources.Where(r => r.Public && r.Stock > r.Reserved).OrderBy(r => Distance(view.Participant.Position, r.Position)).ThenBy(r => r.ItemId, StringComparer.Ordinal).FirstOrDefault();
        var plot = Cheapest(view);
        string money = plot is null ? "" : " " + L("Свободно ", "Available ") + PlayerText.Money(view.AvailableCashMinor) + L(". Участок от ", ". A plot costs from ") + PlayerText.Money(plot.PriceMinor) + ".";
        if (site is null)
            return Step("find", L("Найдите доступный ресурс", "Find an available resource"), L("Сейчас нет видимого общего ресурса с запасом.", "No visible public resource currently has stock.") + money, -1, -1, "resources", 0, 1, "", true, "", 0, []);
        long have = Free(pack, site.ItemId);
        return Step("find", L("Добудьте ", "Harvest ") + PlayerText.Item(language, site.ItemId), L("Общий ресурс на карте. Первая единица появится в рюкзаке только после завершения цикла.", "A public resource on the map. The first unit reaches your backpack only when the cycle finishes.") + money, site.Position.X, site.Position.Z, "resources", have, 1, site.ItemId, true, "", 0, []);
    }

    private static StarterStep WaitingOffer(PlayerView view, ScenarioDefinition scenario, string language, string item, long have)
    {
        string L(string ru, string en) => PlayerText.Local(language, ru, en);
        var terminal = scenario.Layout?.CivicObjects.FirstOrDefault(c => c.Kind == "terminal")?.Entry ?? scenario.CapitalDemand?.Terminal ?? view.Participant.Position;
        bool known = scenario.Layout?.CivicObjects.Any(c => c.Kind == "terminal") == true || scenario.CapitalDemand?.Terminal is not null;
        return Step("wait-order", L("Продайте ", "Sell ") + PlayerText.Item(language, item), L("В рюкзаке есть товар, но действующего заказа столицы сейчас нет. Цель не отмечается выполненной, пока заказ не появится.", "The backpack holds goods, but there is no live capital order. The goal stays open until an order exists."), known ? terminal.X : -1, known ? terminal.Z : -1, "orders", have, 0, item, false, "", 0, []);
    }

    private static StarterStep Harvesting(string language, OwnHarvest job, long have, Cell position)
    {
        string L(string ru, string en) => PlayerText.Local(language, ru, en);
        return Step("harvest", L("Добыча: ", "Harvesting: ") + PlayerText.Item(language, job.ItemId), L("Цикл ещё не завершён. Остановка не засчитывает товар.", "The cycle is not finished. Stopping does not grant the goods."), position.X, position.Z, "resources", have, Math.Max(1, job.Quantity), job.ItemId, true, "", 0, []);
    }

    private static StarterStep Sell(string language, string item, long have, CapitalOfferView offer, string id)
    {
        string L(string ru, string en) => PlayerText.Local(language, ru, en);
        return Step(id, L("Продайте ", "Sell ") + PlayerText.Item(language, item), L("Заказ столицы: ", "Capital order: ") + PlayerText.Money(offer.UnitPriceMinor) + L(" за единицу. Это действующая цена заказа, не новая формула.", " per unit. This is the live order price, not a new formula."), offer.Position.X, offer.Position.Z, "orders", have, offer.RemainingQuantity, item, true, "", offer.Id, []);
    }

    private static StarterStep Step(string id, string title, string detail, int x, int z, string focus, long have, long need, string item, bool count, string kind, long target, RequirementGap[] gaps) =>
        new(id, title, title, detail, detail, x, z, focus, have, need, item, count, kind, target, gaps);

    private static IEnumerable<CapitalOfferView> Live(PlayerView view, string item) => view.CapitalOffers.Where(o => o.ItemId == item && o.RemainingQuantity > 0 && (o.ExpiresAtTick is null || o.ExpiresAtTick >= view.Tick));

    private static IEnumerable<(string Item, long Have, CapitalOfferView Offer)> Sellable(PlayerView view, InventoryView pack) =>
        pack.Stock.Where(s => s.Quantity > s.Reserved).Select(s => (s.ItemId, s.Quantity - s.Reserved, Live(view, s.ItemId).OrderBy(o => Distance(view.Participant.Position, o.Position)).ThenBy(o => o.Id).FirstOrDefault()))
            .Where(s => s.Item3 is not null).Select(s => (s.ItemId, s.Item2, s.Item3!)).OrderByDescending(s => s.Item2).ThenBy(s => s.ItemId, StringComparer.Ordinal);

    private static PlotView? Cheapest(PlayerView view) => view.Plots.Where(p => p.OwnerId is null && p.ReservedBy is null && !p.IsCorridor && !p.IsShopSite && p.ShopParticipantId is null && p.ExpansionFor is null)
        .OrderBy(p => p.PriceMinor).ThenBy(p => p.Id.Value).FirstOrDefault();

    private static InventoryView MaterialsOf(PlayerView view, PlotView plot, InventoryView pack)
    {
        var ground = view.Storage?.Ground.FirstOrDefault(g => g.PlotId == plot.Id.Value);
        return ground is null ? pack : view.Inventories.FirstOrDefault(i => i.Id.Value == ground.InventoryId) ?? pack;
    }

    private static BuildingLevelDefinition Level(BuildingTypeDefinition type) => type.Levels.Single(l => l.Level == 1);

    private static long Free(InventoryView inventory, string item) => inventory.Stock.Where(s => s.ItemId == item).Sum(s => s.Quantity - s.Reserved);

    private static int Distance(Cell a, Cell b) => Math.Abs(a.X - b.X) + Math.Abs(a.Z - b.Z);

    private static string Reason(string language, string code)
    {
        string tablet = TabletText.Reason(language, code);
        if (tablet != code) return tablet;
        return PlayerText.Reason(language, code);
    }
}
