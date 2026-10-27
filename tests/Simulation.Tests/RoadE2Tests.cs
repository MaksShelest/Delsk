using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class RoadE2Tests
{
    internal static EconomyProfile Profile()=>VNextRoadProfile.Create(MapDesignTests.Economy(),MapDesignTests.Balance());
    internal static ScenarioDefinition Prepared(ulong seed=42)
    {
        var s=FleetE1Tests.Prepared(seed);var layout=s.Layout!;
        var corridor=layout.RoadCorridorCells.Concat(Enumerable.Range(0,40).Select(x=>new Cell(x,2))).Distinct().ToArray();
        return s with{Id="e2-prepared",StartingTreasuryMinor=500000,Economy=Profile().World.Rules,Layout=layout with{RoadCorridorCells=corridor,TurnCells=[new(4,1),new(12,1),new(20,1),new(28,1)]}};
    }
    internal static void Step(GameSession s,GameAction a,long actor=1,bool accepted=true)=>StorageC1Tests.Step(s,a,actor,accepted);
    internal static OwnedVehicle Paver(GameSession s,bool materials=true)
    {
        var v=FleetE1Tests.Buy(s,"trakt_d1");
        if(materials)foreach(var m in new[]{new MaterialCost("gravel",8),new("asphalt",6),new("concrete",2)})
        {StorageC1Tests.Harvest(s,m.ItemId,m.Quantity);StorageC1Tests.Put(s,v.CargoInventory,m.ItemId,m.Quantity);}
        FleetE1Tests.Board(s,v);StorageC1Tests.Move(s,new(10,1));return s.Engine.InspectFleet()!.Vehicles.Single(x=>x.Id==v.Id);
    }
    internal static GameAction Work(OwnedVehicle v,int level=2,EquipmentMode mode=EquipmentMode.Personal,bool enabled=true,Cell? target=null)=>
        new(ActionKind.SetRoadWork,VehicleId:v.Id,EquipmentLevel:level,Mode:mode,Enabled:enabled,Stops:[target??new(10,1)]);
    internal static void Exact(GameSession s,[System.Runtime.CompilerServices.CallerMemberName]string name="")
    {
        var restored=CheckpointStore.Decode(CheckpointStore.Encode(s));Assert.Equal(s.Engine.ComputeStateHash(),restored.Engine.ComputeStateHash());
        for(int i=0;i<3;i++){s.Step();restored.Step();Assert.Equal(s.Engine.ComputeStateHash(),restored.Engine.ComputeStateHash());}
        var replay=GameSession.Replay(s.Engine.Configuration(),s.CommandTape,s.Engine.Tick);
        if(s.Engine.ComputeStateHash()!=replay.Engine.ComputeStateHash()&&Environment.GetEnvironmentVariable("DELSK_E2_DEBUG") is {} debug)
        {
            Directory.CreateDirectory(debug);File.WriteAllText(Path.Combine(debug,name+".expected.json"),s.Engine.CreateCheckpointJson());File.WriteAllText(Path.Combine(debug,name+".replay.json"),replay.Engine.CreateCheckpointJson());
        }
        Assert.Equal(s.Engine.ComputeStateHash(),replay.Engine.ComputeStateHash());
        if(Environment.GetEnvironmentVariable("DELSK_E2_EVIDENCE") is not {} dir)return;
        Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,name+".json"),JsonSerializer.Serialize(s.Engine.Configuration(),SimulationJson.Options));
        File.WriteAllText(Path.Combine(dir,name+".commands.jsonl"),string.Join("\n",s.CommandTape.Select(c=>JsonSerializer.Serialize(c,SimulationJson.Options)))+"\n");
        File.WriteAllText(Path.Combine(dir,name+".tick"),s.Engine.Tick.ToString());
        CheckpointStore.Save(s,Path.Combine(dir,name+".save.json"));
    }
    [Fact]public void ProfileIsOptInAndBothExistingMapsHaveCityDirtAndThreeExactStages()
    {
        var p=Profile();Assert.Null(FleetE1Tests.Profile().World.Rules.Roads);Assert.Equal(p.Id,EconomyProfile.Read(Encoding.UTF8.GetString(VNextSupportedProfile.Serialize(p))).Id);
        Assert.Equal(new[]{20,40,60},p.World.Rules.Roads!.Stages.Select(s=>s.Speed));
        Assert.Equal(new long[]{200,400,800},p.World.Rules.Roads.Stages.Select(s=>s.PriceMinor));Assert.Equal(new[]{8,12,16},p.World.Rules.Roads.Stages.Select(s=>s.Ticks));
        foreach(var id in new[]{"small_river","large_districts"})
        {
            var config=OrdinaryNewGame.Resolve(MapDesignTests.Template(id),p,new(3),vnextBalance:MapDesignTests.Balance()).Scenario;
            var session=new GameSession(new(config));Assert.Equal(config.Layout!.InitialCityDirtCells.Length,session.Engine.InspectRoads()!.Cells.Length);
            Assert.All(session.Engine.InspectRoads()!.Cells,c=>Assert.Equal(0,Assert.Single(c.Contributions).Builder.Value));session.Step();
            var copy=CheckpointStore.Decode(CheckpointStore.Encode(session));Assert.Equal(session.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        }
    }
    [Fact]public void PersonalPaverConsumesRealCargoWorksHalfSpeedAndKeepsEveryContribution()
    {
        var s=new GameSession(new(Prepared()));var v=Paver(s);long gravel=FleetE1Tests.Stock(s,v.CargoInventory,"gravel");
        long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;Step(s,Work(v,3),accepted:false);Assert.Equal(cash,s.Engine.Observe(new(1)).Account.BalanceMinor);
        Step(s,Work(v));var start=s.Engine.InspectRoads()!.Works.Single().Batch!;
        Assert.Equal(0,start.Work);Assert.Equal(gravel-2,FleetE1Tests.Stock(s,v.CargoInventory,"gravel"));
        Assert.Equal(cash-400,s.Engine.Observe(new(1)).Account.BalanceMinor);Exact(s);
        s.AdvanceTo(start.StartedTick+23);Assert.Equal(1,s.Engine.InspectRoads()!.Cells.Single(c=>c.Cell==start.Cell).Level);s.Step();
        var road=s.Engine.InspectRoads()!.Cells.Single(c=>c.Cell==start.Cell);Assert.Equal(2,road.Level);Assert.Equal(new long[]{0,1},road.Contributions.Select(c=>c.Builder.Value));
        Step(s,Work(v,3));start=s.Engine.InspectRoads()!.Works.Single().Batch!;s.AdvanceTo(start.StartedTick+32);
        road=s.Engine.InspectRoads()!.Cells.Single(c=>c.Cell==start.Cell);Assert.Equal(3,road.Level);Assert.Equal(3,road.Contributions.Length);Exact(s);
    }
    [Fact]public void AutomaticPaverHiresTwoRealWorkersAndChargesOnlyActualWork()
    {
        var s=new GameSession(new(Prepared()));var v=Paver(s);Step(s,new(ActionKind.LeaveVehicle));Step(s,Work(v,mode:EquipmentMode.Automatic));
        for(int i=0;i<10&&s.Engine.InspectRoads()!.Works.Single().Batch is null;i++)s.Step();
        var work=s.Engine.InspectRoads()!.Works.Single();Assert.Equal(2,work.Group.Headcount);Assert.NotNull(work.Batch);
        var start=work.Batch!;long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;
        s.AdvanceTo(start.StartedTick+12);Assert.Equal(2,s.Engine.InspectRoads()!.Cells.Single(c=>c.Cell==start.Cell).Level);
        Assert.Equal(cash-12*40,s.Engine.Observe(new(1)).Account.BalanceMinor);s.Step();cash=s.Engine.Observe(new(1)).Account.BalanceMinor;s.AdvanceTo(s.Engine.Tick+5);Assert.Equal(cash,s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(10,s.Engine.Inspect().Unemployed+s.Engine.Inspect().EmploymentGroups.Sum(g=>(long)g.Headcount));Exact(s);
    }
    [Fact]public void MissingMaterialsAndProtectedPrivateCellsDoNotSpendMoneyOrCreateRoads()
    {
        var s=new GameSession(new(Prepared()));var v=Paver(s,false);long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;
        Step(s,Work(v));Assert.Equal("NoRoadMaterials",s.Engine.InspectRoads()!.Works.Single().Reason);Assert.Equal(cash,s.Engine.Observe(new(1)).Account.BalanceMinor);
        Step(s,Work(v,target:new(4,4)),accepted:false);Assert.DoesNotContain(s.Engine.InspectRoads()!.Cells,c=>c.Cell==new Cell(4,4));Exact(s);
    }
    [Fact]public void PublicRoadOrderHoldsExistingTreasuryAndPaysExactlyOnceAfterCompletion()
    {
        var s=new GameSession(new(Prepared()));var v=Paver(s);var order=s.Engine.InspectRoads()!.Orders.Single();
        Step(s,new(ActionKind.AcceptRoadOrder,OfferId:order.Id));Assert.Equal(order.Reward,s.Engine.Inspect().Accounts.Single(a=>a.Kind=="Treasury").ReservedMinor);
        Step(s,new(ActionKind.AcceptRoadOrder,OfferId:order.Id),accepted:false);
        StorageC1Tests.Move(s,order.Cell);Step(s,Work(v,level:order.Level,target:order.Cell));var batch=s.Engine.InspectRoads()!.Works.Single().Batch!;
        s.AdvanceTo(batch.StartedTick+2*Profile().World.Rules.Roads!.Stages[order.Level-1].Ticks+2);
        Assert.Single(s.Engine.Ledger(),l=>l.Reason=="RoadOrderReward");Exact(s);
    }
    [Fact]public void RestoreRejectsErasedContributionAndInvalidBypassOrTrafficReservation()
    {
        var s=new GameSession(new(Prepared()));var v=Paver(s);Step(s,Work(v));var text=s.Engine.CreateCheckpointJson();var node=JsonNode.Parse(text)!;
        node["roads"]!["cells"]![0]!["contributions"]=new JsonArray();Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(node.ToJsonString()));
        node=JsonNode.Parse(text)!;node["roads"]!["bypasses"]![0]!["path"]![0]=JsonSerializer.SerializeToNode(new Cell(4,4),SimulationJson.Options);
        Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(node.ToJsonString()));
        Exact(s);
    }
    [Fact]public void NativeEntryUsesPaidPaverAndActualHarvestedMaterials()
    {
        var s=new GameSession(new(Prepared()));Paver(s);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        if(Environment.GetEnvironmentVariable("DELSK_E2_EVIDENCE") is {} dir){Directory.CreateDirectory(dir);CheckpointStore.Save(s,Path.Combine(dir,"native-road.save.json"));}
    }
    [Fact]public void ASecondBuildersHighwayRetainsCityBaseAndFirstBuildersRoadRights()
    {
        var s=new GameSession(new(Prepared()));var first=Paver(s);Step(s,Work(first));
        while(s.Engine.InspectRoads()!.Cells.Single(c=>c.Cell==new Cell(10,1)).Level<2)s.Step();s.Step();
        StorageC1Tests.Move(s,new(14,2));Step(s,new(ActionKind.LeaveVehicle));s.AdvanceTo(Math.Max(501,s.Engine.Tick));
        var second=FleetE1Tests.Buy(s,"trakt_d1",2);
        foreach(var m in new[]{new MaterialCost("asphalt",2),new MaterialCost("concrete",1)})
        {StorageC1Tests.Harvest(s,m.ItemId,m.Quantity,2);StorageC1Tests.Put(s,second.CargoInventory,m.ItemId,m.Quantity,2);}
        StorageC1Tests.Move(s,s.Engine.FleetOf(new(2))!.Vehicles.Single().Position,2);Step(s,new(ActionKind.BoardVehicle,VehicleId:second.Id),2);
        StorageC1Tests.Move(s,new(10,1),2);Step(s,Work(second,3),2);
        var batch=s.Engine.InspectRoads()!.Works.Single(w=>w.VehicleId==second.Id).Batch!;s.AdvanceTo(batch.StartedTick+32);
        Assert.Equal(new long[]{0,1,2},s.Engine.InspectRoads()!.Cells.Single(c=>c.Cell==new Cell(10,1)).Contributions.Select(c=>c.Builder.Value));Exact(s);
    }
    [Fact]public void NoMoneyDoesNotConsumeRoadMaterialsOrStartFreeWork()
    {
        var d=Prepared();d=d with{Participants=[..d.Participants.Select(p=>p.Id==1?p with{CashMinor=1200000}:p)]};
        var s=new GameSession(new(d));var v=Paver(s);long before=FleetE1Tests.Stock(s,v.CargoInventory,"gravel");
        Step(s,Work(v));Assert.Equal("NoMoney",s.Engine.InspectRoads()!.Works.Single().Reason);Assert.Null(s.Engine.InspectRoads()!.Works.Single().Batch);
        Assert.Equal(before,FleetE1Tests.Stock(s,v.CargoInventory,"gravel"));Assert.DoesNotContain(s.Engine.Ledger(),l=>l.Reason=="RoadConstruction");Exact(s);
    }
    [Fact]public void AutomaticPaverTravelChargesItsActualCrewAndRetainsOneCargo()
    {
        var s=new GameSession(new(Prepared()));var v=Paver(s);Step(s,new(ActionKind.LeaveVehicle));
        Step(s,Work(v,mode:EquipmentMode.Automatic,target:new(11,1)));int bound=80;
        while(s.Engine.InspectRoads()!.Works.Single().Batch is null&&bound-->0)s.Step();
        Assert.True(bound>0);Assert.Equal(new Cell(11,1),StorageC1Tests.Inventory(s,v.CargoInventory).Position);
        var events=s.Engine.EventsSince(0).Where(e=>e.Type=="RoadWorkTravelled").ToArray();Assert.NotEmpty(events);
        Assert.All(events,e=>Assert.Equal(40,e.AmountMinor));Assert.Equal(events.Sum(e=>e.AmountMinor),s.Engine.Ledger().Where(l=>l.Reason=="RoadWages").Sum(l=>l.AmountMinor));Exact(s);
    }
    [Fact]public void EmptyPublicCorridorCanBecomeDirtBeforeRoadOne()
    {
        var s=new GameSession(new(Prepared()));var v=Paver(s);var at=new Cell(10,2);StorageC1Tests.Move(s,at);
        Step(s,Work(v,level:2,target:at),accepted:false);Step(s,Work(v,level:1,target:at));
        var batch=s.Engine.InspectRoads()!.Works.Single().Batch!;Assert.DoesNotContain(at,batch.Bypass);
        s.AdvanceTo(batch.StartedTick+16);Assert.Equal(1,s.Engine.InspectRoads()!.Cells.Single(c=>c.Cell==at).Level);
        Assert.Equal(1,Assert.Single(s.Engine.InspectRoads()!.Cells.Single(c=>c.Cell==at).Contributions).Builder.Value);Exact(s);
    }
}
