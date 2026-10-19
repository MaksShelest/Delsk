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
        RequireActionLocation(actor, c.Action);
        long quantity = c.Action.Quantity;
        if (quantity <= 0 || quantity > a.RemainingQuantity) throw new RuleViolation("InvalidQuantity");
        BeginTransfer(actor,c,a.SourceInventory.Value,actor.Inventory,a.ItemId,quantity,"Pickup",a.Id);
    }
    private void CompletePickupTransfer(ActorState actor,TransferOperation transfer,long quantity)
    {
        int index=_marketAllocations.FindIndex(a=>a.Id==transfer.Contract);
        var a=_marketAllocations[index];
        long collected = checked(a.CollectedQuantity + quantity), payment = checked(quantity * a.UnitPriceMinor);
        var next = a with
        {
            CollectedQuantity = collected, PaidMinor = checked(a.PaidMinor + payment),
            Status = collected == a.Quantity ? "Fulfilled" : "PartiallyCollected",
            ClosedTick = collected == a.Quantity ? Tick : null
        };
        var tx = _economy.Begin(Tick);
        tx.ConsumeReserved(a.SourceInventory.Value, a.StockHold, quantity);
        tx.ReceiveReservedCapacity(actor.Inventory,transfer.CapacityHold,a.ItemId,quantity);
        tx.Pay(a.EscrowAccount.Value, _actors[a.Seller.Value].Account, payment, "PickupPayment");
        tx.Commit(); _marketAllocations[index] = next;
        Emit("PurchaseCollected", actor.Id, a.Id, a.ItemId, quantity, payment, next.Status, transfer.CommandId);
    }

    private static long PickupFine(long value, int basisPoints) => checked((long)((Int128)value * basisPoints / 10000));

    private void ExpirePurchases()
    {
        // Collection and movement were already processed at this inclusive deadline.
        foreach (int index in Enumerable.Range(0, _marketAllocations.Count).OrderBy(i => _marketAllocations[i].Id))
        {
            var a = _marketAllocations[index];
            if (a.ClosedTick is not null || Tick < a.DeadlineTick) continue;
            foreach(var t in _transfers.Values.Where(t=>t.Kind=="Pickup" && t.Contract==a.Id).ToArray()) CancelTransfer(t,"ContractClosed");
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
