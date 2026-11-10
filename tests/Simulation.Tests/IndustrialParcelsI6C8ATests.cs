using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

/// <summary>
/// Подготовленный мир: денег хватает на участок за 600 монет.
/// Обычный старт на 200 монет этот профиль не включает.
/// Материалы уровня 1 и 2 в проверке стройки заменены на дерево и доски,
/// потому что на карте нет месторождения стали. Цены каталога прежние.
/// </summary>
public class IndustrialParcelsI6C8ATests
{
    const long PreparedCash = 2_000_000;

    static EconomyProfile Profile() => VNextIndustrialParcelProfile.Create(I6C2MapsTests.Economy(PlayMaps.CompactValley));

    static MapDraft Draft()
    {
        var draft = MapDraft.Create(40, 28);
        draft.Id = "industrial_block_i6c8a";
        draft.Name = "Промышленный блок";
        draft.MaxParticipants = 4;
        draft.Spawns.Clear();
        draft.Turns.Clear();
        foreach (var x in new[] { 2, 3, 4, 5 }) draft.ToggleSpawn(new(x, 2));
        draft.Turns.Add(new(2, 2));
        for (int x = 2; x <= 26; x++) draft.PaintRoad(new(x, 2));
        for (int z = 3; z <= 17; z++) draft.PaintRoad(new(2, z));
        for (int x = 3; x <= 9; x++) draft.PaintRoad(new(x, 17));
        for (int z = 3; z <= 9; z++) draft.PaintRoad(new(26, z));
        for (int x = 20; x <= 25; x++) draft.PaintRoad(new(x, 9));
        draft.PlaceCivic("town_hall", new(6, 4));
        draft.PlaceCivic("terminal", new(12, 4));
        draft.PlaceCivic("dealership", new(20, 4));
        draft.ToggleParking(new(20, 10));
        draft.ToggleParking(new(21, 10));
        foreach (var cell in new[] { new Cell(6, 10), new(7, 10), new(8, 10), new(9, 10) }) draft.ToggleStall(cell);
        int row = 4;
        foreach (var item in MapDraft.ResourceItems)
        {
            draft.BeginResourceGroup(item);
            draft.PaintResource(new(32, row), item);
            draft.PaintResource(new(33, row), item);
            row += 2;
        }
        Assert.True(draft.PlaceStandardDistrict(new(10, 18), "north"));
        return draft;
    }

    static ScenarioDefinition Scenario(long cash = PreparedCash, bool buildable = false)
    {
        var profile = Profile();
        var problems = Draft().Check(profile);
        Assert.True(problems.Length == 0, problems);
        var scenario = profile.Apply(MapDesignReader.Read(Encoding.UTF8.GetBytes(Draft().ToJson()), profile));
        scenario = scenario with { Participants = scenario.Participants.Select(p => p with { CashMinor = cash }).ToArray() };
        if (!buildable) return scenario;
        var development = scenario.Economy!.Development!;
        scenario = scenario with
        {
            Labor = (scenario.Labor ?? new LaborSettings()) with { GrowthPeriodTicks = 1, GrowthPerPeriod = 5 },
            Economy = scenario.Economy with
            {
                Development = development with
                {
                    Industries = development.Industries.Select(i => i.Id != "woodworking" ? i : i with
                    {
                        Products = i.Products.Select(p => p with { XpPerUnit = 40000 }).ToImmutableArray()
                    }).ToImmutableArray()
                }
            },
            BuildingTypes = scenario.BuildingTypes!.Select(t => t.Id != "woodworking" ? t : t with
            {
                Levels = t.Levels.Select(l => l.Level switch
                {
                    1 => l with { Materials = [new("wood", 4)] },
                    2 => l with { Materials = [new("plank", 4)] },
                    _ => l
                }).ToArray()
            }).ToArray()
        };
        return scenario;
    }

