using Simulation.Core;
using Simulation.Hosting;
using Xunit;
namespace Simulation.Tests;
public class H2DirectionalEtaTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public void ReverseStartIncludesTheMandatoryLegalTurnInForecastAndSavedJob(bool noSideBay)
    {
        var d=RoadE2Tests.Prepared();d=d with{Economy=d.Economy! with{Movement=new()}};
        if(noSideBay)d=d with{Layout=d.Layout! with{RoadCorridorCells=d.Layout.RoadCenterCells}};
        var s=new GameSession(new(d));var car=BillingE3Tests.Car(s);var o=s.Engine.ObserveController(new(1),true);
        Assert.Equal(-1,s.Engine.InspectFleet()!.Vehicles.Single().FacingX);long eta=MovementTiming.PersonalTicks(o,new(6,1),new(16,1));
        var preview=s.Engine.PreviewMovement(new(1),new(16,1));Assert.True(preview.Allowed);Assert.True(eta>16);
        string command=s.Send(new(1),new(ActionKind.MoveTo,Target:new(16,1)));s.Step();long start=s.Engine.Tick;
        Assert.Equal(ResultStatus.Accepted,s.Engine.Result(command)!.Status);Assert.Equal(start+eta,s.Engine.Observe(new(1)).Participant.ArrivalTick);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));int guard=200;
        while(s.Engine.Observe(new(1)).Participant.Busy&&guard-->0){s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());}
        Assert.True(guard>0);Assert.Equal(eta,s.Engine.Tick-start);RoadE2Tests.Exact(s,"h2-direction-"+noSideBay);
        H2MovementTests.Evidence(new{kind="mandatory-turn",noSideBay,eta,actual=s.Engine.Tick-start-3,path=preview.Path.ToArray()});
    }
    [Fact]public void DeliveryQuoteIncludesTheInitialRestrictedTurnAndRetainsExactReceipt()
    {
        var d=ServiceF2Tests.Definition();d=d with{Economy=d.Economy! with{Movement=new()},Layout=d.Layout! with{TurnCells=[new(4,1),new(12,1),new(20,1),new(28,1)]}};
        var s=new GameSession(new(d));StorageC1Tests.Harvest(s,"wood",30);var source=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==9).InputId;var dest=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==10).InputId;StorageC1Tests.Put(s,source,"wood",30);
        var car=BillingE3Tests.Car(s,"muravey70",2);ServiceF2Tests.Step(s,new(ActionKind.LeaveVehicle));ServiceF2Tests.Step(s,new(ActionKind.SetDeliveryService,VehicleId:car.Id,Enabled:true));
        var order=ServiceF1Tests.Request(s,new(car.Id,source,dest,"wood",30));ServiceF2Tests.Step(s,new(ActionKind.AcceptDeliveryService,ServiceId:order.Id));ServiceF2Tests.Step(s,new(ActionKind.SetServiceAutomation,VehicleId:car.Id,Enabled:true));
        var quote=s.Engine.InspectServices()!.Orders.Single().Quote;long start=s.Engine.Tick;ServiceF2Tests.Until(s,()=>s.Engine.InspectServices()!.Orders.Single().State=="Completed");
        H2MovementTests.Evidence(new{kind="service-mandatory-turn",eta=quote.EtaTicks,actual=s.Engine.Tick-start,quote.TravelTicks,quote.HandlingTicks});
        Assert.Equal(quote.EtaTicks,s.Engine.Tick-start);Assert.Equal(30,ServiceF2Tests.Run(s).Delivered);ServiceF2Tests.Exact(s,"h2-service-direction");
    }
}
