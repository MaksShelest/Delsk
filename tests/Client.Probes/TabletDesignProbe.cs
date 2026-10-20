using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Text.Json;

/// <summary>Isolated native UI probe over real command-driven fixtures. Never shipped.</summary>
public partial class TabletDesignProbe : Control
{
    private string _output="";
    private readonly List<string> _checks=[];
    private static void Check(bool test,string why){if(!test)throw new InvalidOperationException(why);}
    private static IEnumerable<Node> All(Node root){foreach(Node n in root.GetChildren()){yield return n;foreach(var child in All(n))yield return child;}}
    private static T Named<T>(Node root,string name) where T:Node=>All(root).OfType<T>().First(n=>n.Name==name);
    private static void Move(GameSession s,ParticipantId actor,Cell cell)
    {
        s.SetPaused(false);s.Send(actor,PlayerDesk.Move(cell.X,cell.Z));
        int guard=0;do{s.Step();if(++guard>200)throw new InvalidOperationException("Move timed out");}while(s.Engine.Observe(actor).Participant.Busy);
        Check(s.Engine.Observe(actor).Participant.Position==cell,"Move did not arrive");
    }
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();_output=args[Array.IndexOf(args,"--output")+1];
            // The scenario is passed as a filesystem path; Core/runner own all economic work.
            var d=RunArtifacts.LoadScenario(_output+"/food-chain.json");var s=new GameSession(new(d));ScriptedScenarios.FoodChain(s,d);
            foreach(string language in new[]{"ru","en"})foreach(string type in new[]{"dairy","sugar-factory","confectionery"})
            {
                var b=s.Engine.Inspect().Buildings.Single(b=>b.TypeId==type);var actor=b.OwnerId;Move(s,actor,b.Position);s.SetPaused(true);
                GetWindow().Size=new(1440,1024);GetWindow().ContentScaleSize=new(1440,1024);
                var panel=new TabletPanel(PlayerText.Building(language,type),language,type);AddChild(panel);
                string? command=null;
                var card=new HouseholdObjectCard(d,language,"building",b.Id.Value,a=>command=s.Send(actor,a),a=>s.Engine.AccessFor(actor,a));card.ArtworkHeaderChanged+=panel.UseArtworkHeader;panel.Body.AddChild(card);
                void Refresh(){card.ShowData(PlayerDesk.Read(s.Engine,actor),command is not null && s.Engine.Result(command) is null,s.Paused);panel.ShowClock(false,s.Paused,s.Engine.Tick,"");}
                Refresh();for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                var presenter=All(card).OfType<ProductionTabletPresenter>().Single();Check(presenter.Visible,"Production artwork hidden");
                var art=Named<Control>(presenter,"Panel");var e=s.Engine.Observe(actor).OwnEnterprises.Single(e=>e.Id.Value==b.Id.Value);var recipe=d.Recipes.Single(r=>r.Id==e.RecipeId);
                Check(Named<Label>(art,"Headcount").Text.StartsWith(PlayerDesk.Read(s.Engine,actor).Employment.Single(g=>g.Id.Value==e.Id.Value).Headcount.ToString()),"Demo workforce remains");
                Check(Named<Label>(art,"Output").Text==recipe.OutputQuantity.ToString(),"Demo output remains");
                Check(Named<Control>(art,"NoTransport").Visible,"No-truck overlay absent");
                if(type=="confectionery")
                {
                    Check(Named<Label>(art,"InputSugar").Text==recipe.Ingredients.Single(i=>i.ItemId=="sugar").Quantity.ToString(),"Wrong sugar quantity");
                    Check(Named<Label>(art,"InputCapacity").Text.Contains('/'),"Shared input capacity missing");
                }
                long tick=s.Engine.Tick;bool enabled=e.Enabled;
                Named<Button>(art,"PauseProduction").EmitSignal(BaseButton.SignalName.Pressed);Refresh();
                Check(s.Engine.Tick==tick && s.Engine.Observe(actor).OwnEnterprises.Single(e=>e.Id.Value==b.Id.Value).Enabled==enabled,"Paused click advanced production");
                Check(Named<Button>(art,"PauseProduction").Disabled,"Pending action not disabled");
                s.SetPaused(false);s.Step();s.SetPaused(true);Refresh();
                Check(s.Engine.Observe(actor).OwnEnterprises.Single(e=>e.Id.Value==b.Id.Value).Enabled!=enabled,"Artwork did not send the real command");
                Named<Button>(art,"WagePlus").EmitSignal(BaseButton.SignalName.Pressed);s.SetPaused(false);s.Step();s.SetPaused(true);Refresh();
                Check(Named<Label>(art,"Hint").Text.Contains(language=="ru"?"Новая ставка":"Pending wage"),"Pending wage not projected");
                await Capture(type+"-"+language);
                GetWindow().Size=new(800,600);GetWindow().ContentScaleSize=new(800,600);
                for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                Check(!presenter.Visible,"Narrow view scaled artwork instead of keeping native controls");await Capture(type+"-"+language+"-compact");
                s.Engine.ValidateInvariants();RemoveChild(panel);panel.QueueFree();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                _checks.Add(type+"-"+language+"-live/pending/compact");
            }
            System.IO.File.WriteAllText(_output+"/tablets-check.json",JsonSerializer.Serialize(new{status="succeeded",checks=_checks,scope="Actual native controls and command-driven prepared world; not target-device or long-game acceptance"}));
            GD.Print("DELSK_TABLETS_PASS");GetTree().Quit(0);
        }
        catch(Exception ex){GD.PushError(ex.ToString());GetTree().Quit(1);}
    }
    private async Task Capture(string name)
    {await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);Check(GetViewport().GetTexture().GetImage().SavePng(_output+"/"+name+".png")==Error.Ok,"Screenshot failed");}
}
