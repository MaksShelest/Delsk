using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class PresentationStage
{
    private readonly Dictionary<Cell,(int Level,MeshInstance3D Mesh)> _roadSurfaces=[];
    private readonly Dictionary<string,MeshInstance3D> _roadDetours=[];
    private void ShowRoads(PresentationFrame frame)
    {
        var cells=frame.RoadCells.Select(c=>c.Cell).ToHashSet();
        foreach(var cell in _roadSurfaces.Keys.Where(c=>!cells.Contains(c)).ToArray()){_roadSurfaces[cell].Mesh.QueueFree();_roadSurfaces.Remove(cell);}
        foreach(var road in frame.RoadCells)
        {
            if(_roadSurfaces.TryGetValue(road.Cell,out var old)&&old.Level==road.Level)continue;
            if(old.Mesh is not null)old.Mesh.QueueFree();
            var color=road.Level switch{1=>new Color(.35f,.27f,.17f),2=>new Color(.22f,.25f,.29f),_=>new Color(.12f,.16f,.22f)};
            var mesh=RoadTile(road.Cell,color,road.Level==3?.96f:.82f,.015f);
            mesh.Name=$"Road_{road.Cell.X}_{road.Cell.Z}_{road.Level}";_roadSurfaces[road.Cell]=(road.Level,mesh);
        }
        var active=new HashSet<string>();
        foreach(var bypass in frame.Bypasses)
        {
            foreach(var cell in bypass.Path)
            {
                string key=$"{bypass.VehicleId}_{cell.X}_{cell.Z}";active.Add(key);
                if(!_roadDetours.ContainsKey(key))_roadDetours[key]=RoadTile(cell,new Color(.76f,.46f,.08f),.65f,.035f);
            }
            string work=$"work_{bypass.VehicleId}";active.Add(work);
            if(!_roadDetours.ContainsKey(work))_roadDetours[work]=RoadTile(bypass.WorkCell,new Color(.75f,.19f,.07f),.9f,.04f);
        }
        foreach(var key in _roadDetours.Keys.Where(k=>!active.Contains(k)).ToArray()){_roadDetours[key].QueueFree();_roadDetours.Remove(key);}
    }
    private MeshInstance3D RoadTile(Cell cell,Color color,float width,float height)
    {
        var node=new MeshInstance3D{Position=new(cell.X,height,cell.Z),Mesh=new BoxMesh{Size=new(width,.018f,width)},
            MaterialOverride=new StandardMaterial3D{AlbedoColor=color,Roughness=1}};AddChild(node);return node;
    }
}
