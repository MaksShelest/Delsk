using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>
/// Участки 2×3 поверх прав на ресурсы I6-C7.5.
/// Обычные новые партии этот профиль не выбирают: стартовых 200 монет не хватает на 600.
/// </summary>
public static class VNextIndustrialParcelProfile
{
    public const string ProfileId = "vnext-i6c8a-industrial-parcels-1";

    public static EconomyProfile Create(EconomyProfile rights)
    {
        if (rights.Id != VNextResourceRightsProfile.ProfileId || rights.VNext is null
            || rights.World.Rules.ResourceRights?.Version != 1
            || rights.World.Rules.IndustrialParcels is not null
            || rights.World.Rules.PlayerWageStepMinor != VNextWageScheduleProfile.WageStepMinor
            || rights.World.PlotPriceMinor != IndustrialParcelRules.CellPriceMinor)
            throw new ArgumentException("Industrial parcels start from the unchanged resource-rights profile");
        var world = rights.World with { Rules = rights.World.Rules with { IndustrialParcels = new IndustrialParcelRules(1) } };
        var p = rights with { Id = ProfileId, VNext = null, World = world };
        return p with
        {
            VNext = rights.VNext with
            {
                ProjectionVersion = "industrial-parcels-projection-1",
                ProjectionPayloadSha256 = VNextBalanceReader.Digest(VNextSupportedProfile.Serialize(p)),
                ActiveCapabilities = [..rights.VNext.ActiveCapabilities, "industrial-parcels-20261110"],
                Limitations =
                [
                    "Prepared games can buy a whole 2x3 industrial parcel for 60000 minor in one payment. One industrial cell stays 10000 minor.",
                    "Ordinary new games stay on resource-rights-1 until the I6-C8C subsidy. Historical maps, saves and the wage laboratory are unchanged.",
                    "A standard parcel is purchased, built and expanded only inside its six cells. Shop plots stay single-cell purchases.",
                    "Resource rights of I6-C7.5 stay in force. Wages stay 15 minor with a step of 2."
                ]
            }
        };
    }
}
