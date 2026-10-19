using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class PlayContextTests
{
    [Fact]
    public void DistantHoverExplainsTheResourceWithoutOfferingHarvest()
    {
        var (session, scenario) = Economy();
        var view = PlayerDesk.Read(session.Engine, Human);
        var frame = Presentation.Read(scenario, session.Engine, Human.Value);
        Assert.DoesNotContain(PlayContext.At(view, scenario, "ru"), choice => choice.Key.StartsWith("harvest:", StringComparison.Ordinal));
        string hover = PlayContext.Hover(frame, view, 0, 0, "ru");
        Assert.Contains("древесина", hover, StringComparison.Ordinal);
        Assert.Contains("Подойдите", hover, StringComparison.Ordinal);
        Assert.DoesNotContain("20000", hover, StringComparison.Ordinal);
        Assert.DoesNotContain("compensation", hover, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StandingOnAResourceOffersOneUnitAndDoesNotCreditItEarly()
    {
        var (session, scenario) = Economy();
        session.SetTravelHeading(Human, 0, -1);
        session.CompleteTravelTick();
        var view = PlayerDesk.Read(session.Engine, Human);
        Assert.Equal(new Cell(0, 0), view.Participant.Position);
        var choice = Assert.Single(PlayContext.At(view, scenario, "ru"),choice=>choice.Command?.Kind==ActionKind.Harvest);
        Assert.Equal("harvest:wood", choice.Key);
        Assert.Contains("×1", choice.Label, StringComparison.Ordinal);
        Assert.Equal(1, choice.Command!.Quantity);
        long before = Personal(view, "wood");
        Assert.Equal("Accepted", LandContentFixtures.Reason(session, choice.Command));
        var busy = PlayerDesk.Read(session.Engine, Human);
        Assert.True(busy.Participant.Busy);
        Assert.Equal(before, Personal(busy, "wood"));
    }

    [Fact]
    public void FreePlotOffersPurchaseAndTheUncreditedReservationFee()
    {
        var (session, scenario) = Economy();
        Walk(session, new(4, 0));
        var view = PlayerDesk.Read(session.Engine, Human);
        var choices = PlayContext.At(view, scenario, "ru");
        var buy = Assert.Single(choices, choice => choice.Key == "buy:1");
        var reserve = Assert.Single(choices, choice => choice.Key == "reserve:1");
        Assert.Contains("100.00", buy.Label, StringComparison.Ordinal);
        Assert.Contains("10.00", reserve.Label, StringComparison.Ordinal);
        Assert.Equal(ActionKind.BuyPlot, buy.Command!.Kind);
        Assert.Equal(ActionKind.ReservePlot, reserve.Command!.Kind);
        Assert.Equal(1000, PlayContext.ReservationFee(10000));
        session.Send(Human, reserve.Command);
        session.Step();
        var reserved = PlayerDesk.Read(session.Engine, Human);
        Assert.Equal(19000, reserved.CashMinor);
        var plot = Assert.Single(reserved.Plots, p => p.Id.Value == 1);
        Assert.Equal(Human, plot.ReservedBy);
        Assert.Equal(0, PlayContext.Keep(1, choices, choices[..1]));
        session.Send(Human, buy.Command);
        session.Step();
        var owned = PlayerDesk.Read(session.Engine, Human);
        Assert.Equal(9000, owned.CashMinor);
        var bought = Assert.Single(owned.Plots, p => p.Id.Value == 1);
        Assert.Equal(Human, bought.OwnerId);
        Assert.Null(bought.ReservedBy);
    }

    [Fact]
    public void CorridorIsProtectedAndSharedShopCanBeChosen()
    {
        var (session, scenario) = Economy();
        Walk(session, new(4, 3));
        Assert.DoesNotContain(PlayContext.At(PlayerDesk.Read(session.Engine, Human), scenario, "en"), choice => choice.Command is not null);
        Walk(session, new(12, 0));
        var shop = PlayContext.At(PlayerDesk.Read(session.Engine, Human), scenario, "en");
        Assert.Contains(shop, choice => choice.Key.StartsWith("buy:", StringComparison.Ordinal));
        Assert.Contains(shop, choice => choice.Key.StartsWith("reserve:", StringComparison.Ordinal));
        var frame = Presentation.Read(scenario, session.Engine, Human.Value);
        string hover = PlayContext.Hover(frame, PlayerDesk.Read(session.Engine, Human), 4, 3, "en");
        Assert.Contains("Passage", hover, StringComparison.Ordinal);
    }

    [Fact]
    public void CapitalHoverGroupsPublicOffersWithoutPrivateTerms()
    {
        var cycle = JsonSerializer.Deserialize<ScenarioDefinition>(File.ReadAllText(Scenario("economic-cycle.json")), SimulationJson.Options)!;
        var engine = new SimulationEngine(cycle);
        var frame = Presentation.Read(cycle, engine, 1);
        var terminal = Assert.Single(frame.Markers, marker => marker.Kind == "capital");
        string hover = PlayContext.Hover(frame, PlayerDesk.Read(engine, new(1)), terminal.X, terminal.Z, "en");
        Assert.Contains("wood", hover, StringComparison.Ordinal);
        Assert.Contains("plank", hover, StringComparison.Ordinal);
        Assert.DoesNotContain("compensation", hover, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(PlayContext.At(PlayerDesk.Read(engine, new(1)), cycle, "en"), choice => choice.Key.StartsWith("buy:", StringComparison.Ordinal));
    }

    private static (GameSession Session, ScenarioDefinition Scenario) Economy()
    {
        using var stream = typeof(PlayContextTests).Assembly.GetManifestResourceStream("Fixture.baseline-economy.json")!;
        var scenario = JsonSerializer.Deserialize<ScenarioDefinition>(stream, SimulationJson.Options)!;
        return (new GameSession(new SimulationEngine(scenario)), scenario);
    }

    private static void Walk(GameSession session, Cell target)
    {
        session.Send(Human, PlayerDesk.Move(target.X, target.Z));
        for (int i = 0; i < 40 && PlayerDesk.Read(session.Engine, Human).Participant.Position != target; i++) session.Step();
        Assert.Equal(target, PlayerDesk.Read(session.Engine, Human).Participant.Position);
        Assert.False(PlayerDesk.Read(session.Engine, Human).Participant.Busy);
    }

    private static long Personal(PlayerView view, string item) =>
        view.Inventories.Single(i => i.Id == view.Participant.InventoryId).Stock.Where(s => s.ItemId == item).Sum(s => s.Quantity);

    private static string Scenario(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "scenarios", name);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(name);
    }

    private static readonly ParticipantId Human = new(1);
}