    static GameSession Open(long cash = PreparedCash, bool buildable = false) => new(new SimulationEngine(Scenario(cash, buildable)));
    static long Cash(GameSession s, long actor = 1) => s.Engine.Observe(new(actor)).Account.BalanceMinor;
    static PlotView[] Plots(GameSession s, long actor = 1) => s.Engine.Observe(new(actor)).Plots;
    static IndustrialLot Lot(GameSession s, int index = 0) => s.Engine.Configuration().Layout!.IndustrialLots[index];
    static PlotView PlotAt(GameSession s, Cell cell) => Plots(s).Single(p => p.Position == cell);

    [Fact]
    public void DistrictHasEightCompleteParcelsWithAPublicEntrance()
    {
        var lots = IndustrialParcels.District(new(10, 18), "north");
        var roads = IndustrialParcels.DistrictRoads(new(10, 18), "north").ToHashSet();
        Assert.Equal(8, lots.Length);
        Assert.Equal(48, lots.SelectMany(l => l.Cells).Distinct().Count());
        Assert.All(lots, lot =>
        {
            Assert.Equal(6, lot.Cells.Distinct().Count());
            Assert.DoesNotContain(lot.Entry, lot.Cells);
            Assert.Contains(lot.Entry, roads);
            var frame = Assert.IsType<ParcelFrame>(IndustrialParcels.TryFrame(lot));
            Assert.Contains(frame.Facing, new[] { "north", "south" });
        });
        var scenario = Scenario();
        var layout = scenario.Layout!;
        Assert.Equal("map-adapter-4", layout.Version);
        Assert.Equal(8, layout.IndustrialLots.Length);
        Assert.All(layout.IndustrialLots, lot =>
        {
            Assert.True(IndustrialParcels.ReachesNetwork(layout, lot.Entry));
            Assert.All(lot.Cells, cell => Assert.False(layout.RoadCorridorCells.Contains(cell)));
        });
        var levels = scenario.BuildingTypes!.Single(t => t.Id == "woodworking").Levels.OrderBy(l => l.Level).ToArray();
        Assert.Equal(new[] { 1, 3, 4, 5, 6 }, levels.Select(l => l.Footprint.Length).ToArray());
        Assert.All(layout.IndustrialLots, lot =>
        {
            var frame = IndustrialParcels.TryFrame(lot)!.Value;
            Assert.All(levels, level =>
            {
                var cells = IndustrialParcels.MapFootprint(frame, level.Footprint);
                Assert.Equal(level.Footprint.Length, cells.Distinct().Count());
                Assert.All(cells, cell => Assert.Contains(cell, lot.Cells));
            });
        });
    }

    [Fact]
    public void NorthAndSouthAndSideFramesStayInsideTheParcel()
    {
        var north = new IndustrialLot("n", [new(0, 0), new(1, 0), new(0, 1), new(1, 1), new(0, 2), new(1, 2)], new(0, -1));
        var south = new IndustrialLot("s", north.Cells, new(0, 3));
        var west = new IndustrialLot("w", [new(0, 0), new(1, 0), new(2, 0), new(0, 1), new(1, 1), new(2, 1)], new(-1, 0));
        var east = new IndustrialLot("e", west.Cells, new(3, 0));
        var n = IndustrialParcels.TryFrame(north)!.Value;
        var s = IndustrialParcels.TryFrame(south)!.Value;
        Assert.Equal("north", n.Facing);
        Assert.Equal(new Cell(0, 0), n.Anchor);
        Assert.Equal(new Cell(0, 2), n.Map(0, 2));
        Assert.Equal((0, -1), (n.RoadX, n.RoadZ));
        Assert.Equal("south", s.Facing);
        Assert.Equal(new Cell(0, 2), s.Anchor);
        Assert.Equal(new Cell(0, 0), s.Map(0, 2));
        Assert.Equal(new Cell(1, 0), s.Map(1, 2));
        Assert.Equal((0, 1), (s.RoadX, s.RoadZ));
        Assert.Equal(new Cell(2, 0), IndustrialParcels.TryFrame(west)!.Value.Map(0, 2));
        Assert.Equal(new Cell(0, 0), IndustrialParcels.TryFrame(east)!.Value.Map(0, 2));
        var turned = IndustrialParcels.District(new(4, 4), "west");
        Assert.Equal(8, turned.Length);
        Assert.All(turned, lot => Assert.Contains(IndustrialParcels.TryFrame(lot)!.Value.Facing, new[] { "west", "east" }));
    }

