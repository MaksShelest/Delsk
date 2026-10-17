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

    public static Node3D Building(string kind, string type)
    {
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
}
