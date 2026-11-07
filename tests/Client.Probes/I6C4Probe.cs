using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;
using System.Text.Json;

/// <summary>Изолированный native smoke «Лесного края». В обычный клиент не входит.</summary>
public partial class I6C4Probe : Control
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private Playfield _app = null!;
    private GameSession _session = null!;
    private string _shots = "";
    private string _output = "";
    private readonly List<string> _checks = [];
    private static T Field<T>(object o, string key) => (T)o.GetType().GetField(key, Flags)!.GetValue(o)!;
    private static void Set(object o, string key, object value) => o.GetType().GetField(key, Flags)!.SetValue(o, value);
    private static object? Call(object o, string key, params object[] args) => o.GetType().GetMethod(key, Flags)!.Invoke(o, args);
    private static IEnumerable<Node> All(Node root) { yield return root; foreach (var c in root.GetChildren()) foreach (var n in All(c)) yield return n; }
    private T Named<T>(string name) where T : Node => All(_app).OfType<T>().First(n => n.Name == name && (n is not Control c || c.IsVisibleInTree()));
    private PlayerView View => PlayerDesk.Read(_session.Engine, new(1));
    private void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); _checks.Add(text); GD.Print("I6C4 " + text); }
    private async Task Frames(int n = 3) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Shot(string name)
    {
        await Frames();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Check(GetViewport().GetTexture().GetImage().SavePng(_shots + "/" + name + ".png") == Error.Ok, "capture " + name);
    }
    private void Refresh() => Call(_app, "Refresh");
    private async Task Click(string name)
    {
        var button = Named<Button>(name); UiNavigation.Reveal(button); await Frames(); Check(!button.Disabled, "enabled " + name);
        var p = button.GetGlobalRect().GetCenter(); int w = GetWindow().GetWindowId();
        Input.ParseInputEvent(new InputEventMouseMotion { WindowId = w, Position = p, GlobalPosition = p });
        Input.ParseInputEvent(new InputEventMouseButton { WindowId = w, Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = true }); await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton { WindowId = w, Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = false }); await Frames();
    }
    private void Command(GameAction action)
    {
        _session.SetPaused(false); string id = _session.Send(new(1), action); _session.Step();
        Check(_session.Engine.Result(id)?.Status == ResultStatus.Accepted, "Core " + action.Kind + " " + _session.Engine.Result(id)?.ReasonCode);
        int limit = 800; while (View.Participant.Busy && limit-- > 0) _session.Step();
        Check(limit > 0, "bounded real movement/work"); _session.SetPaused(true); Refresh();
    }
    private void Move(Cell at) { if (View.Participant.Position != at) Command(new(ActionKind.MoveTo, Target: at)); }
    private void Look(Vector3 focus, float distance, bool top)
    {
        var stage = Field<PresentationStage>(_app, "_stage");
        Set(stage, "_follow", false);
        var camera = Field<Camera3D>(stage, "_camera");
        if (top)
        {
            camera.Projection = Camera3D.ProjectionType.Orthogonal;
            camera.Size = distance;
            camera.Position = focus + new Vector3(0, 40, 0.01f);
        }
        else
        {
            camera.Projection = Camera3D.ProjectionType.Perspective;
            camera.Position = focus + new Vector3(0, distance, distance * 0.72f);
        }
        camera.LookAt(focus, Vector3.Up);
    }
    private Node3D? ResourceNode(string item)
    {
        var stage = Field<PresentationStage>(_app, "_stage");
        return stage.GetChildren().OfType<Node3D>().FirstOrDefault(node => node.HasMeta("resource_item") && node.GetMeta("resource_item").AsString() == item);
    }

    public override async void _Ready()
    {
        try
        {
            MouseFilter = MouseFilterEnum.Ignore;
            var args = OS.GetCmdlineUserArgs();
            string root = args[Array.IndexOf(args, "--repository") + 1];
            _output = args[Array.IndexOf(args, "--output") + 1];
            _shots = root + "/docs/verification/i6c4";
            Directory.CreateDirectory(_shots);
            Directory.CreateDirectory(_output);
            GetWindow().Size = new(1100, 720);
            GetWindow().ContentScaleSize = GetWindow().Size;
            _app = GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();
            AddChild(_app); await Frames(10); _app.SetProcess(false);
            Set(_app, "_language", "ru");
            Call(_app, "ShowMaps"); await Frames();
            await Click("ChooseMap");
            await Click("SelectMap_second");
            Check(!Named<Button>("StartGame").Disabled, "Woodland ordinary start is enabled");
            Check(Named<SpinBox>("NpcCount").MaxValue == 3, "Woodland roster stays at three NPCs");
            await Click("StartGame"); await Frames(8);
            _app.SetProcess(false);
            _session = Field<GameSession>(_app, "_session");
            _session.SetPaused(true); Refresh(); await Frames(3);
            var d = _session.Engine.Configuration();
            var layout = d.Layout!;
            Check(d.Id == PlayMaps.WoodlandI6C4ScenarioId, "ordinary start uses the new Woodland");
            Check(d.MapWidth == 18 && d.MapHeight == 15 && layout.IndustrialLots.Length == 6, "the map stays 18 by 15 with six lots");
            Check(layout.InitialCityDirtCells.Length == 46, "the ring keeps 46 road cells");
            Check(_session.ControllerStates.Length == 3, "three independent NPCs attach");
            Check(d.Economy!.Fleet!.EquipmentDealerVersion == 2 && layout.CivicObjects.Single(c => c.Kind == "dealership").EquipmentParking!.Length == 2, "new games use the four classes and two bays");
            Check(d.ResourceNodes.Select(n => n.ItemId).Distinct().Count() == 8, "all eight resources are present");
            var grain = ResourceNode("grain");
            var wood = ResourceNode("wood");
            Check(grain is not null && grain.GetMeta("skin_id").AsString() == "WheatField", "grain uses the approved field");
            Check(wood is not null && wood.GetMeta("resource_state").AsString().StartsWith("Forest_"), "forest stays the new scatter");
            Look(new(8.5f, 0, 7f), 16.5f, true); await Shot("01-top");
            Look(new(8.5f, 0, 6.5f), 14f, false); await Shot("02-isometric");
            Look(new(1.5f, 0.3f, 1.2f), 5.5f, false); await Shot("03-forest");
            Look(new(15f, 0.4f, 5.5f), 7.5f, false); await Shot("04-city");
            Look(new(7.5f, 0.2f, 8f), 8f, false); await Shot("05-industry");
            Look(new(12.5f, 0.3f, 10f), 7f, false); await Shot("06-roads-dealer");
            var stage = Field<PresentationStage>(_app, "_stage");
            Set(stage, "_follow", true);
            Set(stage, "_cameraDistance", PresentationStage.CloseView);
            Set(stage, "_cameraTarget", PresentationStage.CloseView);
            Call(stage, "FollowHuman");
            await Shot("07-player");
            var publicWood = d.ResourceNodes.First(r => r.ItemId == "wood" && r.Rules!.Public);
            Move(publicWood.Position);
            Command(new(ActionKind.Harvest, ItemId: "wood", Quantity: 2));
            Check(View.Inventories.Single(i => i.Id == View.Participant.InventoryId).Stock.Any(i => i.ItemId == "wood" && i.Quantity >= 2), "public harvest works");
            Move(d.CapitalDemand!.Terminal);
            Check(View.Participant.Position == d.CapitalDemand.Terminal, "the terminal is reachable");
            var plot = d.Plots!.First(p => !p.IsShopSite);
            Move(plot.Position);
            Command(new(ActionKind.BuyPlot, Plot: new(plot.Id)));
            Check(View.Plots.Single(p => p.Id.Value == plot.Id).OwnerId == new ParticipantId(1), "a real lot can be bought");
            Set(_app, "_appearance", new AppearancePreferences { WorldSet = "classic", PeopleSet = "classic" });
            Call(_app, "SaveAppearance"); await Frames(3);
            Look(new(7f, 0.2f, 1f), 4.2f, false); await Shot("08-grain-classic");
            Set(_app, "_appearance", new AppearancePreferences());
            Call(_app, "SaveAppearance"); await Frames(3);
            Look(new(7f, 0.2f, 1f), 4.2f, false); await Shot("09-grain-cozy");
            Check(_session.Engine.ComputeStateHash().Length > 0, "the world remains hashed");
            await File.WriteAllTextAsync(_output + "/native.json", JsonSerializer.Serialize(new
            {
                status = "succeeded",
                map = d.Id,
                roads = layout.InitialCityDirtCells.Length,
                lots = layout.IndustrialLots.Length,
                participants = d.Participants.Length,
                dealer = d.Economy.Fleet.EquipmentDealerVersion,
                checks = _checks,
                scope = "Linux software GL and synthetic input on the ordinary Woodland start. Not a physical Windows or Android measurement."
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
}
