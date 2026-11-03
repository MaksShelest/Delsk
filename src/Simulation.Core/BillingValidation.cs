namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private void RestoreBilling(BillingState? state)
    {
        _roadAccruals.Clear();_roadInvoices.Clear();_roadAutoPay.Clear();_roadClearing=0;
        if(Billing is null){if(state is not null)throw new CheckpointException("UnexpectedBilling");return;}
        if(state is null)throw new CheckpointException("MissingBilling");
        _roadClearing=state.ClearingAccount;
        foreach(var a in state.Accruals)_roadAccruals.Add((a.Payer.Value,a.Recipient.Value),a.Units);
        foreach(var i in state.Invoices)_roadInvoices.Add(i.Id,i);
        foreach(var p in state.Preferences)_roadAutoPay.Add(p.Actor.Value,p.AutoPay);
    }
    private bool BillingRecipient(long id)=>id==0||_actors.ContainsKey(id);
    // The only new journal writes allowed between ticks are physical traffic witnesses.
    private bool IsBillingTravelEvent(SimulationEvent e)
    {
        if(UsesFreeVehicleControl&&e.Type=="TrafficLaneState")
        {
            if(e.Tick!=Tick||e.Phase!=9||e.ActorId is not {} laneOwner||FindBody(e.EntityId) is not {} laneBody||
                laneBody.Owner!=laneOwner||!LaneBody(laneBody)||e.Quantity is <0 or >4||e.AmountMinor!=0||e.CommandId is not null||
                !string.IsNullOrEmpty(e.ItemId)||e.ReasonCode is null||e.ReasonCode.Length>32)return false;
            var coordinates=e.ReasonCode.Split(',');
            return coordinates.Length==2&&int.TryParse(coordinates[0],out int lx)&&int.TryParse(coordinates[1],out int lz)&&
                _map.Walkable(new(lx,lz))&&(e.Quantity==0||RoadLevel(new(lx,lz))>0);
        }
        bool newUnbilledTraffic=UsesAcceleratedMovement&&Roads is not null&&e.Type=="TrafficCellEntered";
        if(Billing is null&&!newUnbilledTraffic||e.Tick!=Tick||e.Phase!=9||e.ActorId is not {} owner||
            FindBody(e.EntityId) is not {} body||body.Owner!=owner||e.Quantity<=0||e.AmountMinor!=0||e.CommandId is not null||
            e.Type is not ("BillingDistance" or "TrafficCellEntered"))return false;
        if(!UsesAcceleratedMovement||e.Type=="TrafficCellEntered")return e.Quantity<=80;
        if(e.ReasonCode is null||e.ReasonCode.Length>32)return false;
        var parts=e.ReasonCode.Split(',');
        if(parts.Length!=2||!int.TryParse(parts[0],out int x)||!int.TryParse(parts[1],out int z))return false;
        int level=RoadLevel(new(x,z));if(level is <1 or >3)return false;
        int speed=Math.Min(body.Speed,Roads!.Stages[level-1].Speed);
        return e.Quantity<=(Movement.Version==2?speed*6:(speed*5+1)/2);
    }
    private void ValidateBilling()
    {
        if(Billing is null)return;
        void Require(bool valid){if(!valid)throw new InvalidOperationException("Invalid billing state");}
        Require(_economy.Accounts.TryGetValue(_roadClearing,out var clearing)&&clearing.Kind=="RoadClearing"&&clearing.Balance==0&&clearing.Reserved==0);
        Require(_roadAutoPay.Keys.Order().SequenceEqual(_actors.Keys.Order()));
        foreach(var entry in _roadAccruals)Require(_actors.ContainsKey(entry.Key.Payer)&&BillingRecipient(entry.Key.Recipient));
        foreach(long payer in _actors.Keys)Require(_roadAccruals.Where(x=>x.Key.Payer==payer).Sum(x=>x.Value)>=0);
        foreach(var invoice in _roadInvoices.Values)
        {
            Require(invoice.Id>=1000&&invoice.Id<_nextId&&_actors.ContainsKey(invoice.Payer.Value)&&invoice.CreatedTick>0&&invoice.CreatedTick<=Tick&&
                invoice.CreatedTick%Billing.InvoicePeriod==0&&invoice.DueTick==checked(invoice.CreatedTick+Billing.GraceTicks)&&
                invoice.NextPenaltyTick>=invoice.DueTick+1&&(invoice.NextPenaltyTick-invoice.DueTick-1)%Billing.PenaltyPeriod==0&&
                (invoice.PrincipalRemaining==0||invoice.NextPenaltyTick>Tick)&&invoice.PenaltyRemainder is >=0 and <10000&&
                invoice.PenaltyMinor>=invoice.PenaltyPaidMinor&&invoice.PenaltyPaidMinor>=0&&!invoice.Claims.IsDefaultOrEmpty&&
                invoice.Claims.Select(c=>c.Recipient.Value).Distinct().Count()==invoice.Claims.Length&&
                invoice.Claims.All(c=>BillingRecipient(c.Recipient.Value)&&c.PrincipalMinor>0&&c.PaidMinor>=0&&c.PaidMinor<=c.PrincipalMinor));
            Require(_events.Count(e=>e.Type=="BillingInvoiceCreated"&&e.EntityId==invoice.Id&&e.ActorId==invoice.Payer&&e.Tick==invoice.CreatedTick&&e.AmountMinor==invoice.PrincipalMinor)==1);
            Require(_economy.Ledger.Where(l=>l.Reason=="RoadPrincipal:"+invoice.Id).Sum(l=>l.AmountMinor)==invoice.PrincipalMinor-invoice.PrincipalRemaining);
            Require(_economy.Ledger.Where(l=>l.Reason=="RoadPenalty:"+invoice.Id).Sum(l=>l.AmountMinor)==invoice.PenaltyPaidMinor);
            Require(_events.Where(e=>e.Type=="BillingPenaltyAccrued"&&e.EntityId==invoice.Id).Sum(e=>e.AmountMinor)==invoice.PenaltyMinor);
            foreach(var claim in invoice.Claims)
            {
                long account=claim.Recipient.Value==0?_treasury:_actors[claim.Recipient.Value].Account;
                Require(_economy.Ledger.Where(l=>l.Reason=="RoadRevenue:"+invoice.Id&&l.CreditAccount==account).Sum(l=>l.AmountMinor)==claim.PaidMinor);
            }
        }
    }
}
