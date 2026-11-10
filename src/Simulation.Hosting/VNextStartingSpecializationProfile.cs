using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>
/// Выбор стартовой специализации поверх прав на ресурсы.
/// Покупка участка 2×3 и субсидии сюда не входят.
/// </summary>
public static class VNextStartingSpecializationProfile
{
    public const string ProfileId = "vnext-i6c8b-specializations-1";

    public static EconomyProfile ForOrdinaryMap(EconomyProfile mobile) =>
        Create(VNextResourceRightsProfile.Create(VNextWageScheduleProfile.Create(VNextEquipmentClassesProfile.Create(mobile))));

    public static EconomyProfile Create(EconomyProfile rights)
    {
        if (rights.Id != VNextResourceRightsProfile.ProfileId || rights.VNext is null
            || rights.World.Rules.ResourceRights?.Version != 1
            || rights.World.Rules.IndustrialParcels is not null
            || rights.World.Rules.StartingSpecializations is not null
            || rights.World.Rules.PlayerWageStepMinor != VNextWageScheduleProfile.WageStepMinor
            || rights.World.Rules.Fleet?.WageMinor != VNextWageScheduleProfile.StartingWageMinor
            || rights.World.PlotPriceMinor != 10_000)
            throw new ArgumentException("Starting specializations require the unchanged resource-rights profile");
        var world = rights.World with { Rules = rights.World.Rules with { StartingSpecializations = new StartingSpecializationRules(1) } };
        var p = rights with { Id = ProfileId, VNext = null, World = world };
        return p with
        {
            VNext = rights.VNext with
            {
                ProjectionVersion = "starting-specializations-projection-1",
                ProjectionPayloadSha256 = VNextBalanceReader.Digest(VNextSupportedProfile.Serialize(p)),
                ActiveCapabilities = [..rights.VNext.ActiveCapabilities, "starting-specializations-20261111"],
                Limitations =
                [
                    "A new ordinary game records one starting industry for the human and a reproducible industry for each NPC. The choice is not a building permit and does not pay for land, construction or materials.",
                    "Support slots stay reserved at one. No subsidy, grant or price change is posted. Industrial parcels of I6-C8A stay off: one industrial cell remains 10000 minor.",
                    "Profiles without these rules, saved games and the wage laboratory keep the previous economy. Research can assign an explicit roster or disable the rules.",
                    "Resource rights of I6-C7.5 stay in force. Wages stay 15 minor with a step of 2. Starting cash stays 20000 minor."
                ]
            }
        };
    }
}
