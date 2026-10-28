using Simulation.Core;
using Simulation.Hosting;
using Xunit;
using static Simulation.Tests.ServiceF2Tests;
namespace Simulation.Tests;
public class ServiceSettlementF2Tests
{
    [Fact]public void TimelyAndLatePartsAreSettledOnceWithOnlyLateTwentyPercent()
    {
        var(s,v,o)=Prepare();Board(s,v);Load(s,o.Id);Navigate(s,o.Id);
        Step(s,new(ActionKind.UnloadDeliveryService,ServiceId:o.Id));s.Step();Step(s,new(ActionKind.StopDeliveryServiceHandling,ServiceId:o.Id));
        var timely=Run(s);Assert.InRange(timely.Delivered,1,29);Assert.Equal(0,timely.LateRefundMinor);long timelyPaid=timely.PaidMinor;
        s.AdvanceTo(o.DeadlineTick);Exact(s,"late-before-unload");Assert.Equal(timelyPaid,Run(s).PaidMinor);
        Step(s,new(ActionKind.UnloadDeliveryService,ServiceId:o.Id));Until(s,()=>Run(s).Stage=="Completed");
        var r=Run(s);long lateGross=o.Quote.FeeMinor-timely.GrossSettledMinor;Assert.Equal(lateGross*2000/10000,r.LateRefundMinor);Assert.Equal(o.Quote.FeeMinor-r.LateRefundMinor,r.PaidMinor);Assert.Equal(r.LateRefundMinor,r.RefundedMinor);
        Assert.Equal(timelyPaid,r.Receipts.Where(p=>p.Tick<=o.DeadlineTick).Sum(p=>p.PaidMinor));Exact(s,"late-parts");
    }
    [Theory][InlineData(false)][InlineData(true)]public void CancelAfterLoadingPreservesCurrentCargoAndRefundsOnlyUnusedFee(bool finish)
    {
        var(s,v,o)=Prepare(50);Board(s,v);Load(s,o.Id);long carried=Run(s).Cargo;Assert.Equal(35,carried);
        Step(s,new(ActionKind.SetDeliveryService,VehicleId:v.Id,Enabled:false));Assert.Equal("Accepted",s.Engine.InspectServices()!.Orders.Single().State);
        Step(s,new(ActionKind.CancelDeliveryService,ServiceId:o.Id,Enabled:finish),1);Assert.Equal(carried,Run(s).Cargo);Assert.Equal(15,Run(s).Released);
        Exact(s,finish?"cancel-finish-loaded":"cancel-return-loaded");Deliver(s,o.Id);var r=Run(s);Assert.Equal("Cancelled",r.Stage);
        Assert.Equal(finish?35:0,r.Delivered);Assert.Equal(finish?0:35,r.Returned);Assert.Equal(0,r.Cargo);Assert.Equal(o.Quote.FeeMinor,r.PaidMinor+r.RefundedMinor);
        Assert.Equal(finish?15:50,FleetE1Tests.Stock(s,o.Quote.Terms.Source,"wood"));Assert.Equal(finish?35:0,FleetE1Tests.Stock(s,o.Quote.Terms.Destination,"wood"));Exact(s,finish?"cancel-finish":"cancel-return");
    }
    [Fact]public void CustomerForceClosureHasExactBoundaryAndPauseDoesNotAdvanceDeadline()
    {
        var(s,v,o)=Prepare();Board(s,v);Load(s,o.Id);s.AdvanceTo(o.DeadlineTick+1200);s.SetPaused(true);long tick=s.Engine.Tick;
        for(int n=0;n<20;n++)s.Step();Assert.Equal(tick,s.Engine.Tick);
        var id=s.Send(new(1),new(ActionKind.ForceCloseDeliveryService,ServiceId:o.Id));Assert.Equal(ResultStatus.Rejected,s.Engine.Result(id)!.Status);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));copy.SetPaused(false);s.SetPaused(false);s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        Step(s,new(ActionKind.ForceCloseDeliveryService,ServiceId:o.Id),1);Assert.Equal(30,Run(s).Cargo);Deliver(s,o.Id);Assert.Equal(30,Run(s).Returned);Assert.Equal(o.Quote.FeeMinor,Run(s).RefundedMinor);Exact(s,"force-close");
    }
    [Fact]public void CancelDuringPartialLoadingReturnsOnlyRealUnitsAndReleasesRemainder()
    {
        var(s,v,o)=Prepare();Board(s,v);Navigate(s,o.Id);Step(s,new(ActionKind.PickupDeliveryService,ServiceId:o.Id));s.Step();long aboard=Run(s).Cargo;Assert.InRange(aboard,1,29);
        Step(s,new(ActionKind.CancelDeliveryService,ServiceId:o.Id),1);Assert.True(Run(s).Cargo>=aboard);Deliver(s,o.Id);Assert.Equal(30,FleetE1Tests.Stock(s,o.Quote.Terms.Source,"wood"));Assert.Equal(o.Quote.FeeMinor,Run(s).RefundedMinor);Exact(s,"cancel-partial-load");
    }
}
