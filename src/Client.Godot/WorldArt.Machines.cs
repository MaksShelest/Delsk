using Godot;
using Simulation.Hosting;

public static partial class WorldArt
{
    public static Node3D Vehicle(string model, bool cozy)
    {
        var pen = new Pen();
        var root = new Node3D { Name = "VehicleBody" };
        switch (model)
        {
            case "sputnik20": Car(pen, cozy ? new Color("7eb4d2") : new Color("5e92b4"), 0.62f, 0.95f, 0.16f, 0.22f); break;
            case "muravey70": Van(pen, cozy ? new Color("e2c56e") : new Color("d0ad58")); break;
            case "eger70": Jeep(pen, cozy ? new Color("8aaf72") : new Color("6f9358")); break;
            case "vector200": Truck(pen, cozy ? new Color("c98470") : new Color("b46c55")); break;
            case "trakt_d1": Paver(pen, cozy ? new Color("e2b64a") : new Color("d2a644")); break;
            default: Car(pen, new Color("8a9390"), 0.62f, 0.95f, 0.16f, 0.22f); break;
        }
        root.AddChild(Instance(pen.Build(), "Body"));
        return root;
    }

    public static Node3D Machine(string item, int level, bool cozy)
    {
        string cls = WorldArtScatter.EquipmentClass(item);
        if (cls.Length == 0) cls = "extract_field";
        var body = new Pen();
        var extra = new Pen();
        switch (cls)
        {
            case "extract_forest": Forestry(body, extra, level, cozy); break;
            case "extract_dairy": Dairy(body, extra, level, cozy); break;
            case "extract_mine": Mine(body, extra, level, cozy); break;
            default: Combine(body, extra, level, cozy); break;
        }
        var root = new Node3D { Name = "Machine" };
        root.SetMeta("equipment_class", cls);
        root.SetMeta("skin_set", cozy ? "cozy" : "classic");
        root.AddChild(Instance(body.Build(), "Body"));
        if (level > 1) root.AddChild(Instance(extra.Build(), "Upgrade"));
        return root;
    }

    public static void MarkLevel(Node3D model, string cls)
    {
        var mesh = model.GetChildren().OfType<MeshInstance3D>().FirstOrDefault();
        var box = mesh?.Mesh?.GetAabb() ?? new Aabb(new(-0.4f, 0, -0.4f), new(0.8f, 0.6f, 0.8f));
        Vector3 roof = box.GetCenter() + new Vector3(0, box.Size.Y * 0.5f, 0);
        float span = Mathf.Min(box.Size.X, box.Size.Z) * 0.62f;
        float thick = Mathf.Max(0.08f, box.Size.Y * 0.07f);
        var pen = new Pen();
        Color mark = cls == "extract_forest" ? new Color("e0b15a") : new Color("f2d56a");
        pen.Box(roof + new Vector3(0, thick * 0.35f, 0), new(span, thick, span * 0.42f), mark);
        pen.Box(roof + new Vector3(0, thick * 1.3f, -span * 0.12f), new(span * 0.24f, thick * 1.4f, span * 0.24f), new Color("e0703a"));
        if (cls == "extract_forest") pen.Box(roof + new Vector3(span * 0.28f, thick * 0.2f, span * 0.2f), new(span * 0.22f, thick * 0.7f, span * 0.16f), new Color("8d6a3d"));
        model.AddChild(Instance(pen.Build(), "Upgrade"));
    }

    private static void Wheel(Pen pen, Vector3 at, float radius, bool knobby)
    {
        pen.Cylinder(at, radius, knobby ? 0.09f : 0.07f, new Color("2c3330"), Vector3.Right, 8);
        pen.Cylinder(at, radius * 0.45f, knobby ? 0.1f : 0.08f, new Color("c5c8c2"), Vector3.Right, 6);
    }

