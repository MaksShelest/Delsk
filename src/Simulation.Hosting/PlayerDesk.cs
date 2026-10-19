using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>
/// Команды и проекция человека. Деньги меняет только ядро после принятой команды.
/// Чужая поддержка сюда не попадает: источник — Observe, не InspectSupport.
/// </summary>
public static class PlayerDesk
{
    public const long WageStepMinor = 5;
    // SetTravelHeading uses the existing authenticated travel-input path, not the command inbox.
    public static readonly ActionKind[] Allowed = Enum.GetValues<ActionKind>().Where(k => k != ActionKind.SetTravelHeading).ToArray();

    public static GameAction Check(GameAction action)
    {
        if (action is null || !Allowed.Contains(action.Kind))
            throw new ArgumentException("Player input cannot submit this command");
        return action;
    }

    public static GameAction Move(int x, int z) => Check(new(ActionKind.MoveTo, Target: new(x, z)));
    public static GameAction Harvest(string itemId, long quantity)
    {
        if (string.IsNullOrWhiteSpace(itemId) || quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        return Check(new(ActionKind.Harvest, ItemId: itemId, Quantity: quantity));
    }
    public static GameAction StartHarvesting(string itemId) => Check(new(ActionKind.StartHarvesting, ItemId: itemId, Quantity: 1));
    public static GameAction StopHarvest() => Check(new(ActionKind.StopHarvest));
    public static GameAction Transfer(long source, long destination, string itemId, long quantity)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        return Check(new(ActionKind.Transfer, Source: new(source), Destination: new(destination), ItemId: itemId, Quantity: quantity));
    }
    public static GameAction Work(long enterprise, bool enabled)
        => Check(new(ActionKind.SetProductionEnabled, Enterprise: new(enterprise), Enabled: enabled));
    public static GameAction Wage(long group, long wageMinor)
    {
        if (wageMinor < 0) throw new ArgumentOutOfRangeException(nameof(wageMinor));
        return Check(new(ActionKind.SetWage, Group: new(group), WageMinor: wageMinor));
    }
    public static GameAction BuyLogistics(long enterprise) => Check(new(ActionKind.BuyLogistics, Enterprise: new(enterprise)));
    public static GameAction Supplies(long logistics, LogisticsDirection direction, bool enabled)
        => Check(new(ActionKind.SetLogisticsEnabled, Logistics: new(logistics), Direction: direction, Enabled: enabled));
    public static GameAction SendProduct(long logistics, long destination, bool enabled)
        => Check(new(ActionKind.SetLogisticsStorage, Logistics: new(logistics), Destination: new(destination), Enabled: enabled));
    public static GameAction FetchInput(long logistics, long source, bool enabled)
        => Check(new(ActionKind.SetLogisticsInbound, Logistics: new(logistics), Source: new(source), Enabled: enabled));
    public static GameAction AcceptSupport(long proposal) => Check(new(ActionKind.AcceptSupport, SupportProposalId: proposal));
    public static GameAction RejectSupport(long proposal) => Check(new(ActionKind.RejectSupport, SupportProposalId: proposal));
    public static GameAction Vote(long election, long candidate)
        => Check(new(ActionKind.SetVoteChoice, Election: new(election), Candidate: new(candidate)));
    public static GameAction ReservePlot(long plot) => Check(new(ActionKind.ReservePlot, Plot: new(plot)));
    public static GameAction BuyPlot(long plot) => Check(new(ActionKind.BuyPlot, Plot: new(plot)));
    public static GameAction Build(Cell cell, string type) => Check(new(ActionKind.Build, Target: cell, BuildingTypeId: type));
    public static GameAction Sell(long offer, string item, long quantity) => Check(new(ActionKind.SellAvailableToCapital,
        OfferId: offer, ItemId: item, Quantity: quantity));
    public static GameAction CancelSale(long offer) => Check(new(ActionKind.CancelCapitalSale, OfferId: offer));
    public static GameAction PublishLot(long shop, string item, long quantity, long price) => Check(new(ActionKind.CreateSellOffer,
        Building: new(shop), ItemId: item, Quantity: quantity, UnitPriceMinor: price));
    public static GameAction Bid(long offer, long quantity, long maximum) => Check(new(ActionKind.SubmitBuyRequest,
        MarketOfferId: offer, Quantity: quantity, MaximumPriceMinor: maximum));
    public static GameAction RaiseBid(long offer, int round, long price) => Check(new(ActionKind.RaiseBid,
        MarketOfferId: offer, MarketRound: round, UnitPriceMinor: price));
    public static GameAction WithdrawBid(long offer, int round = 0) => Check(new(ActionKind.WithdrawBid, MarketOfferId: offer, MarketRound: round));
    public static GameAction CollectPurchase(long purchase, long quantity) => Check(new(ActionKind.CollectPurchase,
        PurchaseId: purchase, Quantity: quantity));

