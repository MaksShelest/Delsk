using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class LogisticsReturnTests
{
    private static readonly Lazy<(ScenarioDefinition Definition, RecordedCommand[] Tape)> Baseline = new(() =>
    {
        using var stream = typeof(LogisticsReturnTests).Assembly.GetManifestResourceStream("Fixture.logistics-cycle.json")!;
        var d = JsonSerializer.Deserialize<ScenarioDefinition>(stream, SimulationJson.Options)!;
        var s = new GameSession(new SimulationEngine(d)); ScriptedScenarios.LogisticsCycle(s, d);
        return (d, s.CommandTape);
    });
    private static GameSession At(long tick, ScenarioDefinition? definition = null)
    {
        var s = new GameSession(new SimulationEngine(definition ?? Baseline.Value.Definition));
        s.ConfigurePlayback(Baseline.Value.Tape, 85, "logistics-return-baseline"); s.AdvanceTo(tick); return s;
    }
    private static LogisticsView Truck(GameSession s) => s.Engine.Inspect().Logistics.Single();
    private static LogisticsTripView Trip(GameSession s) => Truck(s).Trip!;
    private static DeliveryContractView Contract(GameSession s) => s.Engine.Inspect().DeliveryContracts.Single();
    private static EmploymentView Crew(GameSession s) => s.Engine.Inspect().EmploymentGroups.Single(g => g.Role == EmploymentRole.Logistics);
    private static InventoryView Inventory(GameSession s, InventoryId id) => s.Engine.Inspect().Inventories.Single(i => i.Id == id);
    private static long Wages(GameSession s) => s.Engine.Ledger().Where(l => l.Reason == "LogisticsWages").Sum(l => l.AmountMinor);
    private static string Send(GameSession s, GameAction action) => LandContentFixtures.Reason(s, action);

    // Deliberate adverse-state fixture, NOT a player command or genesis replay. A normal,
    // already-loaded trip is forked under a shorter contract TTL. Only tests use reflection;
    // production load still verifies every invariant, config/state hash and envelope checksum.
    private static GameSession Fault(int ttl = 18, bool smallOutput = false)
    {
        var d = Baseline.Value.Definition with { Population = 2, Logistics = new(CrewCapacity: 3) };
        if (smallOutput) d = d with
        {
            ResourceNodes = [.. d.ResourceNodes, new("wood", new(2, 0))],
            BuildingTypes = d.BuildingTypes!.Select(t => t.Kind != BuildingKind.Production ? t :
                t with { Levels = t.Levels.Select(l => l with { OutputCapacity = 10 }).ToArray() }).ToArray()
        };
        var parent = At(68, d);
        var session = JsonSerializer.Deserialize<SavedSession>(parent.CreateSessionJson(), SimulationJson.Options)!;
        var world = JsonSerializer.Deserialize<WorldCheckpoint>(session.WorldJson, SimulationJson.Options)!;
        var config = d with { Id = "controlled-late-return-fork", Delivery = d.Delivery! with { ContractTtlTicks = ttl } };
        var engine = new SimulationEngine(config);
        world = world with { Config = config, ConfigHash = engine.ConfigHash,
            DeliveryContracts = world.DeliveryContracts.Select(c => c with { DeadlineTick = checked(c.CreatedTick + ttl) }).ToArray() };
        typeof(SimulationEngine).GetMethod("RestoreWorld", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, [world]);
        // CreateCheckpointJson validates first; FromSessionJson performs the normal strict restore.
        return GameSession.FromSessionJson(JsonSerializer.Serialize(session with { WorldJson = engine.CreateCheckpointJson(),
            Samples = [], LaborSamples = [], Playback = new([], 80, 0, "controlled-late-return") }, SimulationJson.Options));
    }
    private static void AssertBalances(GameSession s)
    {
        var w = s.Engine.Inspect();
        Assert.Equal(200000, w.Accounts.Sum(a => a.BalanceMinor));
        Assert.Equal(w.Population, w.Unemployed + w.EmploymentGroups.Sum(g => (long)g.Headcount));
        Assert.All(w.Inventories, i => Assert.True(i.OccupiedVolume + i.ReservedCapacity <= i.Capacity));
        Assert.Equal(10, w.Inventories.Sum(i => i.Stock.Where(x => x.ItemId == "plank").Sum(x => x.Quantity)));
    }
    [Theory]
    [InlineData(17, "Expired", 0)] [InlineData(18, "Expired", 0)] [InlineData(19, "Fulfilled", 10)]
    public void PhysicalArrivalBoundaryAcceptsOnlyInclusiveDeadline(int ttl, string status, long delivered)
    {
        var s = Fault(ttl); s.AdvanceTo(71);
        Assert.Equal(status, Contract(s).Status); Assert.Equal(delivered, Contract(s).DeliveredQuantity);
        Assert.Equal(delivered == 0 ? "ReturningCargo" : "ReturningEmpty", Trip(s).State);
        s.AdvanceTo(80); Assert.Null(Truck(s).Trip); Assert.Equal(1, Truck(s).CompletedTrips);
        Assert.Equal(300, Wages(s)); AssertBalances(s);
    }
    [Fact]
    public void ExpirySettlesMoneyAndDestinationButKeepsLoadedTruckReturnPlaceAndPayroll()
    {
        var s = Fault(); long id = Trip(s).Id; s.AdvanceTo(70); var c = Contract(s);
        Assert.Equal("Expired", c.Status); Assert.Equal(30000, c.RefundedMinor); Assert.Equal(1500, c.FineMinor);
        Assert.Equal(0, c.PaidMinor); Assert.Equal(0, c.PreparedQuantity); Assert.Equal(0, c.InTransitQuantity);
        Assert.Equal("Outbound", Trip(s).State); Assert.Equal(new Cell(4, 0), Trip(s).Position);
        Assert.Equal(10, Inventory(s, Truck(s).CargoInventory).Stock.Single().Reserved);
        Assert.Equal(10, Inventory(s, Truck(s).SourceInventory).ReservedCapacity);
        Assert.Empty(Inventory(s, c.DestinationInventory!.Value).Stock);
        Assert.Equal(0, Inventory(s, c.DestinationInventory.Value).ReservedCapacity);
        Assert.Equal(350, s.Engine.Observe(new(1)).Account.ReservedMinor);
        s.Step(); Assert.Equal("ReturningCargo", Trip(s).State); Assert.Equal(new Cell(5, 0), Trip(s).Position);
        Assert.Single(s.Engine.EventsSince(0), e => e.EntityId == id && e.Type == "LogisticsDeliveryRejected");
        s.AdvanceTo(73); Assert.Equal(new Cell(3, 0), Trip(s).Position); Assert.Empty(Inventory(s, Truck(s).SourceInventory).Stock);
        s.Step(); Assert.Null(Truck(s).Trip); Assert.Equal(10, Inventory(s, Truck(s).SourceInventory).Stock.Single().Quantity);
        Assert.Empty(Inventory(s, Truck(s).CargoInventory).Stock); Assert.Equal(0, Inventory(s, Truck(s).SourceInventory).ReservedCapacity);
        Assert.Equal(0, s.Engine.Observe(new(1)).Account.ReservedMinor); Assert.Equal(300, Wages(s));
        Assert.Equal(76700, s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(101500, s.Engine.Observe(new(2)).Account.BalanceMinor); AssertBalances(s);
        var ledger = s.Engine.Ledger(); s.AdvanceTo(80); Assert.Equal(ledger, s.Engine.Ledger());
        Assert.Single(s.Engine.EventsSince(0), e => e.Type == "LogisticsCargoReturned");
        Assert.Single(s.Engine.EventsSince(0), e => e.Type == "DeliveryExpired");
    }
    [Theory]
    [InlineData(68)] [InlineData(69)] [InlineData(70)] [InlineData(71)] [InlineData(72)] [InlineData(73)]
    public void PauseAndStrictRestoreContinueEveryTickAcrossExpiryRejectionAndLoadedReturn(int cut)
    {
        var s = Fault(); s.AdvanceTo(cut); s.SetPaused(true);
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s)); string hash = s.Engine.ComputeStateHash();
        s.Step(); loaded.Step(); Assert.Equal(hash, s.Engine.ComputeStateHash()); Assert.Equal(hash, loaded.Engine.ComputeStateHash());
        s.SetPaused(false); loaded.SetPaused(false);
        while (s.Engine.Tick < 80)
        { s.Step(); loaded.Step(); Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash()); }
        Assert.Equal(s.Engine.Ledger(), loaded.Engine.Ledger()); Assert.Equal(s.Engine.EventsSince(0), loaded.Engine.EventsSince(0));
        AssertBalances(s);
    }
    [Theory]
    [InlineData(0)] [InlineData(100)]
    public void StopAndWageChangeDuringLateTripWaitForPhysicalReturn(long wage)
    {
        var s = Fault(); var crew = Crew(s); long end = Trip(s).EndTick;
        s.Send(new(1), new(ActionKind.SetLogisticsContract, Logistics: Truck(s).Id, ContractId: Contract(s).Id, Enabled: false));
        s.Send(new(1), new(ActionKind.SetWage, Group: crew.Id, WageMinor: wage)); s.Step();
        Assert.False(Truck(s).Enabled); Assert.Equal(25, Crew(s).WageMinor); Assert.Equal(end + 1, Crew(s).PayEffectiveTick);
        s.AdvanceTo(end); Assert.Null(Truck(s).Trip); Assert.Equal(25, Crew(s).WageMinor); Assert.Equal(300, Wages(s));
        Assert.Equal(0, s.Engine.Observe(new(1)).Account.ReservedMinor);
        s.Step(); Assert.Equal(wage, Crew(s).WageMinor); Assert.Equal(300, Wages(s)); AssertBalances(s);
    }
    [Fact]
    public void ReturnCapacityCannotBeSpentWhileCargoIsAwayEvenAfterContractExpiry()
    {
        var s = Fault(smallOutput: true);
        Assert.Equal("Accepted", Send(s, new(ActionKind.Harvest, ItemId: "wood", Quantity: 1)));
        s.AdvanceTo(71); var avatar = s.Engine.Observe(new(1)).Participant.InventoryId;
        Assert.Equal("NoCapacity", Send(s, new(ActionKind.Transfer, Source: avatar,
            Destination: Truck(s).SourceInventory, ItemId: "wood", Quantity: 1)));
        Assert.Equal(1, Fixtures.Count(s, avatar, "wood")); Assert.Equal(10, Inventory(s, Truck(s).SourceInventory).ReservedCapacity);
        s.AdvanceTo(74); Assert.Equal(10, Fixtures.Count(s, Truck(s).SourceInventory, "plank")); AssertBalances(s);
    }
    [Theory]
    [InlineData("returnPlace")] [InlineData("cargoHold")] [InlineData("receiptEvent")] [InlineData("state")]
    public void ReturningCargoStillRejectsCorruptReservationsHistoryOrState(string field)
    {
        var s = Fault(); s.AdvanceTo(72); var json = JsonNode.Parse(s.Engine.CreateCheckpointJson())!;
        if (field == "returnPlace")
            json["inventories"]!.AsArray().Single(i => i!["id"]!.GetValue<long>() == Truck(s).SourceInventory.Value)!["capacityHolds"] = new JsonArray();
        else if (field == "cargoHold")
            json["inventories"]!.AsArray().Single(i => i!["id"]!.GetValue<long>() == Truck(s).CargoInventory.Value)!["reservations"] = new JsonArray();
        else if (field == "receiptEvent")
            json["events"]!.AsArray().Single(e => e!["type"]!.GetValue<string>() == "LogisticsDeliveryRejected")!["type"] = "LogisticsDeliveryAccepted";
        else json["logistics"]![0]!["view"]!["trip"]!["state"] = "ReturningEmpty";
        Assert.Equal("InvalidWorldCheckpoint", Assert.Throws<CheckpointException>(() => SimulationEngine.FromCheckpointJson(json.ToJsonString(SimulationJson.Options))).ReasonCode);
    }
    [Fact]
    public void WageScheduledInPreparationTickIsDeferredBeforeTripCanStart()
    {
        var s = At(66); Assert.Null(Truck(s).Trip);
        Assert.Equal("Accepted", Send(s, new(ActionKind.SetWage, Group: Crew(s).Id, WageMinor: 100)));
        Assert.Equal("Loading", Trip(s).State); Assert.Equal(25, Trip(s).CommittedWageMinor);
        Assert.Equal(75, Crew(s).PayEffectiveTick); Assert.Equal(75, Crew(s).AttractionEffectiveTick);
        Assert.Single(s.Engine.EventsSince(0), e => e.Type == "LogisticsWageDeferred");
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        s.AdvanceTo(75); loaded.AdvanceTo(75); Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        Assert.Equal(300, Wages(s)); Assert.Equal(100, Crew(s).WageMinor);
    }
    [Fact]
    public void FailedPreparationDoesNotDeferScheduledWage()
    {
        var d = Baseline.Value.Definition with { Participants = Baseline.Value.Definition.Participants
            .Select(p => p.Id == 1 ? p with { CashMinor = 23299 } : p).ToArray() };
        var s = At(66, d);
        Assert.Equal("Accepted", Send(s, new(ActionKind.SetWage, Group: Crew(s).Id, WageMinor: 100)));
        Assert.Null(Truck(s).Trip); Assert.Equal(68, Crew(s).PayEffectiveTick);
        Assert.DoesNotContain(s.Engine.EventsSince(0), e => e.Type == "LogisticsWageDeferred");
        s.Step(); Assert.Equal(100, Crew(s).WageMinor); Assert.Null(Truck(s).Trip); Assert.Equal(0, Wages(s));
    }
    [Fact]
    public void CheckpointCannotActivatePendingWageInsideCommittedTrip()
    {
        var s = At(69); Send(s, new(ActionKind.SetWage, Group: Crew(s).Id, WageMinor: 100));
        var json = JsonNode.Parse(s.Engine.CreateCheckpointJson())!;
        json["logistics"]![0]!["group"]!["payEffectiveTick"] = 72;
        Assert.Equal("InvalidWorldCheckpoint", Assert.Throws<CheckpointException>(() => SimulationEngine.FromCheckpointJson(json.ToJsonString(SimulationJson.Options))).ReasonCode);
    }
    [Fact]
    public void GrowingCrewPaysActualHeadcountWithinOriginalUpperBudget()
    {
        var d = Baseline.Value.Definition with { Population = 2, Logistics = new(CrewCapacity: 3, MoveTicksPerCell: 2),
            Labor = Baseline.Value.Definition.Labor! with { GrowthPeriodTicks = 70, GrowthPerPeriod = 1 } };
        var s = At(68, d); Assert.Equal(2, Crew(s).Headcount); Assert.Equal(900, Trip(s).WageBudgetMinor);
        s.AdvanceTo(71); Assert.Equal(3, Crew(s).Headcount); Assert.Equal(175, Wages(s));
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s)); s.AdvanceTo(81); loaded.AdvanceTo(81);
        Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash()); Assert.Equal(850, Wages(s));
        Assert.Equal(3, s.Engine.Inspect().Population); Assert.Equal(0, s.Engine.Observe(new(1)).Account.ReservedMinor);
        AssertBalances(s);
    }
    [Fact]
    public void CompetitionCanRemoveCrewBelowMinimumWithoutDeletingOrDelayingCommittedCargo()
    {
        var d = Baseline.Value.Definition with { Population = 2, Logistics = new(CrewCapacity: 3, MoveTicksPerCell: 2),
            ResourceNodes = [.. Baseline.Value.Definition.ResourceNodes, new("wood", new(2, 0))] };
        var s = At(68, d); long end = Trip(s).EndTick, arrival = Trip(s).ArrivalTick;
        Assert.Equal("Accepted", Send(s, new(ActionKind.Harvest, ItemId: "wood", Quantity: 2)));
        s.AdvanceTo(73); var o = s.Engine.Observe(new(1)); var factory = o.OwnEnterprises.Single();
        s.Send(new(1), new(ActionKind.SetWage, Group: new(factory.Id.Value), WageMinor: 1000));
        s.Send(new(1), new(ActionKind.SetProductionEnabled, Enterprise: factory.Id, Enabled: true));
        s.Send(new(1), new(ActionKind.Transfer, Source: o.Participant.InventoryId, Destination: factory.InputId, ItemId: "wood", Quantity: 2));
        s.Step(); s.Step(); Assert.True(Crew(s).Headcount < d.Logistics.MinimumCrew);
        Assert.Equal(arrival, Trip(s).ArrivalTick); Assert.Equal(end, Trip(s).EndTick);
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s)); s.AdvanceTo(81); loaded.AdvanceTo(81);
        Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash()); Assert.Equal("Fulfilled", Contract(s).Status);
        Assert.Equal(1, Truck(s).CompletedTrips); Assert.InRange(Wages(s), 1, 599);
        Assert.Equal(0, s.Engine.Observe(new(1)).Account.ReservedMinor);
        var samples = s.LaborSamples.ToDictionary(x => x.Tick);
        foreach (var e in s.Engine.EventsSince(0).Where(e => e.Type == "LogisticsWagesPaid"))
            Assert.Equal(samples[e.Tick - 1].EmploymentGroups.Single(g => g.Role == EmploymentRole.Logistics).Headcount * 25, e.AmountMinor);
        Assert.Equal(2, s.Engine.Inspect().Population);
        Assert.Equal(s.Engine.Inspect().Population, s.Engine.Inspect().Unemployed + s.Engine.Inspect().EmploymentGroups.Sum(g => (long)g.Headcount));
    }
    [Fact]
    public void PreviousRulesAreRejectedWithoutRewritingOldSave()
    {
        var s = At(69); var json = JsonNode.Parse(s.Engine.CreateCheckpointJson())!;
        json["rulesVersion"] = "logistics-4";
        Assert.Equal("UnknownRulesVersion", Assert.Throws<CheckpointException>(() =>
            SimulationEngine.FromCheckpointJson(json.ToJsonString(SimulationJson.Options))).ReasonCode);
    }
    [Fact]
    public void ControlledLateReturnExportsEvidenceAndLoadableReturnCheckpointWhenRequested()
    {
        var s = Fault(); s.AdvanceTo(70);
        string? output = Environment.GetEnvironmentVariable("DELSK_RETURN_ARTIFACTS");
        if (output is not null) RunArtifacts.Export(s, s.Engine.Configuration(), Path.Combine(output, "late-return-at-expiry"),
            "controlled-checkpoint-fork", "controlled-late-return", 0);
        s.AdvanceTo(72); var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        if (output is not null)
        {
            Directory.CreateDirectory(output); CheckpointStore.Save(s, Path.Combine(output, "late-return.save.json"));
            File.WriteAllText(Path.Combine(output, "late-return-fault.json"), JsonSerializer.Serialize(new
            {
                Author = "Шелестов Максим Евгеньевич", Kind = "test-only-controlled-checkpoint-fork",
                ParentScenario = Baseline.Value.Definition.Id, ParentTick = 68,
                Change = "ContractTtlTicks 180 -> 18; contract deadline created+18; loaded trip/path/payroll/goods unchanged",
                Admission = "Ordinary game admission still rejects ETA after deadline; this is not genesis replay or a gameplay delay",
                CheckpointTick = 72, FinalTick = 80
            }, SimulationJson.Options));
        }
        s.AdvanceTo(80); loaded.AdvanceTo(80); Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash()); AssertBalances(s);
        if (output is not null) RunArtifacts.Export(s, s.Engine.Configuration(), Path.Combine(output, "late-return"),
            "controlled-checkpoint-fork", "controlled-late-return", 0);
    }
}
