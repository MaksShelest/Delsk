using Xunit;
using Simulation.Core;
using Simulation.Hosting;

namespace Simulation.Tests;

public sealed class LaborTests
{
    [Fact]
    public void CubicWeights_AndSaturatedCapacity_HaveIndependentNumericOracles()
    {
        var random = new DeterministicRandom(42);
        var a = LaborAllocation.Cubic(100, [new(1, 30, 10), new(2, 90, 10)], random);
        Assert.Equal(30, a[1]); Assert.Equal(70, a[2]);
        a = LaborAllocation.Cubic(90, [new(1, 100, 10), new(2, 100, 20)], random);
        Assert.Equal(10, a[1]); Assert.Equal(80, a[2]);
        a = LaborAllocation.Cubic(100, [new(1, 100, 20), new(2, 100, 10)], random);
        Assert.Equal(89, a[1]); Assert.Equal(11, a[2]);
        a = LaborAllocation.Cubic(100, [new(1, 10, 10), new(2, 10, 20)], random);
        Assert.Equal(20, a.Values.Sum());
        Assert.Empty(LaborAllocation.Cubic(100, [new(1, 10, 0)], random));
        Assert.All(LaborAllocation.Cubic(0, [new(1, 10, 1)], random).Values, n => Assert.Equal(0, n));
    }

    [Fact]
    public void Cubes_AreExactBeyondInt128_AndTiesDoNotAlwaysFavorFirstId()
    {
        var winners = new HashSet<long>();
        for (ulong seed = 1; seed <= 16; seed++)
        {
            LaborCandidate[] candidates = [new(1, 2, long.MaxValue), new(2, 2, long.MaxValue)];
            var a = LaborAllocation.Cubic(1, candidates, new(seed));
            var b = LaborAllocation.Cubic(1, candidates.Reverse().ToArray(), new(seed));
            Assert.Equal(a.OrderBy(x => x.Key), b.OrderBy(x => x.Key));
            Assert.Equal(1, a.Values.Sum()); winners.Add(a.Single(x => x.Value == 1).Key);
        }
        Assert.Equal(2, winners.Count);
    }

    private static ScenarioDefinition Definition(int cap = 2, int staff = 0, long population = 2,
        int step = 10, int delay = 20, int num = 1, int den = 10, int min = 1) => Fixtures.Cycle() with
    {
        Id = "labor-test", Population = population, MaxTicks = 1000, HarvestTicksPerUnit = 1,
        Participants = [new(1, 10000000, new(0, 0), 1000), new(2, 10000000, new(0, 0), 1000)],
        Recipes = [new("sawmill", "wood", 2, "plank", 1, 1000, cap, min, 100)],
        Enterprises = [new(1, 1, new(0, 0), "sawmill", 1000, 40, staff, cap, 10),
            new(2, 2, new(0, 0), "sawmill", 1000, 40, staff, cap, 10)],
        Labor = new(step, delay, num, den, GrowthPerPeriod: 0)
    };
    private static void Supply(GameSession s, long actor, long id, long quantity = 2)
    {
        Fixtures.Command(s, new(ActionKind.Harvest, ItemId: "wood", Quantity: quantity), actor);
        Fixtures.UntilIdle(s, actor);
        var o = s.Engine.Observe(new(actor));
        Fixtures.Command(s, new(ActionKind.Transfer, ItemId: "wood", Quantity: quantity,
            Source: o.Participant.InventoryId, Destination: o.OwnEnterprises.Single(e => e.Id.Value == id).InputId), actor);
    }
    private static void Enable(GameSession s, params long[] ids)
    {
        foreach (long id in ids) s.Send(new(id), new(ActionKind.SetProductionEnabled, Enterprise: new(id), Enabled: true));
        s.Step();
    }
    private static EmploymentView Group(GameSession s, long id = 1) => s.Engine.Inspect().EmploymentGroups.Single(g => g.Id.Value == id);
    private static void To(GameSession s, long tick) { while (s.Engine.Tick < tick) s.Step(); }
    private static long Wages(GameSession s) => s.Engine.Inspect().Accounts.Single(a => a.Kind == "WagesSink").BalanceMinor;

