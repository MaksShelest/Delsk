using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;

/// <summary>Isolated dealer and extractor smoke. Never shipped with the ordinary client.</summary>
public partial class I6C22Probe : Control
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private string _output = "";
    private readonly List<string> _checks = [];
    private static T Field<T>(object o, string key) => (T)o.GetType().GetField(key, Flags)!.GetValue(o)!;
    private static void Set(object o, string key, object value) => o.GetType().GetField(key, Flags)!.SetValue(o, value);
    private static object? Call(object o, string key, params object[] args) => o.GetType().GetMethod(key, Flags)!.Invoke(o, args);
    private static IEnumerable<Node> All(Node n) { yield return n; foreach (Node c in n.GetChildren()) foreach (var x in All(c)) yield return x; }
    private void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); _checks.Add(text); GD.Print("I6C22 " + text); }
    private async Task Frames(int n = 3) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Shot(string name)
    {
        await Frames(); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Check(GetViewport().GetTexture().GetImage().SavePng(_output + "/" + name + ".png") == Error.Ok, "capture " + name);
    }
    private void Camera(Playfield app, Vector3 center, float size)
    {
        var stage = Field<PresentationStage>(app, "_stage"); Set(stage, "_follow", false);
        var camera = Field<Camera3D>(stage, "_camera"); camera.Projection = Camera3D.ProjectionType.Orthogonal;
        camera.Size = size; camera.Position = center + new Vector3(0, 35, 20); camera.LookAt(center, Vector3.Up);
        Field<Control>(app, "_playChrome").Visible = false;
    }
    public override async void _Ready()
    {
        try
        {
            MouseFilter = MouseFilterEnum.Ignore; var args = OS.GetCmdlineUserArgs();
            _output = args[Array.IndexOf(args, "--output") + 1]; System.IO.Directory.CreateDirectory(_output);
            GetWindow().Size = new(1100, 720); GetWindow().ContentScaleSize = GetWindow().Size;
            var app = GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>(); AddChild(app); await Frames(8); app.SetProcess(false);
            var valley = (OrdinaryGameResolution)Call(app, "ResolveNewGame", PlayMaps.CompactValley, 3)!;
            var funded = valley.Scenario with { Participants = valley.Scenario.Participants.Select(p => p.Id == 1 ? p with { CashMinor = 1000000 } : p).ToArray() };
            var session = new GameSession(new(funded));
            string save = typeof(Playfield).GetProperty("SavePath", Flags)!.GetValue(app)!.ToString()!;
            CheckpointStore.Save(session, save); Call(app, "Load"); Call(app, "ContinueGame"); await Frames(8);
            app.SetProcess(false); session = Field<GameSession>(app, "_session"); session.SetPaused(true); Call(app, "Refresh");
            var fleet = session.Engine.InspectFleet()!;
            Check(fleet.Stock.Length == 4, "dealer shows four vehicles");
            Check(fleet.Stock.Count(v => v.ModelId.StartsWith("extractor_", StringComparison.Ordinal)) == 2, "two extractor bays");
            var dealer = session.Engine.Configuration().Layout!.CivicObjects.Single(c => c.Kind == "dealership");
            Camera(app, new(25.5f, 0, 8), 14); await Shot("01-dealer-lot");
            var backdrop = new ColorRect { Color = new(0.96f, 0.95f, 0.92f), MouseFilter = MouseFilterEnum.Ignore };
            backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(backdrop);
            var host = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Stop };
            host.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(host);
            var panel = new FleetPanel("ru", a => session.Send(new(1), a), a => session.Engine.AccessFor(new(1), a), session.Engine.Configuration());
            panel.SizeFlagsHorizontal = SizeFlags.ExpandFill; host.AddChild(panel);
            panel.ShowData(PlayerDesk.Read(session.Engine, new(1)), false); panel.ShowCatalog(true); await Frames(6);
            var mine = All(panel).OfType<Label>().First(l => l.Text.Contains("Горная машина"));
            host.EnsureControlVisible(mine); await Frames(4);
            string catalog = string.Join("\n", All(panel).Select(n => n is Label l ? l.Text : n is Button b ? b.Text : ""));
            Check(catalog.Contains("Полевой комбайн") && catalog.Contains("Лесозаготовительная машина") && catalog.Contains("Молочная машина") && catalog.Contains("Горная машина"), "catalog names the four classes");
            Check(catalog.Contains("Следующее обновление"), "catalog explains the next refresh");
            await Shot("02-class-catalog"); host.QueueFree(); backdrop.QueueFree(); await Frames(2);
            string Go(GameAction action)
            {
                string command = session.Send(new(1), action); session.Step();
                Check(session.Engine.Result(command)?.Status == ResultStatus.Accepted, action.Kind + " " + session.Engine.Result(command)?.ReasonCode);
                int bound = 1500; while (session.Engine.Observe(new(1)).Participant.Busy && bound-- > 0) session.Step();
                Check(bound > 0, "bounded " + action.Kind); return command;
            }
            var grain = session.Engine.Configuration().ResourceNodes.First(n => n.ItemId == "grain" && !n.Rules!.Public);
            Go(new(ActionKind.MoveTo, Target: grain.Position));
            Go(new(ActionKind.BuyResource, Target: grain.Position));
            Go(new(ActionKind.MoveTo, Target: dealer.Entry));
            var field = session.Engine.InspectFleet()!.Stock.Single(v => v.ModelId == "extractor_field_1");
            Go(new(ActionKind.BuyVehicle, VehicleId: field.Id));
            int wait = 12; while (session.Engine.InspectFleet()!.Vehicles.All(v => v.Id != field.Id) && wait-- > 0) session.Step();
            Check(session.Engine.InspectFleet()!.Vehicles.Any(v => v.Id == field.Id), "field harvester purchased");
            Go(new(ActionKind.MoveTo, Target: session.Engine.Inspect().Inventories.Single(i => i.Id == session.Engine.InspectFleet()!.Vehicles.Single(v => v.Id == field.Id).CargoInventory).Position));
            Go(new(ActionKind.BoardVehicle, VehicleId: field.Id));
            Go(new(ActionKind.MoveTo, Target: grain.Position));
            Go(new(ActionKind.PlaceEquipment, Equipment: new(field.Id), ItemId: "grain"));
            Call(app, "Refresh"); Camera(app, new(grain.Position.X, 0, grain.Position.Z), 8); await Shot("03-field-harvester");
            var card = new HouseholdObjectCard(session.Engine.Configuration(), "ru", "equipment", field.Id, a => session.Send(new(1), a), a => session.Engine.AccessFor(new(1), a));
            card.CustomMinimumSize = new(460, 640); card.Position = new(24, 24); AddChild(card);
            card.ShowData(PlayerDesk.Read(session.Engine, new(1)), false, true); await Frames(4);
            string cardText = string.Join("\n", All(card).Select(n => n is Label l ? l.Text : n is Button b ? b.Text : ""));
            Check(cardText.Contains("Полевой комбайн") && cardText.Contains("уровень 1") && cardText.Contains("Улучшить до уровня 2"), "card shows the class, level and upgrade");
            await Shot("04-equipment-card"); card.QueueFree(); await Frames(2);
            Go(new(ActionKind.MoveTo, Target: dealer.Entry));
            var forest = session.Engine.InspectFleet()!.Stock.Single(v => v.ModelId == "extractor_forest_1");
            Go(new(ActionKind.BuyVehicle, VehicleId: forest.Id));
            wait = 12; while (session.Engine.InspectFleet()!.Vehicles.All(v => v.Id != forest.Id) && wait-- > 0) session.Step();
            Go(new(ActionKind.MoveTo, Target: session.Engine.Inspect().Inventories.Single(i => i.Id == session.Engine.InspectFleet()!.Vehicles.Single(v => v.Id == forest.Id).CargoInventory).Position));
            Go(new(ActionKind.BoardVehicle, VehicleId: forest.Id));
            var beside = session.Engine.Configuration().Layout!.InitialCityDirtCells.OrderBy(c => Math.Abs(c.X - grain.Position.X) + Math.Abs(c.Z - grain.Position.Z)).First();
            Go(new(ActionKind.MoveTo, Target: beside));
            Call(app, "Refresh");
            Camera(app, new((grain.Position.X + beside.X) / 2f, 0, (grain.Position.Z + beside.Z) / 2f), 14);
            await Shot("05-class-comparison");
            Check(session.Engine.Inspect().Equipment.Single(e => e.Id.Value == field.Id).Deployed == true, "harvester stayed on the plot");
            session.Engine.ValidateInvariants();
            System.IO.File.WriteAllText(_output + "/native.json", System.Text.Json.JsonSerializer.Serialize(new { status = "succeeded", checks = _checks }));
            GetTree().Quit();
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
}