    public static PlayerView Read(SimulationEngine engine, ParticipantId actor)
    {
        var seen = engine.Observe(actor);
        return new(seen.Tick, seen.Participant, seen.Account.BalanceMinor, seen.OwnInventories, seen.OwnEnterprises,
            seen.OwnBuildings, seen.OwnEmploymentGroups, seen.OwnLogistics, seen.LaborMarket, seen.Support, seen.Elections, seen.Plots,
            seen.CapitalOffers, seen.Account.AvailableMinor, engine.HarvestOf(actor), engine.EconomyRules,
            new(seen.MarketOffers, seen.OwnMarketBids, seen.OwnMarketAllocations, seen.MarketRules, engine.PublicShops()),
            seen.OwnEquipment, seen.OwnDeliveryContracts, seen.BuyOffers, seen.OwnSupplyRequests, seen.City);
    }
}

public record PlayerView(long Tick, ParticipantView Participant, long CashMinor, InventoryView[] Inventories,
    EnterpriseView[] Enterprises, BuildingView[] Buildings, EmploymentView[] Employment, LogisticsView[] Logistics,
    PublicEmploymentView[] LaborMarket, SupportObservation Support, ElectionObservation Elections, PlotView[] Plots,
    CapitalOfferView[] CapitalOffers, long AvailableCashMinor, OwnHarvest? Harvest = null, EconomicRules? Economy = null, PlayerMarketView? Market = null, EquipmentView[]? Equipment = null, DeliveryContractView[]? Contracts = null,
    BuyOfferView[]? BuyOffers = null, SupplyRequest[]? SupplyRequests = null, CityEconomy? City = null);

public sealed record PlayerMarketView(MarketOfferView[] Offers, MarketBidView[] Bids, MarketAllocationView[] Allocations,
    MarketSettings Rules, PublicShop[] Shops);

public static class PlayerText
{
    private static readonly Dictionary<string, Dictionary<string, string>> Table = new(StringComparer.Ordinal)
    {
        ["ru"] = Ru(),
        ["en"] = En()
    };

    public static IReadOnlyCollection<string> Keys(string language) => Table[language].Keys;
    public static string Get(string language, string key) => Table[language][key];

