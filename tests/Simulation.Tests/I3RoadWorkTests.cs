using Simulation.Core;
using Simulation.Hosting;
using System.Text.Json;
using Xunit;

namespace Simulation.Tests;

internal static class I3Fixture
{
    private static readonly Dictionary<(bool,int,bool),string> Saved=[];
    internal static (GameSession Session,OwnedVehicle Paver) Create(bool materials=true,int population=10,bool poor=false)
    {
        if(!Saved.TryGetValue((materials,population,poor),out var json))
        {
            var d=RoadE2Tests.Prepared();var branch=Enumerable.Range(2,5).Select(z=>new Cell(8,z)).ToArray();
            d=d with{Population=population,Participants=[..d.Participants.Select(p=>p.Id==1&&poor?p with{CashMinor=1200000}:p)],Economy=d.Economy! with{Movement=MovementRules.I1,VehicleControl=VehicleControlRules.I2},
                Layout=d.Layout! with{RoadCorridorCells=[..d.Layout.RoadCorridorCells.Concat(branch).Concat(branch.Select(c=>new Cell(9,c.Z))).Distinct()],InitialCityDirtCells=[..d.Layout.InitialCityDirtCells.Concat(branch)]}};
            var original=new GameSession(new(d));RoadE2Tests.Paver(original,materials);I2Fixture.Quanta(original,10);json=CheckpointStore.Encode(original);Saved[(materials,population,poor)]=json;
        }
        var session=CheckpointStore.Decode(json);return(session,session.Engine.InspectFleet()!.Vehicles.Single());
    }
    internal static RoadCellQuote Quote(GameSession s,OwnedVehicle v,Cell? at=null,EquipmentMode mode=EquipmentMode.Personal)=>s.Engine.QuoteRoadCell(new(1),v.Id,at??new(10,1),mode);
    internal static GameAction Start(OwnedVehicle v,Cell? at=null,int level=2,EquipmentMode mode=EquipmentMode.Personal)=>new(ActionKind.StartRoadCell,Target:at??new(10,1),VehicleId:v.Id,Mode:mode,EquipmentLevel:level);
    internal static RoadWork Work(GameSession s)=>s.Engine.InspectRoads()!.Works.Single();
    internal static void Begin(GameSession s,OwnedVehicle v,Cell? at=null,int level=2)
    {I2Fixture.Step(s,Start(v,at,level));int bound=10;while(Work(s).Batch is null&&bound-->0)s.Step();Assert.NotNull(Work(s).Batch);}
    internal static void Exact(GameSession s)=>RoadE2Tests.Exact(s);
    internal static void Save(GameSession session,string name)
    {
        if(Environment.GetEnvironmentVariable("DELSK_I3_EVIDENCE") is not {} folder)return;
        Directory.CreateDirectory(folder);CheckpointStore.Save(session,Path.Combine(folder,name+".save.json"));
        File.WriteAllText(Path.Combine(folder,name+".scenario.json"),JsonSerializer.Serialize(session.Engine.Configuration(),SimulationJson.Options));
        File.WriteAllText(Path.Combine(folder,name+".commands.jsonl"),string.Join('\n',session.CommandTape.Select(c=>JsonSerializer.Serialize(c,SimulationJson.Options)))+"\n");
        File.WriteAllText(Path.Combine(folder,name+"-boundary.json"),JsonSerializer.Serialize(new{tick=session.Engine.Tick,quantum=session.Engine.TravelQuantum,hash=session.Engine.ComputeStateHash()},SimulationJson.Options));
    }
}

