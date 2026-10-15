using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class LogisticsQueueTests
{
    private static ScenarioDefinition Scenario()
    {
        using var stream = typeof(LogisticsQueueTests).Assembly.GetManifestResourceStream("Fixture.logistics-queue.json")!;
        return JsonSerializer.Deserialize<ScenarioDefinition>(stream, SimulationJson.Options)!;
    }
    private static GameSession Run()
    { var d = Scenario(); var s = new GameSession(new SimulationEngine(d)); ScriptedScenarios.LogisticsQueueCycle(s, d); return s; }
    private static readonly Lazy<GameSession> Baseline = new(Run);
    private static long EnableTick => Baseline.Value.CommandTape.Single(c => c.Command.Action.Kind == ActionKind.SetLogisticsEnabled).SubmittedAtTick;
    private static long PreparedTick => Baseline.Value.Engine.EventsSince(0).First(e => e.Type == "LogisticsTripPrepared").Tick;
    private static LogisticsView System(GameSession s) => s.Engine.Inspect().Logistics.Single();
    private static DeliveryContractView[] Contracts(GameSession s) => s.Engine.Inspect().DeliveryContracts.OrderBy(d => d.DeadlineTick).ThenBy(d => d.Id).ToArray();
    private static InventoryId Storage(GameSession s) => s.Engine.Inspect().Buildings.Single(b => b.Id.Value == 2).InputId;
    private static InventoryId Receiver(GameSession s) => s.Engine.Inspect().Buildings.Single(b => b.Id.Value == 3).InputId;
    private static GameSession BeforeEnable(ScenarioDefinition? d = null)
    {
        var s = new GameSession(new SimulationEngine(d ?? Scenario()));
        foreach (var c in Baseline.Value.CommandTape.Where(c => c.SubmittedAtTick < EnableTick))
        { s.AdvanceTo(c.SubmittedAtTick); s.Submit(c.AuthenticatedActor, c.Command); }
        s.AdvanceTo(EnableTick); return s;
    }
    private static GameSession At(long tick)
    {
        var s = new GameSession(new SimulationEngine(Scenario()));
        s.ConfigurePlayback(Baseline.Value.CommandTape, Baseline.Value.Engine.Tick, "logistics-queue"); s.AdvanceTo(tick); return s;
    }
    private static void Execute(GameSession s, GameAction a, long actor = 1) => Assert.Equal("Accepted", LandContentFixtures.Reason(s, a, actor));
    private static GameAction Direction(GameSession s, bool enabled, LogisticsDirection direction = LogisticsDirection.Output) =>
        new(ActionKind.SetLogisticsEnabled, Logistics: System(s).Id, Enabled: enabled, Direction: direction);
    private static GameAction Job(GameSession s, long contract = 0, InventoryId destination = default, InventoryId source = default, bool enabled = true) =>
        new(ActionKind.QueueLogisticsJob, Logistics: System(s).Id, ContractId: contract, Destination: destination, Source: source, Enabled: enabled);
    private static void DisableJobs(GameSession s)
    {
        foreach (var j in System(s).Queue) Execute(s, Job(s, j.ContractId, j.OwnDestination, j.InboundSource, false));
    }
    [Fact]
    public void NearestDeadlinePrecedesOutputAndSequentialTripsUseOnlyRemainingQuota()
    {
        var s = Run(); var contracts = Contracts(s);
        var dispatches = s.Engine.EventsSince(0).Where(e => e.Type == "LogisticsJobDispatched").ToArray();
        Assert.Equal(new[] { $"contract:{contracts[0].Id}", $"contract:{contracts[0].Id}", $"contract:{contracts[1].Id}", $"output:{Storage(s).Value}" }, dispatches.Select(e => e.ReasonCode));
        Assert.Equal(new long[] { 10, 3, 7, 10 }, s.Engine.EventsSince(0).Where(e => e.Type == "LogisticsTripPrepared").Select(e => e.Quantity));
        Assert.All(contracts, d => { Assert.Equal("Fulfilled", d.Status); Assert.Equal(0, d.RemainingQuantity); Assert.Equal(0, d.FineMinor); });
        Assert.Equal(20, Fixtures.Count(s, Receiver(s), "plank")); Assert.Equal(10, Fixtures.Count(s, Storage(s), "plank"));
        Assert.Equal(4, System(s).DispatchCount); Assert.Equal(4, System(s).CompletedTrips);
        Assert.Equal(133700, s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(200000, s.Engine.Inspect().Accounts.Sum(a => a.BalanceMinor));
    }
    [Fact]
    public void WaitingQueueHoldsOnlyIntentAndDuplicateUpsertDoesNotResetFairness()
    {
        var s = BeforeEnable(); var l = System(s);
        Assert.Equal(3, l.Queue.Length); Assert.Equal(0, l.DispatchCount); Assert.Null(l.Trip);
        Assert.Equal(30, Fixtures.Count(s, l.SourceInventory, "plank"));
        Assert.Equal(0, s.Engine.Inspect().Inventories.Single(i => i.Id == l.SourceInventory).Stock.Single().Reserved);
        Assert.Equal(0, s.Engine.Ledger().Where(e => e.Reason == "LogisticsWages").Sum(e => e.AmountMinor));
        Execute(s, Job(s, Contracts(s)[0].Id)); Assert.Equal(3, System(s).Queue.Length);
        Execute(s, Direction(s, true)); s.Step(); var t = System(s).Trip!;
        var served = System(s).Queue.Single(j => j.Key == t.JobKey).LastServed;
        Execute(s, Job(s, Contracts(s)[0].Id));
        Assert.Equal(t.Id, System(s).Trip!.Id); Assert.Equal(served, System(s).Queue.Single(j => j.Key == t.JobKey).LastServed);
    }
    [Fact]
    public void DirectionStopPersistsQueueAndFinishesPaidTripThenResumeCompletesIt()
    {
        var s = At(PreparedTick + 1); var t = System(s).Trip!;
        Execute(s, Direction(s, false)); var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        long stop = t.EndTick + 10; s.AdvanceTo(stop); loaded.AdvanceTo(stop);
        Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        Assert.Equal(1, System(s).CompletedTrips); Assert.Equal(3, System(s).Queue.Length);
        var wages = s.Engine.Ledger(); s.AdvanceTo(stop + 3); Assert.Equal(wages, s.Engine.Ledger()); loaded.AdvanceTo(stop + 3);
        Execute(s, Direction(s, true)); Execute(loaded, Direction(loaded, true));
        while (System(s).CompletedTrips < 4) { s.Step(); loaded.Step(); Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash()); }
        Assert.All(Contracts(s), d => Assert.Equal("Fulfilled", d.Status));
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void UrgentInputPrecedesOutputOnlyWhileProductionIsEnabled(bool production)
    {
        var s = BeforeEnable(); DisableJobs(s);
        LandContentFixtures.Move(s, new(0, 0)); LandContentFixtures.Harvest(s, "wood", 4);
        LandContentFixtures.Move(s, new(5, 0));
        Execute(s, new(ActionKind.Transfer, Source: s.Engine.Observe(new(1)).Participant.InventoryId, Destination: Storage(s), ItemId: "wood", Quantity: 4));
        Execute(s, Job(s, destination: Storage(s)));
        Execute(s, Job(s, source: Storage(s)));
        var factory = s.Engine.Observe(new(1)).OwnEnterprises.Single();
        s.Send(new(1), new(ActionKind.SetProductionEnabled, Enterprise: factory.Id, Enabled: production));
        s.Send(new(1), Direction(s, true, LogisticsDirection.Inbound)); s.Send(new(1), Direction(s, true));
        s.Step(); s.Step();
        var t = System(s).Trip!;
        Assert.Equal(production ? Storage(s) : default, t.InboundSource);
        Assert.Equal(production ? "wood" : "plank", t.ItemId);
        // Stopping either other direction never removes the committed job or its cargo.
        Execute(s, Direction(s, false, production ? LogisticsDirection.Output : LogisticsDirection.Inbound));
        Assert.Equal(t.Id, System(s).Trip!.Id);
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        s.AdvanceTo(t.EndTick); loaded.AdvanceTo(t.EndTick);
        Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash()); Assert.Equal(1, System(s).CompletedTrips);
    }
    private static GameSession EqualContracts(ulong seed)
    {
        var s = BeforeEnable(Scenario() with { Seed = seed }); DisableJobs(s);
        for (int i = 0; i < 2; i++) s.Send(new(2), new(ActionKind.CreateBuyOffer, Destination: Receiver(s), ItemId: "plank", Quantity: 20, UnitPriceMinor: 1000));
        s.Step(); var offers = s.Engine.Observe(new(2)).BuyOffers.OrderByDescending(o => o.Id).Take(2).ToArray();
        foreach (var o in offers) s.Send(new(1), new(ActionKind.AcceptDeliveryQuantity, BuyOfferId: o.Id, Quantity: 20));
        s.Step(); s.AdvanceTo(s.Engine.Tick + 5);
        var contracts = s.Engine.Inspect().DeliveryContracts.Where(d => offers.Any(o => o.Id == d.BuyOfferId)).ToArray();
        Assert.Equal(2, contracts.Length); Assert.Equal(contracts[0].DeadlineTick, contracts[1].DeadlineTick);
        foreach (var d in contracts) Execute(s, Job(s, d.Id));
        Execute(s, Direction(s, true)); return s;
    }
    [Fact]
    public void EqualDeadlinesRotateAcrossTripsAndInitialWinnerVariesWithSeed()
    {
        var winners = new HashSet<string>();
        for (ulong seed = 40; seed < 44; seed++)
        {
            var s = EqualContracts(seed); long before = s.Engine.EventsSince(0).Last().Sequence;
            var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
            long end = s.Engine.Tick + 45;
            while (s.Engine.Tick < end) { s.Step(); loaded.Step(); Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash()); }
            var keys = s.Engine.EventsSince(before).Where(e => e.Type == "LogisticsJobDispatched").Select(e => e.ReasonCode).ToArray();
            Assert.Equal(3, keys.Length); Assert.NotEqual(keys[0], keys[1]); Assert.Equal(keys[0], keys[2]); winners.Add(keys[0]);
            Assert.Equal(30, Contracts(s).Sum(d => d.DeliveredQuantity));
        }
        Assert.True(winners.Count > 1);
    }
    [Fact]
    public void ManualPartialReceiptUsesOnlyUnassignedQuotaWhileTruckIsInTransit()
    {
        var s = BeforeEnable(Scenario() with { Logistics = new(MoveTicksPerCell: 3) }); DisableJobs(s);
        long contract = Contracts(s)[0].Id; Execute(s, Job(s, contract));
        var carrier = s.Engine.Observe(new(1)).Participant.InventoryId;
        Execute(s, new(ActionKind.Transfer, Source: System(s).SourceInventory, Destination: carrier, ItemId: "plank", Quantity: 2));
        Execute(s, Direction(s, true)); s.Step(); var t = System(s).Trip!;
        LandContentFixtures.Move(s, new(5, 2)); Assert.True(s.Engine.Tick < t.ArrivalTick);
        Execute(s, new(ActionKind.DeliverContract, ContractId: contract, Quantity: 2));
        var d = Contracts(s)[0]; Assert.Equal(2, d.DeliveredQuantity); Assert.Equal(10, d.InTransitQuantity); Assert.Equal(1, d.UnprovidedQuantity);
        Assert.Equal("InvalidQuantity", LandContentFixtures.Reason(s, new(ActionKind.DeliverContract, ContractId: contract, Quantity: 2)));
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        while (Contracts(s)[0].Status != "Fulfilled" || System(s).Trip is not null) {
            s.Step(); loaded.Step(); Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash()); }
        Assert.Equal(13, Contracts(s)[0].DeliveredQuantity);
        Assert.Equal(new long[] { 10, 1 }, s.Engine.EventsSince(0).Where(e => e.Type == "LogisticsTripPrepared").Select(e => e.Quantity));
    }
    [Fact]
    public void TwoSystemsCannotReserveSameContractRemainder()
    {
        var d = Scenario(); d = d with { Plots = [.. d.Plots!, new(4, new(2, 1), 10000, OwnerId: 1)] };
        var s = BeforeEnable(d); DisableJobs(s); var first = System(s); long contract = Contracts(s)[0].Id;
        var carrier = s.Engine.Observe(new(1)).Participant.InventoryId;
        Execute(s, new(ActionKind.Transfer, Source: first.SourceInventory, Destination: carrier, ItemId: "plank", Quantity: 15));
        LandContentFixtures.Move(s, new(2, 1)); Execute(s, new(ActionKind.Build, BuildingTypeId: "sawmill", Target: new(2, 1)));
        var built = s.Engine.Inspect().Buildings.Single(b => b.Position == new Cell(2, 1));
        Execute(s, new(ActionKind.BuyLogistics, Enterprise: new(built.Id.Value)));
        var second = s.Engine.Inspect().Logistics.Single(l => l.EnterpriseId.Value == built.Id.Value);
        Execute(s, new(ActionKind.Transfer, Source: carrier, Destination: second.SourceInventory, ItemId: "plank", Quantity: 15));
        foreach (var l in new[] { first, second }) {
            Execute(s, new(ActionKind.QueueLogisticsJob, Logistics: l.Id, ContractId: contract, Enabled: true));
            s.Send(new(1), new(ActionKind.SetLogisticsEnabled, Logistics: l.Id, Enabled: true)); }
        s.Step(); s.Step();
        var systems = s.Engine.Inspect().Logistics; var active = systems.Where(l => l.Trip is not null).ToArray();
        Assert.Equal(2, active.Length); Assert.Equal(13, active.Sum(l => l.Trip!.Quantity));
        Assert.Equal(0, Contracts(s)[0].UnprovidedQuantity);
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        long end = active.Max(l => l.Trip!.EndTick) + 5; s.AdvanceTo(end); loaded.AdvanceTo(end);
        Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        Assert.Equal(13, Contracts(s)[0].DeliveredQuantity); Assert.Equal(0, Contracts(s)[0].PreparedQuantity + Contracts(s)[0].InTransitQuantity);
        Assert.Equal(39000, Contracts(s)[0].PaidMinor);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)] [InlineData(6)] [InlineData(10)] [InlineData(13)] [InlineData(25)]
    public void QueueAndEverySubsequentHashLedgerEventContinueAfterDirectLoad(int offset)
    {
        var s = At(PreparedTick + offset); var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        while (s.Engine.Tick < Baseline.Value.Engine.Tick) { s.Step(); loaded.Step(); Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash()); }
        Assert.Equal(s.Engine.Ledger(), loaded.Engine.Ledger()); Assert.Equal(s.Engine.EventsSince(0), loaded.Engine.EventsSince(0));
    }
    [Fact]
    public void CommandReplayAcrossLoadedQueueMatchesOriginal()
    {
        var s = At(PreparedTick + 2); var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        loaded.AdvanceTo(Baseline.Value.Engine.Tick);
        Assert.Equal(Baseline.Value.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        Assert.Equal(Baseline.Value.Engine.EventsSince(0), loaded.Engine.EventsSince(0));
    }
    [Theory]
    [InlineData("missing")] [InlineData("duplicate")] [InlineData("rotation")] [InlineData("foreign")]
    public void CorruptQueueOrDispatchHistoryIsRejected(string field)
    {
        var s = At(PreparedTick + 1); var json = JsonNode.Parse(s.Engine.CreateCheckpointJson())!;
        var view = json["logistics"]![0]!["view"]!; var queue = view["queue"]!.AsArray();
        if (field == "missing") queue.Clear();
        else if (field == "duplicate") queue.Add(queue[0]!.DeepClone());
        else if (field == "rotation") view["dispatchCount"] = 999999;
        else queue[0]!["contractId"] = 999999;
        Assert.Throws<CheckpointException>(() => SimulationEngine.FromCheckpointJson(json.ToJsonString(SimulationJson.Options)));
    }
    [Fact]
    public void QueueCommandsRejectForeignActorCallerQuotaAndMixedDestinations()
    {
        var s = BeforeEnable(); var a = Job(s, Contracts(s)[0].Id);
        Assert.Equal("NotAvailable", LandContentFixtures.Reason(s, a, 2));
        Assert.Equal("QuantityFromCapacity", LandContentFixtures.Reason(s, a with { Quantity = 10 }));
        Assert.Equal("ItemFromProduction", LandContentFixtures.Reason(s, a with { ItemId = "plank" }));
        Assert.Equal("InvalidLogisticsJob", LandContentFixtures.Reason(s, a with { Source = Storage(s) }));
        Assert.Empty(s.Engine.ObserveController(new(2)).OwnLogistics);
    }
}
