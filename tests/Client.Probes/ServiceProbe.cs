using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Text.Json;
/// <summary>Isolated F1 ordinary native controls against actual saved orders.</summary>
public partial class ServiceProbe : Control
{
    private GameSession _session=null!;private ServicePanel _panel=null!;private ScrollContainer _scroll=null!;private long _actor=2;private string _language="ru";
    private readonly List<string> _checks=[];
    private static IEnumerable<Node> All(Node root){yield return root;foreach(Node child in root.GetChildren())foreach(var node in All(child))yield return node;}
    private T Named<T>(string name)where T:Node=>All(this).OfType<T>().Single(n=>n.Name==name);
    private static void Check(bool yes,string reason){if(!yes)throw new InvalidOperationException(reason);}
    private async Task Frames(int n=3){for(int i=0;i<n;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private void Refresh()=>_panel.ShowData(PlayerDesk.Read(_session.Engine,new(_actor)),false);
    private async Task Panel(long actor)
    {
        _actor=actor;foreach(Node child in _scroll.GetChildren()){_scroll.RemoveChild(child);child.QueueFree();}await Frames();
        _panel=new(_session.Engine.Configuration(),_language,a=>_session.Send(new(_actor),a),terms=>_session.Engine.QuoteDeliveryService(new(_actor),terms)){SizeFlagsHorizontal=SizeFlags.ExpandFill};_scroll.AddChild(TabletDesign.Wrap(_panel));Refresh();await Frames();
    }
    private async Task Click(string name)
    {
        var b=Named<BaseButton>(name);_scroll.EnsureControlVisible(b);await Frames();Check(!b.Disabled&&b.IsVisibleInTree(),"Unavailable "+name);
        var point=b.GetGlobalRect().GetCenter();int window=GetWindow().GetWindowId();
        Input.ParseInputEvent(new InputEventMouseMotion{WindowId=window,Position=point,GlobalPosition=point});
        Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=true});await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=false});await Frames();
    }
    private async Task Commit()
    {
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(_session));_session.SetPaused(false);copy.SetPaused(false);_session.Step();copy.Step();Check(_session.Engine.ComputeStateHash()==copy.Engine.ComputeStateHash(),"Native pending command restore differs");_session.SetPaused(true);Refresh();await Frames();
    }
    private void Select(string name,long identity)
    {var b=Named<OptionButton>(name);int n=Enumerable.Range(0,b.ItemCount).Single(i=>b.GetItemMetadata(i).AsInt64()==identity);b.Select(n);b.EmitSignal(OptionButton.SignalName.ItemSelected,(long)n);}
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();string output=args[Array.IndexOf(args,"--output")+1],fixtures=args[Array.IndexOf(args,"--fixtures")+1];Theme=TabletDesign.Theme;
            _scroll=new(){HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};_scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);AddChild(_scroll);
            foreach(string language in new[]{"ru","en"})
            {
                _language=language;GetWindow().Size=language=="ru"?new(800,720):new(1100,800);GetWindow().ContentScaleSize=GetWindow().Size;
                _session=CheckpointStore.Decode(System.IO.File.ReadAllText(fixtures+"/pending.save.json"));_session.SetPaused(true);var order=_session.Engine.InspectServices()!.Orders.Single();long tick=_session.Engine.Tick;
                await Panel(2);Check(Named<ProgressBar>("ServiceResponse_"+order.Id).Value>0,"Missing response countdown");
                await Click("ServiceView_"+order.Id);Check(_session.Engine.Tick==tick&&!_session.Engine.InspectServices()!.Orders.Single().CarrierViewed,"Paused view changed state");await Commit();
                await Click("ServiceReject_"+order.Id);await Commit();Check(_session.Engine.InspectServices()!.Orders.Single().State=="Rejected","Reject failed");
                await Panel(1);var terms=order.Quote.Terms;Select("ServiceVehicle",terms.VehicleId);Select("ServiceSource",terms.Source.Value);Select("ServiceDestination",terms.Destination.Value);
                var item=Named<OptionButton>("ServiceItem");int index=Array.FindIndex(_session.Engine.Configuration().Items,i=>i.Id==terms.ItemId);item.Select(index);item.EmitSignal(OptionButton.SignalName.ItemSelected,(long)index);Named<SpinBox>("ServiceQuantity").Value=terms.Quantity;
                await Click("ServiceCalculate");Check(Named<Label>("ServiceQuote").Text.Contains(PlayerText.Money(_session.Engine.QuoteDeliveryService(new(1),terms).Quote!.FeeMinor)),"Missing calculated volume/quantity");
                await Click("ServiceRequest");await Commit();var next=_session.Engine.InspectServices()!.Orders.Last();Check(next.State=="Pending","Native request missing");
                string saved=output+"/pending-"+language+".save.json";CheckpointStore.Save(_session,saved);_session=CheckpointStore.Decode(System.IO.File.ReadAllText(saved));_session.SetPaused(true);
                long cash=_session.Engine.Observe(new(2)).Account.BalanceMinor;await Panel(2);await Click("ServiceAccept_"+next.Id);await Commit();next=_session.Engine.InspectServices()!.Orders.Last();Check(next.State=="Accepted"&&_session.Engine.Observe(new(2)).Account.BalanceMinor==cash,"Accept paid carrier early");
                _scroll.ScrollVertical=0;await Frames();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(output+"/services-"+language+".png");
                await Panel(1);await Click("ServiceCancel_"+next.Id);await Commit();Check(_session.Engine.AccountOf(new(1)).EscrowMinor==0,"Before-pickup cancellation did not refund");
                _checks.Add(language+": native quote, request, view, reject, countdown, paused commands, pending disk save, carrier accept without payout, cancellation refund");
            }
            System.IO.File.WriteAllText(output+"/services-native.json",JsonSerializer.Serialize(new{status="succeeded",checks=_checks},SimulationJson.Options));
            foreach(Node child in GetChildren()){RemoveChild(child);child.QueueFree();}await Frames();GetTree().Quit();
        }
        catch(Exception ex){GD.PrintErr(ex);GetTree().Quit(2);}
    }
}
