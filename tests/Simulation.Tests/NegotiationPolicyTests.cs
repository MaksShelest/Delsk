using System.Collections.Immutable;
using System.Text.Json;
using Agents.Algorithmic;
using Agents.Llm;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class NegotiationPolicyTests
{
    private static string Do(GameSession s,GameAction action,long actor=1)=>LandContentFixtures.Reason(s,action,actor);
    private static TradeNegotiation Current(GameSession s)=>s.Engine.InspectNegotiations().Single();
    private static GameAction Reply(GameSession s,ActionKind kind,TradeTerms? terms=null)=>new(kind,NegotiationId:Current(s).Id,NegotiationVersion:Current(s).Current.Version,Terms:terms);
    private static ScenarioDefinition Production()
    {
        var d=TradeFixtures.Scenario();return d with {Plots=[..d.Plots!,new(100,new(6,0),1,OwnerId:2)],
            BuildingTypes=[..d.BuildingTypes!,new("policy-mill",BuildingKind.Production,[new(0,0)],[new(1,"sawmill",1,[],40,40,2,1)])],
            Buildings=[..d.Buildings!,new(100,2,new(6,0),"policy-mill")]};
    }
    private static GameSession Proposed(long price=500)
    {
        var s=new GameSession(new(Production()));var source=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Kind==BuildingKind.Warehouse).InputId;
        Do(s,new(ActionKind.ProposeTrade,Terms:new(TradeKind.Sale,new(1),new(2),"wood",2,price,source)));return s;
    }
    private static AlgorithmicController Policy(long actor=2,int victory=0)=>new(new(actor,"wood",ExpansionWeight:0,MaximumBuyPriceBasisPoints:10000,OwnVictoryWeight:victory,SupportPricePerVoteMinor:2000),42,[1,2,3]);
    [Fact]
    public void NegotiationFactorPreservesTradePoliticsAndLegacyDefaultsAndSurvivesRestore()
    {
        var settings = new ControllerSettings(2,"wood",NegotiationsEnabled:false);
        Assert.True(settings.TradeEnabled); Assert.True(settings.PoliticsEnabled); Assert.True(settings.FutureContractsEnabled);
        var policy = new AlgorithmicController(settings,42,[1,2,3]);
        var restored = AlgorithmicController.Restore(policy.Capture());
        var observation = Proposed(900).Engine.ObserveController(new(2));
        Assert.DoesNotContain(restored.ModelOptions(observation), p=>p.Goal.Contains("Negotiation",StringComparison.Ordinal));
        Assert.DoesNotContain(restored.Decide(observation,[]).Actions,a=>a.Kind is ActionKind.AcceptTrade or ActionKind.CounterTrade or ActionKind.ProposeTrade or ActionKind.RejectTrade);
        Assert.Contains(AlgorithmicController.Restore(policy.Capture()).Decide(observation,[]).Actions,a=>a.Kind==ActionKind.ProposeSupport);
        Assert.Contains("\"negotiationsEnabled\":false",restored.Capture().StateJson);
        Assert.DoesNotContain("negotiationsEnabled",Policy().Capture().StateJson);
        Assert.Contains(Policy().ModelOptions(observation),p=>p.Goal=="CounterNegotiation");
    }
    [Theory] [InlineData(500,ActionKind.AcceptTrade)] [InlineData(900,ActionKind.CounterTrade)]
    public void BaselineNegotiatesPriceAndKeepsReviewedVersionWithoutLlm(long price,ActionKind expected)
    {
        var s=Proposed(price);var policy=Policy();var decision=policy.Decide(s.Engine.ObserveController(new(2)),[]);
        var action=Assert.Single(decision.Actions);Assert.Equal(expected,action.Kind);Assert.Equal(1,action.NegotiationVersion);
        if(expected==ActionKind.CounterTrade){Assert.Equal(500,action.Terms!.UnitPriceMinor);Assert.Equal("Accepted",Do(s,action,2));Assert.Equal(2,Current(s).Current.Version);}
    }
    [Theory] [InlineData(1,ActionKind.RejectTrade)] [InlineData(4,ActionKind.AcceptTrade)]
    public void SupportResponseDependsOnCompensationRatherThanAnyGift(long quantity,ActionKind expected)
    {
        var d=TradeFixtures.Scenario();d=TradeFixtures.WithShops(d with {Participants=[..d.Participants,new(4,100000,new(0,0),20)]});
        var s=new GameSession(new(d));Do(s,new(ActionKind.ProposeTrade,Terms:new(TradeKind.Support,new(2),new(1),Election:new(1),Compensation:[new("wood",quantity)])));
        Assert.Equal(expected,Assert.Single(Policy(victory:100).Decide(s.Engine.ObserveController(new(2)),[]).Actions).Kind);
    }
    [Fact]
    public void ChangedVersionInvalidatesModelOptionAndCaptureRestoresExactAlgorithmicPlan()
    {
        var s=Proposed(900);var policy=Policy();var before=s.Engine.ObserveController(new(2));
        var option=Assert.Single(policy.ModelOptions(before),p=>p.Goal=="CounterNegotiation");
        var copy=AlgorithmicController.Restore(policy.Capture());
        Assert.Equal(JsonSerializer.Serialize(policy.Decide(before,[]),SimulationJson.Options),JsonSerializer.Serialize(copy.Decide(before,[]),SimulationJson.Options));
        Do(s,Reply(s,ActionKind.CounterTrade,Current(s).Current.Terms with {UnitPriceMinor=500}),2);
        Assert.False(Policy().TrySelectModelOption(s.Engine.ObserveController(new(2)),option,out _));
        var third=s.Engine.ObserveController(new(3));var thirdPolicy=Policy(3);var prompt=ModelPrompt.Build(third,thirdPolicy.ModelOptions(third),"private-test",new("s1",1,third.Tick,false),new(),"Initial","");
        Assert.DoesNotContain(Current(s).Id.ToString(),prompt.Prompt);Assert.Empty(third.Negotiations);
    }
    [Theory] [InlineData("{}")] [InlineData("timeout")]
    public async Task ModelRefusalOrDeadlineFallsBackToTheSameValidatedNegotiation(string response)
    {
        var s=Proposed(900);var d=Production();
        var gate=new TaskCompletionSource<ModelReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter=new TestModelAdapter((_,_)=>response=="timeout"?gate.Task:Task.FromResult(new ModelReply(response)));
        using var runtime=new ModelRuntime(adapter,[2]);var hybrid=new HybridController(Policy(),runtime,new(1,3,3,20,1));
        hybrid.BindSession(new("negotiation-model",1,s.Engine.Tick,false));
        hybrid.BeginDecisionBarrier(s.Engine.Tick);
        var requested=hybrid.Decide(s.Engine.ObserveController(new(2)),[]);
        hybrid.EndDecisionBarrier(s.Engine.Tick);
        Assert.Equal("Requested",requested.ModelTrace!.Resolution);
        await ModelTestFixtures.Until(()=>adapter.Calls.Count==1);
        Assert.Contains(adapter.Calls.Single().Options,p=>p.Goal=="CounterNegotiation");
        if(response!="timeout")await ModelTestFixtures.Completed(runtime,adapter);
        s.AdvanceTo(3);hybrid.BeginDecisionBarrier(s.Engine.Tick);
        var fallback=hybrid.Decide(s.Engine.ObserveController(new(2)),[]);hybrid.EndDecisionBarrier(s.Engine.Tick);
        Assert.Equal("FallbackResolved",fallback.ModelTrace!.Resolution);
        var action=Assert.Single(fallback.Actions);Assert.Equal(ActionKind.CounterTrade,action.Kind);
        Assert.Equal("Accepted",Do(s,action,2));
        gate.TrySetResult(new("{}"));await ModelTestFixtures.Until(()=>runtime.ActiveRequests==0);
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());
    }
    [Theory] [InlineData(TradeKind.Sale)] [InlineData(TradeKind.Purchase)] [InlineData(TradeKind.Supply)] [InlineData(TradeKind.Support)]
    public void EachProposedKindAndCounterSurvivesSaveLoadBeforeAcceptance(TradeKind kind)
    {
        var d=TradeFixtures.Scenario();var s=new GameSession(new(d));
        var source=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Kind==BuildingKind.Warehouse).InputId;
        var destination=s.Engine.Observe(new(2)).OwnBuildings.Single(b=>b.Kind==BuildingKind.Warehouse).InputId;
        var terms=kind==TradeKind.Support?new(kind,new(2),new(1),Election:new(1),Compensation:ImmutableArray.Create(new MaterialCost("wood",2))):
            new TradeTerms(kind,new(1),new(2),"wood",2,500,kind==TradeKind.Sale?source:default,kind==TradeKind.Sale?default:destination);
        long actor=kind==TradeKind.Purchase?2:1;Assert.Equal("Accepted",Do(s,new(ActionKind.ProposeTrade,Terms:terms),actor));
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));
        var counter=Reply(s,ActionKind.CounterTrade,kind==TradeKind.Support?terms with {Compensation=[new("wood",3)]}:terms with {UnitPriceMinor=600});
        Assert.Equal("Accepted",Do(s,counter,actor==1?2:1));Assert.Equal("Accepted",Do(copy,counter,actor==1?2:1));
        Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());
    }
}
