using Godot;
using Simulation.Hosting;

public static partial class WorldArt
{
    public static string MapKey { get; set; } = "map";

    public static string StateOf(PresentationMarker marker)
    {
        bool empty = marker.Kind == "resource-depleted" || marker.ResourceStock <= 0;
        long capacity = Math.Max(1, marker.ResourceCapacity);
        long stock = empty ? 0 : marker.ResourceStock;
        if (marker.Label is "grain" or "beet" or "milk" && !WorldArtScatter.ExperimentalCrops)
            return AppearanceCatalog.ResourceModel(marker.Label, stock, capacity, marker.ResourceSparse);
        if (marker.Label == "wood") return AppearanceCatalog.ResourceModel("wood", empty ? 0 : marker.ResourceStock, capacity, marker.ResourceSparse);
        if (marker.Label == "milk") return empty ? "Pasture_Empty" : AppearanceCatalog.ResourceModel("milk", marker.ResourceStock, capacity, marker.ResourceSparse);
        if (marker.Label is "grain" or "beet") return empty ? "EmptyField" : AppearanceCatalog.ResourceModel(marker.Label, marker.ResourceStock, capacity, marker.ResourceSparse);
        if (empty) return "spent";
        if (marker.ResourceSparse || marker.ResourceStock * 2 < capacity) return "thin";
        return "full";
    }

    public static Node3D Resource(PresentationMarker marker, bool cozy)
    {
        CellsBuilt++;
        string state = StateOf(marker);
        var marks = WorldArtScatter.Layout(MapKey, marker.X, marker.Z, marker.Label, state);
        var pen = new Pen();
        if (marker.Label == "wood") Forest(pen, marks, state, cozy);
        else if (marker.Label == "grain" && WorldArtScatter.ExperimentalCrops) Grain(pen, marks, state, cozy);
        else if (marker.Label == "beet" && WorldArtScatter.ExperimentalCrops) Beet(pen, marks, state, cozy);
        else if (marker.Label == "milk" && WorldArtScatter.ExperimentalCrops) Pasture(pen, marks, cozy);
        else if (marker.Label is "stone" or "sand" or "iron_ore" or "copper_ore") Deposit(pen, marks, marker.Label, state, cozy);
        var root = new Node3D { Name = "ResourceArt" };
        root.SetMeta("skin_set", cozy ? "cozy" : "classic");
        root.SetMeta("resource_item", marker.Label);
        root.SetMeta("resource_state", state);
        root.AddChild(Instance(pen.Build(), "Body"));
        return root;
    }