    [Fact]
    public void EmptyReadyGroup_HiresGradually_AndFirstWageIsNextWorkingTick()
    {
        var s = Fixtures.Session(Definition()); Supply(s, 1, 1); Enable(s, 1);
        To(s, 49); Assert.Equal(0, Group(s).Headcount); Assert.Equal(0, Wages(s));
        To(s, 50); Assert.Equal(1, Group(s).Headcount); Assert.Equal(0, Wages(s));
        Assert.True(Group(s).Ready); Assert.Equal("NoStaff", s.Engine.Inspect().Enterprises[0].ReasonCode);
        s.Step(); Assert.Equal(10, Wages(s)); Assert.Equal(1, s.Engine.Inspect().Enterprises[0].WorkDone);
        To(s, 100); Assert.Equal(2, Group(s).Headcount); Assert.Equal(2, s.Engine.Inspect().Population);
        Assert.Equal(0, s.Engine.Inspect().Unemployed);
    }

    [Fact]
    public void BelowMinimumCrew_CanHireWithoutPrematureWork()
    {
        var s = Fixtures.Session(Definition(min: 2)); Supply(s, 1, 1); Enable(s, 1);
        To(s, 99); Assert.Equal(1, Group(s).Headcount); Assert.Equal(0, Wages(s));
        To(s, 100); Assert.Equal(2, Group(s).Headcount); Assert.Equal(0, Wages(s));
        s.Step(); Assert.Equal(20, Wages(s));
    }

    [Fact]
    public void IdleSmallGroup_PaysNothing_AndEventuallyLosesEveryone()
    {
        var s = Fixtures.Session(Definition(staff: 2, population: 4));
        To(s, 49); Assert.Equal(2, Group(s).Headcount);
        To(s, 50); Assert.Equal(1, Group(s).Headcount);
        To(s, 100); Assert.All(s.Engine.Inspect().EmploymentGroups, g => Assert.Equal(0, g.Headcount));
        Assert.Equal(4, s.Engine.Inspect().Unemployed); Assert.Equal(0, Wages(s));
        Assert.Equal("UserPaused", Group(s).ReadinessReason);
    }

    [Fact]
    public void SimultaneousTransfers_UseDonorsAndPoolOnce_AndConservePopulation()
    {
        var d = Definition(cap: 100, staff: 50, population: 100, step: 100);
        var s = Fixtures.Session(d); Supply(s, 1, 1); Supply(s, 2, 2); Enable(s, 1, 2);
        s.Send(new(1), new(ActionKind.SetWage, Group: new(1), WageMinor: 20)); s.Step();
        To(s, 100); Assert.Equal(60, Group(s, 1).Headcount); Assert.Equal(40, Group(s, 2).Headcount);
        To(s, 200); Assert.Equal(70, Group(s, 1).Headcount); Assert.Equal(30, Group(s, 2).Headcount);
        To(s, 400); Assert.Equal(89, Group(s, 1).Headcount); Assert.Equal(11, Group(s, 2).Headcount);
        Assert.Equal(0, s.Engine.Inspect().Unemployed);
        Assert.Equal(100, s.Engine.Inspect().EmploymentGroups.Sum(g => g.Headcount));
        var replay = GameSession.Replay(d, s.CommandTape, s.Engine.Tick);
        Assert.Equal(s.Engine.ComputeStateHash(), replay.Engine.ComputeStateHash());
    }

