using Simulation.Core;
using Simulation.Hosting;
using Xunit;
namespace Simulation.Tests;
public class H2ServiceMovementTests
{
    [Theory][InlineData(30)][InlineData(150)]
    public void RealAutomaticDeliveryETAIncludesAllLegsHandlingAndStageBoundaries(long quantity)
    {
        var(s,v,o)=ServiceF2Tests.Prepare(quantity,fast:true);ServiceF2Tests.Step(s,new(ActionKind.SetServiceAutomation,VehicleId:v.Id,Enabled:true));
        Assert.Equal(1,s.Engine.InspectServices()!.Execution!.Crews.Single().Group.Headcount);
        long start=s.Engine.Tick;var quote=s.Engine.InspectServices()!.Orders.Single().Quote;
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));int guard=2000;
        while(s.Engine.InspectServices()!.Orders.Single().State!="Completed"&&guard-->0)
        {s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());}
        Assert.True(guard>0);long elapsed=s.Engine.Tick-start;
        H2MovementTests.Evidence(new{kind="service",quantity,trips=quote.Trips,quote.TravelTicks,quote.HandlingTicks,eta=quote.EtaTicks,actual=elapsed});
        Assert.Equal(quote.EtaTicks,elapsed);Assert.Equal(quantity,ServiceF2Tests.Run(s).Delivered);Assert.Equal(quote.FeeMinor,ServiceF2Tests.Run(s).PaidMinor);
        ServiceF2Tests.Exact(s,"h2-service-"+quantity);
    }
    [Fact]public void OldAcceptedPriceAndDeadlineRemainExactAfterNewWriterAndContinuation()
    {
        var(s,_,o)=ServiceF2Tests.Prepare();string terms=System.Text.Json.JsonSerializer.Serialize(o.Quote,SimulationJson.Options);long deadline=o.DeadlineTick;
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));copy.AdvanceTo(copy.Engine.Tick+3);var restored=copy.Engine.InspectServices()!.Orders.Single();
        Assert.False(copy.Engine.UsesAcceleratedMovement);Assert.Equal(terms,System.Text.Json.JsonSerializer.Serialize(restored.Quote,SimulationJson.Options));Assert.Equal(deadline,restored.DeadlineTick);
    }
}
