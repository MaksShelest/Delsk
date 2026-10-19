using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class ElectionTests
{
    private static ScenarioDefinition Scenario(int[]? staff = null, long population = 100, long period = 10)
    {
        staff ??= [30, 20, 10, 0];
        var d = Fixtures.Cycle();
        return d with { Population = population, Elections = new(PeriodTicks: period),
            Participants = staff.Select((_, i) => new ParticipantDefinition(i + 1, 100000, new(0, 0), 20)).ToArray(),
            Enterprises = staff.Select((n, i) => new EnterpriseDefinition(i + 1, i + 1, new(10, 0), "sawmill", 40, 40, n, Math.Max(2, n), 100)).ToArray() };
    }
    private static GameSession Session(ScenarioDefinition? d = null) => new(new SimulationEngine(d ?? Scenario()));
    private static ElectionStateView State(GameSession s) => s.Engine.Inspect().Elections;
    private static GameAction Vote(long election, long candidate) => new(ActionKind.SetVoteChoice, Election: new(election), Candidate: new(candidate));
    private static string Execute(GameSession s, GameAction action, long actor = 1) => LandContentFixtures.Reason(s, action, actor);
    private static ElectionResult Result(GameSession s) => State(s).Results.Last();

    [Fact]
    public void CurrentBlocksHaveOneBallotEachUnemployedHaveNoneAndDefaultIsSelf()
    {
        var s = Session(); s.AdvanceTo(10); var r = Result(s);
        Assert.Equal(new long[] {31,21,11,1}, r.Ballots.Select(b => b.Weight));
        Assert.Equal(64, r.TotalWeight); Assert.Equal(40, r.Unemployed); Assert.Null(r.Winner);
        Assert.All(r.Ballots, b => Assert.Equal(b.Voter, b.Candidate));
        Assert.Equal(new ElectionId(2), State(s).NextElectionId); Assert.Equal(20, State(s).NextVotingTick);
        Assert.Equal(ElectionMode.Competitive, State(s).Mode);
    }
    [Fact]
    public void StrictMajorityAndDirectBallotsNeverRecursivelyTransferAnotherBlock()
    {
        var s = Session(); Assert.Equal("Accepted", Execute(s, Vote(1,1), 2)); Assert.Equal("Accepted", Execute(s, Vote(1,2), 3));
        s.AdvanceTo(10); var r = Result(s);
        Assert.Equal(new[] {new ElectionTally(new(1),52),new ElectionTally(new(2),11),new ElectionTally(new(4),1)}, r.Tally.ToArray());
        Assert.Equal(new ParticipantId(1), r.Winner); Assert.Equal(new ElectionVictory(new(1),new(1),10), State(s).FirstVictory);
        Assert.Equal(ElectionMode.Victory, State(s).Mode); Assert.Null(State(s).NextElectionId); Assert.Null(State(s).NextVotingTick);
        Assert.Equal(4, s.Engine.EventsSince(0).Count(e => e.Type == "ElectionBallot" && e.Phase == 8));
    }
    [Fact]
    public void ExactlyHalfIsNotVictoryAndNextElectionUsesNewIdAndFreshChoices()
    {
        var s = Session(Scenario([30,30,0,0]));
        Execute(s, Vote(1,1), 3); Execute(s, Vote(1,2), 4); s.AdvanceTo(10);
        Assert.Equal(new long[]{32,32}, Result(s).Tally.Select(t => t.Weight)); Assert.Null(Result(s).Winner);
        Assert.Equal("ElectionClosed", Execute(s, Vote(1,1),2));
        s.AdvanceTo(20); Assert.Equal(2, State(s).Results.Length);
        Assert.All(Result(s).Ballots, b => Assert.Equal(b.Voter,b.Candidate)); Assert.Null(State(s).FirstVictory);
        Assert.Equal(new ElectionId(3),State(s).NextElectionId);
    }
    [Fact]
    public void LatestChoiceOnVotingTickWinsOnceAndOldOrFutureElectionIdsAreRejected()
    {
        var s = Session(Scenario([0,0], population: 100));
        Assert.Equal("ElectionClosed", Execute(s, Vote(0,2))); Assert.Equal("ElectionClosed", Execute(s, Vote(2,2)));
        Assert.Equal("NotAvailable", Execute(s, Vote(1,99)));
        s.AdvanceTo(9); s.Send(new(1),Vote(1,1)); s.Send(new(1),Vote(1,2)); s.Step();
        Assert.Equal(new ParticipantId(2),Result(s).Winner); Assert.Equal(2,Result(s).Ballots.Length);
        Assert.Equal(2,Result(s).TotalWeight); Assert.Empty(State(s).Choices);
        Assert.Equal("ElectionClosed",Execute(s,Vote(1,1)));
    }
    [Fact]
    public void LaborDeltaOnVotingTickIsAppliedBeforeTheBallotAndIdleWorkersRemainUntilTheyLeave()
    {
        var d = Scenario([2,0],population: 5,period: 2) with {Labor = new(StepTicks:2,MobilityNumerator:1,MobilityDenominator:2,GrowthPerPeriod:0)};
        var s = Session(d); s.AdvanceTo(1); Assert.Equal(2,s.Engine.Observe(new(1)).OwnEmploymentGroups.Single().Headcount);
        s.Step(); var r=Result(s);
        Assert.Equal(new long[]{2,1},r.Ballots.Select(b=>b.Weight)); Assert.Equal(4,r.Unemployed);
        var changed=Assert.Single(s.Engine.EventsSince(0),e=>e.Type=="EmploymentChanged");
        var ballot=s.Engine.EventsSince(0).First(e=>e.Type=="ElectionBallot"); Assert.Equal(5,changed.Phase); Assert.True(changed.Sequence<ballot.Sequence);
    }
    [Fact]
    public void HiringOnVotingTickCountsNewWorkersWithoutInventingEarlierProduction()
    {
        var d=Fixtures.Cycle(staff:0) with {Elections=new(40),Labor=new(StepTicks:40,MobilityNumerator:1,MobilityDenominator:1,GrowthPerPeriod:0)};
        var s=Session(d); Fixtures.Supply(s); Execute(s,new(ActionKind.SetProductionEnabled,Enterprise:new(1),Enabled:true));
        s.AdvanceTo(39); Assert.Equal(0,s.Engine.Observe(new(1)).OwnEmploymentGroups.Single().Headcount);
        s.Step(); Assert.Equal(2,Result(s).Ballots[0].WorkerWeight); Assert.Equal(new ParticipantId(1),Result(s).Winner);
        Assert.Equal(0,s.Engine.Inspect().Flows.Single(f=>f.ItemId=="plank").Produced);
    }
    [Fact]
    public void NewEquipmentAfterAnElectionDoesNotRewriteEarlierEmploymentSnapshots()
    {
        var d=Scenario([0,0]) with {ResourceNodes=[new("wood",new(0,0),new(false,InitialOwner:1))],EquipmentTypes=[new("extractor","wood",[new(1,1,2,20,20,1,2,1)])]};
        var s=Session(d); s.AdvanceTo(10); var before=JsonSerializer.Serialize(Result(s));
        Assert.Equal("Accepted",Execute(s,new(ActionKind.BuyEquipment,EquipmentTypeId:"extractor",Target:new(0,0))));
        s.AdvanceTo(20); Assert.Equal(3,Result(s).Employment.Length); Assert.Equal(2,State(s).Results[0].Employment.Length);
        Assert.Equal(before,JsonSerializer.Serialize(State(s).Results[0]));
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s)); s.AdvanceTo(30);loaded.AdvanceTo(30);
        Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());
    }
    [Fact]
    public void PauseAndDirectLoadBeforeElectionNeverAdvanceOfflineTime()
    {
        var s=Session(); s.AdvanceTo(9); s.SetPaused(true); var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));
        var scheduler=new TickScheduler(loaded); scheduler.AdvanceElapsed(TimeSpan.FromDays(1));
        Assert.Equal(9,loaded.Engine.Tick); Assert.Empty(State(loaded).Results);
        scheduler.SetPaused(false); scheduler.Headless(1); Assert.Equal(10,loaded.Engine.Tick); Assert.Single(State(loaded).Results);
    }
    [Fact]
    public void ControllersUseTheSameElectionObservationBarrierAndOrdinaryAuthenticatedCommands()
    {
        var d=Scenario([0,0]); var s=Session(d);
        s.AttachControllers([new VotingController(new(1),new(2)),new VotingController(new(2),new(2))]); s.AdvanceTo(10);
        Assert.Equal(new ParticipantId(2),Result(s).Winner);
        Assert.Equal(2,s.CommandTape.Length); Assert.All(s.CommandTape,c=>Assert.Equal(c.AuthenticatedActor,c.Command.ActorId));
        Assert.All(s.CommandTape,c=>Assert.Equal(0,c.SubmittedAtTick));
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s)); loaded.RestoreControllers(c=>new VotingController(c.Actor,new(2)));
        s.AdvanceTo(20);loaded.AdvanceTo(20); Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());
        Assert.Equal(s.ComputeControllerStateHash(),loaded.ComputeControllerStateHash());
    }
    private sealed class VotingController(ParticipantId actor,ParticipantId candidate):IController
    {
        public ParticipantId Actor=>actor;
        public ControllerDecision Decide(ControllerObservation observation,System.Collections.Immutable.ImmutableArray<CommandResult> ownResults)
            => new("vote","fixture",observation.Tick==0?[new(ActionKind.SetVoteChoice,Election:observation.Elections.NextElectionId!.Value,Candidate:candidate)]:[]);
        public ControllerCheckpoint Capture()=>new("election-fixture",1,actor,"{}",[]);
        public void CommandSubmitted(string commandId) { }
    }
    [Fact]
    public void ProductionExtractionAndOneSharedLogisticsGroupContributeExactlyOnce()
    {
        var d=Fixtures.Cycle() with {ResourceNodes=[new("wood",new(0,0),new(false,InitialOwner:1))],Population=8,Elections=new(60),Labor=new(StepTicks:1,MobilityNumerator:1,MobilityDenominator:1,GrowthPerPeriod:0),
            Enterprises=[Fixtures.Cycle().Enterprises.Single() with {Position=new(0,0)}],
            Recipes=[Fixtures.Cycle().Recipes.Single() with {FullStaffTicks=3}],
            EquipmentTypes=[new("extractor","wood",[new(1,1,2,1000,2,2,2,1)])],Logistics=new(PurchasePriceMinor:1,WageMinor:1)};
        var s=Session(d); Assert.Equal("Accepted",Execute(s,new(ActionKind.BuyEquipment,EquipmentTypeId:"extractor",Target:new(0,0))));
        var machine=s.Engine.Observe(new(1)).OwnEquipment.Single();
        s.Send(new(1),new(ActionKind.SetEquipmentMode,Equipment:machine.Id,Mode:EquipmentMode.Automatic));
        Assert.Equal("Accepted",Execute(s,new(ActionKind.Harvest,ItemId:"wood",Quantity:20))); Fixtures.UntilIdle(s);
        var o=s.Engine.Observe(new(1)); var enterprise=o.OwnEnterprises.Single();
        Assert.Equal("Accepted",Execute(s,new(ActionKind.Transfer,Source:o.Participant.InventoryId,Destination:enterprise.InputId,ItemId:"wood",Quantity:20)));
        s.Send(new(1),new(ActionKind.SetProductionEnabled,Enterprise:enterprise.Id,Enabled:true));
        Assert.Equal("Accepted",Execute(s,new(ActionKind.BuyLogistics,Enterprise:enterprise.Id)));
        Assert.Equal("Accepted",Execute(s,new(ActionKind.AcceptDeliveryQuantity,OfferId:2,Quantity:5)));
        s.AdvanceTo(s.Engine.Tick+5); var contract=s.Engine.Inspect().DeliveryContracts.Single();
        var logistics=s.Engine.Observe(new(1)).OwnLogistics.Single();
        Assert.Equal("Accepted",Execute(s,new(ActionKind.SetLogisticsContract,Logistics:logistics.Id,ContractId:contract.Id,Enabled:true)));
        s.AdvanceTo(59); var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s)); s.Step(); loaded.Step();
        var r=Result(s); Assert.Equal(3,r.Employment.Length); Assert.Equal(3,r.Employment.Select(g=>g.Group).Distinct().Count());
        Assert.All(r.Employment,g=>Assert.Equal(2,g.Headcount)); Assert.Equal(6,r.Ballots[0].WorkerWeight);
        Assert.Equal(8,r.TotalWeight); Assert.Equal(2,r.Unemployed); Assert.NotNull(s.Engine.Inspect().Logistics.Single().Trip);
        Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());
    }
    [Fact]
    public void WinnerIsPermanentAndContinueCreatesSandboxWithoutAnotherElection()
    {
        var s=Session(Scenario() with {Labor=new(StepTicks:20,MobilityNumerator:1,MobilityDenominator:1,GrowthPerPeriod:0)}); Assert.Equal("NoVictory",Execute(s,new(ActionKind.ContinueAfterVictory)));
        Execute(s,Vote(1,1),2); s.AdvanceTo(10); var first=State(s).FirstVictory;
        Assert.Equal("NotAvailable",Execute(s,new(ActionKind.ContinueAfterVictory),4));
        Assert.Equal("Accepted",Execute(s,new(ActionKind.ContinueAfterVictory),1));
        Assert.Equal(ElectionMode.Sandbox,State(s).Mode); Assert.Equal(12,State(s).SandboxStartedAtTick);
        Assert.Equal("AlreadySandbox",Execute(s,new(ActionKind.ContinueAfterVictory),1));
        Assert.Equal("ElectionClosed",Execute(s,Vote(2,2),2)); s.AdvanceTo(100);
        Assert.All(s.Engine.Inspect().EmploymentGroups,g=>Assert.Equal(0,g.Headcount));
        Assert.Equal(first,State(s).FirstVictory); Assert.Single(State(s).Results); Assert.Single(s.Engine.EventsSince(0),e=>e.Type=="FirstVictory");
    }
    [Fact]
    public void FutureDeclarationsArePrivateButFinalBallotsArePublicAndObservationIsImmutable()
    {
        var s=Session(); Execute(s,Vote(1,1),2); string hash=s.Engine.ComputeStateHash();
        var own=s.Engine.ObserveController(new(2)); var other=s.Engine.ObserveController(new(3));
        Assert.Equal(new ParticipantId(1),own.Elections.OwnChoice); Assert.Null(other.Elections.OwnChoice);
        Assert.Empty(other.Elections.Results); Assert.Equal(4,other.Elections.Participants.Length);
        var altered=other.Elections.Participants.SetItem(0,new(999)); Assert.Equal(new ParticipantId(1),s.Engine.ObserveController(new(3)).Elections.Participants[0]);
        Assert.NotEqual(altered,other.Elections.Participants); Assert.Equal(hash,s.Engine.ComputeStateHash());
        s.AdvanceTo(10); Assert.Equal(new ParticipantId(1),s.Engine.ObserveController(new(3)).Elections.Results[0].Ballots[1].Candidate);
        Assert.Empty(other.Elections.Results); Assert.Single(own.Elections.Participants.Where(p=>p.Value==2));
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(9)] [InlineData(10)] [InlineData(19)] [InlineData(20)] [InlineData(21)] [InlineData(39)]
    public void DirectSaveBeforeAndAfterElectionsAndVictoryReplaysEveryHash(int cut)
    {
        var d=Scenario([30,30,0,0]); var s=Session(d);
        s.Send(new(3),Vote(1,1),delayTicks:2); s.Send(new(4),Vote(1,2),delayTicks:2);
        s.Send(new(2),Vote(2,1),delayTicks:20); s.Send(new(1),new(ActionKind.ContinueAfterVictory),delayTicks:21);
        s.AdvanceTo(cut); var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));
        Assert.Equal(s.Engine.Tick,loaded.Engine.Tick); Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());
        while(s.Engine.Tick<60) {s.Step();loaded.Step();Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());}
        Assert.Equal(s.Engine.EventsSince(0),loaded.Engine.EventsSince(0)); Assert.Equal(s.Engine.Ledger(),loaded.Engine.Ledger());
        var replay=GameSession.Replay(d,s.CommandTape,60); Assert.Equal(s.Engine.ComputeStateHash(),replay.Engine.ComputeStateHash());
        Assert.Equal(JsonSerializer.Serialize(State(s)),JsonSerializer.Serialize(State(replay))); Assert.Equal(ElectionMode.Sandbox,State(s).Mode);
    }
    [Fact]
    public void DedupAndSameSeedInputHaveIdenticalBallotsEventsAndHashes()
    {
        var d=Scenario(); var a=Session(d); var b=Session(d);
        string id=a.Send(new(2),Vote(1,1)); var input=Assert.Single(a.CommandTape); Assert.Null(a.Submit(new(2),input.Command));
        b.Submit(new(2),input.Command); a.AdvanceTo(40);b.AdvanceTo(40);
        Assert.Equal(a.Engine.ComputeStateHash(),b.Engine.ComputeStateHash()); Assert.Equal(a.Engine.EventsSince(0),b.Engine.EventsSince(0));
        Assert.Equal(ResultStatus.Accepted,a.Engine.Enqueue(new(2),input.Command)!.Status);
        Assert.Equal(4,a.Engine.EventsSince(0).Count(e=>e.Type=="ElectionBallot")); Assert.Equal(id,input.Command.CommandId);
    }
    [Fact]
    public void WideMajorityComparisonDoesNotOverflowLong()
    {
        var d=Scenario([0,0,0,0],population:0) with {Elections=new(2,long.MaxValue/4)}; var s=Session(d);
        s.Send(new(2),Vote(1,1)); s.Send(new(3),Vote(1,1)); s.AdvanceTo(2);
        Assert.Equal(new ParticipantId(1),Result(s).Winner); Assert.True(Result(s).Tally[0].Weight>long.MaxValue/2);
    }
    [Theory]
    [InlineData("period")] [InlineData("base")] [InlineData("overflow")] [InlineData("growth")]
    public void InvalidConfigurationIsRejectedBeforeWorldCreation(string field)
    {
        var d=Scenario(); d=field switch {"period"=>d with{Elections=new(0)},"base"=>d with{Elections=new(BaseVote:0)},
            "overflow"=>d with{Elections=new(BaseVote:long.MaxValue/4)},
            _=>d with{Population=long.MaxValue-4,Labor=new(GrowthPeriodTicks:1,GrowthPerPeriod:1)}};
        Assert.Throws<ArgumentException>(()=>new SimulationEngine(d));
    }
    [Theory]
    [InlineData("id")] [InlineData("tick")] [InlineData("duplicate")] [InlineData("weight")] [InlineData("workers")] [InlineData("total")]
    [InlineData("winner")] [InlineData("victory")] [InlineData("mode")] [InlineData("choice")]
    public void InconsistentElectionCheckpointIsRejectedBeforeHashCheck(string field)
    {
        var s=Session(Scenario([30,30,0,0])); s.AdvanceTo(10);
        var saved=JsonNode.Parse(s.Engine.CreateCheckpointJson())!; var e=saved["elections"]!; var r=e["results"]![0]!;
        switch(field) {
            case "id":e["nextElectionId"]!["value"]=9;break;
            case "tick":e["nextVotingTick"]=21;break;
            case "duplicate":r["ballots"]!.AsArray().Add(r["ballots"]![0]!.DeepClone());break;
            case "weight":r["ballots"]![0]!["workerWeight"]=31;break;
            case "workers":r["employment"]![0]!["headcount"]=31;break;
            case "total":r["totalWeight"]=65;break;
            case "winner":r["winner"]=new JsonObject{["value"]=1};break;
            case "victory":e["firstVictory"]=new JsonObject{["election"]=new JsonObject{["value"]=1},["winner"]=new JsonObject{["value"]=1},["tick"]=10};break;
            case "mode":e["mode"]="Sandbox";break;
            case "choice":e["choices"]!.AsArray().Add(new JsonObject{["voter"]=new JsonObject{["value"]=1},["candidate"]=new JsonObject{["value"]=2}});break;
        }
        var error=Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(saved.ToJsonString(SimulationJson.Options)));
        Assert.NotEqual("StateHashMismatch",error.ReasonCode);
    }
}
