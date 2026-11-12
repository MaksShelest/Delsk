using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class LivingWorldTests
{
    private static bool Inside(PedestrianPose pose,int x,int z)
        => pose.X>=x-.501f&&pose.X<=x+.501f&&pose.Z>=z-.501f&&pose.Z<=z+.501f;

    [Fact]
    public void StraightWalkDoesNotStopAndTurnStaysInsideCells()
    {
        var straight=new PedestrianMotion(true,0,0,1,0,2,0,false,true,0,800);
        var samples=Enumerable.Range(0,5).Select(i=>PedestrianPoseMath.At(straight,i/4f)).ToArray();
        for(int i=1;i<samples.Length;i++)Assert.True(samples[i].X>samples[i-1].X+.05f);
        Assert.All(samples,p=>Assert.True(Inside(p,0,0)||Inside(p,1,0)||Inside(p,2,0)));
        var held=new PedestrianMotion(true,0,0,1,0,1,0,false,false,400,400);
        var still=PedestrianPoseMath.At(held,0);
        Assert.Equal(still,PedestrianPoseMath.At(held,1));
        Assert.True(still.X<.5f);
        var turn=new PedestrianMotion(true,0,0,1,0,1,1,true,true,700,300);
        for(int i=0;i<=20;i++)
        {
            var pose=PedestrianPoseMath.At(turn,i/20f);
            Assert.True(Inside(pose,0,0)||Inside(pose,1,0)||Inside(pose,1,1));
        }
    }

    [Fact]
    public void PedestrianSampleFollowsTheTickAndDoesNotChangeTheWorld()
    {
        var session=new GameSession(new(StorageC1Tests.Definition()));
        string id=session.Send(new(1),new(ActionKind.MoveTo,Target:new(4,0)));
        session.Step();
        Assert.Equal(ResultStatus.Accepted,session.Engine.Result(id)!.Status);
        Assert.False(session.Engine.PedestrianMotionOf(1).Active);
        session.Step();
        var hash=session.Engine.ComputeStateHash();
        var first=session.Engine.PedestrianMotionOf(1);
        Assert.True(first.Active);
        Assert.Equal(first,session.Engine.PedestrianMotionOf(1));
        Assert.Equal(hash,session.Engine.ComputeStateHash());
        for(int n=0;n<8&&session.Engine.PedestrianMotionOf(1).Active;n++)
        {
            var before=session.Engine.PedestrianMotionOf(1);
            var end=PedestrianPoseMath.At(before,1);
            session.Step();
            var after=session.Engine.PedestrianMotionOf(1);
            if(!after.Active)break;
            var start=PedestrianPoseMath.At(after,0);
            Assert.True(Math.Abs(end.X-start.X)<.03f&&Math.Abs(end.Z-start.Z)<.03f);
            Assert.True(Math.Abs(end.X-start.X)+Math.Abs(end.Z-start.Z)<1.2f);
        }
        var saved=session.Engine.PedestrianMotionOf(1);
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(session));
        Assert.Equal(saved,loaded.Engine.PedestrianMotionOf(1));
        Assert.Equal(session.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());
    }

    [Fact]
    public void ManualInputDuringRouteKeepsCheckpointAndReplayValid()
    {
        var session = new GameSession(new(StorageC1Tests.Definition()));
        var actor = new ParticipantId(1);
        var move = session.Send(actor, new(ActionKind.MoveTo, Target: new(4, 0)));
        session.Step();
        Assert.Equal(ResultStatus.Accepted, session.Engine.Result(move)!.Status);
        Assert.True(session.Engine.Observe(actor).Participant.Busy);
        var before = session.Engine.TravelOf(actor);

        session.SetTravelHeading(actor, -1, 0);

        Assert.Equal(before, session.Engine.TravelOf(actor));
        var loaded = CheckpointStore.Decode(CheckpointStore.Encode(session));
        Assert.Equal(session.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        StorageC1Tests.Idle(session);
        StorageC1Tests.Idle(loaded);
        Assert.Equal(new Cell(4, 0), loaded.Engine.Observe(actor).Participant.Position);
        Assert.Equal(session.Engine.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        var replay = GameSession.Replay(session.Engine.Configuration(), session.CommandTape, session.Engine.Tick);
        Assert.Equal(session.Engine.ComputeStateHash(), replay.Engine.ComputeStateHash());
    }

    [Fact]
    public void GroundPilesFollowOwnStockAndHideForeignQuantities()
    {
        var definition=StorageC1Tests.Definition();
        var session=new GameSession(new(definition));
        Assert.DoesNotContain(Presentation.Read(definition,session.Engine,1).Markers,m=>m.Kind=="ground");
        var pile=new InventoryId(session.Engine.StorageOf(new(1)).Ground.Single().InventoryId);
        StorageC1Tests.Harvest(session,"wood",4);
        StorageC1Tests.Put(session,pile,"wood",4);
        var hash=session.Engine.ComputeStateHash();
        var own=Presentation.Read(definition,session.Engine,1);
        var ground=Assert.Single(own.Markers,m=>m.Kind=="ground");
        Assert.Equal(1,ground.OwnerId);
        Assert.True(ground.Warehouse is {Known:true});
        Assert.Contains(ground.Warehouse!.Goods,g=>g.ItemId=="wood"&&g.Volume>0);
        var foreign=Presentation.Read(definition,session.Engine,2);
        Assert.DoesNotContain(foreign.Markers,m=>m.Kind=="ground"&&m.OwnerId==1);
        Assert.All(foreign.Markers.Where(m=>m.OwnerId==1),m=>Assert.True(m.Warehouse is not {Known:true}));
        Assert.Equal(hash,session.Engine.ComputeStateHash());
        StorageC1Tests.Harvest(session,"grain",2);
        StorageC1Tests.Put(session,pile,"grain",2);
        var both=Assert.Single(Presentation.Read(definition,session.Engine,1).Markers,m=>m.Kind=="ground");
        Assert.Contains(both.Warehouse!.Goods,g=>g.ItemId=="grain");
        StorageC1Tests.Step(session,new(ActionKind.Transfer,Source:pile,Destination:StorageC1Tests.Carrier(session),ItemId:"wood",Quantity:2));
        StorageC1Tests.Idle(session);
        var partial=Assert.Single(Presentation.Read(definition,session.Engine,1).Markers,m=>m.Kind=="ground");
        Assert.True(partial.Warehouse!.Goods.Single(g=>g.ItemId=="wood").Volume<ground.Warehouse.Goods.Single(g=>g.ItemId=="wood").Volume);
        var left=StorageC1Tests.Inventory(session,pile);
        foreach(var item in left.Stock.Where(s=>s.Quantity>0))
        {StorageC1Tests.Step(session,new(ActionKind.Transfer,Source:pile,Destination:StorageC1Tests.Carrier(session),ItemId:item.ItemId,Quantity:item.Quantity));StorageC1Tests.Idle(session);}
        Assert.DoesNotContain(Presentation.Read(definition,session.Engine,1).Markers,m=>m.Kind=="ground");
        Assert.Empty(WarehouseVisuals.Layout(new(false,70,[new("wood",70)])));
        Assert.Empty(WarehouseVisuals.Layout(new(true,70,[])));
        int small=WarehouseVisuals.Layout(new(true,70,[new("wood",8)])).Sum(p=>p.Layers);
        int full=WarehouseVisuals.Layout(new(true,70,[new("wood",70)])).Sum(p=>p.Layers);
        Assert.True(full>small&&small>0);
    }

    [Fact]
    public void OwnershipOutlineDropsSharedEdgesAndKeepsABentPlot()
    {
        var cells=new LandParcel[]
        {
            new(0,0,1,"plot-own"),new(1,0,1,"plot-own"),new(0,1,1,"plot-own"),
            new(2,0,1,"plot-reserved"),new(3,0,2,"plot-foreign"),new(4,0,0,"plot-free")
        };
        var own=LandOutline.Borders(cells,false);
        Assert.DoesNotContain(own,e=>Near(e,.5f,0));
        Assert.DoesNotContain(own,e=>Near(e,0,.5f));
        Assert.Contains(own,e=>Near(e,-.5f,0));
        Assert.Contains(own,e=>Near(e,1,.5f));
        Assert.Contains(own,e=>Near(e,.5f,1));
        Assert.DoesNotContain(own,e=>e.State is "plot-free" or "plot-foreign");
        var all=LandOutline.Borders(cells,true);
        Assert.Contains(all,e=>e.State=="plot-free");
        Assert.Contains(all,e=>e.State=="plot-foreign");
        Assert.Contains(all,e=>e.State=="plot-reserved");
        Assert.Contains(all,e=>e.Owner==1&&e.State=="plot-own"&&Near(e,1.5f,0));
    }

    [Fact]
    public void RoadCardKeepsBothRevenueShares()
    {
        var city=new RoadContribution(new(1,1),1,new(0),12,0,500);
        var crew=city with{Builder=new(1),Level=2,PriceMinor=900};
        string text=RoadCardText.Header("ru")+"\n"+RoadCardText.Line("ru","Грунтовка","Город",city)+"\n"+RoadCardText.Line("ru","Дорога I","Участник #1",crew)
            +"\n"+RoadCardText.Header("en")+"\n"+RoadCardText.Line("en","Dirt road","City",city)+"\n"+RoadCardText.Line("en","Road I","Participant #1",crew);
        Assert.Contains("100%",text,StringComparison.Ordinal);
        Assert.Contains("90%",text,StringComparison.Ordinal);
        Assert.Contains("10%",text,StringComparison.Ordinal);
        Assert.Contains("t12",text,StringComparison.Ordinal);
        Assert.Contains("денежный вклад",text,StringComparison.Ordinal);
        Assert.Contains("cash contribution",text,StringComparison.Ordinal);
    }

    private static bool Near(LandBorder edge,float x,float z)
        => Math.Abs((edge.X1+edge.X2)/2f-x)<.08f&&Math.Abs((edge.Z1+edge.Z2)/2f-z)<.08f;
}
