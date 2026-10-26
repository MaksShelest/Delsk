namespace Simulation.Core;

public static partial class ScenarioValidation
{
    private static void ValidateProductionContent(ScenarioDefinition s)
    {
        if(s.Economy?.ProductionContent is not {} c)return;
        if(s.Economy.Development is null||s.Economy.Workshops is null||s.CapitalDemand is null||c.Imports.IsDefaultOrEmpty||c.Versions.IsDefaultOrEmpty||c.Modernizations.IsDefaultOrEmpty)
            throw new ArgumentException("Production content requires development/workshops/terminal");
        if(c.Imports.Select(i=>i.ItemId).Distinct().Count()!=c.Imports.Length||c.Imports.Any(i=>!s.Items.Any(item=>item.Id==i.ItemId)||i.Package<=0||i.Package>1000000||i.RefreshTicks<1||i.UnitPriceMinor<=0))
            throw new ArgumentException("Invalid finite import supply");
        if(c.Versions.Select(v=>v.RecipeId).Distinct().Count()!=c.Versions.Length||c.Versions.Length!=s.Recipes.Length)throw new ArgumentException("Invalid recipe version mapping");
        foreach(var v in c.Versions)
        {
            var basis=s.Recipes.SingleOrDefault(r=>r.Id==v.BaseRecipe);var recipe=s.Recipes.SingleOrDefault(r=>r.Id==v.RecipeId);
            if(basis is null||recipe is null||v.Version is <1 or >3||v.TechnologyLevel is <1 or >5||v.Version==2&&v.TechnologyLevel!=4||v.Version==3&&v.TechnologyLevel!=5)throw new ArgumentException("Invalid recipe version");
            int input=v.Version==2?4:v.Version==3?2:1,output=v.Version==2?5:v.Version==3?3:1;
            long ticks=v.Version==1?basis.FullStaffTicks:checked((checked(basis.FullStaffTicks*(v.Version==2?9:5))+1)/2);
            if(recipe.OutputItem!=basis.OutputItem||recipe.OutputQuantity!=checked(basis.OutputQuantity*output)||recipe.FullStaffTicks!=ticks||
                !recipe.Ingredients.SequenceEqual(basis.Ingredients.Select(m=>m with{Quantity=checked(m.Quantity*input)})))throw new ArgumentException("Invalid recipe version formula");
        }
        if(!c.Modernizations.OrderBy(m=>m.Version).Select(m=>m.Version).SequenceEqual(new[]{2,3})||c.Modernizations.Any(m=>m.PriceMinor<=0||m.Ticks<=0||m.Materials.IsDefaultOrEmpty||m.Materials.Any(i=>i.Quantity<=0||!s.Items.Any(item=>item.Id==i.ItemId))||m.Materials.DistinctBy(i=>i.ItemId).Count()!=m.Materials.Length))throw new ArgumentException("Invalid workshop modernization costs");
        foreach(var type in s.BuildingTypes!.Where(t=>t.Kind==BuildingKind.Production))
            if(type.Levels[0].Materials.Sum(m=>checked(m.Quantity*s.Items.Single(i=>i.Id==m.ItemId).UnitVolume))>s.Economy.Storage!.GroundCapacity)
                throw new ArgumentException("Initial construction materials do not fit bare ground");
    }
}