public class I3PositioningTests
{
    [Theory][InlineData(10,1)][InlineData(8,4)]
    public void ConfirmedCellIsCenteredGraduallyAndPersistsBeforeAnySpending(int x,int z)
    {
        var(s,v)=I3Fixture.Create();var at=new Cell(x,z);if(at!=new Cell(10,1))I2Fixture.Move(s,at);I2Fixture.Quanta(s,10);
        var before=s.Engine.VehicleTravelOf(v.Id);Assert.True(before.OffsetX!=500||before.OffsetZ!=500);
        long cash=s.Engine.Observe(new(1)).Account.BalanceMinor,stock=FleetE1Tests.Stock(s,v.CargoInventory,"gravel");
        I2Fixture.Step(s,I3Fixture.Start(v,at));Assert.Equal("PaverPositioning",I3Fixture.Work(s).Reason);Assert.Null(I3Fixture.Work(s).Batch);
        Assert.Equal(cash,s.Engine.Observe(new(1)).Account.BalanceMinor);Assert.Equal(stock,FleetE1Tests.Stock(s,v.CargoInventory,"gravel"));
        Assert.Equal(at,Assert.Single(I3Fixture.Work(s).Route));
        var moved=s.Engine.VehicleTravelOf(v.Id);Assert.InRange(Math.Abs(moved.OffsetX-before.OffsetX)+Math.Abs(moved.OffsetZ-before.OffsetZ),1,40);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));
        for(int n=0;n<5;n++){s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());}
        Assert.NotNull(I3Fixture.Work(s).Batch);Assert.Equal(500,s.Engine.VehicleTravelOf(v.Id).OffsetX);Assert.Equal(500,s.Engine.VehicleTravelOf(v.Id).OffsetZ);
        Assert.Equal(cash-400,s.Engine.Observe(new(1)).Account.BalanceMinor);I3Fixture.Exact(s);
    }
    [Fact]public void BetweenCellsIsRejectedInsteadOfChoosingAnUnexplainedWorkCell()
    {
        var(s,v)=I3Fixture.Create();s.SetTravelHeading(new(1),v.FacingX,v.FacingZ);int bound=20;do{s.AdvanceTravelQuantum();}while(I2Fixture.Traffic(s,v.Id).Progress==0&&bound-->0);Assert.True(bound>0);s.SetTravelHeading(new(1),0,0);
        var at=s.Engine.Observe(new(1)).Participant.Position;Assert.Equal("VehicleMoving",I3Fixture.Quote(s,v,at).Reason);
        string id=s.Send(new(1),I3Fixture.Start(v,at));s.Step();Assert.Equal(ResultStatus.Rejected,s.Engine.Result(id)!.Status);Assert.Empty(s.Engine.InspectRoads()!.Works);
    }
    [Fact]public void OccupiedSweptSpaceRejectsConfirmationWithoutCharging()
    {
        var(s,v)=I3Fixture.Create();I2Fixture.Step(s,new(ActionKind.LeaveVehicle));
        string before=s.Engine.ComputeStateHash();Assert.Equal("WorkSpaceOccupied",I3Fixture.Quote(s,v,mode:EquipmentMode.Automatic).Reason);Assert.Equal(before,s.Engine.ComputeStateHash());
        string id=s.Send(new(1),I3Fixture.Start(v,mode:EquipmentMode.Automatic));s.Step();Assert.Equal("WorkSpaceOccupied",s.Engine.Result(id)!.ReasonCode);Assert.Empty(s.Engine.InspectRoads()!.Works);
        Assert.DoesNotContain(s.Engine.Ledger(),l=>l.Reason=="RoadConstruction");
    }
    [Fact]public void CancelDuringAlignmentHasNoConstructionCostAndUnlocksMovement()
    {
        var(s,v)=I3Fixture.Create();I2Fixture.Step(s,I3Fixture.Start(v));
        I2Fixture.Step(s,new(ActionKind.SetRoadWork,VehicleId:v.Id,Mode:EquipmentMode.Off,Enabled:false,EquipmentLevel:2,Stops:[]));
        Assert.Null(I3Fixture.Work(s).Batch);Assert.DoesNotContain(s.Engine.Ledger(),l=>l.Reason=="RoadConstruction");
        I2Fixture.Move(s,new(11,1));Assert.Equal(new(11,1),s.Engine.Observe(new(1)).Participant.Position);I3Fixture.Exact(s);
    }
}