    [Fact]
    public void PurchaseDebitsSixHundredCoinsAndOwnsAllSixCells()
    {
        var s = Open();
        var lot = Lot(s);
        var anchor = IndustrialParcels.TryFrame(lot)!.Value.Anchor;
        var plot = PlotAt(s, anchor);
        long before = Cash(s);
        StorageC1Tests.Move(s, anchor);
        StorageC1Tests.Step(s, new(ActionKind.BuyPlot, Plot: plot.Id));
        Assert.Equal(before - IndustrialParcelRules.PriceMinor, Cash(s));
        var owned = Plots(s).Where(p => lot.Cells.Contains(p.Position)).ToArray();
        Assert.Equal(6, owned.Length);
        Assert.All(owned, p => Assert.Equal(1, p.OwnerId?.Value));
        Assert.Equal(60000, s.Engine.EventsSince(0).Single(e => e.Type == "ParcelPurchased").AmountMinor);
        var neighbor = s.Engine.Configuration().Layout!.IndustrialLots[1];
        Assert.All(Plots(s).Where(p => neighbor.Cells.Contains(p.Position)), p => Assert.Null(p.OwnerId));
    }

    [Fact]
    public void ShortCashAndATakenCellCancelTheWholePurchase()
    {
        var poor = Open(10_000);
        var lot = Lot(poor);
        var anchor = IndustrialParcels.TryFrame(lot)!.Value.Anchor;
        StorageC1Tests.Move(poor, anchor);
        StorageC1Tests.Step(poor, new(ActionKind.BuyPlot, Plot: PlotAt(poor, anchor).Id), accepted: false);
        Assert.Equal("NoMoney", poor.Engine.Result(poor.CommandTape[^1].Command.CommandId)!.ReasonCode);
        Assert.Equal(10_000, Cash(poor));
        Assert.All(Plots(poor).Where(p => lot.Cells.Contains(p.Position)), p => Assert.Null(p.OwnerId));
        var s = Open();
        var owned = Lot(s);
        var taken = IndustrialParcels.TryFrame(owned)!.Value.Anchor;
        Buy(s, owned);
        StorageC1Tests.Move(s, taken, 2);
        StorageC1Tests.Step(s, new(ActionKind.BuyPlot, Plot: PlotAt(s, taken).Id), actor: 2, accepted: false);
        Assert.Equal("ParcelOccupied", s.Engine.Result(s.CommandTape[^1].Command.CommandId)!.ReasonCode);
        Assert.Equal(PreparedCash, Cash(s, 2));
        Assert.All(Plots(s).Where(p => owned.Cells.Contains(p.Position)), p => Assert.Equal(1, p.OwnerId?.Value));
    }

    [Fact]
    public void CompetingPurchasesLeaveASingleOwnerAndReplayAgrees()
    {
        var s = Open();
        var lot = Lot(s);
        var anchor = IndustrialParcels.TryFrame(lot)!.Value.Anchor;
        var plot = PlotAt(s, anchor).Id;
        StorageC1Tests.Move(s, anchor, 1);
        StorageC1Tests.Move(s, anchor, 2);
        string first = s.Send(new(1), new(ActionKind.BuyPlot, Plot: plot));
        string second = s.Send(new(2), new(ActionKind.BuyPlot, Plot: plot));
        s.Step();
        var results = new[] { s.Engine.Result(first)!, s.Engine.Result(second)! };
        Assert.Equal(1, results.Count(r => r.Status == ResultStatus.Accepted));
        Assert.Equal("ParcelOccupied", results.Single(r => r.Status == ResultStatus.Rejected).ReasonCode);
        long winner = Plots(s).Single(p => p.Position == anchor).OwnerId!.Value.Value;
        Assert.All(Plots(s).Where(p => lot.Cells.Contains(p.Position)), p => Assert.Equal(winner, p.OwnerId?.Value));
        Assert.Equal(PreparedCash, Cash(s, winner == 1 ? 2 : 1));
        var replay = GameSession.Replay(s.Engine.Configuration(), s.CommandTape, s.Engine.Tick);
        Assert.Equal(s.Engine.ComputeStateHash(), replay.Engine.ComputeStateHash());
    }

