using Godot;

/// <summary>Общие материалы и сборка низкополигональных мешей. Геометрия кэшируется на клетку, материалы — на весь мир.</summary>
public static partial class WorldArt
{
    private enum Shade { Lit, Flat, Metal, Glass }

    private static readonly Dictionary<Shade, StandardMaterial3D> Materials = new();
    public static int CellsBuilt { get; private set; }
    public static int VerticesBuilt { get; private set; }

    private static StandardMaterial3D Material(Shade shade)
    {
        if (Materials.TryGetValue(shade, out var ready)) return ready;
        var material = new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            VertexColorUseAsAlbedo = true,
            Roughness = shade == Shade.Glass ? 0.16f : shade == Shade.Metal ? 0.42f : 0.9f,
            Metallic = shade == Shade.Metal ? 0.55f : 0,
            MetallicSpecular = shade == Shade.Glass ? 0.45f : 0.12f,
            ShadingMode = shade == Shade.Flat ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        Materials[shade] = material;
        return material;
    }

    private sealed class Pen
    {
        private readonly Dictionary<Shade, SurfaceTool> _tools = new();
        private readonly Dictionary<Shade, int> _counts = new();

        public void Box(Vector3 center, Vector3 size, Color color, Vector3 euler = default, Shade shade = Shade.Lit)
        {
            var basis = Basis.FromEuler(new Vector3(Mathf.DegToRad(euler.X), Mathf.DegToRad(euler.Y), Mathf.DegToRad(euler.Z)));
            Vector3 h = size * 0.5f;
            Face(center, basis, color, shade, new(0, 1, 0), new(-h.X, h.Y, -h.Z), new(h.X, h.Y, -h.Z), new(h.X, h.Y, h.Z), new(-h.X, h.Y, h.Z));
            Face(center, basis, color, shade, new(0, -1, 0), new(-h.X, -h.Y, h.Z), new(h.X, -h.Y, h.Z), new(h.X, -h.Y, -h.Z), new(-h.X, -h.Y, -h.Z));
            Face(center, basis, color, shade, new(0, 0, 1), new(-h.X, -h.Y, h.Z), new(-h.X, h.Y, h.Z), new(h.X, h.Y, h.Z), new(h.X, -h.Y, h.Z));
            Face(center, basis, color, shade, new(0, 0, -1), new(h.X, -h.Y, -h.Z), new(h.X, h.Y, -h.Z), new(-h.X, h.Y, -h.Z), new(-h.X, -h.Y, -h.Z));
            Face(center, basis, color, shade, new(1, 0, 0), new(h.X, -h.Y, h.Z), new(h.X, h.Y, h.Z), new(h.X, h.Y, -h.Z), new(h.X, -h.Y, -h.Z));
            Face(center, basis, color, shade, new(-1, 0, 0), new(-h.X, -h.Y, -h.Z), new(-h.X, h.Y, -h.Z), new(-h.X, h.Y, h.Z), new(-h.X, -h.Y, h.Z));
        }

        public void Cylinder(Vector3 center, float radius, float height, Color color, Vector3 axis, int seg = 8, bool caps = true, Shade shade = Shade.Lit)
        {
            axis = axis.LengthSquared() < 0.0001f ? Vector3.Up : axis.Normalized();
            Vector3 helper = Mathf.Abs(axis.Dot(Vector3.Up)) > 0.92f ? Vector3.Right : Vector3.Up;
            Vector3 side = axis.Cross(helper).Normalized();
            Vector3 forward = side.Cross(axis).Normalized();
            float half = height * 0.5f;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.Tau / seg, a1 = (i + 1) * Mathf.Tau / seg;
                Vector3 r0 = side * Mathf.Cos(a0) * radius + forward * Mathf.Sin(a0) * radius;
                Vector3 r1 = side * Mathf.Cos(a1) * radius + forward * Mathf.Sin(a1) * radius;
                Vector3 n0 = r0.Normalized(), n1 = r1.Normalized();
                Tri(shade, color, center + r0 + axis * -half, n0, center + r0 + axis * half, n0, center + r1 + axis * half, n1);
                Tri(shade, color, center + r0 + axis * -half, n0, center + r1 + axis * half, n1, center + r1 + axis * -half, n1);
                if (!caps) continue;
                Tri(shade, color, center + axis * half, axis, center + r0 + axis * half, axis, center + r1 + axis * half, axis);
                Tri(shade, color, center + axis * -half, -axis, center + r1 + axis * -half, -axis, center + r0 + axis * -half, -axis);
            }
        }