    private static void Forest(Pen pen, WorldArtScatter.Mark[] marks, string state, bool cozy)
    {
        bool cut = state.Contains("Cut", StringComparison.Ordinal);
        foreach (var mark in marks)
        {
            bool sapling = cut && mark.Scale < 0.5f;
            float lean = Mathf.DegToRad(mark.Lean);
            Vector3 shift = new(Mathf.Sin(lean), 0, Mathf.Cos(Mathf.DegToRad(mark.Yaw)) * lean * 0.15f);
            if (cut && !sapling)
            {
                pen.Cylinder(new(mark.X, 0.035f, mark.Z), 0.055f * mark.Scale, 0.07f, Bark(mark.Tint, cozy), Vector3.Up, 7);
                pen.Cylinder(new(mark.X, 0.072f, mark.Z), 0.072f * mark.Scale, 0.018f, Bark(mark.Tint, cozy).Darkened(0.12f), Vector3.Up, 7);
                continue;
            }
            float height = (sapling ? 0.3f : 0.95f) * mark.Scale;
            float trunk = height * (mark.Variant == 3 ? 0.26f : mark.Variant == 2 ? 0.55f : 0.4f);
            pen.Cylinder(new(mark.X, 0.008f, mark.Z), 0.07f, 0.016f, Soil(cozy), Vector3.Up, 7);
            pen.Cylinder(new(mark.X, trunk * 0.5f, mark.Z), mark.Variant == 2 ? 0.028f : 0.042f, trunk, Bark(mark.Tint, cozy), Vector3.Up, 7);
            Vector3 crown = new Vector3(mark.X, trunk, mark.Z) + shift;
            float radius = (mark.Variant == 2 ? 0.15f : mark.Variant == 3 ? 0.32f : 0.23f) * (sapling ? 0.7f : mark.Scale);
            Color leaf = Leaf(mark.Tint, cozy);
            if (mark.Variant == 1)
            {
                pen.Sphere(crown + new Vector3(0, radius * 0.55f, 0), new(radius, radius * 0.7f, radius), leaf, 7, 4);
                pen.Sphere(crown + new Vector3(radius * 0.28f, radius * 0.28f, radius * 0.08f), new(radius * 0.5f, radius * 0.38f, radius * 0.5f), Leaf((mark.Tint + 1) % 3, cozy), 6, 3);
            }
            else if (mark.Variant == 3)
                pen.Sphere(crown + new Vector3(0, radius * 0.26f, 0), new(radius, radius * 0.4f, radius * 0.86f), leaf, 7, 3);
            else
            {
                pen.Sphere(crown + new Vector3(0, radius * 0.45f, 0), new(radius, radius * 0.95f, radius), leaf, 7, 4);
                pen.Sphere(crown + new Vector3(0, radius * 1.05f, 0), new(radius * 0.62f, radius * 0.55f, radius * 0.62f), Leaf((mark.Tint + 1) % 3, cozy), 6, 3);
                pen.Sphere(crown + new Vector3(0, radius * 1.45f, 0), new(radius * 0.26f, radius * 0.3f, radius * 0.26f), Leaf((mark.Tint + 2) % 3, cozy), 5, 3);
            }
        }
    }

    // Экспериментальный вид I6-C3. В обычной партии его не выбирают.
    private static void Grain(Pen pen, WorldArtScatter.Mark[] marks, string state, bool cozy)
    {
        pen.Cylinder(new(0, 0.012f, 0), 0.42f, 0.022f, Soil(cozy).Lerp(new Color(cozy ? "c6a15a" : "b1843e"), 0.4f), Vector3.Up, 10);
        bool stubble = state == "EmptyField";
        foreach (var mark in marks)
        {
            float height = (stubble ? 0.07f : 0.31f) * mark.Scale;
            bool rye = mark.Tint == 2;
            Color stem = rye ? new Color(cozy ? "a8a86a" : "8ea24a") : new Color(cozy ? "c9a84e" : "d7b24a");
            Color head = rye ? new Color(cozy ? "d5d2a0" : "c6d08a") : mark.Tint == 1 ? new Color(cozy ? "e6c56e" : "f2d27a") : new Color(cozy ? "f3d898" : "ffe7a0");
            var basis = Basis.FromEuler(new Vector3(Mathf.DegToRad(mark.Lean), Mathf.DegToRad(mark.Yaw), 0));
            Vector3 dir = basis * Vector3.Up;
            Vector3 origin = new(mark.X, height * 0.48f, mark.Z);
            pen.Cylinder(origin, stubble ? 0.008f : 0.016f, height, stem, dir, 5);
            if (!stubble)
                pen.Sphere(origin + dir * (height * 0.42f), new(0.032f, 0.08f * mark.Scale, 0.024f), head, 5, 3);
        }
    }

