using System.Collections.Immutable;
using Simulation.Core;

namespace Agents.Algorithmic;

public record PlanningLimits(int MaxCandidates=12,int Depth=2,int HorizonTicks=120,int MaxEvaluations=32,int CapitalOpportunityBasisPoints=1000)
{
    public void Validate()
    {if(MaxCandidates is <2 or >32 || Depth is <1 or >3 || HorizonTicks is <20 or >600 || MaxEvaluations<MaxCandidates || MaxEvaluations>96 || CapitalOpportunityBasisPoints is <0 or >10000)throw new ArgumentException("Invalid planning budget");}
}
public record StrategyAssessment(string Goal,long Eta,long NetMinor,long CapitalMinor,long ExecutionReserveMinor,long RiskMinor,int Continuations,int Evaluations,string Reason);

public sealed partial class AlgorithmicController
{
    public ImmutableArray<StrategyAssessment> LatestStrategyTrace=>[.._state.StrategyTrace??[]];
    private PlanningLimits Limits=>_state.Settings.Planning??new();
    private static long Bounded(Int128 value)=>(long)Int128.Clamp(value,long.MinValue/4,long.MaxValue/4);
    private long ForecastTicks(ControllerObservation o,Candidate c,Cell start)
    {
        long ticks=0;var at=start;var handling=o.Economics?.Handling??new();
        foreach(var step in c.Steps)
        {
            var a=step.Action;ticks++;
            if(a.Kind==ActionKind.MoveTo){int distance=Distance(o.Map,at,a.Target);if(distance<0)return long.MaxValue;ticks+=(long)distance*o.Map.MoveTicksPerCell;at=a.Target;}
            if(a.Kind==ActionKind.Harvest)ticks+=a.Quantity*o.Map.HarvestTicksPerUnit;
            if(a.Kind is ActionKind.Transfer or ActionKind.DeliverContract or ActionKind.CollectPurchase)
                ticks+=(a.Quantity*Volume(o,a.ItemId==""?o.OwnMarketAllocations.FirstOrDefault(x=>x.Id==a.PurchaseId)?.ItemId??o.OwnDeliveryContracts.FirstOrDefault(x=>x.Id==a.ContractId)?.ItemId??o.Map.Items[0].Id:a.ItemId)+handling.PersonalVolumePerTick-1)/handling.PersonalVolumePerTick;
            if(a.Kind==ActionKind.SellAvailableToCapital)ticks+=o.Map.TradeWindowTicks;
        }
        return ticks;
    }
    private long ExecutionReserve(ControllerObservation o)
    {
        Int128 reserve=0;
        foreach(var line in o.Enterprises.SelectMany(ProductionWorkplaces).Where(l=>l.Unit.Enabled))
        {
            var e=line.Unit;var r=o.Map.Recipes.Single(r=>r.Id==e.RecipeId);var input=Inventory(o,e.InputId);
            if(e.BatchId!=0 || r.Ingredients.All(i=>Free(input,i.ItemId)>=i.Quantity))
                reserve+=(Int128)e.StaffCapacity*o.EmploymentGroups.Single(g=>g.Id==line.Group).WageMinor+(o.Economics?.Workshops?.ServiceMinorPerFullTick??r.EnergyPerBatchMinor);
        }
        foreach(var l in o.OwnLogistics.Where(l=>l.Trip is null && (l.Enabled || l.InboundEnabled) && l.Queue.Any(j=>j.Enabled)))
        {
            var group=o.EmploymentGroups.Single(g=>g.Id==l.GroupId);
            reserve+=(Int128)group.Capacity*group.WageMinor*Limits.HorizonTicks;
        }
        return Bounded(reserve);
    }
    private HashSet<string> Claims(ControllerObservation o,Candidate c)
    {
        var keys=new HashSet<string>(StringComparer.Ordinal);
        foreach(var step in c.Steps)
        {
            var a=step.Action;
            var enterprise=o.Enterprises.FirstOrDefault(e=>a.Enterprise==e.Id || a.Destination==e.InputId);
            var recipe=enterprise is null?null:o.Map.Recipes.Single(r=>r.Id==enterprise.RecipeId);
            if(a.Kind==ActionKind.Build){var type=o.BuildingTypes.Single(t=>t.Id==a.BuildingTypeId);if(type.Kind==BuildingKind.Production)recipe=o.Map.Recipes.Single(r=>r.Id==type.Levels[0].RecipeId);}
            if(recipe is not null){keys.Add("future-output:"+recipe.OutputItem);foreach(var input in recipe.Ingredients)keys.Add("supply:"+input.ItemId);}
            if(a.Kind is ActionKind.Transfer or ActionKind.Harvest or ActionKind.SellAvailableToCapital or ActionKind.CollectPurchase or ActionKind.DeliverContract)keys.Add("carrier");
            if(a.OfferId!=0)keys.Add("capital:"+a.OfferId);if(a.MarketOfferId!=0)keys.Add("market:"+a.MarketOfferId);
            if(a.Enterprise.Value!=0)keys.Add("enterprise:"+a.Enterprise.Value);if(a.Group.Value!=0)keys.Add("group:"+a.Group.Value);
            if(a.Source.Value!=0)keys.Add("inventory:"+a.Source.Value);if(a.Destination.Value!=0)keys.Add("inventory:"+a.Destination.Value);
            if(a.Kind is ActionKind.BuyPlot or ActionKind.Build or ActionKind.BuyEquipment or ActionKind.BuyVehicle or ActionKind.BuyLogistics or ActionKind.SubmitBuyRequest or ActionKind.SetWage or ActionKind.ProposeTrade)keys.Add("cash");
        }
        return keys;
    }
    private (long Value,long Capital,long Risk) ForecastValue(ControllerObservation o,Candidate c,long eta)
    {
        Int128 value=0,capital=0,risk=0;var settings=_state.Settings;
        var actions=c.Steps.Select(s=>s.Action).ToArray();
        foreach(var a in actions)
        {
            if(a.Kind==ActionKind.SellAvailableToCapital)
            {
                var offer=o.CapitalOffers.SingleOrDefault(x=>x.Id==a.OfferId);if(offer is null)continue;
                long q=Math.Min(a.Quantity,offer.RemainingQuantity);
                long fresh=actions.Where(x=>x.Kind==ActionKind.Harvest && x.ItemId==a.ItemId).Sum(x=>x.Quantity);
                value+=(Int128)q*offer.UnitPriceMinor-(Int128)Math.Max(0,q-fresh)*BasePrice(o,a.ItemId);
                if(offer.ExpiresAtTick<=o.Tick+eta)risk+=(Int128)q*offer.UnitPriceMinor;
            }
            if(a.Kind==ActionKind.BuyPlot)capital+=o.Plots.Single(x=>x.Id==a.Plot).PriceMinor;
            if(a.Kind==ActionKind.BuyResource)capital+=o.Resources.Single(x=>x.Position==a.Target).PurchasePriceMinor;
            if(a.Kind==ActionKind.Build)
            {
                var type=o.BuildingTypes.Single(x=>x.Id==a.BuildingTypeId);var level=type.Levels[0];capital+=level.PriceMinor;
                capital+=level.Materials.Sum(m=>checked(m.Quantity*BasePrice(o,m.ItemId)));
                if(type.Kind==BuildingKind.Production)value+=ProductionOpportunity(o,o.Map.Recipes.Single(r=>r.Id==level.RecipeId),level.StaffCapacity,level.WorkRateMultiplier,level.WageMinor,eta,a.Target,level.Materials);
            }
            if(a.Kind==ActionKind.BuyEquipment)
            {
                var type=o.EquipmentTypes.Single(x=>x.Id==a.EquipmentTypeId);var level=type.Levels[0];capital+=level.PriceMinor;
                var site=o.Resources.SingleOrDefault(x=>x.Position==a.Target);var offer=BestOffer(o,type.ItemId,level.CycleQuantity,a.Target);
                if(site is not null && offer is not null)
                {long cycles=Math.Min(site.Stock/level.CycleQuantity,Math.Max(0,Limits.HorizonTicks-eta-(o.Economics?.Labor.SalaryChangeDelayTicks??20))/level.CycleTicks);
                    long units=Math.Min(offer.RemainingQuantity,cycles*level.CycleQuantity);value+=(Int128)units*offer.UnitPriceMinor-(Int128)cycles*level.CrewCapacity*level.WageMinor*level.CycleTicks;}
            }
            if(a.Kind==ActionKind.OutfitWorkshop){var b=o.Buildings.Single(b=>b.Id.Value==a.Enterprise.Value);var outfit=o.BuildingTypes.Single(t=>t.Id==b.TypeId).WorkshopOutfit!;capital+=outfit.PriceMinor+outfit.Materials.Sum(m=>checked(m.Quantity*BasePrice(o,m.ItemId)));}
            if(a.Kind==ActionKind.BuyLogistics)capital+=o.LogisticsRules.PurchasePriceMinor;
            if(a.Kind==ActionKind.BuyVehicle)capital+=o.Fleet!.Models.Single(m=>m.Id==o.Fleet.Stock.Single(v=>v.Id==a.VehicleId).ModelId).PriceMinor;
            if(a.Kind==ActionKind.AcceptDeliveryQuantity && o.BuyOffers.FirstOrDefault(b=>b.Id==a.BuyOfferId) is {} buy)
            {
                capital+=(Int128)a.Quantity*buy.UnitPriceMinor*o.DeliveryRules.FineBasisPoints/10000;
                var fetch=Fetch(o,buy.ItemId,Math.Max(0,a.Quantity-Free(Carrier(o),buy.ItemId)),out var end);
                if(fetch is null)risk+=(Int128)a.Quantity*buy.UnitPriceMinor;
                else {Move(fetch,end,buy.Position);fetch.Add(new(new(ActionKind.DeliverContract,ItemId:buy.ItemId,Quantity:a.Quantity)));
                    if(ForecastTicks(o,new("ForecastDelivery","",1,fetch.ToArray()),o.Participant.Position)+o.Map.TradeWindowTicks>o.DeliveryRules.ContractTtlTicks)risk+=(Int128)a.Quantity*buy.UnitPriceMinor;}
            }
            if(a.Kind==ActionKind.SubmitBuyRequest)capital+=(Int128)a.Quantity*a.MaximumPriceMinor;
            if(a.Kind==ActionKind.SetWage && o.Enterprises.FirstOrDefault(e=>e.Id.Value==a.Group.Value) is {} enterprise)
            {
                var group=o.EmploymentGroups.Single(g=>g.Id==a.Group);var next=HiringEconomics.Evaluate(o,enterprise,group,a.WageMinor,settings);var old=HiringEconomics.Evaluate(o,enterprise,group,group.WageMinor,settings);
                value+=next.ProfitMinor-old.ProfitMinor;
            }
            if(a.Kind==ActionKind.ProposeTrade && a.Terms is {Kind:TradeKind.Support} political)
            {
                long compensation=political.Compensation.Sum(i=>checked(i.Quantity*BasePrice(o,i.ItemId)));
                value-=compensation;capital+=compensation;
                if((Int128)2*(KnownVotes(o,Actor)+VoteWeight(o,political.Seller))>TotalVotes(o))value+=(Int128)settings.OwnVictoryWeight*VoteWeight(o,political.Seller)*settings.SupportPricePerVoteMinor/100;
            }
        }
        if(c.Goal is "SupplyProduction" or "ResumeProduction" or "SelectRecipe")
        {
            var e=o.Enterprises.FirstOrDefault(e=>actions.Any(a=>a.Enterprise==e.Id || a.Destination==e.InputId));
            if(e is not null){string id=actions.FirstOrDefault(a=>a.Kind==ActionKind.SetRecipe)?.RecipeId??e.RecipeId;
                value+=ProductionOpportunity(o,o.Map.Recipes.Single(r=>r.Id==id),e.StaffCapacity,e.WorkRateMultiplier,o.EmploymentGroups.Single(g=>g.Id.Value==e.Id.Value).WageMinor,eta,Inventory(o,e.InputId).Position);}
        }
        // Existing commitments keep their signed prices/deadlines; new plans cannot spend their operating reserve.
        foreach(var contract in o.OwnDeliveryContracts.Where(x=>x.Supplier==Actor && x.ClosedTick is null && x.UnprovidedQuantity>0))
            if(o.Tick+eta>=contract.DeadlineTick)risk+=(Int128)contract.BondMinor+contract.UnprovidedQuantity*contract.UnitPriceMinor;
        value-=capital*Limits.CapitalOpportunityBasisPoints/10000;
        return (Bounded(value),Bounded(capital),Bounded(risk));
    }
    private long ProductionOpportunity(ControllerObservation o,RecipeDefinition r,int capacity,int rate,long wage,long setup,Cell source,IEnumerable<MaterialCost>? consumed=null)
    {
        long ready=Math.Max(0,Limits.HorizonTicks-setup-(o.Economics?.Labor.SalaryChangeDelayTicks??20));
        var hypothetical=new EmploymentView(new(0),Actor,EmploymentRole.Production,0,capacity,wage,wage,null,null,null,null,0,0,true,"Estimate",o.Tick);
        int staff=HiringEconomics.ExpectedStaff(o,hypothetical,wage);if(staff<r.MinimumStaff)return 0;
        long duration=(r.FullStaffTicks*capacity+staff*rate-1)/(staff*rate);
        long batches=ready/Math.Max(1,duration);
        foreach(var input in r.Ingredients)
        {
            long stock=o.Inventories.Sum(i=>Free(i,input.ItemId))+o.Resources.Where(n=>n.ItemId==input.ItemId && n.Stock>n.Reserved && (n.Public || n.Owner==Actor || n.Tenant==Actor)).Sum(n=>n.Stock-n.Reserved)+
                o.MarketOffers.Where(m=>m.ItemId==input.ItemId && m.Seller!=Actor && m.Status==MarketOfferStatus.Collecting).Sum(m=>m.Quantity);
            stock=Math.Max(0,stock-(consumed??[]).Where(m=>m.ItemId==input.ItemId).Sum(m=>m.Quantity));
            batches=Math.Min(batches,stock/input.Quantity);
        }
        var offers=o.CapitalOffers.Where(a=>a.ItemId==r.OutputItem && a.RemainingQuantity>0 && (a.ExpiresAtTick is null || a.ExpiresAtTick>o.Tick+setup+duration)).OrderByDescending(a=>a.UnitPriceMinor);
        var eligible=offers.ToArray();if(eligible.Length==0)return 0;
        int distance=Distance(o.Map,source,eligible[0].Position);if(distance<0)return 0;
        long cargo=Carrier(o).Capacity/Volume(o,r.OutputItem);if(cargo==0)return 0;
        long trip=2L*distance*o.Map.MoveTicksPerCell+2*(cargo*Volume(o,r.OutputItem)+(o.Economics?.Handling.PersonalVolumePerTick??1)-1)/(o.Economics?.Handling.PersonalVolumePerTick??1)+o.Map.TradeWindowTicks+3;
        batches=Math.Min(batches,Math.Min(eligible.Sum(a=>a.RemainingQuantity),cargo*(ready/Math.Max(1,trip)))/r.OutputQuantity);
        long remaining=batches*r.OutputQuantity;Int128 revenue=0;
        foreach(var offer in eligible){long q=Math.Min(remaining,offer.RemainingQuantity);remaining-=q;revenue+=(Int128)q*offer.UnitPriceMinor;}
        long raw=r.Ingredients.Sum(i=>checked(i.Quantity*Math.Max(BasePrice(o,i.ItemId),o.CapitalPrices.Where(p=>p.ItemId==i.ItemId).Select(p=>p.UnitPriceMinor).DefaultIfEmpty(0).Max())));
        return Bounded(revenue-(Int128)batches*(raw+r.EnergyPerBatchMinor+(long)staff*wage*duration));
    }
    private Candidate[] StrategicCandidates(ControllerObservation o,Candidate[] candidates)
    {
        if(_state.Settings.Difficulty!=NpcDifficulty.Hard || HasObligations(o))return candidates;
        var limits=Limits;long reserve=ExecutionReserve(o),budget=Math.Max(0,o.Account.AvailableMinor-_state.Settings.CashReserveMinor-reserve);
        var shortlist=candidates.OrderByDescending(c=>c.Score).ThenBy(c=>c.Goal,StringComparer.Ordinal).ThenBy(c=>c.Reason,StringComparer.Ordinal).DistinctBy(c=>c.Goal).Take(limits.MaxCandidates).ToArray();
        var values=new List<(Candidate C,long Eta,long Net,long Capital,long Risk,HashSet<string> Claims)>();var longIntents=new List<Candidate>();int evaluations=0;
        foreach(var c in shortlist)
        {
            if(evaluations>=limits.MaxEvaluations)break;long eta=ForecastTicks(o,c,o.Participant.Position);var f=ForecastValue(o,c,eta);evaluations++;
            if(f.Capital>budget)continue;
            if(eta>limits.HorizonTicks)
            {
                if(UsesEconomicPlanning)longIntents.Add(c with{Score=Math.Max(1,c.Score-10000),Assessment=new(c.Goal,eta,0,f.Capital,reserve,f.Risk,0,evaluations,"ExecutableLongIntent;outsideForecastHorizon")});
                continue;
            }
            values.Add((c,eta,f.Value,f.Capital,f.Risk,Claims(o,c)));
        }
        var result=new List<Candidate>();
        foreach(var first in values)
        {
            long net=first.Net,risk=first.Risk,eta=first.Eta,capital=first.Capital;var used=new HashSet<string>(first.Claims);int continuations=0;
            var at=first.C.Steps.LastOrDefault(s=>s.Action.Kind==ActionKind.MoveTo)?.Action.Target??o.Participant.Position;
            foreach(var next in values.Where(n=>n.C!=first.C).OrderByDescending(n=>n.Net-n.Risk))
            {
                if(continuations>=limits.Depth-1 || evaluations>=limits.MaxEvaluations)break;evaluations++;
                long nextEta=ForecastTicks(o,next.C,at);
                if(at!=o.Participant.Position && next.C.Steps[0].Action.Kind!=ActionKind.MoveTo && next.Claims.Contains("carrier"))continue;
                if(next.Claims.Overlaps(used) || capital+next.Capital>budget || eta+nextEta>limits.HorizonTicks || next.Net<=next.Risk)continue;
                used.UnionWith(next.Claims);net=Bounded((Int128)net+(next.Net-next.Risk)/2);capital+=next.Capital;eta+=nextEta;at=next.C.Steps.LastOrDefault(s=>s.Action.Kind==ActionKind.MoveTo)?.Action.Target??at;continuations++;
            }
            long bonus=(long)Int128.Clamp(((Int128)net-risk)*100/Math.Max(1,eta),-250000,250000);
            var trace=new StrategyAssessment(first.C.Goal,eta,net,capital,reserve,risk,continuations,evaluations,$"forecast;h={limits.HorizonTicks};net={net};risk={risk};reserve={reserve}");
            result.Add(first.C with {Score=Math.Max(1,first.C.Score+bonus),Reason=(first.C.Reason+";"+trace.Reason)[..Math.Min(250,first.C.Reason.Length+1+trace.Reason.Length)],Assessment=trace});
        }
        return result.Concat(longIntents.Take(1)).Select(c=>c with {Assessment=c.Assessment! with {Evaluations=evaluations}}).ToArray();
    }
}
