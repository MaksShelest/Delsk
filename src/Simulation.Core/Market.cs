using System.Collections.Immutable;

namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private readonly Dictionary<long, MarketOfferState> _marketOffers = [];
    private readonly List<MarketAllocationView> _marketAllocations = [];
    private readonly DeterministicRandom _marketRandom;
    private MarketSettings Market => _config.Market ?? new();
    private void CreateSellOffer(ActorState actor, CommandEnvelope c)
    {
        var shop = OwnBuilding(actor, c.Action.Building);
        if (shop.Type.Kind != BuildingKind.Shop) throw new RuleViolation("NotAShop");
        if (c.Action.Quantity <= 0) throw new RuleViolation("InvalidQuantity");
        if (c.Action.UnitPriceMinor <= 0) throw new RuleViolation("InvalidPrice");
        _ = checked(c.Action.Quantity * c.Action.UnitPriceMinor);
        long id = _nextId, next = checked(id + 1);
        var offer = new MarketOfferState(id, actor.Id, shop.Id, shop.Input, c.Action.ItemId,
            c.Action.Quantity, c.Action.UnitPriceMinor, Tick, checked(Tick + _config.TradeWindowTicks));
        var tx = _economy.Begin(Tick); tx.ReserveStock(shop.Input, offer.StockHold, offer.Item, offer.Quantity); tx.Commit();
        _marketOffers.Add(id, offer); _nextId = next;
        Emit("MarketOfferPublished", actor.Id, id, offer.Item, offer.Quantity, offer.BasePrice, command: c.CommandId);
    }
    private MarketOfferState OpenMarketOffer(long id)
    {
        if (!_marketOffers.TryGetValue(id, out var offer) || offer.Status == MarketOfferStatus.Closed ||
            offer.CreatedTick >= Tick) throw new RuleViolation("NotAvailable");
        if (Tick > offer.Cutoff) throw new RuleViolation("MarketWindowClosed");
        return offer;
    }
    private void SubmitBuyRequest(ActorState actor, CommandEnvelope c)
    {
        var o = OpenMarketOffer(c.Action.MarketOfferId);
        if (o.Seller == actor.Id) throw new RuleViolation("OwnOffer");
        if (o.Status != MarketOfferStatus.Collecting) throw new RuleViolation("MarketAlreadyInAuction");
        if (c.Action.MarketRound != 0) throw new RuleViolation("StaleMarketRound");
        if (c.Action.Quantity <= 0) throw new RuleViolation("InvalidQuantity");
        if (c.Action.MaximumPriceMinor < o.BasePrice) throw new RuleViolation("BidLimitBelowPrice");
        _ = checked(c.Action.MaximumPriceMinor * c.Action.Quantity);
        string hold = $"market-bid:{o.Id}:{actor.Id}";
        var bid = new MarketBidView(o.Id, new(actor.Id), c.Action.Quantity, o.BasePrice, c.Action.MaximumPriceMinor, hold);
        var tx = _economy.Begin(Tick);
        if (o.Bids.ContainsKey(actor.Id)) tx.ReleaseMoney(actor.Account, hold);
        tx.HoldMoney(actor.Account, hold, checked(bid.Quantity * bid.CurrentPriceMinor)); tx.Commit();
        o.Bids[actor.Id] = bid;
        Emit("MarketBuyRequested", actor.Id, o.Id, o.Item, bid.Quantity, bid.CurrentPriceMinor, command: c.CommandId);
    }
    private void RaiseBid(ActorState actor, CommandEnvelope c)
    {
        var o = OpenMarketOffer(c.Action.MarketOfferId);
        if (o.Status != MarketOfferStatus.Auction) throw new RuleViolation("MarketNotInAuction");
        if (c.Action.MarketRound != o.Round) throw new RuleViolation("StaleMarketRound");
        if (!o.Bids.TryGetValue(actor.Id, out var previous)) throw new RuleViolation("NotAvailable");
        if (c.Action.UnitPriceMinor <= previous.CurrentPriceMinor ||
            c.Action.UnitPriceMinor - previous.CurrentPriceMinor < Market.MinimumRaiseMinor)
            throw new RuleViolation("RaiseTooSmall");
        if (c.Action.UnitPriceMinor > previous.MaximumPriceMinor) throw new RuleViolation("BidLimitExceeded");
        var next = previous with { CurrentPriceMinor = c.Action.UnitPriceMinor };
        var tx = _economy.Begin(Tick); tx.ReleaseMoney(actor.Account, previous.MoneyHold);
        tx.HoldMoney(actor.Account, next.MoneyHold, checked(next.Quantity * next.CurrentPriceMinor)); tx.Commit();
        o.Bids[actor.Id] = next;
        Emit("MarketBidRaised", actor.Id, o.Id, o.Item, next.Quantity, next.CurrentPriceMinor, command: c.CommandId);
    }
    private void WithdrawBid(ActorState actor, CommandEnvelope c)
    {
        var o = OpenMarketOffer(c.Action.MarketOfferId);
        if (c.Action.MarketRound != o.Round) throw new RuleViolation("StaleMarketRound");
        if (!o.Bids.TryGetValue(actor.Id, out var bid)) throw new RuleViolation("NotAvailable");
        var tx = _economy.Begin(Tick); tx.ReleaseMoney(actor.Account, bid.MoneyHold); tx.Commit();
        o.Bids.Remove(actor.Id);
        Emit("MarketBidWithdrawn", actor.Id, o.Id, o.Item, bid.Quantity, command: c.CommandId);
    }
    private void AdvanceMarketWindows()
    {
        foreach (var o in _marketOffers.Values.OrderBy(o => o.Id))
        {
            if (o.Status == MarketOfferStatus.Closed || Tick < o.Cutoff) continue;
            long demand = o.Bids.Values.Sum(b => b.Quantity);
            if (o.Status == MarketOfferStatus.Collecting && demand > o.Quantity)
            {
                o.Status = MarketOfferStatus.Auction; o.Round = 1; o.Cutoff = checked(Tick + Market.AuctionRoundTicks);
                Emit("MarketAuctionStarted", entity: o.Id, item: o.Item, quantity: demand, reason: "1"); continue;
            }
            if (o.Status == MarketOfferStatus.Auction && o.Round < Market.AuctionRounds)
            {
                ++o.Round; o.Cutoff = checked(Tick + Market.AuctionRoundTicks);
                Emit("MarketRoundStarted", entity: o.Id, reason: o.Round.ToString(System.Globalization.CultureInfo.InvariantCulture)); continue;
            }
            AllocateMarketOffer(o);
        }
    }
    private void AllocateMarketOffer(MarketOfferState o)
    {
        var bids = o.Bids.Values.OrderBy(b => b.Buyer.Value).ToArray();
        var actors = bids.Select(b => b.Buyer.Value).ToArray();
        bool tied = bids.GroupBy(b => b.CurrentPriceMinor).Any(g => g.Count() > 1);
        if (tied) _marketRandom.Shuffle(actors);
        var rank = actors.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => p.index);
        var ordered = bids.OrderByDescending(b => b.CurrentPriceMinor).ThenBy(b => rank[b.Buyer.Value]).ToArray();
        var tx = _economy.Begin(Tick); tx.ReleaseStock(o.Inventory, o.StockHold);
        var allocations = new List<MarketAllocationView>(); long available = o.Quantity, next = _nextId;
        foreach (var bid in ordered)
        {
            long quantity = Math.Min(available, bid.Quantity); available -= quantity;
            if (quantity > 0)
            {
                long id = next++; long account = next++; string stockHold = $"market-pickup:{id}";
                tx.CreateAccount(account, "ContractEscrow");
                tx.Pay(_actors[bid.Buyer.Value].Account, account, checked(quantity * bid.CurrentPriceMinor), "MarketEscrow", bid.MoneyHold);
                tx.ReserveStock(o.Inventory, stockHold, o.Item, quantity);
                allocations.Add(new(id, o.Id, new(o.Seller), bid.Buyer, new(o.Inventory), o.Item,
                    quantity, bid.CurrentPriceMinor, new(account), stockHold, Tick));
            }
            if (quantity < bid.Quantity) tx.ReleaseMoney(_actors[bid.Buyer.Value].Account, bid.MoneyHold);
        }
        tx.Commit(); _nextId = next; o.Allocated = o.Quantity - available;
        o.Status = MarketOfferStatus.Closed; o.ClosedTick = Tick; o.Bids.Clear(); _marketAllocations.AddRange(allocations);
        if (tied)
            foreach (long actor in actors) Emit("MarketTieRank", actor, o.Id, reason: rank[actor].ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var a in allocations)
            Emit("MarketPurchaseAllocated", a.Buyer.Value, a.Id, a.ItemId, a.Quantity,
                checked(a.Quantity * a.UnitPriceMinor), "AwaitingPickup");
        Emit("MarketWindowClosed", entity: o.Id, item: o.Item, quantity: o.Allocated);
    }
    private MarketOfferView[] MarketOfferViews() => _marketOffers.Values.OrderBy(o => o.Id).Select(o =>
        new MarketOfferView(o.Id, new(o.Seller), new(o.Shop), _buildings[o.Shop].Position, o.Item, o.Quantity, o.BasePrice,
            o.CreatedTick, o.Cutoff, o.Round, o.Status, o.Allocated, o.ClosedTick,
            o.Bids.Values.OrderBy(b => b.Buyer.Value).Select(b => new PublicMarketBid(b.Buyer, b.Quantity, b.CurrentPriceMinor)).ToImmutableArray())).ToArray();
    private MarketBidView[] MarketBidViews() => _marketOffers.Values.OrderBy(o => o.Id).SelectMany(o =>
        o.Bids.Values.OrderBy(b => b.Buyer.Value)).ToArray();
    private SavedMarketOffer[] SaveMarketOffers() => _marketOffers.Values.OrderBy(o => o.Id).Select(o =>
        new SavedMarketOffer(o.Id, o.Seller, o.Shop, o.Inventory, o.Item, o.Quantity, o.BasePrice, o.CreatedTick,
            o.Cutoff, o.Round, o.Status, o.Allocated, o.ClosedTick, o.StockHold,
            o.Bids.Values.OrderBy(b => b.Buyer.Value).ToArray())).ToArray();
    private void RestoreMarket(SavedMarketOffer[] offers, MarketAllocationView[] allocations, ulong random)
    {
        _marketRandom.RestoreState(random); _marketOffers.Clear(); _marketAllocations.Clear();
        foreach (var v in offers)
        {
            var o = new MarketOfferState(v.Id, v.Seller, v.Shop, v.Inventory, v.Item, v.Quantity, v.BasePrice, v.CreatedTick, v.Cutoff)
                { Round = v.Round, Status = v.Status, Allocated = v.Allocated, ClosedTick = v.ClosedTick };
            if (o.StockHold != v.StockHold) throw new CheckpointException("InvalidMarketHoldId");
            foreach (var b in v.Bids) o.Bids.Add(b.Buyer.Value, b);
            _marketOffers.Add(o.Id, o);
        }
        _marketAllocations.AddRange(allocations);
    }
}
