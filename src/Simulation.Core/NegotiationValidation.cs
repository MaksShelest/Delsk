namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private bool NegotiatedPickupOrigin(MarketAllocationView a)
    {
        if(a.NegotiationId==0)return _marketOffers.TryGetValue(a.OfferId,out var o) && o.Status==MarketOfferStatus.Closed &&
            a.Seller.Value==o.Seller && a.SourceInventory.Value==o.Inventory && a.ItemId==o.Item && a.UnitPriceMinor>=o.BasePrice &&
            a.CreatedTick==o.ClosedTick && a.DeadlineTick==checked(a.CreatedTick+Market.PickupTtlTicks);
        if(!_negotiations.TryGetValue(a.NegotiationId,out var n))return false;var t=n.Current.Terms;
        return n.Status==NegotiationStatus.Accepted && n.ContractId==a.Id && n.ClosedTick==a.CreatedTick && t.Kind==TradeKind.Sale &&
            a.OfferId==0 && a.Seller==t.Seller && a.Buyer==t.Buyer && a.SourceInventory==t.Source && a.ItemId==t.ItemId &&
            a.Quantity==t.Quantity && a.UnitPriceMinor==t.UnitPriceMinor && a.DeadlineTick==checked(a.CreatedTick+t.FulfillmentTicks);
    }
    private bool NegotiatedDeliveryOrigin(DeliveryContractView d)
    {
        if(!_negotiations.TryGetValue(d.NegotiationId,out var n))return false;var t=n.Current.Terms;
        return n.Status==NegotiationStatus.Accepted && n.ContractId==d.Id && n.ClosedTick==d.CreatedTick &&
            t.Kind is TradeKind.Purchase or TradeKind.Supply && d.BuyOfferId==0 && d.CapitalOfferId==0 &&
            d.Supplier==t.Seller && d.Buyer==t.Buyer && d.DestinationInventory==t.Destination && d.ItemId==t.ItemId &&
            d.Quantity==t.Quantity && d.UnitPriceMinor==t.UnitPriceMinor && d.DeadlineTick==checked(d.CreatedTick+t.FulfillmentTicks) &&
            d.BuyerAccount.Value==_actors[t.Buyer.Value].Account && d.Position==_economy.Inventories[t.Destination.Value].Position;
    }
    private void ValidateNegotiations()
    {
        if(_negotiations.Values.Any(n=>n.Revisions.IsDefaultOrEmpty))throw new InvalidOperationException("Missing negotiation revisions");
        foreach(var group in _negotiations.Values.GroupBy(n=>n.Revisions[0].ProposedBy))
            if(group.OrderBy(n=>n.Id).Where((n,i)=>n.Id!=checked(1_000_000_000_000L+(group.Key.Value-1)*1_000_000_000L+i+1)).Any())
                throw new InvalidOperationException("Invalid private negotiation namespace");
        foreach(var n in _negotiations.Values)
        {
            if(n.Id<1000 || n.CreatedTick<1 || n.CreatedTick>Tick || !Enum.IsDefined(n.Status) ||
                n.Revisions.IsDefaultOrEmpty || n.Revisions.Length>NegotiationRules.MaxVersions ||
                n.ExpiresTick<=n.CreatedTick || n.ExpiresTick>checked(n.CreatedTick+NegotiationRules.TtlTicks))
                throw new InvalidOperationException("Invalid negotiation");
            var first=n.Revisions[0];
            for(int i=0;i<n.Revisions.Length;i++)
            {
                var r=n.Revisions[i];var t=ValidateTradeTerms(r.Terms,false);
                if(r.Position!=TradePoint(t) || r.Version!=i+1 || r.Tick<n.CreatedTick || r.Tick>Tick || r.Tick>n.ExpiresTick ||
                    r.ProposedBy!=t.Seller && r.ProposedBy!=t.Buyer || t.Seller!=first.Terms.Seller || t.Buyer!=first.Terms.Buyer ||
                    t.Kind!=first.Terms.Kind || t.Election!=first.Terms.Election || t.Source!=first.Terms.Source ||
                    i==0 && r.Tick!=n.CreatedTick || i>0 && (r.ProposedBy==n.Revisions[i-1].ProposedBy || r.Tick<n.Revisions[i-1].Tick))
                    throw new InvalidOperationException("Invalid negotiation revision");
                var kind=i==0?ActionKind.ProposeTrade:ActionKind.CounterTrade;
                if(!_seenCommands.Values.Any(c=>c.ActorId==r.ProposedBy && c.Action.Kind==kind &&
                    (i==0 || c.Action.NegotiationId==n.Id && c.Action.NegotiationVersion==i) && TradeTerms.Same(c.Action.Terms,t) &&
                    _results.TryGetValue(c.CommandId,out var result) && result.Status==ResultStatus.Accepted && result.AppliedTick==r.Tick))
                    throw new InvalidOperationException("Negotiation revision has no accepted command");
            }
            if(n.Status==NegotiationStatus.Proposed ? n.ClosedTick is not null || n.ContractId!=0 || n.ExpiresTick<=Tick :
                n.ClosedTick<n.Current.Tick || n.ClosedTick>Tick || n.ClosedTick is null ||
                (n.Status==NegotiationStatus.Expired ? n.ClosedTick!=n.ExpiresTick : n.ClosedTick>n.ExpiresTick) ||
                (n.Status==NegotiationStatus.Accepted ? n.ContractId<=0 : n.ContractId!=0))
                throw new InvalidOperationException("Invalid negotiation lifecycle");
            if(n.Status is NegotiationStatus.Accepted or NegotiationStatus.Rejected)
            {
                var kind=n.Status==NegotiationStatus.Accepted?ActionKind.AcceptTrade:ActionKind.RejectTrade;
                if(!_seenCommands.Values.Any(c=>c.Action.Kind==kind && c.Action.NegotiationId==n.Id && c.Action.NegotiationVersion==n.Current.Version &&
                    c.ActorId!=n.Current.ProposedBy && (c.ActorId==n.Current.Terms.Buyer || c.ActorId==n.Current.Terms.Seller) &&
                    _results.TryGetValue(c.CommandId,out var r) && r.Status==ResultStatus.Accepted && r.AppliedTick==n.ClosedTick))
                    throw new InvalidOperationException("Negotiation closure has no accepted command");
            }
            if(n.Status!=NegotiationStatus.Accepted)continue;
            var terms=n.Current.Terms;
            bool exists=terms.Kind switch {
                TradeKind.Sale=>_marketAllocations.Any(a=>a.Id==n.ContractId && a.NegotiationId==n.Id),
                TradeKind.Support=>_supportProposals.TryGetValue(n.ContractId,out var p) && p.Candidate==terms.Buyer && p.Seller==terms.Seller &&
                    p.Election==terms.Election && p.CreatedTick==n.ClosedTick && p.AcceptedTick==n.ClosedTick && p.Compensation.SequenceEqual(terms.Compensation),
                _=>_deliveryContracts.Any(d=>d.Id==n.ContractId && d.NegotiationId==n.Id)};
            if(!exists)throw new InvalidOperationException("Negotiation contract missing");
        }
        if(_marketAllocations.Where(a=>a.NegotiationId!=0).Select(a=>a.NegotiationId).Concat(_deliveryContracts.Where(d=>d.NegotiationId!=0).Select(d=>d.NegotiationId))
            .GroupBy(id=>id).Any(g=>g.Count()!=1))throw new InvalidOperationException("Duplicate negotiation contract");
    }
}
