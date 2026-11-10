using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>
/// Новые обычные партии поверх ставки 0,15 и шага 0,02.
/// Исторические профили, сохранённые партии и лаборатория на wages-1 не переписываются.
/// </summary>
public static class VNextResourceRightsProfile
{
    public const string ProfileId = "vnext-i6c75-resource-rights-1";

    public static EconomyProfile Create(EconomyProfile wages)
    {
        if (wages.Id != VNextWageScheduleProfile.ProfileId || wages.VNext is null
            || wages.World.Rules.PlayerWageStepMinor != VNextWageScheduleProfile.WageStepMinor
            || wages.World.Rules.Fleet?.WageMinor != VNextWageScheduleProfile.StartingWageMinor
            || wages.World.PlotPriceMinor != 10_000
            || wages.World.Rules.ResourceRights is not null)
            throw new ArgumentException("Resource rights start from the unchanged wage profile");
        var world = wages.World with { Rules = wages.World.Rules with { ResourceRights = new ResourceRightsRules(1) } };
        var p = wages with { Id = ProfileId, VNext = null, World = world };
        return p with
        {
            VNext = wages.VNext with
            {
                ProjectionVersion = "resource-rights-projection-1",
                ProjectionPayloadSha256 = VNextBalanceReader.Digest(VNextSupportedProfile.Serialize(p)),
                ActiveCapabilities = [..wages.VNext.ActiveCapabilities, "resource-rights-20261110"],
                Limitations =
                [
                    "New ordinary games allow manual harvest of every unowned natural cell and purchase at that cell's own price.",
                    "After purchase the owner has exclusive extraction rights. Public stays geography and is not flipped.",
                    "Resource lease is disabled here. Profiles through wages-1 keep the previous public, private and lease rules. Saved games are not migrated.",
                    "The industrial plot price stays 10000 minor. Resource prices stay on each site. Wages stay 15 minor with a step of 2."
                ]
            }
        };
    }
}
