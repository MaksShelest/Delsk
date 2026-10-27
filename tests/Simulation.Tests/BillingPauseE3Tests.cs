using Simulation.Core;
using Simulation.Hosting;
using Xunit;
using static Simulation.Tests.BillingE3Tests;

namespace Simulation.Tests;

public class BillingPauseE3Tests
{
    [Fact]public void PauseAndLoadingBeforePenaltyCannotAdvanceDeadlineOrDuplicatePayment()
    {
        var s=new GameSession(new(Prepared()));var invoice=Invoice(s);s.AdvanceTo(invoice.DueTick);s.SetPaused(true);string hash=s.Engine.ComputeStateHash();
        for(int i=0;i<100;i++){new TickScheduler(s).AdvanceElapsed(TimeSpan.FromSeconds(1));s.AdvanceTravelQuantum();}Assert.Equal(hash,s.Engine.ComputeStateHash());
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));copy.SetPaused(false);s.SetPaused(false);s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        Step(s,new(ActionKind.PayInvoice,OfferId:invoice.Id));Exact(s,"pause-load");
    }
}
