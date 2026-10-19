using System.Collections.Immutable;

namespace Simulation.Core;

public enum TradeKind { Sale, Purchase, Supply, Support }
public enum NegotiationStatus { Proposed, Accepted, Rejected, Expired }
public sealed record NegotiationSettings(int TtlTicks=60,int MaxVersions=4,int MaxActivePerParticipant=16)
{
    public void Validate()
    {if(TtlTicks is <1 or >10000 || MaxVersions is <1 or >16 || MaxActivePerParticipant is <1 or >64)throw new ArgumentException("Invalid negotiation settings");}
}
// Sale means existing stock/pickup; Purchase and Supply are buyer/supplier initiated future deliveries.
public sealed record TradeTerms(TradeKind Kind,ParticipantId Seller,ParticipantId Buyer,string ItemId="",long Quantity=0,
    long UnitPriceMinor=0,InventoryId Source=default,InventoryId Destination=default,int FulfillmentTicks=120,
    ElectionId Election=default,[property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<MaterialCost> Compensation=default)
{
    public static bool Same(TradeTerms? a,TradeTerms? b) => a is null?b is null:b is not null &&
        a with {Compensation=default}==b with {Compensation=default} &&
        (a.Compensation.IsDefault?ImmutableArray<MaterialCost>.Empty:a.Compensation).OrderBy(m=>m.ItemId,StringComparer.Ordinal).SequenceEqual((b.Compensation.IsDefault?ImmutableArray<MaterialCost>.Empty:b.Compensation).OrderBy(m=>m.ItemId,StringComparer.Ordinal));
}
public sealed record TradeRevision(int Version,ParticipantId ProposedBy,TradeTerms Terms,long Tick,Cell? Position=null);
public sealed record TradeNegotiation(long Id,long CreatedTick,long ExpiresTick,NegotiationStatus Status,
    ImmutableArray<TradeRevision> Revisions,long? ClosedTick=null,long ContractId=0)
{
    public TradeRevision Current=>Revisions[^1];
}

public sealed partial class SimulationEngine
{
    private readonly Dictionary<long,TradeNegotiation> _negotiations=[];
    private NegotiationSettings NegotiationRules => _config.Economy?.Negotiations??new();
    private static bool IsNegotiationAction(ActionKind k) => k is ActionKind.ProposeTrade or ActionKind.CounterTrade or ActionKind.AcceptTrade or ActionKind.RejectTrade;
    public TradeNegotiation[] InspectNegotiations()=>_negotiations.Values.OrderBy(n=>n.Id).ToArray();
    private ImmutableArray<TradeNegotiation> ObserveNegotiations(ParticipantId actor) => [.._negotiations.Values
        .Where(n=>n.Current.Terms.Buyer==actor || n.Current.Terms.Seller==actor).OrderBy(n=>n.Id)];
    private void EmitNegotiation(TradeNegotiation n,CommandEnvelope? c=null) => _events.Add(new(++_eventSequence,Tick,_phase,
        "TradeNegotiationChanged",c?.ActorId,CommandId:c?.CommandId,Negotiation:n));
    private TradeNegotiation AvailableNegotiation(ActorState actor,GameAction a)
    {
        if(!_negotiations.TryGetValue(a.NegotiationId,out var n) || n.Current.Terms.Buyer.Value!=actor.Id && n.Current.Terms.Seller.Value!=actor.Id)
            throw new RuleViolation("NotAvailable");
        if(n.Status!=NegotiationStatus.Proposed || Tick>n.ExpiresTick)throw new RuleViolation("NegotiationClosed");
        if(n.Current.Version!=a.NegotiationVersion)throw new RuleViolation("StaleNegotiationVersion");
        if(n.Current.ProposedBy.Value==actor.Id)throw new RuleViolation("AwaitingCounterparty");
        return n;
    }
    private Cell? TradePoint(TradeTerms t) => t.Kind==TradeKind.Support?_config.Support?.TradingPoint:t.Kind==TradeKind.Sale?_economy.Inventories[t.Source.Value].Position:t.Destination.Value==0?null:_economy.Inventories[t.Destination.Value].Position;
    private TradeTerms ValidateTradeTerms(TradeTerms? terms,bool currentElection=true)
    {
        if(terms is not {} t || !Enum.IsDefined(t.Kind) || t.Seller==t.Buyer || !_actors.ContainsKey(t.Seller.Value) || !_actors.ContainsKey(t.Buyer.Value))
            throw new RuleViolation("InvalidTradeTerms");
        if(t.Compensation.IsDefault)t=t with {Compensation=[]};
        if(t.Kind==TradeKind.Support)
        {
            if(_config.Support is null || currentElection && (_electionMode!=ElectionMode.Competitive || t.Election!=_nextElection || Tick>_nextVotingTick) ||
                t.ItemId!="" || t.Quantity!=0 || t.UnitPriceMinor!=0 || t.Source.Value!=0 || t.Destination.Value!=0 ||
                t.Compensation.Length is <1 or >8 || t.Compensation.Any(m=>m is null || !_items.ContainsKey(m.ItemId) || m.Quantity<=0) ||
                t.Compensation.Select(m=>m.ItemId).Distinct().Count()!=t.Compensation.Length ||
                t.Compensation.Sum(m=>checked(m.Quantity*_items[m.ItemId].UnitVolume))>_config.Support.SectionCapacity)
                throw new RuleViolation("InvalidTradeTerms");
            return t with {Compensation=[..t.Compensation.OrderBy(m=>m.ItemId,StringComparer.Ordinal)]};
        }
        if(t.ItemId is null || !_items.ContainsKey(t.ItemId) || t.Quantity<=0 || t.UnitPriceMinor<=0 ||
            t.FulfillmentTicks is <1 or >10000 || t.Election.Value!=0 || t.Compensation.Length!=0 ||
            t.Kind==TradeKind.Sale && (!IsTradingSource(t.Seller.Value,t.Source.Value) || t.Destination.Value!=0) ||
            t.Kind!=TradeKind.Sale && (t.Source.Value!=0 || t.Destination.Value!=0 && !IsTradingDestination(t.Buyer.Value,t.Destination.Value)))
            throw new RuleViolation("InvalidTradeTerms");
        _=checked(t.Quantity*t.UnitPriceMinor);_=checked(t.Quantity*_items[t.ItemId].UnitVolume);_=checked(Tick+t.FulfillmentTicks);
        return t;
    }
    private void ProposeTrade(ActorState actor,CommandEnvelope c)
    {
        if(c.Action.Terms is not {} offered || offered.Buyer.Value!=actor.Id && offered.Seller.Value!=actor.Id ||
            offered.Kind==TradeKind.Sale && offered.Seller.Value!=actor.Id || offered.Kind==TradeKind.Purchase && offered.Buyer.Value!=actor.Id ||
            offered.Kind==TradeKind.Supply && offered.Seller.Value!=actor.Id)throw new RuleViolation("NotAvailable");
        var t=ValidateTradeTerms(c.Action.Terms);
        if(t.Kind==TradeKind.Support && Tick>=_nextVotingTick)throw new RuleViolation("InvalidTradeTerms");
        if(t.Buyer.Value!=actor.Id && t.Seller.Value!=actor.Id || t.Kind==TradeKind.Sale && t.Seller.Value!=actor.Id ||
            t.Kind==TradeKind.Purchase && (t.Buyer.Value!=actor.Id || t.Destination.Value==0) ||
            t.Kind==TradeKind.Supply && t.Seller.Value!=actor.Id)throw new RuleViolation("NotAvailable");
        RequireTradingAccess(t.Seller.Value,t.Buyer.Value);
        foreach(var party in new[]{t.Seller,t.Buyer})
            if(_negotiations.Values.Count(n=>n.Status==NegotiationStatus.Proposed && (n.Current.Terms.Buyer==party || n.Current.Terms.Seller==party))>=NegotiationRules.MaxActivePerParticipant)
                throw new RuleViolation("ProposalLimit");
        // Pending private talks do not advance another observer's object-ID namespace.
        long id=checked(1_000_000_000_000L+(actor.Id-1)*1_000_000_000L+_negotiations.Values.Count(n=>n.Revisions[0].ProposedBy.Value==actor.Id)+1),expiry=checked(Tick+NegotiationRules.TtlTicks);
        if(t.Kind==TradeKind.Support)expiry=Math.Min(expiry,_nextVotingTick!.Value);
        var n=new TradeNegotiation(id,Tick,expiry,NegotiationStatus.Proposed,[new(1,new(actor.Id),t,Tick,TradePoint(t))]);
        _negotiations.Add(id,n);EmitNegotiation(n,c);
    }
    private void CounterTrade(ActorState actor,CommandEnvelope c)
    {
        var n=AvailableNegotiation(actor,c.Action);var t=ValidateTradeTerms(c.Action.Terms);var old=n.Current.Terms;
        if(t.Kind!=old.Kind || t.Buyer!=old.Buyer || t.Seller!=old.Seller || t.Election!=old.Election ||
            t.Source!=old.Source || t.Destination!=old.Destination && t.Buyer.Value!=actor.Id)
            throw new RuleViolation("InvalidTradeTerms");
        RequireTradingAccess(t.Seller.Value,t.Buyer.Value);
        if(n.Current.Version>=NegotiationRules.MaxVersions)throw new RuleViolation("NegotiationRoundLimit");
        n=n with {Revisions=n.Revisions.Add(new(n.Current.Version+1,new(actor.Id),t,Tick,TradePoint(t)))};_negotiations[n.Id]=n;EmitNegotiation(n,c);
    }
    private void RejectTrade(ActorState actor,CommandEnvelope c)
    {var n=AvailableNegotiation(actor,c.Action) with {Status=NegotiationStatus.Rejected,ClosedTick=Tick};_negotiations[n.Id]=n;EmitNegotiation(n,c);}
    private void AcceptTrade(ActorState actor,CommandEnvelope c)
    {
        var n=AvailableNegotiation(actor,c.Action);var t=ValidateTradeTerms(n.Current.Terms);
        RequireTradingAccess(t.Seller.Value,t.Buyer.Value);
        long contract=t.Kind switch {TradeKind.Sale=>CreateNegotiatedPickup(n),TradeKind.Support=>CreateNegotiatedSupport(n),_=>CreateNegotiatedDelivery(n)};
        n=n with {Status=NegotiationStatus.Accepted,ClosedTick=Tick,ContractId=contract};_negotiations[n.Id]=n;EmitNegotiation(n,c);
    }
    private long CreateNegotiatedPickup(TradeNegotiation n)
    {
        var t=n.Current.Terms;long id=_nextId,escrow=checked(id+1),next=checked(id+2),value=checked(t.Quantity*t.UnitPriceMinor);
        string hold=$"market-pickup:{id}";var tx=_economy.Begin(Tick);
        tx.CreateAccount(escrow,"ContractEscrow");tx.ReserveStock(t.Source.Value,hold,t.ItemId,t.Quantity);
        tx.Pay(_actors[t.Buyer.Value].Account,escrow,value,"MarketEscrow");tx.Commit();_nextId=next;
        _marketAllocations.Add(new(id,0,t.Seller,t.Buyer,t.Source,t.ItemId,t.Quantity,t.UnitPriceMinor,new(escrow),hold,Tick,
            DeadlineTick:checked(Tick+t.FulfillmentTicks),NegotiationId:n.Id));return id;
    }
    private long CreateNegotiatedDelivery(TradeNegotiation n)
    {
        var t=n.Current.Terms;if(!IsTradingDestination(t.Buyer.Value,t.Destination.Value))throw new RuleViolation("NotAvailable");
        var buyer=_actors[t.Buyer.Value];long value=checked(t.Quantity*t.UnitPriceMinor),bond=DeliveryFine(value,Delivery.FineBasisPoints),next=_nextId;
        string budget=$"negotiation-budget:{n.Id}",bondHold=$"negotiation-bond:{n.Id}";var tx=_economy.Begin(Tick);
        tx.HoldMoney(buyer.Account,budget,value);if(bond>0)tx.HoldMoney(_actors[t.Seller.Value].Account,bondHold,bond);
        var d=StageDelivery(tx,ref next,new(0,0,t.Seller,t.Quantity,bond,bondHold,Tick),t.Quantity,t.ItemId,t.UnitPriceMinor,t.Buyer,
            t.Destination,_economy.Inventories[t.Destination.Value].Position,buyer.Account,budget) with {NegotiationId=n.Id,DeadlineTick=checked(Tick+t.FulfillmentTicks)};
        tx.Commit();_nextId=next;_deliveryContracts.Add(d);return d.Id;
    }
    private long CreateNegotiatedSupport(TradeNegotiation n)
    {
        var t=n.Current.Terms;
        if(_politicalCommitments.Values.Any(p=>p.Seller==t.Seller && p.Election==t.Election) ||
            _supportProposals.Values.Count(p=>p.Candidate==t.Buyer && p.Election==t.Election && p.Status is SupportProposalStatus.Proposed or SupportProposalStatus.AcceptedAwaitingResources)>=16)
            throw new RuleViolation("NotAvailable");
        long id=checked((t.Buyer.Value-1)*1_000_000_000L+_supportProposals.Values.Count(p=>p.Candidate==t.Buyer)+1);
        var p=new SupportProposal(id,t.Buyer,t.Seller,t.Election,new(_supportSections[t.Buyer.Value]),new(_supportSections[t.Seller.Value]),
            t.Compensation,[],Tick,_nextVotingTick!.Value,SupportProposalStatus.AcceptedAwaitingResources,AcceptedTick:Tick);
        _supportProposals.Add(id,p);EmitSupport("SupportProposed",p with {Status=SupportProposalStatus.Proposed,AcceptedTick=null});EmitSupport("SupportAccepted",p);return id;
    }
    private void ExpireNegotiations()
    {
        foreach(var old in _negotiations.Values.Where(n=>n.Status==NegotiationStatus.Proposed && Tick>=n.ExpiresTick).OrderBy(n=>n.Id).ToArray())
        {var n=old with {Status=NegotiationStatus.Expired,ClosedTick=Tick};_negotiations[n.Id]=n;EmitNegotiation(n);}
    }
}
