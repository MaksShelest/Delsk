using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class LogisticsUpgradeTests
{
    private static ScenarioDefinition Scenario()
    {
        using var stream = typeof(LogisticsUpgradeTests).Assembly.GetManifestResourceStream("Fixture.logistics-upgrade.json")!;
        return JsonSerializer.Deserialize<ScenarioDefinition>(stream, SimulationJson.Options)!;
    }
    private static GameSession Recording()
    {
        var d = Scenario(); var s = new GameSession(new SimulationEngine(d));
        ScriptedScenarios.LogisticsUpgradeCycle(s, d); return s;
    }
    // A prefix without a playback tail lets a test issue its own next command/sequence.
    private static GameSession BeforeUpgrade(ScenarioDefinition? definition = null)
    {
        var record = Recording(); var s = new GameSession(new SimulationEngine(definition ?? Scenario()));
        long cut = record.CommandTape.Single(c => c.Command.Action.Kind == ActionKind.UpgradeLogistics).SubmittedAtTick;
        foreach (var c in record.CommandTape.Where(c => c.SubmittedAtTick < cut))
        { s.AdvanceTo(c.SubmittedAtTick); s.Submit(c.AuthenticatedActor, c.Command); }
        s.AdvanceTo(cut); return s;
    }
    private static LogisticsView System(GameSession s) => s.Engine.Inspect().Logistics.Single();
    private static EmploymentView Crew(GameSession s) => s.Engine.Inspect().EmploymentGroups.Single(g => g.Role == EmploymentRole.Logistics);
    private static string Upgrade(GameSession s, long actor = 1) => LandContentFixtures.Reason(s,
        new(ActionKind.UpgradeLogistics, Logistics: System(s).Id), actor);
    private static GameSession At(long tick, ScenarioDefinition? definition = null)
    {
        var record = Recording(); var s = new GameSession(new SimulationEngine(definition ?? Scenario()));
        s.ConfigurePlayback(record.CommandTape, record.Engine.Tick, "logistics-upgrade"); s.AdvanceTo(tick); return s;
    }
    private static long UpgradedTick => Recording().Engine.EventsSince(0).Single(e => e.Type == "LogisticsUpgraded").Tick;
    private static long LoadedTick => Recording().Engine.EventsSince(0).Single(e => e.Type == "LogisticsCargoLoaded").Tick;

    [Fact]
    public void UpgradedTruckDeliversTwentyOnceWithRealCrewCostsAndConservedGoods()
    {
        var s = Recording(); var w = s.Engine.Inspect(); var system = System(s);
        Assert.Equal(2, system.Level); Assert.Equal(1, system.CompletedTrips); Assert.Null(system.Trip);
        Assert.Equal(20, w.Inventories.Single(i => i.Id == system.CargoInventory).Capacity);
        Assert.Equal(3, Crew(s).Capacity); Assert.Equal(0, Crew(s).Headcount);
        Assert.Equal(104900, s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(40000, s.Engine.Observe(new(2)).Account.BalanceMinor);
        Assert.Equal(53000, w.Accounts.Single(a => a.Kind == "Treasury").BalanceMinor);
        Assert.Equal(2100, w.Accounts.Single(a => a.Kind == "WagesSink").BalanceMinor);
        Assert.Equal(200000, w.Accounts.Sum(a => a.BalanceMinor));
        var flow = w.Flows.Single(f => f.ItemId == "plank");
        Assert.Equal(30, flow.Produced); Assert.Equal(10, flow.ConstructionInput);
        Assert.Equal(20, w.Inventories.Sum(i => i.Stock.Where(x => x.ItemId == "plank").Sum(x => x.Quantity)));
        Assert.Equal(20, w.DeliveryContracts.Single().DeliveredQuantity);
        Assert.Equal(0, w.Accounts.Sum(a => a.ReservedMinor)); Assert.Equal(0, w.Inventories.Sum(i => i.ReservedCapacity));
        Assert.Single(s.Engine.Ledger(), l => l.Reason == "LogisticsUpgrade");
        Assert.Single(s.Engine.EventsSince(0), e => e.Type == "LogisticsTripPrepared" && e.Quantity == 20);
        var trip = At(LoadedTick + 1); Assert.Equal(new Cell(3, 0), System(trip).Trip!.Position);
        Assert.Equal(0, trip.Engine.Inspect().DeliveryContracts.Single().DeliveredQuantity);
        Assert.Equal(675, System(trip).Trip!.WageBudgetMinor);
    }
    [Fact]
    public void UpgradeKeepsIdsWagesPopulationAndHeadcountWithoutCreatingWorkers()
    {
        var s = BeforeUpgrade(); var before = System(s); var crew = Crew(s); var nextId = s.Engine.Inspect().NextId;
        Assert.Equal("Accepted", Upgrade(s)); var after = System(s);
        Assert.Equal(before.Id, after.Id); Assert.Equal(before.GroupId, after.GroupId);
        Assert.Equal(before.SourceInventory, after.SourceInventory); Assert.Equal(before.CargoInventory, after.CargoInventory);
        Assert.Equal(before.ContractId, after.ContractId); Assert.Equal(nextId, s.Engine.Inspect().NextId);
        Assert.Equal(crew.Headcount, Crew(s).Headcount); Assert.Equal(crew.WageMinor, Crew(s).WageMinor);
        Assert.Equal(4, s.Engine.Inspect().Population); Assert.Equal(4, s.Engine.Inspect().Unemployed);
        Assert.Equal(3, Crew(s).Capacity); Assert.False(after.Enabled);
    }
    [Fact]
    public void UpgradeUsesBothBuiltInInventoriesAndNeverPersonalStock()
    {
        var s = BeforeUpgrade(); var e = s.Engine.Observe(new(1)).OwnEnterprises.Single();
        var carrier = s.Engine.Observe(new(1)).Participant.InventoryId;
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.Transfer, Source: e.OutputId, Destination: carrier, ItemId: "plank", Quantity: 25)));
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.Transfer, Source: carrier, Destination: e.InputId, ItemId: "plank", Quantity: 5)));
        Assert.Equal("Accepted", Upgrade(s));
        Assert.Equal(20, Fixtures.Count(s, carrier, "plank")); Assert.Equal(0, Fixtures.Count(s, e.InputId, "plank"));
        Assert.Equal(0, Fixtures.Count(s, e.OutputId, "plank"));
        Assert.Equal(10, s.Engine.Inspect().Flows.Single(f => f.ItemId == "plank").ConstructionInput);
    }
    [Fact]
    public void PersonalMaterialsCannotCoverMissingBuiltInStockAndFailureIsAtomic()
    {
        var s = BeforeUpgrade(); var e = s.Engine.Observe(new(1)).OwnEnterprises.Single();
        var carrier = s.Engine.Observe(new(1)).Participant.InventoryId;
        Fixtures.Command(s, new(ActionKind.Transfer, Source: e.OutputId, Destination: carrier, ItemId: "plank", Quantity: 21));
        long cash = s.Engine.Observe(new(1)).Account.BalanceMinor; var ledger = s.Engine.Ledger();
        Assert.Equal("NoStock", Upgrade(s)); Assert.Equal(1, System(s).Level); Assert.Equal(2, Crew(s).Capacity);
        Assert.Equal(9, Fixtures.Count(s, e.OutputId, "plank")); Assert.Equal(21, Fixtures.Count(s, carrier, "plank"));
        Assert.Equal(cash, s.Engine.Observe(new(1)).Account.BalanceMinor); Assert.Equal(ledger, s.Engine.Ledger());
        Assert.Equal(0, s.Engine.Inspect().Flows.Single(f => f.ItemId == "plank").ConstructionInput);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NoMoneyOrMissingSecondMaterialRollsBackAllDeductions(bool secondMaterial)
    {
        var d = Scenario();
        d = secondMaterial ? d with { Logistics = d.Logistics! with { Upgrade = d.Logistics!.Upgrade! with { Materials = [new("plank", 10), new("wood", 1)] } } }
            : d with { Participants = d.Participants.Select(p => p.Id == 1 ? p with { CashMinor = 50000 } : p).ToArray() };
        var s = BeforeUpgrade(d); var ledger = s.Engine.Ledger(); var cash = s.Engine.Observe(new(1)).Account.BalanceMinor;
        Assert.Equal(secondMaterial ? "NoStock" : "NoMoney", Upgrade(s));
        Assert.Equal(30, Fixtures.Count(s, System(s).SourceInventory, "plank"));
        Assert.Equal(cash, s.Engine.Observe(new(1)).Account.BalanceMinor); Assert.Equal(ledger, s.Engine.Ledger());
        Assert.Equal(1, System(s).Level); Assert.Equal(0, s.Engine.Inspect().Flows.Single(f => f.ItemId == "plank").ConstructionInput);
    }
    [Fact]
    public void OwnershipPresenceAndMaximumLevelAreEnforcedAndDuplicateDoesNotPayTwice()
    {
        var s = BeforeUpgrade(); Assert.Equal("NotAvailable", Upgrade(s, 2));
        LandContentFixtures.Move(s, new(0, 0)); Assert.Equal("NotAtLocation", Upgrade(s));
        LandContentFixtures.Move(s, new(2, 0));
        var id = s.Send(new(1), new(ActionKind.UpgradeLogistics, Logistics: System(s).Id)); s.Step();
        Assert.Equal("Accepted", s.Engine.Result(id)!.ReasonCode);
        var recorded = s.CommandTape.Single(c => c.Command.CommandId == id); var ledger = s.Engine.Ledger();
        Assert.Equal(ResultStatus.Accepted, s.Engine.Enqueue(new(1), recorded.Command)!.Status);
        Assert.Equal("MaxLogisticsLevel", Upgrade(s)); Assert.Equal(ledger, s.Engine.Ledger());
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UpgradeCannotChangeEnabledSystemOrAlreadyStartedTruck(bool inTransit)
    {
        var s = inTransit ? At(LoadedTick) : BeforeUpgrade();
        if (!inTransit) Fixtures.Command(s, new(ActionKind.SetLogisticsContract, Logistics: System(s).Id,
            ContractId: s.Engine.Inspect().DeliveryContracts.Single().Id, Enabled: true));
        else Fixtures.Command(s, new(ActionKind.SetLogisticsContract, Logistics: System(s).Id,
            ContractId: s.Engine.Inspect().DeliveryContracts.Single().Id, Enabled: false));
        var system = System(s); if (inTransit) Assert.NotNull(system.Trip);
        Assert.Equal("LogisticsNotSafe", Upgrade(s)); Assert.Equal(system.Level, System(s).Level);
        if (inTransit) Assert.Equal(system.Trip!.Id, System(s).Trip!.Id);
    }
    [Fact]
    public void HigherMinimumCrewPreventsDispatchWhenOnlyTwoWorkersExist()
    {
        var s = At(LoadedTick + 1, Scenario() with { Population = 2 });
        Assert.Equal(2, System(s).Level); Assert.Equal(2, Crew(s).Headcount); Assert.Equal(3, Crew(s).Capacity);
        Assert.Null(System(s).Trip); Assert.Equal("NoStaff", System(s).ReasonCode);
        Assert.Equal(0, s.Engine.Inspect().DeliveryContracts.Single().DeliveredQuantity);
        Assert.DoesNotContain(s.Engine.Ledger(), l => l.Reason == "LogisticsWages");
    }
    [Fact]
    public void LargerCapacityChecksCurrentAndPendingWageBeforeSpending()
    {
        var s = BeforeUpgrade();
        // Four possible grid legs plus three handling intervals; fits crew2, overflows crew3.
        long bound=4L*(Scenario().MapWidth*Scenario().MapHeight-1)*(Scenario().Logistics??new()).MoveTicksPerCell+3;
        long high = long.MaxValue / (2 * bound);
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.SetWage, Group: Crew(s).Id, WageMinor: high)));
        var ledger = s.Engine.Ledger();
        Assert.Equal("ArithmeticOverflow", Upgrade(s)); Assert.Equal(ledger, s.Engine.Ledger()); Assert.Equal(1, System(s).Level);
    }
    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(1)] [InlineData(3)] [InlineData(6)]
    public void DirectLoadBeforeAfterUpgradeAndDuringLargeCargoTripMatchesEveryTick(int offset)
    {
        long cut = offset <= 0 ? UpgradedTick + offset : LoadedTick + offset - 1;
        var s = At(cut); var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        long end = Recording().Engine.Tick;
        while (s.Engine.Tick < end) { s.Step(); loaded.Step(); Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash()); }
        Assert.Equal(s.Engine.Ledger(), loaded.Engine.Ledger()); Assert.Equal(s.Engine.EventsSince(0), loaded.Engine.EventsSince(0));
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)]
    public void CheckpointRejectsUnknownOrInconsistentTransportLevel(int level)
    {
        var s = At(LoadedTick + 1); var json = JsonNode.Parse(s.Engine.CreateCheckpointJson())!;
        json["logistics"]![0]!["view"]!["level"] = level;
        Assert.Throws<CheckpointException>(() => SimulationEngine.FromCheckpointJson(json.ToJsonString(SimulationJson.Options)));
    }
    [Theory]
    [InlineData("capacity")] [InlineData("crew")] [InlineData("item")] [InlineData("duplicate")]
    [InlineData("quantity")] [InlineData("price")] [InlineData("overflow")]
    public void InvalidUpgradeCatalogFailsBeforeWorldCreation(string problem)
    {
        var d = Scenario(); var upgrade = d.Logistics!.Upgrade!;
        upgrade = problem switch {
            "capacity" => upgrade with { CargoCapacity = 9 }, "crew" => upgrade with { CrewCapacity = 2 },
            "item" => upgrade with { Materials = [new("missing", 10)] },
            "duplicate" => upgrade with { Materials = [new("plank", 5), new("plank", 5)] },
            "quantity" => upgrade with { Materials = [new("plank", 0)] }, "price" => upgrade with { PriceMinor = 0 },
            _ => upgrade with { Materials = [new("plank", long.MaxValue)], CargoCapacity = 20 }
        };
        if (problem == "overflow") d = d with { Items = d.Items.Select(i => i.Id == "plank" ? i with { UnitVolume = 2 } : i).ToArray() };
        Assert.Throws<ArgumentException>(() => new SimulationEngine(d with { Logistics = d.Logistics! with { Upgrade = upgrade } }));
    }
    [Fact]
    public void PreviousWorldSchemaIsExplicitlyRejectedAndRulesAreImmutableInObservation()
    {
        var s = BeforeUpgrade(); var json = JsonNode.Parse(s.Engine.CreateCheckpointJson())!; json["schemaVersion"] = 5;
        Assert.Equal("UnknownWorldSchema", Assert.Throws<CheckpointException>(() => SimulationEngine.FromCheckpointJson(json.ToJsonString(SimulationJson.Options))).ReasonCode);
        var rules = s.Engine.ObserveController(new(1)).LogisticsRules;
        var edited = rules.Upgrade!.Materials.SetItem(0, new("plank", 1));
        Assert.Equal(1, edited[0].Quantity); Assert.Equal(10, s.Engine.ObserveController(new(1)).LogisticsRules.Upgrade!.Materials[0].Quantity);
        Assert.Empty(s.Engine.ObserveController(new(2)).OwnLogistics);
    }
}