public class I3WorkTests
{
    [Theory][InlineData(1)][InlineData(2)]
    public void AutomaticCellHiresRealCrewAndUsesItsActualWorkRate(int staff)
    {
        var(s,v)=I3Fixture.Create(population:staff);I2Fixture.Step(s,new(ActionKind.LeaveVehicle));I2Fixture.Move(s,new(9,1));
        Assert.True(I3Fixture.Quote(s,v,mode:EquipmentMode.Automatic).CanStart);
        var before=s.Engine.VehicleTravelOf(v.Id);
        I2Fixture.Step(s,I3Fixture.Start(v,mode:EquipmentMode.Automatic));Assert.Equal("PaverPositioningNoStaff",I3Fixture.Work(s).Reason);Assert.Equal(before,s.Engine.VehicleTravelOf(v.Id));int bound=12;
        while(I3Fixture.Work(s).Batch is null&&bound-->0)s.Step();Assert.True(bound>0);var work=I3Fixture.Work(s);
        Assert.Equal(3*staff*20,s.Engine.EventsSince(0).Where(e=>e.Type=="RoadWorkTravelled").Sum(e=>e.AmountMinor));
        Assert.Equal(staff,work.Group.Headcount);Assert.Equal(24/staff,I3Fixture.Quote(s,v,mode:EquipmentMode.Automatic).ExpectedTicks);
        I2Fixture.Step(s,new(ActionKind.SetRoadCellEnabled,VehicleId:v.Id,Enabled:false));
        long paused=I3Fixture.Work(s).Batch!.Work;s.AdvanceTo(s.Engine.Tick+2);Assert.Equal(paused,I3Fixture.Work(s).Batch!.Work);
        I2Fixture.Step(s,new(ActionKind.SetRoadCellEnabled,VehicleId:v.Id,Enabled:true));
        long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;
        long remaining=24/staff-I3Fixture.Work(s).Batch!.Work/staff;
        s.AdvanceTo(s.Engine.Tick+remaining);Assert.Null(I3Fixture.Work(s).Batch);Assert.Equal(cash-remaining*staff*work.Group.WageMinor,s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(2,s.Engine.InspectRoads()!.Cells.Single(r=>r.Cell==new Cell(10,1)).Level);I3Fixture.Exact(s);
    }
    [Fact]public void MoneyIsCheckedBeforeAlignmentOrMaterialReservation()
    {
        var(s,v)=I3Fixture.Create(poor:true);Assert.Equal("NoMoney",I3Fixture.Quote(s,v).Reason);
        long gravel=FleetE1Tests.Stock(s,v.CargoInventory,"gravel");string id=s.Send(new(1),I3Fixture.Start(v));s.Step();
        Assert.Equal("NoMoney",s.Engine.Result(id)!.ReasonCode);Assert.Equal(gravel,FleetE1Tests.Stock(s,v.CargoInventory,"gravel"));Assert.Empty(s.Engine.InspectRoads()!.Works);
    }
    [Fact]public void HighwayKeepsExistingContributionsAndCannotBeUpgradedAgain()
    {
        var(s,v)=I3Fixture.Create();I3Fixture.Begin(s,v);s.AdvanceTo(I3Fixture.Work(s).Batch!.StartedTick+24);
        I3Fixture.Begin(s,v,level:3);s.AdvanceTo(I3Fixture.Work(s).Batch!.StartedTick+32);
        Assert.Equal(3,s.Engine.InspectRoads()!.Cells.Single(r=>r.Cell==new Cell(10,1)).Contributions.Length);
        Assert.Equal("RoadAlreadyComplete",I3Fixture.Quote(s,v).Reason);Assert.Equal(new long[]{400,800},s.Engine.Ledger().Where(l=>l.Reason=="RoadConstruction").Select(l=>l.AmountMinor));I3Fixture.Exact(s);
    }
    [Fact]public void RealStageCostsCompletionPauseAndRepeatedPressPreserveOneBatch()
    {
        var(s,v)=I3Fixture.Create();I3Fixture.Begin(s,v);var batch=I3Fixture.Work(s).Batch!;long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;
        string repeated=s.Send(new(1),I3Fixture.Start(v));s.Step();Assert.Equal("RoadBatchInProgress",s.Engine.Result(repeated)!.ReasonCode);
        Assert.Equal(cash,s.Engine.Observe(new(1)).Account.BalanceMinor);Assert.Single(s.Engine.Ledger(),l=>l.Reason=="RoadConstruction");
        var work=I3Fixture.Work(s);I2Fixture.Step(s,new(ActionKind.SetRoadWork,VehicleId:v.Id,Mode:work.Mode,Enabled:false,EquipmentLevel:work.Level,Stops:work.Route));
        long done=I3Fixture.Work(s).Batch!.Work;s.AdvanceTo(s.Engine.Tick+3);Assert.Equal(done,I3Fixture.Work(s).Batch!.Work);
        I3Fixture.Exact(s);I2Fixture.Step(s,new(ActionKind.SetRoadWork,VehicleId:v.Id,Mode:work.Mode,Enabled:true,EquipmentLevel:work.Level,Stops:work.Route));
        int bound=30;while(I3Fixture.Work(s).Batch is not null&&bound-->0)s.Step();Assert.True(bound>0);
        var road=s.Engine.InspectRoads()!.Cells.Single(r=>r.Cell==batch.Cell);Assert.Equal(2,road.Level);Assert.Equal(new long[]{0,1},road.Contributions.Select(c=>c.Builder.Value));
        Assert.Equal(400,Assert.Single(s.Engine.Ledger(),l=>l.Reason=="RoadConstruction").AmountMinor);I3Fixture.Exact(s);
    }
    [Fact]public void EmptyCorridorCanBeBuiltAndStagesCannotBeSkipped()
    {
        var(s,v)=I3Fixture.Create();I2Fixture.Move(s,new(12,2));var at=new Cell(12,2);
        string id=s.Send(new(1),I3Fixture.Start(v,at,3));s.Step();Assert.Equal("PreviousRoadStageRequired",s.Engine.Result(id)!.ReasonCode);
        I3Fixture.Begin(s,v,at,1);var batch=I3Fixture.Work(s).Batch!;s.AdvanceTo(batch.StartedTick+16);
        Assert.Equal(1,s.Engine.InspectRoads()!.Cells.Single(r=>r.Cell==at).Level);I3Fixture.Exact(s);
    }
    [Fact]public void MissingMaterialsHaveExactShortagesAndNeverConfigureFreeWork()
    {
        var(s,v)=I3Fixture.Create(false);var q=I3Fixture.Quote(s,v);Assert.Equal("NoRoadMaterials",q.Reason);
        Assert.Contains(q.Materials,m=>m.ItemId=="gravel"&&m.Required==2&&m.Available==0);
        string id=s.Send(new(1),I3Fixture.Start(v));s.Step();Assert.Equal("NoRoadMaterials",s.Engine.Result(id)!.ReasonCode);Assert.Empty(s.Engine.InspectRoads()!.Works);
    }
    [Fact]public void WorkingPaverIgnoresManualMovementAndRestoresWithNearbyTraffic()
    {
        var(s,v)=I3Fixture.Create();I3Fixture.Begin(s,v);var at=s.Engine.Observe(new(1)).Participant.Position;
        s.SetTravelHeading(new(1),1,0);I2Fixture.Quanta(s,20);Assert.Equal(at,s.Engine.Observe(new(1)).Participant.Position);
        Assert.Equal(500,s.Engine.VehicleTravelOf(v.Id).OffsetX);s.SetTravelHeading(new(1),0,0);I3Fixture.Exact(s);
    }
}

public class I3AccessTests
{
    [Fact]public void ExportsRealAlignmentWorkAndCompletionForIndependentRunnerAndNativeChecks()
    {
        var(s,v)=I3Fixture.Create();I3Fixture.Save(s,"prepared");I2Fixture.Step(s,I3Fixture.Start(v));I3Fixture.Save(s,"positioning");
        int bound=10;while(I3Fixture.Work(s).Batch is null&&bound-->0)s.Step();Assert.True(bound>0);
        s.AdvanceTo(s.Engine.Tick+3);I3Fixture.Save(s,"working");s.AdvanceTo(I3Fixture.Work(s).Batch!.StartedTick+24);I3Fixture.Save(s,"completed");I3Fixture.Exact(s);
        var(loadable,paver)=I3Fixture.Create(false);I2Fixture.Step(loadable,new(ActionKind.LeaveVehicle));
        StorageC1Tests.Harvest(loadable,"gravel",2);I2Fixture.Move(loadable,new(10,1));I3Fixture.Save(loadable,"loadable");
        Assert.Equal(2,FleetE1Tests.Stock(loadable,loadable.Engine.Observe(new(1)).Participant.InventoryId,"gravel"));Assert.Equal(0,FleetE1Tests.Stock(loadable,paver.CargoInventory,"gravel"));
    }
    [Fact]public void QueriesAreReadOnlyAndDoNotRevealOrControlAnotherOwnersPaver()
    {
        var(s,v)=I3Fixture.Create();string hash=s.Engine.ComputeStateHash();for(int n=0;n<5;n++)Assert.Equal(24,I3Fixture.Quote(s,v).ExpectedTicks);
        Assert.Equal(hash,s.Engine.ComputeStateHash());Assert.Equal("NotAvailable",s.Engine.QuoteRoadCell(new(2),v.Id,new(10,1),EquipmentMode.Personal).Reason);
        string id=s.Send(new(2),I3Fixture.Start(v));s.Step();Assert.Equal(ResultStatus.Rejected,s.Engine.Result(id)!.Status);
    }
    [Fact]public void OrdinaryContextExposesRoadWorkFirstInBothLanguages()
    {
        var(s,v)=I3Fixture.Create();foreach(string language in new[]{"ru","en"})
        {
            var choices=PlayContext.At(PlayerDesk.Read(s.Engine,new(1)),s.Engine.Configuration(),language,true);
            Assert.Equal("road-work:"+v.Id,choices[0].Key);Assert.True(choices[0].Enabled);Assert.Null(choices[0].Command);
            foreach(string reason in new[]{"PaverPositioning","PaverPositioningPaused","WorkSpaceOccupied","RoadAlreadyComplete"})Assert.NotEqual(reason,PlayerText.Reason(language,reason));
        }
    }
    [Fact]public void PreviewDoesNotSelectDistantOrProtectedLand()
    {
        var(s,v)=I3Fixture.Create();Assert.Equal("MovePaverToWork",I3Fixture.Quote(s,v,new(11,1)).Reason);
        Assert.Equal("ProtectedRoadCell",I3Fixture.Quote(s,v,new(4,4)).Reason);
    }
}