    private static void Car(Pen pen, Color paint, float width, float length, float wheel, float nose)
    {
        pen.Box(new(0, 0.2f, 0), new(width, 0.16f, length), paint);
        pen.Box(new(0, 0.36f, -length * 0.12f), new(width * 0.86f, 0.2f, length * 0.42f), paint.Lightened(0.05f));
        pen.Box(new(0, 0.38f, -length * 0.34f), new(width * 0.7f, 0.12f, 0.02f), new Color("b7d7dc"), Vector3.Zero, Shade.Glass);
        pen.Box(new(0, 0.16f, -length * 0.5f), new(width * 0.7f, 0.06f, 0.06f), paint.Darkened(0.15f));
        pen.Sphere(new(-width * 0.28f, 0.2f, -length * 0.48f), new(0.035f, 0.03f, 0.02f), new Color("f4f1d8"), 5, 3);
        pen.Sphere(new(width * 0.28f, 0.2f, -length * 0.48f), new(0.035f, 0.03f, 0.02f), new Color("f4f1d8"), 5, 3);
        foreach (float x in new[] { -1f, 1f })
        {
            pen.Box(new(x * width * 0.48f, 0.2f, -nose), new(0.06f, 0.08f, 0.22f), paint.Darkened(0.12f));
            pen.Box(new(x * width * 0.48f, 0.2f, length * 0.28f), new(0.06f, 0.08f, 0.22f), paint.Darkened(0.12f));
            Wheel(pen, new(x * (width * 0.5f + 0.02f), wheel, -length * 0.28f), wheel, false);
            Wheel(pen, new(x * (width * 0.5f + 0.02f), wheel, length * 0.28f), wheel, false);
            pen.Cylinder(new(0, wheel, -length * 0.28f), 0.02f, width, new Color("5c6360"), Vector3.Right, 5, false);
        }
    }

    private static void Van(Pen pen, Color paint)
    {
        pen.Box(new(0, 0.28f, 0.05f), new(0.58f, 0.36f, 0.7f), paint);
        pen.Box(new(0, 0.3f, -0.42f), new(0.52f, 0.28f, 0.28f), paint.Darkened(0.06f));
        pen.Box(new(0, 0.34f, -0.56f), new(0.4f, 0.12f, 0.02f), new Color("d5ebe8"), Vector3.Zero, Shade.Glass);
        pen.Box(new(0.3f, 0.28f, 0.05f), new(0.02f, 0.16f, 0.28f), paint.Darkened(0.18f));
        pen.Box(new(0, 0.48f, 0.05f), new(0.2f, 0.04f, 0.16f), new Color("f2e2a0"));
        foreach (float x in new[] { -1f, 1f })
        {
            Wheel(pen, new(x * 0.34f, 0.12f, -0.32f), 0.12f, false);
            Wheel(pen, new(x * 0.34f, 0.12f, 0.32f), 0.12f, false);
        }
    }

    private static void Jeep(Pen pen, Color paint)
    {
        pen.Box(new(0, 0.28f, 0), new(0.62f, 0.16f, 1.05f), paint);
        pen.Box(new(0, 0.46f, -0.18f), new(0.56f, 0.22f, 0.4f), paint.Lightened(0.04f));
        pen.Box(new(0, 0.46f, -0.39f), new(0.48f, 0.14f, 0.02f), new Color("c5ddd4"), Vector3.Zero, Shade.Glass);
        pen.Box(new(0, 0.22f, -0.58f), new(0.5f, 0.08f, 0.06f), new Color("4d5550"));
        pen.Cylinder(new(0, 0.32f, 0.52f), 0.1f, 0.06f, new Color("2c3330"), Vector3.Right, 8);
        pen.Box(new(0, 0.58f, -0.05f), new(0.5f, 0.03f, 0.7f), paint.Darkened(0.2f));
        foreach (float x in new[] { -1f, 1f })
        {
            pen.Box(new(x * 0.34f, 0.24f, -0.28f), new(0.08f, 0.1f, 0.28f), paint.Darkened(0.15f));
            pen.Box(new(x * 0.34f, 0.24f, 0.28f), new(0.08f, 0.1f, 0.28f), paint.Darkened(0.15f));
            Wheel(pen, new(x * 0.38f, 0.16f, -0.34f), 0.16f, true);
            Wheel(pen, new(x * 0.38f, 0.16f, 0.34f), 0.16f, true);
        }
    }

    private static void Truck(Pen pen, Color paint)
    {
        pen.Box(new(0, 0.28f, 0.15f), new(0.7f, 0.18f, 1.35f), new Color("4a524e"));
        pen.Box(new(0, 0.48f, -0.48f), new(0.66f, 0.32f, 0.42f), paint);
        pen.Box(new(0, 0.52f, -0.7f), new(0.5f, 0.16f, 0.02f), new Color("c9e0e4"), Vector3.Zero, Shade.Glass);
        pen.Box(new(0, 0.42f, 0.28f), new(0.66f, 0.28f, 0.78f), paint.Darkened(0.08f));
        pen.Box(new(0, 0.58f, 0.28f), new(0.68f, 0.03f, 0.8f), new Color("6d5a48"));
        pen.Cylinder(new(-0.28f, 0.62f, -0.35f), 0.03f, 0.22f, new Color("333834"), Vector3.Up, 6);
        foreach (float x in new[] { -1f, 1f })
        {
            Wheel(pen, new(x * 0.4f, 0.16f, -0.5f), 0.16f, false);
            Wheel(pen, new(x * 0.4f, 0.16f, 0.18f), 0.16f, false);
            Wheel(pen, new(x * 0.4f, 0.16f, 0.42f), 0.16f, false);
            pen.Box(new(x * 0.36f, 0.28f, -0.5f), new(0.06f, 0.1f, 0.22f), paint.Darkened(0.18f));
        }
        pen.Sphere(new(-0.22f, 0.4f, -0.68f), new(0.04f, 0.035f, 0.02f), new Color("f6f3df"), 5, 3);
        pen.Sphere(new(0.22f, 0.4f, -0.68f), new(0.04f, 0.035f, 0.02f), new Color("f6f3df"), 5, 3);
    }

