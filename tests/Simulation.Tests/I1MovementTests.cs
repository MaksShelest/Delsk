using System.Collections.Immutable;
using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

// Controlled geography/material sources and one-tick road construction are diagnostic
// fixtures. Travel, model limits, payments, collision checks and the economic second
// use the real engine. No result here describes natural economic progression.
internal static class I1Fixture
{
    internal const int Distance=120;
    internal static MovementRules? Rules(int profile)=>profile==0?null:profile==1?new():MovementRules.I1;
    internal static ScenarioDefinition Definition(int profile=2,bool offroad=false)
    {
        var d=H2MovementTests.LongRoad(false);var road=Enumerable.Range(0,128).Select(x=>new Cell(x,1)).ToArray();
        return d with{Id="i1-diagnostic",MapWidth=128,MaxTicks=1000000,
            Economy=d.Economy! with{Movement=Rules(profile),Roads=d.Economy.Roads! with{Stages=[..d.Economy.Roads.Stages.Select(r=>r with{Ticks=1,Materials=[new("gravel",1)]})]}},
            Layout=d.Layout! with{RoadCenterCells=road,InitialCityDirtCells=offroad?[new(0,1)]:road,TurnCells=road,
                RoadCorridorCells=[..d.Layout.RoadCorridorCells.Concat(road).Concat(Enumerable.Range(0,128).Select(x=>new Cell(x,2))).Distinct()]}};
    }
    internal static void Step(GameSession s,GameAction action,long actor=1)
    {var id=s.Send(new(actor),action);s.Engine.Step();Assert.Equal("Accepted",s.Engine.Result(id)!.Status.ToString());}
    internal static void Move(GameSession s,Cell target,long actor=1)
    {Step(s,new(ActionKind.MoveTo,Target:target),actor);int bound=10000;while(s.Engine.Observe(new(actor)).Participant.Busy&&bound-->0)s.Engine.Step();Assert.True(bound>0);Assert.Equal(target,s.Engine.Observe(new(actor)).Participant.Position);}
    internal static OwnedVehicle Buy(GameSession s,string model,long actor=1)
    {
        Move(s,s.Engine.FleetOf(new(actor))!.Dealer,actor);var stock=s.Engine.FleetOf(new(actor))!.Stock.First(v=>v.ModelId==model);
        Step(s,new(ActionKind.BuyVehicle,VehicleId:stock.Id),actor);for(int n=0;n<5;n++)s.Engine.Step();
        return s.Engine.InspectFleet()!.Vehicles.Single(v=>v.Id==stock.Id);
    }
    internal static OwnedVehicle Car(GameSession s,string model,long actor=1,Cell? start=null)
    {var v=s.Engine.InspectFleet()!.Vehicles.FirstOrDefault(v=>v.ModelId==model&&v.Owner.Value==actor)??Buy(s,model,actor);Move(s,s.Engine.FleetOf(new(actor))!.Vehicles.Single(vv=>vv.Vehicle.Id==v.Id).Position,actor);Step(s,new(ActionKind.BoardVehicle,VehicleId:v.Id),actor);Move(s,start??new(6,1),actor);return v;}
    private static readonly Dictionary<(int,int),string> Surfaces=[];
    internal static GameSession Surface(int profile,int level)
    {
        Trace("surface-start",profile,level);
        if(Surfaces.TryGetValue((profile,level),out var saved))return CheckpointStore.Decode(saved);
        var s=new GameSession(new(Definition(profile,level==0)));
        if(level>=2)
        {
            var paver=Buy(s,"trakt_d1");
            Move(s,s.Engine.Configuration().ResourceNodes.Single(r=>r.ItemId=="gravel").Position);
            Step(s,new(ActionKind.Harvest,ItemId:"gravel",Quantity:121*(level-1)));
            while(s.Engine.Observe(new(1)).Participant.Busy)s.Engine.Step();
            int perLoad=(int)(70/s.Engine.Configuration().Items.Single(i=>i.Id=="gravel").UnitVolume)/(level-1);
            for(int begin=6;begin<=126;begin+=perLoad)
            {
                int count=Math.Min(perLoad,127-begin);
                Move(s,s.Engine.FleetOf(new(1))!.Vehicles.Single(v=>v.Vehicle.Id==paver.Id).Position);
                Step(s,new(ActionKind.Transfer,Source:s.Engine.Observe(new(1)).Participant.InventoryId,Destination:paver.CargoInventory,ItemId:"gravel",Quantity:count*(level-1)));
                int guard=10000;while(s.Engine.Observe(new(1)).Participant.Busy&&guard-->0)s.Engine.Step();Assert.True(guard>0);
                Step(s,new(ActionKind.BoardVehicle,VehicleId:paver.Id));
                for(int x=begin;x<begin+count;x++)
                {
                    Move(s,new(x,1));
                    for(int stage=2;stage<=level;stage++)
                    {
                        Step(s,RoadE2Tests.Work(paver,stage,target:new(x,1)));
                        guard=20;while(s.Engine.InspectRoads()!.Cells.Single(c=>c.Cell==new Cell(x,1)).Level<stage&&guard-->0)s.Engine.Step();Assert.True(guard>0);s.Engine.Step();
                    }
                }
                Move(s,new(begin+count-1,2));Step(s,new(ActionKind.LeaveVehicle));
            }
            Move(s,s.Engine.FleetOf(new(1))!.Vehicles.Single(v=>v.Vehicle.Id==paver.Id).Position);Step(s,new(ActionKind.BoardVehicle,VehicleId:paver.Id));Move(s,new(127,2));Step(s,new(ActionKind.LeaveVehicle));
        }
        Trace("surface-built",profile,level,s.Engine.Tick);
        saved=CheckpointStore.Encode(s);Surfaces[(profile,level)]=saved;
        return CheckpointStore.Decode(saved);
    }
    internal static void RunningOnBuiltRoad(int surface)
    {
        foreach(int profile in new[]{0,1,2})
        {
            var s=Surface(profile,surface);Move(s,new(6,1),2);Step(s,new(ActionKind.MoveTo,Target:new(126,1)),2);long start=s.Engine.Tick;
            while(s.Engine.Observe(new(2)).Participant.Busy)s.Engine.Step();
            Assert.Equal(new[]{480,192,160}[profile],s.Engine.Tick-start);
            Evidence(new{kind="running",surface,profile,distance=120,ticks=s.Engine.Tick-start,nominal=profile==2?10:20});
        }
    }
    internal static void Trace(string stage,int profile,int surface,long tick=0,string model="")
    {if(Environment.GetEnvironmentVariable("DELSK_I1_EVIDENCE") is {} dir){Directory.CreateDirectory(dir);File.AppendAllText(Path.Combine(dir,"phases.jsonl"),JsonSerializer.Serialize(new{stage,profile,surface,tick,model,utc=DateTime.UtcNow})+"\n");}}
    internal static void Evidence(object data)
    {if(Environment.GetEnvironmentVariable("DELSK_I1_EVIDENCE") is {} dir){Directory.CreateDirectory(dir);File.AppendAllText(Path.Combine(dir,"measurements.jsonl"),JsonSerializer.Serialize(data,SimulationJson.Options)+"\n");}}
}

