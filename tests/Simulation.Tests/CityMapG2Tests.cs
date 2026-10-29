using Simulation.Core;
using Simulation.Hosting;
using System.Text.Json;
using Xunit;
namespace Simulation.Tests;

public class CityMapG2Tests
{
    internal static CityMapFrame Map(GameSession s,long viewer=1)=>CityMap.Read(Presentation.Read(s.Engine.Configuration(),s.Engine,viewer),PlayerDesk.Read(s.Engine,new(viewer)));
    [Theory][InlineData("small_river",3)][InlineData("small_river",6)][InlineData("small_river",10)][InlineData("large_districts",3)][InlineData("large_districts",6)][InlineData("large_districts",10)]
    public void BothOriginalMapsShowTheWholeRosterAndFilterEachParticipant(string key,int bots)
    {
        var d=OrdinaryNewGame.Resolve(MapDesignTests.Template(key),NpcEconomyG1Tests.Profile(),new(bots),vnextBalance:MapDesignTests.Balance()).Scenario;
        var s=new GameSession(new(d));string before=s.Engine.ComputeStateHash();var map=Map(s);
        Assert.Equal(d.MapWidth,map.Width);Assert.Equal(d.MapHeight,map.Height);Assert.Equal(bots+1,map.Participants.Length);
        foreach(long id in map.Participants){var person=Assert.Single(map.Objects,o=>o.Kind=="actor"&&CityMap.Visible(o,CityLayer.People,id));Assert.Equal(id,person.Id);}
        Assert.Equal(s.Engine.Observe(new(1)).Plots.Length,map.Objects.Count(o=>o.Layer==CityLayer.Property));Assert.Equal(before,s.Engine.ComputeStateHash());
    }
    [Fact]public void ForeignWarehouseStockAndPaidTermsNeverEnterTheMapDTO()
    {
        var s=StorageC1Tests.Overflowing();var viewer=Map(s,1);var owner=Map(s,2);
        string data=JsonSerializer.Serialize(viewer,SimulationJson.Options);
        Assert.DoesNotContain("stock",data,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("escrow",data,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("unitPrice",data,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(viewer.Objects,o=>o.Kind=="incoming"||o.Kind=="delivery");Assert.Contains(owner.Objects,o=>o.Kind=="warehouse"&&o.Owner==2);
        var invalidOwn=PlayerDesk.Read(s.Engine,new(1)) with{Contracts=s.Engine.Observe(new(2)).OwnDeliveryContracts};
        Assert.DoesNotContain(CityMap.Read(Presentation.Read(s.Engine.Configuration(),s.Engine,1),invalidOwn).Objects,o=>o.Kind=="incoming");
    }
    [Fact]public void SelectingABuildingFootprintTargetsItsLegalEntrance()
    {
        var s=new GameSession(new(ServiceF2Tests.Definition()));var map=Map(s);var building=map.Objects.First(o=>o.Kind=="production");
        Assert.All(building.Footprint,c=>Assert.Contains(CityMap.At(map,c,CityLayer.Industry,0),o=>o.Key==building.Key));
        string before=s.Engine.ComputeStateHash();var route=s.Engine.PreviewMovement(new(1),building.Entry);Assert.True(route.Allowed,route.Reason);Assert.Equal(building.Entry,route.Path[^1]);Assert.Equal(before,s.Engine.ComputeStateHash());
        StorageC1Tests.Move(s,building.Entry);Assert.Equal(building.Entry,s.Engine.Observe(new(1)).Participant.Position);
    }
    [Fact]public void RoadPreviewUsesRealVehicleRulesWithoutCreatingTrafficOrChargingMoney()
    {
        var s=new GameSession(new(BillingE3Tests.Prepared()));BillingE3Tests.Car(s);string before=s.Engine.ComputeStateHash();
        var guide=s.Engine.PreviewMovement(new(1),new(16,1));Assert.True(guide.Allowed,guide.Reason);Assert.Equal(before,s.Engine.ComputeStateHash());
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));Assert.Equal(guide.Path.ToArray(),s.Engine.PreviewMovement(new(1),guide.Target).Path.ToArray());
        StorageC1Tests.Move(s,guide.Target);StorageC1Tests.Move(copy,guide.Target);Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
    }
    [Fact]public void CompletedRoadKeepsCityAndPrivateStagesAndTheirParticipantFilter()
    {
        var s=new GameSession(new(RoadE2Tests.Prepared()));var v=RoadE2Tests.Paver(s);RoadE2Tests.Step(s,RoadE2Tests.Work(v));
        var batch=s.Engine.InspectRoads()!.Works.Single().Batch!;s.AdvanceTo(batch.StartedTick+24);
        var road=Map(s).Objects.Single(o=>o.Kind=="road"&&o.Entry==batch.Cell);Assert.Equal(2,road.Level);Assert.Equal(new long[]{0,1},road.Contributions!.Select(c=>c.Builder.Value));
        Assert.True(CityMap.Visible(road,CityLayer.Roads,1));Assert.False(CityMap.Visible(road,CityLayer.Roads,2));
        var read=new CityReadState();var notice=Assert.Single(CityNotices.Read(s.Engine.Configuration(),PlayerDesk.Read(s.Engine,new(1)),read),n=>n.Target=="road"&&n.Position==batch.Cell);
        Assert.Equal(2,notice.Quantity);Assert.True(notice.Unread);string before=s.Engine.ComputeStateHash();read.MarkSeen(notice.Key);
        Assert.False(CityNotices.Read(s.Engine.Configuration(),PlayerDesk.Read(s.Engine,new(1)),read).Single(n=>n.Key==notice.Key).Unread);Assert.Equal(before,s.Engine.ComputeStateHash());
        CityExperienceG2Tests.Evidence(s,"road-built");
        Assert.Equal(JsonSerializer.Serialize(road,SimulationJson.Options),JsonSerializer.Serialize(Map(CheckpointStore.Decode(CheckpointStore.Encode(s))).Objects.Single(o=>o.Key==road.Key),SimulationJson.Options));
    }
    [Fact]public void AcceptedServiceShowsOnlyItsPartiesTheAgreedRoute()
    {
        var(s,_,order)=ServiceF2Tests.Prepare();var map=Map(s,1);var delivery=Assert.Single(map.Objects,o=>o.Kind=="delivery");
        Assert.Equal(order.Quote.Terms.Quantity,delivery.Quantity);Assert.Equal(order.Quote.Loaded.ToArray(),delivery.Route);Assert.Equal(order.Quote.DestinationPosition,delivery.Entry);ServiceF2Tests.Board(s,s.Engine.InspectFleet()!.Vehicles.Single());CityExperienceG2Tests.Evidence(s,"ready-driver",2);ServiceF2Tests.Load(s,order.Id);ServiceF2Tests.Step(s,new(ActionKind.NavigateDeliveryService,ServiceId:order.Id));CityExperienceG2Tests.Evidence(s,"in-transit",2);
        var foreign=PlayerDesk.Read(s.Engine,new(1));foreign=foreign with{Participant=foreign.Participant with{Id=new(3)}};
        Assert.DoesNotContain(CityMap.Read(Presentation.Read(s.Engine.Configuration(),s.Engine,1),foreign).Objects,o=>o.Kind=="delivery");
    }
    [Fact]public void ZoomIsAnchoredPanIsBoundedAndMemoryRetainsSelectionAcrossLiveUpdates()
    {
        var p=new CityMapPreferences();p.Bind(100,80);var at=p.World(360,250,800,600);p.ZoomAt(3,360,250,800,600);var after=p.World(360,250,800,600);
        Assert.Equal(at.X,after.X,9);Assert.Equal(at.Z,after.Z,9);p.SelectedKey="cell:8:9";p.Layers=CityLayer.Roads;p.Participant=11;
        var memory=p.Capture()!;p.Bind(100,80);Assert.Equal(memory,p.Capture());var restored=new CityMapPreferences();restored.Bind(100,80);restored.Restore(memory);Assert.Equal(memory,restored.Capture());
        restored.Pan(1e8,-1e8,800,600);Assert.InRange(restored.CenterX,0,100);Assert.InRange(restored.CenterZ,0,80);Assert.Equal(11,restored.Participant);
        Assert.Throws<ArgumentException>(()=>restored.Restore(memory with{Zoom=double.NaN}));
    }
    [Fact]public void EmptyCellSelectionSurvivesWithoutBecomingADeliveryPoint()
    {
        var s=new GameSession(new(ServiceF2Tests.Definition()));var map=Map(s);var empty=CityMap.Selected(map,"cell:1:1");Assert.NotNull(empty);Assert.Equal("cell",empty.Kind);
        Assert.Null(CityMap.Selected(map,"cell:100000:1"));Assert.Null(CityMap.Selected(map,"cell:x:y"));Assert.DoesNotContain(s.Engine.ServicesOf(new(1))!.OwnPoints,p=>p.Position==empty.Entry);
    }
    [Fact]public void PublicStatusAndMarketRefreshPreserveObjectIdentityAndReservationRights()
    {
        var d=OrdinaryNewGame.Resolve(MapDesignTests.Template("small_river"),NpcEconomyG1Tests.Profile(),new(3),vnextBalance:MapDesignTests.Balance()).Scenario;var s=new GameSession(new(d));
        var first=Map(s);var capital=first.Objects.First(o=>o.Kind=="capital");s.AdvanceTo(d.CapitalDemand!.PeriodTicks+d.CapitalDemand.OrderTtlTicks+1);
        Assert.Equal(capital.Key,Map(s).Objects.First(o=>o.Kind=="capital").Key);
        var frame=Presentation.Read(d,s.Engine,1);var resource=frame.Markers.First(m=>m.Kind=="resource");
        var refreshed=frame with{Markers=frame.Markers.Select(m=>m==resource?m with{Kind="resource-depleted",ResourceStock=0}:m).ToArray()};
        var before=CityMap.Read(frame,PlayerDesk.Read(s.Engine,new(1))).Objects.Single(o=>o.Entry==new Cell(resource.X,resource.Z)&&o.Layer==CityLayer.Resources);
        var after=CityMap.Read(refreshed,PlayerDesk.Read(s.Engine,new(1))).Objects.Single(o=>o.Key==before.Key);Assert.Equal("Depleted",after.Status);
        // A reservation changes public rights, not the legal owner or the plot's identity.
        var view=PlayerDesk.Read(s.Engine,new(1));var free=view.Plots.First(p=>p.OwnerId is null&&!p.IsCorridor);
        var reserved=view with{Plots=view.Plots.Select(p=>p.Id==free.Id?p with{ReservedBy=new(2),ReservationUntilTick=view.Tick+100}:p).ToArray()};
        var plot=CityMap.Read(frame,reserved).Objects.Single(o=>o.Key=="plot:"+free.Id.Value);Assert.Equal(0,plot.Owner);Assert.Equal(2,plot.Holder);Assert.True(CityMap.Visible(plot,CityLayer.Property,2));
    }
}
