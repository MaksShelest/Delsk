using System.Collections.Immutable;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class SessionBoundaryReviewTests
{
    [Theory]
    [InlineData("core-quantum")]
    [InlineData("session-step")]
    [InlineData("session-quantum")]
    [InlineData("scheduler")]
    public void TickLimitRejectsAdvancementBeforeChangingTravelOrHistory(string entry)
    {
        var definition = RoadE2Tests.Prepared() with { MaxTicks = 1 };
        var session = new GameSession(new(definition));
        session.AdvanceTo(1);
        session.SetTravelHeading(new(1), 1, 0);
        string before = session.Engine.CreateCheckpointJson();
        var tape = session.CommandTape;

        Assert.Throws<InvalidOperationException>(() =>
        {
            switch (entry)
            {
                case "core-quantum": session.Engine.MoveTravelQuantum(); break;
                case "session-step": session.Step(); break;
                case "session-quantum": session.AdvanceTravelQuantum(); break;
                default: new TickScheduler(session).AdvanceElapsed(TimeSpan.FromSeconds(1)); break;
            }
        });

        Assert.Equal(before, session.Engine.CreateCheckpointJson());
        Assert.Equal(tape, session.CommandTape);
        var restored = CheckpointStore.Decode(CheckpointStore.Encode(session));
        Assert.Equal(session.Engine.ComputeStateHash(), restored.Engine.ComputeStateHash());
    }

    [Fact]
    public void TickLimitDoesNotCallHostCallbackOrControllerAgain()
    {
        int callbacks = 0;
        var session = new GameSession(new(Fixtures.Cycle() with { MaxTicks = 1 }), beforeStep: () => callbacks++);
        var controller = new ProbeController();
        session.AttachControllers([controller]);
        session.Step();
        string state = session.ComputeControllerStateHash();

        Assert.Throws<InvalidOperationException>(session.Step);

        Assert.Equal(1, callbacks);
        Assert.Equal(1, controller.Calls);
        Assert.Equal(state, session.ComputeControllerStateHash());
    }

    [Fact]
    public void FrozenFinalElectionAndPausedSessionKeepTheirExistingControlSemantics()
    {
        var session = new GameSession(new(StoppedElectionTests.Definition() with { MaxTicks = 3 }));
        session.AdvanceTo(3);
        Assert.True(session.Engine.TickLimitReached);
        Assert.True(session.Engine.WaitingForBallots);
        session.Step(); session.AdvanceTravelQuantum();
        foreach (var actor in session.Engine.Configuration().Participants)
            session.Send(new(actor.Id), new(ActionKind.SetVoteChoice, Election: new(1), Candidate: new(1)));
        Assert.False(session.Engine.WaitingForBallots);
        Assert.NotNull(session.Engine.Inspect().Elections.FirstVictory);
        session.SetPaused(true);
        string before = session.Engine.CreateCheckpointJson();
        session.Step(); session.AdvanceTravelQuantum();
        Assert.Equal(before, session.Engine.CreateCheckpointJson());
        var restored = CheckpointStore.Decode(CheckpointStore.Encode(session));
        Assert.Equal(session.Engine.ComputeStateHash(), restored.Engine.ComputeStateHash());
    }

    private sealed class ProbeController : IController
    {
        public ParticipantId Actor => new(1);
        public int Calls { get; private set; }
        public ControllerDecision Decide(ControllerObservation observation, ImmutableArray<CommandResult> feedback)
        { Calls++; return new("Probe", "Boundary", [], false); }
        public void CommandSubmitted(string id) { }
        public ControllerCheckpoint Capture() => new("boundary-test", 1, Actor, Calls.ToString(), []);
    }
}
