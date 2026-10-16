using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class SupportTests
{
    internal static ScenarioDefinition Scenario(long period = 100)
    {
        var d = Fixtures.Cycle();
        return d with { Id = "support-test", Population = 100, Elections = new(period), Support = new(new(5,0)),
            Participants = Enumerable.Range(1,4).Select(i=>new ParticipantDefinition(i,100000,new(0,0),20)).ToArray(),
            Enterprises = new[] {30,20,10,0}.Select((n,i)=>new EnterpriseDefinition(i+1,i+1,new(10,0),"sawmill",40,40,n,Math.Max(2,n),100)).ToArray() };
    }
    private static GameSession Session(ScenarioDefinition? d = null)=>new(new SimulationEngine(d??Scenario()));
    private static string Execute(GameSession s,GameAction a,long actor=1)=>LandContentFixtures.Reason(s,a,actor);
    private static InventoryId Section(GameSession s,long actor)=>s.Engine.ObserveController(new(actor)).Support.OwnSection;
    private static SupportProposal Proposal(GameSession s,long id=1)=>s.Engine.InspectSupport().Proposals.Single(p=>p.Id==id);
    private static GameAction Action(ActionKind kind,long id=1)=>new(kind,SupportProposalId:id);
    private static long Propose(GameSession s,long buyer=1,long seller=2,long quantity=4)
    {
        Assert.Equal("Accepted",Execute(s,new(ActionKind.ProposeSupport,SupportSeller:new(seller),Election:s.Engine.Inspect().Elections.NextElectionId!.Value,
            Compensation:[new("wood",quantity)]),buyer));
        return s.Engine.ObserveController(new(buyer)).Support.Proposals.Where(p=>p.Candidate.Value==buyer).Max(p=>p.Id);
    }
    private static void Move(GameSession s,long actor,Cell cell)
    {Assert.Equal("Accepted",Execute(s,new(ActionKind.MoveTo,Target:cell),actor));Fixtures.UntilIdle(s,actor);}
    private static void Deliver(GameSession s,long actor=1,long quantity=4)
    {
        if(s.Engine.Observe(new(actor)).Participant.Position!=new Cell(0,0))Move(s,actor,new(0,0));
        Assert.Equal("Accepted",Execute(s,new(ActionKind.Harvest,ItemId:"wood",Quantity:quantity),actor));Fixtures.UntilIdle(s,actor);
        Move(s,actor,new(5,0)); Assert.Equal("Accepted",Execute(s,new(ActionKind.Transfer,Source:s.Engine.Observe(new(actor)).Participant.InventoryId,
            Destination:Section(s,actor),ItemId:"wood",Quantity:quantity),actor));
    }
    private static GameSession Accepted(bool delivered=true)
    {var s=Session();if(delivered)Deliver(s);Propose(s);Assert.Equal("Accepted",Execute(s,Action(ActionKind.AcceptSupport),2));return s;}
    [Fact]
    public void SignTransfersFullDeliveredCompensationAndCreatesOneCommitmentAtomically()
    {
        var s=Accepted();var before=s.Engine.Inspect();var p=Proposal(s);
        Assert.Equal(SupportProposalStatus.AcceptedAwaitingResources,p.Status);Assert.Equal(4,Assert.Single(p.Reserved).Quantity);
        Assert.Equal("Accepted",Execute(s,Action(ActionKind.SignSupport)));
        Assert.Equal(0,Fixtures.Count(s,Section(s,1),"wood"));Assert.Equal(4,Fixtures.Count(s,Section(s,2),"wood"));
        var c=Assert.Single(s.Engine.InspectSupport().Commitments);Assert.Equal(PoliticalCommitmentStatus.Active,c.Status);
        Assert.Equal(new ParticipantId(2),c.Seller);Assert.Equal(new ParticipantId(1),c.Candidate);Assert.Empty(Proposal(s).Reserved);
        Assert.Equal(before.Accounts,s.Engine.Inspect().Accounts);Assert.Equal(before.Flows,s.Engine.Inspect().Flows);
        Assert.Equal("NotAvailable",Execute(s,Action(ActionKind.SignSupport)));Assert.Single(s.Engine.InspectSupport().Commitments);
        Assert.Equal(4,Fixtures.Count(s,Section(s,2),"wood"));
    }
    [Fact]
    public void IncompleteAndRemoteMaterialsDoNotSignAndPartialHoldIsExclusive()
    {
        var s=Accepted(false);Deliver(s,quantity:2);long before=Fixtures.Count(s,Section(s,2),"wood");
        Assert.Equal("IncompleteCompensation",Execute(s,Action(ActionKind.SignSupport)));
        Assert.Equal(before,Fixtures.Count(s,Section(s,2),"wood"));Assert.Empty(s.Engine.InspectSupport().Commitments);
        Assert.Equal(2,Assert.Single(Proposal(s).Reserved).Quantity);
        Assert.Equal("NoStock",Execute(s,new(ActionKind.Transfer,Source:Section(s,1),Destination:s.Engine.Observe(new(1)).Participant.InventoryId,ItemId:"wood",Quantity:1)));
        Deliver(s,quantity:2);Assert.Equal("Accepted",Execute(s,Action(ActionKind.SignSupport)));Assert.Equal(4,Fixtures.Count(s,Section(s,2),"wood"));
    }
    [Fact]
    public void DestinationCapacityFailureDoesNotPartiallyTransferOrCreateCommitment()
    {
        var d=Scenario() with {Support=new(new(5,0),SectionCapacity:4)};
        var narrow=Session(d);Deliver(narrow);Deliver(narrow,2,4);Propose(narrow);Execute(narrow,Action(ActionKind.AcceptSupport),2);
        Assert.Equal("NoCapacity",Execute(narrow,Action(ActionKind.SignSupport)));
        Assert.Empty(narrow.Engine.InspectSupport().Commitments);Assert.Equal(4,Fixtures.Count(narrow,Section(narrow,1),"wood"));
        Assert.Equal(4,Fixtures.Count(narrow,Section(narrow,2),"wood"));Assert.Equal(4,Assert.Single(Proposal(narrow).Reserved).Quantity);
    }
    [Fact]
    public void SellerCanPromiseOnlyOneOwnBlockAndBoughtSupportNeverBecomesResellableWeight()
    {
        var s=Accepted();Execute(s,Action(ActionKind.SignSupport));
        long competing=Propose(s,3,2,2);Execute(s,Action(ActionKind.AcceptSupport,competing),2);Deliver(s,3,2);
        Assert.Equal("NotAvailable",Execute(s,Action(ActionKind.SignSupport,competing),3));
        // B can buy C's own block, although B's own block already supports A.
        long next=Propose(s,2,3,2);Execute(s,Action(ActionKind.AcceptSupport,next),3);Move(s,2,new(5,0));
        Assert.Equal("Accepted",Execute(s,Action(ActionKind.SignSupport,next),2));
        Execute(s,new(ActionKind.SetVoteChoice,Election:new(1),Candidate:new(2)),2);
        Execute(s,new(ActionKind.SetVoteChoice,Election:new(1),Candidate:new(3)),3);
        s.AdvanceTo(100);var r=s.Engine.Inspect().Elections.Results.Single();
        Assert.Equal(new[]{new ElectionTally(new(1),52),new ElectionTally(new(2),11),new ElectionTally(new(4),1)},r.Tally.ToArray());
        Assert.All(s.Engine.InspectSupport().Commitments,c=>{Assert.Equal(PoliticalCommitmentStatus.Executed,c.Status);Assert.Equal(100,c.ExecutedTick);});
    }
    [Fact]
    public void CandidateMayBuyOnlyForItselfAndCannotAcceptOnBehalfOfSeller()
    {
        var s=Session();Assert.Equal("NotAvailable",Execute(s,new(ActionKind.ProposeSupport,SupportSeller:new(2),Candidate:new(3),Election:new(1),Compensation:[new("wood",1)])));
        Propose(s);Assert.Equal("NotAvailable",Execute(s,Action(ActionKind.AcceptSupport)));
        Assert.Equal("NotAvailable",Execute(s,Action(ActionKind.AcceptSupport),3));Assert.Equal(SupportProposalStatus.Proposed,Proposal(s).Status);
    }
    [Fact]
    public void CurrentWorkforceAtElectionDeterminesBoundVoteRatherThanSigningHeadcount()
    {
        var d=Scenario() with {Labor=new(StepTicks:100,MobilityNumerator:1,MobilityDenominator:2,GrowthPerPeriod:0)};
        var s=Session(d);Deliver(s);Propose(s);Execute(s,Action(ActionKind.AcceptSupport),2);Execute(s,Action(ActionKind.SignSupport));
        s.AdvanceTo(100);var r=s.Engine.Inspect().Elections.Results.Single();
        Assert.Equal(10,r.Ballots.Single(b=>b.Voter.Value==2).WorkerWeight);Assert.Equal(11,r.Ballots.Single(b=>b.Voter.Value==2).Weight);
        Assert.Equal(new ParticipantId(1),r.Ballots.Single(b=>b.Voter.Value==2).Candidate);Assert.Equal(70,r.Unemployed);
    }
    [Fact]
    public void CommitmentExecutesWithoutWinnerAndNeverCarriesToNextElection()
    {
        var d=Scenario() with {Enterprises=[],Population=40};var s=Session(d);Deliver(s);Propose(s);Execute(s,Action(ActionKind.AcceptSupport),2);Execute(s,Action(ActionKind.SignSupport));
        s.AdvanceTo(100);var first=s.Engine.Inspect().Elections.Results.Single();Assert.Null(first.Winner);Assert.Equal(4,first.TotalWeight);
        Assert.Equal(PoliticalCommitmentStatus.Executed,Assert.Single(s.Engine.InspectSupport().Commitments).Status);
        s.AdvanceTo(200);var second=s.Engine.Inspect().Elections.Results.Last();Assert.All(second.Ballots,b=>Assert.Equal(b.Voter,b.Candidate));
        Assert.Equal(4,Fixtures.Count(s,Section(s,2),"wood"));Assert.Single(s.Engine.EventsSince(0),e=>e.Type=="SupportSigned");
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UnsignedProposalExpiresOnInclusiveVotingTickAndReleasesOnlyItsHolds(bool accept)
    {
        var s=Session();Deliver(s,quantity:2);Propose(s);if(accept)Execute(s,Action(ActionKind.AcceptSupport),2);
        s.AdvanceTo(99);Assert.Equal(accept?2:0,Proposal(s).Reserved.Sum(m=>m.Quantity));s.Step();
        Assert.Equal(SupportProposalStatus.Expired,Proposal(s).Status);Assert.Equal(100,Proposal(s).ClosedTick);Assert.Empty(Proposal(s).Reserved);
        Assert.Equal(2,Fixtures.Count(s,Section(s,1),"wood"));Assert.Empty(s.Engine.InspectSupport().Commitments);
    }
    [Fact]
    public void SignOnVotingTickRunsBeforeExpiryAndOverridesOrdinaryChoice()
    {
        var s=Accepted();s.AdvanceTo(99);s.Send(new(2),new(ActionKind.SetVoteChoice,Election:new(1),Candidate:new(3)));
        var id=s.Send(new(1),Action(ActionKind.SignSupport));s.Step();Assert.Equal(ResultStatus.Accepted,s.Engine.Result(id)!.Status);
        Assert.Equal(new ParticipantId(1),s.Engine.Inspect().Elections.Results.Single().Ballots.Single(b=>b.Voter.Value==2).Candidate);
        Assert.Equal(PoliticalCommitmentStatus.Executed,s.Engine.InspectSupport().Commitments.Single().Status);
    }
    [Fact]
    public void RejectionReleasesPartialReservationsWithoutTransferAndCannotUndoSignedDeal()
    {
        var s=Accepted(false);Deliver(s,quantity:2);Assert.Equal("Accepted",Execute(s,Action(ActionKind.RejectSupport),2));
        Assert.Equal(SupportProposalStatus.Rejected,Proposal(s).Status);Assert.Empty(Proposal(s).Reserved);Assert.Equal(0,Fixtures.Count(s,Section(s,2),"wood"));
        long id=Propose(s,quantity:2);Execute(s,Action(ActionKind.AcceptSupport,id),2);Execute(s,Action(ActionKind.SignSupport,id));
        Assert.Equal("NotAvailable",Execute(s,Action(ActionKind.RejectSupport,id),2));
    }
    [Fact]
    public void ThirdPartyObservationPublicEventsErrorsAndGenericDumpHidePrivateTerms()
    {
        var s=Session();Deliver(s);var control=CheckpointStore.Decode(CheckpointStore.Encode(s));
        Propose(s);control.Step();Execute(s,Action(ActionKind.AcceptSupport),2);control.Step();Execute(s,Action(ActionKind.SignSupport));control.Step();
        Assert.Equal(JsonSerializer.Serialize(control.Engine.ObserveController(new(3))),JsonSerializer.Serialize(s.Engine.ObserveController(new(3))));
        Assert.Equal(control.Engine.PublicEventsSince(0),s.Engine.PublicEventsSince(0));
        Assert.Single(s.Engine.ObserveController(new(1)).Support.Proposals);Assert.Single(s.Engine.ObserveController(new(2)).Support.Commitments);
        Assert.Empty(s.Engine.ObserveController(new(3)).Support.Proposals);Assert.Empty(s.Engine.Observe(new(4)).Support.Commitments);
        // Unrelated secret proposals must not advance this observer's ID namespace.
        long own = Propose(s,3,4,1); long isolated = Propose(control,3,4,1); Assert.Equal(isolated,own);
        Assert.DoesNotContain("compensation",JsonSerializer.Serialize(s.Engine.Inspect(),SimulationJson.Options),StringComparison.OrdinalIgnoreCase);
        Assert.Equal("NotAvailable",Execute(s,Action(ActionKind.SignSupport),3));Assert.Equal("NotAvailable",Execute(s,Action(ActionKind.SignSupport,999),3));
        var e=Assert.Single(s.Engine.EventsSince(0),e=>e.Type=="SupportSigned");Assert.NotNull(e.Support);Assert.Equal(4,e.Support.Proposal.Compensation.Single().Quantity);
        s.AdvanceTo(100);Assert.Equal(new ParticipantId(1),s.Engine.ObserveController(new(3)).Elections.Results[0].Ballots[1].Candidate);
        Assert.Equal(own,Assert.Single(s.Engine.ObserveController(new(3)).Support.Proposals).Id);
    }
    [Theory]
    [InlineData("Proposed")] [InlineData("AcceptedAwaitingResources")] [InlineData("Signed")]
    public void EveryPoliticalStateDirectlyRestoresAndContinuesEveryHashAndReplay(string state)
    {
        var d=Scenario();var s=Session(d);Deliver(s);Propose(s);
        if(state!="Proposed")Execute(s,Action(ActionKind.AcceptSupport),2);
        if(state=="Signed")Execute(s,Action(ActionKind.SignSupport));
        Assert.Equal(state,Proposal(s).Status.ToString());var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));
        while(s.Engine.Tick<120){s.Step();loaded.Step();Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());}
        Assert.Equal(JsonSerializer.Serialize(s.Engine.EventsSince(0)),JsonSerializer.Serialize(loaded.Engine.EventsSince(0)));
        var replay=GameSession.Replay(d,s.CommandTape,120);Assert.Equal(s.Engine.ComputeStateHash(),replay.Engine.ComputeStateHash());
        Assert.Equal(JsonSerializer.Serialize(s.Engine.InspectSupport()),JsonSerializer.Serialize(replay.Engine.InspectSupport()));
    }
    [Fact]
    public void DuplicateCommandNeverTransfersCompensationOrCreatesCommitmentTwice()
    {
        var s=Accepted();string id=s.Send(new(1),Action(ActionKind.SignSupport));var input=s.CommandTape.Last();var decoded=JsonSerializer.Deserialize<CommandEnvelope>(JsonSerializer.Serialize(input.Command,SimulationJson.Options),SimulationJson.Options)!;s.Submit(new(1),decoded);s.Step();
        string hash=s.Engine.ComputeStateHash();Assert.Equal(ResultStatus.Accepted,s.Engine.Enqueue(new(1),decoded)!.Status);
        Assert.Equal(hash,s.Engine.ComputeStateHash());Assert.Equal(4,Fixtures.Count(s,Section(s,2),"wood"));Assert.Single(s.Engine.InspectSupport().Commitments);
        Assert.Equal(1,s.Engine.EventsSince(0).Count(e=>e.Type=="SupportSigned"));Assert.Equal(id,input.Command.CommandId);
    }
    [Fact]
    public void DecodedProposalPayloadDeduplicatesAfterLoadAndPlaybackCursorRestores()
    {
        var d=Scenario();var recorded=Session(d);Propose(recorded);
        var replay=Session(d);replay.ConfigurePlayback(recorded.CommandTape,10);replay.AdvanceTo(1);
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(replay));
        var command=JsonSerializer.Deserialize<CommandEnvelope>(JsonSerializer.Serialize(recorded.CommandTape.Single().Command,SimulationJson.Options),SimulationJson.Options)!;
        string hash=loaded.Engine.ComputeStateHash();Assert.Equal(ResultStatus.Accepted,loaded.Engine.Enqueue(new(1),command)!.Status);
        Assert.Equal(hash,loaded.Engine.ComputeStateHash());Assert.Single(loaded.Engine.InspectSupport().Proposals);
        Assert.Equal(replay.CommandTape,loaded.CommandTape);replay.AdvanceTo(10);loaded.AdvanceTo(10);
        Assert.Equal(replay.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());
    }
    [Fact]
    public void MultipleMaterialTransferIsAtomicWhenSecondMaterialCannotFit()
    {
        var d=Scenario() with {Support=new(new(5,0),4),Items=[..Scenario().Items,new("grain",1,300)],ResourceNodes=[..Scenario().ResourceNodes,new("grain",new(0,0))]};
        var s=Session(d);Deliver(s,2,2);Deliver(s,1,2);Move(s,1,new(0,0));
        Execute(s,new(ActionKind.Harvest,ItemId:"grain",Quantity:1));Fixtures.UntilIdle(s);Move(s,1,new(5,0));
        Execute(s,new(ActionKind.Transfer,Source:s.Engine.Observe(new(1)).Participant.InventoryId,Destination:Section(s,1),ItemId:"grain",Quantity:1));
        Execute(s,new(ActionKind.ProposeSupport,SupportSeller:new(2),Election:new(1),Compensation:[new("grain",1),new("wood",2)]));
        Execute(s,Action(ActionKind.AcceptSupport),2);Assert.Equal(2,Proposal(s).Reserved.Length);
        Assert.Equal("NoCapacity",Execute(s,Action(ActionKind.SignSupport)));
        Assert.Equal(1,Fixtures.Count(s,Section(s,1),"grain"));Assert.Equal(0,Fixtures.Count(s,Section(s,2),"grain"));
        Assert.Equal(2,Fixtures.Count(s,Section(s,1),"wood"));Assert.Empty(s.Engine.InspectSupport().Commitments);
    }
    [Theory]
    [InlineData("section")] [InlineData("candidate")] [InlineData("hold")] [InlineData("election")] [InlineData("status")]
    [InlineData("duplicate")] [InlineData("receipt")] [InlineData("execution")]
    public void CorruptPoliticalStateAndReceiptsAreRejectedBeforeHashCheck(string field)
    {
        var s=Accepted();if(field is "duplicate" or "receipt" or "execution")Execute(s,Action(ActionKind.SignSupport));
        var saved=JsonNode.Parse(s.Engine.CreateCheckpointJson())!;var politics=saved["support"]!;var p=politics["proposals"]![0]!;
        switch(field){
            case "section":politics["sections"]![0]!["inventory"]!["value"]=1;break;
            case "candidate":p["candidate"]!["value"]=3;break;
            case "hold":p["reserved"]![0]!["quantity"]=3;break;
            case "election":p["election"]!["value"]=2;break;
            case "status":p["status"]="Signed";break;
            case "duplicate":politics["commitments"]!.AsArray().Add(politics["commitments"]![0]!.DeepClone());break;
            case "receipt":politics["commitments"]![0]!["compensation"]![0]!["quantity"]=3;break;
            case "execution":politics["commitments"]![0]!["status"]="Executed";break;
        }
        var ex=Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(saved.ToJsonString(SimulationJson.Options)));
        Assert.NotEqual("StateHashMismatch",ex.ReasonCode);
    }
}
