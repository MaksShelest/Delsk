using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Text.Json;
/// <summary>Isolated F1 ordinary native controls against actual saved orders.</summary>
public partial class ServiceRunProbe : Control
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
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(_session));if(_session.Engine.Result(_session.CommandTape.Last().Command.CommandId) is null){_session.SetPaused(false);copy.SetPaused(false);_session.Step();copy.Step();}Check(_session.Engine.ComputeStateHash()==copy.Engine.ComputeStateHash(),"Native pending command restore differs");_session.SetPaused(true);Refresh();await Frames();
    }
    private void Select(string name,long identity)
    {var b=Named<OptionButton>(name);int n=Enumerable.Range(0,b.ItemCount).Single(i=>b.GetItemMetadata(i).AsInt64()==identity);b.Select(n);b.EmitSignal(OptionButton.SignalName.ItemSelected,(long)n);}
    private ServiceRun Run()=>_session.Engine.InspectServices()!.Execution!.Runs.Single();
    private void RunUntil(Func<bool> condition)
    { _session.SetPaused(false);int bound=2000;while(!condition()&&bound-->0)_session.Step();_session.SetPaused(true);Check(bound>0,"Native delivery stalled: "+Run()); }
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();string output=args[Array.IndexOf(args,"--output")+1],fixtures=args[Array.IndexOf(args,"--fixtures")+1];Theme=TabletDesign.Theme;
            _scroll=new(){HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};_scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);AddChild(_scroll);
            foreach(string language in new[]{"ru","en"})
            {
                _language=language;GetWindow().Size=language=="ru"?new(800,720):new(1100,800);GetWindow().ContentScaleSize=GetWindow().Size;
                _session=CheckpointStore.Decode(System.IO.File.ReadAllText(fixtures+"/ready-driver.save.json"));_session.SetPaused(true);var order=_session.Engine.InspectServices()!.Orders.Single();
                await Panel(2);await Click("ServiceNavigate_"+order.Id);await Commit();RunUntil(()=>!_session.Engine.Observe(new(2)).Participant.Busy);Refresh();await Frames();
                await Click("ServicePickup_"+order.Id);await Commit();Check(Run().Cargo==0,"Pickup teleported cargo in command tick");RunUntil(()=>Run().Stage=="ToDestination");Refresh();await Frames();
                Check(Run().Cargo==30&&Run().PaidMinor==0,"Loaded cargo or pre-receipt payout incorrect");await Click("ServiceNavigate_"+order.Id);await Commit();
                string saved=output+"/transit-"+language+".save.json";CheckpointStore.Save(_session,saved);_session=CheckpointStore.Decode(System.IO.File.ReadAllText(saved));_session.SetPaused(true);
                RunUntil(()=>!_session.Engine.Observe(new(2)).Participant.Busy);Refresh();await Frames();await Click("ServiceUnload_"+order.Id);await Commit();
                _session.SetPaused(false);_session.Step();_session.SetPaused(true);Refresh();await Frames();Check(Run().Delivered>0&&Run().Cargo>0&&Run().PaidMinor>0,"No actual partial unloading/payment");
                Check(Named<Label>("ServiceProgress_"+order.Id).Text.Contains(PlayerText.Money(Run().PaidMinor)),"UI does not show actual payment");
                _scroll.EnsureControlVisible(Named<ProgressBar>("ServiceDelivered_"+order.Id));await Frames();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(output+"/service-runs-"+language+".png");
                RunUntil(()=>Run().Stage=="Completed");Check(Run().PaidMinor==order.Quote.FeeMinor&&Run().Delivered==30,"Completion mismatch");
                _session=CheckpointStore.Decode(System.IO.File.ReadAllText(fixtures+"/in-transit.save.json"));_session.SetPaused(true);order=_session.Engine.InspectServices()!.Orders.Single();
                await Panel(1);await Click("ServiceCancel_"+order.Id);await Commit();Check(Run().Cargo==30&&Run().Stage=="Returning","Cancellation destroyed or teleported cargo");
                RunUntil(()=>!_session.Engine.Observe(new(2)).Participant.Busy);await Panel(2);await Click("ServiceNavigate_"+order.Id);await Commit();RunUntil(()=>!_session.Engine.Observe(new(2)).Participant.Busy);Refresh();await Frames();
                await Click("ServiceUnload_"+order.Id);await Commit();RunUntil(()=>Run().Stage=="Cancelled");Check(Run().Returned==30&&Run().RefundedMinor==order.Quote.FeeMinor,"Native physical return/refund failed");
                _checks.Add(language+": native navigation, pickup, timed loading, transit disk save/load, unloading, partial payment/progress, completion, customer cancellation, physical return/refund");
            }
            System.IO.File.WriteAllText(output+"/service-runs-native.json",JsonSerializer.Serialize(new{status="succeeded",checks=_checks},SimulationJson.Options));
            foreach(Node child in GetChildren()){RemoveChild(child);child.QueueFree();}await Frames();GetTree().Quit();
        }
        catch(Exception ex){GD.PrintErr(ex);GetTree().Quit(2);}
    }
}
