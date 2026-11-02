using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Text.Json;

/// <summary>Isolated native verification of the saved I1 physics and its actual scene projection.</summary>
public partial class I1MovementProbe : Control
{
    static void Check(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    static IEnumerable<Node> All(Node n){yield return n;foreach(Node c in n.GetChildren())foreach(var x in All(c))yield return x;}
    async Task Frames(int n=2){for(int i=0;i<n;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();string Arg(string key)=>args[Array.IndexOf(args,key)+1];
            string fixtures=Arg("--fixtures"),output=Arg("--output");Directory.CreateDirectory(output);
            var s=CheckpointStore.Load(Path.Combine(fixtures,"native-highway.save.json")).Session;
            using var expected=JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures,"native-highway.expected.json")));
            long vehicle=expected.RootElement.GetProperty("vehicle").GetInt64();
            Check(s.Engine.Configuration().Economy!.Movement==MovementRules.I1,"Wrong saved movement profile");
            Check(s.Engine.TravelQuantum==3,"Expected a save at the third quantum");
            var stage=new PresentationStage();AddChild(stage);await Frames();
            var frame=Presentation.Read(s.Engine.Configuration(),s.Engine,2);stage.Show(frame,true,0,"Driver",true);
            await Frames();GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,"i1-loaded.png"));
            for(int q=0;q<7;q++)s.AdvanceTravelQuantum();
            Check(s.Engine.Tick==expected.RootElement.GetProperty("tick").GetInt64(),"Economic tick changed");
            Check(s.Engine.ComputeStateHash()==expected.RootElement.GetProperty("hash").GetString(),"Native/runner continuation differs");
            frame=Presentation.Read(s.Engine.Configuration(),s.Engine,2);stage.Show(frame,true,0,"Driver",true);await Frames();
            var node=stage.GetNode<Node3D>("Vehicle_"+vehicle);Check(Math.Abs(node.Position.X-12.5f)<.001f,"Native vehicle did not cover4.5cells/second");
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,"i1-highway.png"));
            foreach(string language in new[]{"ru","en"})
            {
                var help=new HelpOverlay(language,"basics",s.Engine.Configuration().Economy!.Movement);AddChild(help);await Frames();
                Check(All(help).OfType<RichTextLabel>().Any(t=>t.Text.Contains(language=="ru"?"10 км/ч":"10 km/h",StringComparison.Ordinal)),"Missing nominal speed in help");
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,"i1-help-"+language+".png"));
                RemoveChild(help);help.QueueFree();await Frames();
            }
            s.SetTravelHeading(new(2),0,0);s.CompleteTravelTick();Check(s.Engine.Observe(new(2)).Participant.Position==new Cell(13,1),"Unsafe stop");
            CheckpointStore.Save(s,Path.Combine(output,"native-stopped.save.json"));
            var loaded=CheckpointStore.Load(Path.Combine(output,"native-stopped.save.json")).Session;
            Check(s.Engine.ComputeStateHash()==loaded.Engine.ComputeStateHash(),"Native save/readback mismatch");
            File.WriteAllText(Path.Combine(output,"native-check.json"),JsonSerializer.Serialize(new{status="succeeded",movement=2,cellsPerSecond=4.5,nominal=60,nativeHash=expected.RootElement.GetProperty("hash").GetString(),finalHash=s.Engine.ComputeStateHash(),quantumRestored=3,helpLanguages=new[]{"ru","en"},scope="Native Linux Godot, actual scene coordinates and save/readback; synthetic fixture, no physical device or GPU FPS claim"}));
            GetTree().Quit();
        }
        catch(Exception ex){GD.PushError(ex.ToString());GetTree().Quit(1);}
    }
}