    private static void Paver(Pen pen, Color paint)
    {
        pen.Box(new(0, 0.28f, 0.05f), new(0.7f, 0.28f, 0.7f), paint);
        pen.Box(new(0, 0.48f, 0.02f), new(0.5f, 0.12f, 0.4f), paint.Darkened(0.08f));
        pen.Box(new(0, 0.18f, 0.48f), new(0.95f, 0.08f, 0.18f), new Color("6a6458"));
        pen.Box(new(0, 0.34f, -0.28f), new(0.55f, 0.22f, 0.28f), paint.Lightened(0.06f));
        pen.Cylinder(new(0, 0.16f, -0.48f), 0.1f, 0.72f, new Color("8d928c"), Vector3.Right, 8);
        foreach (float x in new[] { -1f, 1f })
            pen.Box(new(x * 0.4f, 0.16f, 0), new(0.16f, 0.22f, 0.7f), new Color("3c403c"));
        pen.Cylinder(new(0, 0.62f, 0), 0.04f, 0.08f, new Color("e0703a"), Vector3.Up, 6);
        pen.Box(new(0, 0.52f, -0.12f), new(0.28f, 0.08f, 0.02f), new Color("d7e4e2"), Vector3.Zero, Shade.Glass);
    }

    private static void Combine(Pen body, Pen extra, int level, bool cozy)
    {
        Color green = new(cozy ? "3f7a52" : "2f6a40");
        Color yellow = new(cozy ? "e6c15a" : "e2bf57");
        body.Box(new(0, 0.28f, 0.05f), new(0.55f, 0.22f, 0.7f), green);
        body.Box(new(0, 0.48f, -0.05f), new(0.42f, 0.22f, 0.32f), green.Lightened(0.08f));
        body.Box(new(0, 0.5f, -0.22f), new(0.32f, 0.12f, 0.02f), new Color("d5ebe6"), Vector3.Zero, Shade.Glass);
        body.Cylinder(new(0, 0.42f, 0.22f), 0.16f, 0.28f, yellow.Darkened(0.05f), Vector3.Forward, 8);
        body.Box(new(0, 0.2f, -0.55f), new(0.85f, 0.12f, 0.16f), yellow);
        body.Cylinder(new(0, 0.3f, -0.55f), 0.05f, 0.78f, new Color("5c5344"), Vector3.Right, 6);
        Wheel(body, new(-0.32f, 0.18f, 0.12f), 0.18f, true);
        Wheel(body, new(0.32f, 0.18f, 0.12f), 0.18f, true);
        Wheel(body, new(-0.28f, 0.12f, -0.38f), 0.11f, false);
        Wheel(body, new(0.28f, 0.12f, -0.38f), 0.11f, false);
        if (level > 1)
        {
            extra.Box(new(0, 0.34f, 0.02f), new(0.6f, 0.05f, 0.74f), new Color("f4e27a"));
            extra.Box(new(0, 0.22f, -0.62f), new(0.95f, 0.08f, 0.1f), new Color("f4e7b0"));
            extra.Cylinder(new(0.18f, 0.52f, 0.2f), 0.09f, 0.16f, yellow, Vector3.Up, 6);
        }
    }

