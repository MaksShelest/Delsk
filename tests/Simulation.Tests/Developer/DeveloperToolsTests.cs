using System.Text.Json;
using Agents.Algorithmic;
using Agents.Llm;
using Simulation.Core;
using Simulation.Developer;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class DeveloperToolsTests
{
    private sealed class WaitingAdapter:ILocalModelAdapter
    {public Task<ModelReply> CompleteAsync(ModelRequest request,CancellationToken token)=>new TaskCompletionSource<ModelReply>().Task;}
    private static GameSession Session()=>new(new(StoppedElectionTests.Definition()));
    [Fact]
    public void IndustryExperienceIsAuditedSavedAndReplayedWithoutFakeProduction()
    {
        var d=ExpansionD2Tests.Definition(false);var s=new GameSession(new(d));using var tools=new DeveloperTools(s,new(1));
        tools.IndustryExperience(new(1),"woodworking",600);Assert.Equal(2,s.Engine.DevelopmentOf(new(1))!.Experience.Single(x=>x.Industry=="woodworking").TechnologyLevel);
        tools.IndustryExperience(new(1),"woodworking",650);Assert.Equal(0,s.Engine.DevelopmentOf(new(1))!.Experience.Single(x=>x.Industry=="woodworking").ProductionXp);
        s.Step();var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,s.Engine.Tick,s.Engine.ResearchModifications.ToArray()).Engine.ComputeStateHash());
        Assert.True(s.Engine.ResearchModified);Assert.Equal(2,s.Engine.ResearchModifications.Length);Assert.Throws<ArgumentException>(()=>tools.IndustryExperience(new(1),"missing",1));
    }
    [Fact]
    public void CashAndStockEditsAreAuditedConservedAndDoNotPretendToBeProduction()
    {
        var s=Session();using var tools=new DeveloperTools(s,new(1));var actor=s.Engine.Observe(new(1)).Participant;
        long initialExternal=s.Engine.Inspect().ExternalMoneyInMinor;
        var money=tools.Money(new(1),100123);var stock=tools.Stock(new(1),actor.InventoryId,"wood",2);
        Assert.Equal(100000,money.OldValue);Assert.Equal(100123,money.NewValue);Assert.Equal(0,stock.OldValue);
        Assert.Equal(2,s.Engine.Observe(new(1)).OwnInventories.Single(i=>i.Id==actor.InventoryId).Stock.Single().Quantity);
        Assert.Equal(0,s.Engine.PublicEconomy().GrossOutputMinor);Assert.Equal(123,s.Engine.Inspect().ExternalMoneyInMinor-initialExternal);
        Assert.True(s.Engine.ResearchModified);Assert.Equal(2,s.Engine.ResearchModifications.Length);s.Engine.ValidateInvariants();
    }
    [Fact]
    public void CapacityHoldsAndMoneyHoldsCannotBeDestroyedAndFailureIsAtomic()
    {
        var s=Session();var owner=s.Engine.Observe(new(1));s.Send(new(1),new(ActionKind.Harvest,ItemId:"wood",Quantity:20));s.Step();
        using var tools=new DeveloperTools(s,new(1));var hash=s.Engine.ComputeStateHash();
        Assert.ThrowsAny<Exception>(()=>tools.Stock(new(1),owner.Participant.InventoryId,"wood",1));Assert.Equal(hash,s.Engine.ComputeStateHash());
        var b=new GameSession(new(JsonSerializer.Deserialize<ScenarioDefinition>(EconomyProfileTests.Text("delivery-lifecycle"),SimulationJson.Options)!));
        var input=b.Engine.Observe(new(1)).OwnBuildings.Single().InputId;
        b.Send(new(1),new(ActionKind.CreateBuyOffer,Destination:input,ItemId:"wood",Quantity:2,UnitPriceMinor:100));b.Step();
        using var other=new DeveloperTools(b,new(1));hash=b.Engine.ComputeStateHash();
        Assert.ThrowsAny<Exception>(()=>other.Money(new(1),0));Assert.Equal(hash,b.Engine.ComputeStateHash());
    }
    [Fact]
    public void ParametersOnlyChangeAtSafeBoundariesAndUnsupportedGeometryIsRejected()
    {
        var s=Session();using var tools=new DeveloperTools(s,new(1));string config=s.Engine.ConfigHash;
        tools.WageExponent(5);Assert.Equal(5,s.Engine.EconomyRules.WageAttractionExponent);Assert.Equal(config,s.Engine.ConfigHash);
        var hash=s.Engine.ComputeStateHash();Assert.Throws<ArgumentException>(()=>tools.WageExponent(6));Assert.Equal(hash,s.Engine.ComputeStateHash());
        Assert.Throws<ArgumentException>(()=>s.DeveloperModify("Capacity",new(1),1));Assert.Equal(hash,s.Engine.ComputeStateHash());
        s.AdvanceTravelQuantum();Assert.Throws<InvalidOperationException>(()=>tools.WageExponent(4));
        s.CompleteTravelTick();s.AdvanceTo(3);Assert.Throws<InvalidOperationException>(()=>tools.Money(new(1),100000));
    }
    [Fact]
    public void RestoreAndReplayKeepInterventionsAndTheirExactInputBarrier()
    {
        var d=StoppedElectionTests.Definition();var s=new GameSession(new(d));using var tools=new DeveloperTools(s,new(1));
        tools.Money(new(1),101000);s.AdvanceTo(3);
        foreach(long id in new long[]{1,2,3,4})s.Send(new(id),new(ActionKind.SetVoteChoice,Election:new(1),Candidate:new(id)));
        tools.Stock(new(1),s.Engine.Observe(new(1)).Participant.InventoryId,"wood",2);tools.WageExponent(4);s.AdvanceTo(5);
        var restored=SimulationEngine.FromCheckpointJson(s.Engine.CreateCheckpointJson());
        Assert.Equal(s.Engine.ComputeStateHash(),restored.ComputeStateHash());Assert.Equal(4,restored.EconomyRules.WageAttractionExponent);
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());
        var replay=GameSession.Replay(d,s.CommandTape,5,s.Engine.ResearchModifications.ToArray());
        Assert.Equal(s.Engine.ComputeStateHash(),replay.Engine.ComputeStateHash());
        Assert.Throws<CheckpointException>(()=>GameSession.Replay(d,s.CommandTape,5,s.Engine.ResearchModifications.Select((m,i)=>i==0?m with{OldValue=m.OldValue+1}:m).ToArray()));
    }
    [Fact]
    public void AdvanceStopsForHumanBallotsAndCanBeCancelledWithoutSkippingTicks()
    {
        var s=Session();s.AttachControllers(new long[]{2,3,4}.Select(id=>(IController)new SelfBallotController(new(id))));
        using var tools=new DeveloperTools(s,new(1));s.SetPaused(true);tools.StartAdvance(8);
        for(int i=0;i<50 && s.Engine.Tick<3;i++)tools.PumpAdvance();
        tools.PumpAdvance();Assert.Equal(3,s.Engine.Tick);Assert.Equal("WaitingForBallot",tools.AdvanceStatus);Assert.True(s.Paused);
        tools.CancelAdvance();for(int i=0;i<5;i++)tools.PumpAdvance();Assert.Equal(3,s.Engine.Tick);
    }
    [Fact]
    public void ExplicitTemporaryHumanPolicyIsMarkedAndAdvanceEqualsSequentialRecordedExecution()
    {
        var d=StoppedElectionTests.Definition();var s=new GameSession(new(d));s.AttachControllers(new long[]{2,3,4}.Select(id=>(IController)new SelfBallotController(new(id))));
        using var tools=new DeveloperTools(s,new(1));tools.StartAdvance(8,true);
        for(int i=0;i<200 && tools.AdvanceTarget is not null;i++)tools.PumpAdvance();
        Assert.Equal(8,s.Engine.Tick);Assert.Equal("Completed",tools.AdvanceStatus);
        Assert.Equal("HumanSelfBallot",Assert.Single(s.Engine.ResearchModifications).Kind);Assert.Equal(2,s.Engine.Inspect().Elections.Results.Length);
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,8,s.Engine.ResearchModifications.ToArray()).Engine.ComputeStateHash());
    }
    [Fact]
    public void DeveloperJournalIsBoundedAndIncludesRealCandidateScoresAndObservation()
    {
        var d=JsonSerializer.Deserialize<ScenarioDefinition>(EconomyProfileTests.Text("baseline-economy"),SimulationJson.Options)!;
        var s=new GameSession(new(d));s.AttachControllers([new AlgorithmicController(new(2,"wood"),1,[1,2,3,4])]);
        using var tools=new DeveloperTools(s,new(1),2);s.AdvanceTo(30);
        Assert.InRange(tools.Journal.Length,1,2);Assert.All(tools.Journal,e=>Assert.InRange(e.Observation.Length,1,16385));
        var scored=s.ControllerTurns.First(t=>t.Decision.Candidates is {Length:>0});Assert.Equal(scored.Decision.Goal,scored.Decision.Candidates!.Value[0].Goal);
        Assert.True(scored.Decision.Candidates.Value[0].Score>0);
    }
    [Fact]
    public void AdvanceWaitsForTheModelWithoutBurningEconomicTicksToItsApplyDeadline()
    {
        using var runtime=new ModelRuntime(new WaitingAdapter(),[2],new(TimeoutMilliseconds:10000));
        var s=Session();s.AttachControllers([new HybridController(new(new(2,"wood"),1,[1,2,3,4]),runtime)]);
        using var tools=new DeveloperTools(s,new(1));tools.StartAdvance(10);
        for(int i=0;i<30 && tools.AdvanceStatus!="WaitingForModel";i++)tools.PumpAdvance();
        Assert.Equal("WaitingForModel",tools.AdvanceStatus);long tick=s.Engine.Tick;
        for(int i=0;i<10;i++)tools.PumpAdvance();Assert.Equal(tick,s.Engine.Tick);Assert.True(tick<3);
    }
}