        public void Sphere(Vector3 center, Vector3 radius, Color color, int seg = 7, int rings = 4, Shade shade = Shade.Lit)
        {
            for (int y = 0; y < rings; y++)
            {
                float v0 = y / (float)rings, v1 = (y + 1) / (float)rings;
                float p0 = Mathf.Lerp(-Mathf.Pi * 0.5f, Mathf.Pi * 0.5f, v0);
                float p1 = Mathf.Lerp(-Mathf.Pi * 0.5f, Mathf.Pi * 0.5f, v1);
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i * Mathf.Tau / seg, a1 = (i + 1) * Mathf.Tau / seg;
                    Vector3 A = OnSphere(a0, p0), B = OnSphere(a1, p0), C = OnSphere(a0, p1), D = OnSphere(a1, p1);
                    Tri(shade, color, center + Scale(A), A, center + Scale(C), C, center + Scale(D), D);
                    Tri(shade, color, center + Scale(A), A, center + Scale(D), D, center + Scale(B), B);
                }
            }
            Vector3 Scale(Vector3 n) => new(n.X * radius.X, n.Y * radius.Y, n.Z * radius.Z);
            static Vector3 OnSphere(float a, float p) => new(Mathf.Cos(p) * Mathf.Cos(a), Mathf.Sin(p), Mathf.Cos(p) * Mathf.Sin(a));
        }

        public ArrayMesh? Build()
        {
            ArrayMesh? mesh = null;
            foreach (var (shade, tool) in _tools)
            {
                if (_counts.GetValueOrDefault(shade) == 0) continue;
                mesh ??= new ArrayMesh();
                tool.SetMaterial(Material(shade));
                tool.Commit(mesh);
            }
            return mesh;
        }

        private void Face(Vector3 center, Basis basis, Color color, Shade shade, Vector3 normal, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 n = (basis * normal).Normalized();
            Tri(shade, color, center + basis * a, n, center + basis * b, n, center + basis * c, n);
            Tri(shade, color, center + basis * a, n, center + basis * c, n, center + basis * d, n);
        }

        private void Tri(Shade shade, Color color, Vector3 a, Vector3 na, Vector3 b, Vector3 nb, Vector3 c, Vector3 nc)
        {
            var tool = Tool(shade);
            Put(tool, a, na, color);
            Put(tool, b, nb, color);
            Put(tool, c, nc, color);
            _counts[shade] = _counts.GetValueOrDefault(shade) + 3;
            VerticesBuilt += 3;
        }

        private SurfaceTool Tool(Shade shade)
        {
            if (_tools.TryGetValue(shade, out var tool)) return tool;
            tool = new SurfaceTool();
            tool.Begin(Mesh.PrimitiveType.Triangles);
            _tools[shade] = tool;
            return tool;
        }

        private static void Put(SurfaceTool tool, Vector3 point, Vector3 normal, Color color)
        {
            tool.SetColor(color);
            tool.SetNormal(normal.LengthSquared() < 0.0001f ? Vector3.Up : normal.Normalized());
            tool.AddVertex(point);
        }
    }

    private static MeshInstance3D Instance(ArrayMesh? mesh, string name)
    {
        var node = new MeshInstance3D { Name = name, Mesh = mesh ?? new ArrayMesh() };
        return node;
    }
}
