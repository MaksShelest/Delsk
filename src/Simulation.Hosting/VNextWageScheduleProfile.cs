using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>
/// Новые обычные партии поверх четырёх классов техники.
/// Исторические профили и сохранённые партии со ставкой 20 minor не переписываются.
/// Тарифы услуг доставки и производственная услуга 2 minor остаются прежними.
/// </summary>
public static class VNextWageScheduleProfile
{
    public const string ProfileId = "vnext-i6c72-wages-1";
    public const long StartingWageMinor = 15;
    public const long WageStepMinor = 2;
    private const long PreviousWageMinor = 20;

    public static EconomyProfile Create(EconomyProfile classes)
    {
        if (classes.Id != VNextEquipmentClassesProfile.ProfileId || classes.VNext is null
            || classes.World.Rules.PlayerWageStepMinor != 5 || classes.World.PlotPriceMinor != 10_000
            || classes.World.Rules.Services?.DriverWageMinor != PreviousWageMinor
            || classes.World.Rules.Workshops?.ServiceMinorPerFullTick != 2)
            throw new ArgumentException("The wage schedule starts from the unchanged equipment-classes profile");
        var npc = JsonNode.Parse(classes.Npc.GetRawText())!.AsObject();
        if (npc["baseWageMinor"]!.GetValue<long>() != PreviousWageMinor)
            throw new ArgumentException("The shared NPC starting wage is not 20 minor");
        npc["baseWageMinor"] = StartingWageMinor;
        var rules = classes.World.Rules with
        {
            PlayerWageStepMinor = WageStepMinor,
            Fleet = RequireWage(classes.World.Rules.Fleet!, PreviousWageMinor) with { WageMinor = StartingWageMinor },
            Roads = RequireWage(classes.World.Rules.Roads!, PreviousWageMinor) with { WageMinor = StartingWageMinor }
        };
        var world = classes.World with
        {
            BuildingTypes = classes.World.BuildingTypes.Select(RetargetBuilding).ToArray(),
            EquipmentTypes = classes.World.EquipmentTypes.Select(RetargetEquipment).ToArray(),
            Logistics = RequireWage(classes.World.Logistics, PreviousWageMinor) with { WageMinor = StartingWageMinor },
            Rules = rules
        };
        var p = classes with
        {
            Id = ProfileId,
            VNext = null,
            World = world,
            Npc = JsonSerializer.SerializeToElement(npc, SimulationJson.Options)
        };
        return p with
        {
            VNext = classes.VNext with
            {
                ProjectionVersion = "wage-schedule-projection-1",
                ProjectionPayloadSha256 = VNextBalanceReader.Digest(VNextSupportedProfile.Serialize(p)),
                ActiveCapabilities = [..classes.VNext.ActiveCapabilities, "wage-schedule-20261108"],
                Limitations =
                [
                    "New ordinary games start paid groups at 15 minor per worker per working tick and use a manual step of 2 minor.",
                    "Profiles through equipment-classes keep 20 minor and step 5. Saved games are not migrated.",
                    "Delivery quote tariffs and the workshop service of 2 minor stay at their previous values. Goods prices are not recalibrated."
                ]
            }
        };
    }

    private static BuildingTypeDefinition RetargetBuilding(BuildingTypeDefinition type)
    {
        var levels = type.Levels.Select(level => level.WageMinor == 0 ? level : level with { WageMinor = Retarget(level.WageMinor, type.Id) }).ToArray();
        return type with { Levels = levels };
    }

    private static EquipmentTypeDefinition RetargetEquipment(EquipmentTypeDefinition type)
    {
        var levels = type.Levels.Select(level => level with { WageMinor = Retarget(level.WageMinor, type.Id) }).ToArray();
        return type with { Levels = levels };
    }

    private static long Retarget(long wage, string id)
    {
        if (wage == PreviousWageMinor) return StartingWageMinor;
        throw new ArgumentException("Unexpected starting wage " + wage + " on " + id);
    }

    private static T RequireWage<T>(T rules, long expected) where T : class
    {
        long actual = rules switch
        {
            FleetRules fleet => fleet.WageMinor,
            RoadRules roads => roads.WageMinor,
            LogisticsSettings logistics => logistics.WageMinor,
            _ => throw new ArgumentException("Unsupported wage carrier")
        };
        if (actual != expected) throw new ArgumentException("Unexpected shared wage " + actual);
        return rules;
    }
}
