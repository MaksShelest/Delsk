namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private void CollectPurchase(ActorState actor, CommandEnvelope c)
    {
        int index = _marketAllocations.FindIndex(a => a.Id == c.Action.PurchaseId && a.Buyer.Value == actor.Id);
        if (index < 0) throw new RuleViolation("NotAvailable");
        var a = _marketAllocations[index];
        if (a.ClosedTick is not null || Tick > a.DeadlineTick) throw new RuleViolation("PurchaseClosed");
        RequireIdle(actor);
        if (_economy.Inventories[a.SourceInventory.Value].Position != actor.Position)
            throw new RuleViolation("NotAtLocation");
        long quantity = c.Action.Quantity;
        if (quantity <= 0 || quantity > a.RemainingQuantity) throw new RuleViolation("InvalidQuantity");
        long collected = checked(a.CollectedQuantity + quantity), payment = checked(quantity * a.UnitPriceMinor);
        var next = a with
        {
            CollectedQuantity = collected, PaidMinor = checked(a.PaidMinor + payment),
            Status = collected == a.Quantity ? "Fulfilled" : "PartiallyCollected",
            ClosedTick = collected == a.Quantity ? Tick : null
        };
        var tx = _economy.Begin(Tick);
        tx.ConsumeReserved(a.SourceInventory.Value, a.StockHold, quantity);
        tx.AddStock(actor.Inventory, a.ItemId, quantity);
        tx.Pay(a.EscrowAccount.Value, _actors[a.Seller.Value].Account, payment, "PickupPayment");
        tx.Commit(); _marketAllocations[index] = next;
        Emit("PurchaseCollected", actor.Id, a.Id, a.ItemId, quantity, payment, next.Status, c.CommandId);
    }

    private static long PickupFine(long value, int basisPoints) => checked((long)((Int128)value * basisPoints / 10000));

    private void ExpirePurchases()
    {
        // Collection and movement were already processed at this inclusive deadline.
        foreach (int index in Enumerable.Range(0, _marketAllocations.Count).OrderBy(i => _marketAllocations[i].Id))
        {
            var a = _marketAllocations[index];
            if (a.ClosedTick is not null || Tick < a.DeadlineTick) continue;
            long remaining = a.RemainingQuantity, value = checked(remaining * a.UnitPriceMinor);
            long fine = PickupFine(value, Market.PickupFineBasisPoints), refund = checked(value - fine);
            var next = a with
            {
                ReleasedQuantity = remaining, FineMinor = fine, RefundedMinor = refund,
                Status = "Expired", ClosedTick = Tick
            };
            var tx = _economy.Begin(Tick); tx.ReleaseStock(a.SourceInventory.Value, a.StockHold);
            if (fine > 0) tx.Pay(a.EscrowAccount.Value, _actors[a.Seller.Value].Account, fine, "PickupFine");
            if (refund > 0) tx.Pay(a.EscrowAccount.Value, _actors[a.Buyer.Value].Account, refund, "PickupRefund");
            tx.Commit(); _marketAllocations[index] = next;
            Emit("PurchaseExpired", a.Buyer.Value, a.Id, a.ItemId, remaining, refund, "Expired");
            if (fine > 0) Emit("PickupFinePaid", a.Seller.Value, a.Id, money: fine);
        }
    }
}
