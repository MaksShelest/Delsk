using Simulation.Core;
using Simulation.Hosting;
using Xunit;
using static Simulation.Tests.BillingE3Tests;

namespace Simulation.Tests;

public class BillingBoundaryE3Tests
{
    [Theory][InlineData(1000)][InlineData(1001)]public void PaymentOnGraceOrPenaltyTickRunsBeforeInterest(int after)
    {
        var s=new GameSession(new(Prepared()));var invoice=Invoice(s);s.AdvanceTo(invoice.CreatedTick+after-1);
        Step(s,new(ActionKind.PayInvoice,OfferId:invoice.Id));var paid=s.Engine.InspectBilling()!.Invoices.Single(i=>i.Id==invoice.Id);
        Assert.Equal(0,paid.Remaining);Assert.Equal(0,paid.PenaltyMinor);Exact(s,"boundary-"+after);
    }
}
