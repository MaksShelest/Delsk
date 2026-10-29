using Godot;
using Simulation.Hosting;

public partial class PresentationStage
{
    private readonly Dictionary<long,Mover> _fleet=[];
    private void ShowFleet(PresentationFrame frame,bool snap,bool appearanceChanged)
    {
        foreach(var v in frame.Vehicles)
        {
            if(!_fleet.TryGetValue(v.Id,out var mover))
            {
                var body=new Node3D{Name="Vehicle_"+v.Id};body.AddChild(WorldModels.Vehicle(v.ModelId,WorldSkins.Preferences.ForModel("truck")==AppearancePreferences.Cozy));
                var caption=new Label3D{Text=v.ModelId+" #"+v.Id,Position=new(0,.72f,0),FontSize=22,PixelSize=.0028f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled};body.AddChild(caption);AddChild(body);mover=new(body,Vector3.Zero);_fleet.Add(v.Id,mover);
            }
            if(appearanceChanged)
            {
                var previous=mover.Node.GetChild<Node3D>(0);mover.Node.RemoveChild(previous);previous.QueueFree();
                var visual=WorldModels.Vehicle(v.ModelId,WorldSkins.Preferences.ForModel("truck")==AppearancePreferences.Cozy);mover.Node.AddChild(visual);mover.Node.MoveChild(visual,0);
            }
            mover.Node.GetChildren().OfType<Label3D>().Single().Visible=!v.ForSale&&_cameraDistance<10;
            int slot=frame.Vehicles.Count(other=>other.X==v.X&&other.Z==v.Z&&other.Id<v.Id);
            bool onDisplay=v.ForSale || frame.Layout?.CivicObjects.Any(site=>site.Kind=="dealership" && (site.Parking?.Contains(new(v.X,v.Z))??false))==true && v.OffsetX==500 && v.OffsetZ==500;
            float side=onDisplay&&v.ModelId!="trakt_d1"?(slot%2==0?-.23f:.23f):0;
            var at=new Vector3(v.X+(v.OffsetX-500)/1000f+side,.07f,v.Z+(v.OffsetZ-500)/1000f);
            Target(mover,at,.1f,snap||mover.From==Vector3.Zero);
            if(v.FacingX!=0||v.FacingZ!=0)mover.Node.Rotation=new(0,Mathf.Atan2(-v.FacingX,-v.FacingZ),0);
        }
        foreach(long id in _fleet.Keys.Where(id=>!frame.Vehicles.Any(v=>v.Id==id)).ToArray()){_fleet[id].Node.QueueFree();_fleet.Remove(id);}
    }
}
