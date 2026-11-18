using Simulation.Core;

namespace Simulation.Hosting;

public static class VNextI6C8DBalanceProfile
{
    public const string ProfileId = "vnext-i6c8d-economic-balance-1";
    internal const string ProjectionVersion = "i6c8d-economic-balance-projection-1";

    public static EconomyProfile ForOrdinaryMap(EconomyProfile mobile) =>
        Create(VNextProvincialSupportProfile.ForOrdinaryMap(mobile));

    public static EconomyProfile Create(EconomyProfile provincial)
    {
        if (provincial.Id != VNextProvincialSupportProfile.ProfileId || provincial.VNext is not { } provenance ||
            provincial.World.CapitalDemand.Allocation is not null || provincial.World.Rules.ProvincialSupport is not { Applies: true } ||
            provincial.World.Rules.Workshops is not { FillSingleIngredientInboundCargo: false } workshops)
            throw new ArgumentException("I6-C8D requires the I6-C8C provincial profile");

        var world = provincial.World with
        {
            CapitalDemand = provincial.World.CapitalDemand with { Allocation = new(1, 50, 6) },
            Rules = provincial.World.Rules with { Workshops = workshops with { FillSingleIngredientInboundCargo = true } }
        };
        var profile = provincial with { Id = ProfileId, World = world, VNext = null };
        return profile with
        {
            VNext = provenance with
            {
                ProjectionVersion = ProjectionVersion,
                ProjectionPayloadSha256 = VNextBalanceReader.Digest(VNextSupportedProfile.Serialize(profile)),
                ActiveCapabilities = [..provenance.ActiveCapabilities, "bounded-capital-demand-i6c8d", "single-ingredient-freight-i6c8d"],
                Limitations =
                [
                    "I6-C8C provincial support, recipes, wages and prices are retained.",
                    "The capital budgets a minimum of one order unit for each starter output. Recent production can influence up to half of the budget remaining after minimum orders.",
                    "The order target at base prices is capped at six times the roster-scaled bootstrap budget. Prices and actual escrow use the existing market-price waves.",
                    "Inbound routes for workshops using the same single ingredient load to vehicle and receiving capacity. Mixed inputs retain their previous reserves and batch limit.",
                    "New ordinary games select this profile. Checkpoints retain their resolved economy."
                ]
            }
        };
    }
}
