using System.Collections.Immutable;
using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>New compact-valley games. I4 and older saves keep their sixteen extractor models.</summary>
public static class VNextEquipmentClassesProfile
{
    public const string ProfileId = "vnext-i6c22-equipment-classes-1";

    public static EconomyProfile Create(EconomyProfile mobile)
    {
        if (mobile.Id != VNextMobileEquipmentProfile.ProfileId || mobile.World.Rules.Fleet?.EquipmentDealerVersion != 1)
            throw new ArgumentException("Equipment classes start from the I4 mobile profile");
        var speed = Math.Max(1, 80 / mobile.World.MoveTicksPerCell);
        var classes = new[]
        {
            Class("extract_field", "grain", ["grain", "beet"], Source(mobile, "extract_grain"), 230_000, "Полевой комбайн"),
            Class("extract_forest", "wood", null, Source(mobile, "extract_wood"), null, "Лесозаготовительная машина"),
            Class("extract_dairy", "milk", null, Source(mobile, "extract_milk"), null, "Молочная машина"),
            Class("extract_mine", "stone", ["stone", "sand", "iron_ore", "copper_ore"], Source(mobile, "extract_stone"), null, "Горная машина"),
        };
        var cars = mobile.World.Rules.Fleet.Models.Where(m => m.EquipmentTypeId is null)
            .Select(m => m with { PriceMinor = Discount(m.PriceMinor) }).ToArray();
        var machines = classes.Select(pair =>
        {
            var level = pair.Type.Levels.Single(l => l.Level == 1);
            return new VehicleModel(pair.Type.Id.Replace("extract_", "extractor_", StringComparison.Ordinal) + "_1", pair.Name, speed, level.StorageCapacity, level.PriceMinor, 10, EquipmentTypeId: pair.Type.Id, EquipmentLevel: 1);
        }).ToArray();
        var carIds = cars.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        var factors = mobile.World.Rules.Billing!.ClassFactors.Where(p => carIds.Contains(p.Key))
            .Aggregate(ImmutableSortedDictionary<string, int>.Empty, (d, p) => d.Add(p.Key, p.Value));
        foreach (var model in machines) factors = factors.Add(model.Id, 10000);
        var fleet = mobile.World.Rules.Fleet with { Models = [..cars, ..machines], EquipmentDealerVersion = 2 };
        var p = mobile with { Id = ProfileId, VNext = null, World = mobile.World with {
            EquipmentTypes = classes.Select(c => c.Type).ToArray(),
            Rules = mobile.World.Rules with { Fleet = fleet, Billing = mobile.World.Rules.Billing with { ClassFactors = factors } } } };
        return p with { VNext = mobile.VNext! with {
            ProjectionVersion = "equipment-classes-projection-1",
            ProjectionPayloadSha256 = VNextBalanceReader.Digest(VNextSupportedProfile.Serialize(p)),
            ActiveCapabilities = [..mobile.VNext.ActiveCapabilities, "equipment-classes-20261107", "vehicle-prices-20261107"],
            Limitations = ["Four extractor classes are bought at level 1 and upgraded in place.",
                "The dealer shows two cars, one paver and two extractors. Level 2 is not sold.",
                "The extra vehicle discount applies once, only to this profile."] } };
    }

    private static EquipmentTypeDefinition Source(EconomyProfile mobile, string id) => mobile.World.EquipmentTypes.Single(t => t.Id == id);
    private static (EquipmentTypeDefinition Type, string Name) Class(string id, string item, string[]? items, EquipmentTypeDefinition source, long? level1Price, string name)
    {
        var levels = source.Levels.Select(l => l.Level == 1 && level1Price is { } price ? l with { PriceMinor = price } : l with { }).ToArray();
        var type = items is null ? new EquipmentTypeDefinition(id, item, levels) : new(id, item, levels, items.ToImmutableArray());
        return (type, name);
    }
    private static long Discount(long minor) => checked((long)((((Int128)minor * 7 + 50000) / 100000) * 10000));
}
