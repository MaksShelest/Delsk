using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Collections.Immutable;
using System.Reflection;
using System.Text;

/// <summary>Native smoke участка 2×3. Деньги и опыт подготовлены, это не обычный старт на 200 монет.
/// Linux software GL не заменяет физический Windows или Android.</summary>
public partial class I6C8AProbe : Control
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    private string _shots = "";
    private string _output = "";
    private readonly List<string> _checks = [];
    private static T Field<T>(object o, string key) => (T)o.GetType().GetField(key, Flags)!.GetValue(o)!;
    private static void Set(object o, string key, object? value) => o.GetType().GetField(key, Flags)!.SetValue(o, value);
    private static object? Call(object o, string key, params object?[] args) => o.GetType().GetMethod(key, Flags)!.Invoke(o, args);
    private static IEnumerable<Node> All(Node n) { yield return n; foreach (Node c in n.GetChildren()) foreach (var x in All(c)) yield return x; }
    private void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); _checks.Add(text); GD.Print("I6C8A " + text); }
    private async Task Frames(int n = 3) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Shot(string name)
    {
        await Frames(); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Check(GetViewport().GetTexture().GetImage().SavePng(_shots + "/" + name + ".png") == Error.Ok, "capture " + name);
    }
    private void Camera(Playfield app, Vector3 center, float size)
    {
        var stage = Field<PresentationStage>(app, "_stage"); Set(stage, "_follow", false);
        var camera = Field<Camera3D>(stage, "_camera"); camera.Projection = Camera3D.ProjectionType.Orthogonal;
        camera.Size = size; camera.Position = center + new Vector3(0, 35, 20); camera.LookAt(center, Vector3.Up);
        Field<Control>(app, "_playChrome").Visible = true;
    }
    private static EconomyProfile Profile(Playfield app)
    {
        var mobile = EconomyProfile.Read((string)typeof(Playfield).GetMethod("Text", Flags)!.Invoke(null, ["Play.i4-mobile-equipment.json"])!);
        return VNextIndustrialParcelProfile.Create(VNextResourceRightsProfile.Create(VNextWageScheduleProfile.Create(VNextEquipmentClassesProfile.Create(mobile))));
    }
    private ScenarioDefinition World(Playfield app, string root, long cash, bool buildable)
    {
        var profile = Profile(app);
        var scenario = profile.Apply(MapDesignReader.Read(Encoding.UTF8.GetBytes(File.ReadAllText(root + "/docs/vnext/maps/industrial_block_i6c8a.json")), profile));
        scenario = scenario with { Participants = scenario.Participants.Select(p => p with { CashMinor = p.Id == 1 ? cash : 20_000 }).ToArray() };
        if (!buildable) return scenario;
        var development = scenario.Economy!.Development!;
        return scenario with
        {
            Labor = (scenario.Labor ?? new LaborSettings()) with { GrowthPeriodTicks = 1, GrowthPerPeriod = 5 },
            Economy = scenario.Economy with
            {
                Development = development with
                {
                    Industries = development.Industries.Select(i => i.Id != "woodworking" ? i : i with
                    {
                        Products = i.Products.Select(p => p with { XpPerUnit = 40000 }).ToImmutableArray()
                    }).ToImmutableArray()
                }
            },
            BuildingTypes = scenario.BuildingTypes!.Select(t => t.Id != "woodworking" ? t : t with
            {
                Levels = t.Levels.Select(l => l.Level switch
                {
                    1 => l with { Materials = [new MaterialCost("wood", 4)] },
                    2 => l with { Materials = [new MaterialCost("plank", 4)] },
                    _ => l
                }).ToArray()
            }).ToArray()
        };
    }
    private GameSession Boot(Playfield app, ScenarioDefinition scenario)
    {
        var session = new GameSession(new(scenario));
        string save = typeof(Playfield).GetProperty("SavePath", Flags)!.GetValue(app)!.ToString()!;
        CheckpointStore.Save(session, save);
        Call(app, "Load"); Call(app, "ContinueGame");
        session = Field<GameSession>(app, "_session");
        Set(session, "_controllers", Array.Empty<IController>());
        session.SetPaused(true);
        return session;
    }
    private void Idle(GameSession session, GameAction action)
    {
        session.SetPaused(false);
        string id = session.Send(new(1), action); session.Step();
        var result = session.Engine.Result(id);
        Check(result?.Status == ResultStatus.Accepted, action.Kind + " " + result?.ReasonCode);
        int bound = 8000;
        while (session.Engine.Observe(new(1)).Participant.Busy && bound-- > 0) session.Step();
        Check(bound > 0, "arrived " + action.Kind);
        session.SetPaused(true);
    }
    private static IndustrialLot South(GameSession session) =>
        session.Engine.Configuration().Layout!.IndustrialLots.First(l => IndustrialParcels.TryFrame(l)!.Value.Facing == "south");
    private async Task<Button> FocusBuy(Playfield app, GameSession session, Cell anchor)
    {
        session.SetPaused(true); Call(app, "Refresh"); await Frames(3);
        Camera(app, new(anchor.X + 0.5f, 0, anchor.Z - 1), 14);
        var button = All(app).OfType<Button>().First(b => b.Name.ToString().StartsWith("Context_buy_"));
        button.GrabFocus(); await Frames(4);
        return button;
    }

    public override async void _Ready()
    {
        try
        {
            MouseFilter = MouseFilterEnum.Ignore;
            var args = OS.GetCmdlineUserArgs();
            string root = args[Array.IndexOf(args, "--repository") + 1];
            _output = args[Array.IndexOf(args, "--output") + 1];
            _shots = root + "/docs/verification/i6c8a";
            Directory.CreateDirectory(_shots); Directory.CreateDirectory(_output);
            GetWindow().Size = new(1100, 720); GetWindow().ContentScaleSize = GetWindow().Size; await Frames(3);
            var app = GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();
            AddChild(app); await Frames(8); app.SetProcess(false);
            var poor = Boot(app, World(app, root, 10_000, false));
            var lot = South(poor);
            var frame = IndustrialParcels.TryFrame(lot)!.Value;
            Idle(poor, new(ActionKind.MoveTo, Target: frame.Anchor));
            var denied = await FocusBuy(app, poor, frame.Anchor);
            Check(denied.Text == "Купить участок — 600 монет", "poor button " + denied.Text);
            Check(denied.Disabled && denied.TooltipText.Contains("Нужно 600 монет."), "short cash explains the price");
            var stage = Field<PresentationStage>(app, "_stage");
            Check(Field<bool>(stage, "_previewBlocked") && Field<Cell[]>(stage, "_preview").Length == 6, "short cash outlines six cells in red");
            await Shot("short-cash-1100x720");
            var funded = Boot(app, World(app, root, 2_000_000, true));
            Check(funded.Engine.Configuration().EconomyProfileId == VNextIndustrialParcelProfile.ProfileId, "prepared profile");
            Check(funded.Engine.Configuration().Economy!.IndustrialParcels!.Version == 1, "parcel rules");
            lot = South(funded); frame = IndustrialParcels.TryFrame(lot)!.Value;
            Idle(funded, new(ActionKind.MoveTo, Target: frame.Anchor));
            var buy = await FocusBuy(app, funded, frame.Anchor);
            Check(buy.Text == "Купить участок — 600 монет" && !buy.Disabled, "buy names 600 coins");
            Check(buy.TooltipText.Contains("6 клеток · подъезд к дороге · место для полного развития"), "caption " + buy.TooltipText);
            stage = Field<PresentationStage>(app, "_stage");
            var preview = Field<Cell[]>(stage, "_preview");
            Check(preview.OrderBy(c => c.X).ThenBy(c => c.Z).SequenceEqual(lot.Cells.OrderBy(c => c.X).ThenBy(c => c.Z)), "preview is the six cells");
            Check(Field<Cell?>(stage, "_previewEntry") == lot.Entry && Field<Cell?>(stage, "_previewAnchor") == frame.Anchor && !Field<bool>(stage, "_previewBlocked"), "entrance and anchor");
            await Shot("preview-1100x720");
            foreach (var size in new (int X, int Y, string Name)[] { (800, 480, "800x480"), (640, 360, "640x360"), (800, 360, "800x360") })
            {
                GetWindow().Size = new(size.X, size.Y); GetWindow().ContentScaleSize = GetWindow().Size;
                Call(app, "Refresh"); await Frames(3); buy.GrabFocus(); await Frames(2);
                Check(Field<Cell[]>(Field<PresentationStage>(app, "_stage"), "_preview").Length == 6, "preview stays six at " + size.Name);
                await Shot("preview-" + size.Name);
            }
            GetWindow().Size = new(1100, 720); GetWindow().ContentScaleSize = GetWindow().Size;
            long before = funded.Engine.Observe(new(1)).Account.BalanceMinor;
            Idle(funded, new(ActionKind.BuyPlot, Plot: funded.Engine.Observe(new(1)).Plots.Single(p => p.Position == frame.Anchor).Id));
            Check(before - funded.Engine.Observe(new(1)).Account.BalanceMinor == 60_000, "one payment of 600 coins");
            Check(funded.Engine.Observe(new(1)).Plots.Where(p => lot.Cells.Contains(p.Position)).All(p => p.OwnerId == new ParticipantId(1)), "six cells owned");
            Check(funded.Engine.Configuration().Layout!.RoadCorridorCells.Contains(lot.Entry), "entrance stays a public road");
            Call(app, "Refresh"); await Frames(3);
            Camera(app, new(frame.Anchor.X + 0.5f, 0, frame.Anchor.Z - 1), 14);
            await Shot("owned-1100x720");
            Idle(funded, new(ActionKind.MoveTo, Target: new(32, 4)));
            Idle(funded, new(ActionKind.Harvest, ItemId: "wood", Quantity: 8));
            var ground = funded.Engine.Inspect().Inventories.Single(i => i.Position == frame.Anchor && i.OwnerId == new ParticipantId(1));
            Idle(funded, new(ActionKind.MoveTo, Target: frame.Anchor));
            Idle(funded, new(ActionKind.Transfer, Source: funded.Engine.Observe(new(1)).Participant.InventoryId, Destination: ground.Id, ItemId: "wood", Quantity: 4));
            Idle(funded, new(ActionKind.Build, BuildingTypeId: "woodworking", Target: frame.Anchor));
            funded.SetPaused(false);
            funded.AdvanceTo(funded.Engine.DevelopmentOf(new(1))!.Projects.Single().CompletesTick);
            funded.SetPaused(true);
            var building = funded.Engine.Observe(new(1)).OwnBuildings.Single();
            Check(building.Footprint.SequenceEqual(new[] { frame.Anchor }), "first building stands on the entrance cell");
            Call(app, "Refresh"); await Frames(3);
            Camera(app, new(frame.Anchor.X, 0, frame.Anchor.Z), 10);
            await Shot("built-1100x720");
            var enterprise = funded.Engine.Observe(new(1)).OwnEnterprises.Single();
            Idle(funded, new(ActionKind.Transfer, Source: funded.Engine.Observe(new(1)).Participant.InventoryId, Destination: enterprise.InputId, ItemId: "wood", Quantity: 2));
            Idle(funded, new(ActionKind.SetProductionEnabled, Enterprise: enterprise.Id, Enabled: true));
            funded.SetPaused(false);
            for (int i = 0; i < 800 && funded.Engine.Observe(new(1)).OwnEnterprises.Single().CompletedBatches == 0; i++) funded.Step();
            Check(funded.Engine.Observe(new(1)).OwnEnterprises.Single().CompletedBatches == 1, "one prepared batch");
            funded.SetPaused(true);
            Idle(funded, new(ActionKind.SetProductionEnabled, Enterprise: enterprise.Id, Enabled: false));
            var made = funded.Engine.Observe(new(1)).OwnEnterprises.Single();
            var carrier = funded.Engine.Observe(new(1)).Participant.InventoryId;
            Idle(funded, new(ActionKind.MoveTo, Target: frame.Anchor));
            Idle(funded, new(ActionKind.Transfer, Source: made.OutputId, Destination: carrier, ItemId: "plank", Quantity: 4));
            Idle(funded, new(ActionKind.Transfer, Source: carrier, Destination: made.InputId, ItemId: "plank", Quantity: 4));
            Idle(funded, new(ActionKind.UpgradeBuilding, Building: building.Id, Quantity: 2));
            funded.SetPaused(false);
            funded.AdvanceTo(funded.Engine.DevelopmentOf(new(1))!.Projects.Single().CompletesTick);
            funded.SetPaused(true);
            var grown = funded.Engine.Observe(new(1)).OwnBuildings.Single();
            Check(grown.Level == 2 && grown.Footprint.Length == 3 && grown.Footprint.All(lot.Cells.Contains), "expansion stays inside the parcel");
            Check(IndustrialParcels.FacingOf(funded.Engine.Configuration(), grown.Position) == (frame.RoadX, frame.RoadZ), "the door still faces the road");
            Call(app, "Refresh"); await Frames(4);
            Camera(app, new(frame.Anchor.X, 0, frame.Anchor.Z - 0.5f), 12);
            await Shot("expanded-1100x720");
            File.WriteAllText(_output + "/native.json", System.Text.Json.JsonSerializer.Serialize(new { status = "succeeded", prepared = true, checks = _checks }));
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
