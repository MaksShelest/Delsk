using Godot;
using Simulation.Core;

public static partial class WorldModels
{
    // Each model uses the saved Body, Entry and Footprint. Palettes never supply geometry.
    public static Node3D Civic(CivicSite site,bool cozy=false)
    {
        var root=new Node3D{Name="Civic_"+site.Kind};
        int x=site.Footprint.Min(c=>c.X),z=site.Footprint.Min(c=>c.Z);root.Position=new(x,.02f,z);
        root.SetMeta("skin_set",cozy?"cozy":"classic");
        float width=site.Body.Max(c=>c.X)-site.Body.Min(c=>c.X)+1,depth=site.Body.Max(c=>c.Z)-site.Body.Min(c=>c.Z)+1;
        float bx=site.Body.Min(c=>c.X)-x+(width-1)/2,bz=site.Body.Min(c=>c.Z)-z+(depth-1)/2;
        float front=bz+depth/2,doorX=site.Entry.X-x,doorZ=site.Entry.Z-z;
        Color stone=new(cozy?"eadbc0":"d6d9d2"),roof=new(cozy?"494b48":"3d5059"),trim=new(cozy?"bca57a":"aab4b2"),glass=new(cozy?"7eadae":"80a9bc");
        foreach(var cell in site.Footprint)
        {
            bool solid=site.Body.Contains(cell),display=site.Parking?.Contains(cell)==true;
            Box(root,new(.99f,.035f,.99f),new(display?"8a918d":solid?"a8aaa0":site.Kind=="town_hall"?"c9c3b0":"a5aaa4"),new(cell.X-x,0,cell.Z-z));
        }
        float height=site.Kind=="town_hall"?1.18f:.67f;
        var building=new Node3D{Name="BuildingBody"};root.AddChild(building);
        Box(building,new(width-.12f,height,depth-.12f),stone,new(bx,height/2+.04f,bz));
        Box(building,new(width-.03f,.08f,depth-.03f),trim,new(bx,.08f,bz));
        if(site.Kind=="town_hall")
        {
            Box(building,new(width-.04f,.055f,depth-.04f),trim,new(bx,.64f,bz));
            Part(building,new PrismMesh{Size=new(width-.02f,.34f,depth-.02f)},roof,new(bx,height+.21f,bz));
            foreach(float floor in new[]{.38f,.91f})foreach(float offset in new[]{-.56f,.56f})
            {
                // Keep the ground-floor door clear.
                if(floor<.5f&&Mathf.Abs(bx+offset-doorX)<.28f)continue;
                Window(building,new(bx+offset,floor,front-.05f),glass,trim);
            }
            foreach(float side in new[]{-1f,1f})foreach(float floor in new[]{.38f,.91f})foreach(float offset in new[]{-.48f,.48f})
            {
                var window=new Node3D{Position=new(bx+side*(width/2-.05f),floor,bz+offset),RotationDegrees=new(0,side*90,0)};building.AddChild(window);Window(window,Vector3.Zero,glass,trim);
            }
            // Clock tower has a southern face and two hands; it is part of the blocked body.
            Box(building,new(.52f,.48f,.44f),stone,new(bx,1.74f,front-.37f));
            Box(building,new(.62f,.07f,.54f),roof,new(bx,2.02f,front-.37f));
            var clock=Cylinder(building,.175f,.025f,new("f6efd9"),new(bx,1.78f,front-.135f));clock.RotationDegrees=new(90,0,0);
            Box(building,new(.022f,.12f,.018f),roof,new(bx,1.83f,front-.11f));
            Box(building,new(.12f,.022f,.018f),roof,new(bx+.05f,1.78f,front-.105f));
            Cylinder(building,.018f,.68f,trim,new(bx-.64f,1.77f,bz-.45f));
            Box(building,new(.32f,.19f,.018f),new(cozy?"c99b48":"518d85"),new(bx-.49f,2.01f,bz-.45f));
        }
        else
        {
            Box(building,new(width-.02f,.10f,depth-.02f),roof,new(bx,height+.08f,bz));
            if(site.Kind=="dealership")
            {
                foreach(float offset in new[]{-.55f,.55f})Window(building,new(bx+offset,.43f,front-.05f),glass,trim);
                Box(building,new(Mathf.Min(1.5f,width-.15f),.19f,.035f),new(cozy?"89754f":"4d716d"),new(bx,.82f,front-.04f));
                // Wheels on the fascia identify the showroom without a floating text billboard.
                foreach(float offset in new[]{-.20f,.20f}){var wheel=Cylinder(building,.065f,.025f,new("e5dac2"),new(bx+offset,.83f,front-.025f));wheel.RotationDegrees=new(90,0,0);}
            }
            else
            {
                // Freight shutter faces the open southern loading cell; no crates in its driving line.
                Box(building,new(.65f,.51f,.025f),new("5e7979"),new(doorX,.31f,front-.045f));
                for(int i=0;i<5;i++)Box(building,new(.65f,.018f,.025f),trim,new(doorX,.12f+i*.085f,front-.02f));
                Box(building,new(.22f,.18f,.24f),new("b5986c"),new(bx+.61f,.17f,bz+.3f));
            }
        }
        if(site.Kind!="terminal")
        {
            Box(building,new(.30f,.48f,.025f),new("79664e"),new(doorX,.29f,front-.045f));
            Box(building,new(.21f,.24f,.025f),glass,new(doorX,.36f,front-.03f));
            Box(building,new(.04f,.025f,.025f),new("e0c47d"),new(doorX+.08f,.21f,front-.02f));
        }
        // Cantilever, attached to the facade: nothing stands in the entry cell.
        Box(root,new(.48f,.055f,.19f),roof,new(doorX,site.Kind=="town_hall"?.62f:.61f,front+.035f));
        Box(root,new(.44f,.012f,.22f),new("e2d7b7"),new(doorX,.045f,front+.17f));
        // A flat approach strip reaches the exact Core interaction cell.
        Box(root,new(.36f,.009f,Mathf.Max(.12f,doorZ-front+.3f)),new("d7cdb4"),new(doorX,.041f,(front+doorZ+.3f)/2));
        if(site.Kind=="town_hall")
        {
            // Only perimeter slivers of the open side strip/plaza are furnished. Central 0.6-cell lanes stay clear.
            float side=site.Footprint.Max(c=>c.X)-x;
            foreach(float dz in new[]{-.12f,.88f})
            {
                Box(root,new(.18f,.14f,.48f),trim,new(side+.33f,.10f,dz));
                Box(root,new(.19f,.05f,.49f),new("607e5a"),new(side+.33f,.20f,dz));
            }
            foreach(float dx in new[]{-.30f,side+.30f})
            {
                float plaza=site.Footprint.Max(c=>c.Z)-z;
                Box(root,new(.27f,.035f,.17f),new("826d50"),new(dx,.17f,plaza+.29f));
                Box(root,new(.27f,.18f,.035f),new("826d50"),new(dx,.25f,plaza+.38f));
                foreach(float leg in new[]{-.09f,.09f})Box(root,new(.025f,.14f,.12f),roof,new(dx+leg,.08f,plaza+.29f));
                Cylinder(root,.018f,.69f,roof,new(dx,.38f,plaza-.27f));Round(root,.065f,new("f4df9f"),new(dx,.75f,plaza-.27f));
            }
        }
        if(site.Kind=="dealership")foreach(var cell in site.Parking??[])
        {
            float px=cell.X-x,pz=cell.Z-z;
            foreach(float edge in new[]{-.46f,.46f})Box(root,new(.018f,.009f,.82f),new("ebe1bc"),new(px+edge,.033f,pz));
            Box(root,new(.92f,.009f,.025f),new("ebe1bc"),new(px,.033f,pz-.43f));
        }
        return root;
    }
    private static void Window(Node3D root,Vector3 at,Color glass,Color trim)
    {
        Box(root,new(.29f,.32f,.025f),trim,at);Box(root,new(.23f,.26f,.03f),glass,at+new Vector3(0,0,.01f));
        Box(root,new(.018f,.26f,.035f),trim,at+new Vector3(0,0,.02f));Box(root,new(.23f,.018f,.035f),trim,at+new Vector3(0,0,.02f));
    }

