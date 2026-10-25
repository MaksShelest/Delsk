using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Agents.Algorithmic;
using Agents.Llm;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class OrdinaryNewGameTests
{
    internal static string Text(string name)
    {using var s=typeof(OrdinaryNewGameTests).Assembly.GetManifestResourceStream("Fixture."+name)!;using var r=new StreamReader(s);return r.ReadToEnd();}
    internal static EconomyProfile Economy()=>EconomyProfile.Read(Text("economy.json"));
    internal static ScenarioDefinition Map(string key)=>JsonSerializer.Deserialize<ScenarioDefinition>(Text(PlayMaps.Resource(key).Replace("Play.","",StringComparison.Ordinal)),SimulationJson.Options)!;
    internal static AutonomousConfiguration Config()=>JsonSerializer.Deserialize<AutonomousConfiguration>(Text("baseline-controllers.json"),SimulationJson.Options)!;
    [Theory] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)]
    public void EveryOrdinaryCountOnEveryOldMapHasOneHumanAndIndependentControllers(int count)
    {
        foreach(string map in PlayMaps.All.Where(k=>!PlayMaps.IsDesigned(k)))
        {
            var raw=Map(map);string before=JsonSerializer.Serialize(raw,SimulationJson.Options);
            var resolved=OrdinaryNewGame.Resolve(raw,Economy(),new(count));var d=resolved.Scenario;
            Assert.Equal(count+1,d.Participants.Length);Assert.Equal(count,resolved.Controllers.Length);Assert.DoesNotContain(resolved.Controllers,c=>c.ActorId==1);
            Assert.Equal(count+1,d.Participants.Select(p=>p.Position).Distinct().Count());Assert.True(d.Plots!.Count(p=>p.IsShopSite)>=count+1);
            Assert.All(d.Participants,p=>{Assert.Equal(20000,p.CashMinor);Assert.Equal(20,p.InventoryCapacity);});
            Assert.Equal(before,JsonSerializer.Serialize(raw,SimulationJson.Options));Assert.Equal(raw.ResourceNodes.Select(n=>(n.ItemId,n.Position)).ToArray(),d.ResourceNodes.Select(n=>(n.ItemId,n.Position)).ToArray());Assert.All(d.ResourceNodes,n=>Assert.Equal(n.Rules!.Public?50:200,n.Rules.Capacity));Assert.Equal(raw.CapitalDemand!.Terminal,d.CapitalDemand!.Terminal);
            var config=Config().ForOrdinary(d,1,resolved.Controllers);config.Validate(d);
            var policies=config.Npcs.Select(n=>new AlgorithmicController(n,AlgorithmicController.RuntimeSeed(d.Seed,n.ActorId),d.Participants.Select(p=>p.Id))).ToArray();
            Assert.Equal(count,policies.Select(p=>p.Actor).Distinct().Count());Assert.Equal(count,policies.Select(p=>JsonNode.Parse(p.Capture().StateJson)!["randomState"]!.ToJsonString()).Distinct().Count());
            Assert.All(policies,p=>Assert.Equal(count,JsonNode.Parse(p.Capture().StateJson)!["trust"]!.AsArray().Count));
        }
    }
    [Theory] [InlineData("{\"npcCount\":2}")] [InlineData("{\"npcCount\":11}")] [InlineData("{\"npcCount\":3.5}")]
    [InlineData("{\"npcCount\":null}")] [InlineData("{\"npcCount\":\"3\"}")] [InlineData("{\"npcCount\":true}")]
    [InlineData("{\"npcCount\":3,\"npcCount\":4}")] [InlineData("{\"npcCount\":3,\"unknown\":1}")]
    [InlineData("{\"difficulty\":\"Impossible\"}")] [InlineData("null")]
    public void BadOrdinaryOptionsAreRejectedInsteadOfClamped(string json)=>Assert.ThrowsAny<Exception>(()=>OrdinaryGameOptions.Read(json));
    [Fact]
    public void DefaultLegacySetupKeepsPositionsAndAggregatesAndOnlyLargerNewRunsScale()
    {
        var map=Map(PlayMaps.First);var e=Economy();var old=e.Apply(map);var normal=OrdinaryNewGame.Resolve(map,e,new());
        Assert.Equal(old.Participants,normal.Scenario.Participants);Assert.Equal(old.Population,normal.Scenario.Population);
        Assert.Equal(old.CapitalDemand,normal.Scenario.CapitalDemand);Assert.Equal(old.Plots,normal.Scenario.Plots);
        Assert.Equal("legacy-default",normal.Scenario.NewGame!.AssignmentVersion);
        var custom=e with {World=e.World with {Population=21,StartingTreasuryMinor=101,Labor=e.World.Labor with {MaximumPopulation=101}}};
        var many=OrdinaryNewGame.Resolve(map,custom,new(6)).Scenario;
        Assert.Equal(37,many.Population);Assert.Equal(177,many.StartingTreasuryMinor);Assert.Equal(177,many.Labor!.MaximumPopulation);Assert.Equal(105000,many.CapitalDemand!.BootstrapBaseBudgetMinor);
        Assert.Throws<OverflowException>(()=>OrdinaryNewGame.Scale(long.MaxValue,11));Assert.Throws<ArgumentException>(()=>OrdinaryNewGame.Resolve(map,e,new(HumanActorId:99)));
    }
    [Fact]
    public void ExplicitA2ProfileUsesItsB1AggregatesWithoutOverwritingThemAfterApply()
    {
        var economy=EconomyProfile.Read(Text("vnext-supported.json"));var full=VNextBalanceReader.Read(Encoding.UTF8.GetBytes(Text("vnext-balance.json"))).Balance!;
        var d=OrdinaryNewGame.Resolve(Map(PlayMaps.First),economy,new(10),vnextBalance:full).Scenario;
        Assert.Equal(220,d.Population);Assert.Equal(660,d.Labor!.MaximumPopulation);Assert.Equal(1375000,d.StartingTreasuryMinor);Assert.Equal(55000,d.CapitalDemand!.BootstrapBaseBudgetMinor);
        Assert.Equal(34,d.Items.Length);Assert.Equal(20000,d.Participants[10].CashMinor);Assert.Equal(80,d.NewGame!.BasePopulation);
        var s=new GameSession(new(d));Assert.Equal(1595000,s.Engine.Inspect().InitialMoneyMinor);Assert.Equal(s.Engine.ComputeStateHash(),CheckpointStore.Decode(CheckpointStore.Encode(s)).Engine.ComputeStateHash());
    }
    [Fact]
    public void SeededAssignmentIsDeterministicAndDoesNotPinActorOneToOneCell()
    {
        var map=Map(PlayMaps.Second);var e=Economy();var first=OrdinaryNewGame.Resolve(map,e,new(10,Seed:17));var second=OrdinaryNewGame.Resolve(map,e,new(10,Seed:17));
        Assert.Equal(ScenarioValidation.Hash(first.Scenario),ScenarioValidation.Hash(second.Scenario));
        Assert.True(Enumerable.Range(1,9).Select(seed=>OrdinaryNewGame.Resolve(map,e,new(10,Seed:(ulong)seed)).Scenario.Participants[0].Position).Distinct().Count()>=3);
        var pool=first.Scenario.Participants.Select(p=>p.Position).ToArray();Assert.Throws<ArgumentException>(()=>OrdinaryNewGame.Resolve(map,e,new(10),[pool[0],pool[0]]));
    }
    [Theory] [InlineData("Easy")] [InlineData("Medium")] [InlineData("Hard")]
    public void EveryNpcUsesChosenExistingDifficultyAndSeparatePolicyState(string difficulty)
    {
        var r=OrdinaryNewGame.Resolve(Map(PlayMaps.First),Economy(),new(10,difficulty));var config=Config().ForOrdinary(r.Scenario,1,r.Controllers);
        Assert.All(config.Npcs,n=>Assert.Equal(difficulty,n.Difficulty.ToString()));
        var policies=config.Npcs.Select(n=>new AlgorithmicController(n,AlgorithmicController.RuntimeSeed(42,n.ActorId),r.Scenario.Participants.Select(p=>p.Id))).ToArray();
        var s=new GameSession(new(r.Scenario));s.AttachControllers(policies);s.AdvanceTo(2);
        Assert.Equal(10,s.ControllerStates.Length);Assert.All(s.ControllerStates,state=>{Assert.Equal(state.StateJson,AlgorithmicController.Restore(state).Capture().StateJson);Assert.Equal(10,JsonNode.Parse(state.StateJson)!["trust"]!.AsArray().Count);});
        Assert.Equal(3,Config().Npcs.Length);
    }
    [Fact]
    public void PopulationCapAndInitialTreasuryAreConservedAndRestoredAtTheGrowthBoundary()
    {
        var d=Fixtures.Cycle() with {Enterprises=[],CapitalOffers=[],Population=20,StartingTreasuryMinor=12345,Labor=new(StepTicks:1,GrowthPeriodTicks:2,GrowthPerPeriod:3,MaximumPopulation:25)};
        var s=new GameSession(new(d));s.AdvanceTo(3);Assert.Equal(23,s.Engine.Inspect().Population);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(10);copy.AdvanceTo(10);
        Assert.Equal(25,s.Engine.Inspect().Population);Assert.Equal(25,s.Engine.Inspect().Unemployed);Assert.Equal(12345,s.Engine.Inspect().Accounts.Single(a=>a.Kind=="Treasury").BalanceMinor);
        Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,10).Engine.ComputeStateHash());
        var corrupted=JsonNode.Parse(s.Engine.CreateCheckpointJson())!;corrupted["population"]=26;
        Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(corrupted.ToJsonString(SimulationJson.Options)));
        Assert.Throws<ArgumentException>(()=>new SimulationEngine(d with {Labor=d.Labor with {MaximumPopulation=19}}));
    }
    [Fact]
    public async Task TenHybridActorsShareOneSlotAndRestorePendingWaitWithoutRequery()
    {
        var resolved=OrdinaryNewGame.Resolve(Map(PlayMaps.First),Economy(),new(10));
        var config=Config().ForOrdinary(resolved.Scenario,1,resolved.Controllers);
        var gate=new TaskCompletionSource<ModelReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter=new TestModelAdapter((_,_)=>gate.Task);var model=LocalGameModel.Default with {Runtime=new(3,1000)};
        using var runtime=new ModelRuntime(adapter,config.Npcs.Select(n=>n.ActorId),model.Runtime);
        var s=new GameSession(new(resolved.Scenario));s.AttachControllers(config.Npcs.Select(n=>(IController)new HybridController(new(n,AlgorithmicController.RuntimeSeed(42,n.ActorId),resolved.Scenario.Participants.Select(p=>p.Id)),runtime,gameModel:model)));
        s.Step();await ModelTestFixtures.Until(()=>adapter.Calls.Count>0);Assert.Equal(1,runtime.ActiveRequests);Assert.Single(adapter.Calls);Assert.Equal(10,s.ControllerStates.Length);
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));var unused=new TestModelAdapter((_,_)=>throw new InvalidOperationException("Unexpected restore inference"));
        using var restoredRuntime=new ModelRuntime(unused,config.Npcs.Select(n=>n.ActorId),model.Runtime);
        loaded.RestoreControllers(c=>HybridController.Restore(c,restoredRuntime));loaded.Step();Assert.Empty(unused.Calls);
        Assert.Equal(10,loaded.ControllerStates.Select(c=>c.Actor).Distinct().Count());
        string hash=loaded.Engine.ComputeStateHash();gate.SetResult(new("{}"));await Task.Yield();Assert.Equal(hash,loaded.Engine.ComputeStateHash());
        Assert.Equal(hash,GameSession.Replay(resolved.Scenario,loaded.CommandTape,loaded.Engine.Tick).Engine.ComputeStateHash());
    }
    [Fact]
    public void ElevenLaborCandidatesConservePopulationAndRotateEqualScarcity()
    {
        var candidates=Enumerable.Range(1,11).Select(i=>new LaborCandidate(i,3,10)).ToArray();
        var equal=LaborAllocation.Cubic(11,candidates,new(42));Assert.Equal(11,equal.Count);Assert.All(equal.Values,n=>Assert.Equal(1,n));
        var sets=new HashSet<string>(StringComparer.Ordinal);bool lastWon=false;
        for(ulong seed=1;seed<=9;seed++)
        {
            var first=LaborAllocation.Cubic(7,candidates,new(seed));var reverse=LaborAllocation.Cubic(7,candidates.Reverse().ToArray(),new(seed));
            Assert.Equal(first.OrderBy(p=>p.Key),reverse.OrderBy(p=>p.Key));Assert.Equal(7,first.Values.Sum());Assert.All(first.Values,n=>Assert.InRange(n,0,1));
            lastWon|=first[11]>0;sets.Add(string.Join(',',first.Where(p=>p.Value>0).Select(p=>p.Key).Order()));
        }
        Assert.True(lastWon);Assert.True(sets.Count>1);
    }
    private static ScenarioDefinition Eleven(bool exactHalf=false)
    {
        var d=Fixtures.Cycle();return d with {Id="eleven-participants-fixture",Participants=Enumerable.Range(1,11).Select(i=>new ParticipantDefinition(i,100000,new(0,0),20)).ToArray(),
            Enterprises=[],Population=30,CapitalOffers=[new(1,"wood",10,500,new(1,0))],Elections=new(PeriodTicks:5),Support=new(new(0,0),80)};
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ElevenBallotsUseEachCurrentWorkerOnceAndStrictMajority(bool exactHalf)
    {
        var d=Eleven(exactHalf) with {CapitalOffers=[],Enterprises=Enumerable.Range(1,11).Select(i=>new EnterpriseDefinition(i,i,new(i,1),"sawmill",40,40,exactHalf&&i==11?3:1,3,100)).ToArray()};
        var s=new GameSession(new(d));for(int actor=1;actor<=11;actor++)s.Send(new(actor),new(ActionKind.SetVoteChoice,Election:new(1),Candidate:new(actor<=6?1:actor)));
        s.AdvanceTo(5);var result=Assert.Single(s.Engine.Inspect().Elections.Results);
        Assert.Equal(11,result.Ballots.Length);Assert.Equal(exactHalf?24:22,result.TotalWeight);
        Assert.Equal(12,result.Tally.Single(t=>t.Candidate.Value==1).Weight);Assert.Equal(exactHalf?null:new ParticipantId(1),result.Winner);
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,5).Engine.ComputeStateHash());
    }
    [Fact]
    public void TenSuppliersShareOneWindowAndLastActorReceivesAllocationWithoutPrivateLeaks()
    {
        var d=Eleven() with {Elections=new(PeriodTicks:100),
            BuildingTypes=[new("shop",BuildingKind.Shop,[new(0,0)],[new(1,"",1,[],80,0,0,0)])],
            Plots=[new(1,new(0,2),1,OwnerId:1,ShopParticipantId:1),new(2,new(1,2),1,OwnerId:2,ShopParticipantId:2)],
            Buildings=[new(1,1,new(0,2),"shop"),new(2,2,new(1,2),"shop")]};var s=new GameSession(new(d));
        foreach(long actor in Enumerable.Range(2,10))s.Send(new(actor),new(ActionKind.Harvest,ItemId:"wood",Quantity:2));s.AdvanceTo(6);
        foreach(long actor in Enumerable.Range(2,10))s.Send(new(actor),new(ActionKind.MoveTo,Target:new(1,0)));s.AdvanceTo(9);
        foreach(long actor in Enumerable.Range(2,10))s.Send(new(actor),new(ActionKind.SellAvailableToCapital,ItemId:"wood",Quantity:2,OfferId:1));s.AdvanceTo(20);
        Assert.Equal(10,s.Engine.Inspect().Settlements.Length);Assert.All(s.Engine.Inspect().Settlements,x=>Assert.Equal(1,x.Accepted));
        Assert.Equal(100500,s.Engine.Observe(new(11)).Account.BalanceMinor);Assert.Equal(11,s.Engine.Inspect().Accounts.Where(a=>a.Kind=="Participant").Select(a=>a.Id).Distinct().Count());
        s.Send(new(1),new(ActionKind.ProposeSupport,SupportSeller:new(2),Election:new(1),Compensation:[new("wood",1)]));s.Step();
        Assert.Single(s.Engine.ObserveController(new(1)).Support.Proposals);Assert.Single(s.Engine.ObserveController(new(2)).Support.Proposals);Assert.Empty(s.Engine.ObserveController(new(11)).Support.Proposals);
        Assert.DoesNotContain(s.Engine.ObserveController(new(11)).Inventories,i=>i.Id==s.Engine.Observe(new(1)).Participant.InventoryId);
        s.Engine.ValidateInvariants();Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());
    }
}