    private static void Forestry(Pen body, Pen extra, int level, bool cozy)
    {
        Color green = new(cozy ? "355844" : "243f30");
        body.Box(new(0, 0.26f, 0), new(0.62f, 0.24f, 0.85f), green);
        body.Box(new(0, 0.46f, -0.12f), new(0.4f, 0.22f, 0.32f), green.Lightened(0.1f));
        body.Box(new(0.22f, 0.55f, 0.05f), new(0.08f, 0.28f, 0.08f), new Color("8d6a3d"), new(0, 0, -28));
        body.Box(new(-0.05f, 0.72f, -0.12f), new(0.36f, 0.06f, 0.06f), new Color("c49b43"), new(0, 18, -8));
        body.Box(new(-0.24f, 0.66f, -0.22f), new(0.1f, 0.16f, 0.08f), new Color("4e5848"));
        body.Box(new(-0.3f, 0.58f, -0.22f), new(0.04f, 0.12f, 0.05f), new Color("2c3330"), new(0, 0, 24));
        foreach (float x in new[] { -1f, 1f })
            body.Box(new(x * 0.36f, 0.14f, 0), new(0.14f, 0.2f, 0.72f), new Color("323834"));
        if (level > 1)
        {
            extra.Box(new(0, 0.34f, 0), new(0.68f, 0.05f, 0.9f), new Color("e0b15a"));
            extra.Box(new(-0.32f, 0.58f, -0.2f), new(0.1f, 0.16f, 0.12f), new Color("e0703a"));
            extra.Cylinder(new(0.18f, 0.42f, 0.18f), 0.07f, 0.1f, new Color("6d5a48"), Vector3.Right, 6);
        }
    }

    private static void Dairy(Pen body, Pen extra, int level, bool cozy)
    {
        Color white = new(cozy ? "f4f7f6" : "e7eef2");
        Color blue = new(cozy ? "7eb0c4" : "5d8fa6");
        body.Box(new(0, 0.22f, -0.28f), new(0.48f, 0.28f, 0.32f), white.Darkened(0.04f));
        body.Box(new(0, 0.34f, -0.44f), new(0.36f, 0.12f, 0.02f), new Color("d5e7ea"), Vector3.Zero, Shade.Glass);
        body.Cylinder(new(0, 0.32f, 0.12f), 0.16f, 0.62f, white, Vector3.Forward, 10);
        body.Cylinder(new(0, 0.32f, 0.12f), 0.165f, 0.06f, blue, Vector3.Forward, 10);
        body.Box(new(0.18f, 0.28f, 0.12f), new(0.04f, 0.08f, 0.16f), blue);
        Wheel(body, new(-0.28f, 0.12f, -0.28f), 0.12f, false);
        Wheel(body, new(0.28f, 0.12f, -0.28f), 0.12f, false);
        Wheel(body, new(-0.28f, 0.12f, 0.32f), 0.12f, false);
        Wheel(body, new(0.28f, 0.12f, 0.32f), 0.12f, false);
        if (level > 1)
        {
            extra.Box(new(0, 0.38f, 0.05f), new(0.52f, 0.045f, 0.7f), new Color("f2d56a"));
            extra.Cylinder(new(-0.2f, 0.5f, 0.12f), 0.09f, 0.28f, blue.Lightened(0.12f), Vector3.Up, 8);
            extra.Box(new(-0.2f, 0.2f, 0.24f), new(0.05f, 0.1f, 0.14f), new Color("d7e4ea"));
        }
    }

    private static void Mine(Pen body, Pen extra, int level, bool cozy)
    {
        Color yellow = new(cozy ? "d7a441" : "c48a32");
        body.Box(new(0, 0.24f, 0.05f), new(0.55f, 0.22f, 0.7f), yellow);
        body.Box(new(0, 0.42f, 0.12f), new(0.4f, 0.2f, 0.34f), yellow.Darkened(0.08f));
        body.Box(new(0, 0.46f, -0.04f), new(0.28f, 0.1f, 0.02f), new Color("d5e4ea"), Vector3.Zero, Shade.Glass);
        body.Box(new(0.05f, 0.55f, -0.15f), new(0.08f, 0.28f, 0.08f), new Color("4a4038"), new(-40, 0, 0));
        body.Box(new(0.05f, 0.38f, -0.38f), new(0.07f, 0.22f, 0.07f), new Color("3a342e"), new(28, 0, 0));
        body.Box(new(0.05f, 0.24f, -0.5f), new(0.16f, 0.1f, 0.12f), new Color("5c5148"));
        foreach (float x in new[] { -0.05f, 0.05f, 0.15f })
            body.Box(new(x, 0.18f, -0.56f), new(0.02f, 0.04f, 0.04f), new Color("d9d4cc"));
        foreach (float x in new[] { -1f, 1f })
            body.Box(new(x * 0.34f, 0.14f, 0.02f), new(0.14f, 0.2f, 0.62f), new Color("343834"));
        if (level > 1)
        {
            extra.Box(new(0, 0.32f, 0.04f), new(0.6f, 0.05f, 0.74f), new Color("f2d56a"));
            extra.Box(new(0.05f, 0.16f, -0.62f), new(0.32f, 0.07f, 0.1f), new Color("e0703a"));
            extra.Cylinder(new(0, 0.56f, 0.1f), 0.055f, 0.12f, new Color("f0d78a"), Vector3.Up, 6);
        }
    }
}