    private static void Beet(Pen pen, WorldArtScatter.Mark[] marks, string state, bool cozy)
    {
        pen.Cylinder(new(0, 0.01f, 0), 0.4f, 0.02f, Soil(cozy).Darkened(state == "EmptyField" ? 0.12f : 0.05f), Vector3.Up, 10);
        foreach (var mark in marks)
        {
            float plump = (0.1f + mark.Variant * 0.018f) * mark.Scale;
            Color root = mark.Tint switch
            {
                1 => new Color(cozy ? "d36b78" : "c24a62"),
                2 => new Color(cozy ? "a84d66" : "8e3c52"),
                _ => new Color(cozy ? "c45b74" : "b04360")
            };
            float sink = plump * 0.35f;
            pen.Sphere(new(mark.X, plump * 0.55f - sink, mark.Z), new(plump * 0.85f, plump, plump * 0.8f), root, 7, 4);
            pen.Sphere(new(mark.X, plump * 0.15f, mark.Z), new(plump * 0.45f, plump * 0.18f, plump * 0.45f), Soil(cozy), 6, 3);
            int leaves = 4 + mark.Variant;
            for (int i = 0; i < leaves; i++)
            {
                float yaw = mark.Yaw + i * (360f / leaves) + mark.Lean;
                var basis = Basis.FromEuler(new Vector3(Mathf.DegToRad(-58), Mathf.DegToRad(yaw), 0));
                Vector3 at = new Vector3(mark.X, plump * 0.85f, mark.Z) + basis * new Vector3(0, 0.07f * mark.Scale, 0);
                pen.Box(at, new(0.035f * mark.Scale, 0.16f * mark.Scale, 0.012f), Leaf(i % 3, cozy), new(-58, yaw, 0));
            }
        }
    }

    private static void Pasture(Pen pen, WorldArtScatter.Mark[] marks, bool cozy)
    {
        pen.Cylinder(new(0, 0.008f, 0), 0.44f, 0.016f, new Color(cozy ? "7ea36a" : "6e9458"), Vector3.Up, 10);
        foreach (var mark in marks)
        {
            if (mark.Variant >= 10)
            {
                for (int i = 0; i < 3; i++)
                {
                    float yaw = mark.Yaw + i * 38f;
                    pen.Box(new(mark.X, 0.05f * mark.Scale, mark.Z), new(0.012f, 0.1f * mark.Scale, 0.008f), Leaf((mark.Tint + i) % 3, cozy), new(-8 + i * 6, yaw, 0));
                }
                continue;
            }
            Cow(pen, mark, cozy);
        }
    }

    private static void Cow(Pen pen, WorldArtScatter.Mark mark, bool cozy)
    {
        float s = mark.Scale;
        var basis = Basis.FromEuler(new Vector3(0, Mathf.DegToRad(mark.Yaw), 0));
        Vector3 P(float x, float y, float z) => new Vector3(mark.X, 0, mark.Z) + basis * new Vector3(x, y, z);
        Color hide = new(cozy ? "f4efe4" : "f7f4ee");
        Color dark = new(cozy ? "2c2926" : "1c1a18");
        Color nose = new(cozy ? "e7b7aa" : "e0a898");
        bool graze = mark.Variant == 1;
        float headDrop = graze ? 0.08f : 0f;
        float headForward = graze ? 0.08f : 0f;
        pen.Sphere(P(0, 0.22f * s, 0.02f * s), new(0.16f * s, 0.09f * s, 0.28f * s), hide, 8, 5);
        pen.Sphere(P(0, 0.25f * s, -0.12f * s), new(0.12f * s, 0.1f * s, 0.1f * s), hide, 7, 4);
        for (int i = 0; i < 4; i++)
        {
            float x = i % 2 == 0 ? -0.07f : 0.07f;
            float z = i < 2 ? -0.12f : 0.12f;
            float step = mark.Variant == 2 && i == 0 ? 0.04f : 0f;
            pen.Cylinder(P(x * s, 0.09f * s, (z + step) * s), 0.028f * s, 0.16f * s, hide.Darkened(0.04f), Vector3.Up, 6);
            pen.Sphere(P(x * s, 0.02f * s, (z + step) * s), new(0.03f * s, 0.018f * s, 0.04f * s), dark, 5, 3);
        }
        Vector3 head = P(0, (0.3f - headDrop) * s, (-0.36f - headForward) * s);
        pen.Sphere(head, new(0.08f * s, 0.07f * s, 0.1f * s), hide, 7, 4);
        pen.Sphere(head + basis * new Vector3(0, -0.015f * s, -0.07f * s), new(0.04f * s, 0.03f * s, 0.04f * s), nose, 6, 3);
        pen.Box(head + basis * new Vector3(-0.055f * s, 0.03f * s, 0), new(0.035f * s, 0.02f * s, 0.012f), hide.Darkened(0.08f), new(0, mark.Yaw, 18));
        pen.Box(head + basis * new Vector3(0.055f * s, 0.03f * s, 0), new(0.035f * s, 0.02f * s, 0.012f), hide.Darkened(0.08f), new(0, mark.Yaw, -18));
        pen.Cylinder(head + basis * new Vector3(-0.04f * s, 0.05f * s, 0.01f * s), 0.008f * s, 0.04f * s, new Color(cozy ? "d9c3a4" : "cbb89a"), basis * Vector3.Up, 5);
        pen.Cylinder(head + basis * new Vector3(0.04f * s, 0.05f * s, 0.01f * s), 0.008f * s, 0.04f * s, new Color(cozy ? "d9c3a4" : "cbb89a"), basis * Vector3.Up, 5);
        pen.Sphere(P(0, 0.16f * s, -0.02f * s), new(0.05f * s, 0.04f * s, 0.05f * s), new Color(cozy ? "f3c7c0" : "e8b4ae"), 6, 3);
        Spot(pen, P(0.05f * s, 0.3f * s, 0.06f * s), Vector3.Up, 0.1f * s, 0.14f * s, dark);
        Spot(pen, P(-0.06f * s, 0.29f * s, -0.04f * s), Vector3.Up, 0.08f * s, 0.1f * s, dark);
        Spot(pen, P(0.02f * s, 0.28f * s, 0.16f * s), basis * new Vector3(0.2f, 0.8f, 0.3f), 0.06f * s, 0.08f * s, dark);
        pen.Cylinder(P(0, 0.26f * s, 0.24f * s), 0.012f * s, 0.12f * s, hide.Darkened(0.06f), basis * new Vector3(0.1f, -0.2f, 1), 5, false);
    }

