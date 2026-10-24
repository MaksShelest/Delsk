using Godot;
using Simulation.Core;

public static partial class WorldModels
{
    public static Node3D Civic(CivicSite site)
    {
        var root=new Node3D{Name="Civic_"+site.Kind};int x=site.Footprint.Min(c=>c.X),z=site.Footprint.Min(c=>c.Z);root.Position=new(x,.02f,z);
        float width=site.Body.Max(c=>c.X)-site.Body.Min(c=>c.X)+1,depth=site.Body.Max(c=>c.Z)-site.Body.Min(c=>c.Z)+1;
        float bx=site.Body.Min(c=>c.X)-x+(width-1)/2,bz=site.Body.Min(c=>c.Z)-z+(depth-1)/2;
        Color paint=new(site.Kind=="town_hall"?"d7c3a1":site.Kind=="terminal"?"a4bcb2":"b9b0a2");
        foreach(var cell in site.Footprint)Box(root,new(.99f,.04f,.99f),new("ac9c7c"),new(cell.X-x,0,cell.Z-z));
        Box(root,new(width-.08f,1.15f,depth-.08f),paint,new(bx,.6f,bz));
        Box(root,new(width+.08f,.13f,depth+.08f),new("536a62"),new(bx,1.22f,bz));
        // Canopy faces the southern, actually walkable loading/door apron.
        float doorX=site.Entry.X-x,doorZ=site.Entry.Z-z;
        Box(root,new(.62f,.75f,.035f),new("3e5759"),new(doorX,.42f,bz+depth/2));
        Box(root,new(.9f,.08f,.8f),new("6b8b7c"),new(doorX,1.02f,doorZ-.14f));
        if(site.Kind=="town_hall")
        {Cylinder(root,.05f,.85f,new("716247"),new(bx,1.64f,bz));Box(root,new(.5f,.25f,.025f),new("d5b85e"),new(bx+.23f,1.96f,bz));}
        if(site.Kind=="terminal")
        {Box(root,new(.4f,.32f,.38f),new("b98b55"),new(doorX+.6f,.2f,doorZ));Box(root,new(.25f,.2f,.3f),new("cfad78"),new(doorX+.55f,.45f,doorZ));}
        if(site.Kind=="dealership")foreach(var cell in site.Parking??[])
            Box(root,new(.65f,.012f,.03f),new("e1d6b5"),new(cell.X-x,.06f,cell.Z-z));
        return root;
    }
}
