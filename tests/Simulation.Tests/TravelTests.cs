using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class TravelTests
{
    [Fact]
    public void PartialCellStopTurnsWithoutReachingTheNextCell()
    {
        var session = Fixtures.Session();
        var actor = new ParticipantId(1);
        session.SetTravelHeading(actor, 1, 0);
        for (int i = 0; i < 4; i++) session.AdvanceTravelQuantum();
        var stopped = session.Engine.TravelOf(actor);
        Assert.Equal(new Cell(0, 0), CellOf(session));
        Assert.Equal(900, stopped.OffsetX);
        Assert.Equal(500, stopped.OffsetZ);
        session.SetTravelHeading(actor, 0, 0);
        for (int i = 0; i < 4; i++) session.AdvanceTravelQuantum();
        Assert.Equal(900, session.Engine.TravelOf(actor).OffsetX);
        session.SetTravelHeading(actor, -1, 0);
        for (int i = 0; i < 5; i++) session.AdvanceTravelQuantum();
        Assert.Equal(new Cell(0, 0), CellOf(session));
        Assert.Equal(400, session.Engine.TravelOf(actor).OffsetX);
        Assert.Equal(-1, session.Engine.TravelOf(actor).FacingX);
    }

    [Fact]
    public void DiagonalDistanceDoesNotExceedCardinalDistance()
    {
        long cardinal = Span(Run(1, 0));
        long diagonal = Span(Run(1, 1));
        Assert.True(diagonal < cardinal);
        Assert.Equal(1000L * 1000L, cardinal);
    }

    [Fact]
    public void BlockedCellAndMapEdgeStopTheStep()
    {
        var wall = Fixtures.Session(Fixtures.Cycle() with { BlockedCells = [new Cell(1, 0)] });
        wall.SetTravelHeading(new(1), 1, 0);
        wall.CompleteTravelTick();
        Assert.Equal(new Cell(0, 0), CellOf(wall));
        Assert.Equal(999, wall.Engine.TravelOf(new(1)).OffsetX);

        var corner = Fixtures.Session(Fixtures.Cycle() with { BlockedCells = [new Cell(1, 0), new Cell(0, 1)] });
        corner.SetTravelHeading(new(1), 1, 1);
        corner.CompleteTravelTick();
        Assert.Equal(new Cell(0, 0), CellOf(corner));
        Assert.Equal(999, corner.Engine.TravelOf(new(1)).OffsetX);
        Assert.Equal(999, corner.Engine.TravelOf(new(1)).OffsetZ);

        var edge = Fixtures.Session();
        edge.SetTravelHeading(new(1), -1, 0);
        edge.CompleteTravelTick();
        Assert.Equal(new Cell(0, 0), CellOf(edge));
        Assert.Equal(0, edge.Engine.TravelOf(new(1)).OffsetX);
    }

    [Fact]
    public void WalkingOutCancelsHarvestWithoutCreditingGoods()
    {
        var session = Fixtures.Session();
        var actor = new ParticipantId(1);
        Fixtures.Command(session, PlayerDesk.Harvest("wood", 1));
        session.SetTravelHeading(actor, 1, 0);
        session.CompleteTravelTick();
        Assert.Equal(new Cell(1, 0), CellOf(session));
        Assert.False(session.Engine.Observe(actor).Participant.Busy);
        session.CompleteTravelTick();
        Assert.Equal(0, Fixtures.Count(session, session.Engine.Observe(actor).Participant.InventoryId, "wood"));
    }

    [Fact]
    public void MoveToSnapsToTheCellCenterAndClearsHeading()
    {
        var session = Fixtures.Session();
        var actor = new ParticipantId(1);
        session.SetTravelHeading(actor, 1, 0);
        for (int i = 0; i < 4; i++) session.AdvanceTravelQuantum();
        session.SetTravelHeading(actor, 0, 0);
        session.Send(actor, new(ActionKind.MoveTo, Target: new(2, 0)));
        session.CompleteTravelTick();
        var travel = session.Engine.TravelOf(actor);
        Assert.Equal(500, travel.OffsetX);
        Assert.Equal(0, travel.HeadingX);
        Assert.True(session.Engine.Observe(actor).Participant.Busy);
        Assert.Equal(new Cell(0, 0), CellOf(session));
        session.CompleteTravelTick();
        Assert.Equal(new Cell(1, 0), CellOf(session));
        Assert.Equal(500, session.Engine.TravelOf(actor).OffsetX);
    }

    [Fact]
    public void SaveLoadAndReplayMatchTheSameHeading()
    {
        var session = Fixtures.Session();
        var actor = new ParticipantId(1);
        session.SetTravelHeading(actor, 0, 1);
        for (int i = 0; i < 4; i++) session.AdvanceTravelQuantum();
        var loaded = GameSession.FromSessionJson(session.CreateSessionJson());
        Assert.Equal(session.Engine.TravelOf(actor), loaded.Engine.TravelOf(actor));
        Assert.Equal(session.Engine.TravelQuantum, loaded.Engine.TravelQuantum);
        Assert.Equal(session.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        for (int i = 0; i < 16; i++) { session.AdvanceTravelQuantum(); loaded.AdvanceTravelQuantum(); }
        Assert.Equal(session.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());

        var replay = GameSession.Replay(session.Engine.Configuration(), session.CommandTape, session.Engine.Tick);
        Assert.Equal(session.Engine.TravelOf(actor), replay.Engine.TravelOf(actor));
        Assert.Equal(session.Engine.ComputeStateHash(), replay.Engine.ComputeStateHash());
    }

    [Fact]
    public void OneRealTimeTickMovesTheSameDistanceAsTenQuanta()
    {
        var whole = Fixtures.Session();
        whole.SetTravelHeading(new(1), 1, 0);
        new TickScheduler(whole).AdvanceElapsed(TimeSpan.FromSeconds(1));
        var halves = Fixtures.Session();
        halves.SetTravelHeading(new(1), 1, 0);
        var scheduler = new TickScheduler(halves);
        scheduler.AdvanceElapsed(TimeSpan.FromSeconds(0.5));
        Assert.Equal(0, halves.Engine.Tick);
        scheduler.AdvanceElapsed(TimeSpan.FromSeconds(0.5));
        Assert.Equal(whole.Engine.Tick, halves.Engine.Tick);
        Assert.Equal(whole.Engine.TravelOf(new(1)), halves.Engine.TravelOf(new(1)));
        Assert.Equal(new Cell(1, 0), CellOf(whole));
        Assert.Equal(500, whole.Engine.TravelOf(new(1)).OffsetX);
    }

    [Fact]
    public void CenteredActorsKeepThePreviousStateHash()
    {
        var stepped = Fixtures.Session();
        var traveled = Fixtures.Session();
        stepped.Step();
        for (int i = 0; i < 10; i++) traveled.AdvanceTravelQuantum();
        Assert.Equal(0, traveled.Engine.TravelQuantum);
        Assert.Equal(stepped.Engine.ComputeStateHash(), traveled.Engine.ComputeStateHash());
    }

    [Fact]
    public void MissingTravelFieldsRestoreTheCellCenter()
    {
        var session = Fixtures.Session();
        session.Step();
        var json = JsonNode.Parse(session.Engine.CreateCheckpointJson())!.AsObject();
        foreach (var actor in json["actors"]!.AsArray())
        {
            var row = actor!.AsObject();
            row.Remove("offsetX"); row.Remove("offsetZ");
            row.Remove("headingX"); row.Remove("headingZ");
            row.Remove("facingX"); row.Remove("facingZ");
        }
        json.Remove("travelQuantum");
        var loaded = SimulationEngine.FromCheckpointJson(json.ToJsonString(SimulationJson.Options));
        Assert.Equal(new TravelState(500, 500, 0, 0, 0, -1), loaded.TravelOf(new(1)));
        Assert.Equal(session.Engine.ComputeStateHash(), loaded.ComputeStateHash());
    }

    [Fact]
    public void KeysAndStickShareOneHeading()
    {
        Assert.Equal(new TravelHeading(0, -1), TravelHeading.FromKeys(false, false, false, true));
        Assert.Equal(new TravelHeading(1, -1), TravelHeading.FromKeys(true, false, false, true));
        Assert.Equal(new TravelHeading(0, 0), TravelHeading.FromKeys(true, true, false, false));
        Assert.Equal(TravelHeading.FromKeys(true, false, false, true), TravelHeading.FromStick(800, -900));
        Assert.Equal(new TravelHeading(0, 0), TravelHeading.FromStick(499, -499));
        Assert.Throws<ArgumentOutOfRangeException>(() => TravelHeading.FromStick(1001, 0));
    }

    private static (Cell Cell, TravelState Travel) Run(int x, int z)
    {
        var session = Fixtures.Session();
        session.SetTravelHeading(new(1), x, z);
        for (int i = 0; i < 10; i++) session.AdvanceTravelQuantum();
        return (CellOf(session), session.Engine.TravelOf(new(1)));
    }

    private static long Span((Cell Cell, TravelState Travel) end)
    {
        long dx = end.Cell.X * 1000L + end.Travel.OffsetX - 500;
        long dz = end.Cell.Z * 1000L + end.Travel.OffsetZ - 500;
        return dx * dx + dz * dz;
    }

    private static Cell CellOf(GameSession session) => session.Engine.Observe(new(1)).Participant.Position;
}
