using Godot;
using Simulation.Hosting;

/// <summary>Rendering only. Fixed-step world positions are interpolated independently of FPS.</summary>
public partial class PresentationStage : Node3D
{
    public event System.Action<int, int>? CellChosen;
    private Camera3D _camera = null!;
    private MeshInstance3D _selection = null!;
    private sealed class Mover(Node3D node, Vector3 at)
    {
        public Node3D Node = node;
        public Vector3 From = at;
        public Vector3 To = at;
        public float Elapsed = 1;
        public float Duration = 0.1f;
        public bool Human;
        public bool Loaded;
    }
    private readonly System.Collections.Generic.Dictionary<long, Mover> _actors = new();
    private readonly Dictionary<long,Label3D> _speechLabels=[];
    private readonly System.Collections.Generic.Dictionary<long, Mover> _trips = new();
    private readonly System.Collections.Generic.List<Node3D> _staticNodes = new();
    private string _staticKey = "";
    private readonly Dictionary<long,(string Key,Node3D Node)> _stocks=[];
    public int StaticBuildCount {get;private set;}
    public int StockBuildCount {get;private set;}
    private bool _frozen = true;
    private bool _follow;
    private readonly System.Collections.Generic.Dictionary<long, Node3D> _parked = new();

    public override void _Ready()
    {
        _camera = new Camera3D { Current = true, Fov = 46 };
        AddChild(_camera);
        AddChild(new WorldEnvironment { Environment = new Godot.Environment
        { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color("96aeb2") } });
        _selection = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.96f, 0.025f, 0.96f) }, Visible = false };
        _selection.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("f2cd6a"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        AddChild(_selection);
    }

    public override void _Process(double delta)
    {
        if (_frozen) return;
        foreach (var mover in _actors.Values.Concat(_trips.Values))
        {
            mover.Elapsed = Mathf.Min(mover.Duration, mover.Elapsed + (float)delta);
            mover.Node.Position = mover.From.Lerp(mover.To, Mathf.Clamp(mover.Elapsed / mover.Duration, 0, 1));
        }
        FollowHuman();
    }

    public void Show(PresentationFrame frame, bool frozen, long selectedBuilding, string you, bool followHuman)
    {
        bool snap = frozen || _frozen || _follow != followHuman;
        _frozen = frozen;
        _follow = followHuman;
        string staticKey = frame.MapWidth + "x" + frame.MapHeight + "|" + string.Join(",", frame.Markers.Select(m =>
            $"{m.Kind}:{m.X}:{m.Z}:{(m.Kind == "capital" ? "" : m.Label)}:{(m.Kind == "capital" ? 0 : m.EntityId)}:{m.OwnerId}:{m.Level}:{string.Join(";",m.Footprint)}:{string.Join(";",m.Walls)}"));
        if (_staticKey != staticKey)
        {
            _staticKey = staticKey;
            RebuildStatic(frame);
        }
        ShowWarehouseStock(frame);
        var liveActors = new System.Collections.Generic.HashSet<long>();
        foreach (var actor in frame.Actors)
        {
            liveActors.Add(actor.Id);
            var at = new Vector3(actor.X + (actor.OffsetX - 500) / 1000f, 0.07f, actor.Z + (actor.OffsetZ - 500) / 1000f);
            if (!_actors.TryGetValue(actor.Id, out var mover))
            {
                var body = WorldModels.Person(actor.Human ? new Color("eef0dc") : actor.Id switch
                { 2 => new Color("d87667"), 3 => new Color("6699cf"), _ => new Color("e7ae58") }, actor.Human ? you : actor.Id.ToString());
                body.Position = at;
                AddChild(body);
                mover = new Mover(body, at) { Human = actor.Human };
                _actors[actor.Id] = mover;
            }
            Target(mover, at, actor.Human ? 0.1f : 1, snap, !actor.Human);
            int labelRow = frame.Actors.Count(other => other.X == actor.X && other.Z == actor.Z && other.Id < actor.Id);
            foreach (var label in mover.Node.GetChildren().OfType<Label3D>())
            {
                label.Position = new Vector3(0, 1.01f + labelRow * 0.23f, 0);
                if (actor.Human) label.Text = you;
            }
            if (actor.FacingX != 0 || actor.FacingZ != 0)
                mover.Node.Rotation = new Vector3(0, Mathf.Atan2(-actor.FacingX, -actor.FacingZ), 0);
        }
        foreach (long id in _actors.Keys.Where(id => !liveActors.Contains(id)).ToArray())
        { _actors[id].Node.QueueFree(); _actors.Remove(id);_speechLabels.Remove(id); }
        var live = new System.Collections.Generic.HashSet<long>();
        foreach (var trip in frame.Trips)
        {
            live.Add(trip.LogisticsId);
            // Draw beside the loading door so a cart on the building's cell is not hidden by its roof.
            var at = new Vector3(trip.X + 0.28f, 0.08f, trip.Z + 0.42f);
            if (!_trips.TryGetValue(trip.LogisticsId, out var mover))
            {
                var body = new Node3D();
                body.Position = at;
                AddChild(body);
                body.AddChild(WorldModels.Truck(trip.Loaded));
                mover = new Mover(body, at) { Loaded = trip.Loaded };
                _trips[trip.LogisticsId] = mover;
            }
            if (mover.Loaded != trip.Loaded)
            {
                foreach (Node child in mover.Node.GetChildren()) child.QueueFree();
                mover.Node.AddChild(WorldModels.Truck(trip.Loaded));
                mover.Loaded = trip.Loaded;
            }
            var heading = at - mover.To;
            if (heading.LengthSquared() > 0.001f) mover.Node.Rotation = new Vector3(0, Mathf.Atan2(-heading.X, -heading.Z), 0);
            Target(mover, at, 1, snap, true);
        }
        foreach (long id in _trips.Keys.Where(id => !live.Contains(id)).ToArray())
        { _trips[id].Node.QueueFree(); _trips.Remove(id); }
        foreach (var parked in _parked) parked.Value.Visible = !live.Contains(parked.Key);
        var selected = frame.Markers.FirstOrDefault(m => m.Kind == "production" && m.EntityId == selectedBuilding);
        _selection.Visible = selected is not null;
        if (selected is not null) _selection.Position = new Vector3(selected.X, 0.075f, selected.Z);
        if (_follow) FollowHuman();
        else
        {
            float span = Mathf.Max(frame.MapWidth, frame.MapHeight);
            var center = new Vector3(frame.MapWidth / 2f - 0.5f, 0, frame.MapHeight / 2f - 0.5f);
            _camera.Position = center + new Vector3(0, span * 0.8f, span * 0.7f);
            _camera.LookAt(center, Vector3.Up);
        }
    }

    public void ShowSpeech(IReadOnlyList<SpeechCue> cues,string language,bool visible)
    {
        foreach(var pair in _speechLabels)pair.Value.Visible=false;
        if(!visible)return;
        int row=0;
        foreach(var cue in cues)
        {
            if(!_actors.TryGetValue(cue.Actor,out var actor))continue;
            if(!_speechLabels.TryGetValue(cue.Actor,out var label))
            {label=new Label3D{FontSize=28,PixelSize=.006f,Position=new(0,1.38f,0),Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,OutlineSize=5,Modulate=new("fff4ce"),NoDepthTest=true};actor.Node.AddChild(label);_speechLabels[cue.Actor]=label;}
            label.Position=new(0,1.38f+.28f*row++,0);
            label.Text=PresentationEffects.Text(cue,language);label.Visible=true;
        }
    }

    private void ShowWarehouseStock(PresentationFrame frame)
    {
        foreach(var marker in frame.Markers.Where(m=>m.Warehouse is not null))
        {
            var piles=WarehouseVisuals.Layout(marker.Warehouse!);string key=string.Join(";",piles.Select(p=>$"{p.ItemId}:{p.Slot}:{p.Layers}"));
            if(_stocks.TryGetValue(marker.EntityId,out var old) && old.Key==key)continue;
            if(old.Node is not null){RemoveChild(old.Node);old.Node.QueueFree();}
            var node=WorldModels.WarehouseStock(marker,piles);node.Position=new(marker.X,.045f,marker.Z);AddChild(node);_stocks[marker.EntityId]=(key,node);StockBuildCount++;
        }
    }

    private static void Target(Mover mover, Vector3 at, float duration, bool snap, bool grid = false)
    {
        if(grid && !snap && !mover.To.IsEqualApprox(at))
        {
            var next=at-mover.To;
            if(Mathf.Abs(next.X)>.01f && Mathf.Abs(next.Z)>.01f || Mathf.Abs(next.X)+Mathf.Abs(next.Z)>1.01f)snap=true;
            else if(!mover.Node.Position.IsEqualApprox(mover.To))mover.Node.Position=mover.To;
        }
        if (snap)
        {
            mover.From = mover.To = mover.Node.Position = at;
            mover.Elapsed = mover.Duration;
            return;
        }
        if (mover.To.IsEqualApprox(at)) return;
        mover.From = mover.Node.Position;
        mover.To = at;
        mover.Elapsed = 0;
        mover.Duration = duration;
    }

    public void ResetMotion()
    {
        foreach (var mover in _actors.Values.Concat(_trips.Values))
        { mover.From = mover.To = mover.Node.Position; mover.Elapsed = mover.Duration; }
        _frozen = true;
    }

    private void FollowHuman()
    {
        if (!_follow) return;
        var human = _actors.Values.FirstOrDefault(m => m.Human);
        if (human is null) return;
        _camera.Rotation = new Vector3(Mathf.DegToRad(-55), 0, 0);
        _camera.Position = human.Node.Position + new Vector3(0, 4.2f, 2.94f);
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

    private void AddStatic(Node3D node)
    { AddChild(node); _staticNodes.Add(node); }

    private void RebuildStatic(PresentationFrame frame)
    {
        StaticBuildCount++;
        foreach(var stock in _stocks.Values)stock.Node.QueueFree();_stocks.Clear();
        foreach (var node in _staticNodes) node.QueueFree();
        _staticNodes.Clear();
        _parked.Clear();
        var ground = new Node3D();
        WorldModels.Part(ground, new BoxMesh { Size = new Vector3(frame.MapWidth + 1, 0.15f, frame.MapHeight + 1) },
            new Color("729365"), new Vector3(frame.MapWidth / 2f - 0.5f, -0.13f, frame.MapHeight / 2f - 0.5f));
        for (int x = 0; x < frame.MapWidth; x++) for (int z = 0; z < frame.MapHeight; z++)
        {
            float shade = ((x * 17 + z * 31) % 9) * 0.005f;
            WorldModels.Part(ground, new BoxMesh { Size = new Vector3(0.98f, 0.035f, 0.98f) },
                new Color(0.48f + shade, 0.62f + shade, 0.39f + shade), new Vector3(x, -0.005f, z));
        }
        AddStatic(ground);
        var yardCells=frame.Markers.Where(m=>m.Kind=="warehouse").SelectMany(m=>m.Walls.Select(w=>new Simulation.Core.Cell(m.X+w.X,m.Z+w.Z))).ToHashSet();
        foreach (var marker in frame.Markers)
        {
            if(marker.Kind=="blocked" && yardCells.Contains(new(marker.X,marker.Z)))continue;
            Node3D? node = null;
            if (marker.Kind == "equipment") node = WorldModels.Equipment(marker.Label,marker.Level);
            else if (marker.Kind == "truck-home") { node = WorldModels.Truck(false); _parked[marker.EntityId] = node; }
            else if (marker.Kind is "resource" or "resource-depleted") node = marker.Kind=="resource"?WorldModels.Resource(marker.Label):WorldModels.DepletedResource(marker.Label);
            else if(marker.Kind=="warehouse")node=WorldModels.Warehouse(marker);
            else if (marker.Kind is "production" or "shop" or "capital") node = WorldModels.Building(marker.Kind, marker.Label,marker.Level);
            else
            {
                var tile = new Node3D();
                Color color = marker.Kind switch
                {
                    "blocked" => new("596970"), "corridor" => new("78858a"), "shop-plot" => new("a294ba"),
                    "plot-own" => new("88b47a"), "plot-foreign" => new("be9285"), "plot-reserved" => new("dbbb70"),
                    _ => new("bfa17c")
                };
                WorldModels.Part(tile, new BoxMesh { Size = new Vector3(marker.Kind == "corridor" ? 1 : 0.91f, marker.Kind=="blocked"?0.8f:0.035f, marker.Kind == "corridor" ? 1 : 0.91f) }, color, new Vector3(0, 0.025f, 0));
                if (marker.Kind == "corridor")
                {
                    bool horizontal = frame.Markers.Any(m => m.Kind == "corridor" && m.Z == marker.Z && Math.Abs(m.X - marker.X) == 1);
                    for (int n = -1; n <= 1; n++)
                        WorldModels.Part(tile, new BoxMesh { Size = horizontal ? new Vector3(0.17f, 0.018f, 0.04f) : new Vector3(0.04f, 0.018f, 0.17f) },
                            new Color("e6ddba"), horizontal ? new Vector3(n * 0.29f, 0.055f, 0) : new Vector3(0, 0.055f, n * 0.29f));
                }
                if (marker.Kind.StartsWith("plot", System.StringComparison.Ordinal) || marker.Kind == "shop-plot")
                    foreach (float x in new[] { -0.39f, 0.39f }) foreach (float z in new[] { -0.39f, 0.39f })
                        WorldModels.Part(tile, new BoxMesh { Size = new Vector3(0.055f, 0.18f, 0.055f) }, new Color("e4d8b3"), new Vector3(x, 0.1f, z));
                node = tile;
            }
            if (node is not null) { node.Position = marker.Kind is "truck-home" or "equipment" ? new Vector3(marker.X + 0.28f, 0.08f, marker.Z + 0.42f) : new Vector3(marker.X, 0.045f, marker.Z); AddStatic(node); }
        }
    }
}
