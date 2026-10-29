using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Replaceable visuals only. Original factories remain the primary/fallback set.</summary>
public static class WorldSkins
{
    public static AppearancePreferences Preferences { get; private set; } = new();
    public static int Revision { get; private set; }
    private static readonly Dictionary<string,Mesh> Meshes = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Missing = new(StringComparer.Ordinal);
    public static void Configure(AppearancePreferences preferences) { Preferences=preferences.Normalize(); Revision++; }
    public static string ActorName(long actor,string language) => AppearanceCatalog.Name(Preferences.ForActor(actor),language)+" #"+actor;
    private static Node3D? Model(string key,float scale,Vector3 offset=default,bool reverse=false,bool feet=false)
    {
        if(!Meshes.TryGetValue(key,out var mesh))
        {
            string path="res://skins/cozy/"+key+".res";
            if(!ResourceLoader.Exists(path)) { if(Missing.Add(key))GD.PushWarning("Missing cosmetic model: "+key);return null; }
            mesh=GD.Load<Mesh>(path);Meshes[key]=mesh;
        }
        var root=new Node3D { Name="Appearance" };
        var body=new MeshInstance3D { Name="Model", Mesh=mesh, Scale=Vector3.One*scale,
            Position=offset+new Vector3(0,feet?-mesh.GetAabb().Position.Y*scale:0,0), RotationDegrees=new(0,reverse?180:0,0) };
        body.SetMeta("skin_id",key);root.SetMeta("skin_set",AppearancePreferences.Cozy);root.SetMeta("skin_id",key);root.AddChild(body);
        return root;
    }
    private static Node3D Classic(Node3D node){node.Name="Appearance";node.SetMeta("skin_set",AppearancePreferences.Classic);return node;}
    public static Node3D Person(long actor,bool human,string you,string language)
    {
        string name=ActorName(actor,language),caption=human?you+" · "+name:name;
        Node3D geometry;
        if(Preferences.PeopleSet==AppearancePreferences.Cozy && Model(AppearanceCatalog.People[Preferences.ForActor(actor)].Asset,1,reverse:true) is {} model)geometry=model;
        else
        {
            geometry=WorldModels.Person(human?new("eef0dc"):MapSymbols.ActorColor(actor,false),caption);
            foreach(var label in geometry.GetChildren().OfType<Label3D>()){geometry.RemoveChild(label);label.Free();}
        }
        var original=WorldProportions.Fit(geometry,new(WorldProportions.PersonWidth,WorldProportions.PersonHeight,WorldProportions.PersonWidth));
        original.Name="Appearance";original.SetMeta("skin_set",Preferences.PeopleSet);
        if(Preferences.PeopleSet==AppearancePreferences.Cozy)original.SetMeta("skin_id",AppearanceCatalog.People[Preferences.ForActor(actor)].Asset);
        original.AddChild(new Label3D{Name="Caption",Text=caption,FontSize=24,PixelSize=.0028f,Position=new(0,WorldProportions.CaptionHeight,0),Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,OutlineSize=5,Modulate=Colors.White});
        return original;
    }
    public static Node3D Civic(CivicSite site)=>WorldModels.Civic(site,Preferences.ForModel(site.Kind=="town_hall"?"capital":site.Kind)==AppearancePreferences.Cozy);

