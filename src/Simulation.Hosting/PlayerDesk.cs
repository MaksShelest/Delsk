using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>
/// Команды и проекция человека. Деньги меняет только ядро после принятой команды.
/// Чужая поддержка сюда не попадает: источник — Observe, не InspectSupport.
/// </summary>
public static class PlayerDesk
{
    public const long WageStepMinor = 100;
    public static readonly ActionKind[] Allowed =
    [
        ActionKind.MoveTo, ActionKind.Harvest, ActionKind.Transfer,
        ActionKind.SetProductionEnabled, ActionKind.SetWage,
        ActionKind.BuyLogistics, ActionKind.SetLogisticsEnabled,
        ActionKind.SetLogisticsStorage, ActionKind.SetLogisticsInbound,
        ActionKind.AcceptSupport, ActionKind.RejectSupport, ActionKind.SetVoteChoice
    ];

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

    public static PlayerView Read(SimulationEngine engine, ParticipantId actor)
    {
        var seen = engine.Observe(actor);
        return new(seen.Tick, seen.Participant, seen.Account.BalanceMinor, seen.OwnInventories, seen.OwnEnterprises,
            seen.OwnBuildings, seen.OwnEmploymentGroups, seen.OwnLogistics, seen.LaborMarket, seen.Support, seen.Elections);
    }
}

public record PlayerView(long Tick, ParticipantView Participant, long CashMinor, InventoryView[] Inventories,
    EnterpriseView[] Enterprises, BuildingView[] Buildings, EmploymentView[] Employment, LogisticsView[] Logistics,
    PublicEmploymentView[] LaborMarket, SupportObservation Support, ElectionObservation Elections);

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
        ["scene.hint"] = "Клик по клетке: ход, добыча или своё предприятие.",
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
        ["reason.NoMoney"] = "Недостаточно денег для покупки.",
        ["reason.NoCapacity"] = "Инвентарь заполнен.",
        ["reason.ParticipantBusy"] = "Персонаж занят добычей.",
        ["reason.NoResourceNode"] = "Подойдите к точке ресурса.",
        ["reason.NotAtLocation"] = "Подойдите к нужному месту.",
        ["reason.NoPath"] = "Сюда пройти нельзя.",
        ["reason.NotAvailable"] = "Сейчас это действие недоступно.",
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
        ["scene.hint"] = "Click a cell: move, harvest, or your enterprise.",
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
        ["reason.NoMoney"] = "Not enough money to buy.",
        ["reason.NoCapacity"] = "Inventory is full.",
        ["reason.ParticipantBusy"] = "The character is busy harvesting.",
        ["reason.NoResourceNode"] = "Move to the resource.",
        ["reason.NotAtLocation"] = "Move to the right place.",
        ["reason.NoPath"] = "That cell cannot be reached.",
        ["reason.NotAvailable"] = "That action is not available now.",
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
        return done is null ? null : Received(language, done.ItemId, done.Quantity);
    }
}
