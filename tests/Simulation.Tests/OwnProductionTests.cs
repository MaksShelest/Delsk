using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class OwnProductionTests
{
    private static ScenarioDefinition Scenario()
    {
        using var stream = typeof(OwnProductionTests).Assembly.GetManifestResourceStream("Fixture.own-production-cycle.json")!;
        return JsonSerializer.Deserialize<ScenarioDefinition>(stream, SimulationJson.Options)!;
    }
    private static GameSession Run()
    {
        var d = Scenario(); var s = new GameSession(new SimulationEngine(d));
        ScriptedScenarios.OwnProductionCycle(s, d); return s;
    }
    private static readonly Lazy<GameSession> Baseline = new(Run);
    private static long RouteTick => Baseline.Value.CommandTape.First(c => c.Command.Action.Kind == ActionKind.SetLogisticsProduction).SubmittedAtTick;
    private static long PreparedTick => Baseline.Value.Engine.EventsSince(0).First(e => e.Type == "LogisticsTripPrepared").Tick;
    private static LogisticsView System(GameSession s) => s.Engine.Inspect().Logistics.Single();
    private static EnterpriseView Destination(GameSession s) => s.Engine.Inspect().Enterprises.Single(e => e.Id.Value == 2);
    private static GameSession At(long tick, ScenarioDefinition? definition = null)
    {
        var s = new GameSession(new SimulationEngine(definition ?? Scenario()));
        s.ConfigurePlayback(Baseline.Value.CommandTape, Baseline.Value.Engine.Tick, "own-production"); s.AdvanceTo(tick); return s;
    }
    private static GameSession BeforeRoute(ScenarioDefinition? definition = null)
    {
        var s = new GameSession(new SimulationEngine(definition ?? Scenario()));
        foreach (var c in Baseline.Value.CommandTape.Where(c => c.SubmittedAtTick < RouteTick))
        { s.AdvanceTo(c.SubmittedAtTick); s.Submit(c.AuthenticatedActor, c.Command); }
        s.AdvanceTo(RouteTick); return s;
    }
    private static GameAction Route(GameSession s, bool enabled = true, InventoryId? destination = null) =>
        new(ActionKind.SetLogisticsProduction, Logistics: System(s).Id, Destination: destination ?? Destination(s).InputId, Enabled: enabled);
    private static long Wages(GameSession s) => s.Engine.Ledger().Where(l => l.Reason == "LogisticsWages").Sum(l => l.AmountMinor);
    private static void WaitTrips(GameSession s, long count)
    {
        long limit = s.Engine.Tick + 100;
        while (System(s).CompletedTrips < count && s.Engine.Tick < limit) s.Step();
        Assert.Equal(count, System(s).CompletedTrips);
    }

    [Fact]
    public void PhysicalProductFeedsNextProductionAndRefillsAfterConsumptionWithoutSale()
    {
        var s = Run(); var w = s.Engine.Inspect();
        Assert.Equal(4, System(s).CompletedTrips); Assert.Null(System(s).Trip);
        Assert.Equal("NoDestinationSpace", System(s).ReasonCode);
        Assert.Equal(new long[] { 10, 5, 10, 4 }, s.Engine.EventsSince(0).Where(e => e.Type == "LogisticsProductionReceived").Select(e => e.Quantity));
        Assert.Equal(15, Fixtures.Count(s, Destination(s).InputId, "plank"));
        Assert.Equal(1, Fixtures.Count(s, System(s).SourceInventory, "plank"));
        Assert.Equal(7, Fixtures.Count(s, Destination(s).OutputId, "crate"));
        Assert.Equal(14, w.Flows.Single(f => f.ItemId == "plank").RecipeInput);
        Assert.Equal(30, w.Flows.Single(f => f.ItemId == "plank").Produced);
        Assert.Equal(60, w.Flows.Single(f => f.ItemId == "wood").Harvested);
        Assert.Equal(1600, Wages(s)); Assert.Equal(73200, s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(23350, w.Accounts.Single(a => a.Kind == "Treasury").BalanceMinor);
        Assert.Equal(3450, w.Accounts.Single(a => a.Kind == "WagesSink").BalanceMinor);
        Assert.Equal(200000, w.Accounts.Sum(a => a.BalanceMinor)); Assert.Equal(0, w.ExternalMoneyInMinor);
        Assert.Empty(w.DeliveryContracts); Assert.Empty(w.MarketAllocations);
        Assert.Single(w.EmploymentGroups, g => g.Role == EmploymentRole.Logistics);
        Assert.Equal(w.Population, w.Unemployed + w.EmploymentGroups.Sum(g => g.Headcount));
        Assert.Equal(0, w.Accounts.Sum(a => a.ReservedMinor)); Assert.Equal(0, w.Inventories.Sum(i => i.ReservedCapacity));
    }
    [Fact]
    public void DestinationSpaceIsPromisedBeforeDepartureAndStockOnlyArrivesAfterTravel()
    {
        var s = At(PreparedTick); var t = System(s).Trip!; var dest = Destination(s).InputId;
        Assert.Equal("Loading", t.State); Assert.Equal(dest, t.OwnDestination); Assert.Equal(0, t.ContractId);
        Assert.Equal(30, Fixtures.Count(s, System(s).SourceInventory, "plank")); Assert.Equal(0, Fixtures.Count(s, dest, "plank"));
        Assert.Equal(10, s.Engine.Inspect().Inventories.Single(i => i.Id == dest).ReservedCapacity);
        s.Step(); Assert.Equal(10, Fixtures.Count(s, System(s).CargoInventory, "plank"));
        Assert.Equal(20, Fixtures.Count(s, System(s).SourceInventory, "plank"));
        Assert.Equal(10, s.Engine.Inspect().Inventories.Single(i => i.Id == System(s).SourceInventory).ReservedCapacity);
        s.AdvanceTo(t.ArrivalTick - 1); Assert.Equal(0, Fixtures.Count(s, dest, "plank"));
        s.Step(); Assert.Equal(10, Fixtures.Count(s, dest, "plank")); Assert.Equal("ReturningEmpty", System(s).Trip!.State);
        Assert.Empty(s.Engine.Inspect().Inventories.Single(i => i.Id == System(s).CargoInventory).Stock);
        Assert.Equal(0, s.Engine.Inspect().Inventories.Sum(i => i.ReservedCapacity));
    }
    [Fact]
    public void ManualTransferCannotUseIncomingReservedCapacity()
    {
        var d = Scenario() with { Logistics = new(MoveTicksPerCell: 3) };
        var s = BeforeRoute(d); Assert.Equal("Accepted", LandContentFixtures.Reason(s, Route(s))); s.Step();
        var t = System(s).Trip!; var carrier = s.Engine.Observe(new(1)).Participant.InventoryId;
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.Transfer, Source: System(s).SourceInventory,
            Destination: carrier, ItemId: "plank", Quantity: 6)));
        LandContentFixtures.Move(s, new(5, 0)); Assert.True(s.Engine.Tick < t.ArrivalTick);
        Assert.Equal("NoCapacity", LandContentFixtures.Reason(s, new(ActionKind.Transfer, Source: carrier,
            Destination: Destination(s).InputId, ItemId: "plank", Quantity: 6)));
        Assert.Equal(6, Fixtures.Count(s, carrier, "plank")); Assert.Equal(0, Fixtures.Count(s, Destination(s).InputId, "plank"));
    }
    [Theory]
    [InlineData("foreign")] [InlineData("self")] [InlineData("output")] [InlineData("personal")]
    [InlineData("cargo")] [InlineData("unknown")] [InlineData("storage")]
    public void ProductionDestinationRequiresAnotherOwnProductionInput(string kind)
    {
        var d = Scenario();
        if (kind == "foreign") d = d with {
            Buildings = d.Buildings!.Select(b => b.Id == 2 ? b with { OwnerId = 2 } : b).ToArray(),
            Plots = d.Plots!.Select(p => p.Id == 2 ? p with { OwnerId = 2 } : p).ToArray() };
        if (kind == "storage") d = d with { BuildingTypes = d.BuildingTypes!.Select(b => b.Id == "workshop" ? b with {
            Kind = BuildingKind.Warehouse, Levels = b.Levels.Select(l => l with { RecipeId = "", OutputCapacity = 0, StaffCapacity = 0, WageMinor = 0 }).ToArray() } : b).ToArray() };
        var s = BeforeRoute(d); var source = s.Engine.Observe(new(1)).OwnEnterprises.Single(e => e.Id.Value == 1);
        InventoryId target = kind switch {
            "self" => source.InputId, "output" => Destination(s).OutputId,
            "personal" => s.Engine.Observe(new(1)).Participant.InventoryId, "cargo" => System(s).CargoInventory,
            "unknown" => new(999999), _ => s.Engine.Inspect().Buildings.Single(b => b.Id.Value == 2).InputId };
        var ledger = s.Engine.Ledger(); Assert.Equal("NotAvailable", LandContentFixtures.Reason(s, Route(s, destination: target)));
        Assert.Equal(ledger, s.Engine.Ledger()); Assert.Null(System(s).Trip); Assert.False(System(s).Enabled);
        Assert.Equal(0, System(s).OwnDestination.Value);
    }
    [Fact]
    public void IncompatibleRecipeAndCallerSuppliedItemQuantityContractOrForeignActorAreRejected()
    {
        var s = BeforeRoute();
        Assert.Equal("NotAvailable", LandContentFixtures.Reason(s, Route(s), 2));
        Assert.Equal("QuantityFromCapacity", LandContentFixtures.Reason(s, Route(s) with { Quantity = 100 }));
        Assert.Equal("ItemFromProduction", LandContentFixtures.Reason(s, Route(s) with { ItemId = "wood" }));
        Assert.Equal("ItemFromProduction", LandContentFixtures.Reason(s, Route(s) with { ContractId = 99999 }));
        var d = Scenario(); d = d with { Recipes = d.Recipes.Select(r => r.Id == "workshop" ? r with { InputItem = "wood" } : r).ToArray() };
        s = BeforeRoute(d); Assert.Equal("IncompatibleRecipe", LandContentFixtures.Reason(s, Route(s)));
        Assert.Null(System(s).Trip); Assert.Equal(0, s.Engine.Inspect().Accounts.Sum(a => a.ReservedMinor));
    }
    [Fact]
    public void OutputStopFinishesStartedTripAndPersistsWhileProductionRemainsIndependent()
    {
        // Replay only the prefix so the later fixture commands cannot resume the route.
        var s = BeforeRoute(); Assert.Equal("Accepted", LandContentFixtures.Reason(s, Route(s))); s.Step(); s.Step();
        var t = System(s).Trip!; Assert.Equal("Outbound", t.State);
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, Route(s, false)));
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        s.AdvanceTo(t.EndTick + 5); loaded.AdvanceTo(t.EndTick + 5);
        Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        Assert.Equal(1, System(s).CompletedTrips); Assert.Equal(400, Wages(s));
        Assert.Equal(10, Fixtures.Count(s, Destination(s).InputId, "plank"));
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.SetProductionEnabled, Enterprise: Destination(s).Id, Enabled: true)));
        s.AdvanceTo(s.Engine.Tick + 10); Assert.Equal(5, Fixtures.Count(s, Destination(s).OutputId, "crate"));
        Assert.False(System(s).Enabled); Assert.Equal(1, System(s).CompletedTrips); Assert.Equal(400, Wages(s));
    }
    [Fact]
    public void FullPausedProductionWaitsUnpaidThenResumesAfterManualSpaceRelease()
    {
        var s = BeforeRoute(); Assert.Equal("Accepted", LandContentFixtures.Reason(s, Route(s))); WaitTrips(s, 2); s.Step();
        Assert.Equal("NoDestinationSpace", System(s).ReasonCode); var ledger = s.Engine.Ledger();
        s.AdvanceTo(s.Engine.Tick + 5); Assert.Equal(ledger, s.Engine.Ledger());
        LandContentFixtures.Move(s, new(5, 0));
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.Transfer, Source: Destination(s).InputId,
            Destination: s.Engine.Observe(new(1)).Participant.InventoryId, ItemId: "plank", Quantity: 5)));
        WaitTrips(s, 3); Assert.Equal(15, Fixtures.Count(s, Destination(s).InputId, "plank"));
        Assert.Single(s.Engine.EventsSince(0), e => e.Type == "LogisticsProductionSet"); Assert.Equal(1200, Wages(s));
    }
    [Fact]
    public void RecipeChangePreservesCommittedCargoButBlocksNewIncompatibleTripsAndAllowsStop()
    {
        var d = Scenario(); d = d with {
            BuildingTypes = d.BuildingTypes!.Select(b => b.Id == "workshop" ? b with { Levels = [
                b.Levels[0] with { RecipeId = "sawmill" }, b.Levels[0] with { Level = 2 } ] } : b).ToArray(),
            Buildings = d.Buildings!.Select(b => b.Id == 2 ? b with { Level = 2 } : b).ToArray() };
        var s = BeforeRoute(d); Assert.Equal("Accepted", LandContentFixtures.Reason(s, Route(s))); s.Step();
        long end = System(s).Trip!.EndTick;
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.SetRecipe, Enterprise: Destination(s).Id, RecipeId: "sawmill")));
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s)); s.AdvanceTo(end + 3); loaded.AdvanceTo(end + 3);
        Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        Assert.Equal(10, Fixtures.Count(s, Destination(s).InputId, "plank")); Assert.Equal(1, System(s).CompletedTrips);
        Assert.Equal("IncompatibleRecipe", System(s).ReasonCode);
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, Route(s, false)));
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.SetRecipe, Enterprise: Destination(s).Id, RecipeId: "workshop")));
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, Route(s))); WaitTrips(s, 2);
        Assert.Equal(15, Fixtures.Count(s, Destination(s).InputId, "plank"));
    }
    [Fact]
    public void UnitVolumeBoundsBothCargoAndProductionInput()
    {
        var d = Scenario(); d = d with { Items = d.Items.Select(i => i.Id == "plank" ? i with { UnitVolume = 2 } : i).ToArray(),
            BuildingTypes = d.BuildingTypes!.Select(b => b.Id == "sawmill" ? b with {
                Levels = b.Levels.Select(l => l with { OutputCapacity = 80 }).ToArray() } : b).ToArray() };
        var s = BeforeRoute(d); Assert.Equal("Accepted", LandContentFixtures.Reason(s, Route(s))); WaitTrips(s, 2); s.Step();
        Assert.Equal(new long[] { 5, 2 }, s.Engine.EventsSince(0).Where(e => e.Type == "LogisticsProductionReceived").Select(e => e.Quantity));
        Assert.Equal(7, Fixtures.Count(s, Destination(s).InputId, "plank")); Assert.Equal("NoDestinationSpace", System(s).ReasonCode);
    }
    [Fact]
    public void StartedTripCannotBeRetargetedAndDuplicateCommandCannotReceiveTwice()
    {
        var d = Scenario(); d = d with {
            Plots = [.. d.Plots!, new(3, new(2, 1), 10000, OwnerId: 1)],
            Buildings = [.. d.Buildings!, new(3, 1, new(2, 1), "workshop", 1, 0, false)] };
        var s = BeforeRoute(d); Assert.Equal("Accepted", LandContentFixtures.Reason(s, Route(s))); s.Step();
        var t = System(s).Trip!;
        var other = s.Engine.Inspect().Enterprises.Single(e => e.Id.Value == 3).InputId;
        Assert.Equal("LogisticsBusy", LandContentFixtures.Reason(s, Route(s, destination: other)));
        string id = s.Send(new(1), Route(s)); s.Step();
        var command = s.CommandTape.Single(c => c.Command.CommandId == id).Command;
        Assert.Equal(ResultStatus.Accepted, s.Engine.Enqueue(new(1), command)!.Status);
        Assert.Equal(t.Id, System(s).Trip!.Id); WaitTrips(s, 2); s.Step();
        Assert.Equal(15, s.Engine.EventsSince(0).Where(e => e.Type == "LogisticsProductionReceived").Sum(e => e.Quantity));
        Assert.Equal(0, Fixtures.Count(s, other, "plank")); Assert.Equal(800, Wages(s));
    }
    [Fact]
    public void TwoSystemsShareFiniteDestinationCapacityWithoutPromisingSameSpace()
    {
        var d = Scenario(); d = d with {
            Plots = [.. d.Plots!, new(3, new(2, 1), 10000, OwnerId: 1)],
            Buildings = [.. d.Buildings!, new(3, 1, new(2, 1), "sawmill", 1, 0, false)] };
        var s = BeforeRoute(d); var first = System(s);
        var carrier = s.Engine.Observe(new(1)).Participant.InventoryId;
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.Transfer, Source: first.SourceInventory,
            Destination: carrier, ItemId: "plank", Quantity: 20)));
        LandContentFixtures.Move(s, new(2, 1));
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.BuyLogistics, Enterprise: new(3))));
        var second = s.Engine.Inspect().Logistics.Single(l => l.EnterpriseId.Value == 3);
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.Transfer, Source: carrier,
            Destination: second.SourceInventory, ItemId: "plank", Quantity: 20)));
        s.Send(new(1), new(ActionKind.SetLogisticsProduction, Logistics: first.Id, Destination: Destination(s).InputId, Enabled: true));
        s.Send(new(1), new(ActionKind.SetLogisticsProduction, Logistics: second.Id, Destination: Destination(s).InputId, Enabled: true));
        s.Step(); s.Step();
        var systems = s.Engine.Inspect().Logistics;
        Assert.All(systems, l => Assert.NotNull(l.Trip));
        Assert.Equal(15, systems.Sum(l => l.Trip!.Quantity));
        Assert.Equal(15, s.Engine.Inspect().Inventories.Single(i => i.Id == Destination(s).InputId).ReservedCapacity);
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        long end = systems.Max(l => l.Trip!.EndTick) + 5; s.AdvanceTo(end); loaded.AdvanceTo(end);
        Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        Assert.Equal(15, Fixtures.Count(s, Destination(s).InputId, "plank"));
        Assert.All(s.Engine.Inspect().Logistics, l => { Assert.Null(l.Trip); Assert.Equal(1, l.CompletedTrips); });
        Assert.Equal(0, s.Engine.Inspect().Inventories.Sum(i => i.ReservedCapacity));
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(4)] [InlineData(6)] [InlineData(15)]
    public void DirectCheckpointAcrossPreparationTravelReceiptReturnAndFullWaitMatches(int offset)
    {
        var s = At(PreparedTick + offset); var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        while (s.Engine.Tick < Baseline.Value.Engine.Tick) {
            s.Step(); loaded.Step(); Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash()); }
        Assert.Equal(s.Engine.Ledger(), loaded.Engine.Ledger()); Assert.Equal(s.Engine.EventsSince(0), loaded.Engine.EventsSince(0));
    }
    [Theory]
    [InlineData("self")] [InlineData("output")] [InlineData("hold")]
    public void CheckpointRejectsInvalidProductionReferencesOrMissingDestinationHold(string kind)
    {
        var s = At(PreparedTick + 1); var json = JsonNode.Parse(s.Engine.CreateCheckpointJson())!;
        var l = json["logistics"]![0]!["view"]!;
        if (kind == "hold") {
            foreach (var i in json["inventories"]!.AsArray())
                if (i!["id"]!.GetValue<long>() == Destination(s).InputId.Value) i["capacityHolds"] = new JsonArray();
        } else {
            long target = kind == "self" ? s.Engine.Inspect().Enterprises.Single(e => e.Id.Value == 1).InputId.Value : Destination(s).OutputId.Value;
            l["ownDestination"]!["value"] = target; l["trip"]!["ownDestination"]!["value"] = target;
        }
        Assert.Throws<CheckpointException>(() => SimulationEngine.FromCheckpointJson(json.ToJsonString(SimulationJson.Options)));
    }
    [Fact]
    public void ProductionRouteIsPrivateAndOlderRulesAreRejectedExplicitly()
    {
        var s = At(PreparedTick + 1);
        Assert.Single(s.Engine.ObserveController(new(1)).OwnLogistics);
        Assert.Empty(s.Engine.ObserveController(new(2)).OwnLogistics);
        Assert.DoesNotContain(s.Engine.ObserveController(new(2)).Inventories, i => i.Id == Destination(s).InputId);
        var json = JsonNode.Parse(s.Engine.CreateCheckpointJson())!; json["rulesVersion"] = "logistics-5";
        Assert.Equal("UnknownRulesVersion", Assert.Throws<CheckpointException>(() =>
            SimulationEngine.FromCheckpointJson(json.ToJsonString(SimulationJson.Options))).ReasonCode);
    }
}
