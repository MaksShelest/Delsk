using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class LogisticsTests
{
    private static ScenarioDefinition Scenario()
    {
        using var stream = typeof(LogisticsTests).Assembly.GetManifestResourceStream("Fixture.logistics-cycle.json")!;
        return JsonSerializer.Deserialize<ScenarioDefinition>(stream, SimulationJson.Options)!;
    }
    private static GameSession Recording()
    { var d = Scenario(); var s = new GameSession(new SimulationEngine(d)); ScriptedScenarios.LogisticsCycle(s, d); return s; }
    private static GameSession At(long tick, ScenarioDefinition? d = null)
    {
        var record = Recording(); var s = new GameSession(new SimulationEngine(d ?? Scenario()));
        s.ConfigurePlayback(record.CommandTape, record.Engine.Tick, "logistics"); s.AdvanceTo(tick); return s;
    }
    private static LogisticsView System(GameSession s) => s.Engine.Inspect().Logistics.Single();
    private static DeliveryContractView Contract(GameSession s) => s.Engine.Inspect().DeliveryContracts.Single();
    private static EmploymentView Crew(GameSession s) => s.Engine.Inspect().EmploymentGroups.Single(g => g.Role == EmploymentRole.Logistics);
    private static string Reason(GameSession s, GameAction action, long actor = 1) => LandContentFixtures.Reason(s, action, actor);
    private static long LogisticsWages(GameSession s) => s.Engine.Ledger().Where(l => l.Reason == "LogisticsWages").Sum(l => l.AmountMinor);
    [Fact]
    public void ProductionToPhysicalContractDeliveryAndEmptyReturnPreserveEconomy()
    {
        var s = Recording(); Assert.Equal(76, s.Engine.Tick); Assert.Equal(1, System(s).CompletedTrips); Assert.Null(System(s).Trip);
        Assert.Equal("Fulfilled", Contract(s).Status); Assert.Equal(10, Contract(s).DeliveredQuantity); Assert.Equal(30000, Contract(s).PaidMinor);
        Assert.Equal(108100, s.Engine.Observe(new(1)).Account.BalanceMinor); Assert.Equal(70000, s.Engine.Observe(new(2)).Account.BalanceMinor);
        Assert.Equal(400, LogisticsWages(s)); Assert.Equal(900, s.Engine.Inspect().Accounts.Single(a => a.Kind == "WagesSink").BalanceMinor);
        Assert.Equal(200000, s.Engine.Inspect().Accounts.Sum(a => a.BalanceMinor));
        Assert.Equal(10, s.Engine.Inspect().Inventories.Sum(i => i.Stock.Where(stock => stock.ItemId == "plank").Sum(stock => stock.Quantity)));
        Assert.Equal(4, s.Engine.Inspect().Population); Assert.Equal(4, s.Engine.Inspect().Unemployed);
        Assert.Equal(0, s.Engine.Inspect().Accounts.Sum(a => a.ReservedMinor)); Assert.Equal(0, s.Engine.Inspect().Inventories.Sum(i => i.ReservedCapacity));
        var ledger = s.Engine.Ledger(); s.AdvanceTo(80); Assert.Equal(ledger, s.Engine.Ledger());
    }
    [Fact]
    public void PurchaseCreatesPlacesWithoutEmployeesAndChecksLocationMoneyOwnershipAndDuplicate()
    {
        var s = new GameSession(new SimulationEngine(Scenario()));
        Assert.Equal("NotAtLocation", Reason(s, new(ActionKind.BuyLogistics, Enterprise: new(1))));
        Assert.Equal("NotAvailable", Reason(s, new(ActionKind.BuyLogistics, Enterprise: new(1)), 2));
        LandContentFixtures.Move(s, new(2, 0)); Assert.Equal("Accepted", Reason(s, new(ActionKind.BuyLogistics, Enterprise: new(1))));
        Assert.Equal(0, Crew(s).Headcount); Assert.Equal(2, Crew(s).Capacity); Assert.Equal(4, s.Engine.Inspect().Unemployed);
        Assert.Equal(80000, s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal("LogisticsAlreadyOwned", Reason(s, new(ActionKind.BuyLogistics, Enterprise: new(1))));
        var d = Scenario(); d = d with { Participants = d.Participants.Select(p => p.Id == 1 ? p with { CashMinor = 19999, Position = new(2, 0) } : p).ToArray() };
        var poor = new GameSession(new SimulationEngine(d)); Assert.Equal("NoMoney", Reason(poor, new(ActionKind.BuyLogistics, Enterprise: new(1))));
        Assert.Empty(poor.Engine.Inspect().Logistics); Assert.Empty(poor.Engine.Ledger());
    }
    [Fact]
    public void PreparedQuotaLoadsOnceAndCargoHasRealPositionAndExclusiveReservation()
    {
        var s = At(67); Assert.Equal("Loading", System(s).Trip!.State); Assert.Equal(10, Contract(s).PreparedQuantity); Assert.Equal(0, Contract(s).InTransitQuantity);
        var source = s.Engine.Inspect().Inventories.Single(i => i.Id == System(s).SourceInventory);
        Assert.Equal(10, source.Stock.Single().Reserved); Assert.Equal(0, source.ReservedCapacity);
        s.Step(); Assert.Equal("Outbound", System(s).Trip!.State); Assert.Equal(0, Contract(s).PreparedQuantity); Assert.Equal(10, Contract(s).InTransitQuantity);
        source = s.Engine.Inspect().Inventories.Single(i => i.Id == System(s).SourceInventory); Assert.Empty(source.Stock); Assert.Equal(10, source.ReservedCapacity);
        var cargo = s.Engine.Inspect().Inventories.Single(i => i.Id == System(s).CargoInventory);
        Assert.Equal(10, cargo.Stock.Single().Quantity); Assert.Equal(10, cargo.Stock.Single().Reserved); Assert.Equal(new Cell(2, 0), cargo.Position);
        s.Step(); Assert.Equal(new Cell(3, 0), System(s).Trip!.Position); Assert.Equal(0, Contract(s).DeliveredQuantity); Assert.Equal(100, LogisticsWages(s));
        s.AdvanceTo(72); Assert.Equal(10, Contract(s).DeliveredQuantity); Assert.Equal("ReturningEmpty", System(s).Trip!.State);
        Assert.Empty(s.Engine.Inspect().Inventories.Single(i => i.Id == System(s).CargoInventory).Stock);
    }
    [Fact]
    public void ManualReceiptCannotSpendQuotaReservedForTransportAndTransferCannotStealOrAddCargo()
    {
        var s = At(68); var actor = s.Engine.Observe(new(1)); var system = System(s);
        Assert.Equal("ManagedCargo", Reason(s, new(ActionKind.Transfer, Source: system.CargoInventory,
            Destination: actor.Participant.InventoryId, ItemId: "plank", Quantity: 1)));
        Assert.Equal("ManagedCargo", Reason(s, new(ActionKind.Transfer, Destination: system.CargoInventory,
            Source: actor.Participant.InventoryId, ItemId: "plank", Quantity: 1)));
        // Avatar reaches the receiver after transport receipt; no second receipt or payment is possible.
        LandContentFixtures.Move(s, new(5, 0)); Assert.Equal("ContractClosed", Reason(s, new(ActionKind.DeliverContract, ContractId: Contract(s).Id, Quantity: 10)));
        Assert.Single(s.Engine.Ledger(), l => l.Reason == "DeliveryPayment");
    }
    [Fact]
    public void ManualReceiptAtReceiverBeforeTruckArrivalCannotConsumeItsTransitQuota()
    {
        var s = At(66); Fixtures.Command(s, new(ActionKind.MoveTo, Target: new(5, 0))); s.AdvanceTo(69);
        Assert.Equal("InvalidQuantity", Reason(s, new(ActionKind.DeliverContract, ContractId: Contract(s).Id, Quantity: 1)));
        Assert.Equal(new Cell(5, 0), s.Engine.Observe(new(1)).Participant.Position);
        Assert.Equal(10, Contract(s).InTransitQuantity); Assert.Equal(0, Contract(s).PaidMinor);
        s.AdvanceTo(72); Assert.Equal(10, Contract(s).DeliveredQuantity);
    }
    [Fact]
    public void UpperPayrollBudgetPaysActualHeadcountAndReturnsUnusedReservation()
    {
        var d = Scenario() with { Population = 2, Logistics = new(CrewCapacity: 3) };
        var s = At(69, d); Assert.Equal(2, Crew(s).Headcount); Assert.Equal(675, System(s).Trip!.WageBudgetMinor);
        Assert.Equal(100, System(s).Trip!.WagesPaidMinor); Assert.Equal(575, s.Engine.Observe(new(1)).Account.ReservedMinor);
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s)); s.AdvanceTo(75); loaded.AdvanceTo(75);
        Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash()); Assert.Equal(400, LogisticsWages(s));
        Assert.Equal(0, s.Engine.Observe(new(1)).Account.ReservedMinor);
    }
    [Fact]
    public void RouteQuantityComesFromContractAndRepeatedAssignmentCannotCreateSecondTrip()
    {
        var s = At(68); long trip = System(s).Trip!.Id;
        Assert.Equal("QuantityFromContract", Reason(s, new(ActionKind.SetLogisticsContract, Logistics: System(s).Id,
            ContractId: Contract(s).Id, Quantity: 99, Enabled: true)));
        Assert.Equal("NotAvailable", Reason(s, new(ActionKind.SetLogisticsContract, Logistics: System(s).Id, ContractId: Contract(s).Id, Enabled: true), 2));
        Assert.Equal("Accepted", Reason(s, new(ActionKind.SetLogisticsContract, Logistics: System(s).Id, ContractId: Contract(s).Id, Enabled: true)));
        Assert.Equal(trip, System(s).Trip!.Id); s.AdvanceTo(75); Assert.Equal(1, System(s).CompletedTrips);
        Assert.Single(s.Engine.EventsSince(0), e => e.Type == "LogisticsTripPrepared");
    }
    [Fact]
    public void PrefundedPayrollWorksWithZeroFreeCashAndInsufficientBudgetCannotStart()
    {
        ScenarioDefinition Cash(long amount) { var d = Scenario(); return d with { Participants = d.Participants.Select(p => p.Id == 1 ? p with { CashMinor = amount } : p).ToArray() }; }
        var funded = At(68, Cash(23450)); Assert.Equal(0, funded.Engine.Observe(new(1)).Account.AvailableMinor);
        Assert.Equal(2, Crew(funded).Headcount); funded.AdvanceTo(75); Assert.Equal(400, LogisticsWages(funded)); Assert.Equal("Fulfilled", Contract(funded).Status);
        Assert.Equal(31550, funded.Engine.Observe(new(1)).Account.BalanceMinor);
        var shortBudget = At(69, Cash(23449)); Assert.Null(System(shortBudget).Trip); Assert.Equal("NoMoney", Crew(shortBudget).ReadinessReason);
        Assert.Equal(0, LogisticsWages(shortBudget)); Assert.Equal(10, Contract(shortBudget).UnprovidedQuantity);
    }
    [Fact]
    public void MinimumCrewIsRequiredAndPurchaseDoesNotInventPeople()
    {
        var d = Scenario() with { Population = 2, Logistics = new(MinimumCrew: 3, CrewCapacity: 3) };
        var s = At(69, d); Assert.Equal(2, Crew(s).Headcount); Assert.Equal("NoStaff", System(s).ReasonCode); Assert.Null(System(s).Trip);
        Assert.Equal(2, s.Engine.Inspect().Population); Assert.Equal(0, LogisticsWages(s));
    }
    [Fact]
    public void CommittedWageRemainsFixedUntilReturnAndThenNewWageApplies()
    {
        var s = At(69); Assert.Equal("Accepted", Reason(s, new(ActionKind.SetWage, Group: Crew(s).Id, WageMinor: 100)));
        Assert.Equal(25, Crew(s).WageMinor); Assert.Equal(76, Crew(s).PayEffectiveTick); Assert.Equal(25, System(s).Trip!.CommittedWageMinor);
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s)); s.AdvanceTo(76); loaded.AdvanceTo(76);
        Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash()); Assert.Equal(400, LogisticsWages(s)); Assert.Equal(100, Crew(s).WageMinor);
    }
    [Fact]
    public void StopPreventsNewTripsButAlreadyStartedTripCompletesAndRemainsPaid()
    {
        var s = At(68); Assert.Equal("Accepted", Reason(s, new(ActionKind.SetLogisticsContract,
            Logistics: System(s).Id, ContractId: Contract(s).Id, Enabled: false)));
        s.AdvanceTo(80); Assert.Equal(1, System(s).CompletedTrips); Assert.False(System(s).Enabled);
        Assert.Equal(400, LogisticsWages(s)); Assert.Equal("Fulfilled", Contract(s).Status);
    }
    [Fact]
    public void ExactDeadlineReceiptCanFinishAndReturnAfterFinancialClosure()
    {
        var d = Scenario() with { Delivery = new(ContractTtlTicks: 20) }; var s = At(71, d);
        Assert.Equal(72, Contract(s).DeadlineTick); Assert.Equal(10, Contract(s).InTransitQuantity);
        s.Step(); Assert.Equal("Fulfilled", Contract(s).Status); Assert.Equal(0, Contract(s).FineMinor); Assert.NotNull(System(s).Trip);
        s.AdvanceTo(75); Assert.Equal(1, System(s).CompletedTrips); Assert.Equal(400, LogisticsWages(s));
    }
    [Fact]
    public void KnownLateArrivalDoesNotStartAndExpiryDoesNotDeleteSourceGoods()
    {
        var d = Scenario() with { Delivery = new(ContractTtlTicks: 18) }; var s = At(69, d);
        Assert.Null(System(s).Trip); Assert.Equal("ArrivalAfterDeadline", System(s).ReasonCode); Assert.Equal(0, LogisticsWages(s));
        s.AdvanceTo(75); Assert.Equal("Expired", Contract(s).Status); Assert.Equal(10, Contract(s).UnfulfilledQuantity);
        Assert.Equal(10, s.Engine.Inspect().Inventories.Single(i => i.Id == System(s).SourceInventory).Stock.Single().Quantity);
    }
    [Theory]
    [InlineData(45)] [InlineData(66)] [InlineData(67)] [InlineData(68)] [InlineData(69)] [InlineData(70)]
    [InlineData(71)] [InlineData(72)] [InlineData(73)] [InlineData(74)]
    public void DirectLoadReproducesEveryTickDuringHiringLoadingMovementAndReturn(int cut)
    {
        var s = At(cut); var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        while (s.Engine.Tick < 80) { s.Step(); loaded.Step(); Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash()); }
        Assert.Equal(s.Engine.Ledger(), loaded.Engine.Ledger()); Assert.Equal(s.Engine.EventsSince(0), loaded.Engine.EventsSince(0));
    }
    [Theory]
    [InlineData("pathIndex", 5)] [InlineData("quantity", 11)] [InlineData("arrivalTick", 999)]
    [InlineData("wagesPaidMinor", 0)] [InlineData("committedWageMinor", 100)]
    public void CorruptTripProgressCargoAndSalaryAreRejectedStructurally(string field, long value)
    {
        var s = At(69); var json = JsonNode.Parse(s.Engine.CreateCheckpointJson())!;
        json["logistics"]![0]!["view"]!["trip"]![field] = value;
        string reason = Assert.Throws<CheckpointException>(() => SimulationEngine.FromCheckpointJson(json.ToJsonString(SimulationJson.Options))).ReasonCode;
        Assert.Contains(reason, new[] { "InvalidWorldCheckpoint", "MissingReservation", "OrphanCapacityReservation", "OrphanMoneyReservation", "OrphanStockReservation" });
    }
    [Fact]
    public void OtherControllersDoNotGetTransportInventoryOrPrivateRoute()
    {
        var s = At(69); Assert.Single(s.Engine.ObserveController(new(1)).OwnLogistics); Assert.Empty(s.Engine.ObserveController(new(2)).OwnLogistics);
        var cargo = System(s).CargoInventory;
        Assert.DoesNotContain(s.Engine.ObserveController(new(2)).Inventories, i => i.Id == cargo);
        Assert.Single(s.Engine.Inspect().Logistics);
    }
    [Theory]
    [InlineData(0, 10, 2)] [InlineData(20000, 0, 2)] [InlineData(20000, 10, 0)]
    public void InvalidTransportSettingsFailBeforeCreatingWorld(long price, long capacity, int crew)
        => Assert.Throws<ArgumentException>(() => new SimulationEngine(Scenario() with { Logistics = new(PurchasePriceMinor: price, CargoCapacity: capacity, MinimumCrew: crew, CrewCapacity: crew) }));
}
