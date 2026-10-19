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
        RequireActionLocation(actor, c.Action);
        ReceiveDelivery(index, actor.Inventory, c.Action.Quantity, false, null, c.CommandId);
    }
    private void ReceiveDelivery(int index, long sourceInventory, long q, bool inTransit,
        LogisticsState? logistics, string? command = null)
    {
        var d = _deliveryContracts[index]; long supplier = d.Supplier.Value;
        if (q <= 0 || q > (inTransit ? d.InTransitQuantity : d.UnprovidedQuantity)) throw new RuleViolation("InvalidQuantity");
        long delivered = checked(d.DeliveredQuantity + q), payment = checked(q * d.UnitPriceMinor);
        bool complete = delivered == d.Quantity;
        var next = d with { DeliveredQuantity = delivered, PaidMinor = checked(d.PaidMinor + payment),
            InTransitQuantity = inTransit ? checked(d.InTransitQuantity - q) : d.InTransitQuantity,
            BondReturnedMinor = complete ? d.BondMinor : 0, Status = complete ? "Fulfilled" : "PartiallyDelivered",
            ClosedTick = complete ? Tick : null };
        long exported = d.Buyer is null ? checked(_flows[d.ItemId].Exported + q) : _flows[d.ItemId].Exported;
        var tx = _economy.Begin(Tick);
        if (logistics is not null) tx.ConsumeReserved(sourceInventory, logistics.Trip!.CargoHold, q);
        else tx.RemoveFreeStock(sourceInventory, d.ItemId, q);
        if (d.DestinationInventory is { } inventory) tx.ReceiveReservedCapacity(inventory.Value, d.CapacityHold, d.ItemId, q);
        if (logistics is not null) tx.ReleaseCapacity(logistics.Source, logistics.Trip!.ReturnHold);
        tx.Pay(d.EscrowAccount.Value, _actors[supplier].Account, payment, "DeliveryPayment");
        if (complete && d.BondMinor > 0) tx.Pay(d.BondAccount.Value, _actors[supplier].Account, d.BondMinor, "DeliveryBondReturn");
        tx.Commit(); _deliveryContracts[index] = next; _flows[d.ItemId].Exported = exported;
        Emit("DeliveryReceived", supplier, d.Id, d.ItemId, q, payment, next.Status, command);
    }
    private void ExpireDelivery()
    {
        foreach (int index in Enumerable.Range(0, _deliveryContracts.Count).OrderBy(i => _deliveryContracts[i].Id))
        {
            var d = _deliveryContracts[index]; if (d.ClosedTick is not null || Tick < d.DeadlineTick) continue;
            ReleasePreparedLogistics(d.Id); d = _deliveryContracts[index];
            long remainder = d.RemainingQuantity, refund = checked(remainder * d.UnitPriceMinor);
            long fine = DeliveryFine(refund, Delivery.FineBasisPoints), returned = checked(d.BondMinor - fine);
            var next = d with { UnfulfilledQuantity = remainder, RefundedMinor = refund, FineMinor = fine,
                BondReturnedMinor = returned, PreparedQuantity = 0, InTransitQuantity = 0, Status = "Expired", ClosedTick = Tick };
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