    [Fact]
    public void BuildingTurnsTowardTheRoadAndExpandsInsideTheParcel()
    {
        var s = Open(buildable: true);
        var lot = s.Engine.Configuration().Layout!.IndustrialLots.First(l => IndustrialParcels.TryFrame(l)!.Value.Facing == "south");
        var frame = IndustrialParcels.TryFrame(lot)!.Value;
        Buy(s, lot);
        StorageC1Tests.Move(s, new(32, 4));
        StorageC1Tests.Step(s, new(ActionKind.Harvest, ItemId: "wood", Quantity: 8));
        StorageC1Tests.Idle(s);
        var ground = s.Engine.Inspect().Inventories.Single(i => i.Position == frame.Anchor && i.OwnerId == new ParticipantId(1));
        StorageC1Tests.Put(s, ground.Id, "wood", 4);
        StorageC1Tests.Step(s, new(ActionKind.Build, BuildingTypeId: "woodworking", Target: frame.Anchor));
        var project = s.Engine.DevelopmentOf(new(1))!.Projects.Single();
        s.AdvanceTo(project.CompletesTick);
        var building = s.Engine.Observe(new(1)).OwnBuildings.Single();
        Assert.Equal(new[] { frame.Anchor }, building.Footprint);
        Assert.Equal((frame.RoadX, frame.RoadZ), IndustrialParcels.FacingOf(s.Engine.Configuration(), building.Position));
        var view = s.Engine.Observe(new(1));
        StorageC1Tests.Put(s, view.OwnEnterprises.Single().InputId, "wood", 2);
        StorageC1Tests.Move(s, frame.Anchor);
        StorageC1Tests.Step(s, new(ActionKind.SetProductionEnabled, Enterprise: view.OwnEnterprises.Single().Id, Enabled: true));
        for (int i = 0; i < 400 && s.Engine.Observe(new(1)).OwnEnterprises.Single().Staff == 0; i++) s.Step();
        var hired = s.Engine.Observe(new(1)).OwnEnterprises.Single();
        Assert.True(hired.Staff > 0, hired.ReasonCode);
        for (int i = 0; i < 400 && s.Engine.Observe(new(1)).OwnEnterprises.Single().CompletedBatches == 0; i++) s.Step();
        Assert.Equal(1, s.Engine.Observe(new(1)).OwnEnterprises.Single().CompletedBatches);
        StorageC1Tests.Step(s, new(ActionKind.SetProductionEnabled, Enterprise: view.OwnEnterprises.Single().Id, Enabled: false));
        var made = s.Engine.Observe(new(1)).OwnEnterprises.Single();
        var carrier = s.Engine.Observe(new(1)).Participant.InventoryId;
        StorageC1Tests.Move(s, frame.Anchor);
        StorageC1Tests.Step(s, new(ActionKind.Transfer, Source: made.OutputId, Destination: carrier, ItemId: "plank", Quantity: 4));
        StorageC1Tests.Idle(s);
        StorageC1Tests.Step(s, new(ActionKind.Transfer, Source: carrier, Destination: made.InputId, ItemId: "plank", Quantity: 4));
        StorageC1Tests.Idle(s);
        StorageC1Tests.Step(s, new(ActionKind.UpgradeBuilding, Building: building.Id, Quantity: 2));
        var upgrade = s.Engine.DevelopmentOf(new(1))!.Projects.Single();
        s.AdvanceTo(upgrade.CompletesTick);
        var grown = s.Engine.Observe(new(1)).OwnBuildings.Single();
        Assert.Equal(2, grown.Level);
        Assert.Equal(3, grown.Footprint.Length);
        Assert.All(grown.Footprint, cell => Assert.Contains(cell, lot.Cells));
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        Assert.Equal(grown.Footprint, loaded.Engine.Observe(new(1)).OwnBuildings.Single().Footprint);
        Assert.Equal((frame.RoadX, frame.RoadZ), IndustrialParcels.FacingOf(loaded.Engine.Configuration(), grown.Position));
        var replay = GameSession.Replay(s.Engine.Configuration(), s.CommandTape, s.Engine.Tick);
        Assert.Equal(s.Engine.ComputeStateHash(), replay.Engine.ComputeStateHash());
    }

