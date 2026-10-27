using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Text.Json;

/// <summary>Isolated ordinary E2 native controls. No research mutations or alternate economy.</summary>
public partial class RoadProbe : Control
{
    private GameSession _session=null!;
    private RoadPanel _panel=null!;
    private PresentationStage _stage=null!;
    private ScrollContainer _scroll=null!;
    private string _output="";
    private readonly List<string> _checks=[];
    private static IEnumerable<Node> All(Node node){yield return node;foreach(Node child in node.GetChildren())foreach(var n in All(child))yield return n;}
    private static void Check(bool value,string text){if(!value)throw new InvalidOperationException(text);}
    private async Task Frames(int count=3){for(int i=0;i<count;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private T Named<T>(string name)where T:Node=>All(this).OfType<T>().Single(n=>n.Name==name);
    private async Task Click(string name)
    {
        var button=Named<Button>(name);_scroll.EnsureControlVisible(button);await Frames();
        Check(!button.Disabled&&button.IsVisibleInTree(),"Unavailable "+name);var point=button.GetGlobalRect().GetCenter();int window=GetWindow().GetWindowId();
        Input.ParseInputEvent(new InputEventMouseMotion{WindowId=window,Position=point,GlobalPosition=point});
        Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=true});await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton{WindowId=window,Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=false});await Frames();
    }
    private void Refresh()
    {_panel.ShowData(PlayerDesk.Read(_session.Engine,new(1)),false,_session.Paused);_stage.Show(Presentation.Read(_session.Engine.Configuration(),_session.Engine,1),true,0,"You",true);}
    private async Task Drive(int ticks)
    {
        _session.SetPaused(false);var copy=CheckpointStore.Decode(CheckpointStore.Encode(_session));copy.SetPaused(false);
        for(int i=0;i<ticks;i++){_session.Step();copy.Step();Check(_session.Engine.ComputeStateHash()==copy.Engine.ComputeStateHash(),"Native road continuation differs");}
        _session.SetPaused(true);Refresh();await Frames();
    }
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();_output=args[Array.IndexOf(args,"--output")+1];string fixtures=args[Array.IndexOf(args,"--fixtures")+1];Theme=TabletDesign.Theme;
            _stage=new();AddChild(_stage);_scroll=new(){HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};_scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);AddChild(_scroll);
            foreach(string language in new[]{"en","ru"})
            {
                GetWindow().Size=language=="ru"?new(800,720):new(1100,800);GetWindow().ContentScaleSize=GetWindow().Size;
                foreach(Node node in _scroll.GetChildren()){_scroll.RemoveChild(node);node.QueueFree();}await Frames();
                _session=CheckpointStore.Decode(System.IO.File.ReadAllText(fixtures+"/native-road.save.json"));_session.SetPaused(true);
                _panel=new(language,a=>_session.Send(new(1),a),a=>_session.Engine.AccessFor(new(1),a)){SizeFlagsHorizontal=SizeFlags.ExpandFill};_scroll.AddChild(_panel);Refresh();await Frames();
                Named<OptionButton>("RoadLevel").Select(1);Named<OptionButton>("RoadMode").Select(0);Named<LineEdit>("RoadRoute").Text="10,1";
                var v=_session.Engine.InspectFleet()!.Vehicles.Single();long tick=_session.Engine.Tick,cash=_session.Engine.Observe(new(1)).Account.BalanceMinor;
                long gravel=_session.Engine.Observe(new(1)).OwnInventories.Single(i=>i.Id==v.CargoInventory).Stock.Single(s=>s.ItemId=="gravel").Quantity;
                await Click("RoadStart");Check(_session.Engine.Tick==tick,"Paused road click advanced time");await Drive(1);
                var batch=_session.Engine.InspectRoads()!.Works.Single().Batch!;Check(batch.Work==0&&batch.Level==2,"Road work completed without whole ticks");
                Check(_session.Engine.Observe(new(1)).Account.BalanceMinor==cash-400,"Road startup was not paid");
                Check(_session.Engine.Observe(new(1)).OwnInventories.Single(i=>i.Id==v.CargoInventory).Stock.Single(s=>s.ItemId=="gravel").Quantity==gravel-2,"Road materials were not consumed from real cargo");
                Check(_session.Engine.InspectRoads()!.Bypasses.Single().Path.All(c=>_session.Engine.Configuration().Layout!.RoadCorridorCells.Contains(c)),"Bypass left public corridor");
                await Drive(5);string save=CheckpointStore.Encode(_session);System.IO.File.WriteAllText(_output+"/road-in-use-"+language+".save.json",save);_session=CheckpointStore.Decode(save);Refresh();
                long work=_session.Engine.InspectRoads()!.Works.Single().Batch!.Work;await Click("RoadStop");await Drive(3);Check(_session.Engine.InspectRoads()!.Works.Single().Batch!.Work==work,"Paused work progressed");
                await Click("RoadStart");await Drive(1);int bound=50;while(_session.Engine.InspectRoads()!.Works.Single().Batch is not null&&bound-->0)await Drive(1);Check(bound>0,"Road work did not finish");
                var road=_session.Engine.InspectRoads()!.Cells.Single(c=>c.Cell==new Cell(10,1));await Drive(1);Check(_session.Engine.InspectRoads()!.Works.Single().Reason=="RouteCompleted","Completed work still appears active or paused on the next tick");
                Check(road.Level==2&&road.Contributions.Select(c=>c.Builder.Value).SequenceEqual(new long[]{0,1}),"City base or private contribution lost");
                Check(!_stage.GetNode<Node3D>("Actor_1").Visible,"Mounted road operator was duplicated");
                Check(_stage.GetChildren().OfType<Node3D>().Count(n=>n.Name.ToString()=="Vehicle_"+v.Id)==1,"Physical paver duplicated");
                Check(_stage.GetNodeOrNull<Node3D>("Road_10_1_2") is not null,"Completed surface was not rendered");
                _scroll.ScrollVertical=0;await Frames();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(_output+"/road-"+language+".png");
                _checks.Add(language+":native-route-paid-materials-whole-ticks-pause-save-resume-layers-single-paver");
            }
            _session=CheckpointStore.Decode(System.IO.File.ReadAllText(fixtures+"/ExistingExtractionMachineUsesSameBypassAndContinuesMiningAfterLoad.save.json"));
            var machine=_session.Engine.Inspect().Equipment.Single();Refresh();await Frames();
            Check(_stage.GetNodeOrNull<Node3D>("RoadEquipment_"+machine.Id.Value) is not null,"Existing extraction equipment was not rendered on shared traffic");
            Check(Presentation.Read(_session.Engine.Configuration(),_session.Engine,1).Markers.All(m=>m.Kind!="equipment"),"Extraction machine also rendered as a static marker");
            _checks.Add("existing-extraction-one-dynamic-body");
            foreach(Node node in GetChildren()){RemoveChild(node);node.QueueFree();}await Frames(6);
            System.IO.File.WriteAllText(_output+"/roads-ui.json",JsonSerializer.Serialize(new{status="succeeded",checks=_checks,scope="Actual native mouse controls and 3D, prepared ordinary paid paver/cargo checkpoint; RU800x720 and EN1100x800. Not device or long-run balance acceptance."},SimulationJson.Options));GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
