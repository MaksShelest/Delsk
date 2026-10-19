namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private readonly Dictionary<long, BuyOfferState> _buyOffers = [];
    private readonly List<DeliveryContractView> _deliveryContracts = [];
    private long _capitalCompensation;
    private DeliverySettings Delivery => _config.Delivery ?? new();
    private static long DeliveryFine(long value, int bps) => checked((long)((Int128)value * bps / 10000));
    private void CreateBuyOffer(ActorState actor, CommandEnvelope c)
    {
        RequireTradingAccess(actor.Id);
        if (c.Action.Quantity <= 0) throw new RuleViolation("InvalidQuantity");
        if (c.Action.UnitPriceMinor <= 0) throw new RuleViolation("InvalidPrice");
        if (!_items.ContainsKey(c.Action.ItemId)) throw new RuleViolation("UnknownItem");
        if (!_economy.Inventories.TryGetValue(c.Action.Destination.Value, out var destination) ||
            destination.Owner != actor.Id || !_buildings.Values.Any(b => b.Input == destination.Id && b.Owner == actor.Id))
            throw new RuleViolation("NotAvailable");
        long id = _nextId, next = checked(id + 1), budget = checked(c.Action.Quantity * c.Action.UnitPriceMinor);
        _ = checked(c.Action.Quantity * _items[c.Action.ItemId].UnitVolume);
        var offer = new BuyOfferState(new(id, new(actor.Id), new(destination.Id), destination.Position,
            c.Action.ItemId, c.Action.Quantity, c.Action.Quantity, c.Action.UnitPriceMinor, Tick,
            checked(Tick + Delivery.OfferTtlTicks), null, 1, 0, 0, "Open"));
        var tx = _economy.Begin(Tick); tx.HoldMoney(actor.Account, offer.BudgetHold, budget); tx.Commit();
        _buyOffers.Add(id, offer); _nextId = next;
        Emit("BuyOfferPublished", actor.Id, id, offer.View.ItemId, offer.View.Quantity, budget, command: c.CommandId);
    }
    private (long Price, Dictionary<long, SupplyRequest> Requests) SupplyTarget(GameAction action)
    {
        if ((action.BuyOfferId > 0) == (action.OfferId > 0)) throw new RuleViolation("NotAvailable");
        if (action.BuyOfferId > 0)
        {
            if (!_buyOffers.TryGetValue(action.BuyOfferId, out var b) || b.View.Status != "Open" ||
                b.View.CreatedTick >= Tick || Tick > b.View.ExpiresAtTick) throw new RuleViolation("NotAvailable");
            if (action.OfferWindow != b.View.Window) throw new RuleViolation("StaleOfferWindow");
            return (b.View.UnitPriceMinor, b.Requests);
        }
        if (!_offers.TryGetValue(action.OfferId, out var o) || o.Remaining <= 0 || o.CreatedTick >= Tick || o.ExpiresAtTick < Tick) throw new RuleViolation("NotAvailable");
        if (action.OfferWindow != o.Window) throw new RuleViolation("StaleOfferWindow");
        return (o.Definition.UnitPriceMinor, o.SupplyRequests);
    }
    private void AcceptDeliveryQuantity(ActorState actor, CommandEnvelope c)
    {
        var (price, requests) = SupplyTarget(c.Action);
        if (c.Action.BuyOfferId > 0 && _buyOffers[c.Action.BuyOfferId].View.Buyer.Value == actor.Id)
            throw new RuleViolation("OwnOffer");
        if(c.Action.BuyOfferId>0)RequireTradingAccess(actor.Id,_buyOffers[c.Action.BuyOfferId].View.Buyer.Value);
        if (c.Action.OfferId > 0 && _offers[c.Action.OfferId].Requests.ContainsKey(actor.Id))
            throw new RuleViolation("ExistingCapitalSale");
        if (c.Action.Quantity <= 0) throw new RuleViolation("InvalidQuantity");
        long bond = DeliveryFine(checked(c.Action.Quantity * price), Delivery.FineBasisPoints);
        string hold = $"supply:{c.Action.BuyOfferId}:{c.Action.OfferId}:{actor.Id}";
        var request = new SupplyRequest(c.Action.BuyOfferId, c.Action.OfferId, new(actor.Id), c.Action.Quantity, bond, hold, Tick);
        var tx = _economy.Begin(Tick);
        if (requests.TryGetValue(actor.Id, out var old) && old.BondMinor > 0) tx.ReleaseMoney(actor.Account, old.BondHold);
        if (bond > 0) tx.HoldMoney(actor.Account, hold, bond); tx.Commit(); requests[actor.Id] = request;
        if (c.Action.BuyOfferId > 0)
        {
            var offer = _buyOffers[c.Action.BuyOfferId];
            offer.View = offer.View with { CutoffTick = offer.View.CutoffTick ?? Math.Min(offer.View.ExpiresAtTick, checked(Tick + _config.TradeWindowTicks)) };
        }
        else { var offer = _offers[c.Action.OfferId]; offer.Cutoff ??= Math.Min(offer.ExpiresAtTick ?? long.MaxValue, checked(Tick + _config.TradeWindowTicks)); }
        Emit("SupplyQuantityRequested", actor.Id, c.Action.BuyOfferId > 0 ? c.Action.BuyOfferId : c.Action.OfferId,
            quantity: request.Quantity, money: bond, command: c.CommandId);
    }
    private void WithdrawSupplyRequest(ActorState actor, CommandEnvelope c)
    {
        var (_, requests) = SupplyTarget(c.Action);
        if (!requests.TryGetValue(actor.Id, out var r)) throw new RuleViolation("NotAvailable");
        var tx = _economy.Begin(Tick); if (r.BondMinor > 0) tx.ReleaseMoney(actor.Account, r.BondHold); tx.Commit();
        requests.Remove(actor.Id); Emit("SupplyRequestWithdrawn", actor.Id, quantity: r.Quantity, command: c.CommandId);
    }
    private void CancelBuyOffer(ActorState actor, CommandEnvelope c)
    {
        if (!_buyOffers.TryGetValue(c.Action.BuyOfferId, out var offer) || offer.View.Buyer.Value != actor.Id ||
            offer.View.Status != "Open") throw new RuleViolation("NotAvailable");
        CloseBuyOffer(offer, "Cancelled"); Emit("BuyOfferCancelled", actor.Id, offer.View.Id, command: c.CommandId);
    }
    private void CloseBuyOffer(BuyOfferState offer, string status)
    {
        var v = offer.View; var tx = _economy.Begin(Tick);
        tx.ReleaseMoney(_actors[v.Buyer.Value].Account, offer.BudgetHold);
        foreach (var r in offer.Requests.Values)
            if (r.BondMinor > 0) tx.ReleaseMoney(_actors[r.Supplier.Value].Account, r.BondHold);
        tx.Commit(); offer.Requests.Clear();
        offer.View = v with { ReleasedQuantity = checked(v.ReleasedQuantity + v.RemainingQuantity), RemainingQuantity = 0, CutoffTick = null, Status = status };
    }
    private Dictionary<long, long> AllocateFixedVolume((long Actor, long Quantity)[] requests, long capacity)
    {
        Int128 total = requests.Aggregate((Int128)0, (sum, r) => sum + r.Quantity);
        long volume = (long)Int128.Min(total, capacity);
        var result = requests.ToDictionary(r => r.Actor, r => total <= capacity ? r.Quantity : (long)((Int128)r.Quantity * volume / total));
        long remainder = volume - result.Values.Sum();
        if (remainder > 0)
        {
            var order = requests.Select(r => r.Actor).Order().ToArray(); _allocationRandom.Shuffle(order);
            var rank = order.Select((id, n) => (id, n)).ToDictionary(p => p.id, p => p.n);
            foreach (var r in requests.OrderByDescending(r => (Int128)r.Quantity * volume % total)
                .ThenBy(r => rank[r.Actor]).Take((int)remainder)) ++result[r.Actor];
        }
        return result;
    }
    private void CloseBuyWindows()
    {
        foreach (var offer in _buyOffers.Values.OrderBy(o => o.View.Id))
        {
            var v = offer.View; if (v.Status != "Open" || v.CutoffTick is null || Tick < v.CutoffTick) continue;
            var requests = offer.Requests.Values.OrderBy(r => r.Supplier.Value).ToArray();
            var amounts = AllocateFixedVolume(requests.Select(r => (r.Supplier.Value, r.Quantity)).ToArray(), v.RemainingQuantity);
            long allocated = amounts.Values.Sum(), nextId = _nextId;
            var destination = _economy.Inventories[v.DestinationInventory.Value];
            bool space = checked(destination.Occupied(_items) + destination.Incoming + checked(allocated * _items[v.ItemId].UnitVolume)) <= destination.Capacity;
            var tx = _economy.Begin(Tick); var contracts = new List<DeliveryContractView>();
            foreach (var r in requests)
            {
                long quantity = space ? amounts[r.Supplier.Value] : 0;
                if (quantity > 0) contracts.Add(StageDelivery(tx, ref nextId, r, quantity, v.ItemId, v.UnitPriceMinor,
                    v.Buyer, v.DestinationInventory, v.Position, _actors[v.Buyer.Value].Account, offer.BudgetHold));
                else if (r.BondMinor > 0) tx.ReleaseMoney(_actors[r.Supplier.Value].Account, r.BondHold);
            }
            tx.Commit(); _nextId = nextId; _deliveryContracts.AddRange(contracts); offer.Requests.Clear();
            if (!space) allocated = 0;
            long remaining = v.RemainingQuantity - allocated;
            offer.View = v with { RemainingQuantity = remaining, AllocatedQuantity = checked(v.AllocatedQuantity + allocated),
                CutoffTick = null, Window = checked(v.Window + 1), Status = remaining == 0 ? "Exhausted" : "Open" };
            foreach (var d in contracts) EmitDeliveryCreated(d);
            Emit("BuyVolumeWindowClosed", entity: v.Id, quantity: allocated, reason: space ? "Allocated" : "NoCapacity");
        }
    }
    private DeliveryContractView StageDelivery(EconomyTransaction tx, ref long nextId, SupplyRequest request, long quantity,
        string item, long price, ParticipantId? buyer, InventoryId? destination, Cell position, long buyerAccount, string budgetHold)
    {
        long id = nextId++, escrow = nextId++, bondAccount = nextId++, value = checked(quantity * price);
        long bond = DeliveryFine(value, Delivery.FineBasisPoints); string capacityHold = $"delivery:{id}";
        tx.CreateAccount(escrow, "ContractEscrow"); tx.CreateAccount(bondAccount, "SupplierBond");
        tx.Pay(buyerAccount, escrow, value, "DeliveryFunding", budgetHold);
        if (bond > 0) tx.Pay(_actors[request.Supplier.Value].Account, bondAccount, bond, "DeliveryBond", request.BondHold);
        if (request.BondMinor > bond) tx.ReleaseMoney(_actors[request.Supplier.Value].Account, request.BondHold);
        if (destination is { } inventory) tx.ReserveCapacity(inventory.Value, capacityHold, item, quantity);
        return new(id, request.BuyOfferId, request.CapitalOfferId, buyer, request.Supplier, destination, position,
            item, quantity, price, new(buyerAccount), new(escrow), new(bondAccount), bond, capacityHold,
            Tick, checked(Tick + Delivery.ContractTtlTicks));
    }
    private void EmitDeliveryCreated(DeliveryContractView d) => Emit("DeliveryContractCreated", d.Supplier.Value, d.Id,
        d.ItemId, d.Quantity, checked(d.Quantity * d.UnitPriceMinor), "Active");
    private BuyOfferView[] BuyOfferViews() => _buyOffers.Values.OrderBy(o => o.View.Id).Select(o => o.View).ToArray();
    private SupplyRequest[] SupplyRequestViews() => _buyOffers.Values.OrderBy(o => o.View.Id).SelectMany(o => o.Requests.Values.OrderBy(r => r.Supplier.Value))
        .Concat(_offers.Values.OrderBy(o => o.Definition.Id).SelectMany(o => o.SupplyRequests.Values.OrderBy(r => r.Supplier.Value))).ToArray();
    private SavedBuyOffer[] SaveBuyOffers() => _buyOffers.Values.OrderBy(o => o.View.Id).Select(o =>
        new SavedBuyOffer(o.View, o.Requests.Values.OrderBy(r => r.Supplier.Value).ToArray())).ToArray();
    private void RestoreDelivery(SavedBuyOffer[] offers, DeliveryContractView[] contracts, long compensation)
    {
        _buyOffers.Clear(); _deliveryContracts.Clear(); _capitalCompensation = compensation;
        foreach (var v in offers)
        {
            var o = new BuyOfferState(v.View); foreach (var r in v.Requests) o.Requests.Add(r.Supplier.Value, r);
            _buyOffers.Add(v.View.Id, o);
        }
        _deliveryContracts.AddRange(contracts);
    }
}