    [Fact]
    public void WagePayBoundary_AttractionDelay_AndLatestPendingValue_AreDistinct()
    {
        var s = Fixtures.Session(Definition(cap: 10, staff: 5, population: 10, step: 10));
        Supply(s, 1, 1); Supply(s, 2, 2); Enable(s, 1, 2);
        To(s, 11);
        long before = Wages(s);
        Fixtures.Command(s, new(ActionKind.SetWage, Group: new(1), WageMinor: 20)); // tick12
        Assert.Equal(10, Group(s).WageMinor); Assert.Equal(10, Group(s).AttractionWageMinor);
        Assert.Equal(100, Wages(s) - before);
        s.Step(); Assert.Equal(20, Group(s).WageMinor); Assert.Equal(10, Group(s).AttractionWageMinor);
        To(s, 31); Assert.Equal(5, Group(s).Headcount); Assert.Equal(10, Group(s).AttractionWageMinor);
        To(s, 32); Assert.Equal(20, Group(s).AttractionWageMinor);
        To(s, 40); Assert.Equal(6, Group(s).Headcount); Assert.Equal(4, Group(s, 2).Headcount);
        s.Send(new(1), new(ActionKind.SetWage, Group: new(1), WageMinor: 15));
        s.Send(new(1), new(ActionKind.SetWage, Group: new(1), WageMinor: 30)); s.Step();
        Assert.Equal(30, Group(s).PendingWageMinor); Assert.Equal(61, Group(s).AttractionEffectiveTick);
        To(s, 60); Assert.Equal(20, Group(s).AttractionWageMinor);
        s.Step(); Assert.Equal(30, Group(s).AttractionWageMinor);
    }

    [Fact]
    public void StopPreservesWipAndHeadcount_ThenDeparturesAndRehireAreGradual()
    {
        var s = Fixtures.Session(Definition(staff: 2, population: 4)); Supply(s, 1, 1); Enable(s, 1);
        var before = s.Engine.Inspect().Enterprises[0]; long wages = Wages(s);
        Fixtures.Command(s, new(ActionKind.SetProductionEnabled, Enterprise: new(1), Enabled: false));
        Assert.Equal(before.WorkDone, s.Engine.Inspect().Enterprises[0].WorkDone);
        Assert.Equal(2, Group(s).Headcount); Assert.Equal(wages, Wages(s));
        To(s, 100); Assert.Equal(0, Group(s).Headcount); Assert.Equal(wages, Wages(s));
        Enable(s, 1); To(s, 150); Assert.Equal(1, Group(s).Headcount);
        Assert.True(Group(s).Ready); s.Step(); Assert.True(Wages(s) > wages);
        Assert.Equal(before.BatchId, s.Engine.Inspect().Enterprises[0].BatchId);
        Assert.Equal(2, s.Engine.Inspect().Flows.Single(f => f.ItemId == "wood").RecipeInput);
    }

    [Theory]
    [InlineData("NoInputs")]
    [InlineData("NoMoney")]
    [InlineData("NoPaidOffer")]
    [InlineData("NoOutputSpace")]
    public void UnreadyVacancies_DoNotAttractOrPay(string expected)
    {
        var d = Definition(step: 100);
        if (expected == "NoMoney") d = d with { Participants = d.Participants.Select(p => p with { CashMinor = 0 }).ToArray() };
        if (expected == "NoPaidOffer") d = d with { Enterprises = d.Enterprises.Select(e => e with { WageMinor = 0 }).ToArray() };
        if (expected == "NoOutputSpace") d = d with { Recipes = d.Recipes.Select(r => r with { OutputQuantity = 41 }).ToArray() };
        var s = Fixtures.Session(d);
        if (expected != "NoInputs") Supply(s, 1, 1);
        Enable(s, 1); To(s, 100);
        Assert.False(Group(s).Ready); Assert.Equal(expected, Group(s).ReadinessReason);
        Assert.Equal(0, Group(s).Headcount); Assert.Equal(0, Wages(s));
    }

