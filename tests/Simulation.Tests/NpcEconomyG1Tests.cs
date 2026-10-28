using System.Collections.Immutable;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;
namespace Simulation.Tests;
public class NpcEconomyG1Tests
{
    internal static EconomyProfile Profile()=>VNextNpcEconomyProfile.Create(MapDesignTests.Economy(),MapDesignTests.Balance());
    internal static AlgorithmicController Controller(long actor,string input="wood",NpcDifficulty difficulty=NpcDifficulty.Medium)=>new(new(actor,input,Difficulty:difficulty,CoherentEconomy:true),42,[1,2,3,4]);
    internal static void SameCheckpoint(AlgorithmicController c)
    {var a=c.Capture();var b=AlgorithmicController.Restore(a).Capture();Assert.Equal(a.StateJson,b.StateJson);Assert.Equal(a.Kind,b.Kind);Assert.Equal(a.Version,b.Version);Assert.Equal(a.PendingCommandIds.ToArray(),b.PendingCommandIds.ToArray());}
    internal static void Drive(GameSession s,AlgorithmicController controller,Func<bool> done,int bound=1800,[System.Runtime.CompilerServices.CallerMemberName]string name="")
    {
        CommandResult? result=null;
        while(!done()&&bound-->0)
        {
            var o=s.Engine.ObserveController(controller.Actor,true);var clone=AlgorithmicController.Restore(controller.Capture());var feedback=result is null?ImmutableArray<CommandResult>.Empty:[result];
            var decision=controller.Decide(o,feedback);Assert.Equal(System.Text.Json.JsonSerializer.Serialize(decision.Actions,SimulationJson.Options),System.Text.Json.JsonSerializer.Serialize(clone.Decide(o,feedback).Actions,SimulationJson.Options));string? id=null;
            if(decision.Actions.Length>0){id=s.Send(controller.Actor,Assert.Single(decision.Actions));controller.CommandSubmitted(id);}s.Step();if(id is not null)result=s.Engine.Result(id);
        }
        Assert.True(done(),"NPC did not finish: "+controller.Capture().StateJson+"; units="+string.Join(";",s.Engine.Observe(controller.Actor).OwnEnterprises.Select(e=>$"{e.Id.Value}/{e.RecipeId}/{e.Enabled}/{e.Staff}/{e.ReasonCode}/{e.CompletedBatches}")));
        Record(s,name);
    }
    internal static void Record(GameSession s,string name)
    {
        if(Environment.GetEnvironmentVariable("DELSK_G1_EVIDENCE") is not {} output)return;
        Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,name+".json"),System.Text.Json.JsonSerializer.Serialize(s.Engine.Configuration(),SimulationJson.Options));
        File.WriteAllText(Path.Combine(output,name+".commands.jsonl"),string.Join('\n',s.CommandTape.Select(c=>System.Text.Json.JsonSerializer.Serialize(c,SimulationJson.Options)))+"\n");
        File.WriteAllText(Path.Combine(output,name+".tick"),s.Engine.Tick.ToString());CheckpointStore.Save(s,Path.Combine(output,name+".save.json"));
    }
    [Fact]public void ProfilePreservesF2WorldAndPersistsVersionFourWithoutChangingOldControllers()
    {
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(ServiceF2Tests.Profile().World,SimulationJson.Options),System.Text.Json.JsonSerializer.Serialize(Profile().World,SimulationJson.Options));
        var c=Controller(2);Assert.Equal(4,c.Capture().Version);SameCheckpoint(c);Assert.Equal(3,new AlgorithmicController(new(2,"wood",Difficulty:NpcDifficulty.Medium),42,[1,2]).Capture().Version);
        Assert.Equal(Profile().Id,EconomyProfile.Read(System.Text.Encoding.UTF8.GetString(VNextSupportedProfile.Serialize(Profile()))).Id);
    }
    [Theory][InlineData(NpcDifficulty.Easy)][InlineData(NpcDifficulty.Medium)][InlineData(NpcDifficulty.Hard)]
    public void NaturalStartEarnsBeforeBuyingUnaffordableConstructionImports(NpcDifficulty difficulty)
    {
        var d=OrdinaryNewGame.Resolve(MapDesignTests.Template("small_river"),Profile(),new(3,Difficulty:difficulty.ToString()),vnextBalance:MapDesignTests.Balance()).Scenario;var s=new GameSession(new(d));
        var c=Controller(2,d.Participants[1].PublicPreferenceItem,difficulty);var o=s.Engine.ObserveController(new(2),true);string hash=s.Engine.ComputeStateHash();var decision=c.Decide(o,[]);
        Assert.NotEmpty(decision.Actions);Assert.DoesNotContain(decision.Actions,a=>a.Kind==ActionKind.BuyImport);Assert.Equal(hash,s.Engine.ComputeStateHash());Assert.DoesNotContain(c.LatestStrategyTrace,t=>t.Evaluations>32);
    }
    [Fact]public void RejectionBackoffIsBoundedPersistentAndDoesNotForgetAnotherBusiness()
    {
        var s=new GameSession(new(ServiceF2Tests.Definition()));var c=Controller(2);var decision=c.Decide(s.Engine.ObserveController(new(2),true),[]);Assert.NotEmpty(decision.Actions);c.CommandSubmitted("rejected-fixture");
        c.Decide(s.Engine.ObserveController(new(2),true),[new("rejected-fixture",ResultStatus.Rejected,"NotAvailable",1)]);
        Assert.Contains("retryTick",c.Capture().StateJson);SameCheckpoint(c);
    }
    [Fact]public void AccountPreferenceUsesTheOrdinaryCommandAndRestores()
    {
        var(s,_,_)=ServiceF2Tests.Prepare();ServiceF2Tests.Step(s,new(ActionKind.SetAutoPay,Enabled:false));var c=Controller(2);var action=Assert.Single(c.Decide(s.Engine.ObserveController(new(2),true),[]).Actions);Assert.Equal(ActionKind.SetAutoPay,action.Kind);
        ServiceF2Tests.Step(s,action);Assert.True(s.Engine.AccountOf(new(2)).AutoPay);SameCheckpoint(c);
    }
    [Fact]public void PublicQuotePreviewIsPureAndCannotIncludeOtherOwnersClosedStock()
    {
        var(s,_,_)=ServiceF2Tests.Prepare();string before=s.Engine.ComputeStateHash();var observation=s.Engine.ObserveController(new(1),true);Assert.Equal(before,s.Engine.ComputeStateHash());Assert.NotNull(observation.EconomicOpportunities);
        Assert.All(observation.EconomicOpportunities!.Overflow,p=>Assert.Equal(1,p.Lot.Owner));Assert.Empty(s.Engine.ObserveController(new(2),true).EconomicOpportunities!.ServiceQuotes);
    }
}
