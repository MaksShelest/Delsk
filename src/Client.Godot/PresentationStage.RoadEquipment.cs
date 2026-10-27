using Godot;
using Simulation.Hosting;

public partial class PresentationStage
{
    private readonly Dictionary<long,(int Level,string Item,Mover Mover)> _roadEquipment=[];
    private void ShowRoadEquipment(PresentationFrame frame,bool snap,bool appearanceChanged)
    {
        foreach(var e in frame.EquipmentTraffic)
        {
            if(!_roadEquipment.TryGetValue(e.Id,out var view))
            {
                var body=new Node3D{Name="RoadEquipment_"+e.Id};body.AddChild(WorldSkins.Equipment(e.ItemId,e.Level));AddChild(body);
                view=(e.Level,e.ItemId,new(body,Vector3.Zero));_roadEquipment.Add(e.Id,view);
            }
            if(view.Level!=e.Level||view.Item!=e.ItemId||appearanceChanged)
            {
                foreach(Node child in view.Mover.Node.GetChildren()){view.Mover.Node.RemoveChild(child);child.QueueFree();}
                view.Mover.Node.AddChild(WorldSkins.Equipment(e.ItemId,e.Level));view=(e.Level,e.ItemId,view.Mover);_roadEquipment[e.Id]=view;
            }
            var at=new Vector3(e.X+(e.OffsetX-500)/1000f,.08f,e.Z+(e.OffsetZ-500)/1000f);
            Target(view.Mover,at,.1f,snap||view.Mover.From==Vector3.Zero);
            view.Mover.Node.Rotation=new(0,Mathf.Atan2(-e.FacingX,-e.FacingZ),0);
        }
        foreach(long id in _roadEquipment.Keys.Where(id=>!frame.EquipmentTraffic.Any(e=>e.Id==id)).ToArray())
        {_roadEquipment[id].Mover.Node.QueueFree();_roadEquipment.Remove(id);}
    }
}
