using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public sealed class EquipmentTests
{
    private static ScenarioDefinition Scenario()
    {
        using var stream = typeof(EquipmentTests).Assembly.GetManifestResourceStream("Fixture.equipment-cycle.json")!;
        return JsonSerializer.Deserialize<ScenarioDefinition>(stream, SimulationJson.Options)!;
    }
    private static GameSession Session(ScenarioDefinition? d = null) => Fixtures.Session(d ?? Scenario());
    private static EquipmentView Machine(GameSession s) => s.Engine.Observe(new(1)).OwnEquipment[0];
    private static EmploymentView Crew(GameSession s) => s.Engine.Observe(new(1)).OwnEmploymentGroups.Single(g => g.Id == Machine(s).GroupId);
    private static EquipmentView Buy(GameSession s, int level = 1)
    {
        string id = Fixtures.Command(s, new(ActionKind.BuyEquipment, Target: new(0, 0), EquipmentTypeId: "wood-extractor", EquipmentLevel: level));
        Assert.Equal("Accepted", s.Engine.Result(id)!.ReasonCode); return Machine(s);
    }
    private static void To(GameSession s, long tick) { while (s.Engine.Tick < tick) s.Step(); }
    private static long Wages(GameSession s) => s.Engine.Inspect().Accounts.Single(a => a.Kind == "WagesSink").BalanceMinor;
    private static string Command(GameSession s, GameAction a, long actor = 1) => s.Engine.Result(Fixtures.Command(s, a, actor))!.ReasonCode;
    private static void Mode(GameSession s, EquipmentMode mode) => Assert.Equal("Accepted", Command(s,
        new(ActionKind.SetEquipmentMode, Equipment: Machine(s).Id, Mode: mode)));
    private static GameSession Auto(int level = 1)
    { var s = Session(); Buy(s, level); Mode(s, EquipmentMode.Automatic); return s; }

    [Fact]
    public void PurchaseIsAtomic_PaysCity_AndCreatesPlacesNotPeople()
    {
        var s = Session(); var e = Buy(s);
        Assert.Equal(85000, s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(15000, s.Engine.Inspect().Accounts.Single(a => a.Kind == "Treasury").BalanceMinor);
        Assert.Equal(0, Crew(s).Headcount); Assert.Equal(7, s.Engine.Inspect().Unemployed);
        Assert.Equal(EmploymentRole.Extraction, Crew(s).Role);
        Assert.Equal(20, s.Engine.Inspect().Inventories.Single(i => i.Id == e.StorageId).Capacity);
        var d = Scenario() with { Participants = Scenario().Participants.Select(p => p with { CashMinor = 0 }).ToArray() };
        var poor = Session(d); var before = poor.Engine.Inspect().NextId;
        Assert.Equal("NoMoney", Command(poor, new(ActionKind.BuyEquipment, Target: new(0, 0), EquipmentTypeId: "wood-extractor")));
        Assert.Empty(poor.Engine.Inspect().Equipment); Assert.Empty(poor.Engine.Inspect().EmploymentGroups);
        Assert.Equal(before, poor.Engine.Inspect().NextId);
        Assert.Equal(2, poor.Engine.Inspect().Inventories.Length);
    }

    [Fact]
    public void PurchaseAndUseRequireNode_ValidCatalogAndOwnership()
    {
        var s = Session();
        Assert.Equal("NotAtResourceNode", Command(s, new(ActionKind.BuyEquipment, Target: new(0, 0), EquipmentTypeId: "wood-extractor"), 2));
        Assert.Equal("NotAvailable", Command(s, new(ActionKind.BuyEquipment, Target: new(0, 0), EquipmentTypeId: "missing")));
        Assert.Equal("EquipmentLevelUnavailable", Command(s, new(ActionKind.BuyEquipment, Target: new(0, 0), EquipmentTypeId: "wood-extractor", EquipmentLevel: 3)));
        var e = Buy(s);
        Assert.Equal("NotAvailable", Command(s, new(ActionKind.SetEquipmentMode, Equipment: e.Id, Mode: EquipmentMode.Automatic), 2));
        Assert.Equal("NotAvailable", Command(s, new(ActionKind.SetWage, Group: e.GroupId, WageMinor: 100), 2));
        Assert.Equal("NotAvailable", Command(s, new(ActionKind.Harvest, Equipment: e.Id, ItemId: "wood", Quantity: 2), 2));
        Assert.Equal("WrongItem", Command(s, new(ActionKind.Harvest, Equipment: e.Id, ItemId: "grain", Quantity: 2)));
        Fixtures.Command(s, new(ActionKind.MoveTo, Target: new(1, 0))); Fixtures.UntilIdle(s);
        Assert.Equal("NotAtLocation", Command(s, new(ActionKind.Harvest, Equipment: e.Id, ItemId: "wood", Quantity: 2)));
        Assert.Empty(s.Engine.Observe(new(2)).OwnEquipment);
    }

    [Theory]
    [InlineData(1, 4, 4)]
    [InlineData(2, 6, 2)]
    [InlineData(2, 7, 4)]
    public void PersonalModeHasRealDuration_ExactOutput_NoCrewPayroll(int level, long quantity, long duration)
    {
        var s = Session(); var e = Buy(s, level);
        Fixtures.Command(s, new(ActionKind.Harvest, Equipment: e.Id, ItemId: "wood", Quantity: quantity));
        long start = s.Engine.Tick; var actor = s.Engine.Observe(new(1)).Participant;
        Assert.Equal(start + duration, actor.ArrivalTick);
        Assert.Equal(0, Fixtures.Count(s, actor.InventoryId, "wood"));
        To(s, start + duration - 1); Assert.True(s.Engine.Observe(new(1)).Participant.Busy);
        s.Step(); Assert.Equal(quantity, Fixtures.Count(s, actor.InventoryId, "wood"));
        Assert.Equal(0, Machine(s).PersonalJobId); Assert.Equal(0, Wages(s));
        Assert.Equal(0, Fixtures.Count(s, e.StorageId, "wood"));
        Assert.Equal(quantity, s.Engine.Inspect().Flows.Single(f => f.ItemId == "wood").Harvested);
    }

    [Fact]
    public void PersonalOperationLocksOtherWorkAndMachine_AndDuplicateCommandDoesNotHarvestTwice()
    {
        var s = Session(); var e = Buy(s);
        var id = s.Send(new(1), new(ActionKind.Harvest, Equipment: e.Id, ItemId: "wood", Quantity: 6));
        var input = s.CommandTape.Last(); s.Step();
        Assert.Equal("Accepted", s.Engine.Result(id)!.ReasonCode);
        Assert.Equal("Accepted", s.Submit(input.AuthenticatedActor, input.Command)!.ReasonCode);
        Assert.Equal("ParticipantBusy", Command(s, new(ActionKind.Transfer, ItemId: "wood", Quantity: 1)));
        Assert.Equal("EquipmentBusy", Command(s, new(ActionKind.SetEquipmentMode, Equipment: e.Id, Mode: EquipmentMode.Automatic)));
        Assert.Equal("ParticipantBusy", Command(s, new(ActionKind.Harvest, ItemId: "wood", Quantity: 1)));
        Fixtures.UntilIdle(s); Assert.Equal(6, Fixtures.Count(s, s.Engine.Observe(new(1)).Participant.InventoryId, "wood"));
        Assert.Equal(1, s.Engine.EventsSince(0).Count(x => x.Type == "PersonalEquipmentHarvestCompleted"));
        Assert.Equal(0, Wages(s));
    }

    [Fact]
    public void PersonalCapacityAndOverflowFailWithoutReservationOrLostIds()
    {
        var s = Session(); var e = Buy(s); long next = s.Engine.Inspect().NextId;
        Assert.Equal("NoCapacity", Command(s, new(ActionKind.Harvest, Equipment: e.Id, ItemId: "wood", Quantity: 21)));
        Assert.Equal("ArithmeticOverflow", Command(s, new(ActionKind.Harvest, Equipment: e.Id, ItemId: "wood", Quantity: long.MaxValue)));
        Assert.Equal(next, s.Engine.Inspect().NextId); Assert.Equal(0, Machine(s).PersonalJobId);
        Assert.All(s.Engine.Inspect().Inventories, i => Assert.Equal(0, i.ReservedCapacity));
    }

    [Fact]
    public void AutomaticModeHiresFromZero_RequiresWholeMinimumCrew_AndPaysOnlyWork()
    {
        var s = Auto(); To(s, 99);
        Assert.Equal(1, Crew(s).Headcount); Assert.True(Crew(s).Ready); Assert.Equal(0, Wages(s));
        Assert.Equal(0, Machine(s).CompletedCycles); Assert.Equal("BelowMinimumCrew", Machine(s).ReasonCode);
        To(s, 100); Assert.Equal(2, Crew(s).Headcount); Assert.Equal(0, Wages(s));
        s.Step(); Assert.Equal(50, Wages(s)); Assert.Equal(2, Machine(s).WorkDone);
        s.Step(); Assert.Equal(100, Wages(s)); Assert.Equal(1, Machine(s).CompletedCycles);
        Assert.Equal(2, Fixtures.Count(s, Machine(s).StorageId, "wood"));
        Assert.Equal(0, Fixtures.Count(s, s.Engine.Observe(new(1)).Participant.InventoryId, "wood"));
    }

    [Fact]
    public void StopPausesWip_NoPay_NoDuplicatedResource_AndRejectsUnsafeChanges()
    {
        var s = Auto(); To(s, 101); var before = Machine(s); long wages = Wages(s);
        Mode(s, EquipmentMode.Off); Assert.Equal(before.WorkDone, Machine(s).WorkDone);
        Assert.Equal(before.CycleId, Machine(s).CycleId); Assert.Equal(wages, Wages(s)); Assert.Equal(2, Crew(s).Headcount);
        Assert.Equal("EquipmentBusy", Command(s, new(ActionKind.SetEquipmentMode, Equipment: before.Id, Mode: EquipmentMode.Personal)));
        Assert.Equal("EquipmentNotSafe", Command(s, new(ActionKind.UpgradeEquipment, Equipment: before.Id)));
        To(s, 109); Assert.Equal(wages, Wages(s));
        Mode(s, EquipmentMode.Automatic); // tick110 resumes before labor
        Assert.Equal(1, Machine(s).CompletedCycles); Assert.Equal(100, Wages(s));
        Assert.Equal(2, Fixtures.Count(s, before.StorageId, "wood"));
        Mode(s, EquipmentMode.Off); To(s, 250);
        Assert.Equal(0, Crew(s).Headcount); Assert.Equal(7, s.Engine.Inspect().Unemployed);
        Assert.Equal(100, Wages(s));
    }

    [Fact]
    public void OutputCapacityStopsWorkAndAttraction_AndLocalUnloadCanResume()
    {
        var d = Scenario(); d = d with { EquipmentTypes = d.EquipmentTypes!.Select(t => t with
        { Levels = t.Levels.Select(l => l with { StorageCapacity = l.CycleQuantity }).ToArray() }).ToArray() };
        var s = Session(d); Buy(s); Mode(s, EquipmentMode.Automatic); To(s, 103);
        Assert.Equal("NoOutputSpace", Machine(s).ReasonCode); Assert.Equal(100, Wages(s));
        To(s, 110); Assert.False(Crew(s).Ready); Assert.Equal("NoOutputSpace", Crew(s).ReadinessReason);
        var e = Machine(s);
        Assert.Equal("Accepted", Command(s, new(ActionKind.Transfer, Source: e.StorageId,
            Destination: s.Engine.Observe(new(1)).Participant.InventoryId, ItemId: "wood", Quantity: 2)));
        Assert.Equal(150, Wages(s)); s.Step(); Assert.Equal(2, Machine(s).CompletedCycles);
        Assert.Equal(4, s.Engine.Inspect().Flows.Single(f => f.ItemId == "wood").Harvested);
    }

    [Fact]
    public void UpgradeKeepsIdentityInventoryActualCrewAndWage_AndPaysFullNextLevelPrice()
    {
        var s = Auto(); To(s, 102); Mode(s, EquipmentMode.Off);
        var e = Machine(s); var g = Crew(s);
        Assert.Equal("Accepted", Command(s, new(ActionKind.SetWage, Group: g.Id, WageMinor: 37)));
        Assert.Equal("Accepted", Command(s, new(ActionKind.UpgradeEquipment, Equipment: e.Id)));
        Assert.Equal(e.Id, Machine(s).Id); Assert.Equal(e.StorageId, Machine(s).StorageId);
        Assert.Equal(2, Machine(s).Level); Assert.Equal(2, Crew(s).Headcount); Assert.Equal(5, Crew(s).Capacity);
        Assert.Equal(37, Crew(s).WageMinor); Assert.Equal(g.Id, Crew(s).Id);
        Assert.Equal(24900, s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal("MaxEquipmentLevel", Command(s, new(ActionKind.UpgradeEquipment, Equipment: e.Id)));
        Assert.Equal(2, Fixtures.Count(s, e.StorageId, "wood"));
    }

    [Fact]
    public void UpgradeWithInsufficientMoneyLeavesLevelCapacityAndGoodsUnchanged()
    {
        var d = Scenario(); d = d with { Participants = d.Participants.Select(p => p with { CashMinor = 15000 }).ToArray() };
        var s = Session(d); var e = Buy(s); Mode(s, EquipmentMode.Off);
        Assert.Equal("NoMoney", Command(s, new(ActionKind.UpgradeEquipment, Equipment: e.Id)));
        Assert.Equal(1, Machine(s).Level); Assert.Equal(2, Crew(s).Capacity);
        Assert.Equal(20, s.Engine.Inspect().Inventories.Single(i => i.Id == e.StorageId).Capacity);
    }

    [Fact]
    public void MachineCannotHireWithNoMoney_AndManualRecoveryRemainsAvailable()
    {
        var d = Scenario(); d = d with { Participants = d.Participants.Select(p => p with { CashMinor = 15000 }).ToArray() };
        var s = Session(d); var e = Buy(s); Mode(s, EquipmentMode.Automatic); To(s, 100);
        Assert.Equal(0, Crew(s).Headcount); Assert.Equal("NoMoney", Crew(s).ReadinessReason); Assert.Equal(0, Wages(s));
        Assert.Equal("EquipmentBusy", Command(s, new(ActionKind.Harvest, Equipment: e.Id, ItemId: "wood", Quantity: 2)));
        Assert.Equal("Accepted", Command(s, new(ActionKind.Harvest, ItemId: "wood", Quantity: 1)));
        Fixtures.UntilIdle(s); Assert.Equal(1, Fixtures.Count(s, s.Engine.Observe(new(1)).Participant.InventoryId, "wood"));
    }

    [Fact]
    public void ExtractionWageUsesSameDelayedContractAndPrivateProjection()
    {
        var s = Session(); var e = Buy(s);
        Assert.Equal("Accepted", Command(s, new(ActionKind.SetWage, Group: e.GroupId, WageMinor: 50)));
        Assert.Equal(25, Crew(s).WageMinor); Assert.Equal(25, Crew(s).AttractionWageMinor);
        s.Step(); Assert.Equal(50, Crew(s).WageMinor); Assert.Equal(25, Crew(s).AttractionWageMinor);
        To(s, 21); Assert.Equal(25, Crew(s).AttractionWageMinor); s.Step(); Assert.Equal(50, Crew(s).AttractionWageMinor);
        Assert.Empty(s.Engine.Observe(new(2)).OwnEmploymentGroups);
        Assert.Single(s.Engine.Observe(new(2)).LaborMarket, g => g.Id == e.GroupId && g.Role == EmploymentRole.Extraction);
        var observation = s.Engine.Observe(new(1)); observation.EquipmentTypes[0].Levels[0] = observation.EquipmentTypes[0].Levels[0] with { CycleQuantity = 999 };
        Assert.Equal(2, s.Engine.Observe(new(1)).EquipmentTypes[0].Levels[0].CycleQuantity);
    }

    [Fact]
    public void AutomaticWorkPersistsWhenOwnerWalksAway_BufferMustBePhysicallyCollected()
    {
        var s = Auto(); To(s, 100); var e = Machine(s);
        Fixtures.Command(s, new(ActionKind.MoveTo, Target: new(3, 0))); Fixtures.UntilIdle(s);
        Assert.True(Machine(s).CompletedCycles > 0);
        Assert.Equal("NotAtLocation", Command(s, new(ActionKind.Transfer, Source: e.StorageId,
            Destination: s.Engine.Observe(new(1)).Participant.InventoryId, ItemId: "wood", Quantity: 2)));
        Assert.Equal(0, Fixtures.Count(s, s.Engine.Observe(new(1)).Participant.InventoryId, "wood"));
    }

    [Fact]
    public void CompleteEquipmentScenarioConservesMoneyGoodsPopulation_AndReplaysEveryTick()
    {
        var d = Scenario(); var s = Session(d); ScriptedScenarios.EquipmentCycle(s, d);
        Assert.Equal(18, s.Engine.Inspect().Flows.Sum(f => f.Harvested));
        Assert.Equal(18, s.Engine.Inspect().Flows.Sum(f => f.Exported));
        Assert.Equal(250, Wages(s)); Assert.Equal(90000, s.Engine.Inspect().Accounts.Single(a => a.Kind == "Treasury").BalanceMinor);
        Assert.Equal(32750, s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(85600, s.Engine.Observe(new(2)).Account.BalanceMinor);
        Assert.Equal(7, s.Engine.Inspect().Unemployed);
        var replay = GameSession.Replay(d, s.CommandTape, s.Engine.Tick);
        Assert.Equal(s.Samples, replay.Samples); Assert.Equal(s.Engine.ComputeStateHash(), replay.Engine.ComputeStateHash());
        Assert.Equal(s.Engine.Ledger(), replay.Engine.Ledger());
    }

    [Theory]
    [InlineData("capacity")]
    [InlineData("crew")]
    [InlineData("node")]
    [InlineData("overflow")]
    public void InvalidEquipmentCatalogIsRejectedBeforeWorldCreation(string fault)
    {
        var d = Scenario(); var type = d.EquipmentTypes![0]; var level = type.Levels[0];
        type = fault == "node" ? type with { ItemId = "missing" } : type with { Levels = [fault switch
        { "capacity" => level with { StorageCapacity = 1 }, "crew" => level with { MinimumCrew = 3 },
            _ => level with { CycleTicks = long.MaxValue } }, type.Levels[1]] };
        Assert.Throws<ArgumentException>(() => new SimulationEngine(d with { EquipmentTypes = [type] }));
    }
    [Fact]
    public void ProductionAndExtractionShareOnePopulationAndWalletBudget()
    {
        var d = Scenario();
        d = d with
        {
            Items = [.. d.Items, new("plank", 1, 1500)],
            Recipes = [new("sawmill", "wood", 2, "plank", 1, 1000, 2, 1, 0)],
            Enterprises = [new(1, 1, new(0, 0), "sawmill", 40, 40, 0, 2, 25)],
            Participants = d.Participants.Select(p => p with { CashMinor = p.Id == 1 ? 15050 : 0 }).ToArray(),
            Population = 2, Labor = new(StepTicks: 5, MobilityNumerator: 1, MobilityDenominator: 1, GrowthPerPeriod: 0)
        };
        var s = Session(d); var e = Buy(s);
        Fixtures.Command(s, new(ActionKind.Harvest, Equipment: e.Id, ItemId: "wood", Quantity: 2)); Fixtures.UntilIdle(s);
        var o = s.Engine.Observe(new(1));
        Fixtures.Command(s, new(ActionKind.Transfer, Source: o.Participant.InventoryId, Destination: o.OwnEnterprises[0].InputId,
            ItemId: "wood", Quantity: 2));
        s.Send(new(1), new(ActionKind.SetProductionEnabled, Enterprise: new(1), Enabled: true));
        s.Send(new(1), new(ActionKind.SetEquipmentMode, Equipment: e.Id, Mode: EquipmentMode.Automatic)); s.Step();
        long step = ((s.Engine.Tick / 5) + 1) * 5; To(s, step);
        Assert.Single(s.Engine.Inspect().EmploymentGroups, g => g.Ready);
        Assert.Equal(2, s.Engine.Inspect().EmploymentGroups.Sum(g => g.Headcount));
        Assert.Equal(0, s.Engine.Inspect().Unemployed);
        s.Step(); Assert.Equal(50, Wages(s));
        To(s, 30); Assert.Equal(50, Wages(s));
        Assert.Equal(2, s.Engine.Inspect().Unemployed);
        Assert.Equal(2, s.Engine.Inspect().EmploymentGroups.Length);
        Assert.Equal(s.Engine.ComputeStateHash(), GameSession.Replay(d, s.CommandTape, s.Engine.Tick).Engine.ComputeStateHash());
    }

    [Fact]
    public void UpgradeRejectsOverflowingCurrentOrPendingWageWithoutPayment()
    {
        var s = Session(); var e = Buy(s); Mode(s, EquipmentMode.Off);
        Assert.Equal("Accepted", Command(s, new(ActionKind.SetWage, Group: e.GroupId, WageMinor: long.MaxValue / 2)));
        Assert.Equal("ArithmeticOverflow", Command(s, new(ActionKind.UpgradeEquipment, Equipment: e.Id)));
        Assert.Equal(85000, s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(1, Machine(s).Level); Assert.Equal(2, Crew(s).Capacity);
    }

    [Fact]
    public void RecordedEquipmentCommandsHaveSameTicksAndHashesUnderElapsedScheduler()
    {
        var d = Scenario(); var expected = Session(d); ScriptedScenarios.EquipmentCycle(expected, d);
        var actual = Session(d); var scheduler = new TickScheduler(actual);
        var tape = expected.CommandTape.GroupBy(c => c.SubmittedAtTick).ToDictionary(g => g.Key, g => g.ToArray());
        while (actual.Engine.Tick < expected.Engine.Tick)
        {
            if (tape.TryGetValue(actual.Engine.Tick, out var inputs))
                foreach (var c in inputs) actual.Submit(c.AuthenticatedActor, c.Command);
            scheduler.AdvanceElapsed(TimeSpan.FromMilliseconds(400)); scheduler.AdvanceElapsed(TimeSpan.FromMilliseconds(600));
        }
        Assert.Equal(expected.Samples, actual.Samples);
        Assert.Equal(expected.Engine.ComputeStateHash(), actual.Engine.ComputeStateHash());
    }

}