public class I1WalkingTests
{
    [Theory][InlineData(0,600,240,200)][InlineData(1,480,192,160)]
    public void LongRouteIsExactlyThreeTimesOriginalAndOnePointTwoTimesH2(int surface,int original,int h2,int current)
    {
        int[] expected=[original,h2,current];
        for(int profile=0;profile<3;profile++)
        {
            var s=new GameSession(new(I1Fixture.Definition(profile,surface==0)));var start=new Cell(2,1);var target=new Cell(122,1);
            var id=s.Send(new(1),new(ActionKind.MoveTo,Target:target));s.Step();long tick=s.Engine.Tick;
            var o=s.Engine.ObserveController(new(1),true);long eta=profile==0?expected[profile]:MovementTiming.PersonalTicks(o,start,target);
            while(s.Engine.Observe(new(1)).Participant.Busy)s.Step();
            Assert.Equal(expected[profile],s.Engine.Tick-tick);Assert.Equal(expected[profile],eta);Assert.Equal(target,s.Engine.Observe(new(1)).Participant.Position);
            I1Fixture.Evidence(new{kind="running",surface,profile,distance=120,ticks=s.Engine.Tick-tick,nominal=profile==2?(surface==0?8:10):(surface==0?16:20)});
            RoadE2Tests.Exact(s,"i1-walk-"+surface+"-"+profile);
        }
    }
    [Theory][InlineData(false,750)][InlineData(true,600)]
    public void HumanAndNpcHaveEqualAuthoritativeManualAndRouteRates(bool offroad,int perSecond)
    {
        var d=I1Fixture.Definition(2,offroad);d=d with{Participants=[..d.Participants.Select((p,n)=>p with{Position=new(2,1+n)})],Layout=d.Layout! with{InitialCityDirtCells=offroad?[new(0,1)]:[..d.Layout.InitialCityDirtCells.Concat(Enumerable.Range(0,128).Select(x=>new Cell(x,2)))]}};
        var s=new GameSession(new(d));s.SetTravelHeading(new(1),1,0);s.SetTravelHeading(new(2),1,0);
        for(int n=0;n<40;n++)s.AdvanceTravelQuantum();
        foreach(long actor in new long[]{1,2}){var p=s.Engine.Observe(new(actor)).Participant.Position;var t=s.Engine.TravelOf(new(actor));Assert.Equal(2500+perSecond*4,p.X*1000+t.OffsetX);}
        s.SetTravelHeading(new(1),0,0);s.SetTravelHeading(new(2),0,0);RoadE2Tests.Exact(s,"i1-manual-foot-"+offroad);
    }
    [Theory][InlineData("compact_valley")][InlineData("small_river")][InlineData("large_districts")]
    public void OnlyNewOrdinaryWorldsGetMovement2AndNpcObservationCarriesSavedRules(string map)
    {
        var d=OrdinaryNewGame.Resolve(MapDesignTests.Template(map),NpcEconomyG1Tests.Profile(),new(3),vnextBalance:MapDesignTests.Balance()).Scenario;
        var s=new GameSession(new(d));Assert.Equal(MovementRules.I1,s.Engine.Configuration().Economy!.Movement);
        Assert.Equal(MovementRules.I1,s.Engine.ObserveController(new(2),true).Map.Movement);
        Assert.Equal(29,s.Engine.ActiveCheckpointSchemaVersion);Assert.Null(H2StorageTests.Frozen().Engine.Configuration().Economy!.Movement);
        s.AttachControllers(d.Participants.Skip(1).Select(p=>NpcEconomyG1Tests.Controller(p.Id,p.PublicPreferenceItem)));s.AdvanceTo(20);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));H2StorageTests.Restore(copy);s.AdvanceTo(40);copy.AdvanceTo(40);
        Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.Equal(s.ComputeControllerStateHash(),copy.ComputeControllerStateHash());
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,40).Engine.ComputeStateHash());
    }
}

