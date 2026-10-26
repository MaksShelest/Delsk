using Simulation.Core;
using System.Collections.Immutable;

namespace Simulation.Hosting;

public static class VNextProductionProfile
{
    public const string ProfileId="vnext-d3-production-1";
    public static EconomyProfile Create(EconomyProfile basis,VNextBalance balance)
    {
        var d2=VNextDevelopmentProfile.Create(basis,balance);var source=d2.VNext!;
        var links=balance.Recipes.SelectMany(r=>balance.RecipeVersions.Select(v=>new RecipeVersionLink(r.Id,
            v.Version==1?r.Id:r.Id+"_v"+v.Version,checked((int)v.Version),checked((int)Math.Max(r.UnlockLevel,v.UnlockLevel))))).ToImmutableArray();
        var recipes=links.Select(link=>
        {
            var original=d2.World.Recipes.Single(r=>r.Id==link.BaseRecipe);var v=balance.RecipeVersions.Single(v=>v.Version==link.Version);
            long ticks=checked((checked(original.FullStaffTicks*v.WorkMultiplierNum)+v.WorkMultiplierDen-1)/v.WorkMultiplierDen);
            return original with{Id=link.RecipeId,Inputs=[..original.Ingredients.Select(m=>m with{Quantity=checked(m.Quantity*v.InputMultiplier)})],
                OutputQuantity=checked(original.OutputQuantity*v.OutputMultiplier),FullStaffTicks=ticks,EnergyPerBatchMinor=checked(ticks*balance.Recipes.Single(r=>r.Id==link.BaseRecipe).ServiceMinorPerWorkTick)};
        }).ToArray();
        var industries=balance.Industries.Select(i=>new DevelopmentIndustry(i.Id,[..links.Where(l=>balance.Recipes.Single(r=>r.Id==l.BaseRecipe).Industry==i.Id).Select(l=>
            new IndustryProduct(l.RecipeId,recipes.Single(r=>r.Id==l.RecipeId).OutputItem,recipes.Single(r=>r.Id==l.RecipeId).OutputQuantity,
                balance.Items.Single(item=>item.Id==recipes.Single(r=>r.Id==l.RecipeId).OutputItem).XpPerUnit,l.TechnologyLevel))])).ToImmutableArray();
        var types=d2.World.BuildingTypes.Select(t=>t.Kind!=BuildingKind.Production?t:t with{Levels=t.Levels.Select(level=>
        {
            var unlocked=industries.Single(i=>i.Id==t.Id).Products.Where(p=>p.TechnologyLevel<=level.Level).Select(p=>p.RecipeId).ToArray();
            return level with{AdditionalRecipeIds=[..unlocked.Where(id=>id!=level.RecipeId)]};
        }).ToArray()}).ToArray();
        var g=balance.EconomyGlobals;
        var imports=balance.Items.Where(i=>i.Source=="import"||i.Id is "steel" or "glass" or "asphalt").Select(i=>new ImportSupply(i.Id,
            i.Id switch{"steel"=>g.BootstrapSteel,"glass"=>g.BootstrapGlass,"asphalt"=>g.BootstrapAsphalt,_=>g.ImportStockPerAdditivePer4Participants},
            i.Id=="asphalt"?g.BootstrapAsphaltRefreshTicks:g.ImportRefreshTicks,checked((checked(i.ReferencePriceMinor*g.ImportMarkupBp)+9999)/10000))).ToImmutableArray();
        var modernizations=balance.WorkshopRecipeModernization.Select(m=>new WorkshopModernization(checked((int)m.Version),m.PriceMinor,[..m.Materials.Select(p=>new MaterialCost(p.Key,p.Value))],m.Ticks)).ToImmutableArray();
        var equipment=balance.Resources.Keys.Select(item=>new EquipmentTypeDefinition("extract_"+item,item,
            [new(1,15000,2,2,70,2,2,balance.Rules.WageMinor),new(2,60000,6,2,200,5,5,balance.Rules.WageMinor)])).ToArray();
        var profile=d2 with{Id=ProfileId,VNext=null,World=d2.World with{Recipes=recipes,BuildingTypes=types,EquipmentTypes=equipment,
            Rules=d2.World.Rules with{ProductionContent=new(imports,links,modernizations),Development=d2.World.Rules.Development! with{Industries=industries}}}};
        return profile with{VNext=source with{ProjectionVersion="production-projection-1",ProjectionPayloadSha256=VNextBalanceReader.Digest(VNextSupportedProfile.Serialize(profile)),
            ActiveCapabilities=[..source.ActiveCapabilities,"full-six-industry-production","finite-paid-import","resource-equipment","paid-local-recipe-versions"],
            Limitations=["D3 production. Full fleet/roads/services/invoices remain later packages.","Import is purchased at the terminal and physically collected; purchased stock does not multiply or expire.","Long-run balance and device performance require separate measurements."]}};
    }
}
