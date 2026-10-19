using Godot;

/// <summary>Small code-native models. They have no gameplay state or collision authority.</summary>
public static class WorldModels
{
    private static readonly System.Collections.Generic.Dictionary<Color, StandardMaterial3D> Materials = new();
    public static MeshInstance3D Part(Node3D root, Mesh mesh, Color color, Vector3 position)
    {
        if (!Materials.TryGetValue(color, out var material))
        {
            material = new StandardMaterial3D { AlbedoColor = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
            Materials[color] = material;
        }
        var part = new MeshInstance3D { Mesh = mesh, Position = position, MaterialOverride = material };
        root.AddChild(part);
        return part;
    }
    private static void Box(Node3D root, Vector3 size, Color color, Vector3 at) => Part(root, new BoxMesh { Size = size }, color, at);
    private static void Round(Node3D root, float radius, Color color, Vector3 at) => Part(root,
        new SphereMesh { Radius = radius, Height = radius * 2, RadialSegments = 8, Rings = 4 }, color, at);
    private static void Cylinder(Node3D root, float radius, float height, Color color, Vector3 at, float? top = null) => Part(root,
        new CylinderMesh { TopRadius = top ?? radius, BottomRadius = radius, Height = height, RadialSegments = 8 }, color, at);

    public static Node3D Resource(string item)
    {
        var root = new Node3D();
        if (item == "wood")
        {
            foreach (var at in new[] { new Vector3(-0.22f, 0, 0.12f), new Vector3(0.22f, 0, -0.12f), new Vector3(0.1f, 0, 0.3f) })
            {
                Cylinder(root, 0.055f, 0.6f, new Color("795438"), at + new Vector3(0, 0.3f, 0));
                Cylinder(root, 0.27f, 0.52f, new Color("3f7950"), at + new Vector3(0, 0.65f, 0), 0);
                Cylinder(root, 0.2f, 0.45f, new Color("57955b"), at + new Vector3(0, 0.92f, 0), 0);
            }
        }
        else if(item=="milk")
        {
            Box(root,new(.88f,.08f,.8f),new("879d9d"),new(0,.04f,0));
            foreach(float x in new[]{-.34f,.34f})Box(root,new(.045f,.7f,.045f),new("6298ab"),new(x,.4f,0));
            Box(root,new(.9f,.06f,.8f),new("70b4c9"),new(0,.78f,0));
            foreach(float x in new[]{-.2f,0,.2f}){Cylinder(root,.08f,.3f,new("e5f0ed"),new(x,.23f,.12f));Cylinder(root,.06f,.035f,new("6799ad"),new(x,.4f,.12f));}
        }
        else if(item=="beet")
        {
            Box(root,new(.85f,.045f,.85f),new("8d6852"),new(0,.02f,0));
            foreach(float x in new[]{-.25f,0,.25f})foreach(float z in new[]{-.25f,0,.25f})
            {Round(root,.075f,new("9c486d"),new(x,.08f,z));Cylinder(root,.025f,.2f,new("5b9f48"),new(x,.22f,z),.06f);}
        }
        else
        {
            Box(root, new Vector3(0.85f, 0.045f, 0.85f), new Color("a77b48"), new Vector3(0, 0.02f, 0));
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
            {
                var at = new Vector3(x * 0.23f, 0, z * 0.23f);
                Cylinder(root, 0.012f, 0.38f, new Color("c1a64f"), at + new Vector3(0, 0.21f, 0));
                Part(root, new SphereMesh { Radius = 0.045f, Height = 0.19f, RadialSegments = 6, Rings = 4 },
                    new Color("edcd68"), at + new Vector3(0, 0.43f, 0));
            }
        }
        return root;
    }

    public static Node3D DepletedResource(string item)
    {
        var root=new Node3D();
        if(item=="wood")
            foreach(float x in new[]{-.25f,0,.25f})Cylinder(root,.09f,.12f,new Color("795438"),new Vector3(x,.06f,0));
        else if(item=="milk")Box(root,new(.88f,.08f,.8f),new("879d9d"),new(0,.04f,0));
        else Box(root,new Vector3(.85f,.04f,.85f),new Color("98714b"),new Vector3(0,.02f,0));
        return root;
    }

    public static Node3D Building(string kind, string type)
    {
        if (kind == "production" && type == "sawmill") return Sawmill();
        if (kind == "production" && type == "bakery") return Bakery();
        if(kind=="production" && type is "dairy" or "sugar-factory" or "confectionery")return FoodFactory(type);
        var root = new Node3D();
        Color wall = kind switch { "warehouse" => new("8aabba"), "shop" => new("d4b8cb"), "capital" => new("dfd4b7"), _ => new("d1b486") };
        Color roof = kind switch { "warehouse" => new("4e7182"), "shop" => new("91608d"), "capital" => new("587987"), _ => new("a85e48") };
        Box(root, new Vector3(0.88f, 0.08f, 0.85f), new Color("888d83"), new Vector3(0, 0.04f, 0));
        Box(root, new Vector3(0.69f, 0.48f, 0.65f), wall, new Vector3(0, 0.3f, 0));
        Part(root, new PrismMesh { Size = new Vector3(0.88f, 0.35f, 0.83f) }, roof, new Vector3(0, 0.68f, 0));
        Box(root, new Vector3(0.16f, 0.27f, 0.025f), new Color("594b43"), new Vector3(0, 0.21f, 0.34f));
        foreach (float x in new[] { -0.22f, 0.22f })
            Box(root, new Vector3(0.12f, 0.12f, 0.03f), new Color("b2dce0"), new Vector3(x, 0.36f, 0.34f));
        if (kind == "capital")
        {
            Cylinder(root, 0.025f, 0.7f, new Color("d3b36e"), new Vector3(0.3f, 1.05f, 0));
            Box(root, new Vector3(0.24f, 0.14f, 0.035f), new Color("e5b64b"), new Vector3(0.4f, 1.28f, 0));
            Box(root, new Vector3(0.44f, 0.06f, 0.2f), new Color("b7bcaa"), new Vector3(0, 0.06f, 0.5f));
        }
        if (kind == "shop")
        {
            Box(root, new Vector3(0.85f, 0.06f, 0.34f), roof, new Vector3(0, 0.45f, 0.51f));
            foreach (float x in new[] { -0.33f, 0.33f }) Cylinder(root, 0.025f, 0.4f, new Color("846346"), new Vector3(x, 0.22f, 0.6f));
        }
        if (type == "bakery") Cylinder(root, 0.07f, 0.46f, new Color("8d6857"), new Vector3(-0.22f, 0.83f, -0.08f));
        if (type == "sawmill" || kind == "warehouse")
            for (int i = 0; i < 3; i++)
            {
                var log = Part(root, new CylinderMesh { TopRadius = 0.065f, BottomRadius = 0.065f, Height = 0.4f, RadialSegments = 8 }, new Color("ac8655"), new Vector3(-0.44f, 0.12f + i * 0.09f, 0.12f));
                log.RotationDegrees = new Vector3(90, 0, 0);
            }
        return root;
    }

    private static Node3D FoodFactory(string type)
    {
        var root=new Node3D();
        Box(root,new(.95f,.08f,.9f),new("8d9794"),new(0,.04f,0));
        if(type=="dairy")
        {
            Box(root,new(.45f,.45f,.72f),new("deeeec"),new(.2f,.3f,0));
            Box(root,new(.5f,.06f,.8f),new("598fa8"),new(.2f,.55f,0));
            foreach(float z in new[]{-.22f,.22f}){Cylinder(root,.15f,.55f,new("b5d0d4"),new(-.26f,.35f,z));Cylinder(root,.15f,.12f,new("6799ad"),new(-.26f,.68f,z),0);}
            Box(root,new(.35f,.1f,.15f),new("ebc84c"),new(.18f,.18f,.4f));
        }
        else if(type=="sugar-factory")
        {
            Box(root,new(.6f,.45f,.65f),new("cfad98"),new(-.15f,.3f,0));
            foreach(float z in new[]{-.2f,.2f}){Cylinder(root,.14f,.75f,new("dedcc9"),new(.28f,.46f,z));Cylinder(root,.14f,.15f,new("ab9280"),new(.28f,.9f,z),0);}
            foreach(float x in new[]{-.3f,-.1f,.1f})Round(root,.08f,new("9c486d"),new(x,.16f,.39f));
        }
        else
        {
            Box(root,new(.86f,.6f,.73f),new("e0b6b4"),new(0,.37f,0));
            Box(root,new(.94f,.08f,.84f),new("95567c"),new(0,.72f,0));
            Cylinder(root,.07f,.55f,new("aa8177"),new(-.28f,.91f,-.17f));
            Box(root,new(.7f,.06f,.3f),new("787f87"),new(0,.23f,.44f));
            foreach(float x in new[]{-.22f,0,.22f}){Box(root,new(.14f,.1f,.12f),new("d1a458"),new(x,.31f,.44f));Box(root,new(.14f,.025f,.12f),new("fbebd4"),new(x,.37f,.44f));}
        }
        return root;
    }

    public static Node3D Person(Color shirt, string caption)
    {
        var root = new Node3D();
        Part(root, new CapsuleMesh { Radius = 0.14f, Height = 0.43f }, shirt, new Vector3(0, 0.28f, 0));
        Round(root, 0.13f, new Color("ebcda4"), new Vector3(0, 0.58f, 0));
        Cylinder(root, 0.16f, 0.06f, shirt.Darkened(0.2f), new Vector3(0, 0.7f, 0));
        foreach (float x in new[] { -0.065f, 0.065f })
            Box(root, new Vector3(0.085f, 0.12f, 0.15f), new Color("414c58"), new Vector3(x, 0.07f, -0.02f));
        Round(root, 0.033f, new Color("d4ae85"), new Vector3(0, 0.58f, -0.13f));
        root.AddChild(new Label3D { Text = caption, FontSize = 22, PixelSize = 0.009f, Position = new Vector3(0, 1.01f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, OutlineSize = 5, Modulate = Colors.White });
        return root;
    }

    public static Node3D Cargo()
    {
        var root = new Node3D();
        Box(root, new Vector3(0.45f, 0.24f, 0.56f), new Color("bc9155"), new Vector3(0, 0.22f, 0));
        foreach (float x in new[] { -0.24f, 0.24f }) foreach (float z in new[] { -0.18f, 0.18f })
            Round(root, 0.075f, new Color("46505a"), new Vector3(x, 0.09f, z));
        Box(root, new Vector3(0.48f, 0.025f, 0.05f), new Color("775c40"), new Vector3(0, 0.35f, 0));
        return root;
    }

    private static Node3D Sawmill()
    {
        var root = new Node3D();
        Box(root, new Vector3(0.9f,0.08f,0.86f), new Color("888d83"), new Vector3(0,0.04f,0));
        foreach (float x in new[] {-0.34f,0.34f}) foreach (float z in new[] {-0.3f,0.3f})
            Box(root,new Vector3(0.065f,0.6f,0.065f),new Color("8b643f"),new Vector3(x,0.37f,z));
        Part(root,new PrismMesh {Size=new Vector3(0.95f,0.28f,0.9f)},new Color("527c78"),new Vector3(0,0.76f,0));
        Box(root,new Vector3(0.6f,0.09f,0.3f),new Color("bca27b"),new Vector3(0,0.37f,0));
        var saw=Part(root,new CylinderMesh {TopRadius=0.14f,BottomRadius=0.14f,Height=0.025f,RadialSegments=12},new Color("b5c1c2"),new Vector3(0.1f,0.49f,0));
        saw.RotationDegrees=new Vector3(90,0,0);
        for (int i=0;i<3;i++)
        {
            var log=Part(root,new CylinderMesh {TopRadius=0.055f,BottomRadius=0.055f,Height=0.5f,RadialSegments=8},new Color("ac8655"),new Vector3(-0.31f,0.13f+i*0.07f,0.2f));
            log.RotationDegrees=new Vector3(90,0,0);
            Box(root,new Vector3(0.19f,0.035f,0.5f),new Color("d6ae76"),new Vector3(0.32f,0.12f+i*0.04f,0.18f));
        }
        return root;
    }
    private static Node3D Bakery()
    {
        var root=new Node3D();
        Box(root,new Vector3(0.9f,0.08f,0.86f),new Color("888d83"),new Vector3(0,0.04f,0));
        Box(root,new Vector3(0.66f,0.48f,0.58f),new Color("bd8468"),new Vector3(0,0.32f,-0.06f));
        Part(root,new PrismMesh {Size=new Vector3(0.85f,0.3f,0.8f)},new Color("a85e48"),new Vector3(0,0.69f,-0.06f));
        Box(root,new Vector3(0.24f,0.23f,0.025f),new Color("493f39"),new Vector3(0,0.27f,0.25f));
        Box(root,new Vector3(0.16f,0.06f,0.03f),new Color("e0a750"),new Vector3(0,0.2f,0.27f));
        Box(root,new Vector3(0.13f,0.5f,0.13f),new Color("906b5d"),new Vector3(-0.21f,0.85f,-0.16f));
        Box(root,new Vector3(0.65f,0.07f,0.25f),new Color("9d7754"),new Vector3(0,0.22f,0.44f));
        foreach(float x in new[] {-0.21f,0f,0.21f})
            Part(root,new SphereMesh {Radius=0.075f,Height=0.1f,RadialSegments=8,Rings=4},new Color("e7bc79"),new Vector3(x,0.29f,0.44f));
        return root;
    }
    public static Node3D Truck(bool loaded)
    {
        var root=new Node3D();
        Box(root,new Vector3(0.4f,0.12f,0.66f),new Color("526475"),new Vector3(0,0.18f,0));
        Box(root,new Vector3(0.4f,0.28f,0.22f),new Color("5b94b1"),new Vector3(0,0.36f,-0.23f));
        Box(root,new Vector3(0.3f,0.1f,0.025f),new Color("b9dce0"),new Vector3(0,0.41f,-0.35f));
        foreach(float x in new[] {-0.22f,0.22f}) foreach(float z in new[] {-0.21f,0.21f})
            Round(root,0.08f,new Color("35434b"),new Vector3(x,0.1f,z));
        foreach(float x in new[] {-0.21f,0.21f}) Box(root,new Vector3(0.04f,0.12f,0.43f),new Color("738b94"),new Vector3(x,0.28f,0.1f));
        if(loaded) Box(root,new Vector3(0.3f,0.24f,0.36f),new Color("d5b263"),new Vector3(0,0.37f,0.1f));
        return root;
    }
    public static Node3D Equipment(string item)
    {
        var root=Truck(false);
        Box(root,new Vector3(0.36f,0.19f,0.4f),new Color("d4af4d"),new Vector3(0,0.42f,0.08f));
        if(item=="wood")
        {
            Box(root,new Vector3(0.06f,0.65f,0.06f),new Color("c49b43"),new Vector3(0.13f,0.68f,0.15f));
            Box(root,new Vector3(0.46f,0.06f,0.06f),new Color("c49b43"),new Vector3(-0.06f,0.99f,0.15f));
        }
        else Box(root,new Vector3(0.68f,0.12f,0.2f),new Color("e2bf57"),new Vector3(0,0.16f,-0.42f));
        return root;
    }
}
