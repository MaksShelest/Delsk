using Simulation.Core;
using Simulation.Hosting;
using Xunit;
namespace Simulation.Tests;
public class H2RoadChangeTests
{
    [Fact]public void FootRouteRestoresWhenDirtCompletesDuringItsCurrentOffroadEdge()
    {
        var d=RoadE2Tests.Prepared();var s=new GameSession(new(d with{Economy=d.Economy! with{Movement=new()}}));var paver=RoadE2Tests.Paver(s);
        var work=new Cell(27,2);StorageC1Tests.Move(s,work);RoadE2Tests.Step(s,RoadE2Tests.Work(paver,1,target:work));
        long started=s.Engine.InspectRoads()!.Works.Single().Batch!.StartedTick;s.AdvanceTo(started+15);RoadE2Tests.Step(s,RoadE2Tests.Work(paver,1,enabled:false,target:work));
        StorageC1Tests.Move(s,new(0,1),2);s.Send(new(2),new(ActionKind.MoveTo,Target:new(27,3)));s.Step();long move=s.Engine.Tick;s.AdvanceTo(move+44);
        RoadE2Tests.Step(s,RoadE2Tests.Work(paver,1,target:work));Assert.Equal(move+45,s.Engine.Tick);Assert.Contains(s.Engine.InspectRoads()!.Cells,c=>c.Cell==work&&c.Level==1);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));for(int n=0;n<10;n++){s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());}
        Assert.Equal(new Cell(27,3),s.Engine.Observe(new(2)).Participant.Position);RoadE2Tests.Exact(s,"h2-foot-road-change");
    }
}
