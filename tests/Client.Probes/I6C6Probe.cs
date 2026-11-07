using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

/// <summary>Изолированный native smoke живого мира. В обычный клиент не входит.
/// Кадры снимает настоящий Godot. Linux software GL не заменяет Windows или Android.</summary>
public partial class I6C6Probe : Control
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private Playfield _app = null!;
    private GameSession _session = null!;
    private string _shots = "";
    private string _output = "";
    private readonly List<string> _checks = [];
    private static T Field<T>(object o, string key) => (T)o.GetType().GetField(key, Flags)!.GetValue(o)!;
    private static object? Call(object o, string key, params object?[] args) => o.GetType().GetMethod(key, Flags)!.Invoke(o, args);
    private static IEnumerable<Node> All(Node root) { yield return root; foreach (var c in root.GetChildren()) foreach (var n in All(c)) yield return n; }
    private T Named<T>(string name) where T : Node => All(_app).OfType<T>().First(n => n.Name == name && (n is not Control c || c.IsVisibleInTree()));
    private PlayerView ViewOf(long actor) => PlayerDesk.Read(_session.Engine, new(actor));
    private PlayerView View => ViewOf(1);
    private void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); if (_checks.Count == 0 || _checks[^1] != text) { _checks.Add(text); GD.Print("I6C6 " + text); } }
    private async Task Frames(int n = 3) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Shot(string name)
    {
        await Frames();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Check(GetViewport().GetTexture().GetImage().SavePng(_shots + "/" + name + ".png") == Error.Ok, "capture " + name);
    }
    private void Refresh() => Call(_app, "Refresh");
    private async Task Press(Control control)
    {
        UiNavigation.Reveal(control); await Frames();
        Check(control is not BaseButton button || !button.Disabled, "enabled " + control.Name);
        var p = control.GetGlobalRect().GetCenter(); int w = GetWindow().GetWindowId();
        Input.ParseInputEvent(new InputEventMouseMotion { WindowId = w, Position = p, GlobalPosition = p });
        Input.ParseInputEvent(new InputEventMouseButton { WindowId = w, Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = true }); await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton { WindowId = w, Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = false }); await Frames();
    }
    private void StepWorld()
    {
        if (_session.Engine.WaitingForBallots && View.Elections.NextElectionId is { } election)
            foreach (var person in _session.Engine.Configuration().Participants)
                _session.Send(new(person.Id), new(ActionKind.SetVoteChoice, Election: election, Candidate: new(1)));
        _session.Step();
    }
    private void Command(GameAction action, long actor = 1)
    {
        _session.SetPaused(false); string id = _session.Send(new(actor), action); StepWorld();
        var result = _session.Engine.Result(id);
        Check(result?.Status == ResultStatus.Accepted, "Core " + actor + " " + action.Kind + " " + result?.ReasonCode);
        int limit = 900; while (ViewOf(actor).Participant.Busy && limit-- > 0) StepWorld();
        Check(limit > 0, "bounded real movement/work"); _session.SetPaused(true); Refresh();
    }

    public override async void _Ready()
    {
        try
        {
            MouseFilter = MouseFilterEnum.Ignore;
            var args = OS.GetCmdlineUserArgs();
            string root = args[Array.IndexOf(args, "--repository") + 1];
            _output = args[Array.IndexOf(args, "--output") + 1];
            _shots = root + "/docs/verification/i6c6";
            Directory.CreateDirectory(_shots); Directory.CreateDirectory(_output);
            GetWindow().Size = new(1100, 720); GetWindow().ContentScaleSize = GetWindow().Size;
            _app = GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();
            AddChild(_app); await Frames(8); _app.SetProcess(false);
            Call(_app, "ShowMaps"); await Frames();
            await Press(Named<Button>("ChooseMap")); await Press(Named<Button>("SelectMap_compact-valley"));
            await Press(Named<Button>("StartGame")); await Frames(6); _app.SetProcess(false);
            _session = Field<GameSession>(_app, "_session"); _session.SetPaused(true); Refresh();
            var stage = Field<PresentationStage>(_app, "_stage");
            long npc = _session.Engine.Configuration().Participants.Select(p => p.Id).First(id => id != 1);
            int guard = 400; while (ViewOf(npc).Participant.Busy && guard-- > 0) { _session.SetPaused(false); StepWorld(); }
            Check(guard > 0, "NPC is idle before the route"); _session.SetPaused(true);
            await PlaceGoods(stage);
            await Ownership(stage);
            await Walk(stage, npc);
            await File.WriteAllTextAsync(_output + "/native.json", JsonSerializer.Serialize(new
            {
                status = "succeeded", checks = _checks,
                scope = "Linux software GL, synthetic input, one ordinary Small valley. Not a physical Windows/Android or FPS measurement."
            }, new JsonSerializerOptions { WriteIndented = true }));
            _app.QueueFree(); await Frames(); GetTree().Quit();
        }
        catch (Exception e)
        {
            try { await Shot("failure"); } catch { }
            await File.WriteAllTextAsync(_output + "/failure.txt", e.ToString());
            GD.PrintErr(e); GetTree().Quit(1);
        }
    }

    private async Task PlaceGoods(PresentationStage stage)
    {
        ResourceSiteView Site(string item) => _session.Engine.ResourceSites()
            .Where(s => s.ItemId == item && s.Owner is null && s.Tenant is null)
            .OrderBy(s => s.Public ? 0 : s.LeasePriceMinor).First();
        var wood = Site("wood");
        var grain = Site("grain");
        long access = (wood.Public ? 0 : wood.LeasePriceMinor) + (grain.Public ? 0 : grain.LeasePriceMinor);
        var owned = View.Plots.FirstOrDefault(p => p.OwnerId == View.Participant.Id && p.BuildingId is null && !p.IsCorridor && !p.IsShopSite);
        if (owned is null)
        {
            var free = View.Plots.Where(p => p.OwnerId is null && p.ReservedBy is null && !p.IsCorridor && !p.IsShopSite && p.ExpansionFor is null)
                .OrderBy(p => p.PriceMinor).First();
            Check(View.AvailableCashMinor >= free.PriceMinor + access, "cash buys one empty plot and leases the two resources");
            Command(new(ActionKind.MoveTo, Target: free.Position));
            Command(PlayerDesk.BuyPlot(free.Id.Value));
            owned = View.Plots.Single(p => p.Id == free.Id);
        }
        else Check(View.AvailableCashMinor >= access, "cash leases the two resources");
        void Gather(ResourceSiteView site)
        {
            Command(new(ActionKind.MoveTo, Target: site.Position));
            if (!site.Public) Command(new(ActionKind.LeaseResource, Target: site.Position, ItemId: site.ItemId));
            Command(new(ActionKind.Harvest, ItemId: site.ItemId, Quantity: 2));
        }
        Gather(wood);
        Gather(grain);
        Command(new(ActionKind.MoveTo, Target: owned.Position));
        var pile = new InventoryId(_session.Engine.StorageOf(new(1)).Ground.Single(g => g.PlotId == owned.Id.Value).InventoryId);
        Command(PlayerDesk.Transfer(View.Participant.InventoryId.Value, pile.Value, "wood", 2));
        Command(PlayerDesk.Transfer(View.Participant.InventoryId.Value, pile.Value, "grain", 2));
        var marker = Presentation.Read(_session.Engine.Configuration(), _session.Engine, 1).Markers.Single(m => m.Kind == "ground" && m.EntityId == owned.Id.Value);
        Check(marker.Warehouse!.Goods.Any(g => g.ItemId == "wood") && marker.Warehouse.Goods.Any(g => g.ItemId == "grain"), "own ground shows wood and grain");
        Call(_app, "OnCell", owned.Position.X, owned.Position.Z); Refresh(); await Frames(2);
        stage.AimAt(new(owned.Position.X, .4f, owned.Position.Z), 3.6f);
        await Shot("04-ground-goods");
        Check(Named<Button>("OpenStorageCard").Visible, "own cell offers the storage card");
        var foreign = Presentation.Read(_session.Engine.Configuration(), _session.Engine, 2).Markers.Where(m => m.Kind == "ground" && m.OwnerId == 1);
        Check(!foreign.Any(), "another participant does not receive the pile");
    }

    private async Task Ownership(PresentationStage stage)
    {
        var mine = View.Plots.First(p => p.OwnerId == View.Participant.Id && p.BuildingId is null && !p.IsCorridor);
        Call(_app, "OnCell", mine.Position.X, mine.Position.Z); Refresh();
        stage.AimAt(new(mine.Position.X, .3f, mine.Position.Z), 6.5f);
        await Shot("05-own-plot");
        await Press(Named<Button>("HudPlots"));
        Check(Field<bool>(_app, "_showOwnership"), "ownership layer is on");
        stage.AimAt(new(mine.Position.X + 1.2f, .2f, mine.Position.Z + 1.2f), 9f);
        await Shot("06-ownership-layer");
        await Shot("07-adjacent-plots");
    }

    private async Task Walk(PresentationStage stage, long npc)
    {
        var people = _session.Engine.Configuration().Participants.Select(p => p.Id).ToArray();
        var cluster = people.Select(id => ViewOf(id).Participant.Position).ToArray();
        float cx = (float)cluster.Average(c => c.X), cz = (float)cluster.Average(c => c.Z);
        stage.AimAt(new(cx, .4f, cz), 8f);
        await Shot("03-several-characters");
        long walker = npc;
        MovementGuide? route = null;
        string rejection = "";
        foreach (var candidate in people.Where(id => id != 1))
        {
            int guard = 700;
            while (ViewOf(candidate).Participant.Busy && guard-- > 0) { _session.SetPaused(false); StepWorld(); }
            if (ViewOf(candidate).Participant.Busy) { rejection = "busy"; continue; }
            var origin = ViewOf(candidate).Participant.Position;
            int bestScore = int.MaxValue;
            MovementGuide? found = null;
            for (int dx = 2; dx <= 8; dx++)
                for (int dz = 1; dz <= 8; dz++)
                    foreach (var sign in new (int X, int Z)[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
                    {
                        int tx = origin.X + dx * sign.X, tz = origin.Z + dz * sign.Z;
                        if (tx < 0 || tz < 0) continue;
                        var guide = _session.Engine.PreviewMovement(new(candidate), new(tx, tz));
                        if (!guide.Allowed) { rejection = guide.Reason + " " + tx + "," + tz; continue; }
                        if (guide.Path.Length is < 4 or > 8) continue;
                        int turn = 0;
                        for (int i = 2; i < guide.Path.Length; i++)
                        {
                            int ax = Math.Sign(guide.Path[i - 1].X - guide.Path[i - 2].X), az = Math.Sign(guide.Path[i - 1].Z - guide.Path[i - 2].Z);
                            int bx = Math.Sign(guide.Path[i].X - guide.Path[i - 1].X), bz = Math.Sign(guide.Path[i].Z - guide.Path[i - 1].Z);
                            if (ax != bx || az != bz) { turn = i; break; }
                        }
                        int score = turn * 100 + guide.Path.Length;
                        if (turn is >= 2 and <= 6 && score < bestScore) { bestScore = score; found = guide; }
                    }
            if (found is not null) { walker = candidate; route = found; break; }
        }
        Check(route is not null, "a short turning pedestrian route exists (" + rejection + ")");
        npc = walker;
        _session.SetPaused(false);
        string sent = _session.Send(new(npc), new(ActionKind.MoveTo, Target: route!.Target));
        StepWorld();
        Check(_session.Engine.Result(sent)?.Status == ResultStatus.Accepted, "NPC MoveTo accepted");
        Refresh(); await Frames(4);
        var blocked = View.Blocked.ToHashSet();
        var samples = new List<(double Time, float X, float Z, string Frame)>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        double pauseStart = -1, pauseEnd = -1;
        _app.SetProcess(true); _session.SetPaused(false);
        double recordUntil = 18;
        bool sawStop = false;
        while (clock.Elapsed.TotalSeconds < recordUntil)
        {
            string actorName = "Actor_" + npc.ToString(CultureInfo.InvariantCulture);
            var body = stage.GetNode<Node3D>(actorName);
            stage.AimAt(body.Position + Vector3.Up * .3f, 4.2f);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            body = stage.GetNode<Node3D>(actorName);
            string frame = "";
            if (samples.Count % 10 == 0)
            {
                frame = "motion-" + (samples.Count / 10).ToString("00", CultureInfo.InvariantCulture);
                Check(GetViewport().GetTexture().GetImage().SavePng(_shots + "/" + frame + ".png") == Error.Ok, "capture " + frame);
            }
            samples.Add((clock.Elapsed.TotalSeconds, body.Position.X, body.Position.Z, frame));
            if (!sawStop && clock.Elapsed.TotalSeconds > 1.2 && !ViewOf(npc).Participant.Busy)
            {
                sawStop = true;
                recordUntil = Math.Min(recordUntil, clock.Elapsed.TotalSeconds + 0.7);
            }
            if (pauseStart < 0 && !sawStop && clock.Elapsed.TotalSeconds > 0.55)
            {
                pauseStart = clock.Elapsed.TotalSeconds;
                _app.Notification((int)NotificationApplicationFocusOut);
                await Frames(2);
                body = stage.GetNode<Node3D>(actorName);
                float x = body.Position.X, z = body.Position.Z;
                var until = clock.Elapsed.TotalSeconds + .45;
                while (clock.Elapsed.TotalSeconds < until)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                }
                body = stage.GetNode<Node3D>(actorName);
                Check(Mathf.Abs(body.Position.X - x) < .04f && Mathf.Abs(body.Position.Z - z) < .04f, "pause holds the pedestrian");
                pauseEnd = clock.Elapsed.TotalSeconds;
                _app.Notification((int)NotificationApplicationFocusIn);
                await Frames(1);
            }
        }
        _app.SetProcess(false); _session.SetPaused(true); Refresh();
        Check(pauseStart >= 0, "pause happened during the walk");
        Check(sawStop, "the NPC stops when the route ends");
        double travelled = 0; for (int i = 1; i < samples.Count; i++) travelled = Math.Max(travelled, Dist(samples[0], samples[i]));
        Check(travelled > 1.15, "the NPC crossed more than one cell");
        bool turned = false; bool stalled = false; int turnAt = -1;
        for (int i = 0; i < samples.Count; i++)
        {
            Check(samples[i].X >= -.2f && samples[i].Z >= -.2f, "visual pose stays inside the map");
            foreach (var cell in blocked)
                Check((samples[i].X - cell.X) * (samples[i].X - cell.X) + (samples[i].Z - cell.Z) * (samples[i].Z - cell.Z) > .16f, "visual pose does not enter a blocked cell");
            if (samples[i].Time < pauseEnd) continue;
            int near = i; while (near < samples.Count && samples[near].Time < samples[i].Time + .4) near++;
            if (near < samples.Count)
            {
                double window = Dist(samples[i], samples[near]);
                double along = Dist(samples[0], samples[near]);
                if (along > .35 && along < travelled * .9 && window < .04) stalled = true;
            }
            int j = i; while (j < samples.Count && samples[j].Time < samples[i].Time + 1.05) j++;
            if (j >= samples.Count) continue;
            int k = j; while (k < samples.Count && samples[k].Time < samples[j].Time + 1.05) k++;
            if (k >= samples.Count) continue;
            float ax = samples[j].X - samples[i].X, az = samples[j].Z - samples[i].Z;
            float bx = samples[k].X - samples[j].X, bz = samples[k].Z - samples[j].Z;
            float am = Mathf.Sqrt(ax * ax + az * az), bm = Mathf.Sqrt(bx * bx + bz * bz);
            if (am > .18f && bm > .18f && (ax * bx + az * bz) / (am * bm) < .6f) { turned = true; if (turnAt < 0) turnAt = j; }
        }
        await File.WriteAllTextAsync(_shots + "/motion.json", JsonSerializer.Serialize(samples.Select(s => new { t = Math.Round(s.Time, 3), x = Math.Round(s.X, 3), z = Math.Round(s.Z, 3) }), new JsonSerializerOptions { WriteIndented = true }));
        Check(!stalled, "the NPC does not stop between route cells");
        Check(turned, "the NPC turns during the recording");
        string NamedFrame(int index, string fallback)
        {
            for (int n = index; n >= 0; n--) if (samples[n].Frame.Length > 0) return samples[n].Frame;
            return fallback;
        }
        File.Copy(_shots + "/" + NamedFrame(samples.Count / 5, samples.First(s => s.Frame.Length > 0).Frame) + ".png", _shots + "/01-route.png", true);
        File.Copy(_shots + "/" + NamedFrame(turnAt < 0 ? samples.Count / 2 : turnAt, samples.First(s => s.Frame.Length > 0).Frame) + ".png", _shots + "/02-turn.png", true);
        File.Copy(_shots + "/" + NamedFrame(samples.Count - 1, samples.Last(s => s.Frame.Length > 0).Frame) + ".png", _shots + "/08-stop.png", true);
        var before = _session.Engine.PedestrianMotionOf(npc);
        string path = (string)_app.GetType().GetProperty("SavePath", Flags)!.GetValue(_app)!;
        CheckpointStore.Save(_session, path);
        Call(_app, "Load"); await Frames(8);
        _session = Field<GameSession>(_app, "_session");
        Check(_session.Engine.PedestrianMotionOf(npc).Equals(before), "save and load keep the pedestrian sample");
        Call(_app, "ContinueGame"); await Frames(4);
        stage = Field<PresentationStage>(_app, "_stage");
        Check(stage.GetNodeOrNull<Node3D>("Actor_" + npc.ToString(CultureInfo.InvariantCulture)) is not null, "the loaded world draws the NPC");
    }

    private static double Dist((double Time, float X, float Z, string Frame) a, (double Time, float X, float Z, string Frame) b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
}