    private static Dictionary<string, string> Ru() => new(StringComparer.Ordinal)
    {
        ["title"] = "Delsk — партия",
        ["language"] = "EN",
        ["pause"] = "Пуск / пауза",
        ["step"] = "Один тик",
        ["save"] = "Сохранить",
        ["load"] = "Загрузить",
        ["economy"] = "Экономика",
        ["politics"] = "Политика",
        ["tick"] = "Тик",
        ["cash"] = "Деньги",
        ["position"] = "Место",
        ["busy"] = "Занят",
        ["yes"] = "да",
        ["no"] = "нет",
        ["move.north"] = "z−1",
        ["move.south"] = "z+1",
        ["move.east"] = "x+1",
        ["move.west"] = "x−1",
        ["harvest"] = "Добыть",
        ["transfer.input"] = "Положить 1 на вход",
        ["card.work"] = "Работа",
        ["card.supplies"] = "Поставки",
        ["card.wages"] = "Ставки",
        ["work.start"] = "Продолжить работу",
        ["work.stop"] = "Остановить работу",
        ["wage.raise"] = "Повысить ставку",
        ["wage.lower"] = "Понизить ставку",
        ["supplies.output.on"] = "Отправлять продукцию",
        ["supplies.output.off"] = "Остановить отправку",
        ["supplies.inbound.on"] = "Возобновить вход",
        ["supplies.inbound.off"] = "Заморозить вход",
        ["supplies.buy"] = "Купить доставку",
        ["supplies.send"] = "Вести на склад",
        ["supplies.fetch"] = "Везти со склада",
        ["support.title"] = "Свои предложения поддержки",
        ["support.accept"] = "Принять",
        ["support.reject"] = "Отклонить",
        ["support.none"] = "Нет предложений, в которых вы сторона.",
        ["vote.title"] = "Голос",
        ["vote.choose"] = "Голосовать за",
        ["npc.title"] = "Публичная занятость",
        ["feedback.sent"] = "Команда поставлена в очередь ядра.",
        ["no.enterprise"] = "Своего предприятия пока нет. Добыча и ход доступны.",
        ["paused"] = "Пауза",
        ["running"] = "Идёт",
        ["trip"] = "Рейс",
        ["you"] = "Вы",
        ["selected"] = "Выбрано",
        ["scene.hint"] = "WASD — ход. Наведение объясняет объект. Стрелки и Enter — действие на вашей клетке.",
        ["menu.title"] = "Меню",
        ["menu.continue"] = "Продолжить",
        ["menu.debug"] = "Debug-режим",
        ["menu.exit"] = "Выйти из игры",
        ["menu.settings"] = "Настройки",
        ["menu.escape"] = "Escape здесь означает «Продолжить», если окно в фокусе.",
        ["command.selected"] = "Выбрано, ещё не отправлено.",
        ["command.sent"] = "Отправлено, ждёт тик.",
        ["command.running"] = "Выполняется, осталось {0}",
        ["command.done"] = "Завершено.",
        ["command.rejected"] = "Отклонено:",
        ["inventory.title"] = "Личный инвентарь",
        ["inventory.empty"] = "пусто",
        ["inventory.capacity"] = "Место",
        ["received"] = "Получено: {0} ×{1}",
        ["harvest.left"] = "Добыча {0}: осталось {1}",
        ["reason.NoMoney"] = "Недостаточно денег.",
        ["reason.NoStock"] = "Недостаточно свободных материалов.",
        ["reason.LandNotOwned"] = "Сначала купите все клетки участка.",
        ["reason.WrongBuildingSite"] = "Этот тип здания здесь недоступен.",
        ["reason.NoCapacity"] = "Инвентарь заполнен.",
        ["reason.ParticipantBusy"] = "Персонаж занят другим действием.",
        ["reason.NoResourceNode"] = "Подойдите к точке ресурса.",
        ["reason.NotAtLocation"] = "Подойдите к нужному месту.",
        ["reason.NoPath"] = "Сюда пройти нельзя.",
        ["reason.NotAvailable"] = "Сейчас это действие недоступно.",
        ["reason.PlotOwned"] = "Участок уже куплен.",
        ["reason.PlotReserved"] = "Участок забронирован другим участником.",
        ["reason.ProtectedCorridor"] = "Проход нельзя купить или забронировать.",
        ["reason.ReservationLimit"] = "Слишком много действующих броней.",
        ["reason.ShopSiteLimit"] = "У вас уже есть купленное или забронированное место лавки.",
        ["reason.StaleMarketRound"] = "Раунд торгов изменился. Выберите действие ещё раз.",
        ["reason.BidLimitExceeded"] = "Цена выше вашего первоначального лимита заявки.",
        ["reason.RaiseTooSmall"] = "Повышение меньше минимального шага аукциона.",
        ["reason.PurchaseClosed"] = "Выдача закрыта или срок получения истёк.",
        ["money"] = "Деньги",
        ["item.wood"] = "древесина",
        ["item.grain"] = "зерно",
        ["item.plank"] = "доски",
        ["item.bread"] = "хлеб",
        ["context.hint"] = "Подойдите к объекту. Стрелки выбирают действие, Enter подтверждает одно.",
        ["context.harvest"] = "Добыть",
        ["context.buy"] = "Купить участок за {0}",
        ["context.reserve"] = "Забронировать за {0}",
        ["context.card"] = "Открыть карточку предприятия",
        ["context.transfer"] = "Передать 1 {0}",
        ["context.approach"] = "Подойдите ближе: издалека действие недоступно.",
        ["plot.free"] = "Свободный участок",
        ["plot.own"] = "Ваш участок",
        ["plot.foreign"] = "Чужой участок",
        ["plot.reserved"] = "Забронирован до тика {0}",
        ["plot.price"] = "Покупка {0}, бронь {1} и не засчитывается в цену.",
        ["hover.resource"] = "Ресурс {0}. Добыча одной единицы занимает обычное время.",
        ["hover.capital"] = "Терминал столицы. Публичные предложения: {0}.",
        ["hover.blocked"] = "Непроходимая клетка.",
        ["hover.corridor"] = "Проход. Можно ходить; покупать и строить нельзя.",
        ["hover.building"] = "{0}. Владелец {1}.",
        ["hover.empty"] = "Пустая клетка.",
        ["new.session"] = "Новая партия:",
        ["legend.title"] = "Легенда",
        ["legend.player"] = "круг с номером — вы",
        ["legend.npc"] = "круг с номером — NPC",
        ["legend.wood"] = "круг — древесина",
        ["legend.grain"] = "ромб — зерно",
        ["legend.plot.free"] = "квадрат — свободный участок",
        ["legend.plot.own"] = "квадрат с рамкой — свой участок",
        ["legend.plot.foreign"] = "квадрат — чужой участок",
        ["legend.plot.reserved"] = "пунктир — забронированный участок",
        ["legend.corridor"] = "полосы — проход",
        ["legend.blocked"] = "крест — непроходимо",
        ["legend.production"] = "квадрат — предприятие",
        ["legend.warehouse"] = "широкий прямоугольник — склад",
        ["legend.shop"] = "треугольник — лавочка",
        ["legend.capital"] = "ромб — терминал столицы",
        ["legend.cargo"] = "ящик — груз в пути",
        ["cell.selected"] = "Клетка",
        ["debug.hint"] = "Выберите действие и нажмите «Один тик». Пока оно не подтверждено, время стоит."
    };

