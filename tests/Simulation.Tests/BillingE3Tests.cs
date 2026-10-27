using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class BillingE3Tests
{
    internal static EconomyProfile Profile()=>VNextBillingProfile.Create(MapDesignTests.Economy(),MapDesignTests.Balance());
    internal static ScenarioDefinition Prepared()
    {
        var s=RoadE2Tests.Prepared();return s with{Id="e3-prepared",Economy=Profile().World.Rules,Layout=s.Layout! with{TurnCells=s.Layout!.RoadCorridorCells}};
    }
    internal static void Step(GameSession s,GameAction a,long actor=1,bool accepted=true)
    {string id=s.Send(new(actor),a);s.Step();var result=s.Engine.Result(id)!;Assert.True(result.Status==(accepted?ResultStatus.Accepted:ResultStatus.Rejected),a.Kind+": "+result.ReasonCode);}
    internal static OwnedVehicle Car(GameSession s,string model="sputnik20",long actor=1)
    {
        var v=FleetE1Tests.Buy(s,model,actor);StorageC1Tests.Move(s,s.Engine.FleetOf(new(actor))!.Vehicles.Single(x=>x.Vehicle.Id==v.Id).Position,actor);
        Step(s,new(ActionKind.BoardVehicle,VehicleId:v.Id),actor);StorageC1Tests.Move(s,new(6,1),actor);return v;
    }
    internal static long TotalUnits(GameSession s,long actor=1)=>s.Engine.InspectBilling()!.Accruals.Where(a=>a.Payer.Value==actor).Sum(a=>a.Units)+
        s.Engine.InspectBilling()!.Invoices.Where(i=>i.Payer.Value==actor).Sum(i=>i.PrincipalMinor)*SimulationEngine.RoadAccrualScale;
    internal static RoadInvoice Invoice(GameSession s)
    {
        Step(s,new(ActionKind.SetAutoPay,Enabled:false));Car(s);StorageC1Tests.Move(s,new(16,1));
        s.AdvanceTo((s.Engine.Tick/120+1)*120);return s.Engine.InspectBilling()!.Invoices.First(i=>i.Payer.Value==1);
    }
    internal static void Exact(GameSession s,string name)
    {
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());
        for(int i=0;i<3;i++){s.Step();copy.Step();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());}
        var replay=GameSession.Replay(s.Engine.Configuration(),s.CommandTape,s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),replay.Engine.ComputeStateHash());
        if(Environment.GetEnvironmentVariable("DELSK_E3_EVIDENCE") is not {} output)return;
        Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,name+".json"),JsonSerializer.Serialize(s.Engine.Configuration(),SimulationJson.Options));
        File.WriteAllText(Path.Combine(output,name+".commands.jsonl"),string.Join('\n',s.CommandTape.Select(c=>JsonSerializer.Serialize(c,SimulationJson.Options)))+"\n");
        File.WriteAllText(Path.Combine(output,name+".tick"),s.Engine.Tick.ToString());CheckpointStore.Save(s,Path.Combine(output,name+".save.json"));
    }
    [Fact]public void NewProfileEnablesOnlyE3AndBothMapsPersistResolvedRules()
    {
        var p=Profile();Assert.Null(RoadE2Tests.Profile().World.Rules.Billing);
        Assert.Equal(p.Id,EconomyProfile.Read(Encoding.UTF8.GetString(VNextSupportedProfile.Serialize(p))).Id);
        Assert.Equal(new[]{120,1000,1000},new[]{p.World.Rules.Billing!.InvoicePeriod,p.World.Rules.Billing.GraceTicks,p.World.Rules.Billing.PenaltyPeriod});
        foreach(string map in new[]{"small_river","large_districts"})
        {
            var s=new GameSession(new(OrdinaryNewGame.Resolve(MapDesignTests.Template(map),p,new(3),vnextBalance:MapDesignTests.Balance()).Scenario));s.Step();
            Assert.Equal(s.Engine.ComputeStateHash(),CheckpointStore.Decode(CheckpointStore.Encode(s)).Engine.ComputeStateHash());
        }
        Assert.Equal(ActionReach.Remote,ActionPolicy.Reach(ActionKind.PayInvoice));
    }
    [Theory][InlineData("sputnik20",1)][InlineData("muravey70",1)][InlineData("eger70",1)][InlineData("vector200",2)][InlineData("trakt_d1",2)]
    public void ActualDistanceChargesEveryModelButParkingAndFootTravelAreFree(string model,int factor)
    {
        var s=new GameSession(new(Prepared()));Step(s,new(ActionKind.SetAutoPay,Enabled:false));var v=Car(s,model);long before=TotalUnits(s);
        StorageC1Tests.Move(s,new(16,1));Assert.Equal(10L*factor*SimulationEngine.RoadAccrualScale,TotalUnits(s)-before);
        long parked=TotalUnits(s);s.AdvanceTo(s.Engine.Tick+5);Assert.Equal(parked,TotalUnits(s));
        Step(s,new(ActionKind.LeaveVehicle));StorageC1Tests.Move(s,new(18,1));Assert.Equal(parked,TotalUnits(s));
        Exact(s,"distance-"+model);
    }
    [Fact]public void FractionalManualTravelSavesAcrossCellEntryAndChargesNoMoneyPerFrame()
    {
        var s=new GameSession(new(Prepared()));Step(s,new(ActionKind.SetAutoPay,Enabled:false));var car=Car(s);long before=TotalUnits(s);int ledger=s.Engine.Ledger().Length;
        s.SetTravelHeading(new(1),-1,0);s.AdvanceTravelQuantum();Assert.Equal(SimulationEngine.RoadAccrualScale/40,TotalUnits(s)-before);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(s));
        for(int n=0;n<47;n++){s.AdvanceTravelQuantum();copy.AdvanceTravelQuantum();Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());}
        // Stop immediately after a centre crossing: both the physical witness and fractional accrual survive.
        s.SetTravelHeading(new(1),0,0);s.CompleteTravelTick();copy=CheckpointStore.Decode(CheckpointStore.Encode(s));
        Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.Equal(ledger,s.Engine.Ledger().Length);Exact(s,"fractional");
    }
    [Fact]public void InvoiceIsUnseenUntilViewedAndPaymentDoesNotMarkItViewedOrPayTwice()
    {
        var s=new GameSession(new(Prepared()));var invoice=Invoice(s);Assert.False(invoice.Viewed);Assert.Equal(0,s.Engine.Ledger().Count(l=>l.Reason.StartsWith("RoadPrincipal:")));
        long balance=s.Engine.Observe(new(1)).Account.BalanceMinor;Step(s,new(ActionKind.PayInvoice,OfferId:invoice.Id));
        var paid=s.Engine.InspectBilling()!.Invoices.Single(i=>i.Id==invoice.Id);Assert.Equal(0,paid.Remaining);Assert.False(paid.Viewed);
        Assert.Equal(balance-invoice.PrincipalMinor,s.Engine.Observe(new(1)).Account.BalanceMinor);
        var retry=CheckpointStore.Decode(CheckpointStore.Encode(s));string retryHash=retry.Engine.ComputeStateHash();
        Assert.Equal(ResultStatus.Accepted,retry.Engine.Enqueue(new(1),s.CommandTape.Last().Command)!.Status);Assert.Equal(retryHash,retry.Engine.ComputeStateHash());
        Step(s,new(ActionKind.PayInvoice,OfferId:invoice.Id),accepted:false);Assert.Equal(balance-invoice.PrincipalMinor,s.Engine.Observe(new(1)).Account.BalanceMinor);
        Step(s,new(ActionKind.ViewInvoice,OfferId:invoice.Id));Assert.True(s.Engine.InspectBilling()!.Invoices.Single(i=>i.Id==invoice.Id).Viewed);Exact(s,"paid");
    }



    [Fact]public void PersonalAccountNeverShowsForeignClosedOperationsOrBillsAndRejectsForeignId()
    {
        var s=new GameSession(new(Prepared()));var invoice=Invoice(s);var own=s.Engine.AccountOf(new(1));var foreign=s.Engine.AccountOf(new(2));
        Assert.Contains(own.Invoices,i=>i.Id==invoice.Id);Assert.Empty(foreign.Invoices);Assert.Empty(foreign.Operations);
        Step(s,new(ActionKind.ViewInvoice,OfferId:invoice.Id),2,false);Step(s,new(ActionKind.PayInvoice,OfferId:invoice.Id),2,false);
        Assert.DoesNotContain(s.Engine.PublicEventsSince(0),e=>e.Type.StartsWith("Billing"));Assert.DoesNotContain(s.Engine.PublicEventsSince(0),e=>e.Type=="CommandRejected"&&e.ActorId==new ParticipantId(2));
        Assert.All(s.Engine.AccountOf(new(1)).Operations,o=>Assert.NotEqual(0,o.AmountMinor));Exact(s,"privacy");
    }
    [Fact]public void Schema25WithoutBillingRetainsExactOldStateAndCannotPretendToContainE3()
    {
        var s=new GameSession(new(RoadE2Tests.Prepared()));s.Step();var json=JsonNode.Parse(s.Engine.CreateCheckpointJson())!;json["schemaVersion"]=25;
        Assert.Equal(s.Engine.ComputeStateHash(),SimulationEngine.FromCheckpointJson(json.ToJsonString()).ComputeStateHash());
        var e3=new GameSession(new(Prepared()));e3.Step();json=JsonNode.Parse(e3.Engine.CreateCheckpointJson())!;json["schemaVersion"]=25;
        Assert.Throws<CheckpointException>(()=>SimulationEngine.FromCheckpointJson(json.ToJsonString()));
    }
}
