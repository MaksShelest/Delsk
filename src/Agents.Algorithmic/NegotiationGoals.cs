using Simulation.Core;

namespace Agents.Algorithmic;

public sealed partial class AlgorithmicController
{
    private bool CanTradeWith(ControllerObservation o,ParticipantId other) => o.TradingParticipants.Contains(Actor) && o.TradingParticipants.Contains(other);
    private IEnumerable<Candidate> NegotiationResponses(ControllerObservation o)
    {
        foreach(var n in o.Negotiations.Where(n=>n.Status==NegotiationStatus.Proposed && n.ExpiresTick>o.Tick && n.Current.ProposedBy!=Actor))
        {
            var t=n.Current.Terms;bool buyer=t.Buyer==Actor,accept=false,canCounter=false;long price=t.UnitPriceMinor;
            if(CanTradeWith(o,buyer?t.Seller:t.Buyer))
            {
                if(t.Kind==TradeKind.Support && _state.Settings.PoliticsEnabled)
                {
                    accept=!buyer && AcceptableSupport(o,new(0,t.Buyer,t.Seller,t.Election,default,o.Support.OwnSection,t.Compensation,[],n.CreatedTick,n.ExpiresTick,SupportProposalStatus.Proposed));
                    if(buyer)accept=t.Compensation.Sum(m=>checked(m.Quantity*BasePrice(o,m.ItemId)))<=PoliticalBudgetRemaining(o,n.Id) && !HasObligations(o) && _state.Settings.OwnVictoryWeight>0 && t.Compensation.Sum(m=>checked(m.Quantity*BasePrice(o,m.ItemId)))<=checked(VoteWeight(o,t.Seller)*_state.Settings.SupportPricePerVoteMinor*2);
                }
                else if(t.Kind!=TradeKind.Support && _state.Settings.TradeEnabled)
                {
                    long target=buyer?BuyLimit(o,t.ItemId):Math.Max(1,checked((long)((Int128)BasePrice(o,t.ItemId)*_state.Settings.SellPriceBasisPoints/10000)));
                    bool funds=buyer?checked(t.Quantity*t.UnitPriceMinor)<=Math.Max(0,o.Account.AvailableMinor-_state.Settings.CashReserveMinor):
                        t.Kind==TradeKind.Sale?o.Inventories.Any(i=>i.Id==t.Source && Free(i,t.ItemId)>=t.Quantity):
                        DeliveryFineForPolicy(t.Quantity,t.UnitPriceMinor,o.DeliveryRules.FineBasisPoints)<=o.Account.AvailableMinor;
                    bool feasible=buyer?Need(o,t.ItemId)>0 && (t.Kind==TradeKind.Sale || o.Inventories.Any(i=>i.Id==t.Destination && FreeVolume(i)/Volume(o,t.ItemId)>=t.Quantity)):
                        t.Kind==TradeKind.Sale?funds:CanFulfillNegotiation(o,n);
                    accept=feasible && funds && (buyer?t.UnitPriceMinor<=target:t.UnitPriceMinor>=target);
                    canCounter=feasible && !accept && n.Current.Version<(o.NegotiationRules.MaxVersions) && checked(t.Quantity*target)<= (buyer?Math.Max(0,o.Account.AvailableMinor-_state.Settings.CashReserveMinor):long.MaxValue);
                    price=target;
                }
            }
            var action=accept?ActionKind.AcceptTrade:canCounter?ActionKind.CounterTrade:ActionKind.RejectTrade;
            yield return One(accept?"AcceptNegotiation":canCounter?"CounterNegotiation":"RejectNegotiation",n.Id+":"+n.Current.Version,890000,
                new(action,NegotiationId:n.Id,NegotiationVersion:n.Current.Version,Terms:canCounter?t with {UnitPriceMinor=price}:null));
        }
    }
    private static long DeliveryFineForPolicy(long quantity,long price,int bps)=>checked((long)((Int128)quantity*price*bps/10000));
    private bool CanFulfillNegotiation(ControllerObservation o,TradeNegotiation n)
    {
        var t=n.Current.Terms;if(n.Current.Position is not {} destination)return false;
        var plan=Fetch(o,t.ItemId,Math.Max(0,t.Quantity-Free(Carrier(o),t.ItemId)),out var at);
        if(plan is null)return false;Move(plan,at,destination);
        return PlanTicks(o,plan)+t.Quantity*Volume(o,t.ItemId)+2<=t.FulfillmentTicks;
    }
    private IEnumerable<Candidate> NegotiationCandidates(ControllerObservation o)
    {
        if(!_state.Settings.TradeEnabled || !_state.Settings.FutureContractsEnabled || !o.TradingParticipants.Contains(Actor) || HasObligations(o))yield break;
        foreach(var e in o.Enterprises)
        {
            var recipe=o.Map.Recipes.Single(r=>r.Id==e.RecipeId);var input=Inventory(o,e.InputId);
            foreach(var ingredient in recipe.Ingredients)
            {long price=BasePrice(o,ingredient.ItemId),q=Math.Min(Need(o,ingredient.ItemId),Math.Min(InputSpace(o,input,recipe,ingredient.ItemId),Math.Max(0,o.Account.AvailableMinor-_state.Settings.CashReserveMinor)/price));
            if(q<=0 || o.BuyOffers.Any(b=>b.Buyer==Actor && b.Status=="Open" && b.ItemId==ingredient.ItemId) ||
                o.Negotiations.Any(n=>n.Current.Terms.Buyer==Actor && n.Current.Terms.ItemId==ingredient.ItemId &&
                    (n.Status==NegotiationStatus.Proposed || o.Tick-(n.ClosedTick??n.CreatedTick)<_state.Settings.BlockedRetryTicks*2)))continue;
            var seller=o.City.Participants.Where(p=>p.Actor!=Actor && CanTradeWith(o,p.Actor) && p.PreferenceItem==ingredient.ItemId).OrderBy(p=>p.Actor.Value).FirstOrDefault();
            if(seller is null)continue;
            yield return One("NegotiateInputs",ingredient.ItemId,106000,new(ActionKind.ProposeTrade,Terms:new(TradeKind.Purchase,seller.Actor,Actor,
                ingredient.ItemId,q,price,Destination:input.Id,FulfillmentTicks:o.DeliveryRules.ContractTtlTicks)));
        }
        }
    }
}
