using System.Collections.Immutable;

namespace Simulation.Hosting;

public sealed record VNextCoverage(string Section, string Structural, string Semantic, string Runtime);
public sealed record VNextBatchAnalysis(string RecipeId, long Version, long AvailableLevel, long InputVolume, long OutputVolume,
    long WorkTicks, long InputValueMinor, long OutputValueMinor, long LaborMinor, long ServiceMinor, long DirectMarginMinor);
public sealed record VNextConstructionAnalysis(string IndustryId, long MaterialVolume, long LandMinor, long WorkMinor, long MaterialsReferenceMinor);
public sealed record VNextGraphStep(string IndustryId, long Level, ImmutableSortedDictionary<string, long> FiniteSeedRemaining);
public sealed record VNextBootstrapAnalysis(ImmutableSortedDictionary<string, long> InitialFiniteSeed,
    ImmutableArray<VNextGraphStep> Witness, ImmutableArray<string> RenewableItems, int ExploredStates, bool AllRequiredReachable);
public sealed record VNextStartAnalysis(long SteelPackage, long GlassPackage, long ImportRefreshTicks, long MaximumPackages,
    long ImportMarkupBp, long SteelUnitPriceMinor, long GlassUnitPriceMinor, long SawmillLandMinor, long SawmillWorksMinor,
    long SawmillWoodQuantity, long SawmillSteelQuantity, long SawmillFullReferenceMinor, long CashWithManuallyHarvestedWoodMinor,
    long StartingCashMinor, long AdditionalCashMinor, long WoodSpotUnitsAtReferencePrice, string Limits);
public sealed record VNextBalanceAnalysis(ImmutableArray<VNextCatalogDiagnostic> Errors, ImmutableArray<VNextCatalogDiagnostic> Warnings,
    ImmutableArray<VNextCoverage> Coverage, ImmutableArray<VNextConstructionAnalysis> Construction,
    ImmutableArray<VNextBatchAnalysis> Batches, VNextBootstrapAnalysis? Bootstrap, VNextStartAnalysis? Startup)
{
    public bool IsValid => Errors.IsEmpty;
}

