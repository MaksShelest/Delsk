using Simulation.Core;
namespace Agents.Algorithmic;
public sealed partial class AlgorithmicController
{
    private void RememberMarketIntent(string goal,IEnumerable<GameAction> actions)
    {
        if(!UsesEconomicPlanning||goal!="StageOutputAtExchange")return;
        string item=actions.First(a=>a.ItemId.Length>0).ItemId;_state=_state with{EconomyMemory=_state.EconomyMemory! with{MarketItem=item,MarketWaitUntil=0}};
    }
    private bool WaitForPublicDemand(ControllerObservation o)
    {
        if(!UsesEconomicPlanning||_state.EconomyMemory is not {MarketItem:{} item} memory)return false;
        if(_state.Goal=="StageOutputAtExchange"&&_state.NextStep<_state.Plan.Length)return false;
        if(Free(Carrier(o),item)==0){_state=_state with{EconomyMemory=memory with{MarketItem=null,MarketWaitUntil=0}};return false;}
        if(o.ProductionContent is not {} market||o.Participant.Position!=market.Terminal||HasObligations(o))return false;
        if(memory.MarketWaitUntil==0){memory=memory with{MarketWaitUntil=checked(o.Tick+300)};_state=_state with{EconomyMemory=memory};}
        if(o.Tick>=memory.MarketWaitUntil){_state=_state with{EconomyMemory=memory with{MarketItem=null,MarketWaitUntil=0}};return false;}
        return BestOffer(o,item,Free(Carrier(o),item),o.Participant.Position) is null;
    }
    private IEnumerable<Candidate> EconomicRemote(ControllerObservation o)
    {
        if(!UsesEconomicPlanning)yield break;
        if(o.PersonalAccount is {AutoPay:false})yield return One("ManageAccount","FreeMoneyAutopay",1800000,new(ActionKind.SetAutoPay,Enabled:true));
        foreach(var line in o.Enterprises.SelectMany(ProductionWorkplaces))
            if(line.Unit.Enabled&&line.Unit.BatchId==0&&!ProductionWorthRunning(o,line.Unit,line.Group))
                yield return One("PreserveCash","PauseUnsoldOrLossmaking:"+line.Workshop,1600000,new(ActionKind.SetProductionEnabled,Enterprise:line.Unit.Id,WorkshopId:line.Workshop,Enabled:false));
        foreach(var service in o.Services?.OwnOrders??[])
        {
            if(service.Customer!=Actor)continue;
            if(service.State=="Requote"&&service.ProposedQuote is {} q)
                yield return One("ManageDelivery",service.Id.ToString(),1700000,new(q.FeeMinor-service.Quote.FeeMinor<=o.Account.AvailableMinor&&q.FeeMinor<=service.Quote.FeeMinor*11/10?ActionKind.ConfirmDeliveryService:ActionKind.CancelDeliveryService,ServiceId:service.Id,ServiceQuote:q.Token));
            if(service.State=="Accepted"&&o.Tick>service.DeadlineTick+(o.Services!.Execution?.Rules.ForceCloseAfterTicks??1200))
                yield return One("ManageDelivery","Overdue:"+service.Id,1700000,new(ActionKind.ForceCloseDeliveryService,ServiceId:service.Id));
        }
    }
    private IEnumerable<Candidate> EconomicCandidates(ControllerObservation o)
    {
        if(!UsesEconomicPlanning)yield break;var carrier=Carrier(o);var options=o.EconomicOpportunities;
        if(o.Enterprises.IsEmpty&&carrier.Stock.All(p=>p.Quantity==0)&&o.Account.AvailableMinor<StartupReserve(o,StartupIndustry(o)))
        foreach(var node in o.Map.ResourceNodes.Where(n=>AvailableResource(o,n)).GroupBy(n=>n.ItemId).SelectMany(g=>g.OrderBy(n=>Math.Abs(n.Position.X-o.Participant.Position.X)+Math.Abs(n.Position.Z-o.Participant.Position.Z)).Take(2)))
        {
            var site=o.Resources.Single(r=>r.Position==node.Position&&r.ItemId==node.ItemId);long q=Math.Min(site.Stock-site.Reserved,Math.Min(_state.Settings.BatchQuantity,FreeVolume(carrier)/Volume(o,node.ItemId)));
            var offer=BestOffer(o,node.ItemId,q,node.Position);if(q<=0||offer is null)continue;q=Math.Min(q,offer.RemainingQuantity);
            int approach=Distance(o.Map,o.Participant.Position,node.Position),loaded=Distance(o.Map,node.Position,offer.Position);if(approach<0||loaded<0)continue;
            long ticks=checked((long)(approach+loaded)*o.Map.MoveTicksPerCell+q*o.Map.HarvestTicksPerUnit+o.Map.TradeWindowTicks+2);
            var steps=new List<PlanStep>();Move(steps,o.Participant.Position,node.Position);steps.Add(new(new(ActionKind.Harvest,ItemId:node.ItemId,Quantity:q,Target:node.Position)));Move(steps,node.Position,offer.Position);steps.Add(new(new(ActionKind.SellAvailableToCapital,OfferId:offer.Id,ItemId:node.ItemId,Quantity:q)));
            yield return new("EarnConstructionReserve","FullTrip:"+node.ItemId,200000+q*offer.UnitPriceMinor*1000/Math.Max(1,ticks),steps.ToArray());
        }
        foreach(var pile in options?.Overflow??[])
        {
            long q=Math.Min(pile.Lot.Quantity,FreeVolume(carrier)/Volume(o,pile.Lot.ItemId));if(q<=0||!Reachable(o,pile.Position))continue;
            var offer=BestOffer(o,pile.Lot.ItemId,q,pile.Position);
            var store=o.Inventories.Where(i=>i.Id!=pile.Inventory&&i.Id!=carrier.Id&&FreeVolume(i)>=q*Volume(o,pile.Lot.ItemId)&&o.Buildings.Any(b=>b.Kind is BuildingKind.Warehouse or BuildingKind.Shop&&b.InputId==i.Id)).OrderBy(i=>Distance(o.Map,pile.Position,i.Position)).FirstOrDefault();
            if(offer is null&&store is null)continue;
            var steps=new List<PlanStep>();Move(steps,o.Participant.Position,pile.Position);steps.Add(new(new(ActionKind.TakeOverflow,Source:pile.Inventory,OfferId:pile.Lot.Id,ItemId:pile.Lot.ItemId,Quantity:q)));
            if(store is not null){Move(steps,pile.Position,store.Position);steps.Add(new(new(ActionKind.Transfer,Source:carrier.Id,Destination:store.Id,ItemId:pile.Lot.ItemId,Quantity:q)));}
            else{Move(steps,pile.Position,offer!.Position);steps.Add(new(new(ActionKind.SellAvailableToCapital,OfferId:offer.Id,ItemId:pile.Lot.ItemId,Quantity:q)));}
            if(PlanTicks(o,steps)<pile.Lot.ExpiresTick-o.Tick)yield return new("RescueOverflow",pile.Lot.Id.ToString(),1400000,steps.ToArray());
        }
        var outputs=o.Enterprises.SelectMany(ProductionWorkplaces).Select(l=>o.Map.Recipes.Single(r=>r.Id==l.Unit.RecipeId).OutputItem).ToHashSet(StringComparer.Ordinal);
        foreach(var store in o.Inventories.Where(i=>o.Enterprises.Any(e=>e.OutputId==i.Id)||o.Buildings.Any(b=>b.Kind is BuildingKind.Warehouse or BuildingKind.Shop&&b.InputId==i.Id)))
        foreach(var stock in store.Stock.Where(s=>s.Quantity>s.Reserved&&outputs.Contains(s.ItemId)))
        {
            var producers=o.Enterprises.Where(e=>e.OutputId==store.Id).ToArray();
            if(producers.Length>0&&stock.Quantity-stock.Reserved<Math.Max(1,_state.Settings.BatchQuantity/4)&&o.Account.AvailableMinor>=_state.Settings.CashReserveMinor&&producers.Any(e=>ProductionWorkplaces(e).Any(l=>l.Unit.Enabled))&&!producers.Any(e=>ProductionWorkplaces(e).Any(l=>l.Unit.ReasonCode=="NoOutputSpace")))continue;
            long protectedQuantity=o.Enterprises.SelectMany(ProductionWorkplaces).SelectMany(l=>o.Map.Recipes.Single(r=>r.Id==l.Unit.RecipeId).Ingredients).Where(i=>i.ItemId==stock.ItemId).Sum(i=>i.Quantity);
            long q=Math.Min(Math.Max(0,stock.Quantity-stock.Reserved-protectedQuantity),Math.Min(_state.Settings.BatchQuantity,FreeVolume(carrier)/Volume(o,stock.ItemId)));if(q<=0)continue;
            var offer=BestOffer(o,stock.ItemId,q,store.Position);var steps=new List<PlanStep>();Move(steps,o.Participant.Position,store.Position);steps.Add(new(new(ActionKind.Transfer,Source:store.Id,Destination:carrier.Id,ItemId:stock.ItemId,Quantity:offer is null?q:Math.Min(q,offer.RemainingQuantity))));
            if(offer is not null)
            {
                q=Math.Min(q,offer.RemainingQuantity);Move(steps,store.Position,offer.Position);steps.Add(new(new(ActionKind.SellAvailableToCapital,OfferId:offer.Id,ItemId:stock.ItemId,Quantity:q)));
                yield return new("SellStoredOutput",store.Id.Value+":"+stock.ItemId,o.Account.AvailableMinor<_state.Settings.CashReserveMinor?260000:95000+Rate(o,offer,q,store.Position)*_state.Settings.CashWeight,steps.ToArray());
            }
            else if(o.ProductionContent is {} market&&Reachable(o,market.Terminal)&&o.CapitalPrices.Any(p=>p.ItemId==stock.ItemId&&p.UnitPriceMinor>0))
            {Move(steps,store.Position,market.Terminal);yield return new("StageOutputAtExchange",stock.ItemId,95000,steps.ToArray());}
        }
        if(o.ProductionContent is {} exchange&&o.Participant.Position!=exchange.Terminal&&Reachable(o,exchange.Terminal))
        foreach(var held in carrier.Stock.Where(s=>s.Quantity>s.Reserved&&outputs.Contains(s.ItemId)))
            yield return new("StageOutputAtExchange",held.ItemId,95000,[new(new(ActionKind.MoveTo,Target:exchange.Terminal,ItemId:held.ItemId))]);
        foreach(var e in o.Enterprises)
        foreach(var line in ProductionWorkplaces(e))
        {
            if(line.Unit.Enabled&&line.Unit.BatchId==0&&!ProductionWorthRunning(o,line.Unit,line.Group))
                yield return One("PreserveCash","UnprofitableLine:"+line.Workshop,220000,new(ActionKind.SetProductionEnabled,Enterprise:e.Id,WorkshopId:line.Workshop,Enabled:false));
            var crew=o.EmploymentGroups.Single(g=>g.Id==line.Group);
            if(line.Unit.Enabled&&crew.Headcount==0&&crew.WageMinor==0&&crew.PendingWageMinor is null&&ProductionWorthRunning(o,line.Unit,line.Group)&&o.Account.AvailableMinor>_state.Settings.CashReserveMinor+_state.Settings.BaseWageMinor*_state.Settings.WageReviewTicks)
                yield return WageCandidate(o,crew,_state.Settings.BaseWageMinor,260000,"RestartFundedWorkplace:"+line.Workshop);
            var recipe=o.Map.Recipes.Single(r=>r.Id==line.Unit.RecipeId);var input=Inventory(o,e.InputId);
            foreach(var ingredient in recipe.Ingredients)
            {
                long held=Math.Min(Free(carrier,ingredient.ItemId),Math.Min(InputSpace(o,input,recipe,ingredient.ItemId),Math.Max(0,InputTarget(o,input,recipe,ingredient)-Free(input,ingredient.ItemId))));
                if(held>0){var steps=new List<PlanStep>();Move(steps,o.Participant.Position,input.Position);steps.Add(new(new(ActionKind.Transfer,Source:carrier.Id,Destination:input.Id,ItemId:ingredient.ItemId,Quantity:held)));yield return new("SupplyProduction","UseCarried:"+e.Id.Value+":"+ingredient.ItemId,280000,steps.ToArray());}
            }
        }
        foreach(var run in o.Services?.Execution?.Runs??[])
            if(run.Automatic&&o.Services!.OwnOrders.Any(s=>s.Id==run.Run.Order&&s.Carrier==Actor&&s.State=="Accepted")&&o.Tick-run.Run.StageStartedTick>=_state.Settings.BlockedRetryTicks&&
                (run.Headcount==0||o.Account.AvailableMinor<(o.Services.Execution!.Crews.FirstOrDefault(c=>c.Vehicle==run.Vehicle)?.Group.WageMinor??1)))
                yield return One("UsePersonalDriver","FiniteCrewRecovery:"+run.Vehicle,1500000,new(ActionKind.SetServiceAutomation,VehicleId:run.Vehicle,Enabled:false));
        if(options is null||o.Services is null||o.Services.OwnOrders.Any(s=>s.Customer==Actor&&s.Active))yield break;
        foreach(var q in options.ServiceQuotes)
        {
            var t=q.Terms;var destination=o.Enterprises.FirstOrDefault(e=>e.InputId==t.Destination);
            bool input=destination is not null&&ProductionWorkplaces(destination).Any(l=>o.Map.Recipes.Single(r=>r.Id==l.Unit.RecipeId).Uses(t.ItemId))&&Free(Inventory(o,t.Destination),t.ItemId)<t.Quantity;
            bool contract=t.ContractId!=0;if(!input&&!contract)continue;
            if(q.FeeMinor+_state.Settings.CashReserveMinor+ExecutionReserve(o)>o.Account.AvailableMinor)continue;
            long value=checked(t.Quantity*BasePrice(o,t.ItemId));long unit=Volume(o,t.ItemId),per=carrier.Capacity/unit;if(per<=0)continue;long trips=(t.Quantity+per-1)/per;
            long manualTicks=checked((Distance(o.Map,o.Participant.Position,q.SourcePosition)+(2*trips-1)*q.LoadedDistance)*o.Map.MoveTicksPerCell+2*q.Volume/Math.Max(1,o.Economics?.Handling.PersonalVolumePerTick??1)+2*trips);
            long opportunity=checked(manualTicks*_state.Settings.BaseWageMinor);
            if(q.FeeMinor>=value||q.FeeMinor>=opportunity||q.DestinationOccupiedVolume+q.ExpectedIncomingVolume+q.Volume>q.DestinationCapacity)continue;
            if(contract&&q.FeeMinor>=t.Quantity*o.OwnDeliveryContracts.Single(d=>d.Id==t.ContractId).UnitPriceMinor)continue;
            yield return One("PurchaseDelivery","FundedUsefulTrip:"+t.Source.Value+":"+t.Destination.Value,contract||t.PurchaseId!=0?1250000:310000,new(ActionKind.RequestDeliveryService,VehicleId:t.VehicleId,Source:t.Source,Destination:t.Destination,ItemId:t.ItemId,Quantity:t.Quantity,PurchaseId:t.PurchaseId,ContractId:t.ContractId,ServiceQuote:q.Token));
        }
    }
}
