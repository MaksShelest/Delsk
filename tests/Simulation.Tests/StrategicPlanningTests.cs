using System.Text.Json;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public sealed class StrategicPlanningTests
{
    private static AlgorithmicController Policy(PlanningLimits? limits=null)=>new(new(1,"wood",Difficulty:NpcDifficulty.Hard,Planning:limits,BaseWageMinor:10,TradeEnabled:false,PoliticsEnabled:false),42,[1,2]);
    [Fact]
    public void ProfileEndingOnElectionTickMatchesOrdinaryRun()
    {
        var d=StoppedElectionTests.Definition();
        GameSession Start(){var s=Fixtures.Session(d);s.AttachControllers(d.Participants.Select(p=>new SelfBallotController(new(p.Id))));return s;}
        var a=Start();var b=Start();SessionProfiler.Run(a,3,SelfBallotController.Restore);
        b.AdvanceTo(3);Assert.True(b.PumpVoting());
        Assert.False(a.Engine.WaitingForBallots);Assert.Equal(a.Engine.ComputeStateHash(),b.Engine.ComputeStateHash());
        Assert.Equal(a.CommandTape,b.CommandTape);
    }
    [Fact]
    public void PlannerIsBoundedPureDuringPreviewAndPersistsItsChosenGoal()
    {
        var s=Fixtures.Session(ProductionRecipeTests.LandScenario());var policy=Policy(new(MaxCandidates:3,Depth:2,MaxEvaluations:5));var o=s.Engine.ObserveController(new(1));
        string before=policy.Capture().StateJson;var preview=policy.ModelOptions(o);Assert.Equal(before,policy.Capture().StateJson);
        policy.Decide(o,[]);Assert.NotEmpty(policy.LatestStrategyTrace);
        Assert.All(policy.LatestStrategyTrace,t=>{Assert.InRange(t.Evaluations,1,5);Assert.InRange(t.Eta,0,120);Assert.InRange(t.Continuations,0,1);});
        Assert.InRange(policy.LatestStrategyTrace.Length,1,3);
        var restored=AlgorithmicController.Restore(policy.Capture());Assert.Equal(policy.Capture().StateJson,restored.Capture().StateJson);
    }
    [Fact]
    public void NoPlanCanSpendProductionReserveOrUseTheSameStockTwiceInAContinuation()
    {
        var d=ProductionRecipeTests.LandScenario(capacity:80);var s=Fixtures.Session(d);ProductionRecipeTests.Supply(s,"wood",4);ProductionRecipeTests.Supply(s,"input-b",2);
        ProductionRecipeTests.Do(s,new(ActionKind.SetProductionEnabled,Enterprise:new(1),Enabled:true));
        var o=s.Engine.ObserveController(new(1));o=o with {Account=o.Account with {BalanceMinor=3001}};
        var policy=Policy();policy.Decide(o,[]);
        Assert.All(policy.LatestStrategyTrace,t=>{Assert.True(t.CapitalMinor==0);Assert.True(t.ExecutionReserveMinor>0);Assert.True(t.Continuations<=1);});
        Assert.DoesNotContain(policy.LatestStrategyTrace,t=>t.Goal=="BuildProduction");
    }
    [Theory] [InlineData(0,2,10,1)][InlineData(4,4,120,20)][InlineData(4,2,120,3)]
    public void InvalidBudgetsAreRejected(int candidates,int depth,int horizon,int evaluations)
    {Assert.Throws<ArgumentException>(()=>Policy(new(candidates,depth,horizon,evaluations)));}
    [Fact]
    public void HardControllerOrderingSaveLoadAndReplayAreReproducible()
    {
        var d=JsonSerializer.Deserialize<ScenarioDefinition>(EconomyProfileTests.Text("baseline-food"),SimulationJson.Options)!;
        var config=JsonSerializer.Deserialize<AutonomousConfiguration>(EconomyProfileTests.Text("baseline-food-controllers"),SimulationJson.Options)!;
        GameSession Start(bool reverse){var s=Fixtures.Session(d);var list=config.Npcs.Select(n=>new AlgorithmicController(n with {Difficulty=NpcDifficulty.Hard,Planning=new(6,2,80,12)},AlgorithmicController.RuntimeSeed(d.Seed,n.ActorId),d.Participants.Select(p=>p.Id)));s.AttachControllers(reverse?list.Reverse():list);return s;}
        var a=Start(false);a.AdvanceTo(75);var loaded=CheckpointStore.Decode(CheckpointStore.Encode(a));loaded.RestoreControllers(AlgorithmicController.Restore);
        while(a.Engine.Tick<160){a.Step();loaded.Step();Assert.Equal(a.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());Assert.Equal(a.ComputeControllerStateHash(),loaded.ComputeControllerStateHash());}
        var reversed=Start(true);reversed.AdvanceTo(160);Assert.Equal(a.CommandTape,reversed.CommandTape);
        Assert.Equal(a.Engine.ComputeStateHash(),GameSession.Replay(d,a.CommandTape,160).Engine.ComputeStateHash());
    }
}
