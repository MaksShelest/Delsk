using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public sealed class EconomicCycleTests
{
    [Fact]
    public void FullCycle_ConservesMoneyAndGoods_UsesPhysicalTravel()
    {
        var definition = Fixtures.Cycle(); var session = Fixtures.Session(definition);
        ScriptedScenarios.EconomicCycle(session, definition);
        var state = session.Engine.Inspect();
        Assert.Equal(112500, session.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(2000, state.Accounts.Single(a => a.Kind == "WagesSink").BalanceMinor);
        Assert.Equal(500, state.Accounts.Single(a => a.Kind == "Treasury").BalanceMinor);
        Assert.Equal(state.InitialMoneyMinor + state.ExternalMoneyInMinor, state.Accounts.Sum(a => a.BalanceMinor));
        var wood = state.Flows.Single(f => f.ItemId == "wood"); var plank = state.Flows.Single(f => f.ItemId == "plank");
        Assert.Equal(10, wood.Harvested); Assert.Equal(10, wood.RecipeInput);
        Assert.Equal(5, plank.Produced); Assert.Equal(5, plank.Exported);
        Assert.All(state.Inventories, i => Assert.Empty(i.Stock));
        Assert.Equal(5, state.Enterprises[0].CompletedBatches);
        Assert.All(state.Settlements, s => Assert.Equal("Fulfilled", s.Status));
        var events = session.Engine.EventsSince(0);
        Assert.Equal(2, events.Count(e => e.Type == "MovementStarted"));
        Assert.Equal(2, events.Count(e => e.Type == "MovementArrived"));
        Assert.Equal(5, events.Count(e => e.Type == "ProductionCompleted"));
        session.Engine.ValidateInvariants();
    }
    [Fact]
    public void ZeroCash_CanHarvestTravelAndSellWithoutBond()
    {
        var definition = Fixtures.Cycle(0) with { Enterprises = [], Population = 0 };
        var session = Fixtures.Session(definition);
        ScriptedScenarios.ZeroCashRecovery(session, definition);
        Assert.Equal(1000, session.Engine.Observe(new(1)).Account.BalanceMinor);
        var view = session.Engine.Inspect();
        Assert.Equal(0, view.Accounts.Single(a => a.Kind == "WagesSink").BalanceMinor);
        Assert.Equal(0, view.Accounts.Single(a => a.Kind == "Treasury").BalanceMinor);
        Assert.Equal(2, view.Flows.Single(f => f.ItemId == "wood").Exported);
    }
    [Fact]
    public void PauseResume_PreservesWip_ConsumesInputsAndPaysOnlyOnce()
    {
        var session = Fixtures.Session(); Fixtures.Supply(session);
        Fixtures.Command(session, new(ActionKind.SetProductionEnabled, Enterprise: new(1), Enabled: true));
        var e = session.Engine.Inspect().Enterprises[0];
        Assert.Equal(2, e.WorkDone); Assert.Equal(4, e.WorkRequired);
        Fixtures.Command(session, new(ActionKind.SetProductionEnabled, Enterprise: new(1), Enabled: false));
        long cash = session.Engine.Observe(new(1)).Account.BalanceMinor;
        new TickScheduler(session).Headless(5);
        var stopped = session.Engine.Inspect().Enterprises[0];
        Assert.Equal(e.BatchId, stopped.BatchId); Assert.Equal(e.WorkDone, stopped.WorkDone);
        Assert.Equal(cash, session.Engine.Observe(new(1)).Account.BalanceMinor); Assert.Equal(2, stopped.Staff);
        Fixtures.Command(session, new(ActionKind.SetProductionEnabled, Enterprise: new(1), Enabled: true));
        Assert.Equal(1, Fixtures.Count(session, e.OutputId, "plank"));
        Assert.Equal(99500, session.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(2, session.Engine.Inspect().Flows.Single(f => f.ItemId == "wood").RecipeInput);
        Assert.Equal(1, session.Engine.EventsSince(0).Count(x => x.Type == "ProductionCompleted"));
    }
    [Fact]
    public void CannotAffordWork_DoesNotConsumeInputOrReserveOutput()
    {
        var session = Fixtures.Session(Fixtures.Cycle(249)); Fixtures.Supply(session);
        Fixtures.Command(session, new(ActionKind.SetProductionEnabled, Enterprise: new(1), Enabled: true));
        var state = session.Engine.Inspect(); var e = state.Enterprises[0];
        Assert.Equal("NoMoney", e.ReasonCode); Assert.Equal(0, e.BatchId);
        Assert.Equal(2, Fixtures.Count(session, e.InputId, "wood"));
        Assert.Equal(0, state.Inventories.Single(i => i.Id == e.OutputId).ReservedCapacity);
        Assert.Equal(249, session.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.DoesNotContain(session.Engine.Ledger(), x => x.Reason is "Wages" or "Energy");
    }
    [Fact]
    public void FullOutput_DoesNotConsumeNextBatchOrPayForIdle()
    {
        var session = Fixtures.Session(Fixtures.Cycle(outputCapacity: 1)); Fixtures.Supply(session, 4);
        Fixtures.Command(session, new(ActionKind.SetProductionEnabled, Enterprise: new(1), Enabled: true));
        session.Step(); session.Step();
        var e = session.Engine.Inspect().Enterprises[0];
        Assert.Equal("NoOutputSpace", e.ReasonCode); Assert.Equal(0, e.BatchId);
        Assert.Equal(1, Fixtures.Count(session, e.OutputId, "plank"));
        Assert.Equal(2, Fixtures.Count(session, e.InputId, "wood"));
        Assert.Equal(99500, session.Engine.Observe(new(1)).Account.BalanceMinor);
    }
    [Fact]
    public void HalfStaff_TakesTwiceAsLong_AndEnergyStillTotalsOneBatch()
    {
        var session = Fixtures.Session(Fixtures.Cycle(staff: 1)); Fixtures.Supply(session);
        Fixtures.Command(session, new(ActionKind.SetProductionEnabled, Enterprise: new(1), Enabled: true));
        new TickScheduler(session).Headless(2);
        Assert.Equal(0, session.Engine.Inspect().Enterprises[0].CompletedBatches);
        session.Step();
        var state = session.Engine.Inspect();
        Assert.Equal(1, state.Enterprises[0].CompletedBatches);
        Assert.Equal(100, state.Accounts.Single(a => a.Kind == "Treasury").BalanceMinor);
        Assert.Equal(400, state.Accounts.Single(a => a.Kind == "WagesSink").BalanceMinor);
    }
    [Fact]
    public void ZeroStaff_PreparedMaterialsDoNotCreateProductOrPayroll()
    {
        var session = Fixtures.Session(Fixtures.Cycle(staff: 0)); Fixtures.Supply(session);
        Fixtures.Command(session, new(ActionKind.SetProductionEnabled, Enterprise: new(1), Enabled: true));
        new TickScheduler(session).Headless(4);
        Assert.Equal("NoStaff", session.Engine.Inspect().Enterprises[0].ReasonCode);
        Assert.Equal(100000, session.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(2, session.Engine.Inspect().Unemployed);
    }
    [Fact]
    public void Movement_HasIntermediatePositionsAndExactlyOneArrival()
    {
        var session = Fixtures.Session();
        Fixtures.Command(session, new(ActionKind.Harvest, ItemId: "wood", Quantity: 2)); Fixtures.UntilIdle(session);
        long departure = session.Engine.Tick + 1;
        Fixtures.Command(session, new(ActionKind.MoveTo, Target: new(10, 0)));
        var enterprise = session.Engine.Observe(new(1)).OwnEnterprises[0];
        session.Step();
        Assert.Equal(new Cell(1, 0), session.Engine.Observe(new(1)).Participant.Position);
        Assert.Equal(0, Fixtures.Count(session, enterprise.InputId, "wood"));
        Assert.Equal(2, Fixtures.Count(session, session.Engine.Observe(new(1)).Participant.InventoryId, "wood"));
        var jobId = session.Engine.EventsSince(0).Single(e => e.Type == "MovementStarted").EntityId;
        Assert.False(session.Engine.CompleteMovement(1, jobId));
        Fixtures.UntilIdle(session);
        Assert.Equal(departure + 10, session.Engine.Tick);
        Assert.False(session.Engine.CompleteMovement(1, jobId));
        Assert.Single(session.Engine.EventsSince(0), e => e.Type == "MovementArrived");
    }
    [Fact]
    public void Bfs_GoesAroundBlockedCell_WithRealExtraTicks()
    {
        var definition = Fixtures.Cycle() with { BlockedCells = [new(1, 0)] };
        var session = Fixtures.Session(definition);
        Fixtures.Command(session, new(ActionKind.MoveTo, Target: new(3, 0)));
        Assert.Equal(6, session.Engine.Observe(new(1)).Participant.ArrivalTick);
        Fixtures.UntilIdle(session);
        Assert.Equal(6, session.Engine.Tick); Assert.Equal(new Cell(3, 0), session.Engine.Observe(new(1)).Participant.Position);
    }
    [Fact]
    public void UnreachableTarget_RejectsWithoutChangingPosition()
    {
        var session = Fixtures.Session(Fixtures.Cycle() with { BlockedCells = [new(1, 0), new(0, 1)] });
        var id = Fixtures.Command(session, new(ActionKind.MoveTo, Target: new(2, 0)));
        Assert.Equal("NoPath", session.Engine.Result(id)!.ReasonCode);
        Assert.Equal(new Cell(0, 0), session.Engine.Observe(new(1)).Participant.Position);
        Assert.False(session.Engine.Observe(new(1)).Participant.Busy);
    }
}
