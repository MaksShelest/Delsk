namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private void DeliverContract(ActorState actor, CommandEnvelope c)
    {
        int index = _deliveryContracts.FindIndex(d => d.Id == c.Action.ContractId && d.Supplier.Value == actor.Id);
        if (index < 0) throw new RuleViolation("NotAvailable");
        var d = _deliveryContracts[index];
        if (d.ClosedTick is not null || Tick > d.DeadlineTick) throw new RuleViolation("ContractClosed");
        RequireIdle(actor);
        if (actor.Position != d.Position) throw new RuleViolation("NotAtLocation");
        long q = c.Action.Quantity;
        if (q <= 0 || q > d.RemainingQuantity) throw new RuleViolation("InvalidQuantity");
        long delivered = checked(d.DeliveredQuantity + q), payment = checked(q * d.UnitPriceMinor);
        bool complete = delivered == d.Quantity;
        var next = d with { DeliveredQuantity = delivered, PaidMinor = checked(d.PaidMinor + payment),
            BondReturnedMinor = complete ? d.BondMinor : 0, Status = complete ? "Fulfilled" : "PartiallyDelivered",
            ClosedTick = complete ? Tick : null };
        long exported = d.Buyer is null ? checked(_flows[d.ItemId].Exported + q) : _flows[d.ItemId].Exported;
        var tx = _economy.Begin(Tick); tx.RemoveFreeStock(actor.Inventory, d.ItemId, q);
        if (d.DestinationInventory is { } inventory) tx.ReceiveReservedCapacity(inventory.Value, d.CapacityHold, d.ItemId, q);
        tx.Pay(d.EscrowAccount.Value, actor.Account, payment, "DeliveryPayment");
        if (complete && d.BondMinor > 0) tx.Pay(d.BondAccount.Value, actor.Account, d.BondMinor, "DeliveryBondReturn");
        tx.Commit(); _deliveryContracts[index] = next; _flows[d.ItemId].Exported = exported;
        Emit("DeliveryReceived", actor.Id, d.Id, d.ItemId, q, payment, next.Status, c.CommandId);
    }
    private void ExpireDelivery()
    {
        foreach (int index in Enumerable.Range(0, _deliveryContracts.Count).OrderBy(i => _deliveryContracts[i].Id))
        {
            var d = _deliveryContracts[index]; if (d.ClosedTick is not null || Tick < d.DeadlineTick) continue;
            long remainder = d.RemainingQuantity, refund = checked(remainder * d.UnitPriceMinor);
            long fine = DeliveryFine(refund, Delivery.FineBasisPoints), returned = checked(d.BondMinor - fine);
            var next = d with { UnfulfilledQuantity = remainder, RefundedMinor = refund, FineMinor = fine,
                BondReturnedMinor = returned, Status = "Expired", ClosedTick = Tick };
            var tx = _economy.Begin(Tick);
            tx.Pay(d.EscrowAccount.Value, d.BuyerAccount.Value, refund, "DeliveryRefund");
            if (fine > 0) tx.Pay(d.BondAccount.Value, d.Buyer is null ? _capitalCompensation : d.BuyerAccount.Value, fine, "DeliveryFine");
            if (returned > 0) tx.Pay(d.BondAccount.Value, _actors[d.Supplier.Value].Account, returned, "DeliveryBondReturn");
            if (d.DestinationInventory is { } inventory) tx.ReleaseCapacity(inventory.Value, d.CapacityHold);
            tx.Commit(); _deliveryContracts[index] = next;
            Emit("DeliveryExpired", d.Supplier.Value, d.Id, d.ItemId, remainder, fine, "Expired");
        }
        foreach (var o in _buyOffers.Values.OrderBy(o => o.View.Id))
            if (o.View.Status == "Open" && Tick >= o.View.ExpiresAtTick)
            { CloseBuyOffer(o, "Expired"); Emit("BuyOfferExpired", o.View.Buyer.Value, o.View.Id); }
    }
}
