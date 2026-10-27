using System.Collections.Immutable;

namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    public const long RoadAccrualScale=800L*10000*10000;
    private BillingRules? Billing=>_config.Economy?.Billing;
    private long _roadClearing;
    private readonly Dictionary<(long Payer,long Recipient),long> _roadAccruals=[];
    private readonly SortedDictionary<long,RoadInvoice> _roadInvoices=[];
    private readonly Dictionary<long,bool> _roadAutoPay=[];
    private void InitializeBilling()
    {
        if(Billing is null)return;
        _roadClearing=CreateAccount("RoadClearing");
        foreach(long actor in _actors.Keys)_roadAutoPay.Add(actor,true);
    }
    // Called only after a collision-free forward quantum is admitted. The discarded
    // E2 sub-quantum at the cell centre is not distance and must not be charged.
    private void AccrueRoadTravel(RoadBody vehicle,Cell cell,int distance)
    {
        if(Billing is null||!_roads.TryGetValue(cell,out var road))return;
        int factor=_vehicles.TryGetValue(vehicle.Id,out var owned)?Billing.ClassFactors[owned.ModelId]:10000;
        long units=checked(distance*Billing.LightIncrementMinor*factor);
        foreach(var contribution in road.Contributions)
        {
            void Add(long recipient,long value)
            {
                if(value==0)return;
                var key=(vehicle.Owner.Value,recipient);
                _roadAccruals[key]=checked(_roadAccruals.GetValueOrDefault(key)+value);
            }
            if(contribution.Builder.Value==0)Add(0,checked(units*10000));
            else {Add(0,checked(units*Billing.CityShareBasisPoints));Add(contribution.Builder.Value,checked(units*(10000-Billing.CityShareBasisPoints)));}
        }
        Emit("BillingDistance",vehicle.Owner.Value,vehicle.Id,quantity:distance,reason:cell.X+","+cell.Z);
    }
    public BillingState? InspectBilling()=>Billing is null?null:new(_roadClearing,
        [.._roadAccruals.OrderBy(x=>x.Key.Payer).ThenBy(x=>x.Key.Recipient).Select(x=>new RoadAccrual(new(x.Key.Payer),new(x.Key.Recipient),x.Value))],
        [.._roadInvoices.Values],[.._roadAutoPay.OrderBy(x=>x.Key).Select(x=>new BillingPreference(new(x.Key),x.Value))]);
    // Signed recipient remainders preserve the exact 10/90 split across invoices,
    // while every nonzero whole cent of the TOTAL accrual is billed on time.
    private KeyValuePair<long,long>[] AccruedClaims(long actor)
    {
        var units=_roadAccruals.Where(x=>x.Key.Payer==actor).OrderBy(x=>x.Key.Recipient).ToDictionary(x=>x.Key.Recipient,x=>x.Value);
        long total=units.Values.Sum()/RoadAccrualScale;if(total==0)return [];
        var allocation=units.ToDictionary(x=>x.Key,x=>Math.Max(0,x.Value/RoadAccrualScale));long allocated=allocation.Values.Sum();
        while(allocated>total)
        {
            long recipient=units.Keys.Where(r=>allocation[r]>0).OrderBy(r=>(Int128)units[r]-(Int128)allocation[r]*RoadAccrualScale).ThenBy(r=>r).First();
            allocation[recipient]--;allocated--;
        }
        while(allocated<total)
        {
            long recipient=units.Keys.OrderByDescending(r=>(Int128)units[r]-(Int128)allocation[r]*RoadAccrualScale).ThenBy(r=>r).First();
            allocation[recipient]++;allocated++;
        }
        return [..allocation.Where(x=>x.Value>0).OrderBy(x=>x.Key)];
    }
    private long BillingAccrued(long actor)=>_roadAccruals.Where(x=>x.Key.Payer==actor).Sum(x=>x.Value)/RoadAccrualScale;
    private long BillingDebt(long actor)=>checked(_roadInvoices.Values.Where(i=>i.Payer.Value==actor).Sum(i=>i.Remaining)+BillingAccrued(actor));
    private long BillingReceivable(long actor)=>checked(_roadInvoices.Values.Sum(i=>i.Claims.Where(c=>c.Recipient.Value==actor).Sum(c=>c.PrincipalMinor-c.PaidMinor)+(actor==0?i.PenaltyRemaining:0))+
        _actors.Keys.Sum(p=>AccruedClaims(p).Where(c=>c.Key==actor).Sum(c=>c.Value)));
    public PersonalAccount AccountOf(ParticipantId participant,int offset=0,int count=50,int invoiceOffset=0)
    {
        if(!_actors.TryGetValue(participant.Value,out var actor)||offset<0||invoiceOffset<0||count is <1 or >100)throw new ArgumentException("Invalid personal account query");
        var account=_economy.Accounts[actor.Account];
        var operations=_economy.Ledger.Where(l=>l.DebitAccount==actor.Account||l.CreditAccount==actor.Account).Select((l,index)=>(l,index))
            .Reverse().Skip(offset).Take(count).Select(x=>AccountOperationOf(actor,x.l,x.index)).ToImmutableArray();
        return new(new(new(account.Id),account.Kind,account.Balance,account.Reserved),_roadAutoPay.GetValueOrDefault(actor.Id,true),
            [.._roadInvoices.Values.Where(i=>i.Payer==participant).Reverse().Skip(invoiceOffset).Take(count)],operations,
            BillingAccrued(actor.Id),BillingReceivable(actor.Id),Billing,
            _roadInvoices.Values.Where(i=>i.Payer==participant).Sum(i=>i.Remaining),_roadInvoices.Values.Count(i=>i.Payer==participant&&!i.Viewed),
            _roadInvoices.Values.Count(i=>i.Payer==participant),_economy.Ledger.Count(l=>l.DebitAccount==actor.Account||l.CreditAccount==actor.Account),
            _economy.Ledger.Where(l=>l.CreditAccount==actor.Account).Sum(l=>l.AmountMinor),_economy.Ledger.Where(l=>l.DebitAccount==actor.Account).Sum(l=>l.AmountMinor),
            checked(_marketAllocations.Where(a=>a.Buyer==participant).Sum(a=>_economy.Accounts[a.EscrowAccount.Value].Balance)+
            checked(ServiceEscrow(actor.Id)+_deliveryContracts.Sum(d=>(d.Buyer==participant?_economy.Accounts[d.EscrowAccount.Value].Balance:0)+(d.Supplier==participant?_economy.Accounts[d.BondAccount.Value].Balance:0)))),Tick);
    }
    private AccountOperation AccountOperationOf(ActorState owner,LedgerEntry entry,long ordinal)
    {
        bool incoming=entry.CreditAccount==owner.Account;long other=incoming?entry.DebitAccount:entry.CreditAccount;
        long? counterparty=_actors.Values.FirstOrDefault(a=>a.Account==other)?.Id;
        string kind=entry.Reason,otherKind=other==0?"External":_economy.Accounts[other].Kind;
        if(kind.StartsWith("RoadRevenue:",StringComparison.Ordinal)&&long.TryParse(kind.AsSpan(12),out var bill)&&_roadInvoices.TryGetValue(bill,out var invoice))
        {counterparty=invoice.Payer.Value;otherKind="Participant";}
        var allocation=_marketAllocations.FirstOrDefault(a=>a.EscrowAccount.Value==other&&(a.Buyer.Value==owner.Id||a.Seller.Value==owner.Id));
        if(allocation is not null){counterparty=allocation.Buyer.Value==owner.Id?allocation.Seller.Value:allocation.Buyer.Value;otherKind="Participant";}
        var delivery=_deliveryContracts.FirstOrDefault(c=>(c.EscrowAccount.Value==other||c.BondAccount.Value==other)&&(c.Buyer==new ParticipantId(owner.Id)||c.Supplier.Value==owner.Id));
        if(delivery is not null){counterparty=delivery.Buyer==new ParticipantId(owner.Id)?delivery.Supplier.Value:delivery.Buyer?.Value??0;otherKind=counterparty==0?"Capital":"Participant";}
        // No foreign account IDs, holds or unrelated ledger entries leave this projection.
        var service=_serviceOrders.Values.FirstOrDefault(o=>o.EscrowAccount==other);
        if(service is not null){counterparty=service.Customer.Value==owner.Id?service.Carrier.Value:service.Customer.Value;otherKind="Participant";}
        return new(ordinal,entry.Tick,incoming?entry.AmountMinor:-entry.AmountMinor,kind,
            counterparty is {} id?new ParticipantId(id):null,otherKind);
    }
    private void BillingCommand(ActorState actor,CommandEnvelope command)
    {
        if(Billing is null)throw new RuleViolation("BillingUnavailable");
        if(command.Action.Kind==ActionKind.SetAutoPay)
        {_roadAutoPay[actor.Id]=command.Action.Enabled;Emit("BillingAutoPayChanged",actor.Id,quantity:command.Action.Enabled?1:0,command:command.CommandId);return;}
        if(!_roadInvoices.TryGetValue(command.Action.OfferId,out var invoice)||invoice.Payer.Value!=actor.Id)throw new RuleViolation("InvoiceUnavailable");
        if(command.Action.Kind==ActionKind.ViewInvoice)
        {
            _roadInvoices[invoice.Id]=invoice with{Viewed=true};
            Emit("BillingInvoiceViewed",actor.Id,invoice.Id,command:command.CommandId);return;
        }
        if(invoice.Remaining==0)throw new RuleViolation("InvoicePaid");
        long available=_economy.Accounts[actor.Account].Available;
        long amount=Math.Min(invoice.Remaining,command.Action.Quantity==0?available:Math.Min(available,command.Action.Quantity));
        if(amount<=0)throw new RuleViolation("NoMoney");
        PayRoadInvoice(invoice,amount);
    }
    private void PayRoadInvoice(RoadInvoice invoice,long amount)
    {
        long principal=Math.Min(amount,invoice.PrincipalRemaining),penalty=amount-principal;
        var claims=invoice.Claims.ToArray();
        long[] paid=new long[claims.Length];long allocated=0,total=invoice.PrincipalRemaining;
        if(principal>0)
        {
            for(int n=0;n<claims.Length;n++){paid[n]=checked((long)((Int128)principal*(claims[n].PrincipalMinor-claims[n].PaidMinor)/total));allocated+=paid[n];}
            // Largest residual, then a fixed invoice-specific rotation. Each claim can
            // receive at most its outstanding principal; no penny is lost or paid twice.
            foreach(int n in Enumerable.Range(0,claims.Length).OrderByDescending(n=>(Int128)principal*(claims[n].PrincipalMinor-claims[n].PaidMinor)%total)
                .ThenBy(n=>(n+invoice.Id)%claims.Length).Take(checked((int)(principal-allocated))))paid[n]++;
        }
        var tx=_economy.Begin(Tick);long payer=_actors[invoice.Payer.Value].Account;
        if(principal>0)tx.Pay(payer,_roadClearing,principal,"RoadPrincipal:"+invoice.Id);
        for(int n=0;n<claims.Length;n++)
        {
            if(paid[n]==0)continue;
            tx.Pay(_roadClearing,claims[n].Recipient.Value==0?_treasury:_actors[claims[n].Recipient.Value].Account,paid[n],"RoadRevenue:"+invoice.Id);
            claims[n]=claims[n] with{PaidMinor=checked(claims[n].PaidMinor+paid[n])};
        }
        if(penalty>0)tx.Pay(payer,_treasury,penalty,"RoadPenalty:"+invoice.Id);
        var updated=invoice with{Claims=[..claims],PenaltyPaidMinor=checked(invoice.PenaltyPaidMinor+penalty)};
        tx.Commit();_roadInvoices[invoice.Id]=updated;
        Emit("BillingInvoicePaid",invoice.Payer.Value,invoice.Id,quantity:principal,money:amount);
    }
    private void AdvanceBilling()
    {
        if(Billing is null)return;
        if(Tick%Billing.InvoicePeriod==0)
        {
            foreach(long actor in _actors.Keys.Order())
            {
                var entries=AccruedClaims(actor);
                if(entries.Length==0)continue;
                long id=_nextId++;var invoice=new RoadInvoice(id,new(actor),Tick,checked(Tick+Billing.GraceTicks),checked(Tick+Billing.GraceTicks+1),
                    [..entries.Select(x=>new InvoiceClaim(new(x.Key),x.Value))]);
                foreach(var entry in entries)_roadAccruals[(actor,entry.Key)]=checked(_roadAccruals[(actor,entry.Key)]-entry.Value*RoadAccrualScale);
                _roadInvoices.Add(id,invoice);Emit("BillingInvoiceCreated",actor,id,money:invoice.PrincipalMinor);
            }
        }
        // One frozen budget per participant removes same-phase ordering advantage.
        // New receipts from other bill payments are available at the next active tick.
        var budgets=_actors.Values.ToDictionary(a=>a.Id,a=>_economy.Accounts[a.Account].Available);
        foreach(long actor in _actors.Keys.Order())
        {
            if(!_roadAutoPay[actor])continue;
            long budget=budgets[actor];
            foreach(var invoice in _roadInvoices.Values.Where(i=>i.Payer.Value==actor&&i.Remaining>0).OrderBy(i=>i.CreatedTick).ThenBy(i=>i.Id).ToArray())
            {
                long amount=Math.Min(budget,invoice.Remaining);if(amount==0)break;
                PayRoadInvoice(invoice,amount);budget-=amount;
            }
        }
        foreach(var invoice in _roadInvoices.Values.Where(i=>i.PrincipalRemaining>0&&Tick>=i.NextPenaltyTick).ToArray())
        {
            Int128 numerator=(Int128)invoice.PrincipalRemaining*Billing.PenaltyBasisPoints+invoice.PenaltyRemainder;
            long penalty=checked((long)(numerator/10000));
            _roadInvoices[invoice.Id]=invoice with{PenaltyMinor=checked(invoice.PenaltyMinor+penalty),PenaltyRemainder=(long)(numerator%10000),NextPenaltyTick=checked(invoice.NextPenaltyTick+Billing.PenaltyPeriod)};
            Emit("BillingPenaltyAccrued",invoice.Payer.Value,invoice.Id,money:penalty);
        }
    }
}
