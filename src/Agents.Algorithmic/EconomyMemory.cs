using System.Collections.Immutable;
using Simulation.Core;
namespace Agents.Algorithmic;
public sealed record RejectedIntent(string Key,int Failures,long RetryTick,string Reason);
public sealed record EconomyMemory(ImmutableArray<RejectedIntent> Rejected=default,string ConstructionIndustry="",
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] long MarketWaitUntil=0,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? MarketItem=null);
public sealed partial class AlgorithmicController
{
    private static void ValidateEconomyMemory(EconomyMemory? memory,bool enabled)
    {
        if(!enabled){if(memory is not null)throw new CheckpointException("UnexpectedEconomyMemory");return;}
        if(memory is null||memory.ConstructionIndustry is null||memory.ConstructionIndustry.Length>64||memory.MarketWaitUntil<0||memory.MarketItem is {Length:>80}||!memory.Rejected.IsDefault&& (memory.Rejected.Length>64||memory.Rejected.Select(r=>r.Key).Distinct().Count()!=memory.Rejected.Length||memory.Rejected.Any(r=>r.Key.Length>256||r.Failures is <1 or >8||r.RetryTick<0||r.Reason.Length>80)))throw new CheckpointException("InvalidEconomyMemory");
    }
    private static string Intent(GameAction a)=>Digest(new{a.Kind,a.ItemId,a.Source,a.Destination,a.Building,a.Enterprise,a.WorkshopId,a.VehicleId,a.OfferId,a.ContractId,a.Plot,a.Target,a.Group,a.Equipment,a.ServiceId,a.PurchaseId});
    private void RememberEconomicSuccess(GameAction a)
    {
        if(!UsesEconomicPlanning)return;var memory=_state.EconomyMemory!;if(!memory.Rejected.IsDefault)_state=_state with{EconomyMemory=memory with{Rejected=[..memory.Rejected.Where(r=>r.Key!=Intent(a))]}};
    }