    // Pre-vNext maps reserve only their original terminal footprint. Recognisable compact civic
    // facade, never a fabricated 3x3 territory over neighbouring land or paths.
    public static Node3D LegacyTownHall(bool cozy)
    {
        var root=new Node3D{Name="Appearance"};root.SetMeta("skin_set",cozy?"cozy":"classic");Color stone=new(cozy?"eadbc0":"d6d9d2"),roof=new(cozy?"494b48":"3d5059");
        Box(root,new(.76f,.92f,.50f),stone,new(0,.48f,-.10f));
        Box(root,new(.82f,.09f,.56f),roof,new(0,.98f,-.10f));
        foreach(float x in new[]{-.24f,.24f})foreach(float y in new[]{.34f,.73f})Window(root,new(x,y,.16f),new("80a9bc"),new("adb5aa"));
        Box(root,new(.19f,.41f,.025f),roof,new(0,.25f,.17f));Box(root,new(.34f,.045f,.22f),roof,new(0,.51f,.24f));
        var clock=Cylinder(root,.12f,.025f,new("f6efd9"),new(0,.78f,.18f));clock.RotationDegrees=new(90,0,0);
        Box(root,new(.012f,.09f,.018f),roof,new(0,.81f,.20f));Box(root,new(.08f,.012f,.018f),roof,new(.03f,.78f,.20f));
        Cylinder(root,.012f,.45f,roof,new(-.25f,1.24f,-.14f));Box(root,new(.23f,.14f,.018f),new("c99b48"),new(-.14f,1.40f,-.14f));
        return root;
    }
}
