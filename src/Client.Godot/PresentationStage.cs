using Godot;

/// <summary>Простые фигуры карты. Позиции берутся из ядра, сдвиг между клетками только рисуется.</summary>
public partial class PresentationStage : Node3D
{
    public event System.Action<int, int>? CellChosen;
    private Camera3D _camera = null!;
    private MeshInstance3D _selection = null!;
    private readonly System.Collections.Generic.Dictionary<long, MeshInstance3D> _actors = new();
    private readonly System.Collections.Generic.Dictionary<long, Vector3> _from = new();
    private readonly System.Collections.Generic.Dictionary<long, Vector3> _to = new();
    private readonly System.Collections.Generic.Dictionary<long, MeshInstance3D> _trips = new();
    private readonly System.Collections.Generic.Dictionary<long, Vector2> _facing = new();
    private string _staticKey = "";
    private long _tick = -1;
    private float _fraction = 1;
    private bool _frozen = true;
    private bool _follow;

    public override void _Ready()
    {
        _camera = new Camera3D { Current = true, Fov = 42 };
        AddChild(_camera);
        var light = new DirectionalLight3D { RotationDegrees = new Vector3(-48, 30, 0) };
        AddChild(light);
        _selection = Solid(new BoxMesh { Size = new Vector3(0.9f, 0.06f, 0.9f) }, new Color(1, 0.85f, 0.2f));
        _selection.Visible = false;
        AddChild(_selection);
    }

    public override void _Process(double delta)
    {
        if (_frozen) return;
        _fraction = Mathf.Min(1, _fraction + (float)delta);
        PlaceMovers();
    }

    public void Show(Simulation.Hosting.PresentationFrame frame, bool frozen, long selectedBuilding, string you, bool followHuman)
    {
        _frozen = frozen;
        _follow = followHuman;
        if (_staticKey != StaticKey(frame))
        {
            _staticKey = StaticKey(frame);
            RebuildStatic(frame);
        }
        bool step = frame.Tick == _tick + 1;
        foreach (var actor in frame.Actors)
        {
            var body = Actor(actor.Id, actor.Human, actor.Human ? you : actor.Id.ToString());
            var next = new Vector3(actor.X + (actor.OffsetX - 500) / 1000f, 0.7f, actor.Z + (actor.OffsetZ - 500) / 1000f);
            _facing[actor.Id] = new Vector2(actor.FacingX, actor.FacingZ);
            if (!actor.Human && step && _to.TryGetValue(actor.Id, out var previous)) { _from[actor.Id] = previous; _fraction = 0; }
            else _from[actor.Id] = next;
            _to[actor.Id] = next;
        }
        var live = new System.Collections.Generic.HashSet<long>();
        foreach (var trip in frame.Trips)
        {
            live.Add(trip.LogisticsId);
            var cargo = Cargo(trip.LogisticsId, trip.ItemId);
            var next = new Vector3(trip.X, 0.45f, trip.Z);
            long key = -trip.LogisticsId;
            if (step && _to.TryGetValue(key, out var previous)) { _from[key] = previous; _fraction = 0; }
            else _from[key] = next;
            _to[key] = next;
            cargo.Visible = true;
        }
        foreach (var id in _trips.Keys.ToArray())
            if (!live.Contains(id)) _trips[id].Visible = false;
        var selected = frame.Markers.FirstOrDefault(m => m.Kind == "production" && m.EntityId == selectedBuilding);
        _selection.Visible = selected is not null;
        if (selected is not null) _selection.Position = new Vector3(selected.X, 0.08f, selected.Z);
        _tick = frame.Tick;
        if (frozen || !step) _fraction = 1;
        PlaceMovers();
        if (_follow) FollowHuman(frame);
    }

    private void FollowHuman(Simulation.Hosting.PresentationFrame frame)
    {
        var human = frame.Actors.FirstOrDefault(a => a.Human);
        if (human is null) return;
        float x = human.X + (human.OffsetX - 500) / 1000f;
        float z = human.Z + (human.OffsetZ - 500) / 1000f;
        // Фиксированный наклон: камера выше и сзади, W смотрит к верху кадра, впереди больше места.
        _camera.Rotation = new Vector3(Mathf.DegToRad(-55), 0, 0);
        _camera.Position = new Vector3(x, 0, z) + new Vector3(0, 16, 10);
    }

    public void Pick(Vector2 viewportPosition)
    {
        var origin = _camera.ProjectRayOrigin(viewportPosition);
        var direction = _camera.ProjectRayNormal(viewportPosition);
        if (Mathf.Abs(direction.Y) < 0.0001f) return;
        float distance = -origin.Y / direction.Y;
        if (distance < 0) return;
        var hit = origin + direction * distance;
        CellChosen?.Invoke(Mathf.RoundToInt(hit.X), Mathf.RoundToInt(hit.Z));
    }

    private void PlaceMovers()
    {
        foreach (var actor in _actors)
            if (_from.TryGetValue(actor.Key, out var from) && _to.TryGetValue(actor.Key, out var to))
            {
                actor.Value.Position = from.Lerp(to, _fraction);
                if (_facing.TryGetValue(actor.Key, out var facing) && (facing.X != 0 || facing.Y != 0))
                    actor.Value.LookAt(actor.Value.Position + new Vector3(facing.X, 0, facing.Y), Vector3.Up);
            }
        foreach (var trip in _trips)
        {
            long key = -trip.Key;
            if (_from.TryGetValue(key, out var from) && _to.TryGetValue(key, out var to))
                trip.Value.Position = from.Lerp(to, _fraction);
        }
    }

