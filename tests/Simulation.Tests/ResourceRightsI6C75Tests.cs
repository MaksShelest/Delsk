using System.Text;
using System.Text.Json;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class ResourceRightsI6C75Tests
{
    private static readonly ParticipantId Human = new(1);
    private static readonly ParticipantId Other = new(2);

    private static ScenarioDefinition Open(bool Public, long? owner = null, long cash = 100_000, int harvestTicks = 4) => Fixtures.Cycle() with
    {
        HarvestTicksPerUnit = harvestTicks,
        Participants = Fixtures.Cycle().Participants.Select(p => p with { CashMinor = cash }).ToArray(),
        Economy = new(ResourceRights: new ResourceRightsRules(1)),
        ResourceNodes = [new("wood", new(0, 0), new(Public, Capacity: 8, RegenerationQuantity: 1, RegenerationTicks: 10, PurchasePriceMinor: 15_000, LeasePriceMinor: 50, InitialOwner: owner))]
    };

    private static string Do(GameSession session, GameAction action, long actor = 1) =>
        session.Engine.Result(Fixtures.Command(session, action, actor))!.ReasonCode;

    private static GameAction Harvest(long quantity = 1) => new(ActionKind.Harvest, ItemId: "wood", Quantity: quantity);
    private static GameAction Buy() => new(ActionKind.BuyResource, Target: new(0, 0), ItemId: "wood");
    private static ResourceSiteView Site(GameSession session) => session.Engine.ResourceSites().Single(r => r.Position == new Cell(0, 0));
    private static long Cash(GameSession session, long actor) => session.Engine.Observe(new(actor)).Account.BalanceMinor;
    private static long Treasury(GameSession session) => session.Engine.Inspect().Accounts.Single(a => a.Kind == "Treasury").BalanceMinor;

    [Fact]
    public void TwoParticipantsShareOneUnownedCellUntilTheStockIsReserved()
    {
        var session = Fixtures.Session(Open(false));
        string first = session.Send(Human, Harvest(2));
        string second = session.Send(Other, Harvest(2));
        session.Step();
        Assert.Equal("Accepted", session.Engine.Result(first)!.ReasonCode);
        Assert.Equal("Accepted", session.Engine.Result(second)!.ReasonCode);
        Assert.Equal(4, Site(session).Reserved);
        Fixtures.UntilIdle(session, 1);
        Fixtures.UntilIdle(session, 2);
        Assert.Equal(4, Site(session).Stock);
        Assert.Equal(2, Fixtures.Count(session, session.Engine.Observe(Human).Participant.InventoryId, "wood"));
        Assert.Equal(2, Fixtures.Count(session, session.Engine.Observe(Other).Participant.InventoryId, "wood"));
    }

    [Fact]
    public void UnownedPrivateAndPublicCellsAreManualAndPurchasableOnlyUnderTheNewPolicy()
    {
        var openPrivate = Fixtures.Session(Open(false));
        Assert.Equal("Accepted", Do(openPrivate, Harvest()));
        var openPublic = Fixtures.Session(Open(true));
        long stock = Site(openPublic).Stock;
        long cash = Cash(openPublic, 1);
        long treasury = Treasury(openPublic);
        Assert.Equal("Accepted", Do(openPublic, Harvest()));
        Fixtures.UntilIdle(openPublic);
        Assert.Equal(stock - 1, Site(openPublic).Stock);
        Assert.Equal("Accepted", Do(openPublic, Buy()));
        Assert.Equal(cash - 15_000, Cash(openPublic, 1));
        Assert.Equal(treasury + 15_000, Treasury(openPublic));
        Assert.True(Site(openPublic).Public);
        Assert.Equal(Human, Site(openPublic).Owner);
        Assert.Equal(stock - 1, Site(openPublic).Stock);
        Assert.Equal("NoResourceRights", Do(openPublic, Harvest(), 2));
        Assert.Equal("Accepted", Do(openPublic, Harvest()));
        Assert.Equal("ResourceLeaseDisabled", Do(openPublic, new(ActionKind.LeaseResource, Target: new(0, 0), ItemId: "wood"), 2));
    }

    [Fact]
    public void PurchaseDuringAnOpenCycleIsRejectedWithoutChargingOrReleasingTheReserve()
    {
        var session = Fixtures.Session(Open(true));
        long stock = Site(session).Stock;
        long cash = Cash(session, 2);
        Assert.Equal("Accepted", Do(session, new(ActionKind.StartHarvesting, ItemId: "wood", Quantity: 1)));
        Assert.Equal(1, Site(session).Reserved);
        Assert.Equal("ResourceBusy", Do(session, Buy(), 2));
        Assert.Equal(cash, Cash(session, 2));
        Assert.Null(Site(session).Owner);
        Assert.Equal(stock, Site(session).Stock);
        Assert.True(session.Engine.Observe(Human).Participant.Busy);
        Assert.Equal("Accepted", Do(session, new(ActionKind.StopHarvest)));
        Assert.Equal(0, Site(session).Reserved);
        Assert.Equal(stock, Site(session).Stock);
        Assert.Equal("Accepted", Do(session, Buy(), 2));
        Assert.Equal(cash - 15_000, Cash(session, 2));
        Assert.Equal(Other, Site(session).Owner);
        Assert.Equal(15_000, session.Engine.Ledger().Where(row => row.Reason == "ResourcePurchase").Sum(row => row.AmountMinor));
    }

    [Fact]
    public void RejectedPurchaseDoesNotMoveMoneyAndDepletionStillRegenerates()
    {
        var poor = Fixtures.Session(Open(false, cash: 10));
        long treasury = Treasury(poor);
        Assert.Equal("NoMoney", Do(poor, Buy()));
        Assert.Equal(10, Cash(poor, 1));
        Assert.Equal(treasury, Treasury(poor));
        Assert.Null(Site(poor).Owner);
        Assert.DoesNotContain(poor.Engine.Ledger(), row => row.Reason == "ResourcePurchase");
        Assert.Equal("ResourceLeaseDisabled", Do(poor, new(ActionKind.LeaseResource, Target: new(0, 0), ItemId: "wood")));
        Assert.Null(Site(poor).Tenant);

        var session = Fixtures.Session(Open(false, harvestTicks: 1) with
        {
            ResourceNodes = [new("wood", new(0, 0), new(false, Capacity: 3, RegenerationQuantity: 1, RegenerationTicks: 10, PurchasePriceMinor: 15_000))]
        });
        Assert.Equal("Accepted", Do(session, Harvest(3)));
        Fixtures.UntilIdle(session);
        Assert.Equal(0, Site(session).Stock);
        Assert.Equal("ResourceDepleted", Do(session, Harvest()));
        long before = Site(session).Stock;
        Assert.Equal("Accepted", Do(session, Buy()));
        Assert.Equal(before, Site(session).Stock);
        session.AdvanceTo(10);
        Assert.Equal(1, Site(session).Stock);
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(session));
        Assert.Equal(session.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        Assert.False(loaded.Engine.ResourceSites().Single().Public);
        Assert.Equal(Human, loaded.Engine.ResourceSites().Single().Owner);
        loaded.Engine.ValidateInvariants();
    }

    [Theory]
    [InlineData(PlayMaps.CompactValley)]
    [InlineData(PlayMaps.Second)]
    public void OrdinaryMapKeepsPurchasedPublicRightsThroughSaveLoadAndReplay(string map)
    {
        foreach (bool publicCell in new[] { true, false })
        {
            var scenario = I6C2MapsTests.Resolve(map).Scenario;
            Assert.Equal(32, new SimulationEngine(scenario).ActiveCheckpointSchemaVersion);
            var node = scenario.ResourceNodes.First(n => n.Rules!.Public == publicCell && n.Rules.InitialOwner is null);
            Assert.Equal(15_000, node.Rules!.PurchasePriceMinor);
            var prepared = scenario with
            {
                Participants = scenario.Participants.Select(p => p.Id is 1 or 2 ? p with { Position = node.Position, CashMinor = 1_000_000 } : p).ToArray()
            };
            var session = new GameSession(new(prepared));
            var harvest = new GameAction(ActionKind.Harvest, ItemId: node.ItemId, Quantity: 1);
            var buy = new GameAction(ActionKind.BuyResource, Target: node.Position, ItemId: node.ItemId);
            long stock = session.Engine.ResourceSites().Single(r => r.Position == node.Position && r.ItemId == node.ItemId).Stock;
            Assert.Equal("Accepted", Do(session, new(ActionKind.StartHarvesting, ItemId: node.ItemId, Quantity: 1)));
            long buyer = Cash(session, 2);
            Assert.Equal("ResourceBusy", Do(session, buy, 2));
            Assert.Equal(buyer, Cash(session, 2));
            Assert.Null(session.Engine.ResourceSites().Single(r => r.Position == node.Position && r.ItemId == node.ItemId).Owner);
            Assert.Equal("Accepted", Do(session, new(ActionKind.StopHarvest)));
            var cell = session.Engine.ResourceSites().Single(r => r.Position == node.Position && r.ItemId == node.ItemId);
            Assert.True(cell.Stock <= stock);
            Assert.Equal(0, cell.Reserved);
            stock = cell.Stock;
            long before = Cash(session, 1);
            long treasury = Treasury(session);
            Assert.Equal("Accepted", Do(session, buy));
            cell = session.Engine.ResourceSites().Single(r => r.Position == node.Position && r.ItemId == node.ItemId);
            Assert.Equal(publicCell, cell.Public);
            Assert.Equal(Human, cell.Owner);
            Assert.Equal(stock, cell.Stock);
            Assert.Equal(before - 15_000, Cash(session, 1));
            Assert.Equal(treasury + 15_000, Treasury(session));
            Assert.Equal("NoResourceRights", Do(session, harvest, 2));
            Assert.Equal("Accepted", Do(session, harvest));
            var loaded = CheckpointStore.Decode(CheckpointStore.Encode(session));
            Assert.Equal(session.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
            var saved = loaded.Engine.ResourceSites().Single(r => r.Position == node.Position && r.ItemId == node.ItemId);
            Assert.Equal(publicCell, saved.Public);
            Assert.Equal(Human, saved.Owner);
            loaded.Engine.ValidateInvariants();
            Assert.Equal("NoResourceRights", loaded.Engine.Result(Fixtures.Command(loaded, harvest, 2))!.ReasonCode);
            var replay = GameSession.Replay(session.Engine.Configuration(), session.CommandTape, session.Engine.Tick);
            Assert.Equal(session.Engine.ComputeStateHash(), replay.Engine.ComputeStateHash());
            Assert.Equal(Human, replay.Engine.ResourceSites().Single(r => r.Position == node.Position && r.ItemId == node.ItemId).Owner);
        }
    }

    [Fact]
    public void CompetingPurchaseOfAPublicCellPaysOnce()
    {
        var session = Fixtures.Session(Open(true, cash: 50_000));
        long before = Cash(session, 1) + Cash(session, 2);
        string first = session.Send(Human, Buy());
        string second = session.Send(Other, Buy());
        session.Step();
        Assert.Single(new[] { session.Engine.Result(first), session.Engine.Result(second) }, result => result!.Status == ResultStatus.Accepted);
        Assert.NotNull(Site(session).Owner);
        Assert.True(Site(session).Public);
        Assert.Equal(15_000, before - (Cash(session, 1) + Cash(session, 2)));
        Assert.Equal(15_000, session.Engine.Ledger().Where(row => row.Reason == "ResourcePurchase").Sum(row => row.AmountMinor));
        session.Engine.ValidateInvariants();
    }

    [Fact]
    public void NpcHarvestsUnownedCellsAndDoesNotTakeForeignOrHiddenCash()
    {
        AlgorithmicController Npc() => new(new(2, "wood", DecisionIntervalTicks: 1, LogisticsEnabled: false, PoliticsEnabled: false, ExpansionWeight: 0), 42, [1, 2]);
        var open = Fixtures.Session(Open(false));
        var decision = Npc().Decide(open.Engine.ObserveController(Other), []);
        Assert.Equal(ActionKind.Harvest, Assert.Single(decision.Actions).Kind);
        long stock = Site(open).Stock;
        Assert.Equal("Accepted", Do(open, decision.Actions[0], 2));
        Fixtures.UntilIdle(open, 2);
        Assert.True(Site(open).Stock < stock);

        var closed = Fixtures.Session(Open(false) with { Economy = null });
        Assert.Null(closed.Engine.EconomyRules.ResourceRights);
        var refused = Npc().Decide(closed.Engine.ObserveController(Other), []);
        Assert.DoesNotContain(refused.Actions, action => action.Kind is ActionKind.Harvest or ActionKind.StartHarvesting);
        Assert.Equal("NoResourceRights", Do(closed, Harvest(), 2));
        Assert.Null(closed.Engine.ObserveController(Other).Economics?.ResourceRights);

        var own = Fixtures.Session(Open(false, owner: 2));
        Assert.Contains(Npc().Decide(own.Engine.ObserveController(Other), []).Actions, action => action.Kind == ActionKind.Harvest);
        var foreign = Fixtures.Session(Open(true) with
        {
            Participants = Open(true).Participants.Select(p => p.Id == 1 ? p with { CashMinor = 777_000 } : p with { CashMinor = 100_000 }).ToArray()
        });
        Assert.Equal("Accepted", Do(foreign, Buy()));
        var seen = foreign.Engine.ObserveController(Other);
        var economics = seen.Economics ?? throw new InvalidOperationException("economics");
        Assert.Equal(1, economics.ResourceRights!.Version);
        Assert.Equal(100_000, seen.Account.BalanceMinor);
        Assert.Equal(Human, seen.Resources.Single().Owner);
        Assert.DoesNotContain(Npc().Decide(seen, []).Actions, action => action.Kind is ActionKind.Harvest or ActionKind.StartHarvesting or ActionKind.BuyResource);
        Assert.Equal("NoResourceRights", Do(foreign, Harvest(), 2));
    }

    [Fact]
    public void HistoricalProfilesKeepTheOldModelAndTheNewProfileDoesNotChangeWages()
    {
        var wages = VNextWageScheduleProfile.Create(EquipmentClassesI6C22Tests.Profile());
        string historical = Encoding.UTF8.GetString(VNextSupportedProfile.Serialize(wages));
        Assert.DoesNotContain("resourceRights", historical);
        Assert.Null(EconomyProfile.Read(historical).World.Rules.ResourceRights);
        var profile = VNextResourceRightsProfile.Create(wages);
        Assert.Equal(VNextResourceRightsProfile.ProfileId, profile.Id);
        Assert.Equal("resource-rights-projection-1", profile.VNext!.ProjectionVersion);
        Assert.Equal(1, profile.World.Rules.ResourceRights!.Version);
        Assert.Equal(2, profile.World.Rules.PlayerWageStepMinor);
        Assert.Equal(15, profile.World.Rules.Fleet!.WageMinor);
        Assert.Equal(10_000, profile.World.PlotPriceMinor);
        var read = EconomyProfile.Read(Encoding.UTF8.GetString(VNextSupportedProfile.Serialize(profile)));
        Assert.Equal(profile.Id, read.Id);
        Assert.Equal(1, read.World.Rules.ResourceRights!.Version);
        Assert.Throws<ArgumentException>(() => VNextResourceRightsProfile.Create(EquipmentClassesI6C22Tests.Profile()));
        Assert.Throws<ArgumentException>(() => new SimulationEngine(Fixtures.Cycle() with { Economy = new(ResourceRights: new(2)) }));
        Assert.Throws<ArgumentException>(() => new SimulationEngine(Open(true, owner: 1)));

        var old = Fixtures.Session(Fixtures.Cycle() with
        {
            Participants = Fixtures.Cycle().Participants.Select(p => p with { CashMinor = 100_000 }).ToArray(),
            ResourceNodes =
            [
                new("wood", new(0, 0), new(false, Capacity: 5, RegenerationQuantity: 1, RegenerationTicks: 10, PurchasePriceMinor: 100, LeasePriceMinor: 7, LeaseTicks: 30)),
                new("plank", new(1, 0), new(true, Capacity: 5, RegenerationQuantity: 1, RegenerationTicks: 10, PurchasePriceMinor: 15_000))
            ]
        });
        Assert.Equal("NoResourceRights", Do(old, Harvest()));
        Assert.Equal("Accepted", Do(old, new(ActionKind.LeaseResource, Target: new(0, 0), ItemId: "wood")));
        Assert.Equal(Human, Site(old).Tenant);
        var publicSite = Fixtures.Session(old.Engine.Configuration() with
        {
            Participants = old.Engine.Configuration().Participants.Select(p => p with { Position = new Cell(1, 0) }).ToArray()
        });
        Assert.Equal("ResourceUnavailable", Do(publicSite, new(ActionKind.BuyResource, Target: new(1, 0), ItemId: "plank")));
        Assert.Equal("Accepted", Do(publicSite, new(ActionKind.Harvest, ItemId: "plank", Quantity: 1)));
    }

    [Fact]
    public void ContextNamesTheRightsPriceAndHighlightsThePurchasedCell()
    {
        var session = Fixtures.Session(Open(false));
        var view = PlayerDesk.Read(session.Engine, Human);
        var ru = ContextActions.Read(session.Engine, Human, session.Engine.Configuration(), "ru");
        var buy = Assert.Single(ru, choice => choice.Command?.Kind == ActionKind.BuyResource);
        Assert.True(buy.Enabled);
        Assert.Contains("Купить ресурсную клетку", buy.Label);
        Assert.Contains(PlayerText.Money(15_000), buy.Label);
        Assert.Equal("Добыть", Assert.Single(ru, choice => choice.Command?.Kind == ActionKind.StartHarvesting).Label);
        Assert.DoesNotContain(ru, choice => choice.Command?.Kind == ActionKind.LeaseResource);
        Assert.Contains("ничейный", PlayContext.ResourceLine(view, view.Resources.Single(), "ru"));
        Assert.Contains("остаток", PlayContext.ResourceLine(view, view.Resources.Single(), "ru"));
        Assert.Contains("восстановление", PlayContext.ResourceLine(view, view.Resources.Single(), "ru"));
        Assert.Equal(new Cell(0, 0), Assert.Single(FriendlyGuidance.Cells(buy.Command, session.Engine.Configuration())));
        var en = ContextActions.Read(session.Engine, Human, session.Engine.Configuration(), "en");
        Assert.Contains("Buy resource cell", Assert.Single(en, choice => choice.Command?.Kind == ActionKind.BuyResource).Label);
        Assert.Contains("unowned", PlayContext.ResourceLine(view, view.Resources.Single(), "en"));

        var broke = Fixtures.Session(Open(true, cash: 10));
        var blocked = ContextActions.Read(broke.Engine, Human, broke.Engine.Configuration(), "ru");
        var denied = Assert.Single(blocked, choice => choice.Command?.Kind == ActionKind.BuyResource);
        Assert.False(denied.Enabled);
        Assert.Contains("Недостаточно денег", denied.Details);
        var dock = new ContextSelection();
        dock.Update("cell", blocked);
        Assert.Contains(denied.Key, dock.PrimaryKeys(true));

        Assert.Equal("Accepted", Do(session, new(ActionKind.StartHarvesting, ItemId: "wood", Quantity: 1)));
        var busy = ContextActions.Read(session.Engine, Other, session.Engine.Configuration(), "ru");
        var waiting = Assert.Single(busy, choice => choice.Command?.Kind == ActionKind.BuyResource);
        Assert.False(waiting.Enabled);
        Assert.Contains("занят", waiting.Details);

        Fixtures.UntilIdle(session);
        Assert.Equal("Accepted", Do(session, Buy()));
        var owner = PlayerDesk.Read(session.Engine, Human);
        var foreign = PlayerDesk.Read(session.Engine, Other);
        Assert.Contains("ваш ресурс", PlayContext.ResourceLine(owner, owner.Resources.Single(), "ru"));
        Assert.Contains("your resource", PlayContext.ResourceLine(owner, owner.Resources.Single(), "en"));
        Assert.Contains("владелец #1", PlayContext.ResourceLine(foreign, foreign.Resources.Single(), "ru"));
        Assert.Contains("добыча недоступна", PlayContext.ResourceLine(foreign, foreign.Resources.Single(), "ru"));
        var foreignActions = ContextActions.Read(session.Engine, Other, session.Engine.Configuration(), "ru");
        Assert.DoesNotContain(foreignActions, choice => choice.Command?.Kind is ActionKind.Harvest or ActionKind.StartHarvesting or ActionKind.BuyResource);
        Assert.Contains(ContextActions.Read(session.Engine, Human, session.Engine.Configuration(), "ru"), choice => choice.Command?.Kind == ActionKind.StartHarvesting && choice.Enabled);

        var guide = Fixtures.Session(Fixtures.Cycle() with
        {
            Economy = new(ResourceRights: new(1)),
            ResourceNodes =
            [
                new("wood", new(0, 0), new(false, Capacity: 5, RegenerationQuantity: 1, RegenerationTicks: 10, PurchasePriceMinor: 15_000)),
                new("wood", new(20, 0), new(true, Capacity: 5, RegenerationQuantity: 1, RegenerationTicks: 10, PurchasePriceMinor: 15_000))
            ]
        });
        var step = FriendlyGuidance.Read(PlayerDesk.Read(guide.Engine, Human), guide.Engine.Configuration(), "ru");
        Assert.Equal(0, step.X);
        Assert.Contains("Ничейный", PlayerText.Local("ru", step.DetailRu, step.DetailEn));
        var oldGuide = Fixtures.Session(guide.Engine.Configuration() with { Economy = null });
        var oldStep = FriendlyGuidance.Read(PlayerDesk.Read(oldGuide.Engine, Human), oldGuide.Engine.Configuration(), "ru");
        Assert.Equal(20, oldStep.X);
        Assert.Contains("Общий", PlayerText.Local("ru", oldStep.DetailRu, oldStep.DetailEn));
    }

    [Fact]
    public void OwnersMachineWorksOnAPurchasedPublicCellAndFailsBeforePurchase()
    {
        var prepared = MobileEquipmentI4Tests.Prepared();
        var node = prepared.ResourceNodes.Single(n => n.ItemId == "wood" && n.Rules!.Public);
        var open = prepared with
        {
            Economy = prepared.Economy! with { ResourceRights = new(1) },
            ResourceNodes = prepared.ResourceNodes.Select(n => n == node ? n with { Rules = n.Rules! with { PurchasePriceMinor = 15_000 } } : n).ToArray()
        };
        var session = new GameSession(new(open));
        var vehicle = MobileEquipmentI4Tests.Buy(session, "wood");
        var equipment = new EquipmentId(vehicle.Id);
        FleetE1Tests.Board(session, vehicle);
        FleetE1Tests.Step(session, new(ActionKind.MoveTo, Target: node.Position));
        StorageC1Tests.Idle(session);
        Assert.Equal("NoResourceRights", session.Engine.EquipmentPlacementReason(Human, equipment));
        long cash = Cash(session, 1);
        long stock = session.Engine.ResourceSites().Single(r => r.Position == node.Position && r.ItemId == "wood").Stock;
        FleetE1Tests.Step(session, new(ActionKind.BuyResource, Target: node.Position, ItemId: "wood"));
        Assert.Equal(cash - 15_000, Cash(session, 1));
        var site = session.Engine.ResourceSites().Single(r => r.Position == node.Position && r.ItemId == "wood");
        Assert.True(site.Public);
        Assert.Equal(Human, site.Owner);
        Assert.Equal(stock, site.Stock);
        Assert.Equal("Allowed", session.Engine.EquipmentPlacementReason(Human, equipment));
        FleetE1Tests.Step(session, new(ActionKind.PlaceEquipment, Equipment: equipment));
        Assert.Equal("Accepted", Do(session, new(ActionKind.Harvest, ItemId: "wood", Quantity: 1)));
        Fixtures.UntilIdle(session);
        Assert.True(Fixtures.Count(session, session.Engine.Observe(Human).Participant.InventoryId, "wood") > 0);
        FleetE1Tests.Step(session, new(ActionKind.SetEquipmentMode, Equipment: equipment, Mode: EquipmentMode.Personal));
        FleetE1Tests.Step(session, new(ActionKind.StartHarvesting, Equipment: equipment, ItemId: "wood", Quantity: 2));
        session.AdvanceTo(session.Engine.Tick + 2);
        Assert.True(FleetE1Tests.Stock(session, vehicle.CargoInventory, "wood") > 0 || session.Engine.Observe(Human).OwnEquipment.Single().PersonalJobId != 0);
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(session));
        Assert.Equal(session.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        var saved = loaded.Engine.ResourceSites().Single(r => r.Position == node.Position && r.ItemId == "wood");
        Assert.True(saved.Public);
        Assert.Equal(Human, saved.Owner);
        loaded.Engine.ValidateInvariants();
        MobileEquipmentI4Tests.Exact(session);
    }

    [Fact]
    public void SavedEconomyWithoutTheFlagDoesNotMentionResourceRights()
    {
        var session = Fixtures.Session();
        string json = session.Engine.CreateCheckpointJson();
        Assert.DoesNotContain("resourceRights", json);
        var loaded = SimulationEngine.FromCheckpointJson(json);
        Assert.Equal(session.Engine.ComputeStateHash(), loaded.ComputeStateHash());
        Assert.Null(loaded.EconomyRules.ResourceRights);
    }
}
