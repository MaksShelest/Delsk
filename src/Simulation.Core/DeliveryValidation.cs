namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private void ValidateDeliveryInvariants()
    {
        foreach (var state in _buyOffers.Values)
        {
            var o = state.View;
            if (o.Id < 1000 || !_actors.ContainsKey(o.Buyer.Value) || !_items.ContainsKey(o.ItemId) ||
                !_economy.Inventories.TryGetValue(o.DestinationInventory.Value, out var inventory) ||
                inventory.Owner != o.Buyer.Value || inventory.Position != o.Position ||
                !_buildings.Values.Any(b => b.Input == inventory.Id && b.Owner == o.Buyer.Value) ||
                o.Quantity <= 0 || o.UnitPriceMinor <= 0 || o.CreatedTick < 1 || o.CreatedTick > Tick ||
                o.ExpiresAtTick != checked(o.CreatedTick + Delivery.OfferTtlTicks) || o.Window < 1 ||
                o.RemainingQuantity < 0 || o.AllocatedQuantity < 0 || o.ReleasedQuantity < 0 ||
                checked(o.RemainingQuantity + o.AllocatedQuantity + o.ReleasedQuantity) != o.Quantity ||
                _deliveryContracts.Where(d => d.BuyOfferId == o.Id).Sum(d => d.Quantity) != o.AllocatedQuantity ||
                (o.Status == "Open" ? o.RemainingQuantity == 0 || o.ExpiresAtTick <= Tick || o.ReleasedQuantity != 0 :
                    o.Status is not ("Exhausted" or "Cancelled" or "Expired") || o.RemainingQuantity != 0 ||
                    state.Requests.Count > 0 || o.CutoffTick is not null ||
                    (o.Status == "Exhausted" ? o.AllocatedQuantity != o.Quantity : o.ReleasedQuantity == 0) ||
                    (o.Status == "Expired" && o.ExpiresAtTick > Tick)) ||
                (o.CutoffTick is { } cutoff && (cutoff <= Tick || cutoff > o.ExpiresAtTick || cutoff <= o.CreatedTick)) ||
                (state.Requests.Count > 0 && o.CutoffTick is null) ||
                _economy.Accounts[_actors[o.Buyer.Value].Account].Holds.GetValueOrDefault(state.BudgetHold) !=
                    checked(o.RemainingQuantity * o.UnitPriceMinor))
                throw new InvalidOperationException("Invalid buy offer");
            foreach (var pair in state.Requests)
                ValidateSupplyRequest(pair.Key, pair.Value, o.Id, 0, o.UnitPriceMinor, o.Buyer.Value);
        }
        foreach (var o in _offers.Values)
        {
            if (o.Window < 1 || (o.SupplyRequests.Count > 0 && o.Cutoff is null) ||
                o.Definition.Quantity != checked(o.Remaining + o.ReleasedQuantity + _settlements.Where(s => s.OfferId == o.Definition.Id).Sum(s => s.Accepted) +
                    _deliveryContracts.Where(d => d.CapitalOfferId == o.Definition.Id).Sum(d => d.Quantity)))
                throw new InvalidOperationException("Capital delivery quota mismatch");
            foreach (var pair in o.SupplyRequests)
            {
                if (o.Requests.ContainsKey(pair.Key)) throw new InvalidOperationException("Duplicate capital supplier");
                ValidateSupplyRequest(pair.Key, pair.Value, 0, o.Definition.Id, o.Definition.UnitPriceMinor, 0);
            }
        }
        if (_deliveryContracts.Any(d => d.Buyer is null) ? _capitalCompensation == 0 ||
                !_economy.Accounts.TryGetValue(_capitalCompensation, out var compensation) || compensation.Kind != "CapitalCompensation" || compensation.Holds.Count != 0 :
                _capitalCompensation != 0)
            throw new InvalidOperationException("Invalid capital compensation account");
        var ids = new HashSet<long>(); var accounts = new HashSet<long>();
        foreach (var d in _deliveryContracts)
        {
            if (!ids.Add(d.Id) || d.Id < 1000 || !_actors.ContainsKey(d.Supplier.Value) || !_items.ContainsKey(d.ItemId) ||
                d.PreparedQuantity < 0 || d.InTransitQuantity < 0 || d.UnprovidedQuantity < 0 ||
                d.Quantity <= 0 || d.UnitPriceMinor <= 0 || d.CreatedTick < 1 || d.CreatedTick >= Tick + 1 ||
                d.DeadlineTick != checked(d.CreatedTick + Delivery.ContractTtlTicks) ||
                d.CapacityHold != $"delivery:{d.Id}" || d.DeliveredQuantity < 0 || d.DeliveredQuantity > d.Quantity ||
                d.BondMinor != DeliveryFine(checked(d.Quantity * d.UnitPriceMinor), Delivery.FineBasisPoints) ||
                d.PaidMinor != checked(d.DeliveredQuantity * d.UnitPriceMinor) ||
                !accounts.Add(d.EscrowAccount.Value) || !accounts.Add(d.BondAccount.Value) ||
                !_economy.Accounts.TryGetValue(d.EscrowAccount.Value, out var escrow) || escrow.Kind != "ContractEscrow" || escrow.Holds.Count != 0 ||
                !_economy.Accounts.TryGetValue(d.BondAccount.Value, out var bond) || bond.Kind != "SupplierBond" || bond.Holds.Count != 0 ||
                escrow.Balance != checked(d.RemainingQuantity * d.UnitPriceMinor) || bond.Balance != (d.ClosedTick is null ? d.BondMinor : 0))
                throw new InvalidOperationException("Invalid delivery contract");
            if (d.Buyer is { } buyer)
            {
                if (!_buyOffers.TryGetValue(d.BuyOfferId, out var offer) || d.CapitalOfferId != 0 ||
                    buyer != offer.View.Buyer || buyer == d.Supplier || d.BuyerAccount.Value != _actors[buyer.Value].Account ||
                    d.DestinationInventory != offer.View.DestinationInventory || d.Position != offer.View.Position ||
                    d.ItemId != offer.View.ItemId || d.UnitPriceMinor != offer.View.UnitPriceMinor)
                    throw new InvalidOperationException("Delivery buyer reference mismatch");
            }
            else if (d.BuyOfferId != 0 || !_offers.TryGetValue(d.CapitalOfferId, out var capital) ||
                d.DestinationInventory is not null || d.BuyerAccount.Value != _capital || d.Position != capital.Definition.Position ||
                d.ItemId != capital.Definition.ItemId || d.UnitPriceMinor != capital.Definition.UnitPriceMinor)
                throw new InvalidOperationException("Delivery capital reference mismatch");
            bool active = d.ClosedTick is null;
            long unfulfilled = active ? 0 : d.Quantity - d.DeliveredQuantity;
            if (d.UnfulfilledQuantity != unfulfilled || d.RefundedMinor != checked(unfulfilled * d.UnitPriceMinor) ||
                d.FineMinor != DeliveryFine(d.RefundedMinor, Delivery.FineBasisPoints) ||
                d.BondReturnedMinor != (active ? 0 : d.BondMinor - d.FineMinor) ||
                (active ? d.DeadlineTick <= Tick || d.RemainingQuantity <= 0 ||
                    d.Status != (d.DeliveredQuantity == 0 ? "Active" : "PartiallyDelivered") :
                    d.ClosedTick <= d.CreatedTick || d.ClosedTick > Tick || d.ClosedTick > d.DeadlineTick ||
                    (unfulfilled == 0 ? d.Status != "Fulfilled" : d.Status != "Expired" || d.ClosedTick != d.DeadlineTick)))
                throw new InvalidOperationException("Invalid delivery lifecycle");
            if (d.DestinationInventory is { } inventory)
            {
                long held = _economy.Inventories[inventory.Value].CapacityHolds.GetValueOrDefault(d.CapacityHold);
                if (held != checked(d.RemainingQuantity * _items[d.ItemId].UnitVolume))
                    throw new InvalidOperationException("Delivery capacity reservation mismatch");
            }
            long prepared = _logistics.Values.Where(l=>l.Trip?.ContractId==d.Id).Sum(l=>FreightRemainingSource(l.Trip!)) +
                _transfers.Values.Where(t=>t.Kind=="Delivery" && t.Contract==d.Id).Sum(t=>t.Remaining);
            long transit = _logistics.Values.Where(l=>l.Trip?.ContractId==d.Id).Sum(l=>l.Trip!.Freight!.Cargo);
            if (d.PreparedQuantity != (active ? prepared : 0) || d.InTransitQuantity != (active ? transit : 0))
                throw new InvalidOperationException("Delivery trip quota mismatch");
            ValidateDeliveryLedger(d);
        }
    }
    private void ValidateSupplyRequest(long key, SupplyRequest r, long buy, long capital, long price, long buyer)
    {
        if (key != r.Supplier.Value || !_actors.ContainsKey(key) || key == buyer || r.BuyOfferId != buy ||
            r.CapitalOfferId != capital || r.Quantity <= 0 || r.SubmittedTick < 1 || r.SubmittedTick > Tick ||
            r.BondHold != $"supply:{buy}:{capital}:{key}" || r.BondMinor != DeliveryFine(checked(r.Quantity * price), Delivery.FineBasisPoints) ||
            _economy.Accounts[_actors[key].Account].Holds.GetValueOrDefault(r.BondHold) != r.BondMinor)
            throw new InvalidOperationException("Invalid supplier bond request");
    }
    private void ValidateDeliveryLedger(DeliveryContractView d)
    {
        long supplier = _actors[d.Supplier.Value].Account;
        var funding = _economy.Ledger.Where(l => l.CreditAccount == d.EscrowAccount.Value).ToArray();
        var collateral = _economy.Ledger.Where(l => l.CreditAccount == d.BondAccount.Value).ToArray();
        var payments = _economy.Ledger.Where(l => l.DebitAccount == d.EscrowAccount.Value).ToArray();
        var release = _economy.Ledger.Where(l => l.DebitAccount == d.BondAccount.Value).ToArray();
        if (funding.Length != 1 || funding[0].Reason != "DeliveryFunding" || funding[0].DebitAccount != d.BuyerAccount.Value ||
            funding[0].Tick != d.CreatedTick || funding[0].AmountMinor != checked(d.Quantity * d.UnitPriceMinor) ||
            collateral.Length != (d.BondMinor > 0 ? 1 : 0) || collateral.Any(l => l.Reason != "DeliveryBond" ||
                l.DebitAccount != supplier || l.AmountMinor != d.BondMinor || l.Tick != d.CreatedTick) ||
            payments.Any(l => l.Tick <= d.CreatedTick || l.Tick > d.DeadlineTick ||
                (l.Reason == "DeliveryPayment" ? l.CreditAccount != supplier :
                    l.Reason != "DeliveryRefund" || l.CreditAccount != d.BuyerAccount.Value || l.Tick != d.DeadlineTick)) ||
            release.Any(l => l.Tick <= d.CreatedTick || l.Tick != d.ClosedTick ||
                (l.Reason == "DeliveryBondReturn" ? l.CreditAccount != supplier : l.Reason != "DeliveryFine" ||
                    l.CreditAccount != (d.Buyer is null ? _capitalCompensation : d.BuyerAccount.Value))) ||
            payments.Where(l => l.Reason == "DeliveryPayment").Sum(l => l.AmountMinor) != d.PaidMinor ||
            payments.Where(l => l.Reason == "DeliveryRefund").Sum(l => l.AmountMinor) != d.RefundedMinor ||
            release.Where(l => l.Reason == "DeliveryFine").Sum(l => l.AmountMinor) != d.FineMinor ||
            release.Where(l => l.Reason == "DeliveryBondReturn").Sum(l => l.AmountMinor) != d.BondReturnedMinor)
            throw new InvalidOperationException("Delivery ledger mismatch");
    }
    private void AddExpectedDeliveryHolds(Dictionary<(long Inventory, string Hold), long> capacity,
        Dictionary<(long Account, string Hold), long> money)
    {
        foreach (var o in _buyOffers.Values.Where(o => o.View.RemainingQuantity > 0))
            money.Add((_actors[o.View.Buyer.Value].Account, o.BudgetHold), checked(o.View.RemainingQuantity * o.View.UnitPriceMinor));
        foreach (var r in SupplyRequestViews().Where(r => r.BondMinor > 0))
            money.Add((_actors[r.Supplier.Value].Account, r.BondHold), r.BondMinor);
        foreach (var d in _deliveryContracts.Where(d => d.ClosedTick is null && d.DestinationInventory is not null))
            capacity.Add((d.DestinationInventory!.Value.Value, d.CapacityHold), checked(d.RemainingQuantity * _items[d.ItemId].UnitVolume));
    }
}