    private static Dictionary<string, string> En() => new(StringComparer.Ordinal)
    {
        ["title"] = "Delsk — session",
        ["language"] = "RU",
        ["pause"] = "Run / pause",
        ["step"] = "One tick",
        ["save"] = "Save",
        ["load"] = "Load",
        ["economy"] = "Economy",
        ["politics"] = "Politics",
        ["tick"] = "Tick",
        ["cash"] = "Money",
        ["position"] = "Place",
        ["busy"] = "Busy",
        ["yes"] = "yes",
        ["no"] = "no",
        ["move.north"] = "z−1",
        ["move.south"] = "z+1",
        ["move.east"] = "x+1",
        ["move.west"] = "x−1",
        ["harvest"] = "Harvest",
        ["transfer.input"] = "Place 1 on input",
        ["card.work"] = "Work",
        ["card.supplies"] = "Supplies",
        ["card.wages"] = "Wages",
        ["work.start"] = "Resume work",
        ["work.stop"] = "Stop work",
        ["wage.raise"] = "Raise wage",
        ["wage.lower"] = "Lower wage",
        ["supplies.output.on"] = "Send output",
        ["supplies.output.off"] = "Stop sending",
        ["supplies.inbound.on"] = "Resume inbound",
        ["supplies.inbound.off"] = "Freeze inbound",
        ["supplies.buy"] = "Buy logistics",
        ["supplies.send"] = "Send to storage",
        ["supplies.fetch"] = "Fetch from storage",
        ["support.title"] = "Your support proposals",
        ["support.accept"] = "Accept",
        ["support.reject"] = "Reject",
        ["support.none"] = "No proposal where you are a party.",
        ["vote.title"] = "Vote",
        ["vote.choose"] = "Vote for",
        ["npc.title"] = "Public employment",
        ["feedback.sent"] = "Command queued for the core.",
        ["no.enterprise"] = "No enterprise yet. Harvest and movement are available.",
        ["paused"] = "Paused",
        ["running"] = "Running",
        ["trip"] = "Trip",
        ["you"] = "You",
        ["selected"] = "Selected",
        ["scene.hint"] = "WASD moves. Hover explains an object. Arrows and Enter act on your cell.",
        ["menu.title"] = "Menu",
        ["menu.continue"] = "Continue",
        ["menu.debug"] = "Debug mode",
        ["menu.exit"] = "Exit game",
        ["menu.settings"] = "Settings",
        ["menu.escape"] = "Escape here means Continue while the window is focused.",
        ["command.selected"] = "Selected, not sent yet.",
        ["command.sent"] = "Sent, waiting for a tick.",
        ["command.running"] = "Running, {0} left",
        ["command.done"] = "Finished.",
        ["command.rejected"] = "Rejected:",
        ["inventory.title"] = "Personal inventory",
        ["inventory.empty"] = "empty",
        ["inventory.capacity"] = "Space",
        ["received"] = "Received: {0} ×{1}",
        ["harvest.left"] = "Harvest {0}: {1} left",
        ["reason.NoMoney"] = "Not enough money.",
        ["reason.NoStock"] = "Not enough unreserved materials.",
        ["reason.LandNotOwned"] = "Buy all footprint cells first.",
        ["reason.WrongBuildingSite"] = "This building does not fit this site.",
        ["reason.NoCapacity"] = "Inventory is full.",
        ["reason.ParticipantBusy"] = "The character is busy with another action.",
        ["reason.NoResourceNode"] = "Move to the resource.",
        ["reason.NotAtLocation"] = "Move to the right place.",
        ["reason.NoPath"] = "That cell cannot be reached.",
        ["reason.NotAvailable"] = "That action is not available now.",
        ["reason.PlotOwned"] = "The plot is already owned.",
        ["reason.PlotReserved"] = "Another participant has reserved the plot.",
        ["reason.ProtectedCorridor"] = "A passage cannot be bought or reserved.",
        ["reason.ReservationLimit"] = "Too many active reservations.",
        ["reason.ShopSiteLimit"] = "You already own or reserve a shop site.",
        ["reason.StaleMarketRound"] = "The auction round changed. Select the action again.",
        ["reason.BidLimitExceeded"] = "The price exceeds your original request limit.",
        ["reason.RaiseTooSmall"] = "The increase is smaller than the minimum auction step.",
        ["reason.PurchaseClosed"] = "Pickup is closed or its deadline has expired.",
        ["money"] = "Money",
        ["item.wood"] = "wood",
        ["item.grain"] = "grain",
        ["item.plank"] = "planks",
        ["item.bread"] = "bread",
        ["context.hint"] = "Walk up to an object. Arrows choose an action, Enter confirms one.",
        ["context.harvest"] = "Harvest",
        ["context.buy"] = "Buy plot for {0}",
        ["context.reserve"] = "Reserve for {0}",
        ["context.card"] = "Open the enterprise card",
        ["context.transfer"] = "Transfer 1 {0}",
        ["context.approach"] = "Move closer: distance does not grant the action.",
        ["plot.free"] = "Free plot",
        ["plot.own"] = "Your plot",
        ["plot.foreign"] = "Someone else's plot",
        ["plot.reserved"] = "Reserved until tick {0}",
        ["plot.price"] = "Purchase {0}, reservation {1} and it is not credited toward the price.",
        ["hover.resource"] = "Resource {0}. Harvesting one unit takes the normal time.",
        ["hover.capital"] = "Capital terminal. Public offers: {0}.",
        ["hover.blocked"] = "Blocked cell.",
        ["hover.corridor"] = "Passage. Walk here; purchase and building are not allowed.",
        ["hover.building"] = "{0}. Owner {1}.",
        ["hover.empty"] = "Empty cell.",
        ["new.session"] = "New session:",
        ["legend.title"] = "Legend",
        ["legend.player"] = "numbered circle — you",
        ["legend.npc"] = "numbered circle — NPC",
        ["legend.wood"] = "circle — wood",
        ["legend.grain"] = "diamond — grain",
        ["legend.plot.free"] = "square — free plot",
        ["legend.plot.own"] = "framed square — your plot",
        ["legend.plot.foreign"] = "square — someone else's plot",
        ["legend.plot.reserved"] = "dashed square — reserved plot",
        ["legend.corridor"] = "stripes — passage",
        ["legend.blocked"] = "cross — blocked",
        ["legend.production"] = "square — enterprise",
        ["legend.warehouse"] = "wide rectangle — warehouse",
        ["legend.shop"] = "triangle — shop",
        ["legend.capital"] = "diamond — capital terminal",
        ["legend.cargo"] = "box — cargo on a trip",
        ["cell.selected"] = "Cell",
        ["debug.hint"] = "Choose an action, then press One tick. Time stays stopped until you confirm it."
    };

