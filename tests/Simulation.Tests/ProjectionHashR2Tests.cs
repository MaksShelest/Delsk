using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class ProjectionHashR2Tests
{
    [Fact]
    public void NpcBaseWageCanBeRetargetedWithoutChangingTheWorldSchedule()
    {
        var profile = VNextWageScheduleProfile.Create(EquipmentClassesI6C22Tests.Profile());
        string original = Encoding.UTF8.GetString(VNextSupportedProfile.Serialize(profile));
        var unchanged = EconomyProfile.Read(EconomyProfile.RefreshProjectionPayload(original));
        Assert.Equal(15, unchanged.Npc.GetProperty("baseWageMinor").GetInt64());
        Assert.Equal(15, unchanged.World.Rules.Fleet!.WageMinor);
        Assert.Equal(2, unchanged.World.Rules.PlayerWageStepMinor);

        var node = JsonNode.Parse(original)!.AsObject();
        node["npc"]!["baseWageMinor"] = 25;
        Assert.Throws<ArgumentException>(() => EconomyProfile.Read(node.ToJsonString()));

        var edited = EconomyProfile.Read(EconomyProfile.RefreshProjectionPayload(node.ToJsonString()));
        Assert.Equal(VNextWageScheduleProfile.ProfileId, edited.Id);
        Assert.Equal(25, edited.Npc.GetProperty("baseWageMinor").GetInt64());
        Assert.Equal(50, edited.Npc.GetProperty("maximumWageMinor").GetInt64());
        Assert.Equal(15, edited.World.Rules.Fleet!.WageMinor);
        Assert.Equal(15, edited.World.Rules.Roads!.WageMinor);
        Assert.Equal(15, edited.World.Logistics.WageMinor);
        Assert.Equal(2, edited.World.Rules.PlayerWageStepMinor);
        Assert.Equal(10_000, edited.World.PlotPriceMinor);
        Assert.All(edited.World.BuildingTypes.SelectMany(type => type.Levels), level => Assert.True(level.WageMinor is 0 or 15));
    }
}
