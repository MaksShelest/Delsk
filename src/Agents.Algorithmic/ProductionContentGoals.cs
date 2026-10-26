using Simulation.Core;

namespace Agents.Algorithmic;

public sealed partial class AlgorithmicController
{
    private IEnumerable<Candidate> ProductionContentCandidates(ControllerObservation o)
    {
        var c=o.ProductionContent;if(c is null)yield break;
        var carrier=Carrier(o);
        var needs=new Dictionary<string,long>(StringComparer.Ordinal);
        void Require(IEnumerable<MaterialCost> costs){foreach(var m in costs)needs[m.ItemId]=Math.Max(needs.GetValueOrDefault(m.ItemId),m.Quantity);}
        foreach(var e in o.Enterprises)
        {
            foreach(var line in ProductionWorkplaces(e))Require(o.Map.Recipes.Single(r=>r.Id==line.Unit.RecipeId).Ingredients);
            var type=o.BuildingTypes.Single(t=>t.Id==o.Buildings.Single(b=>b.Id.Value==e.Id.Value).TypeId);
            if(type.Levels.SingleOrDefault(l=>l.Level==e.Level+1) is {} level&&o.Development!.Experience.Single(x=>x.Industry==type.Id).TechnologyLevel>=level.Level)Require(level.Materials);
        }
        if(o.Enterprises.IsEmpty)
            foreach(var type in o.BuildingTypes.Where(t=>t.Kind==BuildingKind.Production&&t.Levels[0].UnlockedRecipes.Any(id=>o.Map.Recipes.Single(r=>r.Id==id).Uses(_state.Settings.PreferredInput))))Require(type.Levels[0].Materials);
        foreach(var e in o.Enterprises)
        foreach(var w in e.Workshops)
        {
            int version=c.Technologies.SingleOrDefault(t=>t.Workshop==w.Id)?.Version??1;
            var upgrade=c.Rules.Modernizations.SingleOrDefault(m=>m.Version==version+1);
            var b=o.Buildings.Single(b=>b.Id.Value==e.Id.Value);
            if(upgrade is null||HasObligations(o)||o.Development!.Experience.Single(x=>x.Industry==b.TypeId).TechnologyLevel<(version==1?4:5)||c.Projects.Any(p=>p.Workshop==w.Id)||o.Account.AvailableMinor<upgrade.PriceMinor+_state.Settings.CashReserveMinor)continue;
            Require(upgrade.Materials);var input=Inventory(o,e.InputId);
            var missing=upgrade.Materials.FirstOrDefault(m=>Free(input,m.ItemId)<m.Quantity);
            if(missing is not null)
            {
                long q=Math.Min(missing.Quantity-Free(input,missing.ItemId),Math.Min(FreeVolume(carrier),FreeVolume(input))/Volume(o,missing.ItemId));
                if(q>0)
                {
                    var held=Math.Min(q,Free(carrier,missing.ItemId));var steps=held>0?new List<PlanStep>():Fetch(o with{Inventories=[..o.Inventories.Where(i=>i.Id!=input.Id)]},missing.ItemId,q,out _);
                    if(steps is not null){if(held>0)q=held;var last=held>0?o.Participant.Position:steps.LastOrDefault(s=>s.Action.Kind==ActionKind.MoveTo)?.Action.Target??o.Participant.Position;Move(steps,last,b.Position);steps.Add(new(new(ActionKind.Transfer,Source:carrier.Id,Destination:input.Id,ItemId:missing.ItemId,Quantity:q)));yield return new("ModernizeWorkshop","Stage:"+missing.ItemId,151000,steps.ToArray());}
                }
                continue;
            }
            if(w.BatchId!=0)continue;
            var plan=new List<PlanStep>();if(w.Enabled)plan.Add(new(new(ActionKind.SetProductionEnabled,Enterprise:e.Id,WorkshopId:w.Id,Enabled:false)));
            Move(plan,o.Participant.Position,b.Position);plan.Add(new(new(ActionKind.ModernizeWorkshop,Enterprise:e.Id,WorkshopId:w.Id,Quantity:version+1)));
            yield return new("ModernizeWorkshop","PaidLocalVersion:"+w.Id,152000,plan.ToArray());
        }
        foreach(var pair in needs)
        {
            var stock=c.Imports.SingleOrDefault(i=>i.ItemId==pair.Key);if(stock is null||stock.Remaining==0)continue;
            long own=o.Inventories.Sum(i=>Free(i,pair.Key));long missing=Math.Max(0,pair.Value-own);if(missing==0)continue;
            var price=c.Rules.Imports.Single(i=>i.ItemId==pair.Key).UnitPriceMinor;
            long q=Math.Min(stock.Remaining,Math.Min(missing,Math.Max(0,o.Account.AvailableMinor-_state.Settings.CashReserveMinor)/price));if(q<=0)continue;
            var steps=new List<PlanStep>();Move(steps,o.Participant.Position,c.Terminal);steps.Add(new(new(ActionKind.BuyImport,ItemId:pair.Key,Quantity:q)));
            yield return new("BuyImport","FiniteSupply:"+pair.Key,153000,steps.ToArray());
        }
    }
}
