namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private void ValidateMarketInvariants()
    {
        var ids = new HashSet<long>(); var accounts = new HashSet<long>();
        foreach (var o in _marketOffers.Values)
        {
            if (o.Id < 1000 || !_actors.ContainsKey(o.Seller) || !_items.ContainsKey(o.Item) ||
                !_buildings.TryGetValue(o.Shop, out var shop) || shop.Type.Kind != BuildingKind.Shop ||
                shop.Owner != o.Seller || shop.Input != o.Inventory || o.Quantity <= 0 || o.BasePrice <= 0 ||
                o.CreatedTick < 1 || o.CreatedTick > Tick || !Enum.IsDefined(o.Status) || o.Round < 0 ||
                o.Round > Market.AuctionRounds || o.Cutoff != checked(o.CreatedTick + _config.TradeWindowTicks +
                    (long)o.Round * Market.AuctionRoundTicks) || o.Allocated < 0 || o.Allocated > o.Quantity ||
                (o.Status == MarketOfferStatus.Collecting && o.Round != 0) ||
                (o.Status == MarketOfferStatus.Auction && (o.Round == 0 || o.CreatedTick + _config.TradeWindowTicks > Tick)) ||
                (o.Status != MarketOfferStatus.Closed && (o.ClosedTick is not null || o.Allocated != 0 || o.Cutoff <= Tick)) ||
                (o.Status == MarketOfferStatus.Closed && (o.ClosedTick != o.Cutoff || o.ClosedTick > Tick ||
                    o.Bids.Count > 0 || (o.Round != 0 && o.Round != Market.AuctionRounds))))
                throw new InvalidOperationException("Invalid market offer");
            var inventory = _economy.Inventories[o.Inventory];
            if (o.Status != MarketOfferStatus.Closed &&
                (!inventory.Reservations.TryGetValue(o.StockHold, out var hold) || hold != new StockReservation(o.Item, o.Quantity)))
                throw new InvalidOperationException("Market stock reservation mismatch");
            if (o.Status == MarketOfferStatus.Closed && inventory.Reservations.ContainsKey(o.StockHold))
                throw new InvalidOperationException("Closed market offer still reserves stock");
            if (_marketAllocations.Where(a => a.OfferId == o.Id).Sum(a => a.Quantity) != o.Allocated)
                throw new InvalidOperationException("Market allocation total mismatch");
            foreach (var pair in o.Bids)
            {
                var b = pair.Value;
                if (b.Buyer.Value != pair.Key || b.OfferId != o.Id || b.Buyer.Value == o.Seller ||
                    !_actors.ContainsKey(b.Buyer.Value) || b.Quantity <= 0 || b.CurrentPriceMinor < o.BasePrice ||
                    b.MaximumPriceMinor < b.CurrentPriceMinor || b.MoneyHold != $"market-bid:{o.Id}:{b.Buyer.Value}" ||
                    (o.Status == MarketOfferStatus.Collecting && b.CurrentPriceMinor != o.BasePrice) ||
                    _economy.Accounts[_actors[b.Buyer.Value].Account].Holds.GetValueOrDefault(b.MoneyHold) !=
                        checked(b.Quantity * b.CurrentPriceMinor))
                    throw new InvalidOperationException("Market bid reservation mismatch");
            }
        }
        foreach (var a in _marketAllocations)
        {
            if (!ids.Add(a.Id) || !accounts.Add(a.EscrowAccount.Value) || a.Id < 1000 || a.EscrowAccount.Value < 1000 ||
                !_marketOffers.TryGetValue(a.OfferId, out var o) || o.Status != MarketOfferStatus.Closed ||
                a.Seller.Value != o.Seller || a.Buyer == a.Seller || !_actors.ContainsKey(a.Buyer.Value) ||
                a.SourceInventory.Value != o.Inventory || a.ItemId != o.Item || a.Quantity <= 0 ||
                a.UnitPriceMinor < o.BasePrice || a.CreatedTick != o.ClosedTick ||
                a.DeadlineTick != checked(a.CreatedTick + Market.PickupTtlTicks) ||
                a.CollectedQuantity < 0 || a.ReleasedQuantity < 0 ||
                checked(a.CollectedQuantity + a.ReleasedQuantity) > a.Quantity ||
                a.PaidMinor != checked(a.CollectedQuantity * a.UnitPriceMinor) ||
                a.FineMinor < 0 || a.RefundedMinor < 0 ||
                a.StockHold != $"market-pickup:{a.Id}" ||
                !_economy.Accounts.TryGetValue(a.EscrowAccount.Value, out var escrow) || escrow.Kind != "ContractEscrow" ||
                escrow.Balance != checked(a.RemainingQuantity * a.UnitPriceMinor) || escrow.Holds.Count != 0)
                throw new InvalidOperationException("Invalid market pickup allocation");
            ValidatePickupLifecycle(a);
        }
    }
    private void ValidatePickupLifecycle(MarketAllocationView a)
    {
        long remaining = a.RemainingQuantity, releasedValue = checked(a.ReleasedQuantity * a.UnitPriceMinor);
        bool active = a.ClosedTick is null;
        if ((active && (a.DeadlineTick <= Tick || a.ReleasedQuantity != 0 || remaining <= 0 ||
                a.Status != (a.CollectedQuantity == 0 ? "AwaitingPickup" : "PartiallyCollected"))) ||
            (!active && (remaining != 0 || a.ClosedTick < a.CreatedTick || a.ClosedTick > Tick ||
                (a.Status == "Fulfilled" ? a.CollectedQuantity != a.Quantity || a.ClosedTick > a.DeadlineTick :
                 a.Status != "Expired" || a.ReleasedQuantity <= 0 || a.ClosedTick != a.DeadlineTick))) ||
            a.FineMinor != PickupFine(releasedValue, Market.PickupFineBasisPoints) ||
            a.RefundedMinor != checked(releasedValue - a.FineMinor))
            throw new InvalidOperationException("Invalid pickup lifecycle");
        var inventory = _economy.Inventories[a.SourceInventory.Value];
        if (remaining > 0 ? !inventory.Reservations.TryGetValue(a.StockHold, out var hold) ||
                hold != new StockReservation(a.ItemId, remaining) : inventory.Reservations.ContainsKey(a.StockHold))
            throw new InvalidOperationException("Pickup stock reservation mismatch");
        var incoming = _economy.Ledger.Where(l => l.CreditAccount == a.EscrowAccount.Value).ToArray();
        var outgoing = _economy.Ledger.Where(l => l.DebitAccount == a.EscrowAccount.Value).ToArray();
        if (incoming.Length != 1 || incoming[0].DebitAccount != _actors[a.Buyer.Value].Account ||
            incoming[0].Reason != "MarketEscrow" || incoming[0].Tick != a.CreatedTick ||
            incoming[0].AmountMinor != checked(a.Quantity * a.UnitPriceMinor) ||
            outgoing.Any(l => l.Tick <= a.CreatedTick || l.Tick > a.DeadlineTick ||
                (l.Reason == "PickupRefund" ? l.CreditAccount != _actors[a.Buyer.Value].Account :
                 l.Reason is not ("PickupPayment" or "PickupFine") || l.CreditAccount != _actors[a.Seller.Value].Account)) ||
            outgoing.Where(l => l.Reason == "PickupPayment").Sum(l => l.AmountMinor) != a.PaidMinor ||
            outgoing.Where(l => l.Reason == "PickupFine").Sum(l => l.AmountMinor) != a.FineMinor ||
            outgoing.Where(l => l.Reason == "PickupRefund").Sum(l => l.AmountMinor) != a.RefundedMinor)
            throw new InvalidOperationException("Pickup ledger mismatch");
    }
    private void AddExpectedMarketHolds(Dictionary<(long Inventory, string Hold), StockReservation> stock,
        Dictionary<(long Account, string Hold), long> money)
    {
        foreach (var o in _marketOffers.Values.Where(o => o.Status != MarketOfferStatus.Closed))
        {
            stock.Add((o.Inventory, o.StockHold), new(o.Item, o.Quantity));
            foreach (var b in o.Bids.Values) money.Add((_actors[b.Buyer.Value].Account, b.MoneyHold), checked(b.Quantity * b.CurrentPriceMinor));
        }
        foreach (var a in _marketAllocations.Where(a => a.RemainingQuantity > 0))
            stock.Add((a.SourceInventory.Value, a.StockHold), new(a.ItemId, a.RemainingQuantity));
    }
}