    [Fact]
    public void SharedWallet_DoesNotPromiseSameBudgetTwice_AndRotatesEligibility()
    {
        var d = Definition(cap: 2, step: 10, num: 1, den: 1);
        d = d with { Enterprises = d.Enterprises.Select(e => e with { OwnerId = 1, WageMinor = 10 }).ToArray(),
            Participants = d.Participants.Select(p => p with { CashMinor = p.Id == 1 ? 10 : 0 }).ToArray() };
        var s = Fixtures.Session(d);
        Fixtures.Command(s, new(ActionKind.Harvest, ItemId: "wood", Quantity: 4)); Fixtures.UntilIdle(s);
        var o = s.Engine.Observe(new(1));
        foreach (var e in o.OwnEnterprises)
            Fixtures.Command(s, new(ActionKind.Transfer, ItemId: "wood", Quantity: 2, Source: o.Participant.InventoryId, Destination: e.InputId));
        foreach (var e in o.OwnEnterprises) s.Send(new(1), new(ActionKind.SetProductionEnabled, Enterprise: e.Id, Enabled: true));
        s.Step(); To(s, 10);
        Assert.Single(s.Engine.Inspect().EmploymentGroups, g => g.Ready);
        Assert.Equal(1, s.Engine.Inspect().EmploymentGroups.Sum(g => g.Headcount));
        s.Step(); Assert.Equal(10, Wages(s));
        To(s, 20); Assert.All(s.Engine.Inspect().EmploymentGroups, g => Assert.Equal(0, g.Headcount));
    }

    [Fact]
    public void GrowthAddsOnlyToFreePool_AtLaborStep_AndReplayIncludesIt()
    {
        var d = Definition(population: 0) with { Labor = new(StepTicks: 10, GrowthPeriodTicks: 12, GrowthPerPeriod: 2) };
        var s = Fixtures.Session(d); To(s, 19); Assert.Equal(0, s.Engine.Inspect().Population);
        To(s, 20); Assert.Equal(2, s.Engine.Inspect().Population); Assert.Equal(2, s.Engine.Inspect().Unemployed);
        To(s, 40); Assert.Equal(6, s.Engine.Inspect().Population); Assert.Equal(6, s.Engine.Inspect().Unemployed);
        Assert.Equal(s.Engine.ComputeStateHash(), GameSession.Replay(d, s.CommandTape, 40).Engine.ComputeStateHash());
    }

    [Fact]
    public void InvalidWageCommands_CannotChangeOtherGroupsOrOverflow()
    {
        var s = Fixtures.Session(Definition());
        Assert.Equal("NotAvailable", s.Engine.Result(Fixtures.Command(s, new(ActionKind.SetWage, Group: new(2), WageMinor: 20)))!.ReasonCode);
        Assert.Equal("InvalidWage", s.Engine.Result(Fixtures.Command(s, new(ActionKind.SetWage, Group: new(1), WageMinor: -1)))!.ReasonCode);
        Assert.Equal("ArithmeticOverflow", s.Engine.Result(Fixtures.Command(s, new(ActionKind.SetWage, Group: new(1), WageMinor: long.MaxValue)))!.ReasonCode);
        Assert.Null(Group(s).PendingWageMinor); Assert.Equal(10, Group(s).WageMinor);
        Assert.DoesNotContain(s.Engine.Observe(new(1)).OwnEmploymentGroups, g => g.OwnerId.Value == 2);
        Assert.Equal(2, s.Engine.Observe(new(1)).LaborMarket.Length);
    }

