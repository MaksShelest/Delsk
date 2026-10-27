namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    public bool HasDeliveryServices=>Services is not null;
    private bool IsImmediateServiceDecision(CommandEnvelope c)=>HasDeliveryServices&&ActionPolicy.IsServiceDecision(c.Action.Kind)&&
        c.IssuedAtTick>0&&c.IssuedAtTick==c.NotBeforeTick&&c.ExpiresAtTick==c.NotBeforeTick;
    private CommandResult ApplyServiceDecision(CommandEnvelope command)
    {
        int previousPhase=_phase;
        try
        {
            _phase=1;
            if(_results.Values.Count(r=>r.Tick==Tick&&_seenCommands[r.CommandId].ActorId==command.ActorId)>=_config.MaxCommandsPerActorTick)Reject(command,"CommandBudgetExceeded");
            else Execute(command);
            ValidateInvariants();return _results[command.CommandId];
        }
        finally{_phase=previousPhase;}
    }

    private bool IsServiceControlEvent(SimulationEvent e)=>HasDeliveryServices&&e.Tick==Tick&&e.Phase==1&&
        (e.Type is "ServiceAccepted" or "ServiceClosed" or "ServiceRequoted" or "ServiceViewed" or "CommandAccepted" or "CommandRejected")&&
        e.CommandId is {} id&&_seenCommands.TryGetValue(id,out var command)&&IsImmediateServiceDecision(command)&&command.IssuedAtTick==Tick&&_results.ContainsKey(id);
    private void ServiceCommand(ActorState actor,CommandEnvelope command)
    {
        if(Services is null)throw new RuleViolation("ServiceUnavailable");var action=command.Action;
        if(action.Kind==ActionKind.SetDeliveryService)
        {
            var vehicle=OwnVehicle(actor,action.VehicleId);
            if(action.Enabled){if(Model(vehicle.ModelId).MaterialsOnly||!ServiceFree(vehicle)||ServiceVehicleBusy(vehicle.Id))throw new RuleViolation("ServiceVehicleUnavailable");_serviceListings.Add(vehicle.Id);}
            else _serviceListings.Remove(vehicle.Id);
            Emit("ServiceListingChanged",actor.Id,vehicle.Id,quantity:action.Enabled?1:0,command:command.CommandId);return;
        }
        if(action.Kind==ActionKind.RequestDeliveryService){RequestService(actor,command);return;}
        if(!_serviceOrders.TryGetValue(action.ServiceId,out var order)||order.Customer.Value!=actor.Id&&order.Carrier.Value!=actor.Id)throw new RuleViolation("ServiceOrderUnavailable");
        if(action.Kind==ActionKind.ViewDeliveryService)
        {_serviceOrders[order.Id]=order with{CustomerViewed=order.Customer.Value==actor.Id||order.CustomerViewed,CarrierViewed=order.Carrier.Value==actor.Id||order.CarrierViewed};Emit("ServiceViewed",actor.Id,order.Id,command:command.CommandId);return;}
        if(!order.Active)throw new RuleViolation("ServiceOrderClosed");
        if(action.Kind==ActionKind.CancelDeliveryService){CloseService(order,"Cancelled","CancelledBeforePickup",command.CommandId);return;}
        if(action.Kind==ActionKind.RejectDeliveryService)
        {if(order.Carrier.Value!=actor.Id||order.State=="Accepted")throw new RuleViolation("ServiceOrderUnavailable");CloseService(order,"Rejected","CarrierRejected",command.CommandId);return;}
        if(action.Kind==ActionKind.AcceptDeliveryService)
        {
            if(order.Carrier.Value!=actor.Id||order.State!="Pending"||Tick>order.RespondByTick)throw new RuleViolation("ServiceOrderUnavailable");
            try
            {
                var quote=CalculateServiceQuote(order.Customer.Value,order.Quote.Terms,order.Id);
                if(quote.FeeMinor>order.Quote.FeeMinor||quote.EtaTicks>order.Quote.EtaTicks||quote.BufferTicks>order.Quote.BufferTicks)
                {_serviceOrders[order.Id]=order with{State="Requote",ProposedQuote=quote,RespondByTick=checked(Tick+Services.ResponseTicks),Reason="CustomerReconfirmation",CarrierViewed=true,CustomerViewed=false};Emit("ServiceRequoted",actor.Id,order.Id,money:quote.FeeMinor,command:command.CommandId);}
                else AcceptService(order,quote,command.CommandId);
            }
            catch(RuleViolation ex){CloseService(order,"Rejected",ex.ReasonCode,command.CommandId);}return;
        }
        if(action.Kind==ActionKind.ConfirmDeliveryService)
        {
            if(order.Customer.Value!=actor.Id||order.State!="Requote"||Tick>order.RespondByTick||order.ProposedQuote?.Token!=action.ServiceQuote)throw new RuleViolation("ServiceOrderUnavailable");
            try
            {
                var quote=CalculateServiceQuote(order.Customer.Value,order.Quote.Terms,order.Id);
                if(quote.FeeMinor>order.ProposedQuote!.FeeMinor||quote.EtaTicks>order.ProposedQuote.EtaTicks||quote.BufferTicks>order.ProposedQuote.BufferTicks)
                {_serviceOrders[order.Id]=order with{ProposedQuote=quote,RespondByTick=checked(Tick+Services.ResponseTicks),CustomerViewed=false};Emit("ServiceRequoted",order.Carrier.Value,order.Id,money:quote.FeeMinor,command:command.CommandId);}
                else AcceptService(order,quote,command.CommandId);
            }
            catch(RuleViolation ex){CloseService(order,"Rejected",ex.ReasonCode,command.CommandId);}return;
        }
        throw new RuleViolation("UnknownAction");
    }
    private void RequestService(ActorState actor,CommandEnvelope command)
    {
        var a=command.Action;var terms=new ServiceTerms(a.VehicleId,a.Source,a.Destination,a.ItemId,a.Quantity,a.PurchaseId,a.ContractId);
        var quote=CalculateServiceQuote(actor.Id,terms);
        if(quote.Token!=a.ServiceQuote)throw new RuleViolation("StaleServiceQuote");
        if(_serviceOrders.Values.Count(o=>o.Active&&o.Customer.Value==actor.Id)>=16)throw new RuleViolation("ServiceOrderLimit");
        long id=_nextId;var order=new ServiceOrder(id,new(actor.Id),_vehicles[a.VehicleId].Owner,quote,Tick,checked(Tick+Services!.ResponseTicks));
        var tx=_economy.Begin(Tick);tx.HoldMoney(actor.Account,order.MoneyHold,quote.FeeMinor);
        if(terms.PurchaseId==0)tx.ReserveStock(terms.Source.Value,order.StockHold,terms.ItemId,terms.Quantity);
        var delivery=terms.ContractId==0?null:_deliveryContracts.Single(d=>d.Id==terms.ContractId);
        long prepared=delivery is null?0:checked(delivery.PreparedQuantity+terms.Quantity);
        tx.Commit();_nextId=checked(id+1);_serviceOrders.Add(id,order);
        if(delivery is not null)_deliveryContracts[_deliveryContracts.IndexOf(delivery)]=delivery with{PreparedQuantity=prepared};
        Emit("ServiceRequested",actor.Id,id,terms.ItemId,terms.Quantity,quote.FeeMinor,command:command.CommandId);
    }
    private void AcceptService(ServiceOrder order,ServiceQuote quote,string? command=null)
    {
        long escrow=_nextId,account=_actors[order.Customer.Value].Account;var tx=_economy.Begin(Tick);
        if(order.Quote.FeeMinor!=quote.FeeMinor){tx.ReleaseMoney(account,order.MoneyHold);tx.HoldMoney(account,order.MoneyHold,quote.FeeMinor);}
        tx.CreateAccount(escrow,"DeliveryServiceEscrow");tx.Pay(account,escrow,quote.FeeMinor,"ServiceFunding:"+order.Id,order.MoneyHold);
        var next=order with{State="Accepted",Quote=quote,ProposedQuote=null,AcceptedTick=Tick,DeadlineTick=quote.EstimatedDeadline(Tick),EscrowAccount=escrow,Reason="AcceptedAwaitingPickup",CarrierViewed=true,CustomerViewed=false};
        tx.Commit();_nextId=checked(escrow+1);_serviceOrders[order.Id]=next;Emit("ServiceAccepted",order.Carrier.Value,order.Id,money:quote.FeeMinor,command:command);
    }
    private void CloseService(ServiceOrder order,string state,string reason,string? command=null)
    {
        var terms=order.Quote.Terms;var tx=_economy.Begin(Tick);long account=_actors[order.Customer.Value].Account;
        if(order.State=="Accepted")tx.Pay(order.EscrowAccount,account,order.Quote.FeeMinor,"ServiceRefund:"+order.Id);
        else tx.ReleaseMoney(account,order.MoneyHold);
        if(terms.PurchaseId==0)tx.ReleaseStock(terms.Source.Value,order.StockHold);
        var d=terms.ContractId==0?null:_deliveryContracts.Single(c=>c.Id==terms.ContractId);
        if(d is not null&&d.ClosedTick is null&&d.PreparedQuantity<terms.Quantity)throw new InvalidOperationException("Missing service delivery quota");
        tx.Commit();
        if(d is not null&&d.ClosedTick is null)_deliveryContracts[_deliveryContracts.IndexOf(d)]=d with{PreparedQuantity=d.PreparedQuantity-terms.Quantity};
        _serviceOrders[order.Id]=order with{State=state,ClosedTick=Tick,Reason=reason,CustomerViewed=false,CarrierViewed=false};
        Emit("ServiceClosed",order.Customer.Value,order.Id,money:order.Quote.FeeMinor,reason:reason,command:command);
    }
    private void AdvanceServices()
    {
        if(Services is null)return;
        foreach(var order in _serviceOrders.Values.Where(o=>o.Active).ToArray())
        {
            var terms=order.Quote.Terms;
            bool rightsExpired=terms.PurchaseId!=0&&_marketAllocations.Any(a=>a.Id==terms.PurchaseId&&(a.ClosedTick is not null||a.DeadlineTick<=Tick))||
                terms.ContractId!=0&&_deliveryContracts.Any(d=>d.Id==terms.ContractId&&(d.ClosedTick is not null||d.DeadlineTick<=Tick));
            if(rightsExpired)CloseService(order,"Cancelled","CargoRightsExpired");
            else if(order.State!="Accepted"&&Tick>=order.RespondByTick)CloseService(order,"Expired","ResponseExpired");
            else if(order.State=="Accepted"&&Tick>=order.DeadlineTick)CloseService(order,"Cancelled","UnstartedServiceExpired");
        }
    }
    private long ServiceEscrow(long actor)=>_serviceOrders.Values.Where(o=>o.EscrowAccount!=0&&o.Customer.Value==actor).Sum(o=>_economy.Accounts[o.EscrowAccount].Balance);
    private void RestoreServices(ServicesState? state)
    {
        _serviceListings.Clear();_serviceOrders.Clear();
        if(Services is null){if(state is not null)throw new CheckpointException("UnexpectedServices");return;}
        if(state is null)throw new CheckpointException("MissingServices");
        foreach(long id in state.ListedVehicles)if(!_serviceListings.Add(id))throw new CheckpointException("DuplicateServiceListing");
        foreach(var order in state.Orders)_serviceOrders.Add(order.Id,order);
    }
    private void AddExpectedServiceHolds(Dictionary<(long Inventory,string Hold),StockReservation> stock,Dictionary<(long Account,string Hold),long> money)
    {
        foreach(var order in _serviceOrders.Values.Where(o=>o.Active))
        {
            if(order.State!="Accepted")money.Add((_actors[order.Customer.Value].Account,order.MoneyHold),order.Quote.FeeMinor);
            if(order.Quote.Terms.PurchaseId==0)stock.Add((order.Quote.Terms.Source.Value,order.StockHold),new(order.Quote.Terms.ItemId,order.Quote.Terms.Quantity));
        }
    }
    private void ValidateServices()
    {
        if(Services is null)return;
        void Require(bool yes){if(!yes)throw new InvalidOperationException("Invalid delivery service state");}
        Require(_serviceListings.All(id=>_vehicles.TryGetValue(id,out var v)&&!Model(v.ModelId).MaterialsOnly));
        Require(_serviceOrders.Values.Where(o=>o.Active).Select(o=>o.Quote.Terms.VehicleId).Distinct().Count()==_serviceOrders.Values.Count(o=>o.Active));
        foreach(var o in _serviceOrders.Values)
        {
            Require(o.Id>=1000&&o.Id<_nextId&&_actors.ContainsKey(o.Customer.Value)&&_actors.ContainsKey(o.Carrier.Value)&&o.Customer!=o.Carrier&&
                _vehicles.TryGetValue(o.Quote.Terms.VehicleId,out var v)&&v.Owner==o.Carrier&&!Model(v.ModelId).MaterialsOnly&&
                o.CreatedTick>0&&o.CreatedTick<=Tick&&o.Quote.Terms.Quantity>0&&o.Quote.FeeMinor>0&&o.Quote.EtaTicks>0&&
                o.Quote.QuantityPerTrip>0&&o.Quote.QuantityPerTrip==Model(_vehicles[o.Quote.Terms.VehicleId].ModelId).CapacityVolume/_items[o.Quote.Terms.ItemId].UnitVolume&&o.Quote.Trips==(o.Quote.Terms.Quantity-1)/o.Quote.QuantityPerTrip+1&&o.Quote.Volume==checked(o.Quote.Terms.Quantity*_items[o.Quote.Terms.ItemId].UnitVolume)&&
                o.Quote.Token==ScenarioValidation.Hash(o.Quote with{Token=""}));
            Require(o.State is "Pending" or "Requote" or "Accepted" or "Rejected" or "Expired" or "Cancelled");
            if(o.Active)
            {
                Require(_vehicles[o.Quote.Terms.VehicleId].Driver==0&&VehicleLogistics(o.Quote.Terms.VehicleId) is null);
                if(o.Quote.Terms.ContractId!=0){var contract=_deliveryContracts.Single(d=>d.Id==o.Quote.Terms.ContractId);Require(contract.Supplier==o.Customer&&contract.ClosedTick is null&&contract.PreparedQuantity>=o.Quote.Terms.Quantity);}
                Require(o.ClosedTick==0&&(o.State=="Accepted"?o.DeadlineTick>Tick:o.RespondByTick>Tick));
                if(o.Quote.Terms.PurchaseId!=0){var claim=_marketAllocations.Single(a=>a.Id==o.Quote.Terms.PurchaseId);Require(claim.Buyer==o.Customer&&claim.ClosedTick is null&&PurchaseServiceQuantity(claim.Id)<=claim.RemainingQuantity);}
            }
            else Require(o.ClosedTick>=o.CreatedTick&&o.ClosedTick<=Tick);
            Require((o.State=="Requote")== (o.ProposedQuote is not null)||!o.Active);
            if(o.ProposedQuote is {} proposed)Require(proposed.Terms==o.Quote.Terms&&proposed.Token==ScenarioValidation.Hash(proposed with{Token=""})&&proposed.FeeMinor>0&&proposed.EtaTicks>0);
            if(o.EscrowAccount!=0)
            {
                var account=_economy.Accounts[o.EscrowAccount];Require(account.Kind=="DeliveryServiceEscrow"&&account.Reserved==0&&account.Balance==(o.State=="Accepted"?o.Quote.FeeMinor:0)&&o.AcceptedTick>=o.CreatedTick&&o.AcceptedTick<=Tick&&o.DeadlineTick==o.Quote.EstimatedDeadline(o.AcceptedTick));
                Require(_economy.Ledger.Count(l=>l.CreditAccount==o.EscrowAccount)==1&&_economy.Ledger.Where(l=>l.CreditAccount==o.EscrowAccount).All(l=>l.AmountMinor==o.Quote.FeeMinor&&l.DebitAccount==_actors[o.Customer.Value].Account&&l.Reason=="ServiceFunding:"+o.Id)&&
                    _economy.Ledger.Where(l=>l.DebitAccount==o.EscrowAccount).All(l=>l.CreditAccount==_actors[o.Customer.Value].Account&&l.Reason=="ServiceRefund:"+o.Id));
            }
            else Require(o.State!="Accepted"&&o.AcceptedTick==0);
        }
    }
}
