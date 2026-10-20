using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Text.Json;

/// <summary>Compiled only into an isolated test stage, never the ordinary game.</summary>
public partial class VisualModelsProbe : Control
{
    private string _output="";
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public override async void _Ready()
    {
        var args=OS.GetCmdlineUserArgs();_output=args[Array.IndexOf(args,"--output")+1];
        try
        {
            GetWindow().Size=new(1440,900);GetWindow().ContentScaleSize=new(1440,900);
            var viewport=new SubViewport{Size=new(1440,900),RenderTargetUpdateMode=SubViewport.UpdateMode.Always,OwnWorld3D=true};AddChild(viewport);
            var texture=new TextureRect{Texture=viewport.GetTexture(),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize};texture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);AddChild(texture);
            var stage=new PresentationStage();viewport.AddChild(stage);
            PresentationMarker Yard(long id,int x,VisibleStock[] goods)=>new("warehouse",x,0,"warehouse",id,1){Footprint=[new(0,0),new(1,0)],Walls=[new(1,0)],Warehouse=new(true,200,goods)};
            var markers=new List<PresentationMarker>{Yard(1,1,[]),Yard(2,5,[new("wood",10),new("grain",15)]),Yard(3,9,[new("wood",80),new("grain",120)])};
            foreach(var yard in markers.ToArray())markers.Add(new("blocked",yard.X+1,0,"",0,0));
            foreach(var (type,x) in new[]{("sawmill",1),("bakery",5)})
            {markers.Add(new("production",x,3,type,20+x,1){Level=1});markers.Add(new("production",x+2,3,type,30+x,1){Level=2});}
            markers.Add(new("equipment",10,3,"wood",50,1){Level=1});markers.Add(new("equipment",12,3,"wood",51,1){Level=2});markers.Add(new("shop",14,3,"shop",52,1));
            var frame=new PresentationFrame(0,16,7,[new(1,true,5,5,false),new(2,false,7,5,false),new(3,false,8,5,false)],[..markers],[]);
            long geometryStart=System.Diagnostics.Stopwatch.GetTimestamp();
            stage.Show(frame,true,0,"You",false);int staticBuilds=stage.StaticBuildCount,stockBuilds=stage.StockBuildCount;
            double geometryMs=System.Diagnostics.Stopwatch.GetElapsedTime(geometryStart).TotalMilliseconds;
            long cacheStart=System.Diagnostics.Stopwatch.GetTimestamp();
            for(int i=0;i<100;i++)stage.Show(frame,true,0,"You",false);
            double unchangedFrameCpuMs=System.Diagnostics.Stopwatch.GetElapsedTime(cacheStart).TotalMilliseconds/100;
            Check(stage.StaticBuildCount==staticBuilds && stage.StockBuildCount==stockBuilds,"Unchanged frame rebuilt models");
            Check(stockBuilds==3,"Warehouse cache missing");
            stage.ShowSpeech([new(2,SpeechTopic.Build,0,3,3),new(3,SpeechTopic.Trade,0,2,3)],"en",true);
            var bubbles=All(stage).OfType<Label3D>().Where(l=>l.Text is "Ready for work!" or "Deal completed.").ToArray();
            Check(bubbles.Length==2 && bubbles[0].Position.Y!=bubbles[1].Position.Y,"Adjacent speakers overlap at the same height");
            for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            Check(viewport.GetTexture().GetImage().SavePng(System.IO.Path.Combine(_output,"models.png"))==Error.Ok,"Screenshot failed");
            markers[0]=Yard(1,1,[new("plank",200)]);long stockStart=System.Diagnostics.Stopwatch.GetTimestamp();stage.Show(frame with {Markers=[..markers]},true,0,"You",false);
            double stockChangeMs=System.Diagnostics.Stopwatch.GetElapsedTime(stockStart).TotalMilliseconds;
            Check(stage.StaticBuildCount==staticBuilds && stage.StockBuildCount==stockBuilds+1,"Stock update rebuilt geometry or failed to update");
            stage.ShowSpeech([],"en",false);
            Check(!All(stage).OfType<Label3D>().Any(l=>l.Text=="Ready for work!" && l.Visible),"Suppression left speech visible");
            var first=WorldModels.Equipment("wood",1);var next=WorldModels.Equipment("wood",2);
            Check(next.GetChildCount()>first.GetChildCount(),"Equipment levels have identical silhouette");first.Free();next.Free();
            var renderedFrames=new List<double>();
            for(int i=0;i<60;i++)
            {long start=System.Diagnostics.Stopwatch.GetTimestamp();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);renderedFrames.Add(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);}
            renderedFrames.Sort();
            var report=new{status="succeeded",staticBuilds,stockBuilds,unchangedFrames=100,changedStockBuilds=stage.StockBuildCount,
                geometryMs,unchangedFrameCpuMs,stockChangeMs,renderedFrameCount=renderedFrames.Count,averageFrameMs=renderedFrames.Average(),p95FrameMs=renderedFrames[56],
                workingSetBytes=System.Diagnostics.Process.GetCurrentProcess().WorkingSet64,platform=OS.GetName(),displayServer=DisplayServer.GetName(),
                gpuMs=(double?)null,gpuReason="GPU timers not measured; cloud Xvfb/software render is not target hardware",
                scope="Controlled render fixture; own DTOs, no economic or NPC achievement or hardware FPS acceptance claim"};
            System.IO.File.WriteAllText(System.IO.Path.Combine(_output,"models-check.json"),JsonSerializer.Serialize(report));GD.Print("DELSK_MODELS_PASS");
            GetTree().Quit(0);
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
    private static IEnumerable<Node> All(Node node){foreach(Node child in node.GetChildren()){yield return child;foreach(var n in All(child))yield return n;}}
}