    [Fact]
    public void InvalidLaborConfiguration_IsRejectedBeforeStarting()
    {
        var d = Definition();
        Assert.Throws<ArgumentException>(() => new SimulationEngine(d with { Labor = new(MobilityNumerator: 0) }));
        Assert.Throws<ArgumentException>(() => new SimulationEngine(d with { Labor = new(MobilityDenominator: 0) }));
        Assert.Throws<ArgumentException>(() => new SimulationEngine(d with { Labor = new(SalaryChangeDelayTicks: -1) }));
        Assert.Throws<ArgumentException>(() => new SimulationEngine(d with { Population = long.MaxValue, Labor = new() }));
    }
    [Fact]
    public void BuiltEnterprise_HiresFromZero_AndUpgradePreservesActualGroup()
    {
        var d = LandContentFixtures.CheapUpgrade() with
        { Buildings = [], Population = 2, Labor = new(GrowthPerPeriod: 0) };
        var s = LandContentFixtures.Session(d);
        LandContentFixtures.Harvest(s, "wood", 12);
        LandContentFixtures.Move(s, new(4, 0));
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.Build, Target: new(4, 0), BuildingTypeId: "sawmill")));
        var e = Assert.Single(s.Engine.Observe(new(1)).OwnEnterprises);
        var g = Assert.Single(s.Engine.Observe(new(1)).OwnEmploymentGroups);
        Assert.Equal(0, g.Headcount);
        var o = s.Engine.Observe(new(1));
        Fixtures.Command(s, new(ActionKind.Transfer, ItemId: "wood", Quantity: 2, Source: o.Participant.InventoryId, Destination: e.InputId));
        Fixtures.Command(s, new(ActionKind.SetProductionEnabled, Enterprise: e.Id, Enabled: true));
        while (Group(s, g.Id.Value).Headcount == 0) s.Step();
        while (s.Engine.Observe(new(1)).OwnEnterprises[0].CompletedBatches == 0) s.Step();
        Fixtures.Command(s, new(ActionKind.SetProductionEnabled, Enterprise: e.Id, Enabled: false));
        Fixtures.Command(s, new(ActionKind.SetWage, Group: g.Id, WageMinor: 37));
        int count = Group(s, g.Id.Value).Headcount;
        var b = Assert.Single(s.Engine.Observe(new(1)).OwnBuildings);
        Assert.Equal("Accepted", LandContentFixtures.Reason(s, new(ActionKind.UpgradeBuilding, Building: b.Id)));
        Assert.Equal(count, Group(s, g.Id.Value).Headcount);
        Assert.Equal(37, Group(s, g.Id.Value).WageMinor);
        Assert.Equal(4, Group(s, g.Id.Value).Capacity);
        Assert.Equal(g.Id, Assert.Single(s.Engine.Inspect().EmploymentGroups).Id);
        Assert.Equal(2, s.Engine.Inspect().Population);
    }

    [Fact]
    public void ScriptedLaborScenario_RecordsDelayIdleAndRehire_AndEveryTickReplays()
    {
        var d = Definition(cap: 10, population: 9) with
        { Enterprises = Definition(cap: 10, population: 9).Enterprises.Select(e => e with { WageMinor = e.Id * 10 }).ToArray() };
        var s = Fixtures.Session(d);
        ScriptedScenarios.LaborDynamics(s, d);
        Assert.Equal(291, s.Engine.Tick);
        Assert.Equal(1, Group(s).Headcount); Assert.Equal(8, Group(s, 2).Headcount);
        Assert.Equal(0, s.Engine.Inspect().Unemployed);
        var replay = GameSession.Replay(d, s.CommandTape, s.Engine.Tick);
        Assert.Equal(s.Samples, replay.Samples);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(s.LaborSamples, SimulationJson.Options),
            System.Text.Json.JsonSerializer.Serialize(replay.LaborSamples, SimulationJson.Options));
        foreach (var sample in s.LaborSamples)
            Assert.True(sample.EmploymentGroups.Sum(g => g.Headcount) <= d.Population);
    }

    [Fact]
    public void VacancyBudget_UsesAnnouncedNextIntervalWage()
    {
        var d = Definition(step: 5, num: 1, den: 1) with
        { Participants = Definition().Participants.Select(p => p with { CashMinor = 10 }).ToArray() };
        var s = Fixtures.Session(d); Supply(s, 1, 1); // tick4
        s.Send(new(1), new(ActionKind.SetProductionEnabled, Enterprise: new(1), Enabled: true));
        s.Send(new(1), new(ActionKind.SetWage, Group: new(1), WageMinor: 20)); s.Step();
        Assert.Equal(5, s.Engine.Tick); Assert.Equal(10, Group(s).WageMinor);
        Assert.Equal(0, Group(s).Headcount); Assert.Equal("NoMoney", Group(s).ReadinessReason);
        s.Step(); Assert.Equal(20, Group(s).WageMinor); Assert.Equal(0, Wages(s));
    }

}