    private static void Spot(Pen pen, Vector3 center, Vector3 normal, float width, float length, Color color)
    {
        Vector3 n = normal.Normalized();
        Vector3 helper = Mathf.Abs(n.Dot(Vector3.Up)) > 0.92f ? Vector3.Right : Vector3.Up;
        Vector3 side = n.Cross(helper).Normalized();
        Vector3 forward = side.Cross(n).Normalized();
        Vector3 euler = new Basis(side, n, forward).GetEuler();
        pen.Box(center + n * 0.002f, new(width, 0.012f, length), color, new(Mathf.RadToDeg(euler.X), Mathf.RadToDeg(euler.Y), Mathf.RadToDeg(euler.Z)));
    }

    private static void Deposit(Pen pen, WorldArtScatter.Mark[] marks, string item, string state, bool cozy)
    {
        bool spent = state == "spent";
        foreach (var mark in marks)
        {
            float s = mark.Scale * (spent ? 0.7f : 1f);
            if (item == "sand") Sand(pen, mark, s, cozy);
            else if (item == "iron_ore") Iron(pen, mark, s, cozy);
            else if (item == "copper_ore") Copper(pen, mark, s, cozy);
            else Stone(pen, mark, s, cozy);
        }
    }

    private static void Stone(Pen pen, WorldArtScatter.Mark mark, float s, bool cozy)
    {
        Color gray = mark.Tint switch
        {
            1 => new Color(cozy ? "9aa09c" : "8b908c"),
            2 => new Color(cozy ? "6e756f" : "5e6662"),
            _ => new Color(cozy ? "8d928c" : "7c817c")
        };
        float h = (0.12f + mark.Variant * 0.05f) * s;
        pen.Box(new(mark.X, h * 0.32f, mark.Z), new(0.3f * s, h * 0.7f, 0.22f * s), gray, new(mark.Lean, mark.Yaw, mark.Lean * 0.4f));
        pen.Box(new(mark.X + 0.06f * s, h * 0.22f, mark.Z - 0.05f * s), new(0.12f * s, h * 0.7f, 0.14f * s), gray.Darkened(0.08f), new(-mark.Lean, mark.Yaw + 28, 8));
        if (!spentLike(s)) pen.Box(new(mark.X - 0.05f * s, h * 0.55f, mark.Z + 0.04f * s), new(0.1f * s, h * 0.45f, 0.09f * s), gray.Lightened(0.08f), new(12, mark.Yaw - 20, -6));
    }

