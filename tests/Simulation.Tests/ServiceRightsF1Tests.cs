using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;
namespace Simulation.Tests;
public class ServiceRightsF1Tests
{
    [Fact]public void SeveralTripsUseVolumeAndForecastDoesNotCreateCapacityHolds()
    {
        var(s,_,t)=ServiceF1Tests.Prepare(quantity:30);var q=ServiceF1Tests.Quote(s,t with{Quantity=25});var before=StorageC1Tests.Inventory(s,t.Destination).ReservedCapacity;
        var order=ServiceF1Tests.Request(s,t);Assert.Equal(before,StorageC1Tests.Inventory(s,t.Destination).ReservedCapacity);
        Assert.Equal(60,order.Quote.Volume);Assert.Equal(0,order.Quote.ExpectedIncomingVolume);ServiceF1Tests.Exact(s,"forecast");
    }
    [Fact]public void StaleQuoteFailsAtomicallyWithoutBlockingVehicleOrGoods()
    {
        var(s,v,t)=ServiceF1Tests.Prepare();var q=ServiceF1Tests.Quote(s,t);
        var action=new GameAction(ActionKind.RequestDeliveryService,VehicleId:v.Id,Source:t.Source,Destination:t.Destination,ItemId:t.ItemId,Quantity:t.Quantity,ServiceQuote:"stale");
        ServiceF1Tests.Step(s,action,accepted:false);Assert.Empty(s.Engine.InspectServices()!.Orders);Assert.Equal(0,StorageC1Tests.Inventory(s,t.Source).Stock.Single().Reserved);
        ServiceF1Tests.Step(s,new(ActionKind.SetDeliveryService,VehicleId:v.Id,Enabled:false),2);Assert.Null(s.Engine.QuoteDeliveryService(new(1),t).Quote);ServiceF1Tests.Exact(s,"stale-quote");
    }
    [Fact]public void PendingLeasePreventsBoardingAndLogisticsAssignmentAndReleasesOnCancel()
    {
        var(s,v,t)=ServiceF1Tests.Prepare();var order=ServiceF1Tests.Request(s,t);
        ServiceF1Tests.Step(s,new(ActionKind.BoardVehicle,VehicleId:v.Id),2,false);
        ServiceF1Tests.Step(s,new(ActionKind.AssignVehicle,VehicleId:v.Id,Enterprise:new(8)),2,false);
        ServiceF1Tests.Step(s,new(ActionKind.CancelDeliveryService,ServiceId:order.Id));Assert.True(s.Engine.ServicesOf(new(2))!.Catalog.Single().Available);
        Assert.Empty(s.Engine.Observe(new(2)).OwnLogistics);ServiceF1Tests.Exact(s,"busy");
    }
    [Fact]public void CarrierCanAcceptExistingRequestAfterGoingOffline()
    {
        var(s,v,t)=ServiceF1Tests.Prepare();var order=ServiceF1Tests.Request(s,t);ServiceF1Tests.Step(s,new(ActionKind.SetDeliveryService,VehicleId:v.Id,Enabled:false),2);
        ServiceF1Tests.Step(s,new(ActionKind.AcceptDeliveryService,ServiceId:order.Id),2);Assert.Equal("Accepted",s.Engine.InspectServices()!.Orders.Single().State);ServiceF1Tests.Exact(s,"offline");
    }
    [Fact]public void AlgorithmicCarrierRespondsUsingOrdinaryProfitableCommandAndRestores()
    {
        var(s,_,t)=ServiceF1Tests.Prepare();var order=ServiceF1Tests.Request(s,t);
        var controller=new AlgorithmicController(new(2,"wood",DecisionIntervalTicks:1),42,[1,2]);
        var restored=AlgorithmicController.Restore(controller.Capture());var observation=s.Engine.ObserveController(new(2));
        var decision=controller.Decide(observation,[]);var replayDecision=restored.Decide(observation,[]);
        Assert.Equal(decision.Actions,replayDecision.Actions);var action=Assert.Single(decision.Actions);Assert.Equal(ActionKind.AcceptDeliveryService,action.Kind);
        ServiceF1Tests.Step(s,action,2);Assert.Equal("Accepted",s.Engine.InspectServices()!.Orders.Single().State);ServiceF1Tests.Exact(s,"npc-carrier");
    }
    [Fact]public void ActualStaffLossRequiresCustomerReconfirmationAndNeverExtraDebit()
    {
        var(s,_,t)=ServiceF1Tests.Prepare();StorageC1Tests.Move(s,new(7,4));ServiceF1Tests.Step(s,new(ActionKind.BuildLoadingPlatform,Building:new(9),EquipmentLevel:1));s.Step();
        var platform=s.Engine.InspectFleet()!.Platforms.Single();Assert.True(platform.Group.Headcount>0);
        var order=ServiceF1Tests.Request(s,t);long balance=s.Engine.Observe(new(1)).Account.BalanceMinor;
        ServiceF1Tests.Step(s,new(ActionKind.SetWage,Group:platform.Group.Id,WageMinor:0));s.Step();
        ServiceF1Tests.Step(s,new(ActionKind.AcceptDeliveryService,ServiceId:order.Id),2);
        order=s.Engine.InspectServices()!.Orders.Single();Assert.Equal("Requote",order.State);Assert.NotNull(order.ProposedQuote);Assert.Equal(balance,s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.True(order.ProposedQuote.EtaTicks>order.Quote.EtaTicks);ServiceF1Tests.Exact(s,"requote");
        ServiceF1Tests.Step(s,new(ActionKind.ConfirmDeliveryService,ServiceId:order.Id,ServiceQuote:order.ProposedQuote.Token));Assert.Equal("Accepted",s.Engine.InspectServices()!.Orders.Single().State);ServiceF1Tests.Exact(s,"reconfirmed");
    }
    [Fact]public void ThreeHundredVolumeOnSeventyVehicleMeansFiveTripsWithoutFinalReturn()
    {
        var(s,_,t)=ServiceF1Tests.Prepare(quantity:150);var q=ServiceF1Tests.Quote(s,t);Assert.Equal(300,q.Volume);Assert.Equal(35,q.QuantityPerTrip);Assert.Equal(5,q.Trips);Assert.Equal(60,q.HandlingTicks);
        var order=ServiceF1Tests.Request(s,t);Assert.Equal(0,StorageC1Tests.Inventory(s,t.Destination).ReservedCapacity);ServiceF1Tests.Exact(s,"five-trips");
    }
    [Fact]public void ServiceHoldFailsForInsufficientFreeMoneyAndLeavesNoPartialStockHold()
    {
        var definition=ServiceF1Tests.Definition();definition=definition with{Participants=[..definition.Participants.Select(p=>p.Id==1?p with{CashMinor=100}:p)]};
        var s=new GameSession(new(definition));StorageC1Tests.Harvest(s,"wood",10);var buildings=s.Engine.Observe(new(1)).OwnBuildings;var source=buildings.Single(b=>b.Id.Value==9).InputId;var dest=buildings.Single(b=>b.Id.Value==10).InputId;
        StorageC1Tests.Put(s,source,"wood",10);var car=FleetE1Tests.Buy(s,"muravey70",2);ServiceF1Tests.Step(s,new(ActionKind.SetDeliveryService,VehicleId:car.Id,Enabled:true),2);
        var terms=new ServiceTerms(car.Id,source,dest,"wood",10);var q=ServiceF1Tests.Quote(s,terms);
        ServiceF1Tests.Step(s,new(ActionKind.RequestDeliveryService,VehicleId:car.Id,Source:source,Destination:dest,ItemId:"wood",Quantity:10,ServiceQuote:q.Token),accepted:false);
        Assert.Empty(s.Engine.InspectServices()!.Orders);Assert.Equal(0,StorageC1Tests.Inventory(s,source).Stock.Single().Reserved);Assert.Equal(100,s.Engine.Observe(new(1)).Account.AvailableMinor);ServiceF1Tests.Exact(s,"no-money");
    }
    [Fact]public void PaidClaimRightsAreExclusiveAndNpcCustomerHasTheSameCommands()
    {
        var d=ServiceF1Tests.Definition();d=d with{Buildings=[..d.Buildings!.Select(b=>b.Id==10?b with{OwnerId=2}:b),new(11,1,new(30,3),"shop"),new(12,2,new(31,3),"shop")],Plots=[..d.Plots!.Select(p=>p.Id==101||p.Id==91?p with{OwnerId=2}:p.Id==90?p with{OwnerId=1}:p)]};
        var s=new GameSession(new(d));StorageC1Tests.Harvest(s,"wood",10);var source=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==9).InputId;var dest=s.Engine.Observe(new(2)).OwnBuildings.Single(b=>b.Id.Value==10).InputId;
        StorageC1Tests.Put(s,source,"wood",10);var car=FleetE1Tests.Buy(s,"muravey70",1);FleetE1Tests.Board(s,car);StorageC1Tests.Move(s,new(7,4));ServiceF1Tests.Step(s,new(ActionKind.LeaveVehicle));ServiceF1Tests.Step(s,new(ActionKind.SetDeliveryService,VehicleId:car.Id,Enabled:true));
        StorageC1Tests.Move(s,new(7,4),2);
        ServiceF1Tests.Step(s,new(ActionKind.CreateSellOffer,Building:new(11),Source:source,ItemId:"wood",Quantity:10,UnitPriceMinor:100));var offer=s.Engine.Inspect().MarketOffers.Single();
        ServiceF1Tests.Step(s,new(ActionKind.SubmitBuyRequest,MarketOfferId:offer.Id,Quantity:10,MaximumPriceMinor:100),2);s.AdvanceTo(s.Engine.Tick+5);var claim=s.Engine.Inspect().MarketAllocations.Single();
        var terms=new ServiceTerms(car.Id,source,dest,"wood",10,claim.Id);var result=s.Engine.QuoteDeliveryService(new(2),terms);Assert.True(result.Quote is not null,result.Reason);var q=result.Quote;
        ServiceF1Tests.Step(s,new(ActionKind.RequestDeliveryService,VehicleId:car.Id,Source:source,Destination:dest,ItemId:"wood",Quantity:10,PurchaseId:claim.Id,ServiceQuote:q.Token),2);var order=s.Engine.InspectServices()!.Orders.Single();
        Assert.Equal(10,StorageC1Tests.Inventory(s,source).Stock.Single().Reserved);
        ServiceF1Tests.Step(s,new(ActionKind.CollectPurchase,PurchaseId:claim.Id,Quantity:10),2,false);
        ServiceF1Tests.Step(s,new(ActionKind.AcceptDeliveryService,ServiceId:order.Id));Assert.Equal("Accepted",s.Engine.InspectServices()!.Orders.Single().State);ServiceF1Tests.Exact(s,"paid-claim");
        ServiceF1Tests.Step(s,new(ActionKind.CancelDeliveryService,ServiceId:order.Id),2);Assert.Equal(10,StorageC1Tests.Inventory(s,source).Stock.Single().Reserved);ServiceF1Tests.Exact(s,"claim-refund");
    }

