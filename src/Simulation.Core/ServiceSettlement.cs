namespace Simulation.Core;
public sealed partial class SimulationEngine
{
    private bool ValidServiceOverflow(long inventory,OverflowLot lot)
    {
        bool returning=lot.Source.StartsWith("service-return:",StringComparison.Ordinal);
        if(!returning&&!lot.Source.StartsWith("service:",StringComparison.Ordinal))return false;
        if(!long.TryParse(lot.Source[(returning?15:8)..],out long id)||!_serviceOrders.TryGetValue(id,out var o)||!_serviceRuns.TryGetValue(id,out var r))return false;
        return o.Quote.Terms.ItemId==lot.ItemId&&(returning?r.ReturnInventory.Value:o.Quote.Terms.Destination.Value)==inventory&&
            _events.Any(e=>e.Type=="OverflowCreated"&&e.EntityId==lot.Id&&e.ActorId?.Value==lot.Owner&&e.ItemId==lot.ItemId&&e.Tick==lot.CreatedTick&&e.Quantity>=lot.Quantity)&&
            (returning?_events.Where(e=>e.Type=="ServiceCargoReturned"&&e.EntityId==id&&e.Tick==lot.CreatedTick).Sum(e=>e.Quantity):r.Receipts.Where(p=>p.Tick==lot.CreatedTick).Sum(p=>p.Quantity))>=lot.Quantity&&
            lot.Warned.ToHashSet().SetEquals(GroundRules.WarningOffsets!.Where(n=>lot.ExpiresTick-Tick<=n));
    }
    private bool ServiceReturnReserved(ServiceOrder o,ServiceRun r)=>r.ReturnInventory==o.Quote.Terms.Source&&_economy.Inventories[r.ReturnInventory.Value].Owner==o.Customer.Value;
    private void LoadServiceUnits(ServiceOrder o,ServiceRun r,long quantity)
    {
        var t=o.Quote.Terms;var v=_vehicles[t.VehicleId];var tx=_economy.Begin(Tick);
        var purchase=t.PurchaseId==0?null:_marketAllocations.Single(a=>a.Id==t.PurchaseId);
        if(purchase is not null&&(purchase.ClosedTick is not null||Tick>purchase.DeadlineTick))throw new RuleViolation("PurchaseClosed");
        tx.ConsumeReserved(t.Source.Value,purchase?.StockHold??o.StockHold,quantity);tx.AddStock(v.CargoInventory.Value,t.ItemId,quantity);tx.ReserveAdditionalStock(v.CargoInventory.Value,r.CargoHold,t.ItemId,quantity);
        if(ServiceReturnReserved(o,r))tx.ReserveAdditionalCapacity(t.Source.Value,r.ReturnHold,t.ItemId,quantity);
        if(purchase is not null)tx.Pay(purchase.EscrowAccount.Value,_actors[purchase.Seller.Value].Account,checked(quantity*purchase.UnitPriceMinor),"PickupPayment");
        tx.Commit();
        if(purchase is not null)
        {
            long collected=checked(purchase.CollectedQuantity+quantity);var next=purchase with{CollectedQuantity=collected,PaidMinor=checked(purchase.PaidMinor+quantity*purchase.UnitPriceMinor),Status=collected==purchase.Quantity?"Fulfilled":"PartiallyCollected",ClosedTick=collected==purchase.Quantity?Tick:null};
            _marketAllocations[_marketAllocations.IndexOf(purchase)]=next;Emit("PurchaseCollected",o.Customer.Value,purchase.Id,t.ItemId,quantity,checked(quantity*purchase.UnitPriceMinor),next.Status);
        }
        if(t.ContractId!=0){int index=_deliveryContracts.FindIndex(d=>d.Id==t.ContractId);var d=_deliveryContracts[index];_deliveryContracts[index]=d with{PreparedQuantity=checked(d.PreparedQuantity-quantity),InTransitQuantity=checked(d.InTransitQuantity+quantity)};}
        _serviceRuns[o.Id]=r with{Loaded=checked(r.Loaded+quantity)};Emit("ServiceCargoLoaded",o.Carrier.Value,o.Id,t.ItemId,quantity);
    }
    private void DeliverServiceUnits(ServiceOrder o,ServiceRun r,long quantity)
    {
        var t=o.Quote.Terms;var v=_vehicles[t.VehicleId];long delivered=checked(r.Delivered+quantity);
        long gross=checked((long)((Int128)o.Quote.FeeMinor*delivered/t.Quantity)-r.GrossSettledMinor);
        long lateGross=checked(r.LateGrossMinor+(Tick>o.DeadlineTick?gross:0));long lateRefund=checked((long)((Int128)lateGross*ServiceExecution!.LateBasisPoints/10000));
        long refund=lateRefund-r.LateRefundMinor,paid=gross-refund;
        if(t.ContractId!=0)
            ReceiveDelivery(_deliveryContracts.FindIndex(d=>d.Id==t.ContractId),v.CargoInventory.Value,quantity,true,null,
                cargoHold:r.CargoHold,serviceReturnInventory:ServiceReturnReserved(o,r)?t.Source.Value:0,serviceReturnHold:ServiceReturnReserved(o,r)?r.ReturnHold:null);
        else
        {
            var tx=_economy.Begin(Tick);tx.ConsumeReserved(v.CargoInventory.Value,r.CargoHold,quantity);long excess=ReceiveForecast(tx,t.Destination.Value,t.ItemId,quantity,"service:"+o.Id);
            if(ServiceReturnReserved(o,r))tx.ReleaseCapacityVolume(t.Source.Value,r.ReturnHold,t.ItemId,quantity);tx.Commit();ForecastReceived(t.Destination.Value,t.ItemId,excess);
        }
        var pay=_economy.Begin(Tick);if(paid>0)pay.Pay(o.EscrowAccount,_actors[o.Carrier.Value].Account,paid,"ServicePayment:"+o.Id);
        if(refund>0)pay.Pay(o.EscrowAccount,_actors[o.Customer.Value].Account,refund,"ServiceLateRefund:"+o.Id);pay.Commit();
        _serviceRuns[o.Id]=r with{Delivered=delivered,GrossSettledMinor=checked(r.GrossSettledMinor+gross),PaidMinor=checked(r.PaidMinor+paid),RefundedMinor=checked(r.RefundedMinor+refund),LateGrossMinor=lateGross,LateRefundMinor=lateRefund,
            Receipts=[..r.Receipts,new ServiceReceipt(Tick,quantity,gross,paid,refund)]};
        _serviceOrders[o.Id]=o with{CustomerViewed=false,CarrierViewed=false};Emit("ServicePartDelivered",o.Carrier.Value,o.Id,t.ItemId,quantity,paid,Tick>o.DeadlineTick?"Late":"OnTime");
        if(refund>0)Emit("ServiceLatePartRefunded",o.Customer.Value,o.Id,money:refund);
    }
    private void ReturnServiceUnits(ServiceOrder o,ServiceRun r,long quantity)
    {
        var t=o.Quote.Terms;var tx=_economy.Begin(Tick);tx.ConsumeReserved(_vehicles[t.VehicleId].CargoInventory.Value,r.CargoHold,quantity);long excess=0;
        if(ServiceReturnReserved(o,r))tx.ReceiveReservedCapacity(r.ReturnInventory.Value,r.ReturnHold,t.ItemId,quantity);
        else excess=ReceiveForecast(tx,r.ReturnInventory.Value,t.ItemId,quantity,"service-return:"+o.Id);
        tx.Commit();if(excess>0)ForecastReceived(r.ReturnInventory.Value,t.ItemId,excess);
        if(t.ContractId!=0){int index=_deliveryContracts.FindIndex(d=>d.Id==t.ContractId);var d=_deliveryContracts[index];if(d.ClosedTick is null)_deliveryContracts[index]=d with{InTransitQuantity=checked(d.InTransitQuantity-quantity)};}
        _serviceRuns[o.Id]=r with{Returned=checked(r.Returned+quantity)};Emit("ServiceCargoReturned",o.Customer.Value,o.Id,t.ItemId,quantity);
    }
    private void ReleaseUnloadedServiceGoods(ServiceOrder o)
    {
        var r=_serviceRuns[o.Id];long remaining=SourceRemaining(o);if(remaining==0)return;var t=o.Quote.Terms;var tx=_economy.Begin(Tick);
        if(t.PurchaseId==0)tx.ReleaseStock(t.Source.Value,o.StockHold);tx.Commit();
        if(t.ContractId!=0){int index=_deliveryContracts.FindIndex(d=>d.Id==t.ContractId);var d=_deliveryContracts[index];if(d.ClosedTick is null)_deliveryContracts[index]=d with{PreparedQuantity=checked(d.PreparedQuantity-remaining)};}
        _serviceRuns[o.Id]=r with{Released=checked(r.Released+remaining)};
    }
    private void RequestServiceCancellation(ServiceOrder o,bool finishCurrent,string reason,string? command=null)
    {
        ReleaseUnloadedServiceGoods(o);var r=_serviceRuns[o.Id];
        if(r.Cargo==0){CloseExecutedService(o,"Cancelled",reason,command);return;}
        bool canDeliver=o.Quote.Terms.ContractId==0||_deliveryContracts.Single(d=>d.Id==o.Quote.Terms.ContractId).ClosedTick is null&&Tick<=_deliveryContracts.Single(d=>d.Id==o.Quote.Terms.ContractId).DeadlineTick;
        // A cancellation cannot turn receipt at the agreed destination into a free delivery.
        bool sameRecipient=r.ReturnInventory==o.Quote.Terms.Destination&&o.Quote.ReturnPosition==o.Quote.DestinationPosition;
        finishCurrent|=sameRecipient;
        _serviceRuns[o.Id]=r with{Cancellation=finishCurrent&&canDeliver?"FinishCurrent":"Return",Stage=finishCurrent&&canDeliver?"ToDestination":"Returning",WorkRemainder=0,StageStartedTick=Tick,Reason=reason};
        _serviceOrders[o.Id]=o with{CustomerViewed=false,CarrierViewed=false};Emit("ServiceCancellationRequested",o.Customer.Value,o.Id,reason:reason,command:command);
    }
    private void CloseExecutedService(ServiceOrder o,string state,string reason,string? command=null)
    {
        ReleaseUnloadedServiceGoods(o);var r=_serviceRuns[o.Id];if(r.Cargo!=0)throw new InvalidOperationException("Cannot close carried goods");
        long refund=_economy.Accounts[o.EscrowAccount].Balance;var tx=_economy.Begin(Tick);if(refund>0)tx.Pay(o.EscrowAccount,_actors[o.Customer.Value].Account,refund,"ServiceRefund:"+o.Id);tx.Commit();
        _serviceRuns[o.Id]=r with{Stage=state,Reason=reason,RefundedMinor=checked(r.RefundedMinor+refund),WorkRemainder=0};
        _serviceOrders[o.Id]=o with{State=state,ClosedTick=Tick,Reason=reason,CustomerViewed=false,CarrierViewed=false};Emit("ServiceClosed",o.Customer.Value,o.Id,money:refund,reason:reason,command:command);
    }
    private void AdvanceExecutedService(ServiceOrder o)
    {
        var r=_serviceRuns[o.Id];var t=o.Quote.Terms;
        if(r.Cancellation==""&&t.ContractId!=0&&_deliveryContracts.Single(d=>d.Id==t.ContractId).DeadlineTick<=Tick)
        {RequestServiceCancellation(o,false,"GoodsContractClosed");return;}
        if(r.Cancellation==""&&SourceRemaining(o)>0&&t.PurchaseId!=0&&_marketAllocations.Single(a=>a.Id==t.PurchaseId).DeadlineTick<=Tick)
        {RequestServiceCancellation(o,true,"CargoRightsExpired");return;}
        if(Tick==o.DeadlineTick+1){_serviceOrders[o.Id]=o with{CustomerViewed=false,CarrierViewed=false};Emit("ServiceOverdue",o.Customer.Value,o.Id);}
    }
    private long ServiceCargoAsset(long actor)
    {
        long value=0;foreach(var r in _serviceRuns.Values.Where(r=>r.Cargo>0)){var o=_serviceOrders[r.Order];long n=checked(r.Cargo*_items[o.Quote.Terms.ItemId].BasePriceMinor);if(o.Customer.Value==actor)value=checked(value+n);if(o.Carrier.Value==actor)value=checked(value-n);}return value;
    }
}
