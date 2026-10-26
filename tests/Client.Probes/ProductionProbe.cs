using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Text.Json;

/// <summary>Isolated ordinary D3 panels, native mouse input and the real command/validator path.</summary>
public partial class ProductionProbe:Control
{
    private readonly List<string> _checks=[];
    private string _output="";
    private GameSession _session=null!;
    private VBoxContainer _body=null!;
    private string _language="en";
    private static IEnumerable<Node> All(Node n){yield return n;foreach(Node c in n.GetChildren())foreach(var x in All(c))yield return x;}
    private static void Check(bool b,string message){if(!b)throw new InvalidOperationException(message);}
    private async Task Frames(int count=3){for(int i=0;i<count;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private async Task Click(Button button)
    {
        for(Node? p=button.GetParent();p is not null;p=p.GetParent())if(p is ScrollContainer scroll)scroll.EnsureControlVisible(button);
        await Frames();Check(!button.Disabled&&button.IsVisibleInTree(),"Unavailable "+button.Name);var position=button.GetGlobalRect().GetCenter();int id=GetWindow().GetWindowId();
        Input.ParseInputEvent(new InputEventMouseMotion{WindowId=id,Position=position,GlobalPosition=position});
        Input.ParseInputEvent(new InputEventMouseButton{WindowId=id,Position=position,GlobalPosition=position,ButtonIndex=MouseButton.Left,Pressed=true});await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton{WindowId=id,Position=position,GlobalPosition=position,ButtonIndex=MouseButton.Left,Pressed=false});await Frames();
    }
    private Button Named(string name)=>All(this).OfType<Button>().Single(b=>b.Name==name);
    private void Submit(GameAction a)=>_session.Send(new(1),a);
    private WorkshopPanel? _workshops;private ImportPanel? _imports;
    private void Refresh()
    {
        var view=PlayerDesk.Read(_session.Engine,new(1));_workshops?.ShowData(view,false);_imports?.ShowView(view);
    }
    private async Task Load(string path,bool production)
    {
        foreach(var child in _body.GetChildren()){_body.RemoveChild(child);child.QueueFree();}await Frames();_workshops=null;_imports=null;
        _session=CheckpointStore.Decode(System.IO.File.ReadAllText(path));_session.SetPaused(true);
        if(production){_workshops=new(_session.Engine.Configuration(),_language,8,Submit,a=>_session.Engine.AccessFor(new(1),a));_body.AddChild(_workshops);}
        else{_imports=new(_language,Submit);_body.AddChild(_imports);}Refresh();await Frames();
    }
    private async Task Drive(long ticks)
    {
        _session.SetPaused(false);var copy=CheckpointStore.Decode(CheckpointStore.Encode(_session));copy.SetPaused(false);
        for(int i=0;i<ticks;i++){_session.Step();copy.Step();Check(_session.Engine.ComputeStateHash()==copy.Engine.ComputeStateHash(),"Restored native tick differs");}
        _session.SetPaused(true);Refresh();await Frames();
    }
    private async Task Capture(string name){await Frames();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(_output+"/"+name+".png");}
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();_output=args[Array.IndexOf(args,"--output")+1];string fixtures=args[Array.IndexOf(args,"--fixtures")+1];
            Theme=TabletDesign.Theme;
            var scroll=new ScrollContainer{HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);AddChild(scroll);_body=new(){SizeFlagsHorizontal=SizeFlags.ExpandFill};scroll.AddChild(_body);
            foreach(var language in new[]{"en","ru"})
            {
                _language=language;GetWindow().Size=language=="ru"?new(800,720):new(1100,800);GetWindow().ContentScaleSize=GetWindow().Size;
                await Load(fixtures+"/native-modernize.save.json",true);long tick=_session.Engine.Tick,cash=_session.Engine.Observe(new(1)).Account.BalanceMinor;
                await Click(Named("Workshop_modernize_8"));Check(_session.Engine.Tick==tick&&_session.Engine.ProductionContentOf(new(1))!.Projects.IsEmpty,"Paused button advanced economy");await Drive(1);
                Check(_session.Engine.Observe(new(1)).Account.BalanceMinor==cash-30000,"Modernization did not pay");var end=_session.Engine.ProductionContentOf(new(1))!.Projects.Single().EndTick;
                Check(Named("Workshop_modernize_8").Disabled,"Project button not disabled");await Capture("modernization-"+language);await Drive(end-_session.Engine.Tick);
                await Click(Named("Workshop_recipe_8_saw_planks_v2"));await Drive(1);Check(_session.Engine.Observe(new(1)).OwnEnterprises.Single().RecipeId=="saw_planks_v2","V2 selection failed");
                await Click(Named("Workshop_recipe_8_saw_planks"));await Drive(1);Check(_session.Engine.Observe(new(1)).OwnEnterprises.Single().RecipeId=="saw_planks","V1 selection failed");_checks.Add(language+":paid-local-120-tick-modernization-select-v2-and-v1-save-hashes");
                await Load(fixtures+"/native-import.save.json",false);tick=_session.Engine.Tick;cash=_session.Engine.Observe(new(1)).Account.BalanceMinor;
                await Click(Named("ImportBuy_steel"));Check(_session.Engine.Tick==tick,"Paused import advanced");await Drive(1);
                var content=_session.Engine.ProductionContentOf(new(1))!;Check(content.Imports.Single(i=>i.ItemId=="steel").Remaining==11&&_session.Engine.Observe(new(1)).Account.BalanceMinor==cash-3125,"Finite paid import failed");
                Check(_session.Engine.Observe(new(1)).OwnInventories.Single(i=>i.Id==_session.Engine.Observe(new(1)).Participant.InventoryId).Stock.All(i=>i.ItemId!="steel"||i.Quantity==0),"Import teleported");
                await Click(Named("ImportTake_steel"));await Drive(2);Check(_session.Engine.Observe(new(1)).OwnInventories.Single(i=>i.Id==_session.Engine.Observe(new(1)).Participant.InventoryId).Stock.Single(i=>i.ItemId=="steel").Quantity==1,"Physical pickup failed");scroll.ScrollVertical=0;await Capture("import-"+language);_checks.Add(language+":finite-import-buy-collect-live-stock-pause");
            }
            foreach(var child in GetChildren()){RemoveChild(child);child.QueueFree();}await Frames(5);
            System.IO.File.WriteAllText(_output+"/production-ui.json",JsonSerializer.Serialize(new{status="succeeded",checks=_checks,scope="Real native panels and Core commands; prepared modernization XP/materials, ordinary import stock/cash. RU800x720/EN1100x800. Not hardware acceptance."},SimulationJson.Options));GetTree().Quit();
        }
        catch(Exception ex){GD.PushError(ex.ToString());GetTree().Quit(1);}
    }
}
