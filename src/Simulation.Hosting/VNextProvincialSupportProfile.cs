using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>
/// Обычный старт I6-C8C: специализация, права на ресурсы и участок 2×3.
/// Цены, зарплата 0,15 и шаг 0,02 не меняются. Субсидия считается по каталогу.
/// </summary>
public static class VNextProvincialSupportProfile
{
    public const string ProfileId = "vnext-i6c8c-provincial-support-1";

    public static EconomyProfile ForOrdinaryMap(EconomyProfile mobile) =>
        Create(VNextStartingSpecializationProfile.ForOrdinaryMap(mobile));

    public static EconomyProfile Create(EconomyProfile specializations)
    {
        if (specializations.Id != VNextStartingSpecializationProfile.ProfileId || specializations.VNext is null
            || specializations.World.Rules.StartingSpecializations is not { Version: 1, Enabled: true }
            || specializations.World.Rules.IndustrialParcels is not null
            || specializations.World.Rules.ProvincialSupport is not null
            || specializations.World.Rules.ResourceRights?.Version != 1
            || specializations.World.Rules.PlayerWageStepMinor != VNextWageScheduleProfile.WageStepMinor
            || specializations.World.Rules.Fleet?.WageMinor != VNextWageScheduleProfile.StartingWageMinor
            || specializations.World.PlotPriceMinor != 10_000)
            throw new ArgumentException("Provincial support starts from the unchanged specialization profile");
        var world = specializations.World with
        {
            Rules = specializations.World.Rules with
            {
                IndustrialParcels = new IndustrialParcelRules(1),
                ProvincialSupport = new ProvincialSupportRules(1)
            }
        };
        var p = specializations with { Id = ProfileId, VNext = null, World = world };
        return p with
        {
            VNext = specializations.VNext with
            {
                ProjectionVersion = "provincial-support-projection-1",
                ProjectionPayloadSha256 = VNextBalanceReader.Digest(VNextSupportedProfile.Serialize(p)),
                ActiveCapabilities = [..specializations.VNext.ActiveCapabilities, "provincial-support-20261111", "industrial-parcels-20261110"],
                Limitations =
                [
                    "A new ordinary game keeps one starting industry and adds one provincial program. The capital pays the first 2x3 parcel, the first level-1 building of that industry, its construction materials, one batch of input and the wage and energy reserve for that batch.",
                    "The grant is posted from the treasury when the expense happens. It is not a free wallet balance. Support slots go from 1 to 0 and stay there after demolition. Research can set ProvincialSupport.Enabled to false.",
                    "Cell price stays 10000 minor and the whole parcel stays 60000 minor. Import prices, recipes, resource rights and the wage laboratory stay unchanged. Wages stay 15 minor with a step of 2. Starting cash stays 20000 minor.",
                    "Profiles before this one and saved games do not receive the program. Old maps keep one-cell purchases."
                ]
            }
        };
    }
}
