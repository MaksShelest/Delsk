using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Agents.Algorithmic;
using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class I9MotionProbe : Node
{
    private const double FrameSeconds = 1.0 / 30;
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private PresentationStage _stage = null!;
    private Camera3D _camera = null!;
    private GameSession _session = null!;
    private ScenarioDefinition _scenario = null!;
    private string _output = "";
    private long _focus = 1;
    private int _clipFrame;
    private readonly List<double> _frameMs = [];
    private readonly List<double> _stageMs = [];
    private readonly HashSet<long> _walkers = [];
    private readonly HashSet<long> _opposition = [];
    private readonly HashSet<Cell> _humanHeadings = [];
    private static readonly Cell[] ManualHeadings = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(-1, 1)];
    private int _arrivals, _missingArrivals, _outsideRoute, _faceErrors, _peakWalkers;
    private float _maxDisplacement, _minFaceDot = 1, _pauseDrift, _skinDrift;
    private int _authorityTargets;

    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Fields)!.GetValue(value)!;
    private static void Check(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
    }

    private Node3D Actor(long id) => _stage.GetNode<Node3D>("Actor_" + id);
    private object Mover(long id)
    {
        var actors = (System.Collections.IDictionary)Field<object>(_stage, "_actors");
        return actors[id]!;
    }
    private static Vector3 Flat(Vector3 value) => new(value.X, 0, value.Z);

    private void Stage(GameSession session)
    {
        if (_stage is not null)
        {
            RemoveChild(_stage);
            _stage.QueueFree();
        }
        _session = session;
        _scenario = session.Engine.Configuration();
        _stage = new PresentationStage();
        AddChild(_stage);
        _stage.SetProcess(false);
        _camera = Field<Camera3D>(_stage, "_camera");
        Show();
    }

    private PresentationFrame Show(bool paused = false)
    {
        var frame = Presentation.Read(_scenario, _session.Engine, 1);
        _stage.Show(frame, paused, 0, "Вы", false, "ru", _session.Engine.TravelQuantum);
        return frame;
    }

    private void Look()
    {
        var center = Actor(_focus).Position + Vector3.Up * 0.18f;
        var offset = _focus == 1 ? new Vector3(0.9f, 0.94f, -1.66f) : new(-0.55f, 2.15f, 1.4f);
        _camera.Position = center + offset;
        _camera.LookAt(center, Vector3.Up);
    }

    private void ObserveWalk(PresentationFrame frame, IReadOnlyDictionary<long, Vector3> before)
    {
        int moving = 0;
        foreach (var actor in frame.Actors)
        {
            var node = Actor(actor.Id);
            var delta = Flat(node.Position - before[actor.Id]);
            if (delta.LengthSquared() < 0.00000001f) continue;
            ++moving;
            _walkers.Add(actor.Id);
            _maxDisplacement = Math.Max(_maxDisplacement, delta.Length());
            if (actor.Human)
                _humanHeadings.Add(new(Math.Abs(delta.X) > 0.001f ? Math.Sign(delta.X) : 0,
                    Math.Abs(delta.Z) > 0.001f ? Math.Sign(delta.Z) : 0));
            var nose = node.FindChild("Nose", true, false) as Node3D;
            Check(nose is not null, "An ordinary character lost its visible face marker");
            float dot = Flat(nose!.GlobalPosition - node.GlobalPosition).Normalized().Dot(delta.Normalized());
            _minFaceDot = Math.Min(_minFaceDot, dot);
            if (dot < 0.8f) _faceErrors++;
            var mover = Mover(actor.Id);
            float Swing(string joint) => Field<Node3D>(mover, joint).Rotation.X - Field<Vector3>(mover, joint + "Rest").X;
            float left = Swing("LegL"), right = Swing("LegR"), leftArm = Swing("ArmL"), rightArm = Swing("ArmR");
            if (left * right < -0.01f && left * leftArm < -0.005f && right * rightArm < -0.005f)
                _opposition.Add(actor.Id);
        }
        _peakWalkers = Math.Max(_peakWalkers, moving);
    }

    private async Task Quantum(string? clip, bool measure = false, int advance = 1)
    {
        var frame = Show();
        for (int i = 0; i < 3; i++)
        {
            var before = frame.Actors.ToDictionary(a => a.Id, a => Actor(a.Id).Position);
            long start = Stopwatch.GetTimestamp();
            _stage._Process(FrameSeconds);
            if (measure) _stageMs.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            Look();
            ObserveWalk(frame, before);
            if ((clip is not null && i == 1) || measure)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (measure) _frameMs.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                if (clip is not null)
                    Check(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(clip, $"{_clipFrame++:000}.png")) == Error.Ok,
                        "Could not save Godot frame");
            }
        }

        foreach (var actor in frame.Actors.Where(a => a.Motion.Active))
        {
            var motion = actor.Motion;
            var target = Field<Vector3>(Mover(actor.Id), "To");
            float phase = (float)(_session.Engine.TravelQuantum + 1) / SimulationEngine.TravelQuantaPerTick;
            var expected = PedestrianPoseMath.At(motion, phase);
            if (target.DistanceTo(new(expected.X, 0, expected.Z)) < 0.015f) _authorityTargets++;
            bool InCell(int x, int z) => Math.Abs(target.X - x) <= 0.501f && Math.Abs(target.Z - z) <= 0.501f;
            if (!InCell(motion.Ax, motion.Az) && !InCell(motion.Bx, motion.Bz) && !InCell(motion.Cx, motion.Cz))
                _outsideRoute++;
        }

        var busy = _session.Engine.Inspect().Participants.Where(p => p.Busy && p.Destination is not null).Select(p => p.Id).ToHashSet();
        for (int i = 0; i < advance; i++) _session.AdvanceTravelQuantum();
        foreach (var actor in _session.Engine.Inspect().Participants.Where(p => busy.Contains(p.Id) && !p.Busy))
        {
            _arrivals++;
            if (!_session.Engine.PedestrianMotionOf(actor.Id.Value).Active) _missingArrivals++;
        }
    }

    private string Clip(string name)
    {
        string folder = Path.Combine(_output, name);
        Directory.CreateDirectory(folder);
        _clipFrame = 0;
        return folder;
    }

    private async Task Controlled()
    {
        string clip = Clip("controlled");
        foreach (var heading in ManualHeadings.Append(new Cell(0, 0)))
        {
            _session.SetTravelHeading(new(1), heading.X, heading.Z);
            for (int n = 0; n < 5; n++) await Quantum(clip);
        }

        foreach (var participant in _scenario.Participants)
        {
            var actor = new ParticipantId(participant.Id);
            var origin = _session.Engine.Observe(actor).Participant.Position;
            var target = new[] { new Cell(origin.X - 4, origin.Z + 2), new(origin.X + 4, origin.Z - 2),
                    new(origin.X, origin.Z + 3), new(origin.X, origin.Z - 3) }
                .First(c => _session.Engine.PreviewMovement(actor, c).Allowed);
            _session.Send(actor, new(ActionKind.MoveTo, Target: target));
        }

        for (int n = 0; n < 25; n++) await Quantum(n % 2 == 0 ? clip : null, measure: n > 17);
        var player = Actor(1);
        var point = player.Position;
        var leg = Field<Node3D>(Mover(1), "LegL").Rotation;
        Show(paused: true);
        _stage._Process(FrameSeconds);
        _pauseDrift = player.Position.DistanceTo(point);
        Check(Field<Node3D>(Mover(1), "LegL").Rotation.DistanceTo(leg) < 0.0001f, "Pause changed the leg pose");
        WorldSkins.Configure(new AppearancePreferences { WorldSet = "cozy", PeopleSet = "classic" });
        Show(paused: true);
        _skinDrift = player.Position.DistanceTo(point);
        var saved = CheckpointStore.Decode(CheckpointStore.Encode(_session));
        Check(saved.Engine.ComputeStateHash() == _session.Engine.ComputeStateHash(), "Mid-route save changed the world");
        foreach (var p in _scenario.Participants)
            Check(saved.Engine.PedestrianMotionOf(p.Id) == _session.Engine.PedestrianMotionOf(p.Id),
                "Active route slice changed after load");
        Stage(saved);
        for (int n = 0; n < 36; n++) await Quantum(n % 3 == 0 ? clip : null, measure: n < 12, advance: n == 12 ? 2 : 1);
        for (int n = 0; n < 120 && _scenario.Participants.Any(p => _session.Engine.Observe(new(p.Id)).Participant.Busy); n++)
            await Quantum(null);
        Check(_scenario.Participants.All(p => !_session.Engine.Observe(new(p.Id)).Participant.Busy),
            "Controlled paths did not arrive");
        Stage(new(new(_scenario)));
        Check(Actor(1).Position.DistanceTo(new(_scenario.Participants[0].Position.X, 0, _scenario.Participants[0].Position.Z)) < 0.001f,
            "Replacing a world inherited a previous position");
    }

    private async Task Natural(string path)
    {
        var session = CheckpointStore.Load(path).Session;
        session.RestoreControllers(c => c.Kind == SelfBallotController.Kind
            ? SelfBallotController.Restore(c) : AlgorithmicController.Restore(c));
        var definition = session.Engine.Configuration();
        var terminal = definition.CapitalDemand!.Terminal;
        var anchors = definition.Layout!.IndustrialLots.Select(lot => IndustrialParcels.TryFrame(lot)!.Value.Anchor).ToHashSet();
        for (int tick = 0; tick < 320; tick++)
        {
            var candidate = session.Engine.Inspect().Participants.FirstOrDefault(p => p.Id.Value != 1 &&
                p.Position == terminal && p.Destination is { } destination && anchors.Contains(destination));
            if (candidate is not null)
            {
                _focus = candidate.Id.Value;
                WorldSkins.Configure(new AppearancePreferences { WorldSet = "cozy", PeopleSet = "cozy" });
                Stage(session);
                string clip = Clip("npc");
                long startTick = session.Engine.Tick;
                for (int n = 0; n < 120 && session.Engine.Observe(candidate.Id).Participant.Busy; n++)
                    await Quantum(n % 2 == 0 ? clip : null);
                var end = session.Engine.Observe(candidate.Id).Participant;
                Check(!end.Busy && end.Position == candidate.Destination, "Autonomous NPC did not reach its parcel");
                File.WriteAllText(Path.Combine(_output, "npc-route.json"), JsonSerializer.Serialize(new
                {
                    actor = candidate.Id.Value, startTick, endTick = session.Engine.Tick,
                    from = terminal, destination = candidate.Destination, end = end.Position, arrived = !end.Busy
                }, SimulationJson.Options));
                await Quantum(clip); // Draw the final authority slice at arrival as well.
                return;
            }
            session.Step();
        }
        throw new InvalidOperationException("No autonomous terminal-to-parcel route found");
    }

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            string Get(string name) => args[Array.IndexOf(args, name) + 1];
            _output = Get("--output");
            var scenario = JsonSerializer.Deserialize<ScenarioDefinition>(File.ReadAllText(Get("--scenario")), SimulationJson.Options)!;
            WorldSkins.Configure(new AppearancePreferences { WorldSet = "cozy", PeopleSet = "cozy" });
            Stage(new(new(scenario)));
            await Controlled();
            if (args.Contains("--natural")) await Natural(Get("--natural"));
            Check(ManualHeadings.All(_humanHeadings.Contains) && _peakWalkers >= 4 && _opposition.Count >= 4 && _arrivals >= 4,
                "Insufficient native movement, heading, gait or arrival coverage");
            if (!args.Contains("--baseline"))
                Check(_faceErrors == 0 && _missingArrivals == 0 && _outsideRoute == 0 &&
                    _maxDisplacement < 0.2f && _pauseDrift < 0.001f && _skinDrift < 0.001f,
                    $"Native motion failed: face={_faceErrors}, arrival={_missingArrivals}, outside={_outsideRoute}, " +
                    $"step={_maxDisplacement}, pause={_pauseDrift}, skin={_skinDrift}");
            var frames = _frameMs.Order().ToArray();
            var stage = _stageMs.Order().ToArray();
            File.WriteAllText(Path.Combine(_output, "native.json"), JsonSerializer.Serialize(new
            {
                status = "succeeded", baseline = args.Contains("--baseline"), actors = scenario.Participants.Length,
                headings = _humanHeadings.OrderBy(c => c.X).ThenBy(c => c.Z),
                walkers = _walkers.Count, opposition = _opposition.Count, _peakWalkers,
                _arrivals, _missingArrivals, _outsideRoute, _authorityTargets, _faceErrors, _minFaceDot,
                _maxDisplacement, _pauseDrift, _skinDrift, sampledFrames = frames.Length,
                medianFrameMs = frames[frames.Length / 2], p95FrameMs = frames[(int)Math.Ceiling(frames.Length * .95) - 1],
                medianStageMs = stage[stage.Length / 2], p95StageMs = stage[(int)Math.Ceiling(stage.Length * .95) - 1]
            }, new JsonSerializerOptions(SimulationJson.Options) { WriteIndented = true }));
            GD.Print("I9_MOTION_PASS");
            GetTree().Quit();
        }
        catch (Exception failure)
        {
            GD.PushError(failure.ToString());
            GetTree().Quit(1);
        }
    }
}
