using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public sealed class PhysicalResourcesTests
{
    private static ScenarioDefinition Definition(ResourceSiteRules? rules=null,long volume=1,long productivity=2) => Fixtures.Cycle() with
    {
        MapWidth=24, MapHeight=4, Enterprises=[Fixtures.Cycle().Enterprises[0] with {Position=new(0,0)}],
        Items=[new("wood",volume,500),new("plank",1,3000)],
        ResourceNodes=[new("wood",new(0,0),rules??new(false,Capacity:20,RegenerationQuantity:2,RegenerationTicks:10,InitialOwner:1)),
            new("wood",new(0,1),new(true,Capacity:3,RegenerationQuantity:1,RegenerationTicks:10))],
        Economy=new(Handling:new(productivity,productivity))
    };
    private static string Do(GameSession s,GameAction a,long actor=1)=>s.Engine.Result(Fixtures.Command(s,a,actor))!.ReasonCode;
    private static GameAction Harvest(long q)=>new(ActionKind.Harvest,ItemId:"wood",Quantity:q);
    [Theory]
    [InlineData(1,2,5,2,0)]
    [InlineData(3,2,5,0,2)]
    [InlineData(3,7,5,2,1)]
    public void HandlingUsesVolumeAndPreservesUnfinishedUnit(long volume,long rate,long quantity,long completed,long remainder)
    {
        var s=Fixtures.Session(Definition(volume:volume,productivity:rate)); Assert.Equal("Accepted",Do(s,Harvest(quantity)));Fixtures.UntilIdle(s);
        var o=s.Engine.Observe(new(1));
        Assert.Equal("Accepted",Do(s,new(ActionKind.Transfer,Source:o.Participant.InventoryId,Destination:o.OwnEnterprises[0].InputId,ItemId:"wood",Quantity:quantity)));
        var process=Assert.Single(s.Engine.TransfersOf(new(1)));
        Assert.Equal(completed,process.Completed);Assert.Equal(remainder,process.WorkRemainder);
        Assert.Equal(completed,Fixtures.Count(s,o.OwnEnterprises[0].InputId,"wood"));
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));
        while(s.Engine.Observe(new(1)).Participant.Busy){s.Step();loaded.Step();Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());}
        Assert.Equal(quantity,Fixtures.Count(s,o.OwnEnterprises[0].InputId,"wood"));Assert.Empty(s.Engine.TransfersOf(new(1)));
        var replay=GameSession.Replay(s.Engine.Configuration(),s.CommandTape,s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),replay.Engine.ComputeStateHash());
    }
    [Fact]
    public void StopKeepsDeliveredUnitsAndReleasesOnlyUnusedHolds()
    {
        var s=Fixtures.Session(Definition());Do(s,Harvest(5));Fixtures.UntilIdle(s);var o=s.Engine.Observe(new(1));
        Do(s,new(ActionKind.Transfer,Source:o.Participant.InventoryId,Destination:o.OwnEnterprises[0].InputId,ItemId:"wood",Quantity:5));
        Assert.Equal("ParticipantBusy",Do(s,Harvest(1)));
        Do(s,new(ActionKind.StopTransfer));Assert.Empty(s.Engine.TransfersOf(new(1)));
        Assert.Equal(4,Fixtures.Count(s,o.OwnEnterprises[0].InputId,"wood"));Assert.Equal(1,Fixtures.Count(s,o.Participant.InventoryId,"wood"));
        Assert.All(s.Engine.Observe(new(1)).OwnInventories,i=>{Assert.Equal(0,i.ReservedCapacity);Assert.All(i.Stock,x=>Assert.Equal(0,x.Reserved));});
        CheckpointStore.Decode(CheckpointStore.Encode(s)).Engine.ValidateInvariants();
    }
    [Fact]
    public void TransferAdmissionIsAtomicForInsufficientStockOrSpace()
    {
        var s=Fixtures.Session(Definition());Do(s,Harvest(3));Fixtures.UntilIdle(s);var o=s.Engine.Observe(new(1));
        Assert.Equal("NoStock",Do(s,new(ActionKind.Transfer,Source:o.Participant.InventoryId,Destination:o.OwnEnterprises[0].InputId,ItemId:"wood",Quantity:4)));
        Assert.Empty(s.Engine.TransfersOf(new(1)));Assert.Equal(0,s.Engine.Observe(new(1)).OwnInventories.Sum(i=>i.ReservedCapacity));
        Assert.Equal(3,Fixtures.Count(s,o.Participant.InventoryId,"wood"));
    }
    [Fact]
    public void LeaseExpiresBeforeWorkAndDoesNotDeleteCompletedProperty()
    {
        var rules=new ResourceSiteRules(false,Capacity:5,RegenerationQuantity:0,LeaseTicks:5,LeasePriceMinor:1);
        var s=Fixtures.Session(Definition(rules));Assert.Equal("NoResourceRights",Do(s,Harvest(1)));
        Assert.Equal("Accepted",Do(s,new(ActionKind.LeaseResource,Target:new(0,0))));
        Assert.Equal("ResourceUnavailable",Do(s,new(ActionKind.LeaseResource,Target:new(0,0)),2));
        Assert.Equal("Accepted",Do(s,new(ActionKind.StartHarvesting,ItemId:"wood",Quantity:1)));
        s.AdvanceTo(7);Assert.False(s.Engine.Observe(new(1)).Participant.Busy);Assert.Equal(1,Fixtures.Count(s,s.Engine.Observe(new(1)).Participant.InventoryId,"wood"));
        Assert.Null(s.Engine.ResourceSites().Single(r=>r.Position==new Cell(0,0)).Tenant);
        Assert.Equal("NoResourceRights",Do(s,Harvest(1)));Assert.Equal("Accepted",Do(s,new(ActionKind.LeaseResource,Target:new(0,0))));
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(20);loaded.AdvanceTo(20);Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());
    }
    [Fact]
    public void ExclusiveCompetitionAndFiniteClaimsPreventDoubleExtraction()
    {
        var s=Fixtures.Session(Definition(new(false,Capacity:2,RegenerationQuantity:0,PurchasePriceMinor:1)));
        string a=s.Send(new(1),new(ActionKind.BuyResource,Target:new(0,0))),b=s.Send(new(2),new(ActionKind.BuyResource,Target:new(0,0)));s.Step();
        Assert.Single(new[]{s.Engine.Result(a),s.Engine.Result(b)},r=>r!.Status==ResultStatus.Accepted);
        var owner=s.Engine.ResourceSites().Single(r=>r.Position==new Cell(0,0)).Owner!.Value;
        Assert.Equal("Accepted",Do(s,Harvest(2),owner.Value));Fixtures.UntilIdle(s,owner.Value);
        Assert.Equal("ResourceDepleted",Do(s,Harvest(1),owner.Value));Assert.Equal(0,s.Engine.ResourceSites().Single(r=>r.Position==new Cell(0,0)).Stock);
    }
    [Fact]
    public void PublicRecoveryCostsNoLandAndStockRegeneratesWithSameHarvestSpeed()
    {
        var d=Definition() with {Participants=Definition().Participants.Select(p=>p with {CashMinor=0,Position=new(0,1)}).ToArray()};
        var s=Fixtures.Session(d);Do(s,Harvest(3));Fixtures.UntilIdle(s);Assert.Equal(0,s.Engine.ResourceSites().Single(r=>r.Public).Stock);
        Assert.Equal("ResourceDepleted",Do(s,Harvest(1)));s.AdvanceTo(10);Assert.Equal(1,s.Engine.ResourceSites().Single(r=>r.Public).Stock);
        Assert.Equal("Accepted",Do(s,Harvest(1)));long start=s.Engine.Tick;Fixtures.UntilIdle(s);Assert.Equal(d.HarvestTicksPerUnit,s.Engine.Tick-start);
        Assert.Equal(0,s.Engine.Observe(new(1)).Account.BalanceMinor);s.Engine.ValidateInvariants();
    }
    [Fact]
    public void ResourceAcquisitionIsSeededAndIndependentOfSubmissionOrderWithoutPermanentIdPriority()
    {
        var winners=new HashSet<ParticipantId>();
        for(ulong seed=0;seed<24;seed++)
        {
            var d=Definition(new(false,Capacity:2,RegenerationQuantity:0,PurchasePriceMinor:1));
            d=d with {Seed=seed,Participants=d.Participants.Select(p=>p with {CashMinor=100}).ToArray()};
            GameSession Run(bool reverse)
            {
                var s=Fixtures.Session(d);foreach(long actor in reverse?new long[]{2,1}:new long[]{1,2})s.Send(new(actor),new(ActionKind.BuyResource,Target:new(0,0)));
                s.Step();return s;
            }
            var a=Run(false);var b=Run(true);Assert.Equal(a.Engine.ComputeStateHash(),b.Engine.ComputeStateHash());
            winners.Add(a.Engine.ResourceSites().Single(r=>r.Position==new Cell(0,0)).Owner!.Value);
        }
        Assert.Equal(2,winners.Count);
    }
    [Fact]
    public void WalkingAwayCancelsPartialTransferAtACommittedQuantumAndRestoresAndReplays()
    {
        var d=Definition();var s=Fixtures.Session(d);Do(s,Harvest(5));Fixtures.UntilIdle(s);var o=s.Engine.Observe(new(1));
        Do(s,new(ActionKind.Transfer,Source:o.Participant.InventoryId,Destination:o.OwnEnterprises.Single().InputId,ItemId:"wood",Quantity:5));
        s.SetTravelHeading(new(1),1,0);for(int i=0;i<6;i++)s.AdvanceTravelQuantum();s.SetTravelHeading(new(1),0,0);
        Assert.Empty(s.Engine.TransfersOf(new(1)));Assert.Equal(2,Fixtures.Count(s,o.OwnEnterprises.Single().InputId,"wood"));Assert.Equal(3,Fixtures.Count(s,o.Participant.InventoryId,"wood"));
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));
        while(s.Engine.TravelQuantum!=0){s.AdvanceTravelQuantum();loaded.AdvanceTravelQuantum();}
        Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());
        var replay=GameSession.Replay(d,s.CommandTape,s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),replay.Engine.ComputeStateHash());
    }
}