    [Fact]
    public void ARoadCannotCrossAnOwnedParcelOrANeighborEntrance()
    {
        var s = Open();
        var lots = s.Engine.Configuration().Layout!.IndustrialLots;
        Buy(s, lots[0]);
        var cell = lots[0].Cells[0];
        Assert.All(lots, lot => Assert.True(IndustrialParcels.ReachesNetwork(s.Engine.Configuration().Layout!, lot.Entry)));
        int supply = s.Engine.Configuration().Economy!.Fleet!.SupplyTicks;
        for (int i = 0; i < 40 && s.Engine.FleetOf(new(1))!.Stock.All(v => v.ModelId != "trakt_d1"); i++) s.AdvanceTo(s.Engine.Tick + supply);
        Assert.Contains(s.Engine.FleetOf(new(1))!.Stock, v => v.ModelId == "trakt_d1");
        var paver = FleetE1Tests.Buy(s, "trakt_d1");
        FleetE1Tests.Board(s, paver);
        StorageC1Tests.Step(s, new(ActionKind.SetRoadWork, VehicleId: paver.Id, EquipmentLevel: 1, Mode: EquipmentMode.Personal, Enabled: true, Stops: [cell]), accepted: false);
        Assert.Equal("InvalidRoadRoute", s.Engine.Result(s.CommandTape[^1].Command.CommandId)!.ReasonCode);
        Assert.All(Plots(s).Where(p => lots[0].Cells.Contains(p.Position)), p => Assert.Equal(1, p.OwnerId?.Value));
        Assert.Contains(lots[1].Entry, s.Engine.Configuration().Layout!.RoadCorridorCells);
    }

    [Fact]
    public void OrdinaryStartStaysOnResourceRightsAndOldMapsBuyOneCell()
    {
        var ordinary = I6C2MapsTests.Economy(PlayMaps.CompactValley);
        Assert.Equal(VNextResourceRightsProfile.ProfileId, ordinary.Id);
        Assert.Null(ordinary.World.Rules.IndustrialParcels);
        Assert.Equal(10_000, ordinary.World.PlotPriceMinor);
        var saved = JsonSerializer.Serialize(ordinary.World.Rules, SimulationJson.Options);
        Assert.DoesNotContain("industrialParcels", saved, StringComparison.OrdinalIgnoreCase);
        var restored = JsonSerializer.Deserialize<EconomicRules>(saved, SimulationJson.Options);
        Assert.Null(restored!.IndustrialParcels);
        var s = I6C2MapsTests.Session(PlayMaps.CompactValley, funded: true);
        var plot = s.Engine.Observe(new(1)).Plots.First(p => p.OwnerId is null && p.ShopParticipantId is null && !p.IsShopSite);
        long before = Cash(s);
        var ownedBefore = s.Engine.Observe(new(1)).Plots.Count(p => p.OwnerId?.Value == 1);
        StorageC1Tests.Move(s, plot.Position);
        StorageC1Tests.Step(s, new(ActionKind.BuyPlot, Plot: plot.Id));
        Assert.Equal(before - 10_000, Cash(s));
        Assert.Equal(ownedBefore + 1, s.Engine.Observe(new(1)).Plots.Count(p => p.OwnerId?.Value == 1));
        var copy = CheckpointStore.Decode(CheckpointStore.Encode(s));
        Assert.Equal(s.Engine.ComputeStateHash(), copy.Engine.ComputeStateHash());
    }