public class I1VehicleMatrixTests
{
    internal static void Verify(int surface,string model,int original,int h2,int current)
    {
        int[] expected=[original,h2,current];long? charge=null;long actor=model=="trakt_d1"?1:2;
        for(int profile=0;profile<3;profile++)
        {
            bool reverse=model=="trakt_d1"&&surface>=2;var start=new Cell(reverse?126:6,1);var target=new Cell(reverse?6:126,1);
            var s=I1Fixture.Surface(profile,surface);I1Fixture.Trace("car-start",profile,surface,s.Engine.Tick,model);var car=I1Fixture.Car(s,model,actor,start);I1Fixture.Trace("car-ready",profile,surface,s.Engine.Tick,model);long before=BillingE3Tests.TotalUnits(s,actor);
            var observation=s.Engine.ObserveController(new(actor),true);
            I1Fixture.Trace("observation-ready",profile,surface,s.Engine.Tick,model);
            long eta=profile==0?expected[profile]:MovementTiming.PersonalTicks(observation,start,target);
            I1Fixture.Trace("eta-ready",profile,surface,s.Engine.Tick,model);
            I1Fixture.Step(s,new(ActionKind.MoveTo,Target:target),actor);long tick=s.Engine.Tick;
            I1Fixture.Trace("route-started",profile,surface,s.Engine.Tick,model);
            // Save while the vehicle is still moving, then compare the remaining
            // journey with its uninterrupted counterpart. The full measured route
            // has already run normally; expected arrival time is independent data.
            for(int n=0;n<expected[profile]-7;n++)s.Engine.Step();Assert.True(s.Engine.Observe(new(actor)).Participant.Busy);var copy=SimulationEngine.FromCheckpointJson(s.Engine.CreateCheckpointJson());
            int guard=2000;while(s.Engine.Observe(new(actor)).Participant.Busy&&guard-->0){s.Engine.Step();copy.Step();}Assert.True(guard>0);
            Assert.Equal(expected[profile],s.Engine.Tick-tick);Assert.Equal(expected[profile],eta);Assert.Equal(target,s.Engine.Observe(new(actor)).Participant.Position);
            Assert.Equal(s.Engine.ComputeStateHash(),copy.ComputeStateHash());
            long fee=BillingE3Tests.TotalUnits(s,actor)-before;if(charge is {} old)Assert.Equal(old,fee);charge=fee;
            int factor=model is "vector200" or "trakt_d1"?2:1;
            Assert.Equal(120L*surface*factor*SimulationEngine.RoadAccrualScale,fee);
            int nominal=surface==0?10:Math.Min(s.Engine.FleetOf(new(actor))!.Models.Single(m=>m.Id==model).Speed,new[]{0,20,40,60}[surface]);
            I1Fixture.Evidence(new{kind=model,surface,profile,distance=120,ticks=s.Engine.Tick-tick,nominal=profile==2?nominal:surface==0?8:nominal,roadUnits=fee});
        }
    }
}

