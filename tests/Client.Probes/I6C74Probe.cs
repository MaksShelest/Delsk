using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Diagnostics;
using System.Reflection;

/// <summary>Походка и видимое лицо. Отсутствующие суставы — отказ, а не покачивание корпуса.
/// Linux software GL не заменяет физический Windows или Android.</summary>
public partial class I6C74Probe : Control
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private string _shots = "";
    private string _output = "";
    private readonly List<string> _checks = [];
    private static T Field<T>(object o, string key) => (T)o.GetType().GetField(key, Flags)!.GetValue(o)!;
    private static void Set(object o, string key, object? value) => o.GetType().GetField(key, Flags)!.SetValue(o, value);
    private static object? Call(object o, string key, params object?[] args) => o.GetType().GetMethod(key, Flags)!.Invoke(o, args);
    private void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); _checks.Add(text); GD.Print("I6C74 " + text); }
    private async Task Frames(int n = 1) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

    private object MoverOf(PresentationStage stage, long id)
    {
        var actors = stage.GetType().GetField("_actors", Flags)!.GetValue(stage)!;
        return actors.GetType().GetProperty("Item")!.GetValue(actors, new object[] { id })!;
    }
    private static Node3D NodeOf(object mover) => (Node3D)mover.GetType().GetField("Node", Flags)!.GetValue(mover)!;
    private static Node3D Joint(object mover, string key)
    {
        var joint = mover.GetType().GetField(key, Flags)!.GetValue(mover) as Node3D;
        if (joint is null) throw new InvalidOperationException("missing joint " + key);
        return joint;
    }
    private static float Swing(object mover, string joint, string rest)
    {
        var node = Joint(mover, joint);
        var basis = (Vector3)mover.GetType().GetField(rest, Flags)!.GetValue(mover)!;
        return node.Rotation.X - basis.X;
    }
    private static void RequireRig(Node3D node, string who, bool cozy)
    {
        foreach (var name in cozy ? new[] { "LeftLeg", "RightLeg", "LeftArm", "RightArm", "Nose" } : new[] { "LegL", "LegR", "ArmL", "ArmR", "Nose" })
            if (node.FindChild(name, true, false) is null) throw new InvalidOperationException(who + " has no " + name);
        if (cozy && node.FindChild("LegL", true, false) is not null) throw new InvalidOperationException(who + " cozy rig fell back to the classic names");
    }

    private async Task March(PresentationStage stage, object mover, Vector3 dir, string who, bool measureGait)
    {
        dir = dir.Normalized();
        var node = NodeOf(mover);
        Set(stage, "_frozen", false);
        Set(stage, "_follow", false);
        Call(stage, "TargetRotation", mover, dir.X, dir.Z, true);
        var origin = node.Position;
        Set(mover, "From", origin);
        Set(mover, "To", origin + dir * 6f);
        Set(mover, "Elapsed", 0f);
        Set(mover, "Duration", 4f);
        float stride = (float)mover.GetType().GetField("Stride", Flags)!.GetValue(mover)!;
        await Frames(2);
        var forward = -node.Basis.Z;
        float yaw = Mathf.Atan2(-forward.X, -forward.Z);
        float want = Mathf.Atan2(-dir.X, -dir.Z);
        Check(Mathf.Abs(Mathf.AngleDifference(yaw, want)) < 0.01f, who + " yaw stays on the step " + dir + " error " + Mathf.AngleDifference(yaw, want).ToString("0.000"));
        var nose = node.FindChild("Nose", true, false) as Node3D ?? throw new InvalidOperationException(who + " nose missing during the step");
        var face = nose.GlobalPosition - node.GlobalPosition;
        face.Y = 0;
        Check(face.Length() > 0.015f, who + " face marker is in front of the body " + face);
        Check(face.Normalized().Dot(forward.Normalized()) > 0.85f, who + " visible face follows the root " + face + " forward " + forward);
        Check(face.Normalized().Dot(dir) > 0.85f, who + " visible face follows the step " + dir + " face " + face);
        if (!measureGait) { await Frames(8); return; }
        float best = 0, bestL = 0, bestR = 0, bestAl = 0, bestAr = 0;
        for (int i = 0; i < 28; i++)
        {
            await Frames(1);
            float left = Swing(mover, "LegL", "LegLRest"), right = Swing(mover, "LegR", "LegRRest");
            float armL = Swing(mover, "ArmL", "ArmLRest"), armR = Swing(mover, "ArmR", "ArmRRest");
            float score = Math.Abs(left) + Math.Abs(right) + Math.Abs(armL) + Math.Abs(armR);
            if (left * right < 0 && armL * left < 0 && armR * right < 0 && score > best)
            { best = score; bestL = left; bestR = right; bestAl = armL; bestAr = armR; }
        }
        Check(best > 0.9f, who + " both legs and both arms leave rest in opposition " + bestL.ToString("0.00") + " " + bestR.ToString("0.00") + " " + bestAl.ToString("0.00") + " " + bestAr.ToString("0.00"));
        float after = (float)mover.GetType().GetField("Stride", Flags)!.GetValue(mover)!;
        Check(after > stride + 0.2f, who + " stride advances with the displayed step " + stride.ToString("0.00") + " -> " + after.ToString("0.00"));
    }

    private void Look(PresentationStage stage, Vector3 focus, Vector3 eye)
    {
        var camera = Field<Camera3D>(stage, "_camera");
        camera.Projection = Camera3D.ProjectionType.Perspective;
        camera.Fov = 32;
        camera.Position = eye;
        camera.LookAt(focus, Vector3.Up);
    }

    private async Task Video(PresentationStage stage, object[] movers)
    {
        string frames = _output + "/frames";
        Directory.CreateDirectory(frames);
        foreach (var file in Directory.GetFiles(frames)) File.Delete(file);
        Set(stage, "_frozen", false);
        Set(stage, "_follow", false);
        var nodes = movers.Select(NodeOf).ToArray();
        var shown = new HashSet<Node3D>(nodes);
        foreach (var child in stage.GetChildren())
            if (child is Node3D extra && extra.Name.ToString().StartsWith("Actor_") && !shown.Contains(extra)) extra.Visible = false;
        foreach (var node in nodes)
            foreach (var caption in node.FindChildren("Caption", "Label3D", true, false).OfType<Label3D>()) caption.Visible = false;
        var home = nodes[0].Position;
        for (int i = 0; i < movers.Length; i++) Set(movers[i], "Stride", 1.57f + i * 0.85f);
        int saved = 0;
        var heading = new Vector3(0, 0, -1);
        void Line(Vector3 spacing, bool walking)
        {
            for (int i = 0; i < nodes.Length; i++)
            {
                var at = home + spacing * (i - 1);
                nodes[i].Position = at;
                Set(movers[i], "From", at);
                Set(movers[i], "To", walking ? at + heading * 5f : at);
                Set(movers[i], "Elapsed", 0f);
                Set(movers[i], "Duration", walking ? 3.4f : 0.1f);
            }
        }
        async Task Capture(int count, Vector3 travel, Vector3 spacing, Vector3 eye, bool snapFacing)
        {
            if (travel != Vector3.Zero) heading = travel.Normalized();
            Line(spacing, travel != Vector3.Zero);
            foreach (var mover in movers) Call(stage, "TargetRotation", mover, heading.X, heading.Z, snapFacing);
            for (int frame = 0; frame < count; frame++)
            {
                var focus = Vector3.Zero;
                foreach (var node in nodes) focus += node.Position;
                focus = focus / nodes.Length + Vector3.Up * 0.15f;
                Look(stage, focus, focus + eye);
                if (frame % 2 == 0)
                {
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    Check(GetViewport().GetTexture().GetImage().SavePng(frames + "/frame_" + saved.ToString("000") + ".png") == Error.Ok, "frame " + saved);
                    saved++;
                }
                foreach (var mover in movers)
                {
                    if (travel == Vector3.Zero)
                    {
                        var at = NodeOf(mover).Position;
                        Set(mover, "From", at); Set(mover, "To", at); Set(mover, "Elapsed", 1f);
                        continue;
                    }
                    float elapsed = (float)mover.GetType().GetField("Elapsed", Flags)!.GetValue(mover)!;
                    float duration = (float)mover.GetType().GetField("Duration", Flags)!.GetValue(mover)!;
                    if (elapsed >= duration - 0.35f)
                    {
                        var node = NodeOf(mover);
                        Set(mover, "From", node.Position);
                        Set(mover, "To", node.Position + heading * 5f);
                        Set(mover, "Elapsed", 0f);
                        Set(mover, "Duration", 3.4f);
                    }
                }
                await Frames(1);
            }
        }
        var across = new Vector3(0.46f, 0, 0);
        var along = new Vector3(0, 0, 0.62f);
        await Capture(32, new Vector3(0, 0, -1), across, new Vector3(0.72f, 0.03f, -1.72f), true);
        await Capture(28, new Vector3(0, 0, -1), along, new Vector3(2.15f, 0.04f, 0), true);
        await Capture(28, new Vector3(0, 0, -1), across, new Vector3(-0.62f, 0.04f, 1.78f), true);
        await Capture(24, new Vector3(1, 0, -1), across, new Vector3(1.35f, 0.06f, -1.55f), false);
        await Capture(28, Vector3.Zero, across, new Vector3(1.2f, 0.04f, -1.4f), true);
        var clip = _shots + "/walk-cycle.mp4";
        var ffmpeg = new Process { StartInfo = new ProcessStartInfo("ffmpeg", "-y -loglevel error -framerate 12 -i " + frames + "/frame_%03d.png -c:v libx264 -pix_fmt yuv420p -movflags +faststart " + clip)
        { RedirectStandardError = true, UseShellExecute = false } };
        ffmpeg.Start();
        var logTask = ffmpeg.StandardError.ReadToEndAsync();
        await ffmpeg.WaitForExitAsync();
        var log = await logTask;
        Check(ffmpeg.ExitCode == 0 && new FileInfo(clip).Length > 20000, "walk video " + ffmpeg.ExitCode + " " + log[^Math.Min(400, log.Length)..]);
        File.Copy(frames + "/frame_008.png", _shots + "/walk-front.png", true);
        File.Copy(frames + "/frame_022.png", _shots + "/walk-side.png", true);
        File.Copy(frames + "/frame_036.png", _shots + "/walk-back.png", true);
        File.Copy(frames + "/frame_" + (saved - 2).ToString("000") + ".png", _shots + "/walk-stop.png", true);
    }

    private GameSession Boot(Playfield app)
    {
        var resolved = (OrdinaryGameResolution)Call(app, "ResolveNewGame", PlayMaps.CompactValley, 3)!;
        var funded = resolved.Scenario with { Participants = resolved.Scenario.Participants.Select(p => p.Id == 1 ? p with { CashMinor = 1000000 } : p).ToArray() };
        var session = new GameSession(new(funded));
        string save = typeof(Playfield).GetProperty("SavePath", Flags)!.GetValue(app)!.ToString()!;
        CheckpointStore.Save(session, save);
        Call(app, "Load"); Call(app, "ContinueGame");
        return Field<GameSession>(app, "_session");
    }

    public override async void _Ready()
    {
        try
        {
            MouseFilter = MouseFilterEnum.Ignore;
            var args = OS.GetCmdlineUserArgs();
            string root = args[Array.IndexOf(args, "--repository") + 1];
            _output = args[Array.IndexOf(args, "--output") + 1];
            _shots = root + "/docs/verification/i6c74";
            Directory.CreateDirectory(_shots); Directory.CreateDirectory(_output);
            GetWindow().Size = new(1100, 720); GetWindow().ContentScaleSize = GetWindow().Size;
            var app = GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();
            AddChild(app); await Frames(8); app.SetProcess(false);
            var session = Boot(app);
            await Frames(6); app.SetProcess(false); session.SetPaused(true); Call(app, "Refresh"); await Frames(2);
            var stage = Field<PresentationStage>(app, "_stage");
            Set(stage, "_follow", false);
            Field<Control>(app, "_playChrome").Visible = false;
            var player = MoverOf(stage, 1);
            var npc = MoverOf(stage, 2);
            var other = MoverOf(stage, 3);
            RequireRig(NodeOf(player), "player cozy", true);
            RequireRig(NodeOf(npc), "npc cozy", true);
            RequireRig(NodeOf(other), "second npc cozy", true);
            Check(NodeOf(player).FindChild("LeftLeg", true, false) is not null, "cozy walk uses the baked legs");
            await March(stage, player, new(0, 0, -1), "player north", true);
            float stride = (float)player.GetType().GetField("Stride", Flags)!.GetValue(player)!;
            Call(stage, "TargetRotation", player, 1f, -1f, false);
            Check(Mathf.IsEqualApprox((float)player.GetType().GetField("Stride", Flags)!.GetValue(player)!, stride), "a turn does not restart the stride");
            await March(stage, player, new(0, 0, 1), "player south", false);
            await March(stage, player, new(-1, 0, 0), "player west", false);
            await March(stage, player, new(1, 0, 0), "player east", false);
            await March(stage, player, new(1, 0, 1), "player diagonal", true);
            await March(stage, npc, new(0, 0, -1), "npc north", true);
            await March(stage, other, new(1, 0, -1), "second npc diagonal", true);
            var held = Joint(player, "LegL").Rotation.X;
            float heldStride = (float)player.GetType().GetField("Stride", Flags)!.GetValue(player)!;
            Set(stage, "_frozen", true);
            await Frames(12);
            Check(Mathf.Abs(Joint(player, "LegL").Rotation.X - held) < 0.002f && Mathf.IsEqualApprox((float)player.GetType().GetField("Stride", Flags)!.GetValue(player)!, heldStride), "pause holds the pose");
            Set(stage, "_frozen", false);
            var parked = NodeOf(player).Position;
            Set(player, "From", parked); Set(player, "To", parked); Set(player, "Elapsed", 1f);
            await Frames(45);
            Check(Math.Abs(Swing(player, "LegL", "LegLRest")) < 0.08f && Math.Abs(Swing(player, "LegR", "LegRRest")) < 0.08f
                && Math.Abs(Swing(player, "ArmL", "ArmLRest")) < 0.08f && Math.Abs(Swing(player, "ArmR", "ArmRRest")) < 0.08f, "a stop eases both arms and both legs back to rest");
            await Video(stage, new[] { player, npc, other });
            WorldSkins.Configure(new AppearancePreferences { PeopleSet = AppearancePreferences.Classic, WorldSet = AppearancePreferences.Cozy });
            session.SetPaused(true); Call(app, "Refresh"); await Frames(3);
            player = MoverOf(stage, 1); npc = MoverOf(stage, 2);
            RequireRig(NodeOf(player), "player classic", false);
            RequireRig(NodeOf(npc), "npc classic", false);
            await March(stage, player, new(0, 0, -1), "classic north", true);
            await March(stage, player, new(1, 0, 0), "classic east", false);
            await March(stage, npc, new(0, 0, 1), "classic npc south", true);
            WorldSkins.Configure(new AppearancePreferences());
            Call(app, "Refresh"); await Frames(3);
            player = MoverOf(stage, 1);
            RequireRig(NodeOf(player), "player cozy again", true);
            session.SetPaused(false);
            var far = session.Engine.Configuration().Layout!.InitialCityDirtCells.OrderByDescending(c => c.X + c.Z).First();
            string move = session.Send(new(1), new GameAction(ActionKind.MoveTo, Target: far));
            session.Step();
            var started = session.Engine.Result(move);
            int spins = 50;
            while (spins-- > 0 && started?.Status == ResultStatus.Accepted && !session.Engine.PedestrianMotionOf(1).Active && session.Engine.Observe(new(1)).Participant.Busy) session.Step();
            Check(started?.Status == ResultStatus.Accepted && session.Engine.PedestrianMotionOf(1).Active, "the live step starts " + started?.Status + " " + started?.ReasonCode);
            Call(app, "Refresh"); Set(stage, "_frozen", false); Set(stage, "_follow", false);
            await Frames(20);
            player = MoverOf(stage, 1);
            var live = Presentation.Read(session.Engine.Configuration(), session.Engine, 1).Actors.Single(a => a.Id == 1);
            var pose = PedestrianPoseMath.At(live.Motion, session.Engine.TravelQuantum / 10f);
            var wantDir = new Vector3(pose.FaceX, 0, pose.FaceZ);
            var noseNow = NodeOf(player).FindChild("Nose", true, false) as Node3D ?? throw new InvalidOperationException("live nose missing");
            var liveFace = noseNow.GlobalPosition - NodeOf(player).GlobalPosition; liveFace.Y = 0;
            Check(wantDir.LengthSquared() > 0.01f && liveFace.Normalized().Dot(wantDir.Normalized()) > 0.85f, "the live route shows the face along the step " + liveFace + " want " + wantDir);
            int settle = 8000;
            while (session.Engine.Observe(new(1)).Participant.Busy && settle-- > 0) session.Step();
            Check(settle > 0, "the live walk ends before the car");
            session.SetPaused(true); Call(app, "Refresh"); await Frames(2);
            var models = session.Engine.Configuration().Economy!.Fleet!.Models;
            var car = session.Engine.InspectFleet()!.Stock.First(v => models.Single(m => m.Id == v.ModelId).EquipmentTypeId is null);
            string walk = session.Send(new(1), new GameAction(ActionKind.MoveTo, Target: car.Position));
            session.SetPaused(false); session.Step();
            Check(session.Engine.Result(walk)?.Status == ResultStatus.Accepted, "walk to the car " + session.Engine.Result(walk)?.ReasonCode);
            int bound = 8000;
            while (session.Engine.Observe(new(1)).Participant.Busy && bound-- > 0) session.Step();
            Check(bound > 0, "reached the car");
            string buy = session.Send(new(1), new GameAction(ActionKind.BuyVehicle, VehicleId: car.Id));
            session.Step();
            Check(session.Engine.Result(buy)?.Status == ResultStatus.Accepted, "buy the car " + session.Engine.Result(buy)?.ReasonCode);
            for (int i = 0; i < 8; i++) session.Step();
            string board = session.Send(new(1), new GameAction(ActionKind.BoardVehicle, VehicleId: car.Id));
            session.Step();
            Check(session.Engine.Result(board)?.Status == ResultStatus.Accepted, "board " + session.Engine.Result(board)?.ReasonCode);
            session.SetPaused(true); Call(app, "Refresh"); await Frames(2);
            player = MoverOf(stage, 1);
            Check(!NodeOf(player).Visible, "boarding hides the walker");
            Check(Math.Abs(Swing(player, "LegL", "LegLRest")) < 0.08f && Math.Abs(Swing(player, "ArmL", "ArmLRest")) < 0.08f, "boarding returns the limbs to rest");
            session.SetPaused(false);
            string leave = session.Send(new(1), new GameAction(ActionKind.LeaveVehicle, VehicleId: car.Id));
            session.Step();
            Check(session.Engine.Result(leave)?.Status == ResultStatus.Accepted, "leave " + session.Engine.Result(leave)?.ReasonCode);
            session.SetPaused(true); Call(app, "Refresh"); await Frames(2);
            player = MoverOf(stage, 1);
            Check(NodeOf(player).Visible && NodeOf(player).FindChild("LeftLeg", true, false) is not null, "leaving the car restores the cozy walker");
            await March(stage, player, new(0, 0, -1), "after leaving the car", true);
            session.Engine.ValidateInvariants();
            await File.WriteAllTextAsync(_output + "/native.json", System.Text.Json.JsonSerializer.Serialize(new { status = "succeeded", checks = _checks }));
            GetTree().Quit();
        }
        catch (Exception e)
        {
            GD.PushError(e.ToString());
            try { await File.WriteAllTextAsync(_output + "/native.json", System.Text.Json.JsonSerializer.Serialize(new { status = "failed", error = e.ToString(), checks = _checks })); }
            catch (Exception write) { GD.PushError(write.ToString()); }
            GetTree().Quit(1);
        }
    }
}