/// <summary>Static feasibility only, not a second executor, economic forecast or dynamic balance proof.</summary>
public static class VNextBalanceAnalyzer
{
    public static VNextBalanceAnalysis Analyze(VNextBalance b)
    {
        var errors = ImmutableArray.CreateBuilder<VNextCatalogDiagnostic>();
        var warnings = ImmutableArray.CreateBuilder<VNextCatalogDiagnostic>();
        var construction = ImmutableArray.CreateBuilder<VNextConstructionAnalysis>();
        var batches = ImmutableArray.CreateBuilder<VNextBatchAnalysis>();
        VNextBootstrapAnalysis? graph = null; VNextStartAnalysis? startup=null;
        void Check(bool ok, string code, string path, string message) { if (!ok) errors.Add(new(code, path, message)); }
        void Warn(string code, string path, string message) => warnings.Add(new(code, path, message));
        var tiers = b.EnterpriseTiers.OrderBy(t => t.Level).ToArray();
        var items = b.Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        long Volume(IEnumerable<KeyValuePair<string, long>> q) => q.Aggregate(0L, (n, x) => checked(n + checked(x.Value * items[x.Key].UnitVolume)));
        long Value(IEnumerable<KeyValuePair<string, long>> q) => q.Aggregate(0L, (n, x) => checked(n + checked(x.Value * items[x.Key].ReferencePriceMinor)));
        try
        {
            Check(tiers.Select(t => t.Level).SequenceEqual(new long[] { 1, 2, 3, 4, 5 }), "TierSequence", "$.enterprise_tiers", "Exactly five ordered levels 1..5 are required.");
            Check(tiers.Length == 5 && tiers[0].Xp == 0 && tiers[0].Cells == 1 && tiers[0].Workshops == 1 && tiers[0].PriceMinor == 0 && tiers[0].Ticks == 0 && tiers[0].Materials.Count == 0,
                "BaseTier", "$.enterprise_tiers", "Tier I is the initial industry, with no second expansion charge.");
            for (int i = 1; i < tiers.Length; i++)
            {
                var a = tiers[i - 1]; var t = tiers[i];
                Check(t.Xp > a.Xp && t.Cells >= a.Cells && t.Workshops > a.Workshops && t.InputCapacity > a.InputCapacity && t.OutputCapacity > a.OutputCapacity && t.Workshops <= t.Cells,
                    "TierMonotonicity", "$.enterprise_tiers[" + i + "]", "XP/capacity/workshops must increase within the six-cell contour.");
                Check(Volume(t.Materials) <= a.InputCapacity, "ExpansionCapacity", "$.enterprise_tiers[" + i + "].materials", "Expansion materials exceed previous tier input capacity.");
                _ = checked(t.Workshops * b.Rules.StaffPerWorkshop * b.Rules.WageMinor * Math.Max(1,t.Ticks));
            }
            var warehouses = b.Warehouses.OrderBy(w => w.Level).ToArray();
            Check(warehouses.Select(w => w.Level).SequenceEqual(new long[]{1,2,3}) && warehouses.Select(w => w.Capacity).SequenceEqual(new long[]{200,300,400}),
                "WarehouseSequence", "$.warehouses", "Warehouse levels/capacities must be 1..3 / 200,300,400.");
            Check(b.Rules.WarehouseCapacities.SequenceEqual(warehouses.Select(w => w.Capacity)), "ConflictingSetting", "$.rules.warehouse_capacities", "Duplicated warehouse capacities differ.");
            Check(b.Rules.MinBots >= 3 && b.Rules.MaxBots <= 10 && b.Rules.MinBots <= b.Rules.DefaultBots && b.Rules.DefaultBots <= b.Rules.MaxBots,
                "BotRange", "$.rules", "Expected 3..10 bots and default within range; metadata only.");
            Check(b.Rules.StaffRateBp.Length == b.Rules.StaffPerWorkshop + 1 && b.Rules.StaffRateBp[0] == 0 && b.Rules.StaffRateBp[^1] == 10000 &&
                b.Rules.StaffRateBp.Zip(b.Rules.StaffRateBp.Skip(1)).All(x => x.First < x.Second), "StaffCurve", "$.rules.staff_rate_bp", "Expected increasing rates for 0..full staff, from 0 to 10000.");
            Check(b.Rules.OverflowWarningsRemaining.All(n=>n>0 && n<b.Rules.OverflowGraceTicks) && b.Rules.OverflowWarningsRemaining.Distinct().Count()==b.Rules.OverflowWarningsRemaining.Length,
                "OverflowWarnings", "$.rules.overflow_warnings_remaining", "Warning offsets must be unique and inside the grace period.");
            Check(b.Rules.RoadSpeedCaps.Length == 3 && b.Rules.RoadSpeedCaps.All(n=>n>0) && b.Rules.RoadSpeedCaps.Zip(b.Rules.RoadSpeedCaps.Skip(1)).All(x=>x.First<x.Second),
                "RoadSpeeds", "$.rules.road_speed_caps", "Three increasing positive road caps are required.");
            Check(b.Rules.RoadStopReleaseTicks >= b.Rules.RoadStopWarningTicks && b.Rules.ShopMinDisplayTicks >= b.Rules.ShopUpdateTicks,
                "TimingOrder", "$.rules", "Release/display timing precedes its prerequisite.");
            Check(b.Resources.Keys.Order(StringComparer.Ordinal).SequenceEqual(b.Items.Where(i=>i.Source=="resource").Select(i=>i.Id).Order(StringComparer.Ordinal)),
                "ResourceCoverage", "$.resources", "Every resource item requires its own profile, and vice versa.");
            foreach(var (id,r) in b.Resources)
                Check(checked(r.PublicStockPerCell*2)==r.PrivateStockPerCell && checked(r.RegenOneEveryTicks*3)==r.PublicRegenOneEveryTicks,
                    "PublicResourceRatio", "$.resources."+id, "Public stock is half, regen interval is three times private.");
            Check(b.AdditionalWorkshopOutfits.Keys.Order(StringComparer.Ordinal).SequenceEqual(b.Industries.Select(i=>i.Id).Order(StringComparer.Ordinal)),
                "OutfitCoverage", "$.additional_workshop_outfits", "Every industry requires a distinct outfit.");
            Check(b.RecipeVersions.OrderBy(v=>v.Version).Select(v=>v.Version).SequenceEqual(new long[]{1,2,3}), "RecipeVersions", "$.recipe_versions", "Expected versions 1,2,3.");
            Check(b.WorkshopRecipeModernization.Select(v=>v.Version).Order().SequenceEqual(b.RecipeVersions.Where(v=>v.Version>1).Select(v=>v.Version).Order()),
                "ModernizationCoverage", "$.workshop_recipe_modernization", "Each upgraded version requires its modernization data.");
            foreach(var v in b.RecipeVersions)
                Check(v.UnlockLevel==(v.Version==1?1:v.Version+2) && (v.Version!=1 || v.InputMultiplier==1 && v.OutputMultiplier==1 && v.WorkMultiplierNum==v.WorkMultiplierDen),
                    "VersionUnlock", "$.recipe_versions", "V1 identity; V2 at IV, V3 at V.");
            Check(b.LoadingPlatforms.Select(p=>p.Level).Order().SequenceEqual(new long[]{1,2,3}) && b.RoadProjects.Select(p=>p.Level).Order().SequenceEqual(new long[]{1,2,3}),
                "InfrastructureLevels", "$", "Loading platforms and road projects require levels 1..3.");
            foreach(var p in b.LoadingPlatforms)
                Check(p.Level!=1 || p.PriceMinor==0 && p.Ticks==0 && p.Materials.IsEmpty, "BasePlatform", "$.loading_platforms", "Base manual capability has no fabricated installation cost.");
            foreach (var industry in b.Industries)
            {
                long volume = Volume(industry.Materials);
                construction.Add(new(industry.Id, volume, b.Rules.CellPriceMinor, industry.ConstructionPriceMinor, Value(industry.Materials)));
                Check(volume <= b.Rules.BareCellCapacity, "InitialMaterialsCapacity", "$.industries." + industry.Id, "Initial material set exceeds bare cell capacity.");
            }
            foreach (var r in b.Recipes)
            foreach (var v in b.RecipeVersions.OrderBy(v=>v.Version))
            {
                long level = Math.Max(r.UnlockLevel, v.UnlockLevel);
                var tier = tiers.SingleOrDefault(t => t.Level == level);
                Check(tier is not null, "MissingTier", "$.recipes."+r.Id, "Recipe version has no available tier.");
                if(tier is null) continue;
                long inputVolume = checked(Volume(r.Inputs) * v.InputMultiplier);
                long outputVolume = checked(items[r.Output].UnitVolume * checked(r.OutputQuantity * v.OutputMultiplier));
                _=checked(items[r.Output].XpPerUnit*checked(r.OutputQuantity*v.OutputMultiplier));
                Check(inputVolume <= tier.InputCapacity && outputVolume <= tier.OutputCapacity, "BatchCapacity", "$.recipes."+r.Id+".v"+v.Version, "Derived batch exceeds available input/output capacity.");
                long product=checked(r.WorkTicksFullStaff*v.WorkMultiplierNum);
                long ticks=checked(product/v.WorkMultiplierDen+(product%v.WorkMultiplierDen==0?0:1));
                long inputs = checked(Value(r.Inputs) * v.InputMultiplier);
                long output = checked(items[r.Output].ReferencePriceMinor * checked(r.OutputQuantity * v.OutputMultiplier));
                long labor = checked(ticks * checked(b.Rules.StaffPerWorkshop * b.Rules.WageMinor));
                long service = checked(ticks * r.ServiceMinorPerWorkTick);
                long margin=checked(output-checked(inputs+checked(labor+service)));
                batches.Add(new(r.Id,v.Version,level,inputVolume,outputVolume,ticks,inputs,output,labor,service,margin));
                if(margin<=0)Warn("NonpositiveDirectMargin","$.recipes."+r.Id+".v"+v.Version,"Reference output does not cover inputs, full staff labor and service; balance was not changed.");
            }
            if(errors.Count==0)
            {
                graph = Reachability(b);
                Check(graph.AllRequiredReachable,graph.ExploredStates>=30000?"GraphAnalysisLimit":"UnreachableDependencies","$","No finite-seed construction/level witness found. Bootstrap is not an unlimited source; see graph coverage/limits.");
                if(graph.AllRequiredReachable)
                {
                    var renewable=graph.RenewableItems.ToHashSet(StringComparer.Ordinal);
                    foreach(var item in b.Items.Where(i=>i.Source=="production"))Check(renewable.Contains(item.Id),"UnreachableItem","$.items."+item.Id,"No grounded renewable recipe chain.");
                    foreach(var set in b.AdditionalWorkshopOutfits.Values.Select(o=>o.Materials).Concat(b.WorkshopRecipeModernization.Select(m=>m.Materials)).Concat(b.Warehouses.Select(w=>w.Materials)).Concat(b.LoadingPlatforms.Select(p=>p.Materials)).Concat(b.VehicleHandlingKits.Select(k=>k.Materials)).Concat(b.RoadProjects.Select(r=>r.Materials)))
                        foreach(string id in set.Keys)Check(renewable.Contains(id),"UnreachableMaterial","$","Infrastructure material cannot be replenished: "+id);
                }
            }
            var g=b.EconomyGlobals;
            foreach(string id in new[]{"steel","glass","asphalt"})
                Check(items.ContainsKey(id),"BootstrapItem","$.economy_globals","Declared bootstrap requires ItemId "+id);
            _=checked(g.StartTreasuryMinorPer4Participants*3); _=checked(g.CapitalBootstrapBudgetMinorPer4Participants*3);
            _=checked(g.ImportStockPerAdditivePer4Participants*6); // 11 participants, two capped packages.
            _=checked(g.StartPopulationPerParticipant*11);_=checked(g.MaxPopulationPerParticipant*11);
            long importedValue=0;
            foreach(var i in b.Items.Where(i=>i.Source=="import" || i.Id is "steel" or "glass" or "asphalt"))
            {
                long perPackage=i.Id switch {"steel"=>g.BootstrapSteel,"glass"=>g.BootstrapGlass,"asphalt"=>g.BootstrapAsphalt,_=>g.ImportStockPerAdditivePer4Participants};
                long maximum=checked(perPackage*6);_=checked(maximum*i.UnitVolume);
                importedValue=checked(importedValue+checked(checked(maximum*i.ReferencePriceMinor)*g.ImportMarkupBp)/10000);
            }
            foreach(var i in b.Items)_=checked(i.ReferencePriceMinor*g.ImportMarkupBp);
            if(items.ContainsKey("steel") && items.ContainsKey("glass") && items.ContainsKey("wood") && b.Industries.Any(i=>i.Id=="woodworking"))
            {
                var sawmill=b.Industries.Single(i=>i.Id=="woodworking");
                long steelProduct=checked(items["steel"].ReferencePriceMinor*g.ImportMarkupBp),glassProduct=checked(items["glass"].ReferencePriceMinor*g.ImportMarkupBp);
                // Conservative cash ceiling in minor units; no float and rounding assumption is explicit.
                long steel=checked(steelProduct/10000+(steelProduct%10000==0?0:1)), glass=checked(glassProduct/10000+(glassProduct%10000==0?0:1));
                long woodQ=sawmill.Materials.GetValueOrDefault("wood"),steelQ=sawmill.Materials.GetValueOrDefault("steel");
                long cash=checked(b.Rules.CellPriceMinor+sawmill.ConstructionPriceMinor+checked(steel*steelQ));
                long full=checked(cash+checked(woodQ*items["wood"].ReferencePriceMinor));
                long shortage=Math.Max(0,checked(cash-b.Rules.StartCashMinor));long price=items["wood"].ReferencePriceMinor;
                startup=new(g.BootstrapSteel,g.BootstrapGlass,g.ImportRefreshTicks,2,g.ImportMarkupBp,steel,glass,b.Rules.CellPriceMinor,sawmill.ConstructionPriceMinor,
                    woodQ,steelQ,full,cash,b.Rules.StartCashMinor,shortage,checked(shortage/price+(shortage%price==0?0:1)),
                    "Per four-participant package; finite stock and refresh, two-package cap from specification §16. Steel/glass purchases cost money, not free stock. One-package witness is conservative. Import quote rounded up to minor only in this static estimate; full B1 quotes are exact. Zero-cash recovery uses public harvest and funded spot sales without a factory. No transport/time/competition guarantee.");
            }
        }
        catch(OverflowException) {errors.Add(new("Overflow","$","Semantic derived cost/volume/work arithmetic exceeds Int64."));}
        Warn("StaticOnly","$","Reference margins exclude logistics, idle time, land/construction financing, taxes/invoices and realized demand. No balanced=true claim.");
        Warn("FiniteBootstrap","$.economy_globals","Reachability spends ONE finite steel/glass/asphalt package; recurring imports have finite stock, refresh and two-package cap. This is not a scheduling/fairness proof for all participants.");
        Warn("MoneyRecovery","$.rules.start_cash_minor","Starting 200 coins is not a free factory. Public manual harvest and funded spot sales require no existing factory; actual time/competition are unmeasured.");
        Warn("RuntimeSubset","$","Five tiers, XP, staff curve, imports/bootstrap inventory, vehicles, invoices, roads, services and workshop parallelism remain metadata; see mapping.");
        return new(errors.ToImmutable(),warnings.ToImmutable(),Coverage(),construction.ToImmutable(),batches.ToImmutable(),graph,startup);
    }

