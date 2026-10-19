using System.Collections;
using System.Reflection;
using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public sealed class PhysicalAutomationTests
{
    private static ScenarioDefinition Definition()
    {
        using var stream=typeof(PhysicalAutomationTests).Assembly.GetManifestResourceStream("Fixture.physical-automation.json")!;
        return JsonSerializer.Deserialize<ScenarioDefinition>(stream,SimulationJson.Options)!;
    }
    private static GameSession Recording()
    {var d=Definition();var s=Fixtures.Session(d);ScriptedScenarios.PhysicalAutomation(s,d);return s;}
    private static GameSession At(Func<GameSession,bool> reached)
    {
        var baseline=Recording();var s=Fixtures.Session(baseline.Engine.Configuration());
        s.ConfigurePlayback(baseline.CommandTape,baseline.Engine.Tick,"physical-proof");
        for(int i=0;i<300;i++){if(reached(s))return s;s.Step();}
        throw new InvalidOperationException("Required physical state was never reached");
    }
    [Fact]
    public void FullMachineWaitsForPhysicalPickupThenMovesAndExhaustsNextOwnedSite()
    {
        var s=Recording();var w=s.Engine.Inspect();var machine=Assert.Single(w.Equipment);
        Assert.Equal(new Cell(0,3),machine.Position);Assert.Null(machine.Motion);Assert.False(machine.Route!.Enabled);
        Assert.Equal(8,w.Flows.Single(f=>f.ItemId=="wood").Harvested);
        Assert.Contains(s.Engine.EventsSince(0),e=>e.Type=="EquipmentMovementArrived");
        Assert.Contains(s.Engine.EventsSince(0),e=>e.Type=="EquipmentTravelWagesPaid");
        Assert.Equal(8,w.Inventories.Single(i=>i.Id==w.Buildings.Single(b=>b.Kind==BuildingKind.Warehouse).InputId).Stock.Single().Quantity);
        Assert.All(w.Inventories,i=>Assert.Equal(0,i.ReservedCapacity));
    }
    [Theory]
    [InlineData("Loading")][InlineData("Outbound")][InlineData("Unloading")][InlineData("ReturningEmpty")][InlineData("EquipmentMotion")]
    public void DirectLoadAndReplayMatchAtEveryRemainingTick(string state)
    {
        var s=At(x=>state=="EquipmentMotion"?x.Engine.Inspect().Equipment.Any(e=>e.Motion is not null):x.Engine.Inspect().Logistics.Any(l=>l.Trip?.State==state));
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));
        long end=Recording().Engine.Tick;
        while(s.Engine.Tick<end){s.Step();loaded.Step();Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());}
    }
    [Fact]
    public void DisablingOrEditingRouteNeverDeletesStartedCargoOrItsReservations()
    {
        var s=At(x=>x.Engine.Inspect().Logistics.Any(l=>l.Trip is {Freight.Loaded:>0,State:"Loading"}));
        var truck=s.Engine.Inspect().Logistics.Single();var original=truck.Trip!;
        var source=original.Freight!.GoodsSource;var destination=original.Freight.Destination;
        s.Send(new(1),new(ActionKind.SetTransportRoute,Logistics:truck.Id,Source:source,Destination:destination,ItemId:"wood",Enabled:false));s.Step();
        Assert.Equal(original.Id,s.Engine.Inspect().Logistics.Single().Trip!.Id);
        for(int i=0;i<150 && s.Engine.Inspect().Logistics.Single().Trip is not null;i++)s.Step();
        var final=s.Engine.Inspect().Logistics.Single();Assert.Null(final.Trip);
        Assert.Equal(original.Quantity,s.Engine.Observe(new(1)).OwnInventories.Single(i=>i.Id==destination).Stock.Single().Quantity);
    }
    [Fact]
    public void LoadingUsesCurrentNumericCrewAndDoesNotWorkOrPayAtZero()
    {
        var s=At(x=>x.Engine.Inspect().Logistics.Any(l=>l.Trip is {State:"Loading",Freight.Loaded:>0}));
        var t=s.Engine.Inspect().Logistics.Single().Trip!;ShiftCrew(s,0);
        // Slow labor so this controlled adverse crew change persists for the observed interval.
        var before=t.Freight!.Loaded;long paid=t.WagesPaidMinor;
        s.Step();var next=s.Engine.Inspect().Logistics.Single().Trip!;
        Assert.Equal(before,next.Freight!.Loaded);Assert.Equal(paid,next.WagesPaidMinor);
        ShiftCrew(s,1);s.Step();Assert.True(s.Engine.Inspect().Logistics.Single().Trip!.Freight!.Loaded>before);
        s.Engine.ValidateInvariants();
    }
    private static void ShiftCrew(GameSession s,int count)
    {
        var dictionary=(IDictionary)typeof(SimulationEngine).GetField("_logistics",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(s.Engine)!;
        var truck=dictionary.Values.Cast<object>().Single();var group=truck.GetType().GetProperty("Group")!.GetValue(truck)!;
        var headcount=group.GetType().GetProperty("Headcount")!;int previous=(int)headcount.GetValue(group)!;headcount.SetValue(group,count);
        var unemployed=typeof(SimulationEngine).GetField("_unemployed",BindingFlags.Instance|BindingFlags.NonPublic)!;
        unemployed.SetValue(s.Engine,checked((long)unemployed.GetValue(s.Engine)!+previous-count));
    }
    private static RecordedCommand[] PrefixTape(bool repeat=false) => Recording().CommandTape
        .Where(c=>c.Command.Action.Kind!=ActionKind.SetTransportRoute || c.Command.Action.Enabled)
        .Where(c=>c.Command.Action.Kind!=ActionKind.SetEquipmentMode || c.Command.Action.Mode!=EquipmentMode.Off)
        .Select(c=>repeat && c.Command.Action.Kind==ActionKind.SetExtractionRoute
            ? c with {Command=c.Command with {Action=c.Command.Action with {Stops=[new(0,0),new(0,3)],Repeat=true}}}:c).ToArray();
    [Fact]
    public void RepeatingRouteRevisitsRegeneratedSitesAndEventuallyWaitsForDestinationSpace()
    {
        var d=Definition();d=d with {ResourceNodes=d.ResourceNodes.Select(n=>n.Rules is {Public:false}
            ?n with {Rules=n.Rules with {RegenerationTicks=60,RegenerationQuantity=n.Position==new Cell(0,0)?4:0}}:n).ToArray()};
        d=d with {BuildingTypes=d.BuildingTypes!.Select(t=>t.Kind==BuildingKind.Warehouse?t with {Levels=t.Levels.Select(l=>l with {InputCapacity=12}).ToArray()}:t).ToArray()};
        var tape=PrefixTape(true);var s=Fixtures.Session(d);s.ConfigurePlayback(tape,450,"repeat-route");s.AdvanceTo(80);
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(450);loaded.AdvanceTo(450);
        Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());
        Assert.True(s.Engine.EventsSince(0).Count(e=>e.Type=="EquipmentMovementArrived")>=2);
        var warehouse=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Kind==BuildingKind.Warehouse);
        Assert.Equal(12,Fixtures.Count(s,warehouse.InputId,"wood"));Assert.Equal("NoDestinationSpace",s.Engine.Observe(new(1)).OwnLogistics.Single().ReasonCode);
        Assert.True(s.Engine.Observe(new(1)).OwnEquipment.Single().Route!.Enabled);
    }
    [Theory]
    [InlineData(0)][InlineData(2)]
    public void NoMoneyOrCrewNeverCreatesFreeExtractionMovementOrPayroll(long population)
    {
        var s=Unfunded(population);s.AdvanceTo(60);
        var machine=s.Engine.Observe(new(1)).OwnEquipment.Single();Assert.Null(machine.Motion);Assert.Equal(0,machine.CompletedCycles);
        Assert.Equal(0,s.Engine.Observe(new(1)).Account.AvailableMinor);Assert.Equal(0,s.Engine.Inspect().Accounts.Single(a=>a.Kind=="WagesSink").BalanceMinor);
        Assert.All(s.Engine.ResourceSites().Where(r=>r.Owner==new ParticipantId(1)),r=>Assert.Equal(4,r.Stock));
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(65);loaded.AdvanceTo(65);Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());
    }
    [Fact]
    public void PublicManualBootstrapFundsAnExistingUnfundedAutomaticRouteWithoutLlm()
    {
        var s=Unfunded(2);s.AdvanceTo(20);
        LandContentFixtures.Move(s,new(8,4));string id=Fixtures.Command(s,new(ActionKind.Harvest,ItemId:"wood",Quantity:1));Assert.Equal("Accepted",s.Engine.Result(id)!.ReasonCode);Fixtures.UntilIdle(s);
        LandContentFixtures.Move(s,new(8,0));id=Fixtures.Command(s,new(ActionKind.SellAvailableToCapital,OfferId:1,ItemId:"wood",Quantity:1));Assert.Equal("Accepted",s.Engine.Result(id)!.ReasonCode);Fixtures.UntilIdle(s);
        Assert.True(s.Engine.Observe(new(1)).Account.AvailableMinor>0);s.AdvanceTo(180);
        Assert.True(s.Engine.Observe(new(1)).OwnLogistics.Single().CompletedTrips>0);Assert.False(s.HasControllers);
        s.Engine.ValidateInvariants();
    }
    [Fact]
    public void ExtractionResumesIntoFreedBufferWhileTruckReturnsIndependently()
    {
        var d=Definition();d=d with {ResourceNodes=d.ResourceNodes.Select(n=>n.Position==new Cell(0,0)
            ?n with {Rules=n.Rules! with {Capacity=20}}:n).ToArray()};
        var s=Fixtures.Session(d);s.ConfigurePlayback(PrefixTape(),150,"independent-extraction");
        bool resumed=false;
        for(int i=0;i<150;i++)
        {
            s.Step();var o=s.Engine.Observe(new(1));
            if(o.OwnLogistics.SingleOrDefault()?.Trip?.State=="ReturningEmpty" && o.OwnEquipment.Single().CompletedCycles>2){resumed=true;break;}
        }
        Assert.True(resumed);s.Engine.ValidateInvariants();
    }
    [Fact]
    public void CustomInputRouteHonorsIndependentInboundStopAndResumeWithoutCancellingOutboundCargo()
    {
        var s=Fixtures.Session(Definition());s.ConfigurePlayback(PrefixTape(),120,"custom-input");
        while(!s.Engine.Inspect().Logistics.Any(l=>l.Trip?.State=="Outbound"))s.Step();
        var o=s.Engine.Observe(new(1));var truck=o.OwnLogistics.Single();var factory=o.OwnEnterprises.Single();
        void Execute(GameAction action)=>Assert.Equal("Accepted",s.Engine.Result(Fixtures.Command(s,action))!.ReasonCode);
        Execute(new(ActionKind.SetTransportRoute,Logistics:truck.Id,Source:o.OwnEquipment.Single().StorageId,Destination:factory.InputId,ItemId:"wood",Enabled:true));
        Execute(new(ActionKind.SetLogisticsEnabled,Logistics:truck.Id,Direction:LogisticsDirection.Inbound,Enabled:false));
        s.AdvanceTo(60);Assert.Equal(1,s.Engine.Observe(new(1)).OwnLogistics.Single().CompletedTrips);Assert.Equal(0,Fixtures.Count(s,factory.InputId,"wood"));
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));
        foreach(var session in new[]{s,loaded}){session.Send(new(1),new(ActionKind.SetLogisticsEnabled,Logistics:truck.Id,Direction:LogisticsDirection.Inbound,Enabled:true));session.Step();}
        s.AdvanceTo(105);loaded.AdvanceTo(105);Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());
        Assert.Equal(4,Fixtures.Count(s,factory.InputId,"wood"));Assert.Equal(2,s.Engine.Observe(new(1)).OwnLogistics.Single().CompletedTrips);
    }
    private static GameSession Unfunded(long population)
    {
        var d=Definition();d=d with {Population=population,Participants=d.Participants.Select(p=>p.Id==1?p with {CashMinor=2}:p).ToArray()};
        var s=Fixtures.Session(d);
        void Execute(GameAction action)=>Assert.Equal("Accepted",s.Engine.Result(Fixtures.Command(s,action))!.ReasonCode);
        Execute(new(ActionKind.BuyEquipment,EquipmentTypeId:"wood-extractor",Target:new(0,0)));
        var machine=s.Engine.Observe(new(1)).OwnEquipment.Single();
        Execute(new(ActionKind.SetExtractionRoute,Equipment:machine.Id,Stops:[new(0,3)],Enabled:true));
        var factory=s.Engine.Observe(new(1)).OwnEnterprises.Single();LandContentFixtures.Move(s,new(2,1));
        Execute(new(ActionKind.BuyLogistics,Enterprise:factory.Id));var truck=s.Engine.Observe(new(1)).OwnLogistics.Single();
        var warehouse=s.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Kind==BuildingKind.Warehouse);
        Execute(new(ActionKind.SetTransportRoute,Logistics:truck.Id,Source:machine.StorageId,Destination:warehouse.InputId,ItemId:"wood",Enabled:true));
        Execute(new(ActionKind.SetEquipmentMode,Equipment:machine.Id,Mode:EquipmentMode.Automatic));return s;
    }
    [Fact]
    public void ForeignMachineAndSourceNeverBecomeAvailableToAnotherParticipant()
    {
        var s=At(x=>x.Engine.Inspect().Logistics.Any());var machine=s.Engine.Inspect().Equipment.Single();var truck=s.Engine.Inspect().Logistics.Single();
        string id=Fixtures.Command(s,new(ActionKind.MoveEquipment,Equipment:machine.Id,Target:new(0,3)),2);
        Assert.Equal("NotAvailable",s.Engine.Result(id)!.ReasonCode);
        id=Fixtures.Command(s,new(ActionKind.SetTransportRoute,Logistics:truck.Id,Source:machine.StorageId,Destination:new(999999),ItemId:"wood",Enabled:true),2);
        Assert.Equal("NotAvailable",s.Engine.Result(id)!.ReasonCode);Assert.Empty(s.Engine.ObserveController(new(2)).Equipment);
    }
}
