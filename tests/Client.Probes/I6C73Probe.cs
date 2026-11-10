using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;

/// <summary>Один native smoke визуальных правок I6-C7.3. Кадры снимает Godot.
/// Linux software GL не заменяет физический Windows.</summary>
public partial class I6C73Probe : Control
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private string _shots = "";
    private string _output = "";
    private readonly List<string> _checks = [];
    private static T Field<T>(object o, string key) => (T)o.GetType().GetField(key, Flags)!.GetValue(o)!;
    private static void Set(object o, string key, object? value) => o.GetType().GetField(key, Flags)!.SetValue(o, value);
    private static object? Call(object o, string key, params object?[] args) => o.GetType().GetMethod(key, Flags)!.Invoke(o, args);
    private static IEnumerable<Node> All(Node n) { yield return n; foreach (Node c in n.GetChildren()) foreach (var x in All(c)) yield return x; }
    private void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); _checks.Add(text); GD.Print("I6C73 " + text); }
    private async Task Frames(int n = 3) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Shot(string name)
    {
        await Frames(); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Check(GetViewport().GetTexture().GetImage().SavePng(_shots + "/" + name + ".png") == Error.Ok, "capture " + name);
    }
    private void Camera(Playfield app, Vector3 center, float size, bool chrome)
    {
        var stage = Field<PresentationStage>(app, "_stage"); Set(stage, "_follow", false);
        var camera = Field<Camera3D>(stage, "_camera"); camera.Projection = Camera3D.ProjectionType.Orthogonal;
        camera.Size = size; camera.Position = center + new Vector3(0, 35, 20); camera.LookAt(center, Vector3.Up);
        Field<Control>(app, "_playChrome").Visible = chrome;
    }
    private async Task Resize(int width, int height)
    {
        GetWindow().Size = new(width, height); GetWindow().ContentScaleSize = GetWindow().Size; await Frames(3);
    }
    private GameSession Boot(Playfield app, string map)
    {
        var resolved = (OrdinaryGameResolution)Call(app, "ResolveNewGame", map, 3)!;
        var funded = resolved.Scenario with { Participants = resolved.Scenario.Participants.Select(p => p.Id == 1 ? p with { CashMinor = 1000000 } : p).ToArray() };
        var session = new GameSession(new(funded));
        string save = typeof(Playfield).GetProperty("SavePath", Flags)!.GetValue(app)!.ToString()!;
        CheckpointStore.Save(session, save);
        Call(app, "Load"); Call(app, "ContinueGame");
        return Field<GameSession>(app, "_session");
    }
    private void Idle(GameSession session, GameAction action, long actor = 1)
    {
        string id = session.Send(new(actor), action); session.Step();
        Check(session.Engine.Result(id)?.Status == ResultStatus.Accepted, action.Kind + " " + actor + " " + session.Engine.Result(id)?.ReasonCode);
        int bound = 8000;
        while (session.Engine.Observe(new(actor)).Participant.Busy && bound-- > 0) session.Step();
        Check(bound > 0, "arrived " + action.Kind + " " + actor);
    }
    private static bool Overlap(Control a, Control b)
    {
        var left = a.GetGlobalRect().Grow(-1); var right = b.GetGlobalRect().Grow(-1);
        return left.HasArea() && right.HasArea() && left.Intersects(right);
    }

    public override async void _Ready()
    {
        try
        {
            MouseFilter = MouseFilterEnum.Ignore;
            var args = OS.GetCmdlineUserArgs();
            string root = args[Array.IndexOf(args, "--repository") + 1];
            _output = args[Array.IndexOf(args, "--output") + 1];
            _shots = root + "/docs/verification/i6c73";
            Directory.CreateDirectory(_shots); Directory.CreateDirectory(_output);
            await Resize(1100, 720);
            var app = GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();
            AddChild(app); await Frames(8); app.SetProcess(false);
            var session = Boot(app, PlayMaps.CompactValley);
            await Frames(6); app.SetProcess(false); session.SetPaused(true); Call(app, "Refresh");
            var dealer = session.Engine.Configuration().Layout!.CivicObjects.Single(c => c.Kind == "dealership");
            var foot = dealer.Footprint.ToHashSet();
            var models = session.Engine.Configuration().Economy!.Fleet!.Models;
            var machines = session.Engine.InspectFleet()!.Stock.Where(v => models.Single(m => m.Id == v.ModelId).EquipmentTypeId is not null).ToArray();
            Check(machines.Length == 2 && machines.Select(v => v.Position).Distinct().Count() == 2, "valley stocks two extractors");
            Check(machines.All(v => foot.Any(c => Math.Abs(c.X - v.Position.X) + Math.Abs(c.Z - v.Position.Z) == 1)), "valley extractors touch the dealership");
            Camera(app, new(26, 0, 7), 16, false); await Shot("after-dealer-valley");
            Camera(app, new(8, 0, 12), 18, false); await Shot("after-grass");
            var actor = session.Engine.Observe(new(1)).Participant;
            var lot = session.Engine.Configuration().Layout!.IndustrialLots.OrderBy(l => l.Cells.Min(c => Math.Abs(c.X - actor.Position.X) + Math.Abs(c.Z - actor.Position.Z))).First();
            var stand = lot.Cells.OrderBy(c => Math.Abs(c.X - actor.Position.X) + Math.Abs(c.Z - actor.Position.Z)).First();
            Idle(session, new(ActionKind.MoveTo, Target: stand));
            session.SetPaused(true); Camera(app, new(stand.X, 0, stand.Z), 12, true); Call(app, "Refresh"); await Frames(4);
            var buy = All(app).OfType<Button>().First(b => b.Name.ToString().StartsWith("Context_", StringComparison.Ordinal) && b.Text.Contains("Купить"));
            buy.GrabFocus();
            await Frames(3);
            var preview = Field<Cell[]>(Field<PresentationStage>(app, "_stage"), "_preview");
            Check(preview.Length == lot.Cells.Length && lot.Cells.All(preview.Contains), "buy highlights the whole lot " + buy.Name + " " + buy.Text + " [" + string.Join(" ", preview.Select(c => c.X + "," + c.Z)) + "]");
            var road = session.Engine.Configuration().Layout!.InitialCityDirtCells.First(c => !lot.Cells.Contains(c));
            Set(app, "_hoverX", road.X); Set(app, "_hoverZ", road.Z); Call(app, "Refresh"); await Frames(2);
            preview = Field<Cell[]>(Field<PresentationStage>(app, "_stage"), "_preview");
            Check(preview.Length == lot.Cells.Length && lot.Cells.All(preview.Contains), "a road under the cursor does not replace the lot");
            await Shot("after-buy-plot");
            buy.ReleaseFocus();
            await Frames(2);
            preview = Field<Cell[]>(Field<PresentationStage>(app, "_stage"), "_preview");
            Check(preview.Length == 0, "the highlight leaves with the button");
            var title = All(app).OfType<Label>().First(l => l.Name == "StarterGoalTitle").Text;
            Check(!title.Contains("7/3", StringComparison.Ordinal), "the goal is not 7/3: " + title);
            if (title.Contains('—'))
            {
                var fraction = title.Split('—')[^1].Trim();
                var parts = fraction.Split('/');
                if (parts.Length == 2 && long.TryParse(parts[0], out var have) && long.TryParse(parts[1], out var need))
                    Check(have <= need && need > 0, "goal fraction stays inside the task " + title);
            }
            Set(app, "_hoverX", stand.X); Set(app, "_hoverZ", stand.Z); Call(app, "Refresh"); await Frames(2);
            var goal = All(app).OfType<Control>().First(c => c.Name == "StarterGoal");
            var tip = Field<Label>(app, "_tooltip");
            Check(tip.Visible && !Overlap(goal, tip), "goal and tooltip stay apart at 1100x720 " + goal.GetGlobalRect() + " " + tip.GetGlobalRect() + " " + tip.Text);
            await Shot("after-goal");
            var more = All(app).OfType<Button>().First(b => b.Name == "StarterGoalMore");
            more.EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
            Check(!Overlap(goal, tip), "opening the goal detail does not cover the tooltip");
            foreach (var (w, h) in new[] { (800, 480), (640, 360), (960, 440), (360, 640) })
            {
                await Resize(w, h); Call(app, "Refresh"); await Frames(2);
                Check(goal.GetGlobalRect().HasArea() && tip.Visible && !Overlap(goal, tip), "goal and tooltip stay apart at " + w + "x" + h);
            }
            await Shot("after-narrow");
            await Resize(1100, 720); Call(app, "Refresh"); await Frames(2);
            var where = All(app).OfType<Button>().First(b => b.Name == "StarterGoalWhere");
            where.EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
            var route = Field<MeshInstance3D?>(Field<PresentationStage>(app, "_stage"), "_route");
            Check(route is not null, "the goal draws a path on the ground");
            Camera(app, new(actor.Position.X, 0, actor.Position.Z), 22, true); await Shot("after-route");
            where.EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
            Check(Field<MeshInstance3D?>(Field<PresentationStage>(app, "_stage"), "_route") is null, "a second press clears the path");
            Call(app, "OpenCityMap"); await Frames(4); await Shot("after-city-map");
            Call(app, "ChangeLanguage"); Call(app, "Refresh"); await Frames(3); await Shot("after-city-map-en");
            Call(app, "CloseTablet"); await Frames(2);
            var far = session.Engine.Configuration().Layout!.InitialCityDirtCells.OrderByDescending(c => Math.Abs(c.X - stand.X) + Math.Abs(c.Z - stand.Z)).First();
            var other = session.Engine.Observe(new(2)).Participant.Position;
            session.SetPaused(false);
            string move = session.Send(new(1), new(ActionKind.MoveTo, Target: far)); session.Step();
            var started = session.Engine.Result(move);
            int spins = 40;
            while (spins-- > 0 && started?.Status == ResultStatus.Accepted && !session.Engine.PedestrianMotionOf(1).Active && session.Engine.Observe(new(1)).Participant.Busy) session.Step();
            Check(started?.Status == ResultStatus.Accepted && session.Engine.PedestrianMotionOf(1).Active, "player starts walking " + started?.Status + " " + started?.ReasonCode);
            session.Send(new(2), new(ActionKind.MoveTo, Target: far == other ? stand : far));
            spins = 40;
            while (spins-- > 0 && !session.Engine.PedestrianMotionOf(2).Active) session.Step();
            session.SetPaused(true); Call(app, "Refresh"); await Frames(30);
            void Face(long id, string who)
            {
                var person = Presentation.Read(session.Engine.Configuration(), session.Engine, 1).Actors.Single(a => a.Id == id);
                if (!person.Motion.Active) return;
                float phase = session.Engine.TravelQuantum / 10f;
                var pose = PedestrianPoseMath.At(person.Motion, phase);
                var node = Field<PresentationStage>(app, "_stage").GetNodeOrNull<Node3D>("Actor_" + id);
                if (node is null) return;
                var forward = -node.Basis.Z;
                var want = new Vector3(pose.FaceX, 0, pose.FaceZ);
                float dot = forward.LengthSquared() < 0.01f || want.LengthSquared() < 0.01f ? -1f : forward.Normalized().Dot(want.Normalized());
                Check(dot > 0.85f, who + " faces the step dot " + dot.ToString("0.00") + " forward " + forward + " want " + want);
                Check(dot > 0.2f, who + " is not turned backwards");
            }
            Face(1, "player"); Face(2, "npc");
            Check(_checks.Any(c => c.StartsWith("player faces", StringComparison.Ordinal)), "player facing was measured");
            Camera(app, new(stand.X, 0, stand.Z), 10, true); await Shot("after-people");
            session = Boot(app, PlayMaps.Second);
            await Frames(4); app.SetProcess(false); session.SetPaused(true); Call(app, "Refresh");
            var woodDealer = session.Engine.Configuration().Layout!.CivicObjects.Single(c => c.Kind == "dealership");
            var woodFoot = woodDealer.Footprint.ToHashSet();
            var woodModels = session.Engine.Configuration().Economy!.Fleet!.Models;
            var woodMachines = session.Engine.InspectFleet()!.Stock.Where(v => woodModels.Single(m => m.Id == v.ModelId).EquipmentTypeId is not null).ToArray();
            Check(woodMachines.Length == 2 && woodMachines.All(v => woodFoot.Any(c => Math.Abs(c.X - v.Position.X) + Math.Abs(c.Z - v.Position.Z) == 1)), "woodland extractors touch the dealership");
            Camera(app, new(15, 0, 10), 16, false); await Shot("after-dealer-woodland");
            var resource = session.Engine.Configuration().ResourceNodes.First(n => !n.Rules!.Public && n.Position.X > 0);
            Idle(session, new(ActionKind.MoveTo, Target: resource.Position));
            session.SetPaused(true); Call(app, "Refresh"); await Frames(3);
            var buyResource = All(app).OfType<Button>().First(b => b.Name.ToString().StartsWith("Context_", StringComparison.Ordinal) && (b.Text.Contains("ресурс") || b.Text.Contains("resource")));
            buyResource.GrabFocus();
            await Frames(3);
            var resourcePreview = Field<Cell[]>(Field<PresentationStage>(app, "_stage"), "_preview");
            Check(resourcePreview.Length == 1 && resourcePreview[0] == resource.Position, "buy resource highlights that cell");
            Camera(app, new(resource.Position.X, 0, resource.Position.Z), 10, true); await Shot("after-buy-resource");
            session.Engine.ValidateInvariants();
            File.WriteAllText(_output + "/native.json", System.Text.Json.JsonSerializer.Serialize(new { status = "succeeded", checks = _checks }));
            GetTree().Quit();
        }
        catch (Exception e)
        {
            GD.PushError(e.ToString());
            try { File.WriteAllText(_output + "/native.json", System.Text.Json.JsonSerializer.Serialize(new { status = "failed", error = e.ToString() })); }
            catch (Exception write) { GD.PushError(write.ToString()); }
            GetTree().Quit(1);
        }
    }
}
