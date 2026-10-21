using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class TabletDesignProbe
{
    private async Task CheckPages(GameSession food,ScenarioDefinition definition)
    {
        foreach(string language in new[]{"ru","en"})
        {
            foreach(var size in new[]{new Vector2I(1440,1024),new Vector2I(800,600),new Vector2I(640,800)})
            {
                GetWindow().Size=size;GetWindow().ContentScaleSize=size;
                var panel=new TabletPanel(language=="ru"?"Мой планшет":"My tablet",language);AddChild(panel);
                var player=new PlayerTablet(definition,language,a=>food.Send(new(1),a),a=>food.Engine.AccessFor(new(1),a));panel.Body.AddChild(player);panel.ShowClock(false,true,food.Engine.Tick,"");player.ShowData(PlayerDesk.Read(food.Engine,new(1)),false,true);
                for(int tab=0;tab<4;tab++)
                {
                    player.SelectedTab=tab;await Frames();
                    await Capture("player-"+tab+"-"+language+"-"+size.X);
                    Check(All(player).OfType<Button>().Where(b=>b.IsVisibleInTree()).All(b=>b.GetGlobalRect().Position.X>=panel.GlobalPosition.X-1 && b.GetGlobalRect().End.X<=GetWindow().Size.X-8),"Player controls escaped left edge");
                }
                Check(PlayerDesk.Read(food.Engine,new(1)).Buildings.All(b=>b.OwnerId==new ParticipantId(1)),"Foreign holdings entered the UI projection");
                RemoveChild(panel);panel.QueueFree();await Frames();
                var help=new HelpOverlay(language,"basics");AddChild(help);await Frames();
                var topics=Named<OptionButton>(help,"HelpTopics");
                for(int i=0;i<11;i++)
                {
                    topics.Select(i);topics.EmitSignal(OptionButton.SignalName.ItemSelected,(long)i);await Frames();
                    Check(All(help).OfType<Label>().Any(l=>l.IsVisibleInTree() && l.Text.StartsWith("3. ",StringComparison.Ordinal)),"Help topic lost three short steps");
                    if(i is 0 or 10)await Capture("help-"+i+"-"+language+"-"+size.X);
                }
                Named<Button>(help,"HelpRules").EmitSignal(BaseButton.SignalName.Pressed);await Frames();
                Check(Named<RichTextLabel>(help,"HelpFullText").Visible && Named<RichTextLabel>(help,"HelpFullText").Text.Length>200,"Full handbook missing");
                RemoveChild(help);help.QueueFree();await Frames();_checks.Add("player/help-"+language+"-"+size.X);
            }
        }
        bool workingSeen=false;
        foreach(long cut in new[]{340L,405L,414L,561L,562L,584L})
        {
            var working=GameSession.Replay(definition,food.CommandTape.Where(c=>c.SubmittedAtTick<=cut).ToArray(),cut);var state=working.Engine.Inspect();
            var active=state.Enterprises.FirstOrDefault(e=>e.BatchId!=0 && state.Buildings.Any(b=>b.Id.Value==e.Id.Value && b.TypeId is "confectionery" or "dairy" or "sugar-factory"));
            if(active is null)continue;
            var b=state.Buildings.Single(b=>b.Id.Value==active.Id.Value);var actor=b.OwnerId;working.SetPaused(true);
            GetWindow().Size=new(1280,720);GetWindow().ContentScaleSize=new(1280,720);
            var host=new TabletPanel("Production in progress","en");AddChild(host);var card=new HouseholdObjectCard(definition,"en","building",b.Id.Value,a=>working.Send(actor,a),a=>working.Engine.AccessFor(actor,a));card.ArtworkHeaderChanged+=host.UseArtworkHeader;host.Body.AddChild(card);
            card.ShowData(PlayerDesk.Read(working.Engine,actor),false,true);host.ShowClock(false,true,cut,"");await Frames();
            var art=All(card).OfType<ProductionTabletPresenter>().Single();var progress=Named<ProgressBar>(art,"Progress");
            Check(progress.Value==active.WorkDone && progress.MaxValue==active.WorkRequired,"WIP progress is a demo value");
            var loaded=CheckpointStore.Decode(CheckpointStore.Encode(working));Check(loaded.Engine.ComputeStateHash()==working.Engine.ComputeStateHash(),"WIP checkpoint changed world");
            var projected=PlayerDesk.Read(loaded.Engine,actor);card.ShowData(projected,false,true);
            var output=projected.Inventories.Single(i=>i.Id==active.OutputId);Check(output.ReservedCapacity>0,"No output hold covered by the WIP case");
            var outputItem=definition.Recipes.Single(r=>r.Id==active.RecipeId).OutputItem;var stock=output.Stock.FirstOrDefault(x=>x.ItemId==outputItem);
            Check(Named<Label>(art,"PlankCount").Text.StartsWith(((stock?.Quantity??0)-(stock?.Reserved??0)).ToString()+" / ",StringComparison.Ordinal),"Future capacity rendered as physical goods");await Capture("working-production-save-load-en-1280");
            working.SetPaused(false);loaded.SetPaused(false);working.Step();loaded.Step();Check(loaded.Engine.ComputeStateHash()==working.Engine.ComputeStateHash(),"WIP resume diverged");
            RemoveChild(host);host.QueueFree();await Frames();workingSeen=true;_checks.Add("WIP/reservations/direct-save-load/resume");break;
        }
        Check(workingSeen,"No actual new-factory batch exercised");
        var d=RunArtifacts.LoadScenario(_output+"/participant-trade.json");var recording=new GameSession(new(d));ScriptedScenarios.ParticipantTrade(recording,d);
        var tape=recording.CommandTape;
        foreach(long cut in new[]{33L,35L,38L,51L})
        {
            var s=GameSession.Replay(d,tape.Where(c=>c.SubmittedAtTick<=cut).ToArray(),cut);s.SetPaused(true);
            foreach(string language in new[]{"ru","en"})
            {
                GetWindow().Size=new(1440,1024);GetWindow().ContentScaleSize=new(1440,1024);
                var view=PlayerDesk.Read(s.Engine,new(1));var shop=view.Buildings.FirstOrDefault(b=>b.Kind==BuildingKind.Shop)?.Id.Value??0;
                var panel=new TabletPanel(language=="ru"?"Биржа":"Exchange",language,"market");AddChild(panel);
                var exchange=new ExchangeTablet(d,language,new(),new(),shop,a=>s.Send(new(1),a),false,a=>s.Engine.AccessFor(new(1),a));panel.Body.AddChild(exchange);panel.ShowClock(false,true,s.Engine.Tick,"");
                exchange.Capital.ShowData(view,s.Engine.CapitalTradingOf(new(1)),false,true);exchange.Market.ShowData(view,false);exchange.ShowHeader(view);
                for(int page=0;page<5;page++){exchange.SelectPage(page);await Frames();if(cut==33)await Capture("exchange-"+page+"-"+language);}
                var third=PlayerDesk.Read(s.Engine,new(3));Check(third.Negotiations.All(n=>n.Current.Terms.Buyer==new ParticipantId(3) || n.Current.Terms.Seller==new ParticipantId(3)),"Third-party negotiation terms escaped projection");
                RemoveChild(panel);panel.QueueFree();await Frames();s.Engine.ValidateInvariants();
            }
            if(cut==35)
            {
                var pickupBuyer=PlayerDesk.Read(s.Engine,new(2));Check(pickupBuyer.Market!.Allocations.Any(a=>a.RemainingQuantity>0),"Partial pickup fixture missing");
                GetWindow().Size=new(800,600);GetWindow().ContentScaleSize=new(800,600);
                var host=new TabletPanel("My purchases","en","market");AddChild(host);var pages=new ExchangeTablet(d,"en",new(),new(),pickupBuyer.Buildings.First(b=>b.Kind==BuildingKind.Shop).Id.Value,a=>s.Send(new(2),a),true,a=>s.Engine.AccessFor(new(2),a));host.Body.AddChild(pages);
                pages.Market.ShowData(pickupBuyer,false);pages.Capital.ShowData(pickupBuyer,s.Engine.CapitalTradingOf(new(2)),false,true);pages.SelectPage(2);host.ShowClock(false,true,s.Engine.Tick,"");await Frames();await Capture("partial-pickup-en-800");
                Check(All(pages.Market).OfType<Label>().Any(l=>l.Text.Contains("collect within",StringComparison.Ordinal)),"Real pickup deadline missing");
                RemoveChild(host);host.QueueFree();await Frames();_checks.Add("partial-paid-pickup/physical-deadline");
            }
            _checks.Add("exchange-actual-cut-"+cut);
        }
        var marketDefinition=RunArtifacts.LoadScenario(_output+"/market-window.json");var marketRun=new GameSession(new(marketDefinition));ScriptedScenarios.MarketWindow(marketRun,marketDefinition);
        var market=GameSession.Replay(marketDefinition,marketRun.CommandTape.Where(c=>c.SubmittedAtTick<=44).ToArray(),44);var buyer=PlayerDesk.Read(market.Engine,new(2));
        Check(buyer.Market!.Offers.Any(o=>o.Status==MarketOfferStatus.Auction),"Fixture did not exercise a live auction");
        GetWindow().Size=new(1280,720);GetWindow().ContentScaleSize=new(1280,720);
        var auctionPanel=new TabletPanel("Auction", "en", "market");AddChild(auctionPanel);
        var auctionUi=new ExchangeTablet(marketDefinition,"en",new(),new(),buyer.Buildings.FirstOrDefault(b=>b.Kind==BuildingKind.Shop)?.Id.Value??0,a=>market.Send(new(2),a),true,a=>market.Engine.AccessFor(new(2),a));auctionPanel.Body.AddChild(auctionUi);
        auctionUi.Capital.ShowData(buyer,market.Engine.CapitalTradingOf(new(2)),false,true);auctionUi.Market.ShowData(buyer,false);auctionPanel.ShowClock(false,true,market.Engine.Tick,"");await Frames();await Capture("active-auction-en-1280");
        Check(All(auctionUi).OfType<Label>().Any(l=>l.Text.Contains("Auction, round",StringComparison.Ordinal)),"Live auction round not rendered");
        RemoveChild(auctionPanel);auctionPanel.QueueFree();await Frames();_checks.Add("active-auction-projection-and-render");
        var capitalDefinition=RunArtifacts.LoadScenario(_output+"/economic-cycle.json");var capitalRun=new GameSession(new(capitalDefinition));ScriptedScenarios.EconomicCycle(capitalRun,capitalDefinition);
        foreach(string language in new[]{"ru","en"})
        {
            var capital=GameSession.Replay(capitalDefinition,capitalRun.CommandTape.Where(c=>c.SubmittedAtTick<=55).ToArray(),55);capital.SetPaused(true);var actor=new ParticipantId(1);Move(capital,actor,capitalDefinition.CapitalOffers[0].Position);capital.SetPaused(true);var v=PlayerDesk.Read(capital.Engine,actor);
            Check(v.CapitalOffers.Length>0,"Capital fixture has no actual offers");
            var host=new TabletPanel(language=="ru"?"Биржа":"Exchange",language,"market");AddChild(host);var exchange=new ExchangeTablet(capitalDefinition,language,new(),new(),0,a=>capital.Send(actor,a),false,a=>capital.Engine.AccessFor(actor,a));host.Body.AddChild(exchange);
            exchange.Capital.ShowData(v,capital.Engine.CapitalTradingOf(actor),false,true);exchange.Market.ShowData(v,false);exchange.ShowHeader(v);host.ShowClock(false,true,capital.Engine.Tick,"");await Frames();await Capture("capital-live-"+language+"-1280");
            Check(All(exchange.Capital).OfType<Label>().Any(l=>l.Text.Contains(language=="ru"?"Спрос:":"Demand:",StringComparison.Ordinal)),"Capital offer rows missing");
            var sell=All(exchange.Capital).OfType<Button>().First(b=>!b.Disabled && b.IsVisibleInTree() && b.Text.StartsWith(language=="ru"?"Продать ":"Sell ",StringComparison.Ordinal));
            long tick=capital.Engine.Tick,cash=PlayerDesk.Read(capital.Engine,actor).CashMinor;sell.EmitSignal(BaseButton.SignalName.Pressed);
            Check(capital.Engine.Tick==tick && PlayerDesk.Read(capital.Engine,actor).CashMinor==cash,"Paused sale settled immediately");
            capital.SetPaused(false);capital.Step();var reserved=PlayerDesk.Read(capital.Engine,actor).Inventories.Single(i=>i.Id==v.Participant.InventoryId).Stock.Sum(x=>x.Reserved);Check(reserved>0,"Sale did not reserve physical goods");
            long end=capital.Engine.Tick+capitalDefinition.TradeWindowTicks+2;capital.AdvanceTo(end);capital.SetPaused(true);Check(PlayerDesk.Read(capital.Engine,actor).CashMinor>cash,"Sale did not settle through Core");capital.Engine.ValidateInvariants();
            RemoveChild(host);host.QueueFree();await Frames();_checks.Add("capital-live/queued/reserved/settled-"+language);
        }
    }
    private async Task Frames(){for(int i=0;i<2;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
}
