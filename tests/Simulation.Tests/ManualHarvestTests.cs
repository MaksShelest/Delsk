using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class ManualHarvestTests
{
    private static readonly ParticipantId Human = new(1);
    private static InventoryView Pack(GameSession s, long actor = 1) => s.Engine.Observe(new(actor)).OwnInventories
        .Single(i => i.Id == s.Engine.Observe(new(actor)).Participant.InventoryId);
    private static long Wood(GameSession s) => Pack(s).Stock.SingleOrDefault(i => i.ItemId == "wood")?.Quantity ?? 0;
    private static void Quanta(GameSession s, int count) { for (int i = 0; i < count; i++) s.AdvanceTravelQuantum(); }
    private static void Start(GameSession s) => Assert.Equal(ResultStatus.Accepted,
        s.Engine.Result(Fixtures.Command(s, PlayerDesk.StartHarvesting("wood")))!.Status);

    [Fact]
    public void OneCommandRepeatsRealCyclesWithOneCapacityHoldAndNoEarlyGoods()
    {
        var s = Fixtures.Session(); Start(s);
        long first = s.Engine.HarvestOf(Human)!.JobId;
        Assert.Equal(0, Wood(s)); Assert.Equal(1, Pack(s).ReservedCapacity);
        s.Step(); Assert.Equal(0, Wood(s));
        s.Step(); Assert.Equal(1, Wood(s)); Assert.Equal(1, Pack(s).ReservedCapacity);
        Assert.NotEqual(first, s.Engine.HarvestOf(Human)!.JobId);
        s.Step(); s.Step(); Assert.Equal(2, Wood(s));
        Assert.Single(s.CommandTape);
        Assert.Equal(2, s.Engine.Inspect().Flows.Single(f => f.ItemId == "wood").Harvested);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void FullInventoryStopsAfterLastCompleteUnitWithoutAutomaticRestart(long capacity)
    {
        var d = Fixtures.Cycle(); d = d with { Participants = d.Participants.Select(p => p with { InventoryCapacity = capacity }).ToArray() };
        var s = Fixtures.Session(d); Start(s);
        while (s.Engine.Tick < 10) s.Step();
        Assert.Null(s.Engine.HarvestOf(Human)); Assert.Equal(capacity, Wood(s)); Assert.Equal(0, Pack(s).ReservedCapacity);
        Assert.Single(s.Engine.PublicEventsSince(0), e => e.Type == "HarvestLoopStopped" && e.ActorId == Human && e.ReasonCode == "NoCapacity");
        Assert.Equal(s.Engine.ComputeStateHash(), CheckpointStore.Decode(CheckpointStore.Encode(s)).Engine.ComputeStateHash());
        Assert.Equal("NoCapacity", s.Engine.Result(Fixtures.Command(s, PlayerDesk.StartHarvesting("wood")))!.ReasonCode);
    }

    [Fact]
    public void StopWinsOnCompletionTickAndPreservesAlreadyCompletedUnits()
    {
        var s = Fixtures.Session(); Start(s); s.Step(); s.Step(); s.Step();
        Assert.Equal(1, Wood(s)); // Next unit would complete on tick 5.
        Fixtures.Command(s, PlayerDesk.StopHarvest());
        Assert.Equal(5, s.Engine.Tick); Assert.Equal(1, Wood(s));
        Assert.Null(s.Engine.HarvestOf(Human)); Assert.Equal(0, Pack(s).ReservedCapacity);
        Assert.Equal("Accepted", s.Engine.Result(Fixtures.Command(s, PlayerDesk.StopHarvest()))!.ReasonCode);
        s.Step(); Assert.Equal(1, Wood(s));
    }

    [Fact]
    public void CancelAffectsOnlyAuthenticatedActor()
    {
        var s = Fixtures.Session(); Start(s);
        Fixtures.Command(s, PlayerDesk.StartHarvesting("wood"), 2);
        Fixtures.Command(s, PlayerDesk.StopHarvest());
        Assert.Null(s.Engine.HarvestOf(Human)); Assert.NotNull(s.Engine.HarvestOf(new(2)));
        Assert.Equal(0, Pack(s).ReservedCapacity); Assert.Equal(1, Pack(s, 2).ReservedCapacity);
    }

    [Fact]
    public void DuplicateStartAndStopDoNotRestartOrReleaseAnotherCycle()
    {
        var s = Fixtures.Session(); Start(s); var input = s.CommandTape.Single();
        s.Step(); s.Step(); var next = s.Engine.HarvestOf(Human);
        Assert.Equal("Accepted", s.Submit(Human, input.Command)!.ReasonCode);
        Assert.Equal(next, s.Engine.HarvestOf(Human)); Assert.Equal(1, Wood(s));
        Fixtures.Command(s, PlayerDesk.StopHarvest()); var stop = s.CommandTape.Last();
        Start(s); var restarted = s.Engine.HarvestOf(Human);
        Assert.Equal("Accepted", s.Submit(Human, stop.Command)!.ReasonCode);
        Assert.Equal(restarted, s.Engine.HarvestOf(Human)); Assert.Equal(1, Pack(s).ReservedCapacity);
    }

    [Theory]
    [InlineData("plank", 1, "NoResourceNode")]
    [InlineData("wood", 0, "InvalidQuantity")]
    [InlineData("wood", 2, "InvalidQuantity")]
    public void InvalidStartDoesNotReserveOrCreateAJob(string item, long quantity, string reason)
    {
        var s = Fixtures.Session(); long next = s.Engine.Inspect().NextId;
        string id = Fixtures.Command(s, new(ActionKind.StartHarvesting, ItemId: item, Quantity: quantity));
        Assert.Equal(reason, s.Engine.Result(id)!.ReasonCode);
        Assert.Null(s.Engine.HarvestOf(Human)); Assert.Equal(0, Pack(s).ReservedCapacity);
        Assert.Equal(next, s.Engine.Inspect().NextId);
    }

    [Fact]
    public void SameResourceCellsKeepProgressButLeavingCancelsBeforeNextTickAndDoesNotRestart()
    {
        var d = Fixtures.Cycle() with { HarvestTicksPerUnit = 5, ResourceNodes = [new("wood", new(0, 0)), new("wood", new(1, 0))] };
        var s = Fixtures.Session(d); Start(s); var job = s.Engine.HarvestOf(Human);
        s.SetTravelHeading(Human, 1, 0); Quanta(s, 5);
        Assert.Equal(new Cell(1, 0), s.Engine.Observe(Human).Participant.Position);
        Assert.Equal(job, s.Engine.HarvestOf(Human));
        Quanta(s, 10); Assert.Equal(new Cell(2, 0), s.Engine.Observe(Human).Participant.Position);
        Assert.Null(s.Engine.HarvestOf(Human)); Assert.Equal(0, Pack(s).ReservedCapacity); Assert.Equal(0, Wood(s));
        s.SetTravelHeading(Human, -1, 0); Quanta(s, 10); s.SetTravelHeading(Human, 0, 0);
        Assert.Equal(new Cell(1, 0), s.Engine.Observe(Human).Participant.Position);
        Quanta(s, 60); Assert.Equal(0, Wood(s)); Assert.Null(s.Engine.HarvestOf(Human));
    }

    [Fact]
    public void WallAndMapEdgeDoNotCancelAJob()
    {
        var s = Fixtures.Session(Fixtures.Cycle() with { BlockedCells = [new(1, 0)] }); Start(s);
        s.SetTravelHeading(Human, 1, 0); Quanta(s, 20);
        Assert.Equal(1, Wood(s)); Assert.NotNull(s.Engine.HarvestOf(Human));
        s.SetTravelHeading(Human, -1, 0); Quanta(s, 40);
        Assert.NotNull(s.Engine.HarvestOf(Human)); Assert.Equal(3, Wood(s));
    }

    [Fact]
    public void InvalidMovePreservesHarvestAndValidMoveReleasesItsHold()
    {
        var s = Fixtures.Session(Fixtures.Cycle() with { HarvestTicksPerUnit = 5 }); Start(s);
        var job = s.Engine.HarvestOf(Human);
        Assert.Equal("NoPath", s.Engine.Result(Fixtures.Command(s, PlayerDesk.Move(-1, 0)))!.ReasonCode);
        Assert.Equal(job, s.Engine.HarvestOf(Human));
        Assert.Equal("Accepted", s.Engine.Result(Fixtures.Command(s, PlayerDesk.Move(1, 0)))!.ReasonCode);
        Assert.Null(s.Engine.HarvestOf(Human)); Assert.Equal(0, Pack(s).ReservedCapacity);
        Assert.False(s.Engine.CanTravel(Human, 1, 0));
        Assert.Equal("ParticipantBusy", s.Engine.Result(Fixtures.Command(s, PlayerDesk.StopHarvest()))!.ReasonCode);
        Assert.Equal(new Cell(1, 0), s.Engine.Observe(Human).Participant.Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MidQuantumSaveLoadAndRecordedReplayContinueExactly(bool leaveArea)
    {
        var s = Fixtures.Session(); Start(s); s.Step(); s.Step();
        s.SetTravelHeading(Human, leaveArea ? 1 : 0, leaveArea ? 0 : -1);
        Quanta(s, leaveArea ? 5 : 3);
        if (leaveArea) Assert.Null(s.Engine.HarvestOf(Human));
        else Assert.NotNull(s.Engine.HarvestOf(Human));
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        Assert.Equal(s.Engine.TravelQuantum, loaded.Engine.TravelQuantum);
        Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        for (int i = 0; i < 45; i++)
        {
            s.AdvanceTravelQuantum(); loaded.AdvanceTravelQuantum();
            Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        }
        s.SetTravelHeading(Human, 0, 0); loaded.SetTravelHeading(Human, 0, 0);
        while (s.Engine.TravelQuantum != 0) { s.AdvanceTravelQuantum(); loaded.AdvanceTravelQuantum(); }
        var replay = GameSession.Replay(s.Engine.Configuration(), s.CommandTape, s.Engine.Tick);
        Assert.Equal(s.Engine.ComputeStateHash(), replay.Engine.ComputeStateHash());
        Assert.Equal(s.Engine.EventsSince(0), replay.Engine.EventsSince(0));
        Assert.Equal(s.Engine.EventsSince(0), loaded.Engine.EventsSince(0));
    }

    [Fact]
    public void PauseFreezesCoreProgressAndResumeDoesNotCompleteEarly()
    {
        var s = Fixtures.Session(); Start(s); var clock = new TickScheduler(s);
        clock.AdvanceElapsed(TimeSpan.FromSeconds(.4));
        double progress = HarvestProgress.Percent(s.Engine.HarvestOf(Human), s.Engine.Tick, s.Engine.TravelQuantum);
        Assert.Equal(20, progress);
        clock.SetPaused(true); clock.AdvanceElapsed(TimeSpan.FromSeconds(30));
        Assert.Equal(progress, HarvestProgress.Percent(s.Engine.HarvestOf(Human), s.Engine.Tick, s.Engine.TravelQuantum));
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s)); Assert.Equal(s.Engine.HarvestOf(Human), loaded.Engine.HarvestOf(Human));
        clock.SetPaused(false); clock.AdvanceElapsed(TimeSpan.FromSeconds(1.5)); Assert.Equal(0, Wood(s));
        clock.AdvanceElapsed(TimeSpan.FromSeconds(.1)); Assert.Equal(1, Wood(s));
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("hold")]
    [InlineData("rules")]
    [InlineData("tail")]
    public void TamperedCheckpointsAreRejected(string field)
    {
        var s = Fixtures.Session(); Start(s);
        if (field == "tail") { s.SetTravelHeading(Human, 1, 0); Quanta(s, 5); }
        var json = JsonNode.Parse(s.Engine.CreateCheckpointJson())!;
        switch (field)
        {
            case "kind": json["actors"]![0]!["job"]!["kind"] = "OtherLoop"; break;
            case "hold": json["actors"]![0]!["job"]!["capacityHold"] = "foreign"; break;
            case "rules": json["rulesVersion"] = "baseline-1"; break;
            case "tail": json["events"]!.AsArray()[^1]!["reasonCode"] = "UserStopped"; break;
        }
        Assert.Throws<CheckpointException>(() => SimulationEngine.FromCheckpointJson(json.ToJsonString()));
    }

    [Fact]
    public void EquipmentCancellationClearsBothReferencesAndCanStartAgain()
    {
        using var stream = typeof(ManualHarvestTests).Assembly.GetManifestResourceStream("Fixture.equipment-cycle.json")!;
        var d = JsonSerializer.Deserialize<ScenarioDefinition>(stream, SimulationJson.Options)!;
        var s = Fixtures.Session(d);
        Fixtures.Command(s, new(ActionKind.BuyEquipment, Target: new(0, 0), EquipmentTypeId: "wood-extractor"));
        var equipment = s.Engine.Observe(Human).OwnEquipment.Single();
        Fixtures.Command(s, new(ActionKind.Harvest, Equipment: equipment.Id, ItemId: "wood", Quantity: 1));
        s.SetTravelHeading(Human, 1, 0); Quanta(s, 5);
        Assert.Null(s.Engine.HarvestOf(Human)); Assert.Equal(0, Pack(s).ReservedCapacity);
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(s));
        Assert.Equal(s.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        s.SetTravelHeading(Human, -1, 0); Quanta(s, 5); s.SetTravelHeading(Human, 0, 0);
        Assert.Equal("Accepted", s.Engine.Result(Fixtures.Command(s,
            new(ActionKind.Harvest, Equipment: equipment.Id, ItemId: "wood", Quantity: 1)))!.ReasonCode);
    }

    [Fact]
    public void PlayOffersContinuousHarvestAndDebugRetainsSingleStepWithAccessibleStop()
    {
        var s = Fixtures.Session(); var d = s.Engine.Configuration(); var view = PlayerDesk.Read(s.Engine, Human);
        Assert.Equal(ActionKind.StartHarvesting, Assert.Single(PlayContext.At(view, d, "ru", true),c=>c.Command is not null).Command!.Kind);
        Assert.Equal(ActionKind.Harvest, Assert.Single(PlayContext.At(view, d, "en"),c=>c.Command is not null).Command!.Kind);
        Start(s); view = PlayerDesk.Read(s.Engine, Human);
        foreach (string lang in new[] { "ru", "en" })
        {
            var stop = Assert.Single(PlayContext.At(view, d, lang, true),c=>c.Command is not null);
            Assert.True(stop.Enabled); Assert.Equal(ActionKind.StopHarvest, stop.Command!.Kind);
            Assert.Equal(ActionKind.StopHarvest, Assert.Single(PlayContext.At(view, d, lang),c=>c.Command is not null).Command!.Kind);
        }
    }
}
