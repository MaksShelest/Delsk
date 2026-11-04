using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class PresentationStage
{
    private MeshInstance3D? _roadSelection;
    private readonly Dictionary<long,MeshInstance3D> _workSignals=[];
    public void ShowRoadWorkEffects(PlayerView view)
    {
        var works=view.Roads?.OwnWorks.Where(w=>w.Batch is not null).ToArray()??[];
        foreach(var w in works)
        {
            var batch=w.Batch!;
            if(!_workSignals.TryGetValue(w.VehicleId,out var signal))
            {
                signal=new(){Name="RoadWorkSignal_"+w.VehicleId,Mesh=new CylinderMesh{TopRadius=.04f,BottomRadius=.14f,Height=.35f},MaterialOverride=new StandardMaterial3D{AlbedoColor=new("eea52a")}};
                AddChild(signal);_workSignals.Add(w.VehicleId,signal);
            }
            // Animation advances only with actual accumulated Core work; pauses freeze it.
            signal.Position=new(batch.Cell.X+.34f,.22f+(batch.Work%3)*.035f,batch.Cell.Z+.34f);
            signal.Rotation=new(0,(float)(batch.Work%16)*Mathf.Pi/8,0);
        }
        foreach(var id in _workSignals.Keys.Where(id=>!works.Any(w=>w.VehicleId==id)).ToArray())
        {_workSignals[id].QueueFree();_workSignals.Remove(id);}
    }
    public void ShowRoadSelection(Cell? cell)
    {
        if(cell is null){if(_roadSelection is not null){_roadSelection.QueueFree();_roadSelection=null;}return;}
        if(_roadSelection is null){_roadSelection=RoadTile(cell.Value,new(.30f,.88f,.55f,.65f),.96f,.09f);_roadSelection.Name="SelectedRoadWorkCell";}
        _roadSelection.Position=new(cell.Value.X,.09f,cell.Value.Z);
    }
    private readonly Dictionary<Cell,(int Signature,MeshInstance3D Mesh)> _roadSurfaces=[];
    private readonly Dictionary<string,(int Signature,MeshInstance3D Mesh)> _roadDetours=[];
    private static readonly Cell[] RoadDirections=[new(1,0),new(0,1),new(-1,0),new(0,-1)];
    private static int ConnectionMask(Cell c,HashSet<Cell> cells)
    {int mask=0;for(int n=0;n<4;n++)if(cells.Contains(new(c.X+RoadDirections[n].X,c.Z+RoadDirections[n].Z)))mask|=1<<n;return mask;}
    private void ShowRoads(PresentationFrame frame)
    {
        var cells=frame.RoadCells.Select(c=>c.Cell).ToHashSet();
        foreach(var cell in _roadSurfaces.Keys.Where(c=>!cells.Contains(c)).ToArray()){_roadSurfaces[cell].Mesh.QueueFree();_roadSurfaces.Remove(cell);}
        foreach(var road in frame.RoadCells)
        {
            int mask=ConnectionMask(road.Cell,cells),signature=road.Level*16+mask;
            if(_roadSurfaces.TryGetValue(road.Cell,out var old)&&old.Signature==signature)continue;
            old.Mesh?.QueueFree();
            var color=road.Level switch{1=>new Color(.46f,.35f,.22f),2=>new Color(.29f,.30f,.30f),_=>new Color(.16f,.19f,.23f)};
            var mesh=ConnectedRoad(road.Cell,mask,color,.45f,.026f,road.Level==1);
            mesh.Name=$"Road_{road.Cell.X}_{road.Cell.Z}_{road.Level}";_roadSurfaces[road.Cell]=(signature,mesh);
        }
        var active=new HashSet<string>();
        foreach(var bypass in frame.Bypasses)
        {
            for(int n=0;n<bypass.Path.Length;n++)
            {
                var cell=bypass.Path[n];int mask=0;
                // Only the declared path connects: nearby unrelated roads cannot invent a shortcut.
                foreach(int neighbor in new[]{n-1,n+1})if(neighbor>=0&&neighbor<bypass.Path.Length)
                {var other=bypass.Path[neighbor];int direction=Array.IndexOf(RoadDirections,new Cell(other.X-cell.X,other.Z-cell.Z));if(direction>=0)mask|=1<<direction;}
                string key=$"{bypass.VehicleId}_{cell.X}_{cell.Z}";active.Add(key);int signature=mask+(bypass.Closing?16:0);
                if(_roadDetours.TryGetValue(key,out var old)&&old.Signature==signature)continue;old.Mesh?.QueueFree();
                _roadDetours[key]=(signature,ConnectedRoad(cell,mask,bypass.Closing?new(.55f,.38f,.17f):new(.67f,.46f,.22f),.35f,.05f,true));
            }
            string work=$"work_{bypass.VehicleId}";active.Add(work);
            if(!_roadDetours.ContainsKey(work))_roadDetours[work]=(0,RoadTile(bypass.WorkCell,new(.75f,.19f,.07f),.9f,.06f));
        }
        foreach(var key in _roadDetours.Keys.Where(k=>!active.Contains(k)).ToArray()){_roadDetours[key].Mesh.QueueFree();_roadDetours.Remove(key);}
    }
    private MeshInstance3D ConnectedRoad(Cell cell,int mask,Color earth,float halfWidth,float y,bool ruts)
    {
        var mesh=new SurfaceTool();mesh.Begin(Mesh.PrimitiveType.Triangles);
        void Triangle(Vector3 a,Vector3 b,Vector3 c,Color color,bool fade=false)
        {foreach(var v in new[]{a,c,b}){var tone=fade?earth.Lerp(color,Mathf.SmoothStep(0,1,Mathf.Clamp((new Vector2(v.X,v.Z).Length()-.22f)/.25f,0,1))):color;mesh.SetColor(tone);mesh.SetNormal(Vector3.Up);mesh.AddVertex(v);}}
        Vector3 Normal(Vector3[] points,int n)
        {
            var p=points[n];var axis=(points[Math.Min(n+1,points.Length-1)]-points[Math.Max(0,n-1)]).Normalized();
            if(Mathf.Abs(Mathf.Abs(p.X)-.5f)<.0001f&&Mathf.Abs(p.Z)<.001f)axis=new(Mathf.Sign(axis.X),0,0);
            if(Mathf.Abs(Mathf.Abs(p.Z)-.5f)<.0001f&&Mathf.Abs(p.X)<.001f)axis=new(0,0,Mathf.Sign(axis.Z));
            return new(-axis.Z,0,axis.X);
        }
        void Ribbon(Vector3[] points,float width,Color color,bool rough=false,bool fade=false)
        {
            for(int i=0;i<points.Length-1;i++)
            {
                var p=points[i];var q=points[i+1];var normal=Normal(points,i);var nextNormal=Normal(points,i+1);
                float Edge(Vector3 v)=>rough?.016f*Mathf.Sin((v.X+cell.X)*7.13f+(v.Z+cell.Z)*5.37f):0;
                var a=p+normal*(width+Edge(p));var b=p-normal*(width+Edge(p));var c=q+nextNormal*(width+Edge(q));var d=q-nextNormal*(width+Edge(q));
                Triangle(a,b,c,color,fade);Triangle(b,d,c,color,fade);
            }
        }
        var paths=new List<Vector3[]>();int[] directions=Enumerable.Range(0,4).Where(n=>(mask&(1<<n))!=0).ToArray();
        Vector3 Edge(int n)=>new(RoadDirections[n].X*.5f,0,RoadDirections[n].Z*.5f);
        if(directions.Length==2&&(directions[0]+2)%4!=directions[1])
        {
            var a=Edge(directions[0]);var b=Edge(directions[1]);var center=a+b;var points=new Vector3[13];
            float angle=Mathf.Atan2(a.Z-center.Z,a.X-center.X),end=Mathf.Atan2(b.Z-center.Z,b.X-center.X),delta=Mathf.Wrap(end-angle,-Mathf.Pi,Mathf.Pi);
            for(int n=0;n<points.Length;n++){float t=angle+delta*n/(points.Length-1);points[n]=center+new Vector3(Mathf.Cos(t)*.5f,0,Mathf.Sin(t)*.5f);}
            paths.Add(points);
        }
        else
        {
            if(directions.Length==0)directions=[0,2];
            if(directions.Contains(0)&&directions.Contains(2))paths.Add([Edge(2),Vector3.Zero,Edge(0)]);
            if(directions.Contains(1)&&directions.Contains(3))paths.Add([Edge(3),Vector3.Zero,Edge(1)]);
            foreach(int n in directions)if(!directions.Contains((n+2)%4))paths.Add([Vector3.Zero,Edge(n)]);
        }
        // Continuous shoulders and surface share exact neighbour endpoints; no per-cell border mesh.
        foreach(var path in paths)Ribbon(path,halfWidth+.025f,earth.Darkened(.09f),true);
        foreach(var path in paths)Ribbon(path.Select(p=>p+new Vector3(0,.002f,0)).ToArray(),halfWidth,earth,true);
        if(ruts)foreach(var path in paths)
        {
            // Junction tracks fade into a common worn centre, rather than changing direction at a tile seam.
            for(int side=-1;side<=1;side+=2)
            {
                var track=new Vector3[path.Length];
                for(int n=0;n<path.Length;n++)
                {track[n]=path[n]+Normal(path,n)*(side*.16f)+new Vector3(0,.005f,0);}
                Ribbon(track,.038f,earth.Darkened(.24f),fade:directions.Length>=3);
            }
        }
        var node=new MeshInstance3D{Position=new(cell.X,y,cell.Z),Mesh=mesh.Commit(),MaterialOverride=new StandardMaterial3D{VertexColorUseAsAlbedo=true,Roughness=1,CullMode=BaseMaterial3D.CullModeEnum.Disabled}};
        AddChild(node);return node;
    }
    private MeshInstance3D RoadTile(Cell cell,Color color,float width,float height)
    {
        var node=new MeshInstance3D{Position=new(cell.X,height,cell.Z),Mesh=new BoxMesh{Size=new(width,.018f,width)},MaterialOverride=new StandardMaterial3D{AlbedoColor=color,Roughness=1}};AddChild(node);return node;
    }
}
