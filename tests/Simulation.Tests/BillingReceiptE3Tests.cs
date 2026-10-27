using Simulation.Core;
using Simulation.Hosting;
using Xunit;
using static Simulation.Tests.BillingE3Tests;

namespace Simulation.Tests;

public class BillingReceiptE3Tests
{
    [Fact]public void FundedEscrowIsProtectedAndActualNewSaleIncomePaysOldDebt()
    {
        var config=Prepared();config=config with{Buildings=[..config.Buildings!,new(9,1,new(30,3),"shop"),new(10,2,new(31,3),"shop")],
            Plots=config.Plots!.Select(p=>p.Id is 90 or 91?p with{OwnerId=p.Id==90?1:2}:p).ToArray()};
        var s=new GameSession(new(config));var invoice=Invoice(s);var shop=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==9);
        long price=s.Engine.Observe(new(1)).Account.AvailableMinor-3;Step(s,new(ActionKind.CreateBuyOffer,ItemId:"wood",Quantity:1,UnitPriceMinor:price,Destination:shop.InputId));
        var offer=Assert.Single(s.Engine.Observe(new(1)).BuyOffers);Step(s,new(ActionKind.AcceptDeliveryQuantity,BuyOfferId:offer.Id,Quantity:1),2);s.AdvanceTo(s.Engine.Tick+5);
        var delivery=Assert.Single(s.Engine.Observe(new(1)).OwnDeliveryContracts);long escrow=s.Engine.Inspect().Accounts.Single(a=>a.Id==delivery.EscrowAccount).BalanceMinor;Assert.Equal(price,escrow);
        Step(s,new(ActionKind.SetAutoPay,Enabled:true));Assert.Equal(invoice.PrincipalMinor-3,s.Engine.InspectBilling()!.Invoices.Single(i=>i.Id==invoice.Id).PrincipalRemaining);
        Assert.Equal(0,s.Engine.Observe(new(1)).Account.AvailableMinor);Assert.Equal(escrow,s.Engine.AccountOf(new(1)).EscrowMinor);
        Step(s,new(ActionKind.LeaveVehicle));var capital=s.Engine.Observe(new(1)).CapitalOffers.First(o=>o.RemainingQuantity>0);
        StorageC1Tests.Harvest(s,capital.ItemId,1);StorageC1Tests.Move(s,capital.Position);
        Step(s,new(ActionKind.SellAvailableToCapital,OfferId:capital.Id,ItemId:capital.ItemId,Quantity:1));s.AdvanceTo(s.Engine.Tick+5);
        Assert.Equal(0,s.Engine.InspectBilling()!.Invoices.Single(i=>i.Id==invoice.Id).Remaining);Assert.Equal(escrow,s.Engine.Inspect().Accounts.Single(a=>a.Id==delivery.EscrowAccount).BalanceMinor);
        Assert.Contains(s.Engine.AccountOf(new(1)).Operations,o=>o.Kind=="CapitalSettlement"&&o.AmountMinor>0);Exact(s,"auto-receipt-escrow");
    }
}
