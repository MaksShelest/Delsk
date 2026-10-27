using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Text.Json;

/// <summary>Isolated E1 native controls. Uses ordinary input, money, time and physical cargo.</summary>
public partial class FleetProbe : Control
{
    private GameSession _session=null!;
    private FleetPanel _panel=null!;
    private PresentationStage _stage=null!;
    private ScrollContainer _scroll=null!;
    private string _output="";
    private readonly List<string> _checks=[];
    private static IEnumerable<Node> All(Node n){yield return n;foreach(Node c in n.GetChildren())foreach(var x in All(c))yield return x;}
    private static void Check(bool condition,string text){if(!condition)throw new InvalidOperationException(text);}
    private async Task Frames(int count=3){for(int i=0;i<count;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private T Named<T>(string name) where T:Node=>All(this).OfType<T>().Single(n=>n.Name==name);
    private async Task Click(string name)
    {
        var button=Named<Button>(name);_scroll.EnsureControlVisible(button);await Frames();
        Check(!button.Disabled&&button.IsVisibleInTree(),"Unavailable "+name);var point=button.GetGlobalRect().GetCenter();int window=GetWindow().GetWindowId();
        Input.ParseInputEvent(new InputEventMouseMotion{WindowId=window,Position=point,GlobalPosition=point});
        Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=true});await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=false});await Frames();
    }
    private void Refresh()
    {
        _panel.ShowData(PlayerDesk.Read(_session.Engine,new(1)),false);
        _stage.Show(Presentation.Read(_session.Engine.Configuration(),_session.Engine,1),true,0,"You",true);
    }
    private async Task Drive(int ticks)
    {
        _session.SetPaused(false);var copy=CheckpointStore.Decode(CheckpointStore.Encode(_session));copy.SetPaused(false);
        for(int i=0;i<ticks;i++){_session.Step();copy.Step();Check(_session.Engine.ComputeStateHash()==copy.Engine.ComputeStateHash(),"Native continuation mismatch");}
        _session.SetPaused(true);Refresh();await Frames();
    }
    private long Cargo(OwnedVehicle v)=>_session.Engine.Observe(new(1)).OwnInventories.Single(i=>i.Id==v.CargoInventory).Stock.Where(s=>s.ItemId=="wood").Sum(s=>s.Quantity);
    private async Task Capture(string name){await Frames();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(_output+"/"+name+".png");}
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();_output=args[Array.IndexOf(args,"--output")+1];string fixtures=args[Array.IndexOf(args,"--fixtures")+1];Theme=TabletDesign.Theme;
            _stage=new();AddChild(_stage);
            _scroll=new(){HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};_scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);AddChild(_scroll);
            foreach(string language in new[]{"en","ru"})
            {
                GetWindow().Size=language=="ru"?new(800,720):new(1100,800);GetWindow().ContentScaleSize=GetWindow().Size;
                foreach(Node n in _scroll.GetChildren()){_scroll.RemoveChild(n);n.QueueFree();}await Frames();
                _session=CheckpointStore.Decode(System.IO.File.ReadAllText(fixtures+"/native-fleet.save.json"));_session.SetPaused(true);
                _panel=new(language,a=>_session.Send(new(1),a),a=>_session.Engine.AccessFor(new(1),a)){SizeFlagsHorizontal=SizeFlags.ExpandFill};_scroll.AddChild(_panel);Refresh();await Frames();
                var listing=_session.Engine.FleetOf(new(1))!.Stock.Single(s=>s.ModelId=="vector200");long tick=_session.Engine.Tick,cash=_session.Engine.Observe(new(1)).Account.BalanceMinor;
                await Click("FleetBuy_"+listing.Id);Check(_session.Engine.Tick==tick,"Paused purchase advanced time");await Drive(1);
                Check(_session.Engine.InspectFleet()!.Vehicles.IsEmpty&&_session.Engine.Observe(new(1)).Account.AvailableMinor==cash-900000,"Purchase did not reserve real money");await Drive(5);
                var vehicle=_session.Engine.InspectFleet()!.Vehicles.Single();Check(_session.Engine.Observe(new(1)).Account.BalanceMinor==cash-900000,"Purchase did not debit money");
                await Click("FleetFind_"+vehicle.Id);await Drive(1);while(_session.Engine.Observe(new(1)).Participant.Busy)await Drive(1);
                await Click("FleetBoard_"+vehicle.Id);await Drive(1);Check(_session.Engine.InspectFleet()!.Vehicles.Single().Driver==1,"Boarding failed");
                Check(!_stage.GetNode<Node3D>("Actor_1").Visible,"Mounted actor was drawn outside the vehicle");
                Check(_stage.GetChildren().OfType<Node3D>().Count(n=>n.Name.ToString().StartsWith("Vehicle_",StringComparison.Ordinal))==6,"Duplicate or missing physical vehicle model");
                Named<SpinBox>("FleetTransferQuantity").Value=30;await Click("FleetTransfer");await Drive(1);Check(Cargo(vehicle)==0,"Transfer completed without a whole tick");await Drive(1);Check(Cargo(vehicle)==5,"Bare truck gained free handling power");
                string save=CheckpointStore.Encode(_session);System.IO.File.WriteAllText(_output+"/using-"+language+".save.json",save);_session=CheckpointStore.Decode(save);Refresh();await Drive(5);Check(Cargo(vehicle)==30,"Saved physical transfer did not finish");
                await Click("FleetBoard_"+vehicle.Id);await Drive(1);Check(_session.Engine.InspectFleet()!.Vehicles.Single().Driver==0&&Cargo(vehicle)==30,"Leaving duplicated or lost cargo");
                _scroll.ScrollVertical=0;await Capture("fleet-"+language);_checks.Add(language+":native-buy-hold-allocation-find-board-whole-tick-load-save-continue-leave-single-model");
            }
            foreach(Node n in GetChildren()){RemoveChild(n);n.QueueFree();}await Frames(6);
            System.IO.File.WriteAllText(_output+"/fleet-ui.json",JsonSerializer.Serialize(new{status="succeeded",checks=_checks,scope="Actual native mouse controls, prepared ordinary checkpoint, Core money/cargo/time and exact continuation. RU800x720 and EN1100x800; not hardware acceptance."},SimulationJson.Options));GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
