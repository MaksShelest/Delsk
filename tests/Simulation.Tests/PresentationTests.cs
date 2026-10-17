using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class PresentationTests
{
    [Fact]
    public void FrameShowsActorsAndHidesForeignSupport()
    {
        var session = new GameSession(new SimulationEngine(SupportTests.Scenario()));
        long election = session.Engine.Inspect().Elections.NextElectionId!.Value.Value;
        Assert.Equal("Accepted", LandContentFixtures.Reason(session, new(ActionKind.ProposeSupport, SupportSeller: new(3),
            Election: new(election), Compensation: [new("wood", 4)]), 2));
        Assert.NotEmpty(session.Engine.InspectSupport().Proposals);
        var frame = Presentation.Read(SupportTests.Scenario(), session.Engine, 1);
        foreach (var actor in session.Engine.Inspect().Participants)
        {
            var shown = frame.Actors.Single(a => a.Id == actor.Id.Value);
            Assert.Equal(actor.Position.X, shown.X);
            Assert.Equal(actor.Position.Z, shown.Z);
            Assert.Equal(actor.Id.Value == 1, shown.Human);
        }
        string json = JsonSerializer.Serialize(frame, SimulationJson.Options);
        Assert.DoesNotContain("compensation", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SupportProposal", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BalanceMinor", json, StringComparison.OrdinalIgnoreCase);
        var middle = Presentation.Between(0, 0, 4, 2, 0.5f);
        Assert.Equal(2, middle.X);
        Assert.Equal(1, middle.Z);
        Assert.Equal(new PresentationPoint(0, 0), Presentation.Between(0, 0, 4, 2, -1));
    }

    [Fact]
    public void VisibleTripStaysOnTheCommittedPath()
    {
        using var stream = typeof(PresentationTests).Assembly.GetManifestResourceStream("Fixture.logistics-cycle.json")!;
        var definition = JsonSerializer.Deserialize<ScenarioDefinition>(stream, SimulationJson.Options)!;
        var session = new GameSession(new SimulationEngine(definition));
        ScriptedScenarios.PrepareVisibleTrip(session, definition);
        var trip = session.Engine.Inspect().Logistics.Single().Trip;
        Assert.NotNull(trip);
        Assert.Contains(trip!.Position, trip.Path);
        var frame = Presentation.Read(definition, session.Engine, 1);
        var shown = Assert.Single(frame.Trips);
        Assert.Equal(trip.Position.X, shown.X);
        Assert.Equal(trip.Position.Z, shown.Z);
        Assert.Equal(trip.ItemId, shown.ItemId);
        Assert.DoesNotContain("compensation", JsonSerializer.Serialize(frame, SimulationJson.Options), StringComparison.OrdinalIgnoreCase);
    }
}
