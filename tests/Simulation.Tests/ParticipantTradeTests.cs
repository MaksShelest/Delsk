using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class ParticipantTradeTests
{
    private static string Do(GameSession s,GameAction a,long actor=1)=>LandContentFixtures.Reason(s,a,actor);
    private static GameSession Session()=>new(new(TradeFixtures.Scenario()));
    private static InventoryId Store(GameSession s,long actor)=>s.Engine.Observe(new(actor)).OwnBuildings.First(b=>b.Kind==BuildingKind.Warehouse).InputId;
    private static void Stock(GameSession s,long q=8)
    {
        Assert.Equal("Accepted",Do(s,new(ActionKind.Harvest,ItemId:"wood",Quantity:q)));Fixtures.UntilIdle(s);
        Assert.Equal("Accepted",Do(s,new(ActionKind.MoveTo,Target:new(2,0))));Fixtures.UntilIdle(s);
        Assert.Equal("Accepted",Do(s,new(ActionKind.Transfer,Source:s.Engine.Observe(new(1)).Participant.InventoryId,Destination:Store(s,1),ItemId:"wood",Quantity:q)));
        while(s.Engine.TransfersOf(new(1)).Length>0)s.Step();
    }
    private static TradeTerms Sale(GameSession s,long q=4,long price=500,int ttl=30)=>new(TradeKind.Sale,new(1),new(2),"wood",q,price,Store(s,1),FulfillmentTicks:ttl);
    private static TradeNegotiation Last(GameSession s)=>s.Engine.InspectNegotiations().Last();
    private static GameAction Reply(TradeNegotiation n,ActionKind kind,TradeTerms? terms=null)=>new(kind,NegotiationId:n.Id,NegotiationVersion:n.Current.Version,Terms:terms);
    [Theory]
    [InlineData(ActionKind.CreateSellOffer)] [InlineData(ActionKind.CreateBuyOffer)] [InlineData(ActionKind.ProposeSupport)]
    public void OwningOnlyLandDoesNotGrantTradingAccess(ActionKind action)
    {
        var d=TradeFixtures.Scenario();var shops=d.Buildings!.Where(b=>d.BuildingTypes!.Single(t=>t.Id==b.TypeId).Kind==BuildingKind.Shop).ToArray();
        d=d with {Buildings=d.Buildings!.Except(shops).ToArray()};var s=new GameSession(new(d));
        Assert.False(s.Engine.HasTradingAccess(new(1)));Assert.Equal("ShopRequired",Do(s,new(action,Building:new(1),ItemId:"wood",Quantity:1,UnitPriceMinor:1,Destination:Store(s,1),SupportSeller:new(2),Election:new(1),Compensation:[new("wood",1)])));
        Assert.Empty(s.Engine.Ledger());Assert.All(s.Engine.Inspect().Accounts,a=>Assert.Equal(0,a.ReservedMinor));
        Assert.Equal("Accepted",Do(s,new(ActionKind.SetVoteChoice,Election:new(1),Candidate:new(1))));
    }
    [Fact]
    public void CounterPriceIsVersionedAndOnlyLatestOtherPartyCanAccept()
    {
        var s=Session();Stock(s);var terms=Sale(s);
        Assert.Equal("Accepted",Do(s,new(ActionKind.ProposeTrade,Terms:terms)));var first=Last(s);
        Assert.All(s.Engine.Inspect().Accounts,a=>Assert.Equal(0,a.ReservedMinor));Assert.Equal(0,s.Engine.Observe(new(1)).OwnInventories.Single(i=>i.Id==terms.Source).Stock.Single().Reserved);
        Assert.Equal("Accepted",Do(s,Reply(first,ActionKind.CounterTrade,terms with {UnitPriceMinor=650}),2));var current=Last(s);
        Assert.Equal("StaleNegotiationVersion",Do(s,Reply(first,ActionKind.AcceptTrade)));
        Assert.Equal("AwaitingCounterparty",Do(s,Reply(current,ActionKind.AcceptTrade),2));
        long money=s.Engine.Observe(new(2)).Account.BalanceMinor;Assert.Equal("Accepted",Do(s,Reply(current,ActionKind.AcceptTrade)));
        var a=Assert.Single(s.Engine.Inspect().MarketAllocations);Assert.Equal(650,a.UnitPriceMinor);Assert.Equal(4,a.Quantity);Assert.Equal(2600,money-s.Engine.Observe(new(2)).Account.BalanceMinor);
        Assert.Equal("NegotiationClosed",Do(s,Reply(Last(s),ActionKind.AcceptTrade),2));Assert.Single(s.Engine.Inspect().MarketAllocations);
        var original=s.CommandTape.Single(c=>c.Command.Action.Kind==ActionKind.ProposeTrade).Command;
        var decoded=JsonSerializer.Deserialize<CommandEnvelope>(JsonSerializer.Serialize(original,SimulationJson.Options),SimulationJson.Options)!;
        Assert.Equal(original,decoded);string hash=s.Engine.ComputeStateHash();
        Assert.Equal(s.Engine.Result(original.CommandId),s.Engine.Enqueue(new(1),decoded));
        Assert.Equal(hash,s.Engine.ComputeStateHash());Assert.Single(s.Engine.InspectNegotiations());
    }
    [Fact]
    public void SameStockCannotBeSoldThroughAuctionAndDirectDealTwice()
    {
        var s=Session();Stock(s);var shop=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Kind==BuildingKind.Shop);
        Assert.Equal("Accepted",Do(s,new(ActionKind.CreateSellOffer,Building:shop.Id,Source:Store(s,1),ItemId:"wood",Quantity:6,UnitPriceMinor:500)));
        Assert.Equal(new Cell(2,0),Assert.Single(s.Engine.Observe(new(2)).MarketOffers).Position);
        Assert.Equal("Accepted",Do(s,new(ActionKind.ProposeTrade,Terms:Sale(s,4))));
        var before=s.Engine.Observe(new(2)).Account;Assert.Equal("NoStock",Do(s,Reply(Last(s),ActionKind.AcceptTrade),2));Assert.Equal(before,s.Engine.Observe(new(2)).Account);
        Assert.Empty(s.Engine.Inspect().MarketAllocations);Assert.Equal(NegotiationStatus.Proposed,Last(s).Status);
    }
    [Fact]
    public void ConcurrentAcceptanceUsesFreeStockAndRollsBackLosingBudget()
    {
        var s=Session();Stock(s,4);
        Do(s,new(ActionKind.ProposeTrade,Terms:Sale(s)));Do(s,new(ActionKind.ProposeTrade,Terms:Sale(s) with {Buyer=new(3)}));
        var ns=s.Engine.InspectNegotiations();s.Send(new(2),Reply(ns[0],ActionKind.AcceptTrade));s.Send(new(3),Reply(ns[1],ActionKind.AcceptTrade));s.Step();
        Assert.Single(s.Engine.Inspect().MarketAllocations);Assert.Equal(1,s.Engine.InspectNegotiations().Count(n=>n.Status==NegotiationStatus.Accepted));
        Assert.Equal(2000,s.Engine.Inspect().Accounts.Where(a=>a.Kind=="ContractEscrow").Sum(a=>a.BalanceMinor));
        Assert.All(s.Engine.Inspect().Accounts,a=>Assert.Equal(0,a.ReservedMinor));
    }
    [Theory] [InlineData(TradeKind.Purchase)] [InlineData(TradeKind.Supply)]
    public void FutureDeliveryUsesExistingEscrowBondCapacityAndPartialReceipt(TradeKind kind)
    {
        var s=Session();var terms=new TradeTerms(kind,new(1),new(2),"wood",4,500,Destination:Store(s,2),FulfillmentTicks:60);
        long proposer=kind==TradeKind.Purchase?2:1;Assert.Equal("Accepted",Do(s,new(ActionKind.ProposeTrade,Terms:terms),proposer));
        Assert.Equal("Accepted",Do(s,Reply(Last(s),ActionKind.AcceptTrade),proposer==1?2:1));var d=Assert.Single(s.Engine.Inspect().DeliveryContracts);
        Assert.Equal(4*500,d.Quantity*d.UnitPriceMinor);Assert.Equal(4,s.Engine.Observe(new(2)).OwnInventories.Single(i=>i.Id==Store(s,2)).ReservedCapacity);
        Assert.Equal("Accepted",Do(s,new(ActionKind.Harvest,ItemId:"wood",Quantity:4)));Fixtures.UntilIdle(s);
        Do(s,new(ActionKind.MoveTo,Target:new(4,0)));Fixtures.UntilIdle(s);Assert.Equal("Accepted",Do(s,new(ActionKind.DeliverContract,ContractId:d.Id,Quantity:4)));
        while(s.Engine.TransfersOf(new(1)).Length>0)s.Step();Assert.Equal("Fulfilled",Assert.Single(s.Engine.Inspect().DeliveryContracts).Status);
        Assert.All(s.Engine.Inspect().Accounts,a=>Assert.Equal(0,a.ReservedMinor));Assert.Equal(4,Fixtures.Count(s,Store(s,2),"wood"));
    }
    [Fact]
    public void ThirdPartyCannotObserveOrProbeNegotiationsAndPoliticalTermsStayPrivate()
    {
        var s=Session();var terms=new TradeTerms(TradeKind.Support,new(2),new(1),Election:new(1),Compensation:[new("wood",7)]);
        Assert.Equal("Accepted",Do(s,new(ActionKind.ProposeTrade,Terms:terms)));var n=Last(s);
        var before=JsonSerializer.Serialize(s.Engine.PublicEventsSince(0),SimulationJson.Options);
        Assert.Empty(s.Engine.ObserveController(new(3)).Negotiations);Assert.Empty(PlayerDesk.Read(s.Engine,new(3)).Negotiations);
        Assert.Equal("NotAvailable",Do(s,Reply(n,ActionKind.AcceptTrade),3));Assert.Equal("NotAvailable",Do(s,new(ActionKind.AcceptTrade,NegotiationId:n.Id+10000,NegotiationVersion:1),3));
        Assert.DoesNotContain("TradeNegotiation",JsonSerializer.Serialize(s.Engine.PublicEventsSince(0),SimulationJson.Options));
        Assert.Equal("Accepted",Do(s,Reply(n,ActionKind.AcceptTrade),2));Assert.Empty(s.Engine.InspectSupport().Commitments);
        Assert.Equal(SupportProposalStatus.AcceptedAwaitingResources,Assert.Single(s.Engine.InspectSupport().Proposals).Status);
        Assert.DoesNotContain("Compensation",JsonSerializer.Serialize(s.Engine.PublicEventsSince(0),SimulationJson.Options),StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TradeNegotiation",JsonSerializer.Serialize(s.Engine.EventsSince(0),SimulationJson.Options));Assert.NotEmpty(before);
    }
    [Fact]
    public void PickupGuideAndPartialDeadlineOnlyExposeBuyerClaimAndPreserveCollectedGoods()
    {
        var s=Session();Stock(s);Do(s,new(ActionKind.ProposeTrade,Terms:Sale(s,4,ttl:5)));Do(s,Reply(Last(s),ActionKind.AcceptTrade),2);
        var a=Assert.Single(s.Engine.Inspect().MarketAllocations);Assert.Equal("NotAvailable",s.Engine.NavigatePurchase(new(3),a.Id).ReasonCode);
        var guide=s.Engine.NavigatePurchase(new(2),a.Id);Assert.Equal(new Cell(2,0),guide.Position);Assert.NotEmpty(guide.Path);
        Assert.Equal("NotAvailable",Do(s,new(ActionKind.Transfer,Source:a.SourceInventory,Destination:s.Engine.Observe(new(2)).Participant.InventoryId,ItemId:"wood",Quantity:1),2));
        Do(s,new(ActionKind.MoveTo,Target:guide.Position),2);Fixtures.UntilIdle(s,2);
        long available=a.DeadlineTick-s.Engine.Tick;Assert.True(available>0);Do(s,new(ActionKind.CollectPurchase,PurchaseId:a.Id,Quantity:4),2);s.AdvanceTo(a.DeadlineTick);
        var final=Assert.Single(s.Engine.Inspect().MarketAllocations);Assert.InRange(final.CollectedQuantity,1,3);Assert.Equal(4,final.CollectedQuantity+final.ReleasedQuantity);
        Assert.Equal(final.CollectedQuantity*500,final.PaidMinor);Assert.Equal(final.CollectedQuantity,Fixtures.Count(s,s.Engine.Observe(new(2)).Participant.InventoryId,"wood"));
    }
    [Fact]
    public void ExpiryRoundsRejectionAndRestartDoNotReserveOrDuplicateResources()
    {
        var d=TradeFixtures.Scenario() with {Economy=new(Negotiations:new(12,2,2))};var s=new GameSession(new(d));Stock(s);
        Do(s,new(ActionKind.ProposeTrade,Terms:Sale(s)));var n=Last(s);
        Do(s,Reply(n,ActionKind.CounterTrade,n.Current.Terms with {UnitPriceMinor=600}),2);
        Assert.Equal("NegotiationRoundLimit",Do(s,Reply(Last(s),ActionKind.CounterTrade,n.Current.Terms with {UnitPriceMinor=700})));
        var loaded=GameSession.FromSessionJson(s.CreateSessionJson());s.AdvanceTo(n.ExpiresTick);loaded.AdvanceTo(n.ExpiresTick);
        Assert.Equal(NegotiationStatus.Expired,Last(s).Status);Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());
        Assert.Equal("NegotiationClosed",Do(s,Reply(Last(s),ActionKind.AcceptTrade)));
        Assert.All(s.Engine.Inspect().Accounts,a=>Assert.Equal(0,a.ReservedMinor));Assert.Empty(s.Engine.Inspect().MarketAllocations);
    }
    [Fact]
    public void SaveLoadReplayAndCorruptRevisionAreCheckedBeforeStateHash()
    {
        var d=TradeFixtures.Scenario();var s=new GameSession(new(d));Stock(s);Do(s,new(ActionKind.ProposeTrade,Terms:Sale(s)));
        var loaded=GameSession.FromSessionJson(s.CreateSessionJson());var n=Last(s);var reply=Reply(n,ActionKind.AcceptTrade);
        Assert.Equal(Do(s,reply,2),Do(loaded,reply,2));s.AdvanceTo(s.Engine.Tick+8);loaded.AdvanceTo(s.Engine.Tick);
        Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());
        var corrupt=JsonNode.Parse(s.Engine.CreateCheckpointJson())!;corrupt["negotiations"]![0]!["revisions"]![0]!["version"]=3;
        var error=Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(corrupt.ToJsonString(SimulationJson.Options)));
        Assert.NotEqual("StateHashMismatch",error.ReasonCode);
    }

    [Fact]
    public void NegotiatedSupportStillRequiresPhysicalCompensationAndVotesOnlyItsOwnBlock()
    {
        var s=Session();Do(s,new(ActionKind.ProposeTrade,Terms:new(TradeKind.Support,new(2),new(1),Election:new(1),Compensation:[new("wood",2)])));
        Do(s,Reply(Last(s),ActionKind.AcceptTrade),2);long id=Last(s).ContractId;
        LandContentFixtures.Move(s,new(5,0));Assert.NotEqual("Accepted",Do(s,new(ActionKind.SignSupport,SupportProposalId:id)));Assert.Empty(s.Engine.InspectSupport().Commitments);
        LandContentFixtures.Move(s,new(0,0));Do(s,new(ActionKind.Harvest,ItemId:"wood",Quantity:2));Fixtures.UntilIdle(s);
        LandContentFixtures.Move(s,new(5,0));Do(s,new(ActionKind.Transfer,Source:s.Engine.Observe(new(1)).Participant.InventoryId,Destination:s.Engine.Observe(new(1)).Support.OwnSection,ItemId:"wood",Quantity:2));
        while(s.Engine.TransfersOf(new(1)).Length>0)s.Step();
        Assert.Equal("Accepted",Do(s,new(ActionKind.SignSupport,SupportProposalId:id)));
        Assert.NotEqual("Accepted",Do(s,new(ActionKind.SignSupport,SupportProposalId:id)));Assert.Single(s.Engine.InspectSupport().Commitments);
        Do(s,new(ActionKind.SetVoteChoice,Election:new(1),Candidate:new(3)),2);
        var restored=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(100);restored.AdvanceTo(100);
        Assert.Equal(new ParticipantId(1),s.Engine.Inspect().Elections.Results.Single().Winner);
        Assert.Equal(s.Engine.ComputeStateHash(),restored.Engine.ComputeStateHash());
        Assert.Equal(2,Fixtures.Count(s,s.Engine.Observe(new(2)).Support.OwnSection,"wood"));
    }
    [Fact]
    public void PendingPrivateTalkDoesNotAdvancePublicObjectIdsOrExposeItsTerms()
    {
        var a=Session();var b=Session();
        Do(a,new(ActionKind.ProposeTrade,Terms:new(TradeKind.Support,new(2),new(1),Election:new(1),Compensation:[new("wood",2)])));
        b.Step();
        var x=a.Engine.ObserveController(new(3));var y=b.Engine.ObserveController(new(3));
        Assert.Equal(JsonSerializer.Serialize(x,SimulationJson.Options),JsonSerializer.Serialize(y,SimulationJson.Options));
        Assert.Equal(JsonSerializer.Serialize(a.Engine.PublicEventsSince(0),SimulationJson.Options),JsonSerializer.Serialize(b.Engine.PublicEventsSince(0),SimulationJson.Options));
    }
    [Fact]
    public void SeveralPickupTripsPreserveClaimAndRestoreMidTransfer()
    {
        var d=TradeFixtures.Scenario();d=d with {Participants=d.Participants.Select(p=>p.Id==2?p with {InventoryCapacity=2}:p).ToArray()};
        var s=new GameSession(new(d));Stock(s);Do(s,new(ActionKind.ProposeTrade,Terms:Sale(s,4,ttl:80)));Do(s,Reply(Last(s),ActionKind.AcceptTrade),2);
        var claim=s.Engine.Inspect().MarketAllocations.Single();Do(s,new(ActionKind.MoveTo,Target:new(2,0)),2);Fixtures.UntilIdle(s,2);
        Do(s,new(ActionKind.CollectPurchase,PurchaseId:claim.Id,Quantity:2),2);var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));
        for(int trip=0;trip<2;trip++)
        {
            while(s.Engine.TransfersOf(new(2)).Length>0){s.Step();copy.Step();}
            var target=new Cell(4,0);var pack=s.Engine.Observe(new(2)).Participant.InventoryId;
            var move=new GameAction(ActionKind.MoveTo,Target:target);Do(s,move,2);Do(copy,move,2);while(s.Engine.Observe(new(2)).Participant.Busy){s.Step();copy.Step();}
            var unload=new GameAction(ActionKind.Transfer,Source:pack,Destination:Store(s,2),ItemId:"wood",Quantity:2);Do(s,unload,2);Do(copy,unload,2);
            while(s.Engine.TransfersOf(new(2)).Length>0){s.Step();copy.Step();}
            if(trip==0)
            {
                move=new(ActionKind.MoveTo,Target:new(2,0));Do(s,move,2);Do(copy,move,2);while(s.Engine.Observe(new(2)).Participant.Busy){s.Step();copy.Step();}
                var collect=new GameAction(ActionKind.CollectPurchase,PurchaseId:claim.Id,Quantity:2);Do(s,collect,2);Do(copy,collect,2);
            }
        }
        Assert.Equal("Fulfilled",s.Engine.Inspect().MarketAllocations.Single().Status);Assert.Equal(4,Fixtures.Count(s,Store(s,2),"wood"));
        Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());
    }

    [Fact]
    public void FactoryInputLotReservesOnlyFreeStockWhileProductionContinues()
    {
        var d=TradeFixtures.Scenario();d=d with {Plots=[..d.Plots!,new(100,new(6,0),1,OwnerId:1)],
            BuildingTypes=[..d.BuildingTypes!,new("mill",BuildingKind.Production,[new(0,0)],[new(1,"sawmill",1,[],40,40,2,1)])],
            Buildings=[..d.Buildings!,new(100,1,new(6,0),"mill",Staff:2)]};
        var s=new GameSession(new(d));Do(s,new(ActionKind.Harvest,ItemId:"wood",Quantity:6));Fixtures.UntilIdle(s);LandContentFixtures.Move(s,new(6,0));
        var factory=s.Engine.Observe(new(1)).OwnEnterprises.Single();Do(s,new(ActionKind.Transfer,Source:s.Engine.Observe(new(1)).Participant.InventoryId,Destination:factory.InputId,ItemId:"wood",Quantity:6));
        while(s.Engine.TransfersOf(new(1)).Length>0)s.Step();
        var shop=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Kind==BuildingKind.Shop);
        Assert.Equal("Accepted",Do(s,new(ActionKind.CreateSellOffer,Building:shop.Id,Source:factory.InputId,ItemId:"wood",Quantity:4,UnitPriceMinor:500)));
        var offer=s.Engine.Observe(new(2)).MarketOffers.Single();Assert.Equal(new Cell(6,0),offer.Position);
        Do(s,new(ActionKind.SubmitBuyRequest,MarketOfferId:offer.Id,Quantity:4,MaximumPriceMinor:500),2);
        Do(s,new(ActionKind.SetProductionEnabled,Enterprise:factory.Id,Enabled:true));s.AdvanceTo(s.Engine.Tick+8);
        Assert.Equal(1,s.Engine.Observe(new(1)).OwnEnterprises.Single().CompletedBatches);
        Assert.Equal(4,Fixtures.Count(s,factory.InputId,"wood"));Assert.Equal(4,s.Engine.Observe(new(1)).OwnInventories.Single(x=>x.Id==factory.InputId).Stock.Single().Reserved);
        Assert.Equal(s.Engine.ComputeStateHash(),CheckpointStore.Decode(CheckpointStore.Encode(s)).Engine.ComputeStateHash());
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void PersonalAndForeignInventoriesCannotBePublicLotSources(bool personal)
    {
        var s=Session();var o=s.Engine.Observe(new(1));var source=personal?o.Participant.InventoryId:Store(s,2);
        Assert.Equal("NotAvailable",Do(s,new(ActionKind.CreateSellOffer,Building:o.OwnBuildings.Single(b=>b.Kind==BuildingKind.Shop).Id,Source:source,ItemId:"wood",Quantity:1,UnitPriceMinor:500)));
        Assert.Empty(s.Engine.Inspect().MarketOffers);
    }
    [Fact]
    public void BuyerWithoutBuiltShopCannotReservePublishedStockOrAcceptDirectTrade()
    {
        var d=TradeFixtures.Scenario();d=d with {Buildings=d.Buildings!.Where(b=>b.OwnerId!=2 || d.BuildingTypes!.Single(t=>t.Id==b.TypeId).Kind!=BuildingKind.Shop).ToArray()};
        var s=new GameSession(new(d));Stock(s);var shop=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Kind==BuildingKind.Shop);
        Do(s,new(ActionKind.CreateSellOffer,Building:shop.Id,Source:Store(s,1),ItemId:"wood",Quantity:2,UnitPriceMinor:500));var offer=s.Engine.Observe(new(2)).MarketOffers.Single();
        Assert.Equal("ShopRequired",Do(s,new(ActionKind.SubmitBuyRequest,MarketOfferId:offer.Id,Quantity:2,MaximumPriceMinor:500),2));
        Assert.Equal("ShopRequired",Do(s,new(ActionKind.ProposeTrade,Terms:Sale(s))));
        Assert.Empty(s.Engine.InspectNegotiations());Assert.Equal(0,s.Engine.Observe(new(2)).Account.ReservedMinor);
    }

    [Fact]
    public void ExistingSupportNegotiationCanAcceptAndPhysicallySignOnInclusiveVotingDeadline()
    {
        var d=TradeFixtures.Scenario();var s=new GameSession(new(d));
        Do(s,new(ActionKind.Harvest,ItemId:"wood",Quantity:2));Fixtures.UntilIdle(s);LandContentFixtures.Move(s,new(5,0));
        Do(s,new(ActionKind.Transfer,Source:s.Engine.Observe(new(1)).Participant.InventoryId,Destination:s.Engine.Observe(new(1)).Support.OwnSection,ItemId:"wood",Quantity:2));
        while(s.Engine.TransfersOf(new(1)).Length>0)s.Step();s.AdvanceTo(70);
        Do(s,new(ActionKind.ProposeTrade,Terms:new(TradeKind.Support,new(2),new(1),Election:new(1),Compensation:[new("wood",2)])));
        var n=Last(s);Assert.Equal(100,n.ExpiresTick);s.AdvanceTo(99);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));
        foreach(var session in new[]{s,copy})
        {
            string accept=session.Send(new(2),Reply(n,ActionKind.AcceptTrade));
            string sign=session.Send(new(1),new(ActionKind.SignSupport,SupportProposalId:1));
            string fresh=session.Send(new(3),new(ActionKind.ProposeTrade,Terms:new(TradeKind.Support,new(1),new(3),Election:new(1),Compensation:[new("wood",1)])));
            session.Step();Assert.Equal(ResultStatus.Accepted,session.Engine.Result(accept)!.Status);Assert.Equal(ResultStatus.Accepted,session.Engine.Result(sign)!.Status);
            Assert.Equal("InvalidTradeTerms",session.Engine.Result(fresh)!.ReasonCode);
            Assert.Equal(PoliticalCommitmentStatus.Executed,session.Engine.InspectSupport().Commitments.Single().Status);
            Assert.Equal(new ParticipantId(1),session.Engine.Inspect().Elections.Results.Single().Winner);
        }
        Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,100).Engine.ComputeStateHash());
    }
}
