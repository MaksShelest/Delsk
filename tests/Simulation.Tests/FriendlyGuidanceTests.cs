using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class FriendlyGuidanceTests
{
    private static readonly ParticipantId Human = new(1);

    private static (ScenarioDefinition Scenario, GameSession Session, PlayerView View) World()
    {
        var scenario = OrdinaryNewGame.Resolve(MapDesignTests.Template("compact_valley"), MobileEquipmentI4Tests.Profile(), new(3), vnextBalance: MapDesignTests.Balance()).Scenario;
        var session = new GameSession(new(scenario));
        return (scenario, session, PlayerDesk.Read(session.Engine, Human));
    }

    [Fact]
    public void BothPresentationsReachTheSameSections()
    {
        var simple = FriendlyGuidance.Navigation(true).SelectMany(page => page.Keys).Order(StringComparer.Ordinal).ToArray();
        var advanced = FriendlyGuidance.Navigation(false).SelectMany(page => page.Keys).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(simple, advanced);
        Assert.Contains("negotiations", simple);
        Assert.Contains("account", simple);
        Assert.Contains("roads", simple);
        Assert.Equal(FriendlyGuidance.Simple, FriendlyGuidance.Normalize(null));
        Assert.Equal(FriendlyGuidance.Advanced, FriendlyGuidance.Normalize("advanced"));
        Assert.Equal(FriendlyGuidance.Simple, FriendlyGuidance.Normalize("other"));
    }

    [Fact]
    public void DefaultPrimaryOrderStaysAndFriendlyKeepsAnImportantDisabledChoice()
    {
        var choices = new[]
        {
            new ContextChoice("section:fleet", "Fleet", null),
            Choice("build", ActionKind.Build),
            Choice("stop", ActionKind.StopHarvest),
            Choice("board", ActionKind.BoardVehicle),
            Choice("harvest", ActionKind.StartHarvesting)
        };
        var state = new ContextSelection();
        state.Update("scope", choices.OrderBy(ContextActions.Priority).ThenBy(c => c.Key, StringComparer.Ordinal).ToArray());
        Assert.Equal(["stop", "board", "harvest"], state.PrimaryKeys());
        var friendly = state.PrimaryKeys(true);
        Assert.Equal("stop", friendly[0]);
        Assert.Contains("harvest", friendly);
        var blocked = new ContextSelection();
        blocked.Update("plot", [Choice("buy", ActionKind.BuyPlot) with { Enabled = false, Details = "money" }, Choice("card", ActionKind.MoveTo)]);
        Assert.Contains("buy", blocked.PrimaryKeys(true));
        Assert.Equal("Продать товары", FriendlyGuidance.Caption(new("exchange", "Биржа", null), "ru"));
        Assert.Equal("Sell goods", FriendlyGuidance.Caption(new("exchange", "Exchange", null), "en"));
    }

    [Fact]
    public void FreshValleyNamesARealResourceAndDoesNotMutateTheWorld()
    {
        var (scenario, session, view) = World();
        string before = session.Engine.ComputeStateHash();
        var step = FriendlyGuidance.Read(view, scenario, "ru");
        Assert.Equal(before, session.Engine.ComputeStateHash());
        Assert.Contains(step.Id, new[] { "find", "harvest" });
        var site = view.Resources.Single(r => r.Position.X == step.X && r.Position.Z == step.Z);
        Assert.True(site.Public);
        Assert.True(site.Stock > 0);
        string title = PlayerText.Local("ru", step.TitleRu, step.TitleEn);
        Assert.Contains(PlayerText.Accusative("ru", site.ItemId), title);
        Assert.DoesNotContain("Добудьте " + PlayerText.Item("ru", site.ItemId), title);
        long price = view.Plots.Where(p => p.OwnerId is null && !p.IsCorridor && !p.IsShopSite && p.ExpansionFor is null).Min(p => p.PriceMinor);
        Assert.DoesNotContain(PlayerText.Money(price), PlayerText.Local("ru", step.DetailRu, step.DetailEn));
        Assert.Equal($"resource:{step.X}:{step.Z}:{site.ItemId}", FriendlyGuidance.PredictedKey(step));
        var en = FriendlyGuidance.Read(view, scenario, "en");
        Assert.Equal(step.Id, en.Id);
        Assert.Equal(step.X, en.X);
        Assert.Equal(step.Z, en.Z);
    }

    [Fact]
    public void AProductionBuildingSkipsTheFirstHarvestLesson()
    {
        var (scenario, _, view) = World();
        var owner = view.Participant.Id;
        var enterprise = new EnterpriseView(new(9), owner, new(9), new(10), false, 0, "NoInputs", 0, 0, 8, 0, 0, "planks", 1, 2, 1);
        var building = new BuildingView(new(9), owner, "sawmill", BuildingKind.Production, 1, view.Participant.Position, [view.Participant.Position], new(9), new(10));
        var step = FriendlyGuidance.Read(view with { Enterprises = [enterprise], Buildings = [building] }, scenario, "ru");
        Assert.Equal("produce", step.Id);
        Assert.NotEqual("find", step.Id);
    }

    [Fact]
    public void ConstructionTextUsesCatalogNumbersAndNamesTheShortage()
    {
        var (scenario, _, view) = World();
        var type = scenario.BuildingTypes!.Where(t => t.Kind == BuildingKind.Production).OrderBy(t => t.Levels.Single(l => l.Level == 1).PriceMinor).First();
        var level = type.Levels.Single(l => l.Level == 1);
        var pack = view.Inventories.Single(i => i.Id == view.Participant.InventoryId);
        string text = FriendlyGuidance.ConstructionText(view, scenario, type, level, pack, "ru");
        Assert.Contains(PlayerText.Money(level.PriceMinor), text);
        Assert.Contains(PlayerText.Money(view.AvailableCashMinor), text);
        Assert.Contains("Цена каталога", text);
        foreach (var material in level.Materials)
        {
            Assert.Contains(material.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture), text);
            Assert.Contains(PlayerText.Item("ru", material.ItemId), text);
        }
        Assert.Contains("не хватает", text);
        Assert.Contains(" / ", text);
    }

    [Fact]
    public void PhrasesUseTheResourceFormAndTheFactoryKeepsItsOwnName()
    {
        Assert.Equal("Соберите 1 свёклу", PlayerText.ActionPhrase("ru", "gather", "beet", 1));
        Assert.Equal("Соберите зерно", PlayerText.ActionPhrase("ru", "gather", "grain", 0));
        Assert.Equal("Добудьте 1 древесину", PlayerText.ActionPhrase("ru", "extract", "wood", 1));
        Assert.Equal("Добудьте 1 железную руду", PlayerText.ActionPhrase("ru", "extract", "iron_ore", 1));
        Assert.Equal("Продайте 1 свёклу", PlayerText.ActionPhrase("ru", "sell", "beet", 1));
        Assert.Equal("Продайте древесину", PlayerText.ActionPhrase("ru", "sell", "wood", 0));
        Assert.Equal("Начать добычу: Кокс", PlayerText.ActionPhrase("ru", "gather", "coke", 1));
        Assert.Equal("Gather 1 beet", PlayerText.ActionPhrase("en", "gather", "beet", 1));
        Assert.Equal("Harvest 1 wood", PlayerText.ActionPhrase("en", "extract", "wood", 1));
        Assert.Equal("Sell beet", PlayerText.ActionPhrase("en", "sell", "beet", 0));
        Assert.Equal("Лесопилка", PlayerText.Enterprise("ru", "woodworking", 1));
        Assert.Equal("Sawmill", PlayerText.Enterprise("en", "woodworking", 1));
        Assert.Equal("Пекарня", PlayerText.Enterprise("ru", "food", 1));
        Assert.NotEqual(PlayerText.Enterprise("ru", "food", 1), PlayerText.Enterprise("ru", "woodworking", 1));
    }

    [Fact]
    public void AConfirmedSaleMovesToAPlotAndAnUnobservedSaleDoesNot()
    {
        var (scenario, _, view) = World();
        var before = FriendlyGuidance.Read(view, scenario, "ru");
        Assert.Contains(before.Id, new[] { "find", "harvest" });
        var after = FriendlyGuidance.Read(view, scenario, "ru", sold: true);
        Assert.Equal("plot", after.Id);
        Assert.StartsWith("plot:", FriendlyGuidance.PredictedKey(after));
        Assert.DoesNotContain(PlayerText.Money(view.AvailableCashMinor), after.TitleRu);
        var plot = view.Plots.First(p => p.OwnerId is null && !p.IsCorridor && !p.IsShopSite && p.ExpansionFor is null);
        var owned = view with { Plots = view.Plots.Select(p => p.Id == plot.Id ? p with { OwnerId = view.Participant.Id } : p).ToArray() };
        var building = FriendlyGuidance.Read(owned, scenario, "ru", sold: true);
        Assert.Equal("materials", building.Id);
        Assert.Equal("woodworking", building.ItemId);
        Assert.Contains("Лесопилка", building.TitleRu);
        Assert.DoesNotContain("Пищевое", building.TitleRu);
        var memory = new CityReadState();
        Assert.False(memory.Note("harvest"));
        Assert.True(memory.Note("sale"));
        var restored = CityReadState.Restore(System.Text.Json.JsonSerializer.Deserialize<CityReadMemory>(System.Text.Json.JsonSerializer.Serialize(memory.Capture()))!);
        Assert.True(restored.Noted("sale"));
        Assert.False(CityReadState.Restore(new(["seen"], 0)).Noted("sale"));
    }

    private static ContextChoice Choice(string key, ActionKind kind) => new(key, key, new(kind));
}