    // A bounded existence proof: finite bootstrap is shared and consumed across construction;
    // only recipes with ALL renewable inputs make their output renewable. A bare SCC is not supply.
    private static VNextBootstrapAnalysis Reachability(VNextBalance b)
    {
        var ids=b.Industries.Select(i=>i.Id).ToArray();
        var initial=ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal,new[]{
            new KeyValuePair<string,long>("steel",b.EconomyGlobals.BootstrapSteel),new("glass",b.EconomyGlobals.BootstrapGlass),new("asphalt",b.EconomyGlobals.BootstrapAsphalt)});
        var seen=new HashSet<string>(StringComparer.Ordinal); int explored=0;
        ImmutableArray<VNextGraphStep> witness=[]; ImmutableArray<string> final=[];
        HashSet<string> Renewable(int[] levels)
        {
            var have=b.Items.Where(i=>i.Source=="import" || i.Source=="resource" && b.Resources.ContainsKey(i.Id)).Select(i=>i.Id).ToHashSet(StringComparer.Ordinal);
            bool changed;
            do {changed=false;foreach(var r in b.Recipes)if(levels[Array.IndexOf(ids,r.Industry)]>=r.UnlockLevel && r.Inputs.Keys.All(have.Contains))changed|=have.Add(r.Output);}while(changed);
            return have;
        }
        bool Search(int[] levels,ImmutableSortedDictionary<string,long> stock,ImmutableArray<VNextGraphStep> steps)
        {
            if(explored>=30000)return false;
            string key=string.Join(',',levels)+":"+string.Join(',',stock.Values);
            if(!seen.Add(key))return false;
            ++explored;
            var have=Renewable(levels);
            if(levels.All(n=>n==5)) {witness=steps;final=[..have.Order(StringComparer.Ordinal)];return true;}
            for(int i=0;i<ids.Length;i++)
            {
                if(explored>=30000)break;
                int next=levels[i]+1;if(next>5)continue;
                // XP is a future mechanic; static unlock requires a repeatable positive-XP recipe.
                if(next>1 && !b.Recipes.Any(r=>r.Industry==ids[i] && r.UnlockLevel<=levels[i] && r.Inputs.Keys.All(have.Contains) && b.Items.Single(x=>x.Id==r.Output).XpPerUnit>0))continue;
                var materials=next==1?b.Industries[i].Materials:b.EnterpriseTiers.Single(t=>t.Level==next).Materials;
                if(materials.Any(x=>!have.Contains(x.Key) && stock.GetValueOrDefault(x.Key)<x.Value))continue;
                var remaining=stock;
                foreach(var (id,q) in materials)if(!have.Contains(id))remaining=remaining.SetItem(id,checked(remaining.GetValueOrDefault(id)-q));
                var updated=(int[])levels.Clone();updated[i]=next;
                if(Search(updated,remaining,steps.Add(new(ids[i],next,remaining))))return true;
            }
            return false;
        }
        bool reached=Search(new int[ids.Length],initial,[]);
        return new(initial,witness,final,explored,reached);
    }
    public static ImmutableArray<VNextCoverage> Coverage() => [
        new("rules","all fields/types/ranges","units, duplicate settings, timing order, staff/road curves; no future scheduler proof","selected existing scalar rules only"),
        new("items","A1 strict","references, grounded source and checked value/volume","34 ItemDefinition; XP/source metadata"),
        new("recipes","A1 strict 1..8 inputs","69 exact V1/V2/V3 derived batches, capacity, margin, reachability","23 V1 RecipeDefinition; one executor/linear staff, no unlock/XP"),
        new("industries","A1 strict","initial material volume/cost and finite-seed construction witness","metadata; no legacy rename"),
        new("enterprise_tiers","all fields/refs/checked","five levels, XP/capacity/workshops/2x3/staff, previous input, unlock witness","metadata D"),
        new("warehouses","all fields/refs/checked","200/300/400 and duplicate settings; materials renewable","metadata C"),
        new("recipe_versions","all fields/exact rational","ceil integer work, derived input/output/capacity/margin","metadata D"),
        new("resources","all fields/refs","item coverage, public half stock/triple regen, renewable source","eight ResourceSiteRules profiles; uniform manual duration only"),
        new("additional_workshop_outfits","all fields/refs/checked","industry coverage and renewable materials; no future installation proof","metadata D"),
        new("workshop_recipe_modernization","all fields/refs/checked","version coverage and renewable materials; no future installation proof","metadata D"),
        new("loading_platforms","all fields/refs/checked","three levels/base identity/renewable materials; no actual throughput proof","metadata E/F"),
        new("vehicle_handling_kits","all fields/refs/checked","operator count and renewable materials; compatibility deferred","metadata E"),
        new("vehicles","all fields/ids/types/checked","positive speed/capacity/handling/class cost; motion/stock simulation deferred","metadata E"),
        new("road_projects","all fields/refs/checked","three levels/renewable materials; traffic/ROI deferred","metadata E"),
        new("economy_globals","all fields/units/checked","population order/import arithmetic/finite bootstrap witness; money recovery static","selected resource/demand scalars only; other metadata") ];
}
