using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>D1 starts with one paid, equipped place. Expansion/XP and advanced unlocks remain D2.</summary>
public static class VNextWorkshopProfile
{
    public const string ProfileId = "vnext-d1-workshops-1";
    public static EconomyProfile Create(EconomyProfile basis, VNextBalance balance)
    {
        if (balance.Rules.StaffPerWorkshop != 5 || !balance.Rules.StaffRateBp.SequenceEqual(Enumerable.Range(0,6).Select(n => (long)WorkshopRules.Rate(n)*100)))
            throw new ArgumentException("D1 requires the agreed five-worker productivity curve");
        var storage = VNextStorageProfile.Create(basis, balance);
        var source = storage.VNext!;
        long service = balance.Recipes.Select(r => r.ServiceMinorPerWorkTick).Distinct().Single();
        var baseTier = balance.EnterpriseTiers.Single(t => t.Level == 1);
        var factories = balance.Industries.Select(industry =>
        {
            var recipes = balance.Recipes.Where(r => r.Industry == industry.Id && r.UnlockLevel == 1).OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();
            var outfit = balance.AdditionalWorkshopOutfits[industry.Id];
            MaterialCost[] Materials(IEnumerable<KeyValuePair<string, long>> m) => m.Select(p => new MaterialCost(p.Key, p.Value)).ToArray();
            return new BuildingTypeDefinition(industry.Id, BuildingKind.Production, [new(0, 0)],
                [new(1, recipes[0].Id, industry.ConstructionPriceMinor, Materials(industry.Materials), baseTier.InputCapacity,
                    baseTier.OutputCapacity, checked((int)balance.Rules.StaffPerWorkshop), balance.Rules.WageMinor,
                    AdditionalRecipeIds: [.. recipes.Skip(1).Select(r => r.Id)], WorkshopSlots: 1)], [],
                new(outfit.PriceMinor, [.. Materials(outfit.Materials)], 60));
        }).ToArray();
        var p = storage with { Id = ProfileId, VNext = null, World = storage.World with {
            BuildingTypes = [.. storage.World.BuildingTypes.Where(t => t.Kind != BuildingKind.Production), .. factories],
            Rules = storage.World.Rules with { Workshops = new(service) }
        }};
        return p with { VNext = source with { ProjectionVersion = "workshops-projection-1",
            ProjectionPayloadSha256 = VNextBalanceReader.Digest(VNextSupportedProfile.Serialize(p)),
            ActiveCapabilities = [.. source.ActiveCapabilities, "parallel-workshops", "workshop-labor-curve", "paid-ready-slot-outfit", "safe-demolition"],
            Limitations = ["D1: one initial equipped workshop; new places/contours, XP, advanced unlocks and recipe versions remain D2.",
                "Real construction inputs are required. Material import/bootstrap supply is not implemented by this profile."] }};
    }
}