public class I1OffroadVehicleTests
{
    [Theory]
    [InlineData("sputnik20",1200,480,160)]
    [InlineData("muravey70",1200,480,160)]
    [InlineData("eger70",1200,480,160)]
    [InlineData("vector200",1200,480,160)]
    [InlineData("trakt_d1",1200,480,160)]
    public void PhysicalTimeBillingAndRestore(string model,int original,int h2,int current)
        =>I1VehicleMatrixTests.Verify(0,model,original,h2,current);
}

public class I1DirtVehicleTests
{
    [Theory]
    [InlineData("sputnik20",480,192,80)]
    [InlineData("muravey70",480,192,80)]
    [InlineData("eger70",480,192,80)]
    [InlineData("vector200",480,192,80)]
    [InlineData("trakt_d1",960,384,160)]
    public void PhysicalTimeBillingAndRestore(string model,int original,int h2,int current)
        =>I1VehicleMatrixTests.Verify(1,model,original,h2,current);
}

public class I1RoadVehicleTests
{
    [Fact]public void RunningIsStillTenOnBuiltRoad()=>I1Fixture.RunningOnBuiltRoad(2);
    [Theory]
    [InlineData("sputnik20",240,96,40)]
    [InlineData("muravey70",288,113,48)]
    [InlineData("eger70",240,96,40)]
    [InlineData("vector200",240,96,40)]
    public void PhysicalTimeBillingAndRestore(string model,int original,int h2,int current)
        =>I1VehicleMatrixTests.Verify(2,model,original,h2,current);
}

public class I1HighwayVehicleTests
{
    [Fact]public void RunningIsStillTenOnBuiltRoad()=>I1Fixture.RunningOnBuiltRoad(3);
    [Theory]
    [InlineData("sputnik20",168,65,27)]
    [InlineData("muravey70",288,113,48)]
    [InlineData("eger70",192,74,31)]
    [InlineData("vector200",216,86,36)]
    public void PhysicalTimeBillingAndRestore(string model,int original,int h2,int current)
        =>I1VehicleMatrixTests.Verify(3,model,original,h2,current);
}