    private void RebuildStatic(Simulation.Hosting.PresentationFrame frame)
    {
        foreach (Node child in GetChildren())
            if (child != _camera && child != _selection && child is not DirectionalLight3D) child.QueueFree();
        _actors.Clear();
        _trips.Clear();
        _facing.Clear();
        if (!_follow)
        {
            float centerX = frame.MapWidth / 2f - 0.5f;
            float centerZ = frame.MapHeight / 2f - 0.5f;
            float span = Mathf.Max(frame.MapWidth, frame.MapHeight);
            _camera.Position = new Vector3(centerX, span * 1.05f, centerZ + span * 0.95f);
            _camera.LookAt(new Vector3(centerX, 0, centerZ), Vector3.Up);
        }
        for (int x = 0; x < frame.MapWidth; x++)
            for (int z = 0; z < frame.MapHeight; z++)
                AddChild(Tile(x, z, new Color(0.42f, 0.52f, 0.3f)));
        foreach (var marker in frame.Markers)
        {
            var node = marker.Kind switch
            {
                "blocked" => Tile(marker.X, marker.Z, new Color(0.22f, 0.23f, 0.26f), 0.03f),
                "corridor" => Tile(marker.X, marker.Z, new Color(0.58f, 0.52f, 0.4f), 0.03f),
                "plot" or "plot-free" => Tile(marker.X, marker.Z, new Color(0.62f, 0.45f, 0.26f), 0.03f),
                "plot-own" => Tile(marker.X, marker.Z, new Color(0.4f, 0.58f, 0.28f), 0.03f),
                "plot-foreign" => Tile(marker.X, marker.Z, new Color(0.45f, 0.28f, 0.24f), 0.03f),
                "plot-reserved" => Tile(marker.X, marker.Z, new Color(0.72f, 0.55f, 0.22f), 0.03f),
                "shop-plot" => Tile(marker.X, marker.Z, new Color(0.46f, 0.36f, 0.55f), 0.03f),
                "resource" => Mark(new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0.22f, Height = 0.7f },
                    marker.Label == "wood" ? new Color(0.25f, 0.55f, 0.22f) : new Color(0.72f, 0.45f, 0.16f),
                    new Vector3(marker.X, 0.35f, marker.Z), ""),
                "production" => Mark(new BoxMesh { Size = new Vector3(0.7f, 0.7f, 0.7f) }, new Color(0.55f, 0.32f, 0.18f),
                    new Vector3(marker.X, 0.35f, marker.Z), ""),
                "warehouse" => Mark(new BoxMesh { Size = new Vector3(0.75f, 0.45f, 0.75f) }, new Color(0.25f, 0.4f, 0.62f),
                    new Vector3(marker.X, 0.25f, marker.Z), ""),
                "shop" => Mark(new BoxMesh { Size = new Vector3(0.6f, 0.55f, 0.6f) }, new Color(0.55f, 0.28f, 0.62f),
                    new Vector3(marker.X, 0.3f, marker.Z), ""),
                "capital" => Mark(new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.28f, Height = 1.1f },
                    new Color(0.9f, 0.75f, 0.15f), new Vector3(marker.X, 0.55f, marker.Z), ""),
                _ => null
            };
            if (node is not null) AddChild(node);
        }
    }

    private MeshInstance3D Actor(long id, bool human, string caption)
    {
        if (_actors.TryGetValue(id, out var existing)) return existing;
        var color = human ? new Color(0.95f, 0.95f, 0.95f) : id switch
        {
            2 => new Color(0.85f, 0.25f, 0.22f),
            3 => new Color(0.2f, 0.45f, 0.9f),
            _ => new Color(0.95f, 0.55f, 0.15f)
        };
        var body = Mark(new CapsuleMesh { Radius = 0.22f, Height = 0.9f }, color, Vector3.Zero, caption);
        if (human)
        {
            var nose = Solid(new BoxMesh { Size = new Vector3(0.08f, 0.08f, 0.18f) }, new Color(0.15f, 0.15f, 0.15f));
            nose.Position = new Vector3(0, 0.15f, -0.42f);
            body.AddChild(nose);
        }
        AddChild(body);
        _actors[id] = body;
        return body;
    }

    private MeshInstance3D Cargo(long id, string item)
    {
        if (_trips.TryGetValue(id, out var existing)) return existing;
        var cargo = Mark(new BoxMesh { Size = new Vector3(0.38f, 0.28f, 0.55f) }, new Color(0.95f, 0.82f, 0.2f), Vector3.Zero, item);
        AddChild(cargo);
        _trips[id] = cargo;
        return cargo;
    }

    private static string StaticKey(Simulation.Hosting.PresentationFrame frame) =>
        frame.MapWidth + "x" + frame.MapHeight + "|" + string.Join(",", frame.Markers.Select(m => $"{m.Kind}:{m.X}:{m.Z}:{m.Label}:{m.EntityId}"));

    private static MeshInstance3D Tile(int x, int z, Color color, float y = 0)
    {
        var tile = Solid(new BoxMesh { Size = new Vector3(0.94f, 0.04f, 0.94f) }, color);
        tile.Position = new Vector3(x, y, z);
        return tile;
    }

    private static MeshInstance3D Mark(Mesh mesh, Color color, Vector3 position, string caption)
    {
        var node = Solid(mesh, color);
        node.Position = position;
        if (caption.Length > 0)
        {
            node.AddChild(new Label3D
            {
                Text = caption,
                FontSize = 28,
                PixelSize = 0.012f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Position = new Vector3(0, 0.85f, 0),
                OutlineSize = 6
            });
        }
        return node;
    }

    private static MeshInstance3D Solid(Mesh mesh, Color color)
    {
        var node = new MeshInstance3D { Mesh = mesh };
        node.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
        {
            AlbedoColor = color,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        });
        return node;
    }
}
