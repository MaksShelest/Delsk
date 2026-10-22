using Godot;
using Simulation.Hosting;
using System.Reflection;

/// <summary>Copied only into an isolated QA project, never shipped.</summary>
public partial class WorldSkinsProbe : Control
{
    static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static T Field<T>(object game,string key)=>(T)game.GetType().GetField(key,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(game)!;
    static void Call(Playfield game,string method,params object[] args)=>typeof(Playfield).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(game,args);
    static IEnumerable<Node> All(Node node){foreach(Node child in node.GetChildren()){yield return child;foreach(var n in All(child))yield return n;}}
    static T Named<T>(Node root,string name) where T:Node=>All(root).OfType<T>().Single(n=>n.Name==name);
    static void Press(Node root,string name)=>Named<Button>(root,name).EmitSignal(BaseButton.SignalName.Pressed);
    public override async void _Ready()
    {
        try
        {
            SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            var args=OS.GetCmdlineUserArgs();string output=args[Array.IndexOf(args,"--output")+1];
            GetWindow().Size=new(1280,900);GetWindow().ContentScaleSize=new(1280,900);
            var game=GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();AddChild(game);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);game.SetProcess(false);
            Check(WorldSkins.Preferences.WorldSet=="cozy","First-run default must be additional");
            foreach(string file in DirAccess.GetFilesAt("res://skins/cozy"))
                if(file.EndsWith(".res",StringComparison.Ordinal))Check(GD.Load<Mesh>("res://skins/cozy/"+file).GetSurfaceCount()>0,"Empty baked mesh: "+file);
            foreach(string key in AppearancePreferences.ModelKeys){var model=WorldSkins.Preview(key);Check(model.GetMeta("skin_set").AsString()=="cozy","Missing additional model: "+key);model.Free();}
            for(int i=0;i<10;i++){var model=WorldSkins.Person(i+1,false,"You","ru");Check(model.GetMeta("skin_id").AsString()==AppearanceCatalog.People[i].Asset,"Missing character");model.Free();}
            Call(game,"DismissMaps");Call(game,"Start","economy",true);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            var session=Field<GameSession>(game,"_session");var stage=Field<PresentationStage>(game,"_stage");
            string hash=session.Engine.ComputeStateHash();long tick=session.Engine.Tick;
            var actor=Named<Node3D>(stage,"Actor_1");ulong actorId=actor.GetInstanceId();var position=actor.Position;
            int builds=stage.StaticBuildCount;Call(game,"Refresh");Check(stage.StaticBuildCount==builds,"Unchanged frame rebuilt world");
            Call(game,"OpenAppearanceSettings");await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            var dialog=Named<AcceptDialog>(game,"AppearanceSettings");
            Check(Named<OptionButton>(dialog,"CharacterSkin").ItemCount==10,"Settings must offer ten people");
            Press(dialog,"WorldSkinSet");Check(WorldSkins.Preferences.WorldSet=="classic","Primary selection failed");
            Press(dialog,"ChangeBuildingSkin");Check(WorldSkins.Preferences.ForModel("sawmill")=="cozy","Per-building override failed");
            Press(dialog,"NextCharacterSkin");Check(WorldSkins.Preferences.ForActor(1)==1,"Next character button failed");
            Check(actor.GetNode<Node3D>("Appearance").GetMeta("skin_id").AsString()=="NPC_02_Mikhail","Visible actor did not switch");
            Check(actor.GetInstanceId()==actorId && actor.Position==position,"Cosmetic switch reset actor motion");
            Check(hash==session.Engine.ComputeStateHash() && tick==session.Engine.Tick,"Cosmetics mutated simulation");
            var saved=System.Text.Json.JsonSerializer.Deserialize<AppearancePreferences>(Godot.FileAccess.GetFileAsString("user://appearance.json"))!;
            Check(saved.WorldSet=="classic"&&saved.ForActor(1)==1&&saved.ForModel("sawmill")=="cozy","Selections not persisted");
            Call(game,"InitializeAppearance");Check(WorldSkins.Preferences.ForActor(1)==1,"Preferences failed to reload");
            Press(dialog,"WorldSkinSet");
            var type=Named<OptionButton>(dialog,"ModelType");type.Select(4);type.EmitSignal(OptionButton.SignalName.ItemSelected,4L);
            for(int i=0;i<5;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(output+"/appearance-settings.png");
            dialog.EmitSignal(AcceptDialog.SignalName.Confirmed);
            Call(game,"ContinueGame");
            for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(output+"/integrated-world.png");
            game.Visible=false;WorldSkins.Configure(new AppearancePreferences());
            var gallery=new PanelContainer {Theme=TabletDesign.Theme};gallery.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);AddChild(gallery);
            var column=new VBoxContainer();gallery.AddChild(column);
            column.AddChild(TabletDesign.Heading("Delsk · Козырьки и десять именных образов",28));
            var buildings=new HBoxContainer();column.AddChild(buildings);
            foreach(string key in new[]{"confectionery","capital","shop"})
            {var preview=new SkinPreview();buildings.AddChild(preview);preview.ShowModel(WorldSkins.Preview(key),capital:key=="capital");}
            for(int row=0;row<2;row++)
            {
                var line=new HBoxContainer();column.AddChild(line);
                for(int i=row*5;i<row*5+5;i++)
                {var cell=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};line.AddChild(cell);var preview=new SkinPreview{CustomMinimumSize=new(220,185)};cell.AddChild(preview);preview.ShowModel(WorldSkins.Person(i+1,false,"","ru"),true);cell.AddChild(new Label{Text=AppearanceCatalog.Name(i,"ru"),HorizontalAlignment=HorizontalAlignment.Center});}
            }
            for(int i=0;i<4;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(output+"/models-and-people.png");
            GD.Print("WORLD_SKINS_PASS: 13 model types, 10 people, defaults, per-model switch, next character, persistence, stable actor, unchanged world hash/tick, static cache");
            GetTree().Quit(0);
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
}
