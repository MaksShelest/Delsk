namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private void DeliverContract(ActorState actor, CommandEnvelope c)
    {
        int index = _deliveryContracts.FindIndex(d => d.Id == c.Action.ContractId && d.Supplier.Value == actor.Id);
        if (index < 0) throw new RuleViolation("NotAvailable");
        var d = _deliveryContracts[index];
        if (d.CreatedTick>=Tick || d.ClosedTick is not null || Tick > d.DeadlineTick) throw new RuleViolation("ContractClosed");
        RequireIdle(actor);
        RequireActionLocation(actor, c.Action);
        if (c.Action.Quantity <= 0 || c.Action.Quantity > d.UnprovidedQuantity) throw new RuleViolation("InvalidQuantity");
        BeginTransfer(actor,c,actor.Inventory,d.DestinationInventory?.Value??0,d.ItemId,c.Action.Quantity,"Delivery",d.Id);
    }
    private void ReceiveDelivery(int index, long sourceInventory, long q, bool inTransit,
        LogisticsState? logistics, string? command = null, string? preparedHold = null)
    {
        var d = _deliveryContracts[index]; long supplier = d.Supplier.Value;
        if (q <= 0 || q > (inTransit ? d.InTransitQuantity : preparedHold is not null ? d.PreparedQuantity : d.UnprovidedQuantity)) throw new RuleViolation("InvalidQuantity");
        long delivered = checked(d.DeliveredQuantity + q), payment = checked(q * d.UnitPriceMinor);
        bool complete = delivered == d.Quantity;
        var next = d with { DeliveredQuantity = delivered, PaidMinor = checked(d.PaidMinor + payment),
            InTransitQuantity = inTransit ? checked(d.InTransitQuantity - q) : d.InTransitQuantity,
            PreparedQuantity = preparedHold is not null ? checked(d.PreparedQuantity-q) : d.PreparedQuantity,
            BondReturnedMinor = complete ? d.BondMinor : 0, Status = complete ? "Fulfilled" : "PartiallyDelivered",
            ClosedTick = complete ? Tick : null };
        long exported = d.Buyer is null ? checked(_flows[d.ItemId].Exported + q) : _flows[d.ItemId].Exported;
        var tx = _economy.Begin(Tick);
        if (logistics is not null) tx.ConsumeReserved(sourceInventory, logistics.Trip!.CargoHold, q);
        else if(preparedHold is not null) tx.ConsumeReserved(sourceInventory,preparedHold,q);
        else tx.RemoveFreeStock(sourceInventory, d.ItemId, q);
        long excess=0;
        if (d.DestinationInventory is { } inventory){if(d.CapacityForecast)excess=ReceiveForecast(tx,inventory.Value,d.ItemId,q,$"delivery:{d.Id}");else tx.ReceiveReservedCapacity(inventory.Value, d.CapacityHold, d.ItemId, q);}
        if (logistics is not null) tx.ReleaseCapacityVolume(logistics.Trip!.Freight!.GoodsSource.Value, logistics.Trip.ReturnHold,d.ItemId,q);
        tx.Pay(d.EscrowAccount.Value, _actors[supplier].Account, payment, "DeliveryPayment");
        if (complete && d.BondMinor > 0) tx.Pay(d.BondAccount.Value, _actors[supplier].Account, d.BondMinor, "DeliveryBondReturn");
        tx.Commit(); _deliveryContracts[index] = next; _flows[d.ItemId].Exported = exported;
        if(d.DestinationInventory is {} received)ForecastReceived(received.Value,d.ItemId,excess);
        Emit("DeliveryReceived", supplier, d.Id, d.ItemId, q, payment, next.Status, command);
    }
    private void ExpireDelivery()
    {
        foreach (int index in Enumerable.Range(0, _deliveryContracts.Count).OrderBy(i => _deliveryContracts[i].Id))
        {
            var d = _deliveryContracts[index]; if (d.ClosedTick is not null || Tick < d.DeadlineTick) continue;
            foreach(var t in _transfers.Values.Where(t=>t.Kind=="Delivery" && t.Contract==d.Id).ToArray()) CancelTransfer(t,"ContractClosed");
            ReleasePreparedLogistics(d.Id); d = _deliveryContracts[index];
            long remainder = d.RemainingQuantity, refund = checked(remainder * d.UnitPriceMinor);
            long fine = DeliveryFine(refund, Delivery.FineBasisPoints), returned = checked(d.BondMinor - fine);
            var next = d with { UnfulfilledQuantity = remainder, RefundedMinor = refund, FineMinor = fine,
                BondReturnedMinor = returned, PreparedQuantity = 0, InTransitQuantity = 0, Status = "Expired", ClosedTick = Tick };
            var tx = _economy.Begin(Tick);
            tx.Pay(d.EscrowAccount.Value, d.BuyerAccount.Value, refund, "DeliveryRefund");
            if (fine > 0) tx.Pay(d.BondAccount.Value, d.Buyer is null ? _capitalCompensation : d.BuyerAccount.Value, fine, "DeliveryFine");
            if (returned > 0) tx.Pay(d.BondAccount.Value, _actors[d.Supplier.Value].Account, returned, "DeliveryBondReturn");
            if (!d.CapacityForecast && d.DestinationInventory is { } inventory) tx.ReleaseCapacity(inventory.Value, d.CapacityHold);
            tx.Commit(); _deliveryContracts[index] = next;
            Emit("DeliveryExpired", d.Supplier.Value, d.Id, d.ItemId, remainder, fine, "Expired");
        }
        foreach (var o in _buyOffers.Values.OrderBy(o => o.View.Id))
            if (o.View.Status == "Open" && Tick >= o.View.ExpiresAtTick)
            { CloseBuyOffer(o, "Expired"); Emit("BuyOfferExpired", o.View.Buyer.Value, o.View.Id); }
    }
}
