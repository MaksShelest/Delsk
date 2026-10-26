using Simulation.Core;

namespace Agents.Algorithmic;

public sealed partial class AlgorithmicController
{
    private IEnumerable<Candidate> IndustryQuestCandidates(ControllerObservation o)
    {
        var d=o.Development;if(d is null)yield break;
        if(d.Quests.Count(q=>q.Status=="Active")<d.Rules.QuestSlots)
        foreach(var c in d.Choices.GroupBy(c=>c.ContractId).Select(g=>g.OrderBy(c=>c.Quantity).First()))
            yield return One("AcceptIndustryQuest","FundedDemand:"+c.ContractId,400000,new(ActionKind.AcceptIndustryQuest,ContractId:c.ContractId,BuildingTypeId:c.Industry,Quantity:c.Batches,OfferWindow:c.ReviewWindow));
    }
    private IEnumerable<Candidate> DevelopmentCandidates(ControllerObservation o)
    {
        var d=o.Development;if(d is null)yield break;var carrier=Carrier(o);var settings=_state.Settings;
        if(HasObligations(o)||o.Account.AvailableMinor<settings.CashReserveMinor||settings.ExpansionWeight==0)yield break;
        // Each plan stages one real backpack load and replans after receipt. No promised inventory IDs.
        Candidate? Supply(ControllerInventory destination,MaterialCost[] materials,string goal,string reason)
        {
            var missing=materials.FirstOrDefault(m=>Free(destination,m.ItemId)<m.Quantity);if(missing is null)return null;
            long q=Math.Min(missing.Quantity-Free(destination,missing.ItemId),Math.Min(FreeVolume(destination),FreeVolume(carrier))/Volume(o,missing.ItemId));
            long held=Math.Min(missing.Quantity-Free(destination,missing.ItemId),Free(carrier,missing.ItemId));
            List<PlanStep>? steps;Cell last;
            if(held>0){q=Math.Min(held,FreeVolume(destination)/Volume(o,missing.ItemId));steps=[];last=o.Participant.Position;}
            else{steps=Fetch(o with{Inventories=[..o.Inventories.Where(i=>i.Id!=destination.Id)]},missing.ItemId,q,out last);}
            if(steps is null||q<=0||Distance(o.Map,last,destination.Position)<0)return null;
            Move(steps,last,destination.Position);steps.Add(new(new(ActionKind.Transfer,Source:carrier.Id,Destination:destination.Id,ItemId:missing.ItemId,Quantity:q)));
            return new(goal,reason+":"+missing.ItemId,150000*settings.ExpansionWeight/100,steps.ToArray());
        }
        foreach(var b in o.Buildings.Where(b=>b.Kind==BuildingKind.Production))
        {
            if(d.Projects.Any(p=>p.BuildingId==b.Id.Value)||o.Enterprises.Single(e=>e.Id.Value==b.Id.Value).ReasonCode=="PreparingDemolition")continue;
            var type=o.BuildingTypes.Single(t=>t.Id==b.TypeId);var next=type.Levels.SingleOrDefault(l=>l.Level==b.Level+1);if(next is null||d.Experience.Single(x=>x.Industry==b.TypeId).TechnologyLevel<next.Level)continue;
            var input=Inventory(o,b.InputId);var enterprise=o.Enterprises.Single(e=>e.Id.Value==b.Id.Value);
            var cells=next.Footprint.Select(c=>new Cell(b.Position.X+c.X,b.Position.Z+c.Z)).ToArray();var plots=cells.Select(c=>o.Plots.SingleOrDefault(p=>p.Position==c)).ToArray();
            if(plots.Any(p=>p is null||p.OwnerId is {} owner&&owner!=Actor||p.ReservedBy is {} reservation&&reservation!=Actor||p.BuildingId is {} occupied&&occupied!=b.Id))continue;
            long land=plots.Where(p=>p!.OwnerId is null).Sum(p=>p!.PriceMinor);
            if(o.Account.AvailableMinor<next.PriceMinor+land+settings.CashReserveMinor)continue;
            // Require a reachable source before stopping a working business.
            bool materialsPossible=next.Materials.All(m=>Free(input,m.ItemId)+Free(carrier,m.ItemId)>=m.Quantity||o.Inventories.Any(i=>i.Id!=input.Id&&Free(i,m.ItemId)>0)||o.Map.ResourceNodes.Any(n=>n.ItemId==m.ItemId&&AvailableResource(o,n))||o.ProductionContent?.Imports.Any(i=>i.ItemId==m.ItemId&&i.Remaining>0)==true);
            if(!materialsPossible)continue;
            if(!d.Preparing.Contains(b.Id.Value)&&(enterprise.Workshops.Any(w=>w.Enabled||w.BatchId!=0)))
            {yield return One("ExpandEnterprise","Prepare:"+b.Id.Value,155000,new(ActionKind.PrepareEnterpriseExpansion,Building:b.Id));continue;}
            var plot=plots.FirstOrDefault(p=>p!.OwnerId is null);
            if(plot is not null){var steps=new List<PlanStep>();Move(steps,o.Participant.Position,b.Position);steps.Add(new(new(ActionKind.BuyExpansionLand,Building:b.Id,Plot:plot.Id)));yield return new("ExpandEnterprise","Land:"+plot.Id.Value,150000,steps.ToArray());continue;}
            if(Supply(input,next.Materials.ToArray(),"ExpandEnterprise","Materials:"+b.Id.Value) is {} supply){yield return supply;continue;}
            if(next.Materials.Any(m=>Free(input,m.ItemId)<m.Quantity)||enterprise.Workshops.Any(w=>w.Enabled||w.BatchId!=0)||enterprise.Installations.Length>0)continue;
            var build=new List<PlanStep>();Move(build,o.Participant.Position,b.Position);build.Add(new(new(ActionKind.UpgradeBuilding,Building:b.Id,Quantity:next.Level)));yield return new("ExpandEnterprise","PaidTier:"+next.Level,150000,build.ToArray());
        }
        if(o.Enterprises.Length>0||d.Projects.Length>0)yield break;
        foreach(var type in o.BuildingTypes.Where(t=>t.Kind==BuildingKind.Production))
        {
            var level=type.Levels.Single(l=>l.Level==1);var recipe=o.Map.Recipes.Single(r=>r.Id==level.RecipeId);
            if(!recipe.Uses(settings.PreferredInput)||BestOffer(o,recipe.OutputItem,recipe.OutputQuantity,o.Participant.Position) is null)continue;
            foreach(var plot in o.Plots.Where(p=>p.BuildingId is null&&p.ExpansionFor is null&&!p.IsCorridor&&!p.IsShopSite&&p.ShopParticipantId is null&&(p.OwnerId is null||p.OwnerId==Actor)&&(p.ReservedBy is null||p.ReservedBy==Actor)&&
                (d.BuildAnchors.IsEmpty||d.BuildAnchors.Contains(p.Position))).OrderBy(p=>p.OwnerId==Actor?0:1).ThenBy(p=>p.Id.Value))
            {
                var contour=type.Levels.MaxBy(l=>l.Level)!.Footprint.Select(c=>new Cell(plot.Position.X+c.X,plot.Position.Z+c.Z));
                if(contour.Any(c=>!o.Plots.Any(p=>p.Position==c&&p.BuildingId is null&&p.ExpansionFor is null&&(p.OwnerId is null||p.OwnerId==Actor)&&(p.ReservedBy is null||p.ReservedBy==Actor))))continue;
                if(o.Account.AvailableMinor<level.PriceMinor+(plot.OwnerId is null?plot.PriceMinor:0)+settings.CashReserveMinor)continue;
                if(plot.OwnerId is null){var steps=new List<PlanStep>();Move(steps,o.Participant.Position,plot.Position);steps.Add(new(new(ActionKind.BuyPlot,Plot:plot.Id)));yield return new("BuildProduction","Anchor:"+plot.Id.Value,145000,steps.ToArray());break;}
                var ground=o.Inventories.SingleOrDefault(i=>i.Id!=carrier.Id&&i.Position==plot.Position&&!o.Buildings.Any(b=>b.InputId==i.Id||b.OutputId==i.Id));if(ground is null)continue;
                if(Supply(ground,level.Materials.ToArray(),"BuildProduction","Stage:"+plot.Id.Value) is {} supply){yield return supply;break;}
                if(level.Materials.Any(m=>Free(ground,m.ItemId)<m.Quantity))break;
                var build=new List<PlanStep>();Move(build,o.Participant.Position,plot.Position);build.Add(new(new(ActionKind.Build,Target:plot.Position,BuildingTypeId:type.Id)));yield return new("BuildProduction","Timed:"+plot.Id.Value,150000,build.ToArray());break;
            }
        }
    }
}
