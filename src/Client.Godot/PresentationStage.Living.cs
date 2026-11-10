using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class PresentationStage
{
    private string _landKey="";
    private MeshInstance3D? _land;
    private PresentationFrame? _shown;
    private bool _ownership;
    private Cell[] _preview=[];
    private MeshInstance3D? _route;
    private string _routeKey="";
    private readonly Dictionary<long,MeshInstance3D> _activity=[];
    private string _activityKey="";

    private static void BindLimbs(Mover mover)
    {
        mover.LegL=mover.Node.FindChild("LegL",true,false) as Node3D;
        mover.LegR=mover.Node.FindChild("LegR",true,false) as Node3D;
        mover.ArmL=mover.Node.FindChild("ArmL",true,false) as Node3D;
        mover.ArmR=mover.Node.FindChild("ArmR",true,false) as Node3D;
        mover.Bob=mover.Node.FindChild("Model",true,false) as Node3D;
        if(mover.Bob is not null)mover.BobRest=mover.Bob.Position.Y;
        ResetLimbs(mover);
    }

    private static void ResetLimbs(Mover mover)
    {
        if(mover.LegL is not null)mover.LegL.Rotation=Vector3.Zero;
        if(mover.LegR is not null)mover.LegR.Rotation=Vector3.Zero;
        if(mover.ArmL is not null)mover.ArmL.Rotation=Vector3.Zero;
        if(mover.ArmR is not null)mover.ArmR.Rotation=Vector3.Zero;
        if(mover.Bob is not null){mover.Bob.Position=new(mover.Bob.Position.X,mover.BobRest,mover.Bob.Position.Z);mover.Bob.Rotation=Vector3.Zero;}
    }

    private static void StepWalk(Mover mover,double delta,bool live)
    {
        if(!mover.Node.Visible||!live){EaseLimbs(mover,0,(float)delta);return;}
        var now=mover.Node.Position;
        float speed=0;
        if(mover.SnapWalk){mover.SnapWalk=false;mover.Last=now;mover.LastSet=true;}
        else if(mover.LastSet)speed=now.DistanceTo(mover.Last)/Mathf.Max((float)delta,0.001f);
        mover.Last=now;mover.LastSet=true;
        float swing=speed<0.05f?0:Mathf.Clamp(speed*0.18f,0.08f,0.34f);
        if(swing>0)mover.Stride+=(float)delta*Mathf.Clamp(speed*5.2f,1.8f,7.2f);
        float angle=Mathf.Sin(mover.Stride)*swing;
        if(mover.LegL is null)
        {
            if(mover.Bob is null)return;
            float bob=swing>0?Mathf.Sin(mover.Stride*2f)*0.016f:0;
            float sway=swing>0?Mathf.Sin(mover.Stride)*0.045f:0;
            var place=mover.Bob.Position;
            mover.Bob.Position=new(place.X,Mathf.Lerp(place.Y,mover.BobRest+bob,0.35f),place.Z);
            mover.Bob.Rotation=new(0,0,Mathf.Lerp(mover.Bob.Rotation.Z,sway,0.35f));
            return;
        }
        EaseLimbs(mover,angle,(float)delta);
    }

    private static void EaseLimbs(Mover mover,float angle,float delta)
    {
        Ease(mover.LegL,angle,delta);Ease(mover.LegR,-angle,delta);
        Ease(mover.ArmL,-angle*0.65f,delta);Ease(mover.ArmR,angle*0.65f,delta);
    }

    private static void Ease(Node3D? joint,float target,float delta)
    {
        if(joint is null)return;
        float next=Mathf.Lerp(joint.Rotation.X,target,1f-Mathf.Exp(-delta*12f));
        joint.Rotation=new(next,0,0);
    }

    private void StepMechanisms()
    {
        foreach(var mover in _fleet.Values)
        {
            var arm=mover.Node.GetNodeOrNull<MeshInstance3D>("WorkArm");
            if(arm is null||!arm.Visible)continue;
            arm.Rotation=new(Mathf.Sin(Time.GetTicksMsec()/1000f*2.2f)*0.16f,0,0);
        }
    }

    public void SetPreview(IReadOnlyList<Cell> cells)
    {
        _preview=cells.Count==0?[]:cells.ToArray();
        if(_shown is {} frame)ShowLand(frame,_ownership);
    }

    public void ShowRoute(Cell[]? path)
    {
        string key=path is null?"":string.Join(";",path.Select(c=>$"{c.X},{c.Z}"));
        if(key==_routeKey)return;
        _routeKey=key;
        if(_route is not null){_route.QueueFree();_route=null;}
        if(path is null||path.Length==0)return;
        var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        var ink=new Color(0.33f,0.46f,0.28f,0.9f);
        var mark=new Color(0.95f,0.9f,0.62f,0.95f);
        for(int i=1;i<path.Length;i++)
        {
            AddEdge(tool,path[i-1].X,path[i-1].Z,path[i].X,path[i].Z,ink,0.032f,0.03f);
            AddChevron(tool,path[i-1],path[i],mark,0.045f);
        }
        var end=path[^1];
        AddRing(tool,end.X,end.Z,mark,0.016f,0.05f);
        tool.GenerateNormals();
        _route=new MeshInstance3D{Name="GoalRoute",Mesh=tool.Commit(),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
        _route.MaterialOverride=new StandardMaterial3D{VertexColorUseAsAlbedo=true,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,Transparency=BaseMaterial3D.TransparencyEnum.Alpha,CullMode=BaseMaterial3D.CullModeEnum.Disabled};
        AddChild(_route);
    }

    private void ShowLand(PresentationFrame frame,bool ownership)
    {
        var parcels=frame.Markers.Where(m=>m.Kind is "plot-own" or "plot-foreign" or "plot-reserved" or "plot-free" or "shop-plot")
            .Select(m=>new LandParcel(m.X,m.Z,m.OwnerId,m.Kind)).ToArray();
        var borders=LandOutline.Borders(parcels,ownership);
        string key=ownership+"|"+string.Join(",",_preview.Select(c=>$"{c.X}:{c.Z}"))+"|"+string.Join(";",borders.Select(b=>$"{b.State}:{b.Owner}:{b.X1:0.00}:{b.Z1:0.00}:{b.X2:0.00}:{b.Z2:0.00}"));
        if(key==_landKey&&_land is not null)return;
        _landKey=key;
        if(_land is not null){_land.QueueFree();_land=null;}
        if(borders.Length==0&&_preview.Length==0)return;
        var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        foreach(var edge in borders)AddEdge(tool,edge.X1,edge.Z1,edge.X2,edge.Z2,BorderColor(edge,frame),edge.State=="plot-own"?0.045f:0.028f,0.09f);
        var chosen=_preview.ToHashSet();
        var fill=new Color(0.86f,0.91f,0.78f,0.22f);
        var line=new Color(0.96f,0.93f,0.82f,0.9f);
        foreach(var cell in _preview)
        {
            AddFill(tool,cell.X,cell.Z,fill,0.025f);
            if(!chosen.Contains(new(cell.X,cell.Z-1)))AddEdge(tool,cell.X-.5f,cell.Z-.5f,cell.X+.5f,cell.Z-.5f,line,0.016f,0.04f);
            if(!chosen.Contains(new(cell.X+1,cell.Z)))AddEdge(tool,cell.X+.5f,cell.Z-.5f,cell.X+.5f,cell.Z+.5f,line,0.016f,0.04f);
            if(!chosen.Contains(new(cell.X,cell.Z+1)))AddEdge(tool,cell.X+.5f,cell.Z+.5f,cell.X-.5f,cell.Z+.5f,line,0.016f,0.04f);
            if(!chosen.Contains(new(cell.X-1,cell.Z)))AddEdge(tool,cell.X-.5f,cell.Z+.5f,cell.X-.5f,cell.Z-.5f,line,0.016f,0.04f);
        }
        tool.GenerateNormals();
        _land=new MeshInstance3D{Name="LandOutline",Mesh=tool.Commit(),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
        _land.MaterialOverride=new StandardMaterial3D{VertexColorUseAsAlbedo=true,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,Transparency=BaseMaterial3D.TransparencyEnum.Alpha,CullMode=BaseMaterial3D.CullModeEnum.Disabled};
        AddChild(_land);
    }

    private static Color BorderColor(LandBorder edge,PresentationFrame frame)
    {
        bool human=frame.Actors.Any(a=>a.Human&&a.Id==edge.Owner);
        var color=edge.State switch
        {
            "plot-free"=>new Color("9aa59a"),
            "shop-plot"=>new Color("8d7e9e"),
            "plot-reserved"=>new Color("c4a15a"),
            _=>MapSymbols.ActorColor(edge.Owner,human)
        };
        if(color==Colors.White)color=new Color("d7e3c8");
        color.A=edge.State=="plot-own"?0.95f:edge.State=="plot-free"?0.45f:0.7f;
        return color;
    }

    private static void AddRing(SurfaceTool tool,int x,int z,Color color,float width,float y)
    {
        AddEdge(tool,x-.5f,z-.5f,x+.5f,z-.5f,color,width,y);
        AddEdge(tool,x+.5f,z-.5f,x+.5f,z+.5f,color,width,y);
        AddEdge(tool,x+.5f,z+.5f,x-.5f,z+.5f,color,width,y);
        AddEdge(tool,x-.5f,z+.5f,x-.5f,z-.5f,color,width,y);
    }

    private static void AddFill(SurfaceTool tool,int x,int z,Color color,float y)
    {
        void Vert(float px,float pz){tool.SetColor(color);tool.AddVertex(new(px,y,pz));}
        float x0=x-.5f,x1=x+.5f,z0=z-.5f,z1=z+.5f;
        Vert(x0,z0);Vert(x1,z0);Vert(x1,z1);Vert(x0,z0);Vert(x1,z1);Vert(x0,z1);
    }

    private static void AddChevron(SurfaceTool tool,Cell from,Cell to,Color color,float y)
    {
        var dir=new Vector3(to.X-from.X,0,to.Z-from.Z);
        if(dir.LengthSquared()<0.01f)return;
        dir=dir.Normalized();
        var side=new Vector3(-dir.Z,0,dir.X);
        var tip=new Vector3(from.X,y,from.Z)+dir*0.72f;
        var left=tip-dir*0.14f+side*0.07f;
        var right=tip-dir*0.14f-side*0.07f;
        void Vert(Vector3 p){tool.SetColor(color);tool.AddVertex(p);}
        Vert(left);Vert(tip);Vert(right);Vert(right);Vert(tip);Vert(left);
    }

    private static void AddEdge(SurfaceTool tool,float x1,float z1,float x2,float z2,Color color,float width,float y)
    {
        var a=new Vector3(x1,y,z1);var b=new Vector3(x2,y,z2);var dir=b-a;dir.Y=0;
        if(dir.LengthSquared()<0.0001f)return;
        var side=new Vector3(-dir.Z,0,dir.X).Normalized()*width;
        var p0=a-side;var p1=a+side;var p2=b+side;var p3=b-side;
        void Vert(Vector3 p){tool.SetColor(color);tool.AddVertex(p);}
        Vert(p0);Vert(p1);Vert(p2);Vert(p0);Vert(p2);Vert(p3);
    }

    public void ShowActivity(PlayerView view)
    {
        var marks=new List<(long Id,Vector3 At,Color Color,bool Arm)>();
        foreach(var unit in view.Enterprises)
        {
            string state=CityMap.ProductionState(unit);
            if(state is not ("Working" or "Stopped" or "Blocked"))continue;
            var building=view.Buildings.FirstOrDefault(b=>b.Id.Value==unit.Id.Value);if(building is null)continue;
            var color=state=="Working"?new Color("3f8f62"):state=="Stopped"?new Color("c4a15a"):new Color("b86a45");
            marks.Add((unit.Id.Value,new Vector3(building.Position.X,1.35f,building.Position.Z),color,false));
        }
        foreach(var job in view.Storage?.Construction??[])
            marks.Add((1_000_000+job.Id,new Vector3(job.Position.X,1.15f,job.Position.Z),new Color("d08a3a"),false));
        foreach(var machine in view.Equipment??[])
        {
            if(CityMap.EquipmentState(machine)!="Harvesting")continue;
            marks.Add((2_000_000+machine.Id.Value,new Vector3(machine.Position.X,1.05f,machine.Position.Z),new Color("6e9a62"),true));
        }
        string key=string.Join(";",marks.Select(m=>$"{m.Id}:{m.At.X:0.0}:{m.At.Z:0.0}:{m.Color.ToHtml()}"));
        if(key==_activityKey)return;
        _activityKey=key;
        var live=new HashSet<long>();
        foreach(var mark in marks)
        {
            live.Add(mark.Id);
            if(!_activity.TryGetValue(mark.Id,out var bead))
            {
                var material=new StandardMaterial3D{ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,AlbedoColor=mark.Color};
                bead=new MeshInstance3D{Mesh=new SphereMesh{Radius=.055f,Height=.11f,RadialSegments=8,Rings=4},CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,MaterialOverride=material};
                AddChild(bead);_activity[mark.Id]=bead;
            }
            bead.Position=mark.At;if(bead.MaterialOverride is StandardMaterial3D paint)paint.AlbedoColor=mark.Color;
            if(_fleet.TryGetValue(mark.Id-2_000_000,out var vehicle))EnsureArm(vehicle,mark.Arm);
        }
        foreach(var id in _activity.Keys.Where(id=>!live.Contains(id)).ToArray()){ _activity[id].QueueFree();_activity.Remove(id); }
        foreach(var pair in _fleet)if(!live.Contains(2_000_000+pair.Key))HideArm(pair.Value);
    }

    private static void EnsureArm(Mover mover,bool on)
    {
        var arm=mover.Node.GetNodeOrNull<MeshInstance3D>("WorkArm");
        if(!on){if(arm is not null)arm.Visible=false;return;}
        if(arm is null)
        {
            arm=new MeshInstance3D{Name="WorkArm",Mesh=new BoxMesh{Size=new(.06f,.2f,.06f)},Position=new(0,.58f,.16f),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
            arm.MaterialOverride=new StandardMaterial3D{AlbedoColor=new("c4a15a"),ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded};
            mover.Node.AddChild(arm);
        }
        arm.Visible=true;
    }

    private static void HideArm(Mover mover){var arm=mover.Node.GetNodeOrNull<MeshInstance3D>("WorkArm");if(arm is not null)arm.Visible=false;}
}
