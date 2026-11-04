using Simulation.Core;
using Simulation.Hosting;
using System.IO.Compression;
using System.Text.Json;
using Xunit;

namespace Simulation.Tests;

internal static class I3MapWork
{
    internal static void Check(string key,Cell target)
    {
        var d=OrdinaryNewGame.Resolve(MapDesignTests.Template(key),NpcEconomyG1Tests.Profile(),new(3),vnextBalance:MapDesignTests.Balance()).Scenario;
        // Explicit diagnostic funding and one staffed factory. Real designed geography,
        // purchase price, production, cargo transfer, I1/I2 and road rules remain intact.
        // This test does not establish natural earning or a live NPC economy.
        var anchor=d.Layout!.IndustrialLots.First().Cells[0];
        d=d with{Participants=[..d.Participants.Select(p=>p with{CashMinor=10_000_000})],
            Plots=[..d.Plots!.Select(p=>p.Position==anchor?p with{OwnerId=1}:p)],
            Buildings=[new(8,1,anchor,"materials",Staff:5)],Elections=d.Elections! with{PeriodTicks=20000}};
        var session=new GameSession(new(d));var paver=FleetE1Tests.Buy(session,"trakt_d1");
        var source=d.ResourceNodes.First(n=>n.ItemId=="stone"&&n.Rules!.Public).Position;
        StorageC1Tests.Move(session,source);I2Fixture.Step(session,new(ActionKind.Harvest,ItemId:"stone",Quantity:3));StorageC1Tests.Idle(session);
        var factory=session.Engine.Observe(new(1)).OwnBuildings.Single();StorageC1Tests.Put(session,factory.InputId,"stone",3);
        I2Fixture.Step(session,new(ActionKind.SetProductionEnabled,Enterprise:new(8),Enabled:true));int budget=120;
        while(FleetE1Tests.Stock(session,factory.OutputId!.Value,"gravel")<2&&budget-->0)session.Step();Assert.True(budget>0);
        I2Fixture.Step(session,new(ActionKind.SetProductionEnabled,Enterprise:new(8),Enabled:false));
        StorageC1Tests.Move(session,factory.Position);I2Fixture.Step(session,new(ActionKind.Transfer,Source:factory.OutputId.Value,Destination:StorageC1Tests.Carrier(session),ItemId:"gravel",Quantity:2));StorageC1Tests.Idle(session);
        StorageC1Tests.Put(session,paver.CargoInventory,"gravel",2);FleetE1Tests.Board(session,paver);StorageC1Tests.Move(session,target);
        var quote=session.Engine.QuoteRoadCell(new(1),paver.Id,target,EquipmentMode.Personal);Assert.Equal(0,quote.CurrentLevel);Assert.True(quote.CanStart,quote.Reason);Assert.Equal(16,quote.ExpectedTicks);
        long cash=session.Engine.Observe(new(1)).Account.BalanceMinor;
        I2Fixture.Step(session,new(ActionKind.StartRoadCell,VehicleId:paver.Id,Target:target,EquipmentLevel:1));budget=10;
        while(session.Engine.InspectRoads()!.Works.Single().Batch is null&&budget-->0)session.Step();Assert.True(budget>0);
        var batch=session.Engine.InspectRoads()!.Works.Single().Batch!;Assert.All(batch.Bypass,c=>Assert.Contains(c,d.Layout.RoadCorridorCells));
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(session));
        while(session.Engine.Tick<batch.StartedTick+16){session.Step();copy.Step();Assert.Equal(session.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());}
        Assert.Equal(cash-200,session.Engine.Observe(new(1)).Account.BalanceMinor);Assert.Equal(0,FleetE1Tests.Stock(session,paver.CargoInventory,"gravel"));
        var road=session.Engine.InspectRoads()!.Cells.Single(c=>c.Cell==target);Assert.Equal(1,road.Level);Assert.Equal(new ParticipantId(1),Assert.Single(road.Contributions).Builder);
        Assert.Equal(500,session.Engine.VehicleTravelOf(paver.Id).OffsetX);Assert.Equal(500,session.Engine.VehicleTravelOf(paver.Id).OffsetZ);
        StorageC1Tests.Move(session,new(target.X+(key=="compact_valley"?2:1),target.Z));
        if(key=="compact_valley")
        {
            I2Fixture.Step(session,new(ActionKind.LeaveVehicle));var car=FleetE1Tests.Buy(session,"sputnik20",2);
            StorageC1Tests.Move(session,session.Engine.FleetOf(new(2))!.Vehicles.Single(v=>v.Vehicle.Id==car.Id).Position,2);
            I2Fixture.Step(session,new(ActionKind.BoardVehicle,VehicleId:car.Id),2);
            var neighbor=d.Layout.InitialCityDirtCells.First(c=>Math.Abs(c.X-target.X)+Math.Abs(c.Z-target.Z)==1);
            StorageC1Tests.Move(session,neighbor,2);StorageC1Tests.Move(session,target,2);
            Assert.Contains(session.Engine.EventsSince(0),e=>e.Type=="TrafficCellEntered"&&e.EntityId==car.Id&&e.ReasonCode.EndsWith(">"+target.X+","+target.Z,StringComparison.Ordinal)&&e.Quantity==20);
            long Receivable()=>session.Engine.InspectBilling()!.Accruals.Where(a=>a.Payer.Value==2&&a.Recipient.Value==1).Sum(a=>a.Units)+
                session.Engine.InspectBilling()!.Invoices.Where(i=>i.Payer.Value==2).SelectMany(i=>i.Claims).Where(c=>c.Recipient.Value==1).Sum(c=>c.PrincipalMinor)*SimulationEngine.RoadAccrualScale;
            long before=Receivable();StorageC1Tests.Move(session,neighbor,2);Assert.True(Receivable()>before,"The real new dirt cell must accrue the builder's road income");
        }
        I3Fixture.Save(session,"map-"+key);
        if(key=="large_districts")
        {
            // Compare the entire independently regenerated trajectory before running
            // its full replay in a separate bounded process, as in the H3 gate.
            var frozen=FrozenDistrict();Assert.Equal(frozen.Engine.Tick,session.Engine.Tick);
            Assert.Equal(frozen.Engine.ComputeStateHash(),session.Engine.ComputeStateHash());
            Assert.Equal(JsonSerializer.Serialize(frozen.CommandTape,SimulationJson.Options),JsonSerializer.Serialize(session.CommandTape,SimulationJson.Options));
            return;
        }
        var replay=GameSession.Replay(d,session.CommandTape,session.Engine.Tick);Assert.Equal(session.Engine.ComputeStateHash(),replay.Engine.ComputeStateHash());
    }
    internal static GameSession FrozenDistrict()
    {
        using var data=typeof(I3MapWork).Assembly.GetManifestResourceStream("Fixture.i3-district-completed.json.gz")!;
        using var zip=new GZipStream(data,CompressionMode.Decompress);using var reader=new StreamReader(zip);
        return CheckpointStore.Decode(reader.ReadToEnd());
    }
}
public class I3CompactMapTests{[Fact]public void RealProductionTransferAndPavingWithI2()=>I3MapWork.Check("compact_valley",new(14,5));}
public class I3RiverMapTests{[Fact]public void RealProductionTransferAndPavingWithI2()=>I3MapWork.Check("small_river",new(5,5));}
public class I3DistrictMapTests{[Fact]public void RealProductionTransferAndPavingWithI2()=>I3MapWork.Check("large_districts",new(5,5));}
public class I3DistrictReplayTests
{
    [Fact]public void EntirePaidLargeMapTrajectoryReplaysFromItsOriginalStart()
    {
        var s=I3MapWork.FrozenDistrict();var replay=GameSession.Replay(s.Engine.Configuration(),s.CommandTape,s.Engine.Tick);
        Assert.Equal(s.Engine.ComputeStateHash(),replay.Engine.ComputeStateHash());
    }
}
