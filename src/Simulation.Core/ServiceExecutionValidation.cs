using System.Collections.Immutable;
namespace Simulation.Core;
public sealed partial class SimulationEngine
{
    private void RestoreServiceExecution(ServiceExecutionState? state)
    {
        _serviceRuns.Clear();_serviceCrews.Clear();_serviceDriverFunding.Clear();
        if(ServiceExecution is null){if(state is not null)throw new CheckpointException("UnexpectedServiceExecution");return;}
        if(state is null)throw new CheckpointException("MissingServiceExecution");
        foreach(var r in state.Runs)_serviceRuns.Add(r.Order,r);
        foreach(var c in state.Crews){var g=new EmploymentGroup(c.Group.Id.Value,c.Group.OwnerId.Value,0,1,c.Group.WageMinor,EmploymentRole.DeliveryService);LoadGroup(g,c.Group);_serviceCrews.Add(c.Vehicle,new(c.Vehicle,g){Enabled=c.Enabled});}
    }
    private void ValidateExecutedOrder(ServiceOrder o,ServiceRun r)
    {
        void Require(bool good){if(!good)throw new InvalidOperationException("Invalid service execution");}
        var t=o.Quote.Terms;var v=_vehicles[t.VehicleId];var inv=VehicleCargo(v);
        Require(r.Order==o.Id&&o.Quote.ReturnPosition is not null&&r.ReturnInventory==o.Quote.ReturnInventory&&_economy.Inventories[r.ReturnInventory.Value].Owner==o.Customer.Value&&
            r.Loaded>=0&&r.Loaded<=t.Quantity&&r.Cargo<=o.Quote.QuantityPerTrip&&r.TripQuantity>=0&&r.TripQuantity<=o.Quote.QuantityPerTrip&&r.TripNumber>=0&&r.LastWorkedTick>=0&&r.Delivered>=0&&r.Returned>=0&&r.Released>=0&&r.Cargo>=0&&SourceRemaining(o)>=0&&r.WorkRemainder>=0&&r.WorkRemainder<_items[t.ItemId].UnitVolume&&
            r.StageStartedTick>=o.AcceptedTick&&r.StageStartedTick<=Tick&&r.LastWorkedTick<=Tick&&!r.Receipts.IsDefault);
        Require(r.Stage is "AwaitingPickup" or "ToSource" or "Loading" or "ToDestination" or "AwaitingUnload" or "Unloading" or "Returning" or "UnloadingReturn" or "Completed" or "Cancelled");
        Require(o.AcceptedTick>=o.CreatedTick&&o.AcceptedTick<=Tick&&o.DeadlineTick==o.Quote.EstimatedDeadline(o.AcceptedTick));
        Require(r.GrossSettledMinor==(long)((Int128)o.Quote.FeeMinor*r.Delivered/t.Quantity)&&r.PaidMinor>=0&&r.RefundedMinor>=0&&r.PaidMinor+r.RefundedMinor<=o.Quote.FeeMinor);
        Require(o.Active?r.Stage is not ("Completed" or "Cancelled"):r.Stage==o.State);
        long delivered=0,gross=0,paid=0,late=0,lateGross=0,lastReceipt=0;
        foreach(var receipt in r.Receipts)
        {
            delivered=checked(delivered+receipt.Quantity);long expected=checked((long)((Int128)o.Quote.FeeMinor*delivered/t.Quantity)-gross);gross=checked(gross+expected);
            if(receipt.Tick>o.DeadlineTick)lateGross=checked(lateGross+expected);long refund=checked((long)((Int128)lateGross*ServiceExecution!.LateBasisPoints/10000)-late);
            Require(receipt.Quantity>0&&receipt.Tick>=o.AcceptedTick&&receipt.Tick>=lastReceipt&&receipt.Tick<=Tick&&receipt.GrossMinor==expected&&receipt.LateRefundMinor==refund&&receipt.PaidMinor==expected-refund);
            lastReceipt=receipt.Tick;paid=checked(paid+receipt.PaidMinor);late=checked(late+refund);
        }
        Require(delivered==r.Delivered&&gross==r.GrossSettledMinor&&paid==r.PaidMinor&&late==r.LateRefundMinor&&lateGross==r.LateGrossMinor);
        var account=_economy.Accounts[o.EscrowAccount];Require(account.Kind=="DeliveryServiceEscrow"&&account.Reserved==0&&account.Balance==o.Quote.FeeMinor-r.PaidMinor-r.RefundedMinor);
        Require(_economy.Ledger.Count(l=>l.CreditAccount==o.EscrowAccount)==1&&_economy.Ledger.Where(l=>l.CreditAccount==o.EscrowAccount).All(l=>l.Reason=="ServiceFunding:"+o.Id&&l.DebitAccount==_actors[o.Customer.Value].Account&&l.AmountMinor==o.Quote.FeeMinor));
        Require(_economy.Ledger.Where(l=>l.DebitAccount==o.EscrowAccount&&l.CreditAccount==_actors[o.Carrier.Value].Account&&l.Reason=="ServicePayment:"+o.Id).Sum(l=>l.AmountMinor)==r.PaidMinor);
        Require(_economy.Ledger.Where(l=>l.DebitAccount==o.EscrowAccount&&l.CreditAccount==_actors[o.Customer.Value].Account&&(l.Reason=="ServiceRefund:"+o.Id||l.Reason=="ServiceLateRefund:"+o.Id)).Sum(l=>l.AmountMinor)==r.RefundedMinor);
        Require(_economy.Ledger.Where(l=>l.DebitAccount==o.EscrowAccount).Sum(l=>l.AmountMinor)==r.PaidMinor+r.RefundedMinor);
        if(o.Active)
        {
            Require(o.State=="Accepted"&&o.ClosedTick==0&&VehicleLogistics(v.Id) is null&&(v.Driver==0||v.Driver==o.Carrier.Value));
            Require(inv.Stock.GetValueOrDefault(t.ItemId)==r.Cargo&&inv.Stock.Where(p=>p.Key!=t.ItemId).All(p=>p.Value==0));
            if(t.PurchaseId!=0){var p=_marketAllocations.Single(a=>a.Id==t.PurchaseId);Require(p.Buyer==o.Customer&&(SourceRemaining(o)==0||p.ClosedTick is null)&&PurchaseServiceQuantity(p.Id)<=p.RemainingQuantity);}
        }
        else Require(o.State is "Completed" or "Cancelled"&&r.Cargo==0&&SourceRemaining(o)==0&&account.Balance==0&&o.ClosedTick>=o.AcceptedTick&&o.ClosedTick<=Tick);
        if(o.State=="Completed")Require(r.Delivered==t.Quantity&&r.Returned==0&&r.Released==0);
    }
    private void ValidateServiceExecution()
    {
        if(ServiceExecution is null)return;
        if(_serviceDriverFunding.Count!=0||_serviceRuns.Values.Any(r=>!_serviceOrders.ContainsKey(r.Order))||_serviceOrders.Values.Any(o=>o.State=="Accepted"&&!_serviceRuns.ContainsKey(o.Id)))throw new InvalidOperationException("Invalid service execution references");
        foreach(var c in _serviceCrews.Values)if(!_vehicles.TryGetValue(c.Vehicle,out var v)||v.Owner.Value!=c.Group.Owner||Model(v.ModelId).MaterialsOnly||c.Group.Capacity!=1||c.Group.Role!=EmploymentRole.DeliveryService)throw new InvalidOperationException("Invalid service crew");
    }
}
