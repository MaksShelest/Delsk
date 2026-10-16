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
        ["running"] = "Идёт"
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
        ["running"] = "Running"
    };
}