    public static string Item(string language, string itemId)
    {
        string key = "item." + itemId;
        return Keys(language).Contains(key) ? Get(language, key) : itemId;
    }

    public static string Local(string language, string ru, string en) => language == "ru" ? ru : en;
    public static string Money(long minor) => (minor / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    public static string Building(string language, string type) => type switch
    {
        "sawmill" => Local(language, "Лесопилка", "Sawmill"),
        "bakery" => Local(language, "Пекарня", "Bakery"),
        "warehouse" => Local(language, "Склад", "Warehouse"),
        "shop" => Local(language, "Лавочка", "Shop"),
        _ => type
    };

    public static string Reason(string language, string code)
    {
        string key = "reason." + code;
        return Keys(language).Contains(key) ? Get(language, key) : code;
    }

    public static string Received(string language, string item, long quantity) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, Get(language, "received"), item, quantity);

    public static string HarvestLeft(string language, string item, long remaining) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, Get(language, "harvest.left"), item, remaining);

    public static string? OwnHarvest(PublicSimulationEvent[] events, long actor, string language)
    {
        var done = events.LastOrDefault(e => e.Type == "HarvestCompleted" && e.ActorId?.Value == actor);
        return done is null ? null : Received(language, Item(language, done.ItemId), done.Quantity);
    }
}
