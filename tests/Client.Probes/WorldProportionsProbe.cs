using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;
using System.Text.Json;

/// <summary>Isolated real Playfield capture. Only camera and local appearance are controlled.</summary>
public partial class WorldProportionsProbe : Control
{
    static readonly BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
    static T Field<T>(object o,string key)=>(T)o.GetType().GetField(key,Flags)!.GetValue(o)!;
    static void Set(object o,string key,object value)=>o.GetType().GetField(key,Flags)!.SetValue(o,value);
    static void Call(object o,string method,params object[] args)=>o.GetType().GetMethod(method,Flags)!.Invoke(o,args);
    static void Check(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    static IEnumerable<Node> All(Node n){yield return n;foreach(Node c in n.GetChildren())foreach(var x in All(c))yield return x;}
    async Task Frames(int n=2){for(int i=0;i<n;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    static Aabb Bounds(Node3D node)
    {
        var boxes=All(node).OfType<MeshInstance3D>().Select(m=>node.GlobalTransform.AffineInverse()*m.GlobalTransform*m.GetAabb()).ToArray();
        return boxes.Aggregate((a,b)=>a.Merge(b));
    }
    public override async void _Ready()
    {
        string output="";
        try
        {
            var args=OS.GetCmdlineUserArgs();output=args[Array.IndexOf(args,"--output")+1];string map=args[Array.IndexOf(args,"--map")+1];
            bool verify=args.Contains("--verify");GetWindow().Size=new(1600,1000);GetWindow().ContentScaleSize=GetWindow().Size;
            var app=GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();AddChild(app);await Frames();app.SetProcess(false);
            Call(app,"DismissMaps");Set(app,"_mapKey",map);Set(app,"_newNpcCount",10);Call(app,"Start","economy",false);await Frames();app.SetProcess(false);
            if(args.Contains("--load"))
            {
                string file=args[Array.IndexOf(args,"--load")+1];var save=typeof(Playfield).GetProperty("SavePath",Flags)!.GetValue(app)!.ToString()!;
                System.IO.File.Copy(file,save,true);Call(app,"Load");await Frames();Call(app,"ContinueGame");await Frames();
            }
            var session=Field<GameSession>(app,"_session");session.SetPaused(true);var scenario=session.Engine.Configuration();
            var stage=Field<PresentationStage>(app,"_stage");stage.SetProcess(false);var camera=Field<Camera3D>(stage,"_camera");
            var actors=Presentation.Read(scenario,session.Engine,1).Actors;
            var hall=scenario.Layout?.CivicObjects.Single(c=>c.Kind=="town_hall");
            var center=hall is null?new Vector3(scenario.CapitalDemand!.Terminal.X,0,scenario.CapitalDemand.Terminal.Z):new Vector3(hall.Footprint.Min(c=>c.X)+5.5f,0,hall.Footprint.Min(c=>c.Z)+1.5f);
            var measures=new List<object>();string hash=session.Engine.ComputeStateHash();
            foreach(string set in new[]{"classic","cozy"})
            {
                Set(app,"_appearance",new AppearancePreferences{WorldSet=set,PeopleSet=set});Call(app,"SaveAppearance");await Frames(3);stage.SetProcess(false);
                Check(session.Engine.ComputeStateHash()==hash,"Skin changed authoritative world");
                if(verify)
                {
                    foreach(var actor in actors)
                    {
                        var dimensions=Bounds(stage.GetNode<Node3D>("Actor_"+actor.Id+"/Appearance")).Size;
                        Check(dimensions.Y<=.481f&&dimensions.X<=.301f&&dimensions.Z<=.301f,"Actor outside agreed bounds");
                    }
                    var vehicles=Presentation.Read(scenario,session.Engine,1).Vehicles;
                    foreach(var group in vehicles.GroupBy(v=>(v.X,v.Z)))
                    {
                        var rows=group.ToArray();
                        for(int i=0;i<rows.Length;i++)for(int j=i+1;j<rows.Length;j++)
                        {
                            var first=stage.GetNode<Node3D>("Vehicle_"+rows[i].Id);var second=stage.GetNode<Node3D>("Vehicle_"+rows[j].Id);
                            var a=Bounds(first);var b=Bounds(second);var delta=first.Position-second.Position;
                            Check(Mathf.Abs(delta.X)>=(a.Size.X+b.Size.X)/2-.001f || Mathf.Abs(delta.Z)>=(a.Size.Z+b.Size.Z)/2-.001f,"Parked vehicles overlap after purchase/skin switch");
                        }
                    }
                    foreach(var v in vehicles)
                    {
                        var size=Bounds(stage.GetNode<Node3D>("Vehicle_"+v.Id)).Size;
                        Check(size.X<=(v.ModelId=="trakt_d1"?.761f:.381f)&&size.Z<=.861f,"Vehicle blocks neighbouring slot");
                    }
                    if(scenario.Layout is {} geometry)foreach(var site in geometry.CivicObjects)
                    {
                        var body=Bounds(stage.GetNode<Node3D>("Civic_"+site.Kind+"/BuildingBody"));
                        float minX=site.Body.Min(c=>c.X)-site.Footprint.Min(c=>c.X)-.5f,minZ=site.Body.Min(c=>c.Z)-site.Footprint.Min(c=>c.Z)-.5f;
                        float maxX=site.Body.Max(c=>c.X)-site.Footprint.Min(c=>c.X)+.5f,maxZ=site.Body.Max(c=>c.Z)-site.Footprint.Min(c=>c.Z)+.5f;
                        Check(body.Position.X>=minX&&body.Position.Z>=minZ&&body.End.X<=maxX&&body.End.Z<=maxZ,"Facade contradicts Core body: "+site.Kind+" "+body+" allowed "+new Vector2(minX,minZ)+".."+new Vector2(maxX,maxZ));
                    }
                }
                var person=stage.GetNode<Node3D>("Actor_1/Appearance");var bounds=Bounds(person);
                if(verify)Check(bounds.Size.Y<=.52f&&Mathf.Max(bounds.Size.X,bounds.Size.Z)<=.34f,"Person overwhelms cell: "+bounds);
                measures.Add(new{set,personBounds=new{size=bounds.Size.ToString(),position=bounds.Position.ToString()}});
                var shots=new List<(string Name,Vector3 Focus,float Distance)>{("district",center,10),("people",new(actors[0].X,0,actors[0].Z),4.2f),("overview",new(scenario.MapWidth/2f,0,scenario.MapHeight/2f),scenario.MapWidth*.9f)};
                if(scenario.Layout is {} layout)foreach(var site in layout.CivicObjects)
                {
                    int x=site.Footprint.Min(c=>c.X),z=site.Footprint.Min(c=>c.Z);float w=site.Footprint.Max(c=>c.X)-x+1,d=site.Footprint.Max(c=>c.Z)-z+1;
                    shots.Add((site.Kind,new(x+(w-1)/2,0,z+(d-1)/2),site.Kind=="dealership"?6:4.2f));
                }
                else shots.Add(("town_hall",center,4.2f));
                foreach(var shot in shots)
                {
                    Set(stage,"_cameraDistance",shot.Distance);Call(app,"Refresh");
                    camera.Position=shot.Focus+new Vector3(0,shot.Distance,shot.Distance*.7f);camera.LookAt(shot.Focus,Vector3.Up);await Frames(2);
                    await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    GetViewport().GetTexture().GetImage().SavePng(output+"/"+set+"-"+shot.Name+".png");
                }
            }
            // Exercise real cell selection at close and far camera distances with the actual pick ray.
            var target=scenario.Layout?.CivicObjects.Single(s=>s.Kind=="terminal").Entry??scenario.CapitalDemand!.Terminal;
            foreach(float distance in new[]{4.2f,10f,scenario.MapWidth*.9f})
            {
                var focus=new Vector3(target.X,0,target.Z);camera.Position=focus+new Vector3(0,distance,distance*.7f);camera.LookAt(focus,Vector3.Up);
                Simulation.Core.Cell? picked=null;void OnPick(int x,int z)=>picked=new(x,z);stage.CellChosen+=OnPick;
                stage.Pick(camera.UnprojectPosition(focus));stage.CellChosen-=OnPick;Check(picked==target,"Pick changed with scale");
            }
            var copy=CheckpointStore.Decode(CheckpointStore.Encode(session));Check(copy.Engine.ComputeStateHash()==hash,"Save restore changed geometry/state");
            Check(JsonSerializer.Serialize(copy.Engine.Configuration(),SimulationJson.Options)==JsonSerializer.Serialize(scenario,SimulationJson.Options),"Restore substituted map");
            System.IO.File.WriteAllText(output+"/native-check.json",JsonSerializer.Serialize(new{status="succeeded",map,participantCount=scenario.Participants.Length,prepared=args.Contains("--load"),layout=scenario.Layout?.Version,measures,checks=new[]{"real Playfield and resolved "+scenario.Participants.Length+"-participant world","both skin families, six camera views per designed map","skin change preserves authoritative world","cell picking at close/district/overview scales","checkpoint preserves exact resolved geometry"},scope="Native Godot/X11 software renderer; camera framed in isolated probe; not physical device acceptance"},new JsonSerializerOptions{WriteIndented=true}));
            RemoveChild(app);app.QueueFree();await Frames(8);GC.Collect();GC.WaitForPendingFinalizers();await Frames();GD.Print("WORLD_PROPORTIONS_PASS");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());if(output.Length>0)System.IO.File.WriteAllText(output+"/failure.txt",e.ToString());GetTree().Quit(1);}
    }
}
