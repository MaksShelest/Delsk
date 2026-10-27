using Simulation.Core;
using Simulation.Hosting;
using Xunit;
using static Simulation.Tests.BillingE3Tests;

namespace Simulation.Tests;

public class BillingPenaltyE3Tests
{
    [Fact]public void GraceIsInclusiveFirstPenaltyAt1001Then1000NoCompoundingAndPartialPrincipalFirst()
    {
        var s=new GameSession(new(Prepared()));var invoice=Invoice(s);long due=invoice.DueTick;
        s.AdvanceTo(due);Assert.Equal(0,s.Engine.InspectBilling()!.Invoices.Single(i=>i.Id==invoice.Id).PenaltyMinor);Exact(s,"before-penalty");
        // Exact advances three ticks, therefore first penalty must already have appeared once.
        var first=s.Engine.InspectBilling()!.Invoices.Single(i=>i.Id==invoice.Id);Assert.Equal(invoice.PrincipalMinor/5,first.PenaltyMinor);
        long principal=first.PrincipalRemaining;Step(s,new(ActionKind.PayInvoice,OfferId:invoice.Id,Quantity:3));
        var partial=s.Engine.InspectBilling()!.Invoices.Single(i=>i.Id==invoice.Id);Assert.Equal(principal-3,partial.PrincipalRemaining);Assert.Equal(first.PenaltyMinor,partial.PenaltyRemaining);
        s.AdvanceTo(due+1000);Assert.Equal(first.PenaltyMinor,s.Engine.InspectBilling()!.Invoices.Single(i=>i.Id==invoice.Id).PenaltyMinor);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        var second=s.Engine.InspectBilling()!.Invoices.Single(i=>i.Id==invoice.Id);Assert.Equal((invoice.PrincipalMinor*2000+(principal-3)*2000)/10000,second.PenaltyMinor);
        Step(s,new(ActionKind.PayInvoice,OfferId:invoice.Id,Quantity:second.PrincipalRemaining));
        var principalPaid=s.Engine.InspectBilling()!.Invoices.Single(i=>i.Id==invoice.Id);Assert.Equal(0,principalPaid.PrincipalRemaining);Assert.True(principalPaid.PenaltyRemaining>0);
        s.AdvanceTo(due+2001);Assert.Equal(second.PenaltyMinor,s.Engine.InspectBilling()!.Invoices.Single(i=>i.Id==invoice.Id).PenaltyMinor);Exact(s,"simple-penalties");
    }
}
