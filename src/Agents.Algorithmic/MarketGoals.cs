using Simulation.Core;

namespace Agents.Algorithmic;

public sealed partial class AlgorithmicController
{
    private IEnumerable<Candidate> MarketCandidates(ControllerObservation o)
    {
        var carrier=Carrier(o);long volume=FreeVolume(carrier);
        foreach(var e in o.Enterprises)
        {
            var recipe=o.Map.Recipes.Single(r=>r.Id==e.RecipeId);var input=Inventory(o,e.InputId);
            long missing=Math.Min(FreeVolume(input)/Volume(o,recipe.InputItem),Math.Max(0,_state.Settings.BatchQuantity-Free(input,recipe.InputItem)));
            long q=Math.Min(missing,Free(carrier,recipe.InputItem));
            if(q>0){var steps=new List<PlanStep>();Move(steps,o.Participant.Position,input.Position);
                steps.Add(new(new(ActionKind.Transfer,Source:carrier.Id,Destination:input.Id,ItemId:recipe.InputItem,Quantity:q)));
                yield return new("SupplyProduction","CarriedInputs",420000*_state.Settings.ProductionWeight/100,steps.ToArray());}
            if(q==0 && missing>0 && volume>0)
            {
                var source=o.Inventories.Where(i=>i.Id!=carrier.Id && i.Id!=input.Id && !o.Enterprises.Any(x=>x.InputId==i.Id) && Free(i,recipe.InputItem)>0 && Reachable(o,i.Position))
                    .OrderBy(i=>Distance(o.Map,o.Participant.Position,i.Position)).ThenBy(i=>i.Id.Value).FirstOrDefault();
                if(source is not null && !o.OwnLogistics.Any(l=>l.EnterpriseId==e.Id && l.InboundEnabled && l.Queue.Any(j=>j.Enabled && j.InboundSource==source.Id)))
                {
                    q=Math.Min(missing,Math.Min(Free(source,recipe.InputItem),volume/Volume(o,recipe.InputItem)));
                    if(q>0){var steps=new List<PlanStep>();Move(steps,o.Participant.Position,source.Position);
                        steps.Add(new(new(ActionKind.Transfer,Source:source.Id,Destination:carrier.Id,ItemId:recipe.InputItem,Quantity:q)));
                        Move(steps,source.Position,input.Position);steps.Add(new(new(ActionKind.Transfer,Source:carrier.Id,Destination:input.Id,ItemId:recipe.InputItem,Quantity:q)));
                        yield return new("SupplyProduction","StoredInputs",130000*_state.Settings.ProductionWeight/100,steps.ToArray());}
                }
            }
        }
        if(!_state.Settings.TradeEnabled)yield break;
        foreach(var posted in o.BuyOffers.Where(b=>b.Buyer==Actor && b.Status=="Open"))
            if(o.MarketOffers.Any(m=>m.Seller!=Actor && m.ItemId==posted.ItemId && m.Status==MarketOfferStatus.Collecting && m.BasePriceMinor<=BuyLimit(o,m.ItemId)) ||
                (o.Tick-posted.CreatedTick>=_state.Settings.BlockedRetryTicks && posted.AllocatedQuantity==0 && posted.CutoffTick is null))
                yield return One("CancelInputOffer",posted.ItemId,290000,new(ActionKind.CancelBuyOffer,BuyOfferId:posted.Id));
        foreach(var offer in o.MarketOffers.Where(m=>m.Seller!=Actor && m.Status==MarketOfferStatus.Collecting && m.CreatedTick<=o.Tick && m.CutoffTick>o.Tick))
        {
            long need=Need(o,offer.ItemId);long limit=BuyLimit(o,offer.ItemId);
            long budget=Math.Max(0,o.Account.AvailableMinor-_state.Settings.CashReserveMinor);
            long q=Math.Min(need,Math.Min(offer.Quantity,Math.Min(budget/Math.Max(1,limit),volume/Volume(o,offer.ItemId))));
            if(q<=0 || o.OwnMarketBids.Any(b=>b.OfferId==offer.Id) || offer.BasePriceMinor>limit || !Reachable(o,offer.Position))continue;
            var node=o.Map.ResourceNodes.Where(n=>n.ItemId==offer.ItemId && Reachable(o,n.Position)).OrderBy(n=>Distance(o.Map,o.Participant.Position,n.Position)).FirstOrDefault();
            long mining=node is null?long.MaxValue:checked((long)Distance(o.Map,o.Participant.Position,node.Position)*o.Map.MoveTicksPerCell+q*o.Map.HarvestTicksPerUnit);
            long buying=checked((long)Distance(o.Map,o.Participant.Position,offer.Position)*o.Map.MoveTicksPerCell+o.Map.TradeWindowTicks+2);
            if(mining<=buying && offer.BasePriceMinor>=BasePrice(o,offer.ItemId))continue;
            yield return One("BuyInputs",offer.ItemId+":"+offer.Id,170000,
                new(ActionKind.SubmitBuyRequest,MarketOfferId:offer.Id,Quantity:q,MaximumPriceMinor:limit));
        }
        foreach(var shop in o.Buildings.Where(b=>b.Kind==BuildingKind.Shop))
        {
            var stock=Inventory(o,shop.InputId);
            foreach(var item in stock.Stock.Where(s=>s.Quantity>s.Reserved))
            {
                var capital=BestOffer(o,item.ItemId,item.Quantity-item.Reserved,stock.Position);
                if(capital is not null && !o.BuyOffers.Any(b=>b.Buyer!=Actor && b.Status=="Open" && b.ItemId==item.ItemId))
                {
                    long sell=Math.Min(item.Quantity-item.Reserved,Math.Min(capital.RemainingQuantity,volume/Volume(o,item.ItemId)));
                    if(sell>0){var liquidation=new List<PlanStep>();Move(liquidation,o.Participant.Position,stock.Position);
                        liquidation.Add(new(new(ActionKind.Transfer,Source:stock.Id,Destination:carrier.Id,ItemId:item.ItemId,Quantity:sell)));
                        Move(liquidation,stock.Position,capital.Position);liquidation.Add(new(new(ActionKind.SellAvailableToCapital,OfferId:capital.Id,ItemId:item.ItemId,Quantity:sell)));
                        yield return new("SellStored",item.ItemId,165000,liquidation.ToArray());}
                }
                var last=o.MarketOffers.Where(m=>m.Seller==Actor && m.Shop==shop.Id && m.ItemId==item.ItemId).OrderByDescending(m=>m.CreatedTick).FirstOrDefault();
                if(last is {Status:not MarketOfferStatus.Closed} || last is {AllocatedQuantity:0,ClosedTick: { } closed} && o.Tick-closed<_state.Settings.BlockedRetryTicks)continue;
                long q=Math.Min(item.Quantity-item.Reserved,_state.Settings.MarketLotQuantity);
                long price=checked((long)((Int128)BasePrice(o,item.ItemId)*_state.Settings.SellPriceBasisPoints/10000));
                yield return One("PublishSellOffer",item.ItemId,160000,new(ActionKind.CreateSellOffer,Building:shop.Id,ItemId:item.ItemId,Quantity:q,UnitPriceMinor:Math.Max(1,price)));
            }
            if(o.MarketOffers.Any(m=>m.Seller==Actor && m.Shop==shop.Id && m.Status!=MarketOfferStatus.Closed))continue;
            foreach(var node in o.Map.ResourceNodes.Where(n=>o.BuyOffers.Any(b=>b.Buyer!=Actor && b.Status=="Open" && b.ItemId==n.ItemId)))
            {
                if(Free(stock,node.ItemId)>0 || carrier.Stock.Any(x=>x.Quantity>0))continue;
                long q=Math.Min(_state.Settings.MarketLotQuantity,Math.Min(volume,FreeVolume(stock))/Volume(o,node.ItemId));
                var steps=Acquire(o,node.ItemId,q);if(steps is null)continue;var end=AcquisitionPosition(o,node.ItemId,q);
                if(Distance(o.Map,end,stock.Position)<0)continue;
                Move(steps,end,stock.Position);steps.Add(new(new(ActionKind.Transfer,Source:carrier.Id,Destination:stock.Id,ItemId:node.ItemId,Quantity:q)));
                yield return new("StockShop",node.ItemId,140000,steps.ToArray());
            }
            foreach(var source in o.Inventories.Where(i=>i.Id!=carrier.Id && i.Id!=stock.Id && !o.Enterprises.Any(e=>e.InputId==i.Id)))
                foreach(var item in source.Stock.Where(x=>x.Quantity>x.Reserved && o.BuyOffers.Any(b=>b.Buyer!=Actor && b.Status=="Open" && b.ItemId==x.ItemId)))
                {
                    long q=Math.Min(_state.Settings.BatchQuantity,Math.Min(item.Quantity-item.Reserved,Math.Min(volume,FreeVolume(stock))/Volume(o,item.ItemId)));
                    if(q<=0 || Distance(o.Map,source.Position,stock.Position)<0 || !Reachable(o,source.Position))continue;
                    var steps=new List<PlanStep>();Move(steps,o.Participant.Position,source.Position);steps.Add(new(new(ActionKind.Transfer,Source:source.Id,Destination:carrier.Id,ItemId:item.ItemId,Quantity:q)));
                    Move(steps,source.Position,stock.Position);steps.Add(new(new(ActionKind.Transfer,Source:carrier.Id,Destination:stock.Id,ItemId:item.ItemId,Quantity:q)));
                    yield return new("StockShop",item.ItemId,140000,steps.ToArray());
                }
        }
        if(o.Account.AvailableMinor>=_state.Settings.CashReserveMinor)
        foreach(var e in o.Enterprises)
        {
            var r=o.Map.Recipes.Single(r=>r.Id==e.RecipeId);var input=Inventory(o,e.InputId);long q=Math.Min(Need(o,r.InputItem),FreeVolume(input)/Volume(o,r.InputItem));
            if(q<=0 || !o.Buildings.Any(b=>b.InputId==input.Id) || o.BuyOffers.Any(b=>b.Buyer==Actor && b.ItemId==r.InputItem && (b.Status=="Open" || o.Tick-(b.ExpiresAtTick-o.DeliveryRules.OfferTtlTicks)<_state.Settings.BlockedRetryTicks*2)))continue;
            long price=BasePrice(o,r.InputItem);q=Math.Min(q,Math.Max(0,o.Account.AvailableMinor-_state.Settings.CashReserveMinor)/price);
            if(q>0)yield return One("PostInputOffer",r.InputItem,105000,new(ActionKind.CreateBuyOffer,Destination:input.Id,ItemId:r.InputItem,Quantity:q,UnitPriceMinor:price));
        }
        if(o.Buildings.All(b=>b.Kind!=BuildingKind.Shop) && o.Enterprises.Length>0 && carrier.Stock.All(s=>s.Quantity==0))
        foreach(var type in o.BuildingTypes.Where(t=>t.Kind==BuildingKind.Shop && t.Footprint.Length==1))
        {
            var level=type.Levels.Single(l=>l.Level==1);
            var plot=o.Plots.OrderByDescending(p=>p.OwnerId==Actor || p.ReservedBy==Actor)
                .ThenBy(p=>Math.Abs(p.Position.X-o.Participant.Position.X)+Math.Abs(p.Position.Z-o.Participant.Position.Z)).ThenBy(p=>p.Id.Value).FirstOrDefault(p=>(p.IsShopSite || p.ShopParticipantId==Actor) && p.BuildingId is null && (p.OwnerId is null || p.OwnerId==Actor) && (p.ReservedBy is null || p.ReservedBy==Actor) && Reachable(o,p.Position));
            if(plot is null || o.Account.AvailableMinor<checked(level.PriceMinor+(plot.OwnerId is null?plot.PriceMinor:0)+_state.Settings.CashReserveMinor) ||
                level.Materials.Sum(m=>checked(m.Quantity*Volume(o,m.ItemId)))>volume)continue;
            var steps=new List<PlanStep>();Cell end=o.Participant.Position;bool valid=true;
            foreach(var material in level.Materials)
            {
                if(level.Materials.Length>1){valid=false;break;}
                var fetch=Fetch(o,material.ItemId,material.Quantity,out end);if(fetch is null){valid=false;break;}steps.AddRange(fetch);
            }
            if(!valid)continue;Move(steps,end,plot.Position);
            if(plot.OwnerId is null)steps.Add(new(new(ActionKind.BuyPlot,Plot:plot.Id)));
            steps.Add(new(new(ActionKind.Build,Target:plot.Position,BuildingTypeId:type.Id)));
            yield return new("BuildShop",type.Id,30000,steps.ToArray());
        }
    }
    private long BuyLimit(ControllerObservation o,string item)=>Math.Max(1,checked((long)((Int128)BasePrice(o,item)*_state.Settings.MaximumBuyPriceBasisPoints/10000)));
}
