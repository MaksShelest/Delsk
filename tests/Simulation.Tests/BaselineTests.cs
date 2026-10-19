using System.Collections.Immutable;
using System.Text.Json;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class BaselineTests
{
    private static T Read<T>(string name)
    {using var stream=typeof(BaselineTests).Assembly.GetManifestResourceStream("Fixture."+name)!;return JsonSerializer.Deserialize<T>(stream,SimulationJson.Options)!;}
    private static GameSession Session(string name="economy",bool reverse=false)
    {
        var d=Read<ScenarioDefinition>("baseline-"+name+".json");
        var config=Read<AutonomousConfiguration>(name=="economy"?"baseline-controllers.json":"baseline-"+name+"-controllers.json");
        var s=new GameSession(new SimulationEngine(d));var policies=config.Npcs.Select(n=>new AlgorithmicController(n,d.Seed^(ulong)n.ActorId,d.Participants.Select(p=>p.Id))).ToArray();
        s.AttachControllers(reverse?policies.Reverse():policies);return s;
    }
    private static readonly Lazy<GameSession> Economy=new(()=>{var s=Session();s.AdvanceTo(1200);return s;});
    private static readonly Lazy<GameSession> Auction=new(()=>{var s=Session("auction");s.AdvanceTo(180);return s;});
    private static readonly Lazy<GameSession> Politics=new(()=>{var s=Session("politics");s.AdvanceTo(160);return s;});
    private static readonly Lazy<GameSession> Logistics=new(()=>{var s=Session("logistics");s.AdvanceTo(360);return s;});
    private static string Execute(GameSession s,GameAction a,long actor=1)=>LandContentFixtures.Reason(s,a,actor);
    private static void PolicyStep(GameSession s,AlgorithmicController policy)
    {
        var feedback=s.CommandTape.Where(c=>c.AuthenticatedActor==policy.Actor).Select(c=>s.Engine.Result(c.Command.CommandId)).Where(r=>r is not null).Select(r=>r!).ToImmutableArray();
        var decision=policy.Decide(s.Engine.ObserveController(policy.Actor),feedback);
        foreach(var a in decision.Actions){string id=s.Send(policy.Actor,a);policy.CommandSubmitted(id);}s.Step();
    }
    [Fact]
    public void ThreeNpcsIndependentlyMineProduceTradeAndFulfilEveryDueContractWithoutLlm()
    {
        var s=Economy.Value;var w=s.Engine.Inspect();
        foreach(long actor in new long[]{2,3,4})
        {
            Assert.Contains(s.Engine.EventsSince(0),e=>e.ActorId?.Value==actor && e.Type is "HarvestCompleted" or "EquipmentCycleCompleted");
            Assert.Contains(w.Enterprises,e=>e.OwnerId.Value==actor && e.CompletedBatches>0);
            Assert.Contains(w.Settlements,e=>e.Seller.Value==actor && e.PaidMinor>0);
        }
        Assert.NotEmpty(w.DeliveryContracts);Assert.All(w.DeliveryContracts.Where(c=>c.DeadlineTick<=w.Tick),c=>Assert.Equal("Fulfilled",c.Status));
        Assert.Contains(s.CommandTape,c=>c.Command.Action.Kind==ActionKind.AcceptDeliveryQuantity);
        Assert.Contains(s.CommandTape,c=>c.Command.Action.Kind==ActionKind.DeliverContract);
        Assert.Contains(s.CommandTape,c=>c.Command.Action.Kind==ActionKind.SetWage);
        Assert.DoesNotContain(s.CommandTape,c=>c.AuthenticatedActor.Value==1);
    }
    [Fact]
    public void AutonomousMerchantAndTwoBuyersUseScarceAuctionBoundedPricesAndPhysicalPickup()
    {
        var s=Auction.Value;var events=s.Engine.EventsSince(0);
        Assert.Contains(events,e=>e.Type=="MarketAuctionStarted");Assert.Contains(events,e=>e.Type=="MarketBidRaised");
        Assert.Contains(s.CommandTape,c=>c.Command.Action.Kind==ActionKind.CreateSellOffer && c.AuthenticatedActor.Value==2);
        Assert.Contains(s.CommandTape,c=>c.Command.Action.Kind==ActionKind.CollectPurchase);
        foreach(var c in s.CommandTape.Where(c=>c.Command.Action.Kind==ActionKind.RaiseBid))
        {
            var bid=s.ControllerTurns.Where(t=>t.Actor==c.AuthenticatedActor).SelectMany(t=>t.Observation.OwnMarketBids).First(b=>b.OfferId==c.Command.Action.MarketOfferId);
            Assert.InRange(c.Command.Action.UnitPriceMinor,bid.CurrentPriceMinor,bid.MaximumPriceMinor);
        }
        Assert.All(s.CommandTape.Where(c=>c.Command.Action.Kind==ActionKind.RaiseBid).GroupBy(c=>(c.AuthenticatedActor,c.Command.Action.MarketOfferId,c.Command.Action.MarketRound)),g=>Assert.Single(g));
        Assert.All(s.Engine.Inspect().MarketAllocations,c=>Assert.True(c.CollectedQuantity>0 || c.ClosedTick is null));
        Assert.DoesNotContain(events,e=>e.Type=="CommandRejected" && e.ReasonCode is "NoMoney" or "BidLimitExceeded" or "NoCapacity");
    }
    [Fact]
    public void AutonomousPoliticsAcceptsRejectsDeliversAndExecutesOnlyItsOwnBlocks()
    {
        var s=Politics.Value;var politics=s.Engine.InspectSupport();
        Assert.Contains(s.Engine.InspectNegotiations(),n=>n.Current.Terms.Kind==TradeKind.Support && n.Status==NegotiationStatus.Rejected);
        Assert.Contains(politics.Commitments,c=>c.Status==PoliticalCommitmentStatus.Executed);
        Assert.Contains(s.CommandTape,c=>c.Command.Action.Kind==ActionKind.AcceptTrade && c.Command.Action.NegotiationId>0);
        Assert.Contains(s.CommandTape,c=>c.Command.Action.Kind==ActionKind.RejectTrade && c.Command.Action.NegotiationId>0);
        Assert.Contains(s.CommandTape,c=>c.Command.Action.Kind==ActionKind.SignSupport);
        Assert.All(s.CommandTape.Where(c=>c.Command.Action.Kind==ActionKind.SignSupport).GroupBy(c=>(c.AuthenticatedActor,c.Command.Action.SupportProposalId)),g=>Assert.Single(g));
        foreach(var result in s.Engine.Inspect().Elections.Results)
        {
            Assert.Equal(4,result.Ballots.Length);
            Assert.Equal(11,result.TotalWeight);
            foreach(var c in politics.Commitments.Where(c=>c.Election==result.Id))
                Assert.Equal(c.Candidate,result.Ballots.Single(b=>b.Voter==c.Seller).Candidate);
        }
        Assert.All(s.ControllerTurns,t=>{
            Assert.All(t.Observation.Support.Proposals,p=>Assert.True(p.Candidate==t.Actor || p.Seller==t.Actor));
            Assert.All(t.Observation.Support.Commitments,c=>Assert.True(c.Candidate==t.Actor || c.Seller==t.Actor));
        });
    }
    [Fact]
    public void ThreeNpcsPurchaseTransportAssignOutputAndCompleteRealPaidTrips()
    {
        var s=Logistics.Value;var w=s.Engine.Inspect();Assert.Equal(3,w.Logistics.Length);
        foreach(long actor in new long[]{2,3,4})
        {
            Assert.Contains(w.Logistics,l=>l.OwnerId.Value==actor && l.CompletedTrips>0);
            Assert.Contains(w.Enterprises,e=>e.OwnerId.Value==actor && e.CompletedBatches>0);
        }
        Assert.Contains(s.CommandTape,c=>c.Command.Action.Kind==ActionKind.QueueLogisticsJob);
        Assert.Contains(s.Engine.EventsSince(0),e=>e.Type=="LogisticsWagesPaid" && e.AmountMinor>0);
        Assert.Contains(s.Engine.EventsSince(0),e=>e.Type=="LogisticsStorageReceived");
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PolicyConfiguresExistingVehicleForActualInboundAndContractWork(bool inbound)
    {
        var d=Read<ScenarioDefinition>("logistics-queue.json");var s=new GameSession(new SimulationEngine(d));
        ScriptedScenarios.LogisticsQueueCycle(s,d);
        var l=Assert.Single(s.Engine.Inspect().Logistics);
        Assert.Equal("Accepted",Execute(s,new(ActionKind.SetLogisticsEnabled,Logistics:l.Id,Enabled:false)));
        foreach(var job in l.Queue)Assert.Equal("Accepted",Execute(s,new(ActionKind.QueueLogisticsJob,Logistics:l.Id,
            ContractId:job.ContractId,Source:job.InboundSource,Destination:job.OwnDestination,Enabled:false)));
        var storage=s.Engine.Inspect().Buildings.Single(b=>b.Id.Value==2).InputId;
        var carrier=s.Engine.Observe(new(1)).Participant.InventoryId;
        if(inbound)
        {
            LandContentFixtures.Move(s,new(0,0));LandContentFixtures.Harvest(s,"wood",4);LandContentFixtures.Move(s,new(5,0));
            Assert.Equal("Accepted",Execute(s,new(ActionKind.Transfer,Source:carrier,Destination:storage,ItemId:"wood",Quantity:4)));
        }
        else
        {
            LandContentFixtures.Move(s,new(5,0));Assert.Equal("Accepted",Execute(s,new(ActionKind.Transfer,Source:storage,Destination:carrier,ItemId:"plank",Quantity:10)));
            LandContentFixtures.Move(s,new(2,0));Assert.Equal("Accepted",Execute(s,new(ActionKind.Transfer,Source:carrier,Destination:l.SourceInventory,ItemId:"plank",Quantity:10)));
            var receiver=s.Engine.Inspect().Buildings.Single(b=>b.Id.Value==3).InputId;
            Assert.Equal("Accepted",Execute(s,new(ActionKind.CreateBuyOffer,Destination:receiver,ItemId:"plank",Quantity:10,UnitPriceMinor:3000),2));
            long offer=s.Engine.Observe(new(2)).BuyOffers.Max(o=>o.Id);
            Assert.Equal("Accepted",Execute(s,new(ActionKind.AcceptDeliveryQuantity,BuyOfferId:offer,Quantity:10)));s.AdvanceTo(s.Engine.Tick+5);
        }
        long trips=l.CompletedTrips;
        var policy=new AlgorithmicController(new(1,"wood",ExpansionWeight:0,TradeEnabled:false,PoliticsEnabled:false),42,d.Participants.Select(p=>p.Id));
        for(int i=0;i<80 && s.Engine.Inspect().Logistics.Single().CompletedTrips==trips;i++)PolicyStep(s,policy);
        Assert.True(s.Engine.Inspect().Logistics.Single().CompletedTrips>trips);
        Assert.Contains(s.CommandTape,c=>c.Command.Action.Kind==ActionKind.QueueLogisticsJob &&
            (inbound?c.Command.Action.Source==storage:c.Command.Action.ContractId==s.Engine.Inspect().DeliveryContracts.Max(d=>d.Id)));
        if(inbound)Assert.Contains(s.Engine.EventsSince(0),e=>e.Type=="LogisticsInputReceived");
        else Assert.Equal("Fulfilled",s.Engine.Inspect().DeliveryContracts.MaxBy(d=>d.Id)!.Status);
    }
    [Theory]
    [InlineData(2,2,100,ActionKind.AcceptSupport)]
    [InlineData(2,1,100,ActionKind.RejectSupport)]
    [InlineData(1,40,100,ActionKind.RejectSupport)]
    [InlineData(1,40,0,ActionKind.AcceptSupport)]
    public void SupportDecisionValuesCompensationAndVetoesRivalVictoryEvenForLargeGift(long candidate,long quantity,int ownPriority,ActionKind expected)
    {
        var d=Read<ScenarioDefinition>("baseline-politics.json");var s=new GameSession(new SimulationEngine(d));
        Execute(s,new(ActionKind.ProposeSupport,SupportSeller:new(3),Election:new(1),Compensation:[new("wood",quantity)]),candidate);
        var policy=new AlgorithmicController(new(3,"wood",OwnVictoryWeight:ownPriority,SupportPricePerVoteMinor:1000),42,d.Participants.Select(p=>p.Id));
        var decision=policy.Decide(s.Engine.ObserveController(new(3)),[]);
        Assert.Equal(expected,Assert.Single(decision.Actions).Kind);
    }
    [Fact]
    public void AcceptedPoliticalProposalDoesNotMeanItIsSignedOrResourcesTeleport()
    {
        var d=Read<ScenarioDefinition>("baseline-politics.json");var s=new GameSession(new SimulationEngine(d));
        Execute(s,new(ActionKind.ProposeSupport,SupportSeller:new(3),Election:new(1),Compensation:[new("wood",2)]),2);
        var seller=new AlgorithmicController(new(3,"wood"),42,d.Participants.Select(p=>p.Id));PolicyStep(s,seller);
        Assert.Equal(SupportProposalStatus.AcceptedAwaitingResources,Assert.Single(s.Engine.InspectSupport().Proposals).Status);
        Assert.Empty(s.Engine.InspectSupport().Commitments);Assert.Equal(0,s.Engine.Inspect().Flows.Sum(f=>f.Harvested));
    }
    [Fact]
    public void ExistingContractIsFulfilledBeforeExpansionAndCarriedInputsAreNotDivertedToOwnFactory()
    {
        var d=Fixtures.Cycle();var s=Fixtures.Session(d);Execute(s,new(ActionKind.Harvest,ItemId:"wood",Quantity:4));Fixtures.UntilIdle(s);
        Execute(s,new(ActionKind.AcceptDeliveryQuantity,OfferId:1,Quantity:4));s.AdvanceTo(s.Engine.Tick+5);
        var policy=new AlgorithmicController(new(1,"wood"),42,d.Participants.Select(p=>p.Id));
        for(int i=0;i<35 && s.Engine.Inspect().DeliveryContracts.Single().ClosedTick is null;i++)PolicyStep(s,policy);
        var c=Assert.Single(s.Engine.Inspect().DeliveryContracts);Assert.Equal("Fulfilled",c.Status);Assert.Equal(4,c.DeliveredQuantity);
        Assert.Equal(2000,c.PaidMinor);Assert.DoesNotContain(s.CommandTape,c=>c.Command.Action.Kind==ActionKind.Build);
        Assert.Equal(0,Fixtures.Count(s,s.Engine.Observe(new(1)).OwnEnterprises.Single().InputId,"wood"));
    }
    [Fact]
    public void RemoteVoteResponsePreservesTheRemainingContractRouteAcrossSaveLoad()
    {
        var d=SupportTests.Scenario(period:200);var s=Fixtures.Session(d);
        Execute(s,new(ActionKind.Harvest,ItemId:"wood",Quantity:4));Fixtures.UntilIdle(s);Execute(s,new(ActionKind.AcceptDeliveryQuantity,OfferId:1,Quantity:4));s.AdvanceTo(s.Engine.Tick+5);
        var policy=new AlgorithmicController(new(1,"wood"),42,d.Participants.Select(p=>p.Id));PolicyStep(s,policy);PolicyStep(s,policy);
        Assert.True(s.Engine.Observe(new(1)).Participant.Busy);
        Execute(s,new(ActionKind.ProposeSupport,SupportSeller:new(1),Election:new(1),Compensation:[new("wood",1)]),2);
        PolicyStep(s,policy);var state=JsonSerializer.Deserialize<AlgorithmState>(policy.Capture().StateJson,SimulationJson.Options)!;
        Assert.NotEmpty(state.SuspendedPlan);Assert.Equal("DeliverContract",state.SuspendedGoal);
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));var restored=AlgorithmicController.Restore(policy.Capture());
        for(int i=0;i<35 && s.Engine.Inspect().DeliveryContracts.Single().ClosedTick is null;i++)
        {PolicyStep(s,policy);PolicyStep(loaded,restored);Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());}
        Assert.Equal("Fulfilled",s.Engine.Inspect().DeliveryContracts.Single().Status);
    }
    [Fact]
    public void ImpossibleDeadlineDoesNotProduceAnInvalidDeliverPlanOrExpansion()
    {
        var d=Fixtures.Cycle() with {Delivery=new(ContractTtlTicks:3)};var s=Fixtures.Session(d);
        Execute(s,new(ActionKind.AcceptDeliveryQuantity,OfferId:1,Quantity:2));s.AdvanceTo(s.Engine.Tick+5);
        var policy=new AlgorithmicController(new(1,"wood"),42,d.Participants.Select(p=>p.Id));var decision=policy.Decide(s.Engine.ObserveController(new(1)),[]);
        Assert.DoesNotContain(decision.Actions,a=>a.Kind is ActionKind.Build or ActionKind.BuyEquipment or ActionKind.DeliverContract);
        Assert.DoesNotContain(JsonSerializer.Deserialize<AlgorithmState>(policy.Capture().StateJson,SimulationJson.Options)!.Plan,p=>p.Action.Kind==ActionKind.DeliverContract);
    }
    [Theory]
    [InlineData("auction",25,130)] [InlineData("auction",30,130)] [InlineData("politics",8,140)] [InlineData("politics",19,140)] [InlineData("economy",350,700)]
    public void DirectLoadRestoresAuctionReviewsSuspendedPlansGoalsPoliticsAndPrivateRandom(string name,int cut,int end)
    {
        var s=Session(name);s.AdvanceTo(cut);var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));loaded.RestoreControllers(AlgorithmicController.Restore);
        while(s.Engine.Tick<end){s.Step();loaded.Step();Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());Assert.Equal(s.ComputeControllerStateHash(),loaded.ComputeControllerStateHash());}
        Assert.Equal(s.CommandTape,loaded.CommandTape);
        Assert.Equal(JsonSerializer.Serialize(s.ControllerTurns,SimulationJson.Options),JsonSerializer.Serialize(loaded.ControllerTurns,SimulationJson.Options));
    }
    [Theory]
    [InlineData("auction")] [InlineData("politics")]
    public void SameSeedAndControllerPermutationGiveIdenticalDecisionsAndEveryInput(string name)
    {
        var a=Session(name);var b=Session(name,true);a.AdvanceTo(160);b.AdvanceTo(160);
        Assert.Equal(a.Engine.ComputeStateHash(),b.Engine.ComputeStateHash());Assert.Equal(a.ComputeControllerStateHash(),b.ComputeControllerStateHash());Assert.Equal(a.CommandTape,b.CommandTape);
        Assert.Equal(JsonSerializer.Serialize(a.ControllerTurns,SimulationJson.Options),JsonSerializer.Serialize(b.ControllerTurns,SimulationJson.Options));
        var replay=GameSession.Replay(a.Engine.Configuration(),a.CommandTape,160);Assert.Equal(a.Engine.ComputeStateHash(),replay.Engine.ComputeStateHash());Assert.False(replay.HasControllers);
    }
    [Fact]
    public void EveryNpcRecoversFromZeroCashWithPeriodicBootstrapAndNoLlm()
    {
        var d=Read<ScenarioDefinition>("baseline-economy.json");d=d with{Participants=d.Participants.Select(p=>p with{CashMinor=0}).ToArray()};
        var s=new GameSession(new SimulationEngine(d));var config=Read<AutonomousConfiguration>("baseline-controllers.json");
        s.AttachControllers(config.Npcs.Select(n=>new AlgorithmicController(n,42^(ulong)n.ActorId,d.Participants.Select(p=>p.Id))));s.AdvanceTo(180);
        foreach(long actor in new long[]{2,3,4})Assert.Contains(s.Engine.Inspect().Settlements,c=>c.Seller.Value==actor && c.PaidMinor>0);
        Assert.Contains(s.ControllerTurns,t=>t.Decision.Goal=="RecoverCash");
    }
    [Fact]
    public void DisabledOrInvalidEconomicSettingsAndMalformedImmutableCompensationFailSafely()
    {
        Assert.Throws<ArgumentException>(()=>new AlgorithmicController(new(2,"wood",MaximumBuyPriceBasisPoints:0),42,[]));
        var s=Fixtures.Session();var command=new CommandEnvelope("bad-compensation",new(1),1,0,1,2,new(ActionKind.ProposeSupport,Compensation:default(ImmutableArray<MaterialCost>)));
        string hash=s.Engine.ComputeStateHash();Assert.Equal("InvalidEnvelope",s.Engine.Enqueue(new(1),command)!.ReasonCode);Assert.Equal(hash,s.Engine.ComputeStateHash());
    }
}
