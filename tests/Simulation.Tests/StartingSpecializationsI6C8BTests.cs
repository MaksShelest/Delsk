using System.Text;
using System.Text.Json;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class StartingSpecializationsI6C8BTests
{
    private static EconomyProfile Profile() => VNextStartingSpecializationProfile.Create(I6C2MapsTests.Economy(PlayMaps.CompactValley));
    private static OrdinaryGameResolution Open(string? industry = "woodworking", ulong seed = 42, bool disable = false, bool preview = false, StartingSpecializationAssignment[]? roster = null, string map = PlayMaps.CompactValley)
    {
        string file = map == PlayMaps.Second ? "woodland_region_i6c4.json" : "compact_valley_i6c21.json";
        var template = MapDesignReader.Read(Encoding.UTF8.GetBytes(OrdinaryNewGameTests.Text(file)), Profile());
        return OrdinaryNewGame.Resolve(template, Profile(), new(3, Seed: seed, StartingIndustry: disable || preview || roster is not null ? null : industry, DisableStartingSpecializations: disable, AllowUnassigned: preview, SpecializationRoster: roster), vnextBalance: MapDesignTests.Balance());
    }
    private static string Industry(ScenarioDefinition scenario, long actor) => scenario.StartingSpecializationRoster!.Single(r => r.ActorId == actor).IndustryId;
    private static long Balance(GameSession session, long actor = 1) => session.Engine.Observe(new(actor)).Account.BalanceMinor;
    private static (string Reason, long Amount)[] Money(GameSession session) => session.Engine.Ledger().Select(r => (r.Reason, r.AmountMinor)).ToArray();

    [Fact]
    public void ProfileKeepsPricesWagesAndLeavesParcelsForLater()
    {
        var profile = Profile();
        var round = EconomyProfile.Read(Encoding.UTF8.GetString(VNextSupportedProfile.Serialize(profile)));
        Assert.Equal(VNextStartingSpecializationProfile.ProfileId, round.Id);
        Assert.Equal("starting-specializations-projection-1", round.VNext!.ProjectionVersion);
        Assert.Equal(1, round.World.Rules.StartingSpecializations!.Version);
        Assert.True(round.World.Rules.StartingSpecializations.Enabled);
        Assert.Null(round.World.Rules.IndustrialParcels);
        Assert.Equal(1, round.World.Rules.ResourceRights!.Version);
        Assert.Equal(2, round.World.Rules.PlayerWageStepMinor);
        Assert.Equal(15, round.World.Rules.Fleet!.WageMinor);
        Assert.Equal(10_000, round.World.PlotPriceMinor);
        Assert.Equal(20_000, round.World.StartingCashMinor);
        Assert.Throws<ArgumentException>(() => VNextStartingSpecializationProfile.Create(VNextIndustrialParcelProfile.Create(I6C2MapsTests.Economy(PlayMaps.CompactValley))));
        var old = I6C2MapsTests.Resolve(PlayMaps.CompactValley).Scenario;
        Assert.Null(old.Economy!.StartingSpecializations);
        Assert.Null(old.StartingSpecializationRoster);
        Assert.Null(old.Economy.IndustrialParcels);
    }

    [Theory]
    [InlineData("woodworking")]
    [InlineData("food")]
    [InlineData("dairy")]
    [InlineData("materials")]
    public void EachDirectionIsStoredForTheHumanOnly(string industry)
    {
        var scenario = Open(industry).Scenario;
        Assert.Equal(industry, Industry(scenario, 1));
        Assert.Equal(4, scenario.StartingSpecializationRoster!.Length);
        Assert.All(scenario.StartingSpecializationRoster.Where(r => r.ActorId != 1), row => Assert.Contains(row.IndustryId, StartingSpecializations.Directions.Select(d => d.IndustryId)));
        var session = new GameSession(new(scenario));
        Assert.Equal(industry, session.Engine.SpecializationChoices().Single(s => s.ActorId == 1).IndustryId);
        Assert.Equal(1, session.Engine.SpecializationChoices().Single(s => s.ActorId == 1).SupportSlotsRemaining);
        Assert.Equal(industry, session.Engine.ObserveController(new(1)).Economics!.StartingIndustry);
        Assert.All(session.Engine.SpecializationChoices().Where(s => s.ActorId != 1), row => Assert.Equal(row.IndustryId, session.Engine.ObserveController(new(row.ActorId)).Economics!.StartingIndustry));
    }

    [Fact]
    public void OneChoiceDoesNotRewriteAnotherAndASecondChoiceAddsNothing()
    {
        var roster = new StartingSpecializationAssignment[] { new(1, "woodworking"), new(2, "food"), new(3, "dairy"), new(4, "materials") };
        var scenario = Open(roster: roster).Scenario;
        var left = new GameSession(new(scenario));
        var right = new GameSession(new(scenario));
        long before = Balance(left);
        var ledger = Money(left);
        string id = left.Send(new(2), new(ActionKind.ChooseStartingSpecialization, BuildingTypeId: "woodworking"));
        left.Step();
        right.Step();
        Assert.Equal(ResultStatus.Rejected, left.Engine.Result(id)!.Status);
        Assert.Equal("SpecializationAlreadyChosen", left.Engine.Result(id)!.ReasonCode);
        Assert.Equal(new[] { "woodworking", "food", "dairy", "materials" }, left.Engine.SpecializationChoices().OrderBy(s => s.ActorId).Select(s => s.IndustryId).ToArray());
        Assert.All(left.Engine.SpecializationChoices(), row => Assert.Equal(1, row.SupportSlotsRemaining));
        Assert.Equal(Balance(right), Balance(left));
        Assert.Equal(Money(right), Money(left));
        Assert.Equal(before, Balance(left));
        Assert.Equal(ledger, Money(left));
    }

    [Fact]
    public void CommandChoosesEachDirectionWithoutALedgerLine()
    {
        foreach (var (actor, industry) in new[] { (1L, "woodworking"), (2L, "food"), (3L, "dairy"), (4L, "materials") })
        {
            var scenario = Open(preview: true).Scenario;
            var left = new GameSession(new(scenario));
            var right = new GameSession(new(scenario));
            string id = left.Send(new(actor), new(ActionKind.ChooseStartingSpecialization, BuildingTypeId: industry));
            left.Step();
            right.Step();
            Assert.Equal(ResultStatus.Accepted, left.Engine.Result(id)!.Status);
            Assert.Equal(industry, left.Engine.SpecializationChoices().Single().IndustryId);
            Assert.Equal(1, left.Engine.SpecializationChoices().Single().SupportSlotsRemaining);
            Assert.Empty(right.Engine.SpecializationChoices());
            Assert.Equal(Balance(right, actor), Balance(left, actor));
            Assert.Equal(Money(right), Money(left));
            Assert.DoesNotContain(Money(left), row => row.Reason.Contains("Special", StringComparison.Ordinal) || row.Reason.Contains("Subsidy", StringComparison.Ordinal) || row.Reason.Contains("Grant", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void SaveLoadAndReplayRestoreTheSameChoice()
    {
        var scenario = Open("dairy", seed: 7).Scenario;
        var session = new GameSession(new(scenario));
        session.Step();
        string hash = session.Engine.ComputeStateHash();
        var choices = session.Engine.SpecializationChoices();
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(session));
        Assert.Equal(hash, loaded.Engine.ComputeStateHash());
        Assert.Equal(choices, loaded.Engine.SpecializationChoices());
        Assert.Equal("dairy", loaded.Engine.ObserveController(new(1)).Economics!.StartingIndustry);
        var replay = GameSession.Replay(scenario, session.CommandTape, session.Engine.Tick);
        Assert.Equal(hash, replay.Engine.ComputeStateHash());
        Assert.Equal(choices, replay.Engine.SpecializationChoices());
        Assert.Equal(32, session.Engine.ActiveCheckpointSchemaVersion);
        Assert.Contains("\"schemaVersion\":32", session.Engine.CreateCheckpointJson(), StringComparison.Ordinal);
        Assert.Contains("startingSpecializations", session.Engine.CreateCheckpointJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void NpcDirectionsAreReproducibleAndIgnoreCollectionOrder()
    {
        var first = Open("woodworking", seed: 42).Scenario;
        var second = Open("woodworking", seed: 42).Scenario;
        Assert.Equal(first.StartingSpecializationRoster, second.StartingSpecializationRoster);
        var reversed = first with { ResourceNodes = first.ResourceNodes.Reverse().ToArray() };
        foreach (long actor in new[] { 2L, 3L, 4L })
        {
            string? forward = StartingSpecializations.ChooseNpc(first, actor);
            Assert.Equal(forward, StartingSpecializations.ChooseNpc(reversed, actor));
            Assert.Equal(forward, Industry(first, actor));
            Assert.Contains(forward, StartingSpecializations.Directions.Select(d => d.IndustryId));
        }
        var barren = first with { ResourceNodes = first.ResourceNodes.Where(n => !StartingSpecializations.Directions.Any(d => d.InputItem == n.ItemId)).ToArray() };
        Assert.Null(StartingSpecializations.ChooseNpc(barren, 2));
        var noMilk = first with { ResourceNodes = first.ResourceNodes.Where(n => n.ItemId != "milk").ToArray() };
        Assert.NotEqual("dairy", StartingSpecializations.ChooseNpc(noMilk, 2));
        Assert.NotEqual("dairy", StartingSpecializations.ChooseNpc(noMilk, 3));
        Assert.NotEqual("dairy", StartingSpecializations.ChooseNpc(noMilk, 4));
        var woodland = Open("food", seed: 42, map: PlayMaps.Second).Scenario;
        Assert.Equal(woodland.StartingSpecializationRoster, Open("food", seed: 42, map: PlayMaps.Second).Scenario.StartingSpecializationRoster);
        foreach (var map in new[] { first, woodland })
            Assert.All(StartingSpecializations.Directions, direction => Assert.Contains(map.ResourceNodes, node => node.ItemId == direction.InputItem));
    }

    [Fact]
    public void OldProfilesAndAnExplicitDisableKeepThePreviousRules()
    {
        var historical = I6C2MapsTests.Resolve(PlayMaps.CompactValley);
        var session = new GameSession(new(historical.Scenario));
        string hash = session.Engine.ComputeStateHash();
        string id = session.Send(new(1), new(ActionKind.ChooseStartingSpecialization, BuildingTypeId: "food"));
        session.Step();
        Assert.Equal("SpecializationDisabled", session.Engine.Result(id)!.ReasonCode);
        Assert.Empty(session.Engine.SpecializationChoices());
        Assert.Null(session.Engine.ObserveController(new(1)).Economics!.StartingIndustry);
        Assert.DoesNotContain("startingSpecializations", session.Engine.CreateCheckpointJson(), StringComparison.Ordinal);
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(session));
        Assert.Equal(session.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        Assert.NotEqual(hash, session.Engine.ComputeStateHash());
        var disabled = Open(disable: true).Scenario;
        Assert.False(disabled.Economy!.StartingSpecializations!.Enabled);
        Assert.Null(disabled.StartingSpecializationRoster);
        Assert.Null(disabled.Economy.IndustrialParcels);
        var disabledSession = new GameSession(new(disabled));
        string denied = disabledSession.Send(new(1), new(ActionKind.ChooseStartingSpecialization, BuildingTypeId: "dairy"));
        disabledSession.Step();
        Assert.Equal("SpecializationDisabled", disabledSession.Engine.Result(denied)!.ReasonCode);
        Assert.Empty(disabledSession.Engine.SpecializationChoices());
        Assert.Throws<ArgumentException>(() => Open(industry: "metallurgy"));
        Assert.Throws<ArgumentException>(() => OrdinaryNewGame.Resolve(historical.Scenario, I6C2MapsTests.Economy(PlayMaps.CompactValley), new(3, StartingIndustry: "food"), vnextBalance: MapDesignTests.Balance()));
    }

    [Fact]
    public void PlannerUsesAFeasibleSpecializationAndDropsAnImpossibleOne()
    {
        var resolution = Open("materials", seed: 11);
        var scenario = resolution.Scenario;
        var config = OrdinaryNewGameTests.Config().ForOrdinary(scenario, 1, resolution.Controllers);
        var session = new GameSession(new(scenario));
        foreach (var npc in config.Npcs)
        {
            var controller = new AlgorithmicController(AlgorithmicController.WithEconomy(npc, Profile().Npc), 1, scenario.Participants.Select(a => a.Id));
            var observation = session.Engine.ObserveController(new(npc.ActorId), economicPlanning: true);
            string own = observation.Economics!.StartingIndustry!;
            string? planned = controller.PreferredNewEnterprise(observation);
            bool feasible = observation.BuildingTypes.Any(t => t.Id == own && t.Kind == BuildingKind.Production && t.Levels[0].UnlockedRecipes.Any(id =>
            {
                var recipe = observation.Map.Recipes.Single(r => r.Id == id);
                return recipe.Ingredients.All(i => observation.Map.ResourceNodes.Any(n => n.ItemId == i.ItemId) || observation.ProductionContent?.Rules.Imports.Any(stock => stock.ItemId == i.ItemId) == true);
            }));
            if (feasible) Assert.Equal(own, planned);
            else Assert.NotEqual(own, planned);
        }
        var second = config.Npcs.Single(n => n.ActorId == 2);
        var controllerOnly = new AlgorithmicController(AlgorithmicController.WithEconomy(second, Profile().Npc), 1, scenario.Participants.Select(a => a.Id));
        var view = session.Engine.ObserveController(new(2), economicPlanning: true);
        view = view with { Map = view.Map with { ResourceNodes = view.Map.ResourceNodes.RemoveAll(n => n.ItemId == "stone") } };
        string? fallback = controllerOnly.PreferredNewEnterprise(view);
        Assert.NotEqual("materials", fallback);
    }

    [Fact]
    public void OrdinaryStartStillBuysOneCellAndAnotherIndustryRemainsBuildable()
    {
        // Деньги сверх 200 монет подготовлены только для стройки. Специализация их не начисляет.
        var draft = MapDraft.Parse(OrdinaryNewGameTests.Text("compact_valley_i6c21.json"));
        var free = FreeCell(draft);
        var lotId = draft.AddLotCell(free.Cell)!;
        draft.SetEntry(lotId, free.Road);
        draft.Id = UserMaps.SuggestId(draft.Name, []);
        var resolved = UserMaps.Start(draft, Profile(), new(3, StartingIndustry: "woodworking"), MapDesignTests.Balance());
        Assert.Equal(VNextStartingSpecializationProfile.ProfileId, resolved.Scenario.EconomyProfileId);
        Assert.Null(resolved.Scenario.Economy!.IndustrialParcels);
        Assert.Equal(20_000, resolved.Scenario.Participants.Single(p => p.Id == 1).CashMinor);
        Assert.Equal("woodworking", Industry(resolved.Scenario, 1));
        var scenario = resolved.Scenario with { Participants = resolved.Scenario.Participants.Select(p => p.Id == 1 ? p with { CashMinor = 2_000_000 } : p).ToArray() };
        var session = new GameSession(new(scenario));
        var plot = scenario.Plots!.Single(p => p.Position == free.Cell);
        Assert.Equal(10_000, plot.PriceMinor);
        StorageC1Tests.Move(session, free.Cell);
        long before = Balance(session);
        StorageC1Tests.Step(session, new(ActionKind.BuyPlot, Plot: new(plot.Id)));
        Assert.Equal(10_000, before - Balance(session));
        Assert.Single(session.Engine.Observe(new(1)).Plots, p => p.OwnerId == new ParticipantId(1) && p.Position == free.Cell);
        var level = scenario.BuildingTypes!.Single(t => t.Id == "food").Levels.Single(l => l.Level == 1);
        long wood = level.Materials.Single(m => m.ItemId == "wood").Quantity;
        long steel = level.Materials.Single(m => m.ItemId == "steel").Quantity;
        var ground = new InventoryId(session.Engine.StorageOf(new(1)).Ground.Single(g => g.PlotId == plot.Id).InventoryId);
        var node = scenario.ResourceNodes.Where(n => n.ItemId == "wood" && n.Rules!.Public).OrderBy(n => Math.Abs(n.Position.X - free.Cell.X) + Math.Abs(n.Position.Z - free.Cell.Z)).First();
        long volume = scenario.Items.Single(i => i.Id == "wood").UnitVolume;
        long chunk = Math.Max(1, scenario.Participants[0].InventoryCapacity / volume);
        for (long left = wood; left > 0; left -= chunk)
        {
            long take = Math.Min(chunk, left);
            if (session.Engine.ResourceSites().Single(r => r.Position == node.Position).Stock < take) session.AdvanceTo(session.Engine.Tick + take * 40);
            StorageC1Tests.Move(session, node.Position);
            StorageC1Tests.Step(session, new(ActionKind.Harvest, ItemId: "wood", Quantity: take));
            StorageC1Tests.Idle(session);
            StorageC1Tests.Put(session, ground, "wood", take);
        }
        StorageC1Tests.Move(session, scenario.CapitalDemand!.Terminal);
        StorageC1Tests.Step(session, new(ActionKind.BuyImport, ItemId: "steel", Quantity: steel));
        var pickup = session.Engine.ProductionContentOf(new(1))!.Pickup!.Value;
        StorageC1Tests.Step(session, new(ActionKind.Transfer, Source: pickup, Destination: StorageC1Tests.Carrier(session), ItemId: "steel", Quantity: steel));
        StorageC1Tests.Idle(session);
        StorageC1Tests.Put(session, ground, "steel", steel);
        StorageC1Tests.Move(session, free.Cell);
        StorageC1Tests.Step(session, new(ActionKind.Build, BuildingTypeId: "food", Target: free.Cell));
        var project = session.Engine.StorageOf(new(1)).Construction.Single();
        session.AdvanceTo(project.CompletesTick);
        Assert.Equal("food", session.Engine.Observe(new(1)).OwnBuildings.Single(b => b.Kind == BuildingKind.Production).TypeId);
        Assert.Equal("woodworking", session.Engine.SpecializationChoices().Single(s => s.ActorId == 1).IndustryId);
    }

    [Theory]
    [InlineData(PlayMaps.CompactValley)]
    [InlineData(PlayMaps.Second)]
    public void FourStarterChainsStayPayableOnlyAtOrdinaryPrices(string map)
    {
        var scenario = Open("woodworking", map: map).Scenario;
        var steel = scenario.Economy!.ProductionContent!.Imports.Single(i => i.ItemId == "steel");
        Assert.Equal(3125, steel.UnitPriceMinor);
        Assert.NotNull(scenario.CapitalDemand);
        foreach (var direction in StartingSpecializations.Directions)
        {
            var type = scenario.BuildingTypes!.Single(t => t.Id == direction.IndustryId);
            var level = type.Levels.Single(l => l.Level == 1);
            var recipe = scenario.Recipes.Single(r => r.Id == direction.RecipeId);
            Assert.Contains(direction.RecipeId, level.UnlockedRecipes);
            Assert.Equal(direction.OutputItem, recipe.OutputItem);
            Assert.Contains(recipe.Ingredients, i => i.ItemId == direction.InputItem);
            Assert.DoesNotContain(level.Materials, m => m.ItemId == direction.OutputItem);
            Assert.Equal(5, level.StaffCapacity);
            Assert.Equal(15, level.WageMinor);
            Assert.Contains(scenario.ResourceNodes, n => n.ItemId == direction.InputItem);
            Assert.True(scenario.Items.Single(i => i.Id == direction.OutputItem).BasePriceMinor > 0);
            long shell = 10_000 + level.PriceMinor + level.Materials.Where(m => m.ItemId == "steel").Sum(m => m.Quantity * steel.UnitPriceMinor);
            Assert.True(shell > 20_000);
        }
        var food = scenario.BuildingTypes!.Single(t => t.Id == "food").Levels.Single(l => l.Level == 1);
        Assert.Equal("bake_bread", food.RecipeId);
        Assert.Contains("mill_flour", food.AdditionalRecipeIds);
        var saw = scenario.BuildingTypes!.Single(t => t.Id == "woodworking");
        Assert.Contains(saw.WorkshopOutfit!.Materials, m => m.ItemId == "plank");
        Assert.DoesNotContain(saw.Levels.Single(l => l.Level == 1).Materials, m => m.ItemId == "plank");
        Assert.Equal(17_000, saw.Levels.Single(l => l.Level == 1).PriceMinor);
        Assert.Equal(19_000, scenario.BuildingTypes!.Single(t => t.Id == "dairy").Levels.Single(l => l.Level == 1).PriceMinor);
        Assert.Equal(19_000, scenario.BuildingTypes!.Single(t => t.Id == "materials").Levels.Single(l => l.Level == 1).PriceMinor);
        Assert.DoesNotContain(StartingSpecializations.Directions, d => d.IndustryId is "metallurgy" or "machinery");
    }

    private static (Cell Cell, Cell Road) FreeCell(MapDraft draft)
    {
        var blocked = draft.Roads.Concat(draft.Spawns).Concat(draft.EquipmentParking).Concat(draft.Lots.SelectMany(l => l.Cells)).Concat(draft.Districts.SelectMany(d => d.Cells)).Concat(draft.Civics.SelectMany(c => c.Footprint)).Concat(draft.Stalls.Select(s => s.Cell)).ToHashSet();
        foreach (var road in draft.Roads)
            foreach (var next in new[] { new Cell(road.X, road.Z + 1), new(road.X, road.Z - 1), new(road.X + 1, road.Z), new(road.X - 1, road.Z) })
                if (next.X >= 0 && next.Z >= 0 && next.X < draft.Width && next.Z < draft.Height && !blocked.Contains(next)) return (next, road);
        throw new InvalidOperationException("no free cell");
    }
}