public class I1HighSpeedSafetyTests
{
    [Fact]public void FourPointFiveCellsPerSecondCarryTimeReserveEachCellAndStopBeforeAnOccupiedCell()
    {
        var s=I1Fixture.Surface(2,3);var paver=s.Engine.InspectFleet()!.Vehicles.Single(v=>v.ModelId=="trakt_d1");
        var car=I1Fixture.Car(s,"sputnik20",2);
        I1Fixture.Move(s,s.Engine.FleetOf(new(1))!.Vehicles.Single().Position);I1Fixture.Step(s,new(ActionKind.BoardVehicle,VehicleId:paver.Id));I1Fixture.Move(s,new(11,1));I1Fixture.Step(s,new(ActionKind.LeaveVehicle));
        I1Fixture.Move(s,new(8,1),2);
        if(s.Engine.InspectFleet()!.Vehicles.Single(v=>v.Id==car.Id).FacingX==-1)I1Fixture.Step(s,new(ActionKind.TurnVehicle,VehicleId:car.Id),2);
        s.SetTravelHeading(new(2),1,0);s.AdvanceTravelQuantum();var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));
        for(int n=0;n<29;n++)
        {
            s.AdvanceTravelQuantum();copy.AdvanceTravelQuantum();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
            Assert.InRange(s.Engine.Observe(new(2)).Participant.Position.X,8,10);
            Assert.Equal(new Cell(11,1),s.Engine.FleetOf(new(1))!.Vehicles.Single().Position);
        }
        s.SetTravelHeading(new(2),0,0);s.CompleteTravelTick();
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(s.Engine.Configuration(),s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());
    }
    [Fact]public void HighSpeedRouteAcrossOffroadAndHighwayHasExactEtaAndDistanceCharge()
    {
        var s=I1Fixture.Surface(2,3);var car=I1Fixture.Car(s,"sputnik20",2);I1Fixture.Move(s,new(6,2),2);
        I1Fixture.Step(s,new(ActionKind.TurnVehicle,VehicleId:car.Id),2);
        var target=new Cell(126,2);long eta=MovementTiming.PersonalTicks(s.Engine.ObserveController(new(2),true),new(6,2),target);
        long before=BillingE3Tests.TotalUnits(s,2);I1Fixture.Step(s,new(ActionKind.MoveTo,Target:target),2);long start=s.Engine.Tick;
        while(s.Engine.Observe(new(2)).Participant.Busy)s.Engine.Step();
        Assert.Equal(30,s.Engine.Tick-start);Assert.Equal(30,eta);
        Assert.Equal(363L*SimulationEngine.RoadAccrualScale,BillingE3Tests.TotalUnits(s,2)-before);
    }
    [Fact]public void ExactNominalHighwaySpeedSurvivesManualCentreCrossingsAndMidQuantumSave()
    {
        var s=I1Fixture.Surface(2,3);var car=I1Fixture.Car(s,"sputnik20",2);I1Fixture.Move(s,new(8,1),2);
        if(s.Engine.InspectFleet()!.Vehicles.Single(v=>v.Id==car.Id).FacingX==-1)I1Fixture.Step(s,new(ActionKind.TurnVehicle,VehicleId:car.Id),2);
        long before=BillingE3Tests.TotalUnits(s,2);long tick=s.Engine.Tick;s.SetTravelHeading(new(2),1,0);
        for(int n=0;n<3;n++)s.AdvanceTravelQuantum();var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));
        string? native=Environment.GetEnvironmentVariable("DELSK_I1_EVIDENCE");
        if(native is not null){Directory.CreateDirectory(native);CheckpointStore.Save(s,Path.Combine(native,"native-highway.save.json"));}
        for(int n=0;n<7;n++){s.AdvanceTravelQuantum();copy.AdvanceTravelQuantum();}
        Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.Equal(tick+1,s.Engine.Tick);
        if(native is not null)File.WriteAllText(Path.Combine(native,"native-highway.expected.json"),JsonSerializer.Serialize(new{vehicle=car.Id,tick=s.Engine.Tick,hash=s.Engine.ComputeStateHash()},SimulationJson.Options));
        Assert.Equal(new Cell(12,1),s.Engine.Observe(new(2)).Participant.Position);
        Assert.Equal(400,s.Engine.InspectRoads()!.Traffic.Single(t=>t.VehicleId==car.Id).Progress);
        Assert.Equal(27L*SimulationEngine.RoadAccrualScale/2,BillingE3Tests.TotalUnits(s,2)-before);
        s.SetTravelHeading(new(2),0,0);s.CompleteTravelTick();Assert.Equal(new Cell(13,1),s.Engine.Observe(new(2)).Participant.Position);
        // Five highway cells followed by an off-road exit finish in 22/9s.
        // At t=2 the exit has advanced 533+1/3 of its 800 distance units.
        // Check the genuinely nonzero saved fraction, not only a straight-road save.
        before=BillingE3Tests.TotalUnits(s,2);
        s.Send(new(2),new(ActionKind.MoveTo,Target:new(18,2)));s.Step();tick=s.Engine.Tick;
        s.Step();s.Step();var exit=s.Engine.InspectRoads()!.Traffic.Single(t=>t.VehicleId==car.Id);
        Assert.Equal(533,exit.Progress);Assert.Equal(198900,exit.ProgressFraction);
        copy=CheckpointStore.Decode(CheckpointStore.Encode(s));s.Step();copy.Step();
        Assert.Equal(3,s.Engine.Tick-tick);Assert.Equal(new Cell(18,2),s.Engine.Observe(new(2)).Participant.Position);
        Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        Assert.Equal(18L*SimulationEngine.RoadAccrualScale,BillingE3Tests.TotalUnits(s,2)-before);
        RoadE2Tests.Exact(s,"i1-highway-exit-fraction");
    }
}

public class I1RoadTractorTests
{[Fact]public void TractRetainsItsModelLimit()=>I1VehicleMatrixTests.Verify(2,"trakt_d1",960,384,160);}
public class I1HighwayTractorTests
{[Fact]public void TractRetainsItsModelLimit()=>I1VehicleMatrixTests.Verify(3,"trakt_d1",960,384,160);}