    private static bool spentLike(float s) => s < 0.45f;

    private static void Sand(Pen pen, WorldArtScatter.Mark mark, float s, bool cozy)
    {
        Color sand = mark.Tint switch
        {
            1 => new Color(cozy ? "e6c98a" : "d7b56e"),
            2 => new Color(cozy ? "c9a56a" : "c09658"),
            _ => new Color(cozy ? "f0d7a4" : "e2c48a")
        };
        float mound = 0.04f * s + 0.018f;
        pen.Sphere(new(mark.X, mound * 0.2f, mark.Z), new(0.3f * s, mound, 0.24f * s), sand, 8, 4);
        pen.Sphere(new(mark.X + 0.1f * s, mound * 0.12f, mark.Z + 0.05f * s), new(0.16f * s, mound * 0.6f, 0.13f * s), sand.Darkened(0.06f), 7, 3);
    }

    private static void Iron(Pen pen, WorldArtScatter.Mark mark, float s, bool cozy)
    {
        Color rock = new(cozy ? "6d645e" : "5c554f");
        Color metal = new(cozy ? "3e4248" : "2c3036");
        Color rust = new(cozy ? "8a4e3a" : "7a4030");
        float h = 0.22f * s;
        pen.Box(new(mark.X, h * 0.4f, mark.Z), new(0.2f * s, h, 0.16f * s), rock, new(mark.Lean, mark.Yaw, 6));
        pen.Box(new(mark.X + 0.04f * s, h * 0.62f, mark.Z), new(0.08f * s, 0.06f * s, 0.07f * s), metal, new(0, mark.Yaw, 0), Shade.Metal);
        pen.Box(new(mark.X - 0.05f * s, h * 0.35f, mark.Z + 0.04f * s), new(0.05f * s, 0.04f * s, 0.06f * s), rust, new(10, mark.Yaw + 20, 0));
        if (!spentLike(s)) pen.Box(new(mark.X + 0.07f * s, h * 0.25f, mark.Z - 0.04f * s), new(0.1f * s, h * 0.55f, 0.08f * s), rock.Darkened(0.1f), new(-8, mark.Yaw - 16, 4));
    }

    private static void Copper(Pen pen, WorldArtScatter.Mark mark, float s, bool cozy)
    {
        Color rock = new(cozy ? "7a6858" : "6a5848");
        Color copper = new(cozy ? "b8734a" : "c47a42");
        Color oxide = new(cozy ? "6e8f86" : "4f8a80");
        float h = 0.12f * s;
        pen.Box(new(mark.X, h * 0.45f, mark.Z), new(0.28f * s, h, 0.1f * s), rock, new(0, mark.Yaw, mark.Lean * 0.3f));
        pen.Box(new(mark.X + 0.04f * s, h * 0.7f, mark.Z), new(0.08f * s, 0.05f * s, 0.05f * s), copper, new(0, mark.Yaw + 12, 8), Shade.Metal);
        pen.Box(new(mark.X - 0.06f * s, h * 0.4f, mark.Z + 0.02f * s), new(0.06f * s, 0.035f * s, 0.05f * s), oxide, new(-6, mark.Yaw, 0));
        if (!spentLike(s)) pen.Box(new(mark.X, h + 0.04f * s, mark.Z - 0.02f * s), new(0.07f * s, 0.1f * s, 0.06f * s), rock.Lightened(0.05f), new(14, mark.Yaw - 24, -4));
    }

    private static Color Bark(int tint, bool cozy) => tint switch
    {
        1 => new Color(cozy ? "8a6244" : "6d4a30"),
        2 => new Color(cozy ? "6d4c34" : "5a3b26"),
        _ => new Color(cozy ? "7a5638" : "6b4630")
    };
    private static Color Leaf(int tint, bool cozy) => tint switch
    {
        1 => new Color(cozy ? "8eae68" : "67a34a"),
        2 => new Color(cozy ? "3f6844" : "2f6a38"),
        _ => new Color(cozy ? "5d8c55" : "3f8a45")
    };
    private static Color Soil(bool cozy) => new(cozy ? "8d6a45" : "7a5738");
}