    private void RememberEconomicFailure(GameAction action,long tick,string reason)
    {
        if(!UsesEconomicPlanning)return;var memory=_state.EconomyMemory!;var rows=memory.Rejected.IsDefault?ImmutableArray<RejectedIntent>.Empty:memory.Rejected;string key=Intent(action);int failures=Math.Min(8,(rows.FirstOrDefault(r=>r.Key==key)?.Failures??0)+1);
        long delay=Math.Min(600,checked(_state.Settings.BlockedRetryTicks*(1L<<Math.Min(4,failures-1))));
        _state=_state with{EconomyMemory=memory with{Rejected=[..rows.Where(r=>r.Key!=key).TakeLast(63),new(key,failures,checked(tick+delay),reason)]}};
    }
    private bool EconomicFeasible(Candidate c,ControllerObservation o)
    {
        if(!UsesEconomicPlanning)return true;
        var blocked=_state.EconomyMemory!.Rejected;if(!blocked.IsDefault&&c.Steps.Any(s=>blocked.Any(r=>r.Key==Intent(s.Action)&&r.RetryTick>o.Tick)))return false;
        if(c.Steps.Any(s=>s.Action.Kind==ActionKind.Transfer&&(o.Fleet?.Vehicles.Any(v=>v.Vehicle.CargoInventory==s.Action.Source&&v.State=="ServiceReserved")==true)))return false;
        if(o.Services?.OwnOrders.Any(order=>order.Customer==Actor&&order.Active&&c.Steps.Any(step=>
            step.Action.Kind==ActionKind.CollectPurchase&&step.Action.PurchaseId==order.Quote.Terms.PurchaseId&&step.Action.PurchaseId!=0||
            step.Action.Kind==ActionKind.DeliverContract&&step.Action.ContractId==order.Quote.Terms.ContractId&&step.Action.ContractId!=0))==true)return false;
        if(c.Goal=="SupplyProduction"&&o.Services is {} services)
        {
            var transfer=c.Steps.LastOrDefault(s=>s.Action.Kind==ActionKind.Transfer)?.Action;
            if(transfer is not null&&services.OwnOrders.Any(order=>order.Customer==Actor&&order.Active&&order.Quote.Terms.Destination==transfer.Destination&&order.Quote.Terms.ItemId==transfer.ItemId))return false;
        }
        if(c.Steps.Any(step=>step.Action.Kind==ActionKind.BuyEquipment&&!o.Resources.Any(r=>r.Position==step.Action.Target&&r.Owner==Actor&&!r.Public)))return false;
        if(c.Goal=="ResumeProduction")
        {
            var action=c.Steps.Last().Action;var parent=o.Enterprises.SingleOrDefault(e=>e.Id==action.Enterprise);
            if(parent is not null)
            {var line=ProductionWorkplaces(parent).FirstOrDefault(l=>action.WorkshopId==0?l.Workshop==0||l.Workshop==parent.Id.Value:l.Workshop==action.WorkshopId);if(line.Unit is not null&&!ProductionWorthRunning(o,line.Unit,line.Group))return false;}
        }
        var startup=StartupIndustry(o);long reserve=StartupReserve(o,startup);
        if(startup is not null)
        {
            if(c.Goal=="BuyImport"&&!o.Enterprises.SelectMany(ProductionWorkplaces).Any(l=>o.Map.Recipes.Single(r=>r.Id==l.Unit.RecipeId).Ingredients.Any(i=>c.Reason.EndsWith(":"+i.ItemId,StringComparison.Ordinal)))&&(!startup.Levels[0].Materials.Any(m=>c.Reason.EndsWith(":"+m.ItemId,StringComparison.Ordinal))||o.Account.AvailableMinor<reserve))return false;
            if(c.Goal=="BuildProduction"&&o.Account.AvailableMinor<reserve)return false;
        }
        if(c.Goal is "BuyExtraction" or "BuyLogistics" or "OutfitWorkshop" or "ExpandEnterprise" or "BuildShop")
        {
            if(o.Account.AvailableMinor<_state.Settings.CashReserveMinor+ExecutionReserve(o))return false;
            if(o.Enterprises.Any(e=>ProductionWorkplaces(e).Any(l=>l.Unit.CompletedBatches==0||l.Unit.Enabled&&l.Unit.BatchId==0&&(l.Unit.Staff==0||l.Unit.ReasonCode is "NoInputs" or "NoOutputSpace"))))return false;
        }
        return true;
    }
    private bool ProductionWorthRunning(ControllerObservation o,EnterpriseView e,EmploymentGroupId groupId)
    {
        var r=o.Map.Recipes.Single(r=>r.Id==e.RecipeId);if(o.OwnDeliveryContracts.Any(d=>d.Supplier==Actor&&d.ClosedTick is null&&d.ItemId==r.OutputItem&&d.UnprovidedQuantity>0))return true;
        if(o.Enterprises.Where(other=>other.Id!=e.Id).SelectMany(ProductionWorkplaces).Any(l=>o.Map.Recipes.Single(recipe=>recipe.Id==l.Unit.RecipeId).Uses(r.OutputItem)))return true;
        var offer=BestOffer(o,r.OutputItem,r.OutputQuantity,Inventory(o,e.OutputId).Position);if(o.Population==0)return false;
        if(offer is null&&o.Inventories.Any(i=>i.Stock.Any(s=>s.ItemId==r.OutputItem&&s.Quantity>0)))return false;
        long price=offer?.UnitPriceMinor??o.CapitalPrices.Where(p=>p.ItemId==r.OutputItem).Select(p=>p.UnitPriceMinor).DefaultIfEmpty(0).Max();if(price<=0)return false;
        var group=o.EmploymentGroups.Single(g=>g.Id==groupId);long effectiveWage=group.WageMinor>0?group.WageMinor:_state.Settings.BaseWageMinor;int staff=Math.Max(1,group.Headcount>0?group.Headcount:HiringEconomics.ExpectedStaff(o,group,effectiveWage));long ticks=o.Economics?.Workshops is not null?(r.FullStaffTicks*100+WorkshopRules.Rate(staff)-1)/Math.Max(1,WorkshopRules.Rate(staff)):r.FullStaffTicks;
        long inputs=r.Ingredients.Sum(i=>i.Quantity*(o.Map.ResourceNodes.Any(n=>n.ItemId==i.ItemId)||o.Enterprises.SelectMany(ProductionWorkplaces).Any(l=>o.Map.Recipes.Single(recipe=>recipe.Id==l.Unit.RecipeId).OutputItem==i.ItemId)?BasePrice(o,i.ItemId):o.ProductionContent?.Rules.Imports.FirstOrDefault(stock=>stock.ItemId==i.ItemId)?.UnitPriceMinor??BasePrice(o,i.ItemId)));
        return r.OutputQuantity*price>inputs+staff*effectiveWage*ticks+(o.Economics?.Workshops is {} workshops?workshops.ServiceMinorPerFullTick*ticks:r.EnergyPerBatchMinor);
    }
    private bool CanBootstrapIndustry(ControllerObservation o,ControllerBuildingType type)
    {
        if(o.Population==0)return false;
        return type.Levels[0].UnlockedRecipes.Select(id=>o.Map.Recipes.Single(r=>r.Id==id)).Any(r=>
            r.Ingredients.All(i=>o.Map.ResourceNodes.Any(n=>n.ItemId==i.ItemId&&AvailableResource(o,n))||o.ProductionContent?.Imports.Any(stock=>stock.ItemId==i.ItemId&&stock.Remaining>=i.Quantity)==true||o.Inventories.Any(inv=>Free(inv,i.ItemId)>=i.Quantity))&&
            r.OutputQuantity*o.CapitalPrices.Where(p=>p.ItemId==r.OutputItem).Select(p=>p.UnitPriceMinor).DefaultIfEmpty(0).Max()>RecipeInputValue(o,r)+5*_state.Settings.BaseWageMinor*r.FullStaffTicks+r.EnergyPerBatchMinor);
    }
    private ControllerBuildingType? StartupIndustry(ControllerObservation o)
    {
        if(_state.EconomyMemory?.ConstructionIndustry is {Length:>0} planned&&!o.Buildings.Any(b=>b.TypeId==planned))return o.BuildingTypes.FirstOrDefault(t=>t.Id==planned);
        var candidates=o.BuildingTypes.Where(t=>t.Kind==BuildingKind.Production&&CanBootstrapIndustry(o,t));
        if(o.Enterprises.IsEmpty)
        {
            var preferred=candidates.Where(t=>t.Levels[0].UnlockedRecipes.Any(id=>o.Map.Recipes.Single(r=>r.Id==id).Uses(_state.Settings.PreferredInput))).ToArray();
            if(preferred.Length>0)candidates=preferred;
        }
        else
        {
            if(o.Enterprises.Any(e=>ProductionWorkplaces(e).Any(l=>l.Unit.CompletedBatches==0||l.Unit.Staff==0||l.Unit.ReasonCode is "NoInputs" or "NoOutputSpace")))return null;
            var outputs=o.Enterprises.SelectMany(ProductionWorkplaces).Select(l=>o.Map.Recipes.Single(r=>r.Id==l.Unit.RecipeId).OutputItem).ToHashSet();
            var inputs=o.Enterprises.SelectMany(ProductionWorkplaces).SelectMany(l=>o.Map.Recipes.Single(r=>r.Id==l.Unit.RecipeId).Ingredients).Select(i=>i.ItemId).ToHashSet();
            candidates=candidates.Where(t=>!o.Buildings.Any(b=>b.TypeId==t.Id)&&t.Levels[0].UnlockedRecipes.Any(id=>{var r=o.Map.Recipes.Single(r=>r.Id==id);return inputs.Contains(r.OutputItem)||r.Ingredients.Any(i=>outputs.Contains(i.ItemId));}));
        }
        return candidates.OrderBy(t=>t.Levels[0].PriceMinor+t.Levels[0].Materials.Sum(m=>m.Quantity*BasePrice(o,m.ItemId))).ThenBy(t=>t.Id,StringComparer.Ordinal).FirstOrDefault();
    }
    private long StartupReserve(ControllerObservation o,ControllerBuildingType? type)
    {
        if(type is null)return _state.Settings.CashReserveMinor;var level=type.Levels[0];
        long land=o.Plots.Where(p=>p.OwnerId==Actor&&p.BuildingId is null&&o.Development?.BuildAnchors.Contains(p.Position)==true).Any()?0:o.Plots.Where(p=>!p.IsCorridor&&!p.IsShopSite).Select(p=>p.PriceMinor).DefaultIfEmpty(0).Min();
        long imports=level.Materials.Sum(m=>Math.Max(0,m.Quantity-o.Inventories.Sum(i=>Free(i,m.ItemId)))*(o.ProductionContent?.Rules.Imports.FirstOrDefault(i=>i.ItemId==m.ItemId)?.UnitPriceMinor??0));
        return checked(level.PriceMinor+land+imports+_state.Settings.CashReserveMinor+5*_state.Settings.BaseWageMinor*_state.Settings.WageReviewTicks);
    }
}
