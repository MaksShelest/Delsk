using System.Collections.Immutable;
using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>New I4 games only. Historical E1 through G1 projections stay byte exact.</summary>
public static class VNextMobileEquipmentProfile
{
    public const string ProfileId = "vnext-i4-mobile-equipment-1";

    public static EconomyProfile Create(EconomyProfile basis, VNextBalance balance)
    {
        var prior = VNextNpcEconomyProfile.Create(basis, balance);
        var fleet = prior.World.Rules.Fleet!;
        // The input is the verified, undiscounted A2 catalog, never an already resolved I4 profile.
        var models = fleet.Models.Select(m => m with { PriceMinor = Discount(m.PriceMinor) })
            .Concat(prior.World.EquipmentTypes.SelectMany(t => t.Levels.Select(l =>
                new VehicleModel("extractor_" + t.Id + "_" + l.Level, t.Id, Math.Max(1,80/prior.World.MoveTicksPerCell),
                    l.StorageCapacity, l.PriceMinor, 10, EquipmentTypeId:t.Id, EquipmentLevel:l.Level))))
            .ToImmutableArray();
        var billing = prior.World.Rules.Billing!;
        foreach (var model in models.Where(m => m.EquipmentTypeId is not null))
            billing = billing with { ClassFactors = billing.ClassFactors.Add(model.Id, 10000) };
        var p = prior with { Id = ProfileId, VNext = null, World = prior.World with {
            Rules = prior.World.Rules with {
                Fleet = fleet with { Models = models, EquipmentDealerVersion = 1 }, Billing = billing,
                Movement = MovementRules.I1, VehicleControl = VehicleControlRules.I2 } } };
        return p with { VNext = prior.VNext! with {
            ProjectionVersion = "mobile-equipment-projection-1",
            ProjectionPayloadSha256 = VNextBalanceReader.Digest(VNextSupportedProfile.Serialize(p)),
            ActiveCapabilities = [..prior.VNext!.ActiveCapabilities, "city-mobile-extraction", "vehicle-prices-20261104"],
            Limitations = ["Mobile equipment uses the shared Fleet/Movement2/VehicleControl1 executor.",
                "Device acceptance and long-run economic calibration remain separate."] } };
    }

    private static long Discount(long minor) => checked((long)((((Int128)minor * 7 + 50000) / 100000) * 10000));
}