    [Fact]
    public void ResourceRightsStillAllowHarvestWithoutBuyingTheCell()
    {
        var s = Open();
        Assert.Equal(1, s.Engine.Configuration().Economy!.ResourceRights!.Version);
        var wood = s.Engine.Configuration().ResourceNodes.First(n => n.ItemId == "wood" && n.Position == new Cell(32, 4));
        StorageC1Tests.Move(s, wood.Position);
        StorageC1Tests.Step(s, new(ActionKind.Harvest, ItemId: "wood", Quantity: 1));
        StorageC1Tests.Idle(s);
        Assert.Equal(1, s.Engine.Observe(new(1)).OwnInventories.SelectMany(i => i.Stock).Where(i => i.ItemId == "wood").Sum(i => i.Quantity));
        Assert.DoesNotContain(s.Engine.Observe(new(1)).Plots, p => p.Position == wood.Position && p.OwnerId is not null);
    }

    [Fact]
    public void AnAlgorithmicNpcPricesOneWholeParcel()
    {
        var s = Open();
        var settings = AlgorithmicController.WithEconomy(new(2, "wood"), Profile().Npc);
        var npc = new AlgorithmicController(settings, AlgorithmicController.RuntimeSeed(s.Engine.Configuration().Seed, 2), s.Engine.Configuration().Participants.Select(p => p.Id));
        PlanIndustry(npc, "woodworking");
        var observation = s.Engine.ObserveController(new(2));
        var listed = Candidates(npc, observation);
        var factory = listed.FirstOrDefault(c => c.goal == "BuildProduction");
        Assert.True(factory.goal == "BuildProduction", string.Join(",", listed.Select(c => c.goal).Distinct()));
        var buys = factory.steps.Where(a => a.Kind == ActionKind.BuyPlot).ToArray();
        Assert.Single(buys);
        _ = npc.Decide(observation, []);
        var plot = s.Engine.Observe(new(2)).Plots.Single(p => p.Id == buys[0].Plot);
        var lot = s.Engine.Configuration().Layout!.IndustrialLots.Single(l => l.Cells.Contains(plot.Position));
        Assert.Equal(IndustrialParcels.TryFrame(lot)!.Value.Anchor, plot.Position);
        Assert.Equal(IndustrialParcelRules.PriceMinor, AlgorithmicController.ParcelPrice(s.Engine.ObserveController(new(2)), plot));
        s.AttachControllers([npc]);
        for (int i = 0; i < 30; i++) s.Step();
        foreach (var candidate in s.Engine.Configuration().Layout!.IndustrialLots)
        {
            int owned = Plots(s, 2).Count(p => candidate.Cells.Contains(p.Position) && p.OwnerId is not null);
            Assert.True(owned is 0 or 6);
        }
    }

