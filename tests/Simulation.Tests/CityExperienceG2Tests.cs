using Simulation.Core;
using Simulation.Hosting;
using System.Text.Json;
using Xunit;
namespace Simulation.Tests;

public class CityExperienceG2Tests
{
    internal static void Evidence(GameSession session,string name,long actor=1)
    {
        if(Environment.GetEnvironmentVariable("DELSK_G2_EVIDENCE") is not {} folder)return;
        Directory.CreateDirectory(folder);CheckpointStore.Save(session,Path.Combine(folder,name+".save.json"));File.WriteAllText(Path.Combine(folder,name+".actor"),actor.ToString());
        File.WriteAllText(Path.Combine(folder,name+".json"),JsonSerializer.Serialize(session.Engine.Configuration(),SimulationJson.Options));
        File.WriteAllText(Path.Combine(folder,name+".commands.jsonl"),string.Join('\n',session.CommandTape.Select(c=>JsonSerializer.Serialize(c,SimulationJson.Options)))+"\n");
        File.WriteAllText(Path.Combine(folder,name+".tick"),session.Engine.Tick.ToString());
    }

    [Fact]public void InvoiceNotificationViewingAndPaymentRemainDifferentRealStates()
    {
        var s=new GameSession(new(BillingE3Tests.Prepared()));var bill=BillingE3Tests.Invoice(s);var memory=new CityReadState();
        var notice=CityNotices.Read(s.Engine.Configuration(),PlayerDesk.Read(s.Engine,new(1)),memory).Single(n=>n.Key=="bill:"+bill.Id);
        Evidence(s,"invoice");Assert.True(notice.Unread);Assert.False(notice.Resolved);long cash=s.Engine.AccountOf(new(1)).Account.BalanceMinor;
        s.SetPaused(true);s.Send(new(1),notice.ViewAction!);long tick=s.Engine.Tick;s.Step();Assert.Equal(tick,s.Engine.Tick);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.SetPaused(false);copy.SetPaused(false);s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        notice=CityNotices.Read(s.Engine.Configuration(),PlayerDesk.Read(s.Engine,new(1)),memory).Single(n=>n.Key==notice.Key);Assert.False(notice.Unread);Assert.False(notice.Resolved);Assert.Equal(cash,s.Engine.AccountOf(new(1)).Account.BalanceMinor);
        BillingE3Tests.Step(s,new(ActionKind.PayInvoice,OfferId:bill.Id));Assert.True(CityNotices.Read(s.Engine.Configuration(),PlayerDesk.Read(s.Engine,new(1)),memory).Single(n=>n.Key==notice.Key).Resolved);
    }
    [Fact]public void ReadingOverflowDoesNotEraseCargoOrExtendItsExpiry()
    {
        var s=StorageC1Tests.Overflowing();var read=new CityReadState();var notice=CityNotices.Read(s.Engine.Configuration(),PlayerDesk.Read(s.Engine,new(2)),read).Single(n=>n.Category=="overflow");var before=s.Engine.ComputeStateHash();Evidence(s,"overflow",2);
        read.MarkSeen(notice.Key);read=CityReadState.Restore(JsonSerializer.Deserialize<CityReadMemory>(JsonSerializer.Serialize(read.Capture()))!);
        var seen=CityNotices.Read(s.Engine.Configuration(),PlayerDesk.Read(s.Engine,new(2)),read).Single(n=>n.Key==notice.Key);Assert.False(seen.Unread);Assert.Equal(notice.Deadline,seen.Deadline);Assert.Equal(before,s.Engine.ComputeStateHash());
        StorageC1Tests.Move(s,new(0,2),2);StorageC1Tests.Step(s,new(ActionKind.Transfer,Source:StorageC1Tests.Warehouse(s,2),Destination:StorageC1Tests.Carrier(s,2),ItemId:"grain",Quantity:10),2);StorageC1Tests.Idle(s,2);
        Assert.DoesNotContain(CityNotices.Read(s.Engine.Configuration(),PlayerDesk.Read(s.Engine,new(2)),read),n=>n.Key==notice.Key);
    }
    [Fact]public void ActualConstructionProducesAnActionableReadNoticeWithoutASecondReward()
    {
        var d=StorageC1Tests.Definition();var s=new GameSession(new(d));var pile=new InventoryId(s.Engine.StorageOf(new(1)).Ground.Single().InventoryId);
        StorageC1Tests.Harvest(s,"plank",10);StorageC1Tests.Put(s,pile,"plank",10);StorageC1Tests.Step(s,new(ActionKind.Build,Target:new(0,0),BuildingTypeId:"warehouse"));
        s.AdvanceTo(s.Engine.StorageOf(new(1)).Construction.Single().CompletesTick);var read=new CityReadState();var notice=Assert.Single(CityNotices.Read(d,PlayerDesk.Read(s.Engine,new(1)),read),n=>n.Category=="work");Assert.Equal("building",notice.Target);Evidence(s,"built");
        var before=s.Engine.ComputeStateHash();read.MarkSeen(notice.Key);Assert.False(CityNotices.Read(d,PlayerDesk.Read(s.Engine,new(1)),read).Single(n=>n.Key==notice.Key).Unread);Assert.Equal(before,s.Engine.ComputeStateHash());
    }
    [Fact]public void IncomingRequestProjectionDoesNotPauseOrSpendItsResponseTicks()
    {
        var(s,_,terms)=ServiceF1Tests.Prepare();var order=ServiceF1Tests.Request(s,terms);var state=new CityReadState();var before=s.Engine.ComputeStateHash();
        for(int i=0;i<20;i++){var n=CityNotices.Read(s.Engine.Configuration(),PlayerDesk.Read(s.Engine,new(2)),state).Single(n=>n.Key=="service:"+order.Id);Assert.True(n.Unread);Assert.Equal(order.RespondByTick,n.Deadline);}
        Evidence(s,"pending-service",2);Assert.False(s.Paused);Assert.Equal(before,s.Engine.ComputeStateHash());s.AdvanceTo(order.RespondByTick+1);Assert.Equal("Expired",s.Engine.InspectServices()!.Orders.Single().State);
        for(int i=0;i<24;i++){var historical=ServiceF1Tests.Request(s,terms);ServiceF1Tests.Step(s,new(ActionKind.RejectDeliveryService,ServiceId:historical.Id),2);}
        Assert.Equal(25,s.Engine.ServicesOf(new(2))!.OwnOrders.Length);Evidence(s,"service-history",2);
    }
    [Fact]public void CancellationDuringPersonalServiceNavigationRestoresBeforePhysicalReturn()
    {
        var(s,v,o)=ServiceF2Tests.Prepare();ServiceF2Tests.Board(s,v);ServiceF2Tests.Load(s,o.Id);
        ServiceF2Tests.Step(s,new(ActionKind.NavigateDeliveryService,ServiceId:o.Id));
        Assert.True(s.Engine.Observe(new(2)).Participant.Busy);
        ServiceF2Tests.Step(s,new(ActionKind.CancelDeliveryService,ServiceId:o.Id),1);
        Assert.Equal("Returning",ServiceF2Tests.Run(s).Stage);Assert.Equal(30,ServiceF2Tests.Run(s).Cargo);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));
        for(int i=0;s.Engine.Observe(new(2)).Participant.Busy&&i<2000;i++)
        {s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());}
        Assert.False(s.Engine.Observe(new(2)).Participant.Busy);Assert.Equal(0,ServiceF2Tests.Run(s).PaidMinor);
        ServiceF2Tests.Navigate(s,o.Id);ServiceF2Tests.Step(s,new(ActionKind.UnloadDeliveryService,ServiceId:o.Id));
        ServiceF2Tests.Until(s,()=>ServiceF2Tests.Run(s).Stage=="Cancelled");
        Assert.Equal(30,ServiceF2Tests.Run(s).Returned);Assert.Equal(o.Quote.FeeMinor,ServiceF2Tests.Run(s).RefundedMinor);
        ServiceF2Tests.Exact(s,"g2-cancel-physical-return");
    }
    [Theory][InlineData(0,0,1000,800,10,25,970,740,10,25,20,35)][InlineData(200,100,1000,800,0,0,2000,1500,0,0,0,0)]
    public void SafeInsetsRespectTheActualWindowAndViewport(double x,double y,double w,double h,double sx,double sy,double sw,double sh,double left,double top,double right,double bottom)
    {
        var actual=ScreenInsets.Calculate(w/2,h/2,x,y,w,h,sx,sy,sw,sh);Assert.Equal(new ScreenInsets(left/2,top/2,right/2,bottom/2),actual);
    }
    [Theory][InlineData(TabletKind.CityMap)][InlineData(TabletKind.Notices)][InlineData(TabletKind.Tutorial)]
    public void GlobalScreensKeepTheirTimeChoiceAcrossMovementAndRestoreExternalPause(TabletKind kind)
    {
        var s=new GameSession(new(ServiceF2Tests.Definition()));var view=PlayerDesk.Read(s.Engine,new(1));var tablet=new TabletState();
        if(kind==TabletKind.CityMap)tablet.OpenCityMap(view);else if(kind==TabletKind.Notices)tablet.OpenNotices(view);else tablet.OpenTutorial(view);
        Assert.True(tablet.BlocksTime);tablet.SetTimeRunning(true);Assert.False(tablet.BlocksTime);StorageC1Tests.Move(s,new(1,1));Assert.True(tablet.ValidateLocation(PlayerDesk.Read(s.Engine,new(1)),s.Engine.Configuration()));Assert.Equal(kind,tablet.Kind);
        tablet.Close();Assert.True(tablet.IsPaused(true));Assert.False(tablet.IsPaused(false));
    }
}
