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
                a.UnitPriceMinor < o.BasePrice || a.CreatedTick != o.ClosedTick || a.Status != "AwaitingPickup" ||
                a.StockHold != $"market-pickup:{a.Id}" ||
                !_economy.Accounts.TryGetValue(a.EscrowAccount.Value, out var escrow) || escrow.Kind != "ContractEscrow" ||
                escrow.Balance != checked(a.Quantity * a.UnitPriceMinor) || escrow.Holds.Count != 0 ||
                !_economy.Inventories[a.SourceInventory.Value].Reservations.TryGetValue(a.StockHold, out var hold) ||
                hold != new StockReservation(a.ItemId, a.Quantity))
                throw new InvalidOperationException("Invalid market pickup allocation");
        }
    }
    private void AddExpectedMarketHolds(Dictionary<(long Inventory, string Hold), StockReservation> stock,
        Dictionary<(long Account, string Hold), long> money)
    {
        foreach (var o in _marketOffers.Values.Where(o => o.Status != MarketOfferStatus.Closed))
        {
            stock.Add((o.Inventory, o.StockHold), new(o.Item, o.Quantity));
            foreach (var b in o.Bids.Values) money.Add((_actors[b.Buyer.Value].Account, b.MoneyHold), checked(b.Quantity * b.CurrentPriceMinor));
        }
        foreach (var a in _marketAllocations) stock.Add((a.SourceInventory.Value, a.StockHold), new(a.ItemId, a.Quantity));
    }
}
