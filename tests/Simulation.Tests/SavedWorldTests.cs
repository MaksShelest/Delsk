using System.Text.Json;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public sealed class SavedWorldTests : IDisposable
{
    private static readonly ParticipantId Human = new(1);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "delsk-worlds-" + Guid.NewGuid().ToString("N"));
    private SavedWorldStore Store => new(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void TwoOrdinaryWorldsKeepProductionRosterMoneyStockAndEventsIndependent()
    {
        var store = Store;
        var (first, _) = Ordinary(PlayMaps.CompactValley, 17);
        first.AttachControllers(first.Engine.Configuration().Participants.Select(p => new SelfBallotController(new(p.Id))));
        string a = store.Create(first, "Долина");
        RunFirstBatch(first);
        store.Save(a, first);
        string firstHash = first.Engine.ComputeStateHash();

        var (second, resolved) = Ordinary(PlayMaps.Second, 91);
        var profile = Profile();
        var config = OrdinaryNewGameTests.Config().ForOrdinary(resolved.Scenario, 1, resolved.Controllers);
        second.AttachControllers(config.Npcs.Select(n => AlgorithmicController.WithEconomy(n, profile.Npc))
            .Select(n => new AlgorithmicController(n, AlgorithmicController.RuntimeSeed(resolved.Scenario.Seed, n.ActorId),
                resolved.Scenario.Participants.Select(p => p.Id))));
        string b = store.Create(second, "Лес");
        second.AdvanceTo(24);
        store.Save(b, second);
        string secondHash = second.Engine.ComputeStateHash();

        Assert.NotEqual(a, b);
        Assert.Equal(b, Store.LastActive());
        var rows = store.List();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, w => w.Id == a && w.Summary is { Enterprises: 1, Participants: 4 }
            && w.SavedAt is not null && w.Summary.Tick == first.Engine.Tick);
        Assert.Contains(rows, w => w.Id == b && w.Summary is { Enterprises: 0, Participants: 4, Tick: 24 });

        var resumedA = Restore(store, a);
        Assert.Equal(17UL, resumedA.Engine.Configuration().Seed);
        Assert.Equal(firstHash, resumedA.Engine.ComputeStateHash());
        Assert.Equal(first.ComputeControllerStateHash(), resumedA.ComputeControllerStateHash());
        Assert.Equal(first.Engine.Ledger(), resumedA.Engine.Ledger());
        Assert.Equal(first.Engine.EventsSince(0), resumedA.Engine.EventsSince(0));
        int grants = first.Engine.Ledger().Count(e => e.Reason == "ProvincialOperatingReserve");
        Assert.Equal(1, grants);
        string secondCheckpoint = File.ReadAllText(store.CheckpointPath(b));
        Assert.Equal("ProvincialSupportUsed", Command(resumedA, new(ActionKind.AcceptProvincialSupport), ResultStatus.Rejected));
        Assert.Equal(grants, resumedA.Engine.Ledger().Count(e => e.Reason == "ProvincialOperatingReserve"));
        store.Save(a, resumedA);
        Assert.Equal(secondCheckpoint, File.ReadAllText(store.CheckpointPath(b)));

        var resumedB = Restore(store, b);
        Assert.Equal(91UL, resumedB.Engine.Configuration().Seed);
        Assert.Equal(PlayMaps.WoodlandI6C8CScenarioId, resumedB.Engine.Configuration().Id);
        Assert.Equal(VNextI6C8DBalanceProfile.ProfileId, resumedB.Engine.Configuration().EconomyProfileId);
        Assert.Equal(secondHash, resumedB.Engine.ComputeStateHash());
        Assert.Equal(second.ComputeControllerStateHash(), resumedB.ComputeControllerStateHash());
        Assert.Equal(second.Engine.Observe(Human).Account, resumedB.Engine.Observe(Human).Account);
        Assert.Equal(second.Engine.EventsSince(0), resumedB.Engine.EventsSince(0));
        Assert.Empty(resumedB.Engine.Observe(Human).OwnEnterprises);
        Assert.Equal("Offered", resumedB.Engine.ProvincialProgramOf(Human)!.Status);

        second.AdvanceTo(48);
        resumedB.AdvanceTo(48);
        var replay = GameSession.Replay(resolved.Scenario, second.CommandTape, 48);
        Assert.Equal(second.Engine.ComputeStateHash(), resumedB.Engine.ComputeStateHash());
        Assert.Equal(second.Engine.ComputeStateHash(), replay.Engine.ComputeStateHash());
        Assert.Equal(second.ComputeControllerStateHash(), resumedB.ComputeControllerStateHash());
        Assert.Equal(second.Engine.Ledger(), resumedB.Engine.Ledger());
        MoneyConserved(resumedA);
        MoneyConserved(resumedB);
    }

    [Fact]
    public void DamagedPrimaryRecoversAndDoubleFailureStaysInItsWorld()
    {
        var store = Store;
        var session = Fixtures.Session();
        string id = store.Create(session);
        string untouched = store.Create(Fixtures.Session(Fixtures.Cycle() with { Seed = 18 }));
        string path = store.CheckpointPath(id);
        session.AdvanceTo(3);
        store.Save(id, session);
        string backup = File.ReadAllText(path + ".bak");
        File.WriteAllText(path, "{}");
        var recovered = store.Load(id);
        Assert.True(recovered.UsedBackup);
        Assert.Equal(0, recovered.Session.Engine.Tick);
        recovered.Session.AdvanceTo(4);
        store.Save(id, recovered.Session);
        Assert.Equal(4, store.Load(id).Session.Engine.Tick);
        Assert.Equal(backup, File.ReadAllText(path + ".bak"));
        Assert.Equal("{}", File.ReadAllText(Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.corrupt-*"))));

        File.WriteAllText(path, "{damaged");
        File.WriteAllText(path + ".bak", "{damaged too");
        Assert.Null(store.List().Single(w => w.Id == id).Summary);
        Assert.Throws<CheckpointException>(() => store.Load(id));
        Assert.Equal(18UL, store.Load(untouched).Session.Engine.Configuration().Seed);
        store.Delete(id);
        Assert.Equal(untouched, Assert.Single(store.List()).Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyImportKeepsBytesAndSettingsAndDoesNotRepeatAfterDelete(bool backupOnly)
    {
        var store = Store;
        var profile = VNextProvincialSupportProfile.ForOrdinaryMap(MobileEquipmentI4Tests.Profile());
        var (session, _) = Ordinary(PlayMaps.CompactValley, 19, profile, disableSpecializations: true);
        session.AdvanceTo(10);
        string legacy = store.LegacyPath("economy");
        string actual = backupOnly ? legacy + ".bak" : legacy;
        CheckpointStore.Save(session, actual);
        string original = File.ReadAllText(actual);
        string foreign = legacy.Replace(".json", SimulationEngine.IsDeveloperBuild ? "-release.json" : "-developer.json");
        File.WriteAllText(foreign, "different edition");
        string id = Assert.IsType<string>(store.ImportLegacy("economy"));
        var loaded = Store.Load(id);
        Assert.Equal(backupOnly, loaded.UsedBackup);
        Assert.Equal(session.Engine.ComputeStateHash(), loaded.Session.Engine.ComputeStateHash());
        var config = loaded.Session.Engine.Configuration();
        Assert.Equal(VNextProvincialSupportProfile.ProfileId, config.EconomyProfileId);
        Assert.False(config.Economy!.StartingSpecializations!.Enabled);
        Assert.Equal(original, File.ReadAllText(actual));
        Assert.Equal(id, Store.ImportLegacy("economy"));
        Assert.Single(Store.List());
        store.Rename(id, "Прежняя долина");
        Assert.Equal("Прежняя долина", Assert.Single(Store.List()).Name);
        Assert.Equal(session.Engine.ComputeStateHash(), Store.Load(id).Session.Engine.ComputeStateHash());
        store.Delete(id);
        Assert.Null(Store.ImportLegacy("economy"));
        Assert.Empty(Store.List());
        Assert.Equal(original, File.ReadAllText(actual));
        Assert.Equal("different edition", File.ReadAllText(foreign));
    }

    [Fact]
    public void LegacyBackupBelongsToItsWorldBeforeItCanBeUsedForRecovery()
    {
        var store = Store;
        var current = Fixtures.Session();
        var foreign = Fixtures.Session(Fixtures.Cycle() with { Seed = 91 });
        string legacy = store.LegacyPath("economy");
        CheckpointStore.Save(foreign, legacy);
        CheckpointStore.Save(current, legacy);
        string oldBackup = File.ReadAllText(legacy + ".bak");
        string id = store.ImportLegacy("economy")!;
        Assert.False(File.Exists(store.CheckpointPath(id) + ".bak"));
        Assert.Equal(oldBackup, File.ReadAllText(legacy + ".bak"));

        string trip = store.LegacyPath("trip");
        CheckpointStore.Save(current, trip);
        current.AdvanceTo(4);
        CheckpointStore.Save(current, trip);
        id = store.ImportLegacy("trip")!;
        File.WriteAllText(store.CheckpointPath(id), "{truncated");
        var loaded = store.Load(id);
        Assert.True(loaded.UsedBackup);
        Assert.Equal(42UL, loaded.Session.Engine.Configuration().Seed);
        Assert.Equal(0, loaded.Session.Engine.Tick);
    }

    [Fact]
    public void IncompatiblePrimaryAndFailedMigrationDoNotOverwriteSources()
    {
        var store = Store;
        var session = Fixtures.Session();
        string id = store.Create(session);
        string path = store.CheckpointPath(id);
        string original = File.ReadAllText(path);
        File.Copy(path, path + ".bak");
        const string incompatible = """{"formatVersion":999,"payloadJson":"","sha256":""}""";
        File.WriteAllText(path, incompatible);
        string legacy = store.LegacyPath("economy");
        File.WriteAllText(legacy, incompatible);
        File.WriteAllText(legacy + ".bak", original);
        Assert.Throws<CheckpointException>(() => store.Load(id));
        Assert.Throws<CheckpointException>(() => store.Save(id, session));
        Assert.Throws<CheckpointException>(() => store.ImportLegacy("economy"));
        Assert.Equal(incompatible, File.ReadAllText(path));
        Assert.Equal(incompatible, File.ReadAllText(legacy));
        Assert.Equal(original, File.ReadAllText(legacy + ".bak"));
        Assert.Single(store.List());
    }

    [Fact]
    public void LastActiveSelectionSurvivesRestartAndNamesDoNotIdentifyStorage()
    {
        var store = Store;
        var a = Fixtures.Session();
        string first = store.Create(a, "Same");
        string second = store.Create(Fixtures.Session(), "Same");
        string third = store.Create(Fixtures.Session(), "Same");
        string fourth = store.Create(Fixtures.Session(), "Same");
        Assert.Equal(4, Store.List().Count);
        Assert.Equal(fourth, Store.LastActive());
        Store.SetActive(first);
        a.AdvanceTo(7);
        Store.Save(first, a);
        Assert.Equal(first, Store.LastActive());
        Assert.Equal(a.Engine.ComputeStateHash(), Store.Load(first).Session.Engine.ComputeStateHash());
        Assert.Equal(0, Store.Load(second).Session.Engine.Tick);
        Assert.Equal(0, Store.Load(third).Session.Engine.Tick);
        Assert.Throws<ArgumentException>(() => Store.Load("../other-world"));
        Store.Delete(first);
        Assert.Null(Store.LastActive());
        Assert.Throws<DirectoryNotFoundException>(() => Store.Save(first, a));
        Assert.Equal(3, Store.List().Count);
    }

    private static EconomyProfile Profile() =>
        VNextI6C8DBalanceProfile.Create(VNextProvincialSupportProfile.ForOrdinaryMap(MobileEquipmentI4Tests.Profile()));

    private static (GameSession Session, OrdinaryGameResolution Resolution) Ordinary(string map, ulong seed,
        EconomyProfile? profile = null, bool disableSpecializations = false)
    {
        profile ??= Profile();
        var source = JsonSerializer.Deserialize<ScenarioDefinition>(
            OrdinaryNewGameTests.Text(PlayMaps.NewGameResource(map).Replace("Play.", "")), SimulationJson.Options)!;
        var resolved = OrdinaryNewGame.Resolve(source, profile, new(3, Seed: seed,
            StartingIndustry: disableSpecializations ? null : "woodworking", DisableStartingSpecializations: disableSpecializations),
            vnextBalance: MapDesignTests.Balance());
        return (new(new(resolved.Scenario)), resolved);
    }

    private static GameSession Restore(SavedWorldStore store, string id)
    {
        var session = store.Load(id).Session;
        session.RestoreControllers(c => c.Kind == SelfBallotController.Kind
            ? SelfBallotController.Restore(c) : AlgorithmicController.Restore(c));
        return session;
    }

    private static string Command(GameSession session, GameAction action, ResultStatus expected = ResultStatus.Accepted)
    {
        string id = Fixtures.Command(session, action);
        var result = session.Engine.Result(id)!;
        Assert.True(result.Status == expected, action.Kind + " " + result.ReasonCode);
        return result.ReasonCode;
    }

    private static void RunFirstBatch(GameSession session)
    {
        var scenario = session.Engine.Configuration();
        var lot = scenario.Layout!.IndustrialLots.First();
        var anchor = IndustrialParcels.TryFrame(lot)!.Value.Anchor;
        var plot = scenario.Plots!.Single(p => p.Position == anchor);
        Command(session, new(ActionKind.AcceptProvincialSupport));
        StorageC1Tests.Move(session, anchor);
        Command(session, new(ActionKind.BuyPlot, Plot: new(plot.Id)));
        StorageC1Tests.Move(session, scenario.CapitalDemand!.Terminal);
        Command(session, new(ActionKind.CollectProvincialSupply));
        var supply = session.Engine.Observe(Human).OwnInventories.Single(i => i.Stock.Any(n => n.ItemId == "steel"));
        var ground = new InventoryId(session.Engine.StorageOf(Human).Ground.Single(g => g.PlotId == plot.Id).InventoryId);
        var level = scenario.BuildingTypes!.Single(t => t.Id == "woodworking").Levels.Single(l => l.Level == 1);
        var recipe = scenario.Recipes.Single(r => r.Id == StartingSpecializations.Direction("woodworking")!.RecipeId);
        foreach (var material in level.Materials)
            ProvincialSupportI6C8CTests.Haul(session, supply.Id, ground, material.ItemId, material.Quantity);
        StorageC1Tests.Move(session, anchor);
        Command(session, new(ActionKind.Build, BuildingTypeId: "woodworking", Target: anchor));
        session.AdvanceTo(session.Engine.DevelopmentOf(Human)!.Projects.Single().CompletesTick);
        var enterprise = session.Engine.Observe(Human).OwnEnterprises.Single();
        if (enterprise.RecipeId != recipe.Id) Command(session, new(ActionKind.SetRecipe, Enterprise: enterprise.Id, RecipeId: recipe.Id));
        foreach (var input in recipe.Ingredients)
            ProvincialSupportI6C8CTests.Haul(session, supply.Id, enterprise.InputId, input.ItemId, input.Quantity);
        StorageC1Tests.Move(session, anchor);
        Command(session, new(ActionKind.SetProductionEnabled, Enterprise: enterprise.Id, Enabled: true));
        for (int i = 0; i < 800 && session.Engine.Observe(Human).OwnEnterprises.Single().CompletedBatches == 0; i++) session.Step();
        var made = Assert.Single(session.Engine.Observe(Human).OwnEnterprises);
        Assert.Equal(1, made.CompletedBatches);
        Assert.Equal(recipe.OutputQuantity, Fixtures.Count(session, made.OutputId, recipe.OutputItem));
    }

    private static void MoneyConserved(GameSession session)
    {
        session.Engine.ValidateInvariants();
        var world = session.Engine.Inspect();
        Assert.Equal(world.InitialMoneyMinor + world.ExternalMoneyInMinor - world.ExternalMoneyOutMinor,
            world.Accounts.Sum(a => a.BalanceMinor));
    }
}
