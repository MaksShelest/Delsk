using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class WorldArtScatterTests
{
    [Fact]
    public void ScatterRepeatsForTheSameCellAndIgnoresCallOrder()
    {
        var first = WorldArtScatter.Layout("compact_valley_i6c21", 3, 4, "wood", "Forest_Rich_Full");
        var second = WorldArtScatter.Layout("compact_valley_i6c21", 3, 4, "wood", "Forest_Rich_Full");
        Assert.Equal(first, second);
        Assert.NotEmpty(first);
        Assert.All(first, m => Assert.InRange(MathF.Max(MathF.Abs(m.X), MathF.Abs(m.Z)), 0, 0.42f));
        Assert.True(first.Select(m => m.Variant).Distinct().Count() > 1);
    }

    [Fact]
    public void NeighbouringForestCellsMeetAtTheSharedEdge()
    {
        var left = WorldArtScatter.Layout("compact_valley_i6c21", 2, 3, "wood", "Forest_Rich_Full");
        var right = WorldArtScatter.Layout("compact_valley_i6c21", 3, 3, "wood", "Forest_Rich_Full");
        Assert.Contains(left, m => m.X > 0.25f);
        Assert.Contains(right, m => m.X < -0.25f);
        Assert.NotEqual(left, WorldArtScatter.Layout("compact_valley_i6c21", 2, 4, "wood", "Forest_Rich_Full"));
    }

    [Fact]
    public void ForestStatesChangeDensityWithoutLeavingTheCell()
    {
        int Full(string state) => WorldArtScatter.Layout("valley", 1, 1, "wood", state).Length;
        Assert.True(Full("Forest_Rich_Full") > Full("Forest_Sparse_Full"));
        Assert.True(Full("Forest_Sparse_Full") >= Full("Forest_Sparse_Cut"));
        var cut = WorldArtScatter.Layout("valley", 1, 1, "wood", "Forest_Sparse_Cut");
        Assert.All(cut, m => Assert.InRange(m.Scale, 0.5f, 1.3f));
        var young = WorldArtScatter.Layout("valley", 1, 1, "wood", "Forest_Rich_Young");
        Assert.Contains(young, m => m.Scale < 0.8f);
    }

    [Fact]
    public void ApprovedCropsStayTheDefaultCatalog()
    {
        Assert.False(WorldArtScatter.ExperimentalCrops);
        Assert.Equal("WheatField", AppearanceCatalog.ResourceModel("grain", 80, 100, false));
        Assert.Equal("BeetField", AppearanceCatalog.ResourceModel("beet", 40, 100, false));
        Assert.Equal("Pasture_2Cow", AppearanceCatalog.ResourceModel("milk", 80, 100, false));
        Assert.Equal("Pasture_1Cow", AppearanceCatalog.ResourceModel("milk", 80, 100, true));
        Assert.Equal("EmptyField", AppearanceCatalog.ResourceModel("grain", 0, 100, false));
        Assert.Equal("Forest_Rich_Full", AppearanceCatalog.ResourceModel("wood", 80, 100, false));
        Assert.Equal("", AppearanceCatalog.ResourceModel("stone", 80, 100, false));
    }

    [Fact]
    public void CropsAreUnevenAndBeetsAreFewer()
    {
        var wheat = WorldArtScatter.Layout("valley", 5, 6, "grain", "WheatField");
        var beet = WorldArtScatter.Layout("valley", 5, 6, "beet", "BeetField");
        var stubble = WorldArtScatter.Layout("valley", 5, 6, "grain", "EmptyField");
        Assert.InRange(wheat.Length, 12, 16);
        Assert.InRange(beet.Length, 4, 5);
        Assert.True(stubble.Length < wheat.Length);
        var xs = wheat.Select(m => m.X).OrderBy(v => v).ToArray();
        Assert.True(xs.Zip(xs.Skip(1), (a, b) => b - a).Distinct().Count() > 1);
        Assert.Contains(wheat, m => MathF.Abs(m.Lean) > 1f);
        Assert.True(wheat.Select(m => m.Tint).Distinct().Count() > 1);
    }

    [Fact]
    public void PastureCowCountFollowsTheState()
    {
        int Cows(string state) => WorldArtScatter.Layout("valley", 4, 8, "milk", state).Count(m => m.Variant < 10);
        Assert.Equal(2, Cows("Pasture_2Cow"));
        Assert.Equal(1, Cows("Pasture_1Cow"));
        Assert.Equal(0, Cows("Pasture_Empty"));
        Assert.Contains(WorldArtScatter.Layout("valley", 4, 8, "milk", "Pasture_2Cow"), m => m.Variant >= 10);
    }

    [Fact]
    public void DepositsDifferByCellAndShrinkWhenSpent()
    {
        var full = WorldArtScatter.Layout("valley", 8, 2, "stone", "full");
        var spent = WorldArtScatter.Layout("valley", 8, 2, "stone", "spent");
        var iron = WorldArtScatter.Layout("valley", 8, 2, "iron_ore", "full");
        Assert.True(full.Length > spent.Length);
        Assert.All(spent, m => Assert.True(m.Scale < 0.7f));
        Assert.NotEqual(full, iron);
        Assert.True(full.Select(m => m.Scale).Distinct().Count() > 1);
    }

    [Theory]
    [InlineData("grain", "extract_field")]
    [InlineData("beet", "extract_field")]
    [InlineData("wood", "extract_forest")]
    [InlineData("milk", "extract_dairy")]
    [InlineData("stone", "extract_mine")]
    [InlineData("sand", "extract_mine")]
    [InlineData("iron_ore", "extract_mine")]
    [InlineData("copper_ore", "extract_mine")]
    public void EquipmentVisualFollowsClassNotTheCurrentResource(string item, string expected)
        => Assert.Equal(expected, WorldArtScatter.EquipmentClass(item));

    [Fact]
    public void SwitchingASharedResourceDoesNotChangeTheMachineClass()
    {
        Assert.Equal(WorldArtScatter.EquipmentClass("grain"), WorldArtScatter.EquipmentClass("beet"));
        Assert.Equal(WorldArtScatter.EquipmentClass("stone"), WorldArtScatter.EquipmentClass("copper_ore"));
        Assert.NotEqual(WorldArtScatter.EquipmentClass("milk"), WorldArtScatter.EquipmentClass("grain"));
        Assert.NotEqual(WorldArtScatter.EquipmentClass("wood"), WorldArtScatter.EquipmentClass("stone"));
        Assert.Equal("", WorldArtScatter.EquipmentClass("sputnik20"));
    }
}