    public static Node3D Resource(PresentationMarker marker)
    {
        if(Preferences.ForModel("resource:"+marker.Label)==AppearancePreferences.Cozy)
        {
            string key=AppearanceCatalog.ResourceModel(marker.Label,marker.Kind=="resource-depleted"?0:marker.ResourceStock,marker.ResourceCapacity,marker.ResourceSparse);
            if(key.Length>0 && Model(key,.2f) is {} visual)return visual;
        }
        return Classic(marker.Kind=="resource-depleted"?WorldModels.DepletedResource(marker.Label):WorldModels.Resource(marker.Label));
    }
    public static Node3D Building(PresentationMarker marker)
    {
        if(marker.Kind=="capital")return WorldModels.EnterpriseExtent(marker,Preferences.ForModel("capital")==AppearancePreferences.Cozy?WorldModels.LegacyTownHall(true):Classic(WorldModels.LegacyTownHall(false)));
        string id=marker.Kind=="production"?marker.Label:marker.Kind;
        string key=id switch {"sawmill"=>"Sawmill","bakery"=>"Bakery","dairy"=>"Dairy","sugar-factory"=>"SugarFactory","confectionery"=>"Confectionery","shop"=>"MarketStall","capital"=>"CapitalTownHall",_=>""};
        if(key.Length>0 && Preferences.ForModel(id)==AppearancePreferences.Cozy)
        {
            var cells=marker.Footprint.Length==0?new[]{new Cell(0,0)}:marker.Footprint;
            int minX=cells.Min(c=>c.X),minZ=cells.Min(c=>c.Z),width=cells.Max(c=>c.X)-minX+1,depth=cells.Max(c=>c.Z)-minZ+1;
            float sourceWidth=id=="capital"?15:5;
            float scale=Mathf.Min(width/sourceWidth,depth/5f);
            if(Model(key,scale,new((minX+cells.Max(c=>c.X))/2f,0,(minZ+cells.Max(c=>c.Z))/2f)) is {} visual)
            {AddLevel(visual,marker.Level);return WorldModels.EnterpriseExtent(marker,visual);}
        }
        return WorldModels.EnterpriseExtent(marker,Classic(WorldModels.Building(marker.Kind,marker.Label,marker.Level)));
    }
    public static Node3D Truck(bool loaded)
    {
        if(Preferences.ForModel("truck")==AppearancePreferences.Cozy && Model(loaded?"DeliveryTruck":"DeliveryTruckEmpty",1,reverse:true) is {} model)return Fitted(model,new(.42f,.45f,.75f));
        return Classic(Fitted(WorldModels.Truck(loaded),new(.42f,.45f,.75f)));
    }
    public static Node3D Equipment(string item,int level)
    {
        if(Preferences.ForModel("equipment")==AppearancePreferences.Cozy && Model(item=="wood"?"ForestHarvester":"FieldHarvester",1,reverse:true) is {} model)
        {AddLevel(model,level);return Fitted(model,new(.72f,.75f,.82f));}
        return Classic(Fitted(WorldModels.Equipment(item,level),new(.72f,.75f,.82f)));
    }
    private static Node3D Fitted(Node3D node,Vector3 size){var root=WorldProportions.Fit(node,size);root.Name="Appearance";root.SetMeta("skin_set",node.HasMeta("skin_set")?node.GetMeta("skin_set"):AppearancePreferences.Cozy);return root;}
    private static void AddLevel(Node3D root,int level)
    {
        // Existing real level only; decorative strips do not create capacity or upgrades.
        if(level<=1)return;
        for(int i=0;i<Math.Min(level,4);i++)WorldModels.Part(root,new BoxMesh {Size=new(.035f,.018f,.12f)},new Color("e5c76c"),new(-.25f+i*.055f,.24f,.30f));
    }
    public static Node3D Preview(string key) => key switch
    {
        "truck"=>Truck(true),"equipment"=>Equipment("wood",1),
        _ when key.StartsWith("resource:",StringComparison.Ordinal)=>Resource(new("resource",0,0,key[9..],0,0){ResourceStock=100,ResourceCapacity=100}),
        "terminal" or "dealership"=>Civic(new CivicSite(key,key,Enumerable.Range(0,key=="terminal"?6:25).Select(i=>new Cell(i%(key=="terminal"?2:5),i/(key=="terminal"?2:5))).ToArray(),null,key=="dealership"?[new(0,3),new(1,3),new(2,3),new(3,3)]:null,[new(0,0),new(1,0),new(0,1),new(1,1)],new(0,2))),
        "capital"=>Civic(new CivicSite("town_hall","town_hall",Enumerable.Range(0,9).Select(i=>new Cell(i%3,i/3)).ToArray(),null,null,[new(0,0),new(1,0),new(0,1),new(1,1)],new(1,2))),
        "shop"=>Building(new("shop",0,0,"shop",0,0)),
        _=>Building(new("production",0,0,key,0,0))
    };
}
