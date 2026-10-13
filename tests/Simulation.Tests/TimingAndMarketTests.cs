using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public sealed class TimingAndMarketTests
{
    [Fact]
    public void ReplayAndRepeatSameSeedAndTape_ProduceIdenticalHashAndLedger()
    {
        var definition = Fixtures.Cycle(); var first = Fixtures.Session(definition);
        ScriptedScenarios.EconomicCycle(first, definition);
        var replay = GameSession.Replay(definition, first.CommandTape, first.Engine.Tick);
        var second = Fixtures.Session(definition); ScriptedScenarios.EconomicCycle(second, definition);
        Assert.Equal(first.Engine.ComputeStateHash(), replay.Engine.ComputeStateHash());
        Assert.Equal(first.Engine.ComputeStateHash(), second.Engine.ComputeStateHash());
        Assert.Equal(first.Engine.Ledger(), replay.Engine.Ledger());
        Assert.Equal(first.Samples, replay.Samples);
    }
    [Fact]
    public void PacedAndHeadlessScheduling_UseIdenticalMovementDurations()
    {
        var fast = Fixtures.Session(); var paced = Fixtures.Session();
        var action = new GameAction(ActionKind.MoveTo, Target: new(10, 0));
        fast.Send(new(1), action); paced.Send(new(1), action);
        new TickScheduler(fast).Headless(11);
        var scheduler = new TickScheduler(paced);
        for (int i = 0; i < 110; i++) scheduler.AdvanceElapsed(TimeSpan.FromMilliseconds(100));
        Assert.Equal(11, paced.Engine.Tick); Assert.Equal(fast.Engine.ComputeStateHash(), paced.Engine.ComputeStateHash());
        Assert.Single(paced.Engine.EventsSince(0), e => e.Type == "MovementArrived");
    }
    [Fact]
    public void Pause_DiscardsElapsedTime_NoOfflineCatchUp()
    {
        var session = Fixtures.Session(); var scheduler = new TickScheduler(session);
        session.Send(new(1), new(ActionKind.MoveTo, Target: new(10, 0))); scheduler.Headless(3);
        string hash = session.Engine.ComputeStateHash(); scheduler.SetPaused(true);
        scheduler.AdvanceElapsed(TimeSpan.FromDays(2)); scheduler.Headless(100);
        Assert.Equal(hash, session.Engine.ComputeStateHash()); scheduler.SetPaused(false);
        scheduler.AdvanceElapsed(TimeSpan.FromMilliseconds(999)); Assert.Equal(3, session.Engine.Tick);
        scheduler.AdvanceElapsed(TimeSpan.FromMilliseconds(1)); Assert.Equal(4, session.Engine.Tick);
    }
    [Fact]
    public void CapitalSale_RequiresPhysicalArrival_AndWaitsForClosing()
    {
        var session = Fixtures.Session(Fixtures.Cycle(0) with { Enterprises = [], Population = 0 });
        Fixtures.Command(session, new(ActionKind.Harvest, ItemId: "wood", Quantity: 2)); Fixtures.UntilIdle(session);
        var rejected = Fixtures.Command(session, new(ActionKind.SellAvailableToCapital, ItemId: "wood", Quantity: 2, OfferId: 1));
        Assert.Equal("NotAtLocation", session.Engine.Result(rejected)!.ReasonCode);
        Fixtures.Command(session, new(ActionKind.MoveTo, Target: new(20, 0))); Fixtures.UntilIdle(session);
        Fixtures.Command(session, new(ActionKind.SellAvailableToCapital, ItemId: "wood", Quantity: 2, OfferId: 1));
        long cutoff = session.Engine.Inspect().CapitalOffers[0].CutoffTick!.Value;
        Assert.Equal(0, session.Engine.Observe(new(1)).Account.BalanceMinor);
        var move = Fixtures.Command(session, new(ActionKind.MoveTo, Target: new(0, 0)));
        Assert.Equal("ParticipantBusy", session.Engine.Result(move)!.ReasonCode);
        while (session.Engine.Tick < cutoff - 1) session.Step();
        Assert.Equal(0, session.Engine.Observe(new(1)).Account.BalanceMinor); session.Step();
        Assert.Equal(1000, session.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Single(session.Engine.Inspect().Settlements); session.Step(); session.Step();
        Assert.Equal(1000, session.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Single(session.Engine.Inspect().Settlements);
    }
    [Fact]
    public void Cancellation_ReleasesReservedStockAndPaysNothing()
    {
        var definition = Market(2); var session = Fixtures.Session(definition); HarvestBoth(session);
        Fixtures.Command(session, new(ActionKind.SellAvailableToCapital, ItemId: "wood", Quantity: 1, OfferId: 1));
        Fixtures.Command(session, new(ActionKind.CancelCapitalSale, OfferId: 1)); new TickScheduler(session).Headless(5);
        Assert.Equal(1, Fixtures.Count(session, session.Engine.Observe(new(1)).Participant.InventoryId, "wood"));
        Assert.Equal(0, session.Engine.Observe(new(1)).Account.BalanceMinor); Assert.Empty(session.Engine.Inspect().Settlements);
    }
    [Fact]
    public void AllRequestsAtInclusiveCutoffAreConsidered()
    {
        var session = Fixtures.Session(Market(2)); HarvestBoth(session);
        Fixtures.Command(session, new(ActionKind.SellAvailableToCapital, ItemId: "wood", Quantity: 1, OfferId: 1));
        long cutoff = session.Engine.Inspect().CapitalOffers[0].CutoffTick!.Value;
        while (session.Engine.Tick < cutoff - 1) session.Step();
        var id = Fixtures.Command(session, new(ActionKind.SellAvailableToCapital, ItemId: "wood", Quantity: 1, OfferId: 1), 2);
        Assert.Equal(ResultStatus.Accepted, session.Engine.Result(id)!.Status);
        Assert.Equal(2, session.Engine.Inspect().Settlements.Sum(x => x.Accepted));
        Assert.Equal(500, session.Engine.Observe(new(1)).Account.BalanceMinor); Assert.Equal(500, session.Engine.Observe(new(2)).Account.BalanceMinor);
    }
    [Fact]
    public void ReversingControllerSubmissionOrderDoesNotChangeAllocation()
    {
        GameSession Run(bool reverse)
        {
            var session = Fixtures.Session(Market(1)); HarvestBoth(session, reverse);
            foreach (long actor in reverse ? new long[] { 2, 1 } : [1, 2])
                session.Send(new(actor), new(ActionKind.SellAvailableToCapital, ItemId: "wood", Quantity: 1, OfferId: 1));
            new TickScheduler(session).Headless(6); return session;
        }
        var a = Run(false); var b = Run(true);
        Assert.Equal(a.Engine.ComputeStateHash(), b.Engine.ComputeStateHash());
        Assert.Equal(1, a.Engine.Inspect().Settlements.Sum(s => s.Accepted));
        Assert.Equal(500, a.Engine.Inspect().Settlements.Sum(s => s.PaidMinor));
    }
    [Fact]
    public void EqualRequests_UseSeededTieBreaking_NotPermanentLowestActor()
    {
        var winners = new HashSet<long>();
        for (ulong seed = 0; seed < 32; seed++)
        {
            var session = Fixtures.Session(Market(1) with { Seed = seed }); HarvestBoth(session);
            session.Send(new(1), new(ActionKind.SellAvailableToCapital, ItemId: "wood", Quantity: 1, OfferId: 1));
            session.Send(new(2), new(ActionKind.SellAvailableToCapital, ItemId: "wood", Quantity: 1, OfferId: 1));
            new TickScheduler(session).Headless(6);
            winners.Add(session.Engine.Inspect().Settlements.Single(s => s.Accepted == 1).Seller.Value);
        }
        Assert.Contains(1L, winners); Assert.Contains(2L, winners);
    }
    [Fact]
    public void SeededCommandSequences_PreserveInvariantsAndPerTickCommandBudget()
    {
        var session = Fixtures.Session();
        for (int tick = 0; tick < 60; tick++)
        {
            for (int n = 0; n < 10; n++) session.Send(new(1), new(ActionKind.SetProductionEnabled, Enterprise: new(1), Enabled: (tick + n) % 2 == 0));
            session.Step(); session.Engine.ValidateInvariants();
        }
        Assert.Equal(120, session.Engine.EventsSince(0).Count(e => e.ReasonCode == "CommandBudgetExceeded"));
        Assert.Equal(100000, session.Engine.Observe(new(1)).Account.BalanceMinor);
    }
    private static ScenarioDefinition Market(long quantity) => Fixtures.Cycle(0) with
    { Enterprises = [], Population = 0, CapitalOffers = [new(1, "wood", quantity, 500, new(0, 0))] };
    private static void HarvestBoth(GameSession session, bool reverse = false)
    {
        foreach (long actor in reverse ? new long[] { 2, 1 } : [1, 2]) session.Send(new(actor), new(ActionKind.Harvest, ItemId: "wood", Quantity: 1));
        new TickScheduler(session).Headless(3);
    }
}