    [Fact]
    public void TheButtonNamesTheWholeParcelAndTheEditorKeepsPartialLotsBlocked()
    {
        var s = Open();
        var lot = Lot(s);
        var anchor = IndustrialParcels.TryFrame(lot)!.Value.Anchor;
        StorageC1Tests.Move(s, anchor);
        var choice = PlayContext.At(PlayerDesk.Read(s.Engine, new(1)), s.Engine.Configuration(), "ru").Single(c => c.Key.StartsWith("buy:", StringComparison.Ordinal));
        Assert.Equal("Купить участок — 600 монет", choice.Label);
        Assert.Equal("6 клеток · подъезд к дороге · место для полного развития", choice.Details);
        var english = PlayContext.At(PlayerDesk.Read(s.Engine, new(1)), s.Engine.Configuration(), "en").Single(c => c.Key.StartsWith("buy:", StringComparison.Ordinal));
        Assert.Equal("Buy parcel — 600 coins", english.Label);
        Assert.Equal("6 cells · road entrance · room to develop fully", english.Details);
        var poor = Open(10_000);
        StorageC1Tests.Move(poor, anchor);
        var denied = PlayContext.At(PlayerDesk.Read(poor.Engine, new(1)), poor.Engine.Configuration(), "ru").Single(c => c.Key.StartsWith("buy:", StringComparison.Ordinal));
        Assert.False(denied.Enabled);
        Assert.Contains("Нужно 600 монет.", denied.Details, StringComparison.Ordinal);
        var draft = Draft();
        draft.AddLotCell(new(30, 26));
        Assert.Equal("delsk-map-design-4", draft.SchemaVersion);
        Assert.Contains("целым 2×3", draft.Check(Profile()), StringComparison.Ordinal);
        draft = Draft();
        draft.PaintRoad(new(38, 26));
        Assert.Contains(draft.Review(Profile()), n => n.Warning && n.Text.Contains("Изолированная дорога", StringComparison.Ordinal));
    }

    [Fact]
    public void PublishedMapMatchesTheDraft()
    {
        var json = Draft().ToJson();
        var path = Repo("docs", "vnext", "maps", "industrial_block_i6c8a.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path)) File.WriteAllText(path, json);
        Assert.Equal(json, File.ReadAllText(path));
        var read = MapDesignReader.Read(Encoding.UTF8.GetBytes(File.ReadAllText(path)), Profile());
        Assert.Equal("industrial_block_i6c8a", read.Id);
        Assert.Equal("map-adapter-4", read.Layout!.Version);
        Assert.Null(I6C2MapsTests.Economy(PlayMaps.CompactValley).World.Rules.IndustrialParcels);
    }

    static void Buy(GameSession s, IndustrialLot lot, long actor = 1)
    {
        var anchor = IndustrialParcels.TryFrame(lot)!.Value.Anchor;
        StorageC1Tests.Move(s, anchor, actor);
        StorageC1Tests.Step(s, new(ActionKind.BuyPlot, Plot: PlotAt(s, anchor).Id), actor);
    }

    static (string goal, GameAction[] steps)[] Candidates(AlgorithmicController npc, ControllerObservation observation)
    {
        var found = typeof(AlgorithmicController).GetMethod("DecisionCandidates", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(npc, [observation])!;
        return ((System.Collections.IEnumerable)found).Cast<object>().Select(candidate =>
        {
            var steps = candidate.GetType().GetProperty("Steps")!.GetValue(candidate)!;
            return ((string)candidate.GetType().GetProperty("Goal")!.GetValue(candidate)!,
                ((System.Collections.IEnumerable)steps).Cast<object>().Select(step => (GameAction)step.GetType().GetProperty("Action")!.GetValue(step)!).ToArray());
        }).ToArray();
    }

    static void PlanIndustry(AlgorithmicController npc, string industry)
    {
        var field = typeof(AlgorithmicController).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var state = field.GetValue(npc)!;
        var clone = state.GetType().GetMethod("<Clone>$")!.Invoke(state, null)!;
        var memory = (EconomyMemory)clone.GetType().GetProperty("EconomyMemory")!.GetValue(clone)!;
        clone.GetType().GetProperty("EconomyMemory")!.SetValue(clone, memory with { ConstructionIndustry = industry });
        field.SetValue(npc, clone);
    }

    static GameAction[] Plan(AlgorithmicController npc)
    {
        var state = typeof(AlgorithmicController).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(npc)!;
        var plan = state.GetType().GetProperty("Plan")!.GetValue(state)!;
        return ((System.Collections.IEnumerable)plan).Cast<object>()
            .Select(step => (GameAction)step.GetType().GetProperty("Action")!.GetValue(step)!)
            .ToArray();
    }

    static string Repo(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Delsk.sln"))) dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("repository root");
        return Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
    }
}
