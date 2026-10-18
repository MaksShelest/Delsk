using System.Text.Json;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class SharedShopTests
{
    private static GameSession Session() => Fixtures.Session(EconomyProfileTests.Map() with {Participants=EconomyProfileTests.Map().Participants.Select(p=>p with {CashMinor=100000}).ToArray()});
    private static PlotDefinition[] Sites(GameSession s)=>s.Engine.Configuration().Plots!.Where(p=>p.IsShopSite).OrderBy(p=>p.Id).ToArray();
    [Fact]
    public void AnyParticipantCanChooseAnySiteAndBuyOnlyOneOwnedOrReserved()
    {
        var s=Session();var sites=Sites(s);Assert.Equal(4,sites.Length);Assert.All(sites,p=>Assert.Null(p.ShopParticipantId));
        LandContentFixtures.Move(s,sites[^1].Position);
        Assert.Equal("Accepted",LandContentFixtures.Reason(s,PlayerDesk.ReservePlot(sites[^1].Id)));
        LandContentFixtures.Move(s,sites[0].Position);
        Assert.Equal("ShopSiteLimit",LandContentFixtures.Reason(s,PlayerDesk.BuyPlot(sites[0].Id)));
        Assert.Equal("ShopSiteLimit",LandContentFixtures.Reason(s,PlayerDesk.ReservePlot(sites[0].Id)));
        LandContentFixtures.Move(s,sites[^1].Position);
        Assert.Equal("Accepted",LandContentFixtures.Reason(s,PlayerDesk.BuyPlot(sites[^1].Id)));
        LandContentFixtures.Move(s,sites[0].Position);
        Assert.Equal("ShopSiteLimit",LandContentFixtures.Reason(s,PlayerDesk.ReservePlot(sites[0].Id)));
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(s));Assert.Equal(s.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());
    }
    [Fact]
    public void ReleasingAReservationFreesChoiceButDoesNotRefundItsFee()
    {
        var s=Session();var sites=Sites(s);LandContentFixtures.Move(s,sites[0].Position);
        Fixtures.Command(s,PlayerDesk.ReservePlot(sites[0].Id));long cash=s.Engine.Observe(new(1)).Account.BalanceMinor;
        Fixtures.Command(s,PlayerDesk.Check(new(ActionKind.CancelPlotReservation,Plot:new(sites[0].Id))));
        Assert.Equal(cash,s.Engine.Observe(new(1)).Account.BalanceMinor);
        LandContentFixtures.Move(s,sites[1].Position);Assert.Equal("Accepted",LandContentFixtures.Reason(s,PlayerDesk.BuyPlot(sites[1].Id)));
    }
    [Fact]
    public void ConcurrentNpcAndHumanCannotBuyTheSameSlotOrBypassTheQuota()
    {
        var s=Session();var site=Sites(s)[0];LandContentFixtures.Move(s,site.Position);
        Fixtures.Command(s,PlayerDesk.Move(site.Position.X,site.Position.Z),2);Fixtures.UntilIdle(s,2);
        string a=s.Send(new(1),PlayerDesk.BuyPlot(site.Id)),b=s.Send(new(2),PlayerDesk.BuyPlot(site.Id));s.Step();
        Assert.Single(new[]{a,b},id=>s.Engine.Result(id)!.Status==ResultStatus.Accepted);
        Assert.Single(s.Engine.Inspect().Plots,p=>p.Id.Value==site.Id && p.OwnerId.HasValue);
    }
    [Fact]
    public void PublicMarketProjectionDoesNotExposeForeignPrivateInventoryOrBidLimit()
    {
        var map=JsonSerializer.Deserialize<ScenarioDefinition>(EconomyProfileTests.Text("market-window"),SimulationJson.Options)!;
        var s=Fixtures.Session(map);LandContentFixtures.Harvest(s,"wood",2);LandContentFixtures.Move(s,new(2,0));
        var seller=s.Engine.Observe(new(1));var shop=seller.OwnBuildings.Single();
        Fixtures.Command(s,PlayerDesk.Transfer(seller.Participant.InventoryId.Value,shop.InputId.Value,"wood",2));
        Fixtures.Command(s,PlayerDesk.PublishLot(shop.Id.Value,"wood",2,500));long lot=s.Engine.Observe(new(1)).MarketOffers.Single().Id;
        Fixtures.Command(s,PlayerDesk.Bid(lot,2,7777),2);
        var buyer=PlayerDesk.Read(s.Engine,new(2));var other=PlayerDesk.Read(s.Engine,new(3));
        Assert.Single(buyer.Market!.Bids);Assert.Empty(other.Market!.Bids);Assert.DoesNotContain(other.Inventories,i=>i.OwnerId==new ParticipantId(1));
        Assert.Single(other.Market.Shops);Assert.Equal(shop.Position,other.Market.Shops.Single().Position);
        var tablet=new TabletState();Assert.False(tablet.OpenMarket(buyer,map,shop.Id.Value));
        Fixtures.Command(s,PlayerDesk.Move(2,0),2);Fixtures.UntilIdle(s,2);
        Assert.True(tablet.OpenMarket(PlayerDesk.Read(s.Engine,new(2)),map,shop.Id.Value));Assert.True(tablet.BlocksTime);
        Fixtures.Command(s,PlayerDesk.Move(3,0),2);Fixtures.UntilIdle(s,2);
        Assert.False(tablet.ValidateLocation(PlayerDesk.Read(s.Engine,new(2)),map));
    }
}
