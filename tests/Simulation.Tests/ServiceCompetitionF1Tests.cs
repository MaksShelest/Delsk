using Simulation.Core;
using Simulation.Hosting;
using Xunit;
namespace Simulation.Tests;
public class ServiceCompetitionF1Tests
{
    [Fact]public void TwoDifferentCustomersCompeteForOneVehicleWithoutDoubleGoodsOrMoneyHolds()
    {
        var d=ServiceF1Tests.Definition();d=d with{Participants=[..d.Participants,new(3,1000000,d.Participants[1].Position,2000)],Market=new(PickupTtlTicks:1200),
            Buildings=[..d.Buildings!,new(11,1,new(30,3),"shop"),new(12,3,new(31,3),"shop")],Plots=[..d.Plots!.Select(p=>p.Id==90?p with{OwnerId=1}:p.Id==91?p with{OwnerId=3}:p),new(92,new(32,3),10000,IsShopSite:true)],Layout=d.Layout! with{MarketStalls=[..d.Layout!.MarketStalls,new("stall3",new(32,3))]}};
        var s=new GameSession(new(d));StorageC1Tests.Harvest(s,"wood",20);var source=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==9).InputId;StorageC1Tests.Put(s,source,"wood",20);
        var car=FleetE1Tests.Buy(s,"muravey70",2);ServiceF1Tests.Step(s,new(ActionKind.SetDeliveryService,VehicleId:car.Id,Enabled:true),2);
        ServiceF1Tests.Step(s,new(ActionKind.CreateSellOffer,Building:new(11),Source:source,ItemId:"wood",Quantity:10,UnitPriceMinor:100));var offer=s.Engine.Inspect().MarketOffers.Single();
        ServiceF1Tests.Step(s,new(ActionKind.SubmitBuyRequest,MarketOfferId:offer.Id,Quantity:10,MaximumPriceMinor:100),3);s.AdvanceTo(s.Engine.Tick+5);var claim=s.Engine.Inspect().MarketAllocations.Single();
        var a=new ServiceTerms(car.Id,source,s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==10).InputId,"wood",10);
        var b=new ServiceTerms(car.Id,source,s.Engine.Observe(new(3)).OwnBuildings.Single().InputId,"wood",10,claim.Id);
        string Send(long actor,ServiceTerms t){var q=s.Engine.QuoteDeliveryService(new(actor),t);Assert.True(q.Quote is not null,q.Reason);return s.Send(new(actor),new(ActionKind.RequestDeliveryService,VehicleId:car.Id,Source:t.Source,Destination:t.Destination,ItemId:t.ItemId,Quantity:t.Quantity,PurchaseId:t.PurchaseId,ServiceQuote:q.Quote.Token));}
        string first=Send(1,a),second=Send(3,b);s.Step();var order=Assert.Single(s.Engine.InspectServices()!.Orders);
        Assert.Equal(1,new[]{s.Engine.Result(first)!,s.Engine.Result(second)!}.Count(r=>r.Status==ResultStatus.Accepted));
        long held=s.Engine.Observe(new(1)).Account.ReservedMinor+s.Engine.Observe(new(3)).Account.ReservedMinor;Assert.Equal(order.Quote.FeeMinor,held);
        Assert.Equal(order.Customer.Value==1?20:10,StorageC1Tests.Inventory(s,source).Stock.Single().Reserved);ServiceF1Tests.Exact(s,"two-customers");
    }
}
