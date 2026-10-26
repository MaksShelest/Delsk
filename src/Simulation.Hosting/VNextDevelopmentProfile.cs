using Simulation.Core;
using System.Collections.Immutable;

namespace Simulation.Hosting;

/// <summary>D2 progression and physical projects; advanced production content/recipe modernizations remain D3.</summary>
public static class VNextDevelopmentProfile
{
    public const string ProfileId="vnext-d2-development-1";
    public static EconomyProfile Create(EconomyProfile basis,VNextBalance balance)
    {
        var workshops=VNextWorkshopProfile.Create(basis,balance);var source=workshops.VNext!;
        if(!balance.EnterpriseTiers.OrderBy(t=>t.Level).Select(t=>t.Xp).SequenceEqual(Enumerable.Range(1,5).Select(DevelopmentRules.Threshold)))throw new ArgumentException("D2 requires the agreed industry thresholds");
        Cell[] shape=[new(0,0),new(1,0),new(0,1),new(1,1),new(0,2),new(1,2)];
        var industries=balance.Industries.Select(i=>new DevelopmentIndustry(i.Id,[..balance.Recipes.Where(r=>r.Industry==i.Id).Select(r=>new IndustryProduct(r.Id,r.Output,r.OutputQuantity,balance.Items.Single(item=>item.Id==r.Output).XpPerUnit,r.UnlockLevel))])).ToImmutableArray();
        var factories=workshops.World.BuildingTypes.Select(type=>type.Kind!=BuildingKind.Production?type:type with{
            Levels=balance.EnterpriseTiers.OrderBy(t=>t.Level).Select(t=>type.Levels[0] with{Level=checked((int)t.Level),PriceMinor=t.Level==1?type.Levels[0].PriceMinor:t.PriceMinor,
                Materials=t.Level==1?type.Levels[0].Materials:t.Materials.Select(m=>new MaterialCost(m.Key,m.Value)).ToArray(),InputCapacity=t.InputCapacity,OutputCapacity=t.OutputCapacity,
                ConstructionTicks=t.Level==1?60:t.Ticks,WorkshopSlots=checked((int)t.Workshops),Footprint=[..shape.Take(checked((int)t.Cells))]}).ToArray()}).ToArray();
        var p=workshops with{Id=ProfileId,VNext=null,World=workshops.World with{BuildingTypes=factories,
            Rules=workshops.World.Rules with{Development=new(industries,checked((int)balance.Rules.QuestActiveLimit),balance.Rules.QuestRefreshTicks)}}};
        return p with{VNext=source with{ProjectionVersion="development-projection-1",ProjectionPayloadSha256=VNextBalanceReader.Digest(VNextSupportedProfile.Serialize(p)),
            ActiveCapabilities=[..source.ActiveCapabilities,"industry-experience","funded-delivery-quests","industrial-contour-rights","timed-enterprise-expansion","per-cell-investment-levels"],
            Limitations=["D2: shared technology and paid physical development. Only D1 production recipes are active; full content and recipe modernization remain D3.",
                "Quest demand comes from funded signed deliveries, not invented orders. Public city-project quests require an explicitly declared funded project.",
                "Real material costs are preserved; missing natural production/import supply is not replaced by free materials."]}};
    }
}