    [Fact]public void PausedResponseBarrierChangesOnlyOrderAndFundsAndReplaysAtExactTravelQuantum()
    {
        var(s,_,terms)=ServiceF1Tests.Prepare();var order=ServiceF1Tests.Request(s,terms);ServiceF1Tests.Step(s,new(ActionKind.SetDeliveryService,VehicleId:terms.VehicleId,Enabled:false),2);s.AdvanceTravelQuantum();s.AdvanceTravelQuantum();s.SetPaused(true);
        long tick=s.Engine.Tick;int quantum=s.Engine.TravelQuantum;long carrier=s.Engine.Observe(new(2)).Account.BalanceMinor;var flows=s.Engine.Inspect().Flows;
        string view=s.Send(new(2),new(ActionKind.ViewDeliveryService,ServiceId:order.Id));Assert.Equal(ResultStatus.Accepted,s.Engine.Result(view)!.Status);
        string accepted=s.Send(new(2),new(ActionKind.AcceptDeliveryService,ServiceId:order.Id));Assert.Equal(ResultStatus.Accepted,s.Engine.Result(accepted)!.Status);
        Assert.Equal(tick,s.Engine.Tick);Assert.Equal(quantum,s.Engine.TravelQuantum);Assert.Equal(flows,s.Engine.Inspect().Flows);Assert.Equal(carrier,s.Engine.Observe(new(2)).Account.BalanceMinor);
        Assert.Equal("Accepted",s.Engine.InspectServices()!.Orders.Single().State);Assert.Equal(s.Engine.Observe(new(1)).Account.BalanceMinor,s.Samples.Last().ParticipantCashMinor);var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        var command=s.CommandTape.Single(c=>c.Command.CommandId==accepted).Command;string hash=copy.Engine.ComputeStateHash();copy.Engine.Enqueue(new(2),command);Assert.Equal(hash,copy.Engine.ComputeStateHash());
        string retry=s.Send(new(2),new(ActionKind.AcceptDeliveryService,ServiceId:order.Id));Assert.Equal(ResultStatus.Rejected,s.Engine.Result(retry)!.Status);Assert.Single(s.Engine.Ledger(),l=>l.Reason=="ServiceFunding:"+order.Id);
        for(int n=0;n<4;n++){string seen=s.Send(new(2),new(ActionKind.ViewDeliveryService,ServiceId:order.Id));Assert.Equal(ResultStatus.Accepted,s.Engine.Result(seen)!.Status);}
        string excess=s.Send(new(2),new(ActionKind.ViewDeliveryService,ServiceId:order.Id));Assert.Equal("CommandBudgetExceeded",s.Engine.Result(excess)!.ReasonCode);
        string cancel=s.Send(new(1),new(ActionKind.CancelDeliveryService,ServiceId:order.Id));Assert.Equal(ResultStatus.Accepted,s.Engine.Result(cancel)!.Status);Assert.Equal(tick,s.Engine.Tick);Assert.Equal(quantum,s.Engine.TravelQuantum);
        s.SetPaused(false);ServiceF1Tests.Exact(s,"paused-response");
    }

}
