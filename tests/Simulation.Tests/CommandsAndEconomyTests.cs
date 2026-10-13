using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public sealed class CommandsAndEconomyTests
{
    [Fact]
    public void DuplicateCommand_BeforeAndAfterExecution_HarvestsOnlyOnce()
    {
        var session = Fixtures.Session(); var actor = new ParticipantId(1);
        var c = new CommandEnvelope("test:1", actor, 1, 0, 1, 10, new(ActionKind.Harvest, ItemId: "wood", Quantity: 2));
        Assert.Null(session.Submit(actor, c)); Assert.Null(session.Submit(actor, c));
        session.Step(); var first = session.Engine.Result(c.CommandId);
        Assert.Equal(first, session.Submit(actor, c)); Fixtures.UntilIdle(session);
        Assert.Equal(2, session.Engine.Inspect().Flows.Single(f => f.ItemId == "wood").Harvested);
        Assert.Single(session.Engine.EventsSince(0), e => e.Type == "HarvestStarted");
    }
    [Fact]
    public void ReusedIdWithDifferentPayload_RejectsAndPreservesOriginalResult()
    {
        var session = Fixtures.Session(); var actor = new ParticipantId(1);
        var c = new CommandEnvelope("test:1", actor, 1, 0, 1, 10, new(ActionKind.Harvest, ItemId: "wood", Quantity: 1));
        session.Submit(actor, c); session.Step();
        var bad = session.Submit(actor, c with { Action = c.Action with { Quantity = 10 } });
        Assert.Equal("CommandIdConflict", bad!.ReasonCode); Fixtures.UntilIdle(session);
        Assert.Equal(ResultStatus.Accepted, session.Engine.Result(c.CommandId)!.Status);
        Assert.Equal(1, session.Engine.Inspect().Flows.Single(f => f.ItemId == "wood").Harvested);
    }
    [Fact]
    public void ActorCannotImpersonateAnotherOrAccessTheirInventory()
    {
        var session = Fixtures.Session();
        var c = new CommandEnvelope("spoof", new(2), 1, 0, 1, 10, new(ActionKind.Harvest, ItemId: "wood", Quantity: 1));
        Assert.Equal("NotAvailable", session.Submit(new(1), c)!.ReasonCode);
        var own = session.Engine.Observe(new(1)); var other = session.Engine.Observe(new(2));
        var id = Fixtures.Command(session, new(ActionKind.Transfer, ItemId: "wood", Quantity: 1,
            Source: own.Participant.InventoryId, Destination: other.Participant.InventoryId));
        Assert.Equal("NotAvailable", session.Engine.Result(id)!.ReasonCode);
        Assert.All(session.Engine.Inspect().Inventories, i => Assert.Empty(i.Stock));
    }
    [Fact]
    public void ObservationContainsOnlyOwnPrivateAccountsAndInventories()
    {
        var session = Fixtures.Session(); var own = session.Engine.Observe(new(1));
        Assert.All(own.OwnInventories, i => Assert.Equal(new ParticipantId(1), i.OwnerId));
        Assert.Equal(100000, own.Account.BalanceMinor);
        Assert.DoesNotContain(own.OwnInventories, i => i.Id == session.Engine.Observe(new(2)).Participant.InventoryId);
    }
    [Theory]
    [InlineData(0, "InvalidQuantity")]
    [InlineData(-1, "InvalidQuantity")]
    [InlineData(long.MaxValue, "ArithmeticOverflow")]
    public void InvalidHarvest_DoesNotReserveSpaceOrCreateGoods(long quantity, string reason)
    {
        var session = Fixtures.Session();
        var id = Fixtures.Command(session, new(ActionKind.Harvest, ItemId: "wood", Quantity: quantity));
        Assert.Equal(reason, session.Engine.Result(id)!.ReasonCode);
        var inventory = session.Engine.Observe(new(1)).OwnInventories.Single(i => i.Id == session.Engine.Observe(new(1)).Participant.InventoryId);
        Assert.Empty(inventory.Stock); Assert.Equal(0, inventory.ReservedCapacity); Assert.False(session.Engine.Observe(new(1)).Participant.Busy);
    }
    [Fact]
    public void HarvestCannotCreateAProducedItemAtResourceNode()
    {
        var session = Fixtures.Session(); var id = Fixtures.Command(session, new(ActionKind.Harvest, ItemId: "plank", Quantity: 1));
        Assert.Equal("NoResourceNode", session.Engine.Result(id)!.ReasonCode);
    }
    [Fact]
    public void HarvestRespectsFinalInventoryCapacity()
    {
        var session = Fixtures.Session(); var id = Fixtures.Command(session, new(ActionKind.Harvest, ItemId: "wood", Quantity: 21));
        Assert.Equal("NoCapacity", session.Engine.Result(id)!.ReasonCode);
        Assert.False(session.Engine.Observe(new(1)).Participant.Busy);
    }
    [Fact]
    public void ExpiryBoundary_IsInclusive_AndLateCommandIsRejected()
    {
        var session = Fixtures.Session(); var actor = new ParticipantId(1);
        var c = new CommandEnvelope("timely", actor, 1, 0, 1, 1, new(ActionKind.Harvest, ItemId: "wood", Quantity: 1));
        session.Submit(actor, c); session.Step(); Assert.Equal(ResultStatus.Accepted, session.Engine.Result("timely")!.Status);
        Fixtures.UntilIdle(session);
        var late = c with { CommandId = "late", LocalSequence = 2 };
        session.Submit(actor, late); session.Step(); Assert.Equal("CommandExpired", session.Engine.Result("late")!.ReasonCode);
        Assert.Equal(1, session.Engine.Inspect().Flows.Single(f => f.ItemId == "wood").Harvested);
    }
    [Fact]
    public void TransferToFullInventory_RollsBackSourceDebit()
    {
        var definition = Fixtures.Cycle() with { Enterprises = [Fixtures.Cycle().Enterprises[0] with { Position = new(0, 0), InputCapacity = 1 }] };
        var session = Fixtures.Session(definition);
        Fixtures.Command(session, new(ActionKind.Harvest, ItemId: "wood", Quantity: 2)); Fixtures.UntilIdle(session);
        var o = session.Engine.Observe(new(1)); var id = Fixtures.Command(session,
            new(ActionKind.Transfer, ItemId: "wood", Quantity: 2, Source: o.Participant.InventoryId, Destination: o.OwnEnterprises[0].InputId));
        Assert.Equal("NoCapacity", session.Engine.Result(id)!.ReasonCode);
        Assert.Equal(2, Fixtures.Count(session, o.Participant.InventoryId, "wood"));
        Assert.Equal(0, Fixtures.Count(session, o.OwnEnterprises[0].InputId, "wood"));
    }
    [Fact]
    public void StockReservation_CannotBeSoldOrReservedTwice()
    {
        var store = Store(); var tx = store.Begin(0); tx.AddStock(10, "wood", 5); tx.ReserveStock(10, "lot-a", "wood", 4); tx.Commit();
        var failed = store.Begin(1);
        Assert.Throws<RuleViolation>(() => failed.ReserveStock(10, "lot-b", "wood", 2));
        Assert.Throws<RuleViolation>(() => store.Begin(1).RemoveFreeStock(10, "wood", 2));
        Assert.Equal(5, store.Inventories[10].Count("wood")); Assert.Equal(4, store.Inventories[10].Reserved("wood"));
        store.Validate();
    }
    [Fact]
    public void MoneyHoldAndPayment_AreExclusive_AndCommitCannotRunTwice()
    {
        var store = Store(); var tx = store.Begin(0); tx.HoldMoney(1, "purchase", 80); tx.Commit();
        Assert.Equal(20, store.Accounts[1].Available);
        Assert.Throws<RuleViolation>(() => store.Begin(1).Pay(1, 2, 30, "OtherPurchase"));
        var settle = store.Begin(1); settle.Pay(1, 2, 60, "Partial", "purchase"); settle.Commit();
        Assert.Equal(40, store.Accounts[1].Balance); Assert.Equal(20, store.Accounts[1].Reserved); Assert.Equal(60, store.Accounts[2].Balance);
        Assert.Throws<InvalidOperationException>(() => settle.Commit());
        Assert.Throws<InvalidOperationException>(() => settle.Pay(1, 2, 1, "PostCommit")); store.Validate();
    }
    [Fact]
    public void StagedDebitAndFailedCapacityCheck_LeaveOriginalStoreUntouched()
    {
        var store = Store(); var tx = store.Begin(0); tx.AddStock(10, "wood", 5); tx.Commit();
        var failed = store.Begin(1); failed.RemoveFreeStock(10, "wood", 2); failed.Pay(1, 2, 40, "Staged");
        Assert.Throws<RuleViolation>(() => failed.ReserveCapacity(10, "too-much", "wood", 10));
        Assert.Equal(5, store.Inventories[10].Count("wood")); Assert.Equal(100, store.Accounts[1].Balance); Assert.Empty(store.Ledger);
    }
    [Fact]
    public void ConfigOverflow_IsRejectedBeforeWorldIsCreated()
    {
        var config = Fixtures.Cycle() with { CapitalOffers = [new(1, "wood", long.MaxValue, 500, new(0, 0))] };
        Assert.Throws<ArgumentException>(() => new SimulationEngine(config));
    }
    private static EconomyStore Store()
    {
        var store = new EconomyStore(new Dictionary<string, ItemDefinition> { ["wood"] = new("wood", 1, 500) });
        store.Accounts.Add(1, new(1, "Participant", 100)); store.Accounts.Add(2, new(2, "Participant", 0)); store.InitialMoney = 100;
        store.Inventories.Add(10, new(10, 1, new(0, 0), 10)); return store;
    }
}
