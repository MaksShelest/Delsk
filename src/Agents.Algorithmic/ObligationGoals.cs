using Simulation.Core;

namespace Agents.Algorithmic;

public sealed partial class AlgorithmicController
{
    private IEnumerable<Candidate> ContractCandidates(ControllerObservation o)
    {
        var carrier=Carrier(o);long volume=FreeVolume(carrier);
        foreach(var purchase in o.OwnMarketAllocations.Where(c=>c.Buyer==Actor && c.RemainingQuantity>0).OrderBy(c=>c.DeadlineTick))
        {
            var offer=o.MarketOffers.FirstOrDefault(m=>m.Id==purchase.OfferId);if(offer is null)continue;
            long q=Math.Min(purchase.RemainingQuantity,volume/Volume(o,purchase.ItemId));if(q<=0)continue;
            var steps=new List<PlanStep>();Move(steps,o.Participant.Position,offer.Position);
            steps.Add(new(new(ActionKind.CollectPurchase,PurchaseId:purchase.Id,Quantity:q)));
            if(PlanTicks(o,steps)<=purchase.DeadlineTick-o.Tick)
                yield return new("CollectPurchase",purchase.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),1050000-purchase.DeadlineTick+o.Tick,steps.ToArray());
        }
        foreach(var c in o.OwnDeliveryContracts.Where(c=>c.Supplier==Actor && c.ClosedTick is null && c.UnprovidedQuantity>0).OrderBy(c=>c.DeadlineTick))
        {
            bool automatic=o.OwnLogistics.Any(l=>l.Enabled && l.Queue.Any(j=>j.Enabled && j.ContractId==c.Id) && l.ReasonCode!="NoMoney" &&
                o.EmploymentGroups.Any(g=>g.Id==l.GroupId && g.Headcount>=FleetCrewMinimum(o,l)) &&
                Free(Inventory(o,l.SourceInventory),c.ItemId)>0 &&
                Distance(o.Map,Inventory(o,l.SourceInventory).Position,c.Position)>=0 &&
                (long)Distance(o.Map,Inventory(o,l.SourceInventory).Position,c.Position)*FreightCellEstimate(o)+2<=c.DeadlineTick-o.Tick);
            if(automatic)continue;
            long carried=Free(carrier,c.ItemId);
            long stock=o.Inventories.Where(i=>i.Id!=carrier.Id).Select(i=>Free(i,c.ItemId)).DefaultIfEmpty(0).Max();
            long obtainable=o.Map.ResourceNodes.Where(n=>AvailableResource(o,n)).Any(n=>n.ItemId==c.ItemId)?long.MaxValue:checked(carried+stock);
            long q=Math.Min(obtainable,Math.Min(c.UnprovidedQuantity,Math.Min(_state.Settings.BatchQuantity,checked(carried+volume/Volume(o,c.ItemId)))));
            if(q<=0)continue;
            var steps=new List<PlanStep>();Cell position=o.Participant.Position;
            if(carried<q){var fetch=Fetch(o,c.ItemId,q-carried,out position);if(fetch is null){q=Math.Min(q,carried);if(q<=0)continue;}else steps.AddRange(fetch);}
            Move(steps,position,c.Position);steps.Add(new(new(ActionKind.DeliverContract,ContractId:c.Id,Quantity:q)));
            if(PlanTicks(o,steps)>c.DeadlineTick-o.Tick && q>carried && carried>0)
            {q=Math.Min(carried,c.UnprovidedQuantity);steps.Clear();Move(steps,o.Participant.Position,c.Position);steps.Add(new(new(ActionKind.DeliverContract,ContractId:c.Id,Quantity:q)));}
            if(PlanTicks(o,steps)<=c.DeadlineTick-o.Tick)
                yield return new("DeliverContract",c.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),1000000-c.DeadlineTick+o.Tick,steps.ToArray());
        }
        // Accept one future quantity at a time. Existing holds, claims and requests are already excluded.
        if(_state.Settings.FutureContractsEnabled && o.OwnSupplyRequests.Length==0 && !o.OwnDeliveryContracts.Any(c=>c.Supplier==Actor && c.ClosedTick is null))
        {
            foreach(var system in o.OwnLogistics)
            {
                var e=o.Enterprises.Single(e=>e.Id==system.EnterpriseId);var recipe=o.Map.Recipes.Single(r=>r.Id==e.RecipeId);
                var source=Inventory(o,system.SourceInventory);var crew=o.EmploymentGroups.Single(g=>g.Id==system.GroupId);
                long cargo=FleetCargoCapacity(o,system)/Volume(o,recipe.OutputItem);
                foreach(var offer in o.CapitalOffers.Where(a=>a.ItemId==recipe.OutputItem && a.RemainingQuantity>0))
                {
                    long q=Math.Min(offer.RemainingQuantity,Math.Min(cargo,Free(source,offer.ItemId)));
                    if(q<Math.Min(_state.Settings.BatchQuantity,Math.Max(1,cargo/2)))continue;
                    int distance=Distance(o.Map,source.Position,offer.Position);if(distance<0)continue;
                    long waiting=Math.Max(0,(system.Trip?.EndTick??o.Tick)-o.Tick);
                    if(waiting+o.Map.TradeWindowTicks+3+(long)distance*FreightCellEstimate(o)>o.DeliveryRules.ContractTtlTicks)continue;
                    long payroll=checked(crew.Capacity*(crew.PendingWageMinor??crew.WageMinor)*Math.Max(2L,2L*distance*FreightCellEstimate(o)));
                    long bond=checked((long)((Int128)q*offer.UnitPriceMinor*o.DeliveryRules.FineBasisPoints/10000));
                    long unitCost=checked((RecipeInputValue(o,recipe)+recipe.EnergyPerBatchMinor+
                        recipe.RequiredStaff*_state.Settings.BaseWageMinor*recipe.FullStaffTicks)/recipe.OutputQuantity);
                    long net=checked(q*(offer.UnitPriceMinor-unitCost)-payroll);
                    if(net>0 && checked(payroll+bond)<=o.Account.AvailableMinor)
                        yield return One("AcceptCapitalDelivery",offer.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),200000+net,
                            new(ActionKind.AcceptDeliveryQuantity,OfferId:offer.Id,OfferWindow:offer.Window,Quantity:q));
                }
            }
            foreach(var offer in o.BuyOffers.Where(b=>b.Buyer!=Actor && CanTradeWith(o,b.Buyer) && b.Status=="Open" && b.RemainingQuantity>0 && b.CreatedTick<=o.Tick && b.ExpiresAtTick>o.Tick))
            {
                long available=o.Inventories.Sum(i=>Free(i,offer.ItemId));
                bool raw=o.Map.ResourceNodes.Where(n=>AvailableResource(o,n)).Any(n=>n.ItemId==offer.ItemId);
                long q=Math.Min(offer.RemainingQuantity,Math.Min(_state.Settings.BatchQuantity,Math.Min(checked(Free(carrier,offer.ItemId)+volume/Volume(o,offer.ItemId)),raw?long.MaxValue:available)));
                if(q<=0 || offer.UnitPriceMinor<BasePrice(o,offer.ItemId))continue;
                long bond=checked((long)((Int128)q*offer.UnitPriceMinor*o.DeliveryRules.FineBasisPoints/10000));if(bond>o.Account.AvailableMinor)continue;
                var plan=new List<PlanStep>();Cell end=o.Participant.Position;
                long needed=Math.Max(0,q-Free(carrier,offer.ItemId));
                if(needed>0){var fetch=Fetch(o,offer.ItemId,needed,out end);if(fetch is null)continue;plan.AddRange(fetch);}
                Move(plan,end,offer.Position);plan.Add(new(new(ActionKind.DeliverContract,Quantity:q)));
                if(PlanTicks(o,plan)+o.Map.TradeWindowTicks+2>o.DeliveryRules.ContractTtlTicks)continue;
                yield return One("AcceptDelivery",offer.ItemId+":"+offer.Id,180000+offer.UnitPriceMinor*q,
                    new(ActionKind.AcceptDeliveryQuantity,BuyOfferId:offer.Id,OfferWindow:offer.Window,Quantity:q));
            }
        }
        if(volume==0 && HasObligations(o))
            foreach(var stock in carrier.Stock.Where(s=>s.Quantity>s.Reserved && !o.OwnDeliveryContracts.Any(c=>c.Supplier==Actor && c.ClosedTick is null && c.ItemId==s.ItemId)))
            {
                var store=o.Inventories.Where(i=>i.Id!=carrier.Id && !o.OwnLogistics.Any(l=>l.CargoInventory==i.Id) && FreeVolume(i)>=Volume(o,stock.ItemId) && Reachable(o,i.Position))
                    .OrderBy(i=>Distance(o.Map,o.Participant.Position,i.Position)).ThenBy(i=>i.Id.Value).FirstOrDefault();if(store is null)continue;
                long q=Math.Min(stock.Quantity-stock.Reserved,FreeVolume(store)/Volume(o,stock.ItemId));
                var steps=new List<PlanStep>();Move(steps,o.Participant.Position,store.Position);steps.Add(new(new(ActionKind.Transfer,Source:carrier.Id,Destination:store.Id,ItemId:stock.ItemId,Quantity:q)));
                yield return new("ClearCarrier",stock.ItemId,1100000,steps.ToArray());
            }
    }
    private IEnumerable<Candidate> RemoteCandidates(ControllerObservation o)
    {
        foreach(var c in EconomicRemote(o))yield return c;
        foreach(var c in ServiceResponses(o))yield return c;
        foreach(var c in IndustryQuestCandidates(o))yield return c;
        foreach(var c in AuctionCandidates(o))yield return c;
        foreach(var p in SupportResponses(o))yield return p;
        foreach(var n in NegotiationResponses(o))yield return n;
        foreach(var c in LogisticsCandidates(o).Where(c=>c.Steps.Length==1 && c.Steps[0].Action.Kind is ActionKind.QueueLogisticsJob or ActionKind.SetLogisticsEnabled))yield return c;
    }
    private IEnumerable<Candidate> WageCandidates(ControllerObservation o)
    {
        if(o.Tick<_state.NextWageTick || o.Account.AvailableMinor<_state.Settings.CashReserveMinor)yield break;
        if(_state.Settings.Difficulty>=NpcDifficulty.Medium)
        {foreach(var candidate in EconomicWages(o))yield return candidate;yield break;}
        foreach(var group in o.EmploymentGroups.Where(g=>g.Ready && g.Headcount<g.Capacity))
        {
            long announced=group.PendingWageMinor??group.WageMinor;
            long competitor=o.LaborMarket.Where(g=>g.OwnerId!=Actor && g.Headcount>0).Select(g=>g.WageMinor).DefaultIfEmpty(_state.Settings.BaseWageMinor).Max();
            long wage=Math.Min(_state.Settings.MaximumWageMinor,Math.Max(_state.Settings.BaseWageMinor,checked(competitor+_state.Settings.WageRaiseMinor)));
            if(wage>announced && o.Account.AvailableMinor>=checked(wage*group.Capacity*_state.Settings.WageReviewTicks))
                yield return WageCandidate(o,group,wage,250000,group.Role.ToString());
        }
    }
    private IEnumerable<Candidate> AuctionCandidates(ControllerObservation o)
    {
        if(!_state.Settings.TradeEnabled)yield break;
        foreach(var bid in o.OwnMarketBids)
        {
            var offer=o.MarketOffers.FirstOrDefault(m=>m.Id==bid.OfferId && m.Status==MarketOfferStatus.Auction);
            if(offer is null || offer.CutoffTick<=o.Tick || _state.BidReviews.Any(r=>r.OfferId==offer.Id && r.Round==offer.Round))continue;
            long other=offer.Bids.Where(b=>b.Buyer!=Actor).Select(b=>b.CurrentPriceMinor).DefaultIfEmpty(offer.BasePriceMinor).Max();
            long target=Math.Min(bid.MaximumPriceMinor,Math.Max(checked(bid.CurrentPriceMinor+o.MarketRules.MinimumRaiseMinor),checked(other+o.MarketRules.MinimumRaiseMinor)));
            if(target>bid.CurrentPriceMinor && checked((target-bid.CurrentPriceMinor)*bid.Quantity)<=o.Account.AvailableMinor)
                yield return One("AuctionBid",offer.Id+":"+offer.Round,900000,new(ActionKind.RaiseBid,MarketOfferId:offer.Id,MarketRound:offer.Round,UnitPriceMinor:target));
        }
    }
    private IEnumerable<Candidate> ServiceResponses(ControllerObservation o)
    {
        if(o.Services is not {} services)yield break;
        if(_state.Settings.LogisticsEnabled&&o.Fleet is {} fleet)
            foreach(var car in fleet.Vehicles.Where(v=>v.State=="Parked"&&!v.Model.MaterialsOnly&&v.Vehicle.Driver==0&&v.Vehicle.Installation is null&&v.Logistics.Value==0&&
                Inventory(o,v.Vehicle.CargoInventory).OccupiedVolume==0&&!services.OwnListings.Contains(v.Vehicle.Id)))
                yield return One("OfferDeliveryService",car.Vehicle.Id.ToString(),400000,new(ActionKind.SetDeliveryService,VehicleId:car.Vehicle.Id,Enabled:true));
        foreach(var order in services.OwnOrders.Where(x=>x.Carrier==Actor&&x.State=="Pending"&&x.RespondByTick>o.Tick))
        {
            var q=order.Quote;
            long cost=checked(services.Rules.DriverWageMinor*(q.EtaTicks+q.ExtraStaffTicks)+(q.RoadUnits+SimulationEngine.RoadAccrualScale-1)/SimulationEngine.RoadAccrualScale);
            bool profitable=q.FeeMinor>cost;
            yield return One("ServiceResponse",order.Id.ToString(),1900000,new(profitable?ActionKind.AcceptDeliveryService:ActionKind.RejectDeliveryService,ServiceId:order.Id));
        }
    }

}
