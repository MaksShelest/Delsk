using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public sealed class VolumeContractTests
{
    private static ScenarioDefinition Read(string name)
    {
        using var stream=typeof(VolumeContractTests).Assembly.GetManifestResourceStream("Fixture."+name+".json")!;
        var d=JsonSerializer.Deserialize<ScenarioDefinition>(stream,SimulationJson.Options)!;
        return d with {Economy=new(Handling:new(1,1)),Participants=d.Participants.Select(p=>p with {InventoryCapacity=20}).ToArray()};
    }
    private static string Do(GameSession s,GameAction action,long actor=1)=>s.Engine.Result(Fixtures.Command(s,action,actor))!.ReasonCode;
    private static GameSession Purchased(int ttl)
    {
        var d=Read("pickup-lifecycle");d=d with {Market=new(PickupTtlTicks:ttl),Participants=[..d.Participants,d.Participants[1] with {Id=3}]};
        var s=Fixtures.Session(d);LandContentFixtures.Harvest(s,"wood",10);LandContentFixtures.Move(s,new(2,0));
        var o=s.Engine.Observe(new(1));Assert.Equal("Accepted",Do(s,new(ActionKind.Transfer,Source:o.Participant.InventoryId,Destination:o.OwnBuildings.Single().InputId,ItemId:"wood",Quantity:10)));Fixtures.UntilIdle(s);
        Assert.Equal("Accepted",Do(s,new(ActionKind.CreateSellOffer,Building:new(1),ItemId:"wood",Quantity:10,UnitPriceMinor:500)));
        Do(s,new(ActionKind.MoveTo,Target:new(2,0)),2);Fixtures.UntilIdle(s,2);long offer=s.Engine.Inspect().MarketOffers.Single().Id;
        Assert.Equal("Accepted",Do(s,new(ActionKind.SubmitBuyRequest,MarketOfferId:offer,Quantity:10,MaximumPriceMinor:500),2));
        s.AdvanceTo(s.Engine.Inspect().MarketOffers.Single().CutoffTick);return s;
    }
    [Theory]
    [InlineData(1,1)][InlineData(2,2)][InlineData(4,4)]
    public void PickupPaysOnlyPhysicallyCompletedUnitsIncludingTheDeadlineTick(int ttl,long collected)
    {
        var s=Purchased(ttl);long purchase=s.Engine.Inspect().MarketAllocations.Single().Id;
        string id=s.Send(new(2),new(ActionKind.CollectPurchase,PurchaseId:purchase,Quantity:10));s.Step();
        Assert.Equal("Accepted",s.Engine.Result(id)!.ReasonCode);var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));
        long deadline=s.Engine.Inspect().MarketAllocations.Single().DeadlineTick;
        s.AdvanceTo(deadline+2);loaded.AdvanceTo(deadline+2);Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());
        var p=s.Engine.Inspect().MarketAllocations.Single();Assert.Equal(collected,p.CollectedQuantity);Assert.Equal(10-collected,p.ReleasedQuantity);
        Assert.Equal(collected*500,p.PaidMinor);Assert.Equal(5000,p.PaidMinor+p.FineMinor+p.RefundedMinor);
        Assert.Equal(collected,Fixtures.Count(s,s.Engine.Observe(new(2)).Participant.InventoryId,"wood"));Assert.Empty(s.Engine.TransfersOf(new(2)));
        Assert.All(s.Engine.Inspect().Inventories,i=>Assert.Equal(0,i.ReservedCapacity));Assert.Empty(s.Engine.ObserveController(new(3)).OwnMarketAllocations);
        string hash=s.Engine.ComputeStateHash();var c=s.CommandTape.Single(c=>c.Command.CommandId==id).Command;
        Assert.Equal(ResultStatus.Accepted,s.Engine.Enqueue(new(2),c)!.Status);Assert.Equal(hash,s.Engine.ComputeStateHash());
        var replay=GameSession.Replay(s.Engine.Configuration(),s.CommandTape,s.Engine.Tick);Assert.Equal(hash,replay.Engine.ComputeStateHash());
    }
    [Fact]
    public void StoppingPickupDoesNotReleaseThePurchaseClaimOrPayTheRemainder()
    {
        var s=Purchased(40);var p=s.Engine.Inspect().MarketAllocations.Single();
        Do(s,new(ActionKind.CollectPurchase,PurchaseId:p.Id,Quantity:10),2);Do(s,new(ActionKind.StopTransfer),2);
        p=s.Engine.Inspect().MarketAllocations.Single();Assert.Equal(1,p.CollectedQuantity);Assert.Equal(9,p.RemainingQuantity);Assert.Equal(500,p.PaidMinor);
        Assert.Equal(9,s.Engine.Inspect().Inventories.Single(i=>i.Id==p.SourceInventory).Stock.Single().Reserved);
        Assert.Equal(0,s.Engine.Observe(new(2)).OwnInventories.Sum(i=>i.ReservedCapacity));
        Do(s,new(ActionKind.CollectPurchase,PurchaseId:p.Id,Quantity:9),2);Fixtures.UntilIdle(s,2);
        p=s.Engine.Inspect().MarketAllocations.Single();Assert.Equal("Fulfilled",p.Status);Assert.Equal(5000,p.PaidMinor);Assert.Equal(10,p.CollectedQuantity);
    }
    [Theory]
    [InlineData(1)][InlineData(3)]
    public void ManualDeliveryReservesQuotaAndSettlesOnlyCompletedHandlingBeforeExpiry(int ttl)
    {
        var d=Read("delivery-lifecycle") with {Delivery=new(ContractTtlTicks:ttl)};
        var s=Fixtures.Session(d);LandContentFixtures.Harvest(s,"wood",10);
        var receiver=s.Engine.Observe(new(2)).OwnBuildings.Single();LandContentFixtures.Move(s,receiver.Position);
        Do(s,new(ActionKind.CreateBuyOffer,Destination:receiver.InputId,ItemId:"wood",Quantity:10,UnitPriceMinor:500),2);
        long offer=s.Engine.Inspect().BuyOffers.Single().Id;Do(s,new(ActionKind.AcceptDeliveryQuantity,BuyOfferId:offer,Quantity:10));
        s.AdvanceTo(s.Engine.Inspect().BuyOffers.Single().CutoffTick!.Value);var contract=s.Engine.Inspect().DeliveryContracts.Single();
        Assert.Equal("Accepted",Do(s,new(ActionKind.DeliverContract,ContractId:contract.Id,Quantity:10)));
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));s.AdvanceTo(contract.DeadlineTick+2);loaded.AdvanceTo(contract.DeadlineTick+2);
        Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());contract=s.Engine.Inspect().DeliveryContracts.Single();
        Assert.Equal(ttl,contract.DeliveredQuantity);Assert.Equal(10-ttl,contract.UnfulfilledQuantity);Assert.Equal(ttl*500,contract.PaidMinor);
        Assert.Equal(5000,contract.PaidMinor+contract.RefundedMinor);Assert.Equal(contract.BondMinor,contract.FineMinor+contract.BondReturnedMinor);
        Assert.Equal(10-ttl,Fixtures.Count(s,s.Engine.Observe(new(1)).Participant.InventoryId,"wood"));Assert.Empty(s.Engine.TransfersOf(new(1)));
        Assert.Equal(0,s.Engine.Inspect().Inventories.Sum(i=>i.ReservedCapacity));
    }
    [Fact]
    public void WaitingElectionFreezesHandlingAndReservationsThenReplayResumesExactly()
    {
        var d=Fixtures.Cycle(staff:0) with {Population=0,HarvestTicksPerUnit=1,Economy=new(Handling:new(1,1)),Elections=new(9,PauseForBallots:true),
            Enterprises=[Fixtures.Cycle(staff:0).Enterprises[0] with {Position=new(0,0)}]};
        var s=Fixtures.Session(d);LandContentFixtures.Harvest(s,"wood",6);var o=s.Engine.Observe(new(1));
        Do(s,new(ActionKind.Transfer,Source:o.Participant.InventoryId,Destination:o.OwnEnterprises.Single().InputId,ItemId:"wood",Quantity:6));
        s.AdvanceTo(9);Assert.True(s.Engine.WaitingForBallots);var progress=Assert.Single(s.Engine.TransfersOf(new(1)));
        for(int i=0;i<20;i++)s.Step();Assert.Equal(progress,Assert.Single(s.Engine.TransfersOf(new(1))));
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));
        foreach(var session in new[]{s,loaded})foreach(long actor in new long[]{1,2})
            session.Send(new(actor),new(ActionKind.SetVoteChoice,Election:new(1),Candidate:new(actor)));
        Assert.False(s.Engine.WaitingForBallots);s.AdvanceTo(14);loaded.AdvanceTo(14);Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());
        Assert.Empty(s.Engine.TransfersOf(new(1)));var replay=GameSession.Replay(d,s.CommandTape,s.Engine.Tick);Assert.Equal(s.Engine.ComputeStateHash(),replay.Engine.ComputeStateHash());
    }
}
