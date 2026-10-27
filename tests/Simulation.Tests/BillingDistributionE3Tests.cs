using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class BillingDistributionE3Tests
{
    private static long ClaimUnits(GameSession s,long payer,long recipient)=>s.Engine.InspectBilling()!.Accruals.Where(a=>a.Payer.Value==payer&&a.Recipient.Value==recipient).Sum(a=>a.Units)+
        s.Engine.InspectBilling()!.Invoices.Where(i=>i.Payer.Value==payer).SelectMany(i=>i.Claims).Where(c=>c.Recipient.Value==recipient).Sum(c=>c.PrincipalMinor)*SimulationEngine.RoadAccrualScale;
    private static void Pave(GameSession s,OwnedVehicle paver,int level)
    {
        foreach(var cell in new[]{new Cell(10,1),new Cell(11,1)})
        {StorageC1Tests.Move(s,cell);BillingE3Tests.Step(s,RoadE2Tests.Work(paver,level,target:cell));s.AdvanceTo(s.Engine.Tick+2*(level==2?12:16));}
    }
    [Fact]public void HundredHighwayCellsOnVectorCostSixCoinsAndPayCity240Builder360OnlyAfterPayment()
    {
        var s=new GameSession(new(BillingE3Tests.Prepared()));BillingE3Tests.Step(s,new(ActionKind.SetAutoPay,Enabled:false));BillingE3Tests.Step(s,new(ActionKind.SetAutoPay,Enabled:false),2);
        var paver=RoadE2Tests.Paver(s);Pave(s,paver,2);Pave(s,paver,3);StorageC1Tests.Move(s,new(14,2));BillingE3Tests.Step(s,new(ActionKind.LeaveVehicle));
        BillingE3Tests.Car(s,"vector200",2);StorageC1Tests.Move(s,new(10,1),2);
        long city=ClaimUnits(s,2,0),builder=ClaimUnits(s,2,1),builderBalance=s.Engine.Observe(new(1)).Account.BalanceMinor;
        for(int n=0;n<50;n++){StorageC1Tests.Move(s,new(11,1),2);StorageC1Tests.Move(s,new(10,1),2);}
        Assert.Equal(240L*SimulationEngine.RoadAccrualScale,ClaimUnits(s,2,0)-city);Assert.Equal(360L*SimulationEngine.RoadAccrualScale,ClaimUnits(s,2,1)-builder);
        Assert.Equal(builderBalance,s.Engine.Observe(new(1)).Account.BalanceMinor);s.AdvanceTo((s.Engine.Tick/120+1)*120);
        var invoices=s.Engine.InspectBilling()!.Invoices.Where(i=>i.Payer.Value==2).ToArray();long expected=invoices.Sum(i=>i.Claims.Where(c=>c.Recipient.Value==1).Sum(c=>c.PrincipalMinor));
        foreach(var invoice in invoices)
        {
            // Tiny repeated payments test the shared rounding residual and eventual exact payout.
            for(int n=0;n<3&&s.Engine.InspectBilling()!.Invoices.Single(i=>i.Id==invoice.Id).Remaining>0;n++)BillingE3Tests.Step(s,new(ActionKind.PayInvoice,OfferId:invoice.Id,Quantity:1),2);
            if(s.Engine.InspectBilling()!.Invoices.Single(i=>i.Id==invoice.Id).Remaining>0)BillingE3Tests.Step(s,new(ActionKind.PayInvoice,OfferId:invoice.Id,Quantity:long.MaxValue),2);
        }
        Assert.Equal(builderBalance+expected,s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.All(s.Engine.InspectBilling()!.Invoices.Where(i=>i.Payer.Value==2),i=>Assert.All(i.Claims,c=>Assert.Equal(c.PrincipalMinor,c.PaidMinor)));
        BillingE3Tests.Exact(s,"highway100");
    }
    [Fact]public void UpgradeDoesNotRewriteExistingInvoiceOrUnbilledRecipientRights()
    {
        var s=new GameSession(new(BillingE3Tests.Prepared()));BillingE3Tests.Step(s,new(ActionKind.SetAutoPay,Enabled:false));BillingE3Tests.Step(s,new(ActionKind.SetAutoPay,Enabled:false),2);
        var paver=RoadE2Tests.Paver(s);Pave(s,paver,2);StorageC1Tests.Move(s,new(14,2));BillingE3Tests.Step(s,new(ActionKind.LeaveVehicle));
        BillingE3Tests.Car(s,"vector200",2);StorageC1Tests.Move(s,new(10,1),2);StorageC1Tests.Move(s,new(12,1),2);BillingE3Tests.Step(s,new(ActionKind.LeaveVehicle),2);
        s.AdvanceTo((s.Engine.Tick/120+1)*120);var old=s.Engine.InspectBilling()!.Invoices.Where(i=>i.Payer.Value==2).Select(i=>System.Text.Json.JsonSerializer.Serialize(i,SimulationJson.Options)).ToArray();
        long city=ClaimUnits(s,2,0),builder=ClaimUnits(s,2,1);FleetE1Tests.Board(s,paver);Pave(s,paver,3);
        Assert.Equal(old,s.Engine.InspectBilling()!.Invoices.Where(i=>i.Payer.Value==2).Select(i=>System.Text.Json.JsonSerializer.Serialize(i,SimulationJson.Options)));
        Assert.Equal(city,ClaimUnits(s,2,0));Assert.Equal(builder,ClaimUnits(s,2,1));BillingE3Tests.Exact(s,"frozen-rights");
    }
    [Fact]public void AutoPayUsesOnlyFreeMoneyPaysPartiallyAndPaysAfterReservedFundsAreReleased()
    {
        var config=BillingE3Tests.Prepared();config=config with{Buildings=[..config.Buildings!,new(9,1,new(30,3),"shop")],Plots=config.Plots!.Select(p=>p.Id==90?p with{OwnerId=1}:p).ToArray()};
        var s=new GameSession(new(config));var invoice=BillingE3Tests.Invoice(s);long balance=s.Engine.Observe(new(1)).Account.AvailableMinor;
        var shop=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Id.Value==9);
        BillingE3Tests.Step(s,new(ActionKind.CreateBuyOffer,ItemId:"wood",Quantity:1,UnitPriceMinor:balance-3,Destination:shop.InputId));
        long reserved=s.Engine.Observe(new(1)).Account.ReservedMinor;Assert.Equal(balance-3,reserved);
        BillingE3Tests.Step(s,new(ActionKind.SetAutoPay,Enabled:true));var partial=s.Engine.InspectBilling()!.Invoices.Single(i=>i.Id==invoice.Id);
        Assert.Equal(invoice.PrincipalMinor-3,partial.PrincipalRemaining);Assert.Equal(reserved,s.Engine.Observe(new(1)).Account.ReservedMinor);Assert.Equal(0,s.Engine.Observe(new(1)).Account.AvailableMinor);
        var offer=Assert.Single(s.Engine.Observe(new(1)).BuyOffers);BillingE3Tests.Step(s,new(ActionKind.CancelBuyOffer,BuyOfferId:offer.Id));
        Assert.Equal(0,s.Engine.InspectBilling()!.Invoices.Single(i=>i.Id==invoice.Id).Remaining);Assert.Equal(0,s.Engine.Observe(new(1)).Account.ReservedMinor);BillingE3Tests.Exact(s,"auto-holds");
    }
    [Fact]public void DebtDoesNotBlockManualHarvestAndRemotePaymentWhileMoving()
    {
        var s=new GameSession(new(BillingE3Tests.Prepared()));var invoice=BillingE3Tests.Invoice(s);BillingE3Tests.Step(s,new(ActionKind.LeaveVehicle));
        long before=s.Engine.Inspect().Flows.Single(f=>f.ItemId=="wood").Harvested;StorageC1Tests.Harvest(s,"wood",1);
        Assert.Equal(before+1,s.Engine.Inspect().Flows.Single(f=>f.ItemId=="wood").Harvested);
        string move=s.Send(new(1),new(ActionKind.MoveTo,Target:new(20,1)));s.Step();Assert.Equal(ResultStatus.Accepted,s.Engine.Result(move)!.Status);Assert.True(s.Engine.Observe(new(1)).Participant.Busy);
        BillingE3Tests.Step(s,new(ActionKind.PayInvoice,OfferId:invoice.Id,Quantity:1));Assert.Equal(invoice.PrincipalMinor-1,s.Engine.InspectBilling()!.Invoices.Single(i=>i.Id==invoice.Id).PrincipalRemaining);BillingE3Tests.Exact(s,"working-debtor");
    }
}
