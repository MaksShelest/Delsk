using Simulation.Core;
using Simulation.Hosting;
using Xunit;
using static Simulation.Tests.ServiceF2Tests;
namespace Simulation.Tests;
public class ServiceGoodsF2Tests
{
    private static (GameSession Session,OwnedVehicle Vehicle,ServiceOrder Order,DeliveryContractView Contract) Contract(bool capital)
    {
        var d=Definition();d=d with{CapitalDemand=d.CapitalDemand! with{OrderTtlTicks=10000}};if(!capital)d=d with{Buildings=[..d.Buildings!.Select(b=>b.Id==10?b with{OwnerId=2}:b),new(11,1,new(30,3),"shop"),new(12,2,new(31,3),"shop")],Plots=[..d.Plots!.Select(p=>p.Id==101||p.Id==91?p with{OwnerId=2}:p.Id==90?p with{OwnerId=1}:p)]};
        var(s,v,destination)=ServicePointsF2Tests.Empty(d);StorageC1Tests.Harvest(s,"wood",10);var source=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==9).InputId;StorageC1Tests.Put(s,source,"wood",10);
        if(capital){var offer=s.Engine.Observe(new(1)).CapitalOffers.First(o=>o.ItemId=="wood"&&o.RemainingQuantity>=10);Step(s,new(ActionKind.AcceptDeliveryQuantity,OfferId:offer.Id,Quantity:10),1);destination=new(0);}
        else {destination=s.Engine.Observe(new(2)).OwnBuildings.Single(b=>b.Id.Value==10).InputId;Step(s,new(ActionKind.CreateBuyOffer,Destination:destination,ItemId:"wood",Quantity:10,UnitPriceMinor:100));var offer=s.Engine.Observe(new(2)).BuyOffers.Single();Step(s,new(ActionKind.AcceptDeliveryQuantity,BuyOfferId:offer.Id,Quantity:10),1);}
        s.AdvanceTo(s.Engine.Tick+6);var contract=s.Engine.Inspect().DeliveryContracts.Single();var o=ServiceF1Tests.Request(s,new(v.Id,source,destination,"wood",10,ContractId:contract.Id));Step(s,new(ActionKind.AcceptDeliveryService,ServiceId:o.Id));return(s,v,s.Engine.InspectServices()!.Orders.Single(),contract);
    }
    private static (GameSession Session,OwnedVehicle Vehicle,ServiceOrder Order) Paid(long quantity)
    {
        var d=Definition();d=d with{Buildings=[..d.Buildings!.Select(b=>b.Id==9?b with{OwnerId=2}:b),new(11,2,new(30,3),"shop"),new(12,1,new(31,3),"shop")],Plots=[..d.Plots!.Select(p=>p.Id==100||p.Id==90?p with{OwnerId=2}:p.Id==91?p with{OwnerId=1}:p)]};
        var(s,v,destination)=ServicePointsF2Tests.Empty(d);StorageC1Tests.Harvest(s,"wood",quantity,2);var source=s.Engine.Observe(new(2)).OwnBuildings.Single(b=>b.Id.Value==9).InputId;StorageC1Tests.Put(s,source,"wood",quantity,2);
        Step(s,new(ActionKind.CreateSellOffer,Building:new(11),Source:source,ItemId:"wood",Quantity:quantity,UnitPriceMinor:100));var offer=s.Engine.Inspect().MarketOffers.Single();
        Step(s,new(ActionKind.SubmitBuyRequest,MarketOfferId:offer.Id,Quantity:quantity,MaximumPriceMinor:100),1);s.AdvanceTo(s.Engine.Tick+5);var claim=s.Engine.Inspect().MarketAllocations.Single();
        var o=ServiceF1Tests.Request(s,new(v.Id,source,destination,"wood",quantity,PurchaseId:claim.Id));Step(s,new(ActionKind.AcceptDeliveryService,ServiceId:o.Id));return(s,v,s.Engine.InspectServices()!.Orders.Single());
    }
    [Fact]public void CancellationOfPaidForeignCargoReturnsToAgreedCustomerStorageWithoutReverseSale()
    {
        var(s,v,o)=Paid(10);Board(s,v);Load(s,o.Id);Assert.Equal(1000,s.Engine.Ledger().Where(l=>l.Reason=="PickupPayment").Sum(l=>l.AmountMinor));
        Step(s,new(ActionKind.CancelDeliveryService,ServiceId:o.Id),1);Deliver(s,o.Id);Assert.Equal(10,Run(s).Returned);Assert.Equal(0,Run(s).PaidMinor);Assert.Equal(0,FleetE1Tests.Stock(s,o.Quote.Terms.Source,"wood"));Assert.Equal(10,FleetE1Tests.Stock(s,Run(s).ReturnInventory,"wood"));Assert.Equal(o.Quote.FeeMinor,Run(s).RefundedMinor);Exact(s,"paid-return");
    }
    [Fact]public void ExpiringUnloadedPurchaseRightsKeepCurrentCargoForActualReceipt()
    {
        var(s,v,o)=Paid(50);Board(s,v);Load(s,o.Id);Assert.Equal(35,Run(s).Cargo);long due=s.Engine.Inspect().MarketAllocations.Single().DeadlineTick;s.AdvanceTo(due-1);Exact(s,"pickup-expiring-loaded");
        Assert.Equal(35,Run(s).Cargo);Assert.Equal(15,Run(s).Released);Deliver(s,o.Id);Assert.Equal(35,Run(s).Delivered);Assert.Equal("Cancelled",Run(s).Stage);Assert.Equal(3500,s.Engine.Ledger().Where(l=>l.Reason=="PickupPayment").Sum(l=>l.AmountMinor));Assert.Equal(o.Quote.FeeMinor,Run(s).PaidMinor+Run(s).RefundedMinor);Exact(s,"pickup-expired-delivered");
    }
    [Theory][InlineData(false)][InlineData(true)]public void GoodsPaymentAndServiceFeeHaveSeparateRecipientsAndActualReceipts(bool capital)
    {
        var(s,v,o,contract)=Contract(capital);Board(s,v);Load(s,o.Id);Assert.Equal(10,s.Engine.Inspect().DeliveryContracts.Single().InTransitQuantity);
        Deliver(s,o.Id);var d=s.Engine.Inspect().DeliveryContracts.Single();Assert.Equal(10,d.DeliveredQuantity);Assert.Equal("Fulfilled",d.Status);Assert.Equal(o.Quote.FeeMinor,Run(s).PaidMinor);
        Assert.Equal(10*contract.UnitPriceMinor,s.Engine.Ledger().Where(l=>l.Reason=="DeliveryPayment").Sum(l=>l.AmountMinor));Assert.Equal(o.Quote.FeeMinor,s.Engine.Ledger().Where(l=>l.Reason=="ServicePayment:"+o.Id).Sum(l=>l.AmountMinor));Exact(s,capital?"capital-delivered":"foreign-delivered");
    }
    [Fact]public void GoodsContractExpiryReturnsPhysicalCargoWithoutUsingServiceFeeAsGoodsPayment()
    {
        var(s,v,o,contract)=Contract(false);Board(s,v);Load(s,o.Id);s.AdvanceTo(contract.DeadlineTick-1);Exact(s,"goods-expiring-loaded");
        Assert.Equal("Expired",s.Engine.Inspect().DeliveryContracts.Single().Status);Assert.Equal(10,Run(s).Cargo);Assert.Equal("Returning",Run(s).Stage);Assert.Equal(0,Run(s).PaidMinor);Deliver(s,o.Id);Assert.Equal(10,Run(s).Returned);Assert.Equal(10,FleetE1Tests.Stock(s,o.Quote.Terms.Source,"wood"));Assert.Equal(o.Quote.FeeMinor,Run(s).RefundedMinor);Exact(s,"goods-expired-return");
    }
}
