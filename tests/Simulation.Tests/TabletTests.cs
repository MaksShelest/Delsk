using System.Text.Json;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class TabletTests
{
    private static readonly ParticipantId Human = new(1);
    private static ScenarioDefinition Read(string name)
    {
        using var stream = typeof(TabletTests).Assembly.GetManifestResourceStream("Fixture."+name+".json")!;
        return JsonSerializer.Deserialize<ScenarioDefinition>(stream,SimulationJson.Options)!;
    }
    private static ScenarioDefinition Market() => Fixtures.Cycle() with
    { Enterprises = [], Population = 0, CapitalOffers = [new(1,"wood",20,500,new(0,0))] };
    private static PlayerView View(GameSession session) => PlayerDesk.Read(session.Engine,Human);

    [Fact]
    public void OpeningTabletStopsTheEntireNpcSessionAndDoesNotCatchUp()
    {
        var map = Read("baseline-economy"); var session = Fixtures.Session(map);
        using var stream = typeof(TabletTests).Assembly.GetManifestResourceStream("Fixture.baseline-controllers.json")!;
        var config = JsonSerializer.Deserialize<AutonomousConfiguration>(stream,SimulationJson.Options)!;
        session.AttachControllers(config.Npcs.Select(n => new AlgorithmicController(n with { HumanActorId = 1 },AlgorithmicController.RuntimeSeed(map.Seed,n.ActorId),map.Participants.Select(p=>p.Id))));
        LandContentFixtures.Move(session,map.CapitalDemand!.Terminal);
        var tablet = new TabletState(); Assert.True(tablet.OpenCapital(View(session),map));
        var clock = new TickScheduler(session); clock.SetPaused(tablet.IsPaused(false));
        string world = session.Engine.ComputeStateHash(), controllers = session.ComputeControllerStateHash(); long tick = session.Engine.Tick; int commands = session.CommandTape.Length;
        clock.AdvanceElapsed(TimeSpan.FromSeconds(50));
        Assert.Equal(tick,session.Engine.Tick); Assert.Equal(world,session.Engine.ComputeStateHash()); Assert.Equal(controllers,session.ComputeControllerStateHash()); Assert.Equal(commands,session.CommandTape.Length);
        tablet.SetTimeRunning(true); clock.SetPaused(tablet.IsPaused(false)); clock.AdvanceElapsed(TimeSpan.FromMilliseconds(100));
        Assert.Equal(tick,session.Engine.Tick); clock.AdvanceElapsed(TimeSpan.FromMilliseconds(900));
        Assert.Equal(tick+1,session.Engine.Tick); Assert.True(session.ControllerTurns.Length>0);
    }

    [Fact]
    public void QueuedActionDoesNotExecuteOnTacticalPause()
    {
        var map=Market();var session=Fixtures.Session(map);var tablet=new TabletState(); Assert.True(tablet.OpenCapital(View(session),map));
        var clock=new TickScheduler(session);clock.SetPaused(tablet.IsPaused(false));
        string id=session.Send(Human,PlayerDesk.Harvest("wood",1));clock.AdvanceElapsed(TimeSpan.FromSeconds(20));
        Assert.Null(session.Engine.Result(id));Assert.Equal(0,Fixtures.Count(session,View(session).Participant.InventoryId,"wood"));
        tablet.SetTimeRunning(true);clock.SetPaused(tablet.IsPaused(false));clock.AdvanceElapsed(TimeSpan.FromSeconds(1));
        Assert.Equal(ResultStatus.Accepted,session.Engine.Result(id)!.Status);Assert.True(View(session).Participant.Busy);
        clock.AdvanceElapsed(TimeSpan.FromSeconds(2));Assert.Equal(1,Fixtures.Count(session,View(session).Participant.InventoryId,"wood"));
    }

    [Fact]
    public void SaleBlockedMovementCannotResumeTimeButRequestCanBeCancelled()
    {
        var map=Market();var session=Fixtures.Session(map);LandContentFixtures.Harvest(session,"wood",1);
        Fixtures.Command(session,PlayerDesk.Sell(1,"wood",1));Assert.True(View(session).Participant.Busy);
        var tablet=new TabletState();Assert.True(tablet.OpenCapital(View(session),map));
        Assert.False(session.Engine.CanTravel(Human,1,0));Assert.False(tablet.ResumeForMovement(session.Engine.CanTravel(Human,1,0)));
        Assert.True(tablet.BlocksTime);
        var clock=new TickScheduler(session);clock.SetPaused(tablet.IsPaused(false));long tick=session.Engine.Tick;
        session.Send(Human,PlayerDesk.CancelSale(1));clock.AdvanceElapsed(TimeSpan.FromSeconds(10));Assert.Equal(tick,session.Engine.Tick);
        Assert.Single(session.Engine.CapitalTradingOf(Human).Requests);
        tablet.SetTimeRunning(true);clock.SetPaused(tablet.IsPaused(false));clock.AdvanceElapsed(TimeSpan.FromSeconds(1));
        Assert.Empty(session.Engine.CapitalTradingOf(Human).Requests);Assert.False(View(session).Participant.Busy);
        Assert.Equal(1,Fixtures.Count(session,View(session).Participant.InventoryId,"wood"));
    }

    [Fact]
    public void WalkingResumesWorldAndClosesTabletOnlyAfterLeavingTheCell()
    {
        var map=Market();var session=Fixtures.Session(map);var tablet=new TabletState();Assert.True(tablet.OpenCapital(View(session),map));
        Assert.True(tablet.ResumeForMovement(session.Engine.CanTravel(Human,1,0)));
        var clock=new TickScheduler(session);clock.SetPaused(tablet.IsPaused(false));session.SetTravelHeading(Human,1,0);
        clock.AdvanceElapsed(TimeSpan.FromMilliseconds(300));Assert.True(tablet.ValidateLocation(View(session),map));Assert.True(tablet.IsOpen);
        clock.AdvanceElapsed(TimeSpan.FromMilliseconds(300));Assert.False(tablet.ValidateLocation(View(session),map));Assert.False(tablet.IsOpen);
        Assert.Equal(new Cell(1,0),View(session).Participant.Position);Assert.False(tablet.IsPaused(false));
        clock.AdvanceElapsed(TimeSpan.FromMilliseconds(400));Assert.Equal(1,session.Engine.Tick);
    }

    [Fact]
    public void ClosingTabletCannotRemoveMenuOrLifecyclePause()
    {
        var map=Market();var session=Fixtures.Session(map);var tablet=new TabletState();Assert.True(tablet.OpenCapital(View(session),map));
        tablet.SetTimeRunning(true);Assert.True(tablet.IsPaused(true));tablet.Close();Assert.True(tablet.IsPaused(true));
        var clock=new TickScheduler(session);clock.SetPaused(tablet.IsPaused(true));clock.AdvanceElapsed(TimeSpan.FromSeconds(30));Assert.Equal(0,session.Engine.Tick);
        clock.SetPaused(tablet.IsPaused(false));clock.AdvanceElapsed(TimeSpan.FromMilliseconds(100));Assert.Equal(0,session.Engine.Tick);
    }

    [Fact]
    public void MovementAvailabilityQueryIsReadOnlyAndRespectsBoundariesAndOccupation()
    {
        var session=Fixtures.Session(Fixtures.Cycle() with {BlockedCells=[new(1,0)]});
        session.SetTravelHeading(Human,1,0);session.CompleteTravelTick();session.SetTravelHeading(Human,0,0);
        string before=session.Engine.ComputeStateHash();Assert.False(session.Engine.CanTravel(Human,1,0));Assert.True(session.Engine.CanTravel(Human,-1,0));Assert.False(session.Engine.CanTravel(Human,0,0));Assert.Equal(before,session.Engine.ComputeStateHash());
        LandContentFixtures.Harvest(session,"wood",1);
        Fixtures.Command(session,PlayerDesk.Harvest("wood",1));Assert.True(session.Engine.CanTravel(Human,-1,0));
    }

    [Fact]
    public void CapitalProjectionIsOwnOnlyAndSurvivesCheckpointWithSameSettlement()
    {
        var map=Market();var session=Fixtures.Session(map);
        Fixtures.Command(session,PlayerDesk.Harvest("wood",1),1);Fixtures.UntilIdle(session,1);
        Fixtures.Command(session,PlayerDesk.Harvest("wood",2),2);Fixtures.UntilIdle(session,2);
        session.Send(new(1),PlayerDesk.Sell(1,"wood",1));session.Send(new(2),PlayerDesk.Sell(1,"wood",2));session.Step();
        string hash=session.Engine.ComputeStateHash();var own=session.Engine.CapitalTradingOf(new(1));var other=session.Engine.CapitalTradingOf(new(2));
        Assert.Equal(1,Assert.Single(own.Requests).Quantity);Assert.Equal(2,Assert.Single(other.Requests).Quantity);Assert.Equal(hash,session.Engine.ComputeStateHash());
        string json=JsonSerializer.Serialize(own,SimulationJson.Options);Assert.DoesNotContain("reservation",json,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("inventoryId",json,StringComparison.OrdinalIgnoreCase);
        var loaded=CheckpointStore.Decode(CheckpointStore.Encode(session));Assert.Equal(own.Requests,loaded.Engine.CapitalTradingOf(Human).Requests);
        long cutoff=own.Requests[0].CutoffTick!.Value;
        while(session.Engine.Tick<cutoff){session.Step();loaded.Step();Assert.Equal(session.Engine.ComputeStateHash(),loaded.Engine.ComputeStateHash());}
        Assert.Empty(session.Engine.CapitalTradingOf(Human).Requests);var settled=Assert.Single(session.Engine.CapitalTradingOf(Human).Settlements);
        Assert.Equal(Human,settled.Seller);Assert.Equal(1,settled.Accepted);Assert.Equal(500,settled.PaidMinor);
        Assert.Equal(session.Engine.CapitalTradingOf(Human).Settlements,loaded.Engine.CapitalTradingOf(Human).Settlements);
    }

    [Fact]
    public void CapitalFiltersAndPriceSortUsePublicOffersWithStableTies()
    {
        var session=Fixtures.Session(Market());var location=new Cell(0,0);
        var view=View(session) with {Tick=10,CapitalOffers=[new(1,"wood",3,500,location,null,0),new(2,"wood",2,300,location,null,0),new(3,"plank",1,700,location,null,0),new(4,"wood",2,500,location,null,0),new(5,"wood",3,900,new(1,0),null,0),new(6,"wood",3,900,location,null,0,ExpiresAtTick:9)]};
        var prefs=new CapitalTablePreferences();Assert.Equal(new long[]{3,1,4,2},prefs.Offers(view,location).Select(o=>o.Id));
        prefs.ItemId="wood";Assert.Equal(new long[]{1,4,2},prefs.Offers(view,location).Select(o=>o.Id));
        prefs.HighestPriceFirst=false;Assert.Equal(new long[]{2,1,4},prefs.Offers(view,location).Select(o=>o.Id));
        var tablet=new TabletState();tablet.OpenCapital(view,Market());tablet.Close();tablet.OpenCapital(view,Market());Assert.Equal("wood",prefs.ItemId);Assert.False(prefs.HighestPriceFirst);
    }

    [Fact]
    public void ProgressUsesTheRealBatchAndEachOrdersOwnCutoff()
    {
        var session=Fixtures.Session();var idle=Assert.Single(View(session).Enterprises);Assert.Equal(new TabletProgress(0,1),TabletProgress.Production(idle));
        Fixtures.Supply(session);Fixtures.Command(session,PlayerDesk.Work(1,true));var working=Assert.Single(View(session).Enterprises);
        Assert.True(working.BatchId>0);Assert.Equal(new TabletProgress(working.WorkDone,working.WorkRequired),TabletProgress.Production(working));
        var offer=new CapitalOfferView(1,"wood",10,500,new(0,0),15,0);Assert.Equal(new TabletProgress(2,5),TabletProgress.Window(offer,12,5));
        Assert.Equal(new TabletProgress(0,5),TabletProgress.Window(offer with {CutoffTick=20},12,5));
        Assert.Equal(new TabletProgress(0,1),TabletProgress.Window(offer with {CutoffTick=null},12,5));
    }

    [Fact]
    public void EnterpriseTabletNeedsOwnBuildingAndCurrentFootprint()
    {
        var map=LandContentFixtures.Scenario();var session=Fixtures.Session(map);var tablet=new TabletState();
        Assert.False(tablet.OpenEnterprise(View(session),1));LandContentFixtures.Move(session,new(4,0));
        Assert.True(tablet.OpenEnterprise(View(session),1));Assert.True(tablet.BlocksTime);
        Assert.False(tablet.OpenEnterprise(PlayerDesk.Read(session.Engine,new(2)),1));
        LandContentFixtures.Move(session,new(3,0));Assert.False(tablet.ValidateLocation(View(session),map));Assert.False(tablet.IsOpen);
    }
}
