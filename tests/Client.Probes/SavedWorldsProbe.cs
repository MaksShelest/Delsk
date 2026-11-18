using System.Reflection;
using System.Text.Json;
using Godot;
using Simulation.Core;
using Simulation.Hosting;
using File = System.IO.File;

/// <summary>Isolated native checks of the two entry points and real disk files.</summary>
public partial class SavedWorldsProbe : Control
{
    private Playfield _game = null!;
    private SavedWorldStore _store = null!;
    private string _output = "";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameSession Current => (GameSession)typeof(Playfield).GetField("_session", Private)!.GetValue(_game)!;

    public override async void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        _output = args[Array.IndexOf(args, "--output") + 1];
        Directory.CreateDirectory(_output);
        try
        {
            MouseFilter = MouseFilterEnum.Ignore;
            GetWindow().Size = GetWindow().ContentScaleSize = new(800, 440);
            await Frames(4);
            _store = new SavedWorldStore(ProjectSettings.GlobalizePath("user://"));
            _game = GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();
            AddChild(_game);
            _game.SetProcess(false);
            await Frames();

            if (args.Contains("--restart")) await Restart();
            else await Exercise();
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(_output, "failure.json"), JsonSerializer.Serialize(new { error = error.ToString() }));
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private async Task Exercise()
    {
        Check(_store.List().Count == 0, "Test needs clean user data");
        await Click(Button("OpenMyWorlds"));
        Check(Text("Сохранённых миров пока нет."), "Missing RU empty state");
        await Click(Button("WorldsNewGame"));
        await Start("Долина 17", "17");
        string a = _store.LastActive()!;
        var first = Current;
        first.SetPaused(false);
        string command = first.Send(new(1), new(ActionKind.AcceptProvincialSupport));
        first.AdvanceTo(8);
        Check(first.Engine.Result(command)!.Status == ResultStatus.Accepted, "World 1 did not accept support");
        string hashA = first.Engine.ComputeStateHash();
        string controllerA = first.ComputeControllerStateHash();

        await WorldsFromGame();
        var aCard = Named<PanelContainer>("World_" + a);
        Check(Labels(aCard).Any(label => label.Text.Contains("Тик 8")), "World card has stale tick");
        await Click(Button("WorldsNewGame"));
        await Click(Button("ChooseMap"));
        await Click(Button("SelectMap_second"));
        await Start("Лес 91", "91");
        string b = _store.LastActive()!;
        var second = Current;
        Check(a != b && first != second && second.Engine.Tick == 0, "New game reused a slot or a clock");
        Check(second.Engine.Configuration() is { Seed: 91, Id: PlayMaps.WoodlandI6C8CScenarioId }, "World 2 map/seed changed");
        Check(second.ControllerStates.Length == 3, "World 2 NPC roster missing");
        Check(second.Engine.ProvincialProgramOf(new(1))!.Status == "Offered", "World 1 support leaked");
        var stageB = Stage();

        await WorldsFromGame();
        await Screenshot("worlds-ru-800x440");
        await Activate(Button("Continue_" + a));
        Check(!ReferenceEquals(stageB, Stage()), "Map/camera/actor cache was reused");
        Check(Current.Engine.ComputeStateHash() == hashA && Current.ComputeControllerStateHash() == controllerA,
            "World 1 world/controller hash changed while away");
        Check(Current.Engine.Configuration().Seed == 17 && Current.Engine.Tick == 8 && Current.Engine.ProvincialProgramOf(new(1))!.Status == "Open",
            "World 1 progress or provenance lost");
        await WorldsFromGame();
        await Click(Button("Rename_" + a));
        var rename = Named<ConfirmationDialog>("RenameWorldDialog");
        Named<LineEdit>("RenameWorldField").Text = "Вернувшаяся долина";
        rename.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Check(Text("Вернувшаяся долина"), "Renamed world did not refresh");

        // Autosave the second world twice to produce a valid preceding generation.
        await Activate(Button("Continue_" + b));
        Current.SetPaused(false);
        Current.AdvanceTo(3);
        _game.Notification((int)NotificationApplicationFocusOut);
        var bPath = _store.CheckpointPath(b);
        Check(_store.Load(b).Session.Engine.Tick == 3 && File.Exists(bPath + ".bak"), "Focus loss missed autosave");
        _game.Notification((int)NotificationApplicationFocusIn);
        await WorldsFromGame();
        await Activate(Button("Continue_" + a));
        await WorldsFromGame();
        File.WriteAllText(bPath, "{incomplete");
        await Activate(Button("Continue_" + b));
        var recovered = Named<AcceptDialog>("SaveError");
        Check(recovered.DialogText.Contains("резервная копия") && Current.Engine.Tick == 3, "Backup recovery was not visible");
        recovered.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        await WorldsFromGame();
        await Activate(Button("Continue_" + a));
        await WorldsFromGame();
        File.WriteAllText(bPath, "{bad main");
        File.WriteAllText(bPath + ".bak", "{bad backup");
        await Activate(Button("Continue_" + b));
        var failed = Named<AcceptDialog>("SaveError");
        Check(failed.DialogText.Contains("Не удалось прочитать") && Current.Engine.ComputeStateHash() == hashA, "Corrupt slot displaced good world");
        failed.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();

        await Click(Button("Delete_" + b));
        var confirm = Named<ConfirmationDialog>("DeleteWorldDialog");
        confirm.GetCancelButton().EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Check(File.Exists(bPath), "Cancel deleted a world");
        await Click(Button("Delete_" + b));
        Named<ConfirmationDialog>("DeleteWorldDialog").GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
        await Frames();
        Check(_store.List().Count == 1 && !File.Exists(bPath), "Confirmed delete left the slot in the catalog");

        // A verified copy of a single-file save is imported through the real startup/worlds screens.
        CheckpointStore.Save(first, _store.LegacyPath("economy"));
        byte[] legacy = File.ReadAllBytes(_store.LegacyPath("economy"));
        await Click(Button("WorldsNewGame"));
        await English();
        await Click(Button("OpenMyWorlds"));
        Check(_store.List().Count == 2 && Text("My worlds"), "Legacy import or English screen missing");
        GetWindow().Size = GetWindow().ContentScaleSize = new(360, 700);
        await Frames(5);
        foreach (var button in Named<SavedWorldsScreen>("SavedWorlds").FindChildren("*", "Button", recursive: true, owned: false).OfType<Button>())
        {
            var rect = button.GetGlobalRect();
            Check(rect.Position.X >= 0 && rect.End.X <= GetViewportRect().Size.X + 1, "World action exceeds portrait width");
        }
        await Screenshot("worlds-en-360x700");
        await Tap(Button("Continue_legacy-economy"));
        Check(Current.Engine.ComputeStateHash() == hashA && File.ReadAllBytes(_store.LegacyPath("economy")).SequenceEqual(legacy),
            "Legacy load altered the original checkpoint");
        Current.SetPaused(false);
        Current.AdvanceTo(13);
        string final = Current.Engine.ComputeStateHash();
        string finalControllers = Current.ComputeControllerStateHash();
        File.WriteAllText(Path.Combine(_output, "exit.json"), JsonSerializer.Serialize(new Receipt(final, finalControllers, 13)));
        _game.Notification((int)NotificationWMCloseRequest);
        Check(_store.Load("legacy-economy").Session.Engine.ComputeStateHash() == final, "Window close lost progress");
        GD.Print("DELSK_SAVED_WORLDS_NATIVE_PASS");
    }

    private async Task Restart()
    {
        var expected = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(Path.Combine(_output, "exit.json")))!;
        await English();
        await Activate(Button("ContinueGame"));
        Check(Current.Engine.Tick == expected.Tick && Current.Engine.ComputeStateHash() == expected.Hash
            && Current.ComputeControllerStateHash() == expected.Controllers, "One-click restart did not restore the last active world");
        Check(_store.List().Count == 2, "Repeat migration made a duplicate");
        Check(Stage().GetParent() is SubViewport viewport && viewport.RenderTargetUpdateMode == SubViewport.UpdateMode.Always,
            "Continue did not enter the game from the first click");
        File.WriteAllText(Path.Combine(_output, "native-check.json"), JsonSerializer.Serialize(new
        {
            status = "succeeded", scope = "Godot native windows, synthetic mouse, keyboard and touch",
            cases = new[] { "two worlds", "independent hashes and controllers", "rename", "backup fallback", "double failure",
                "delete cancel and confirm", "legacy migration", "focus autosave", "window close and second-process Continue", "RU/EN, landscape/portrait" }
        }));
        GD.Print("DELSK_SAVED_WORLDS_RESTART_PASS");
        GetTree().Quit();
    }

    private sealed record Receipt(string Hash, string Controllers, long Tick);

    private PresentationStage Stage() => (PresentationStage)typeof(Playfield).GetField("_stage", Private)!.GetValue(_game)!;

    private async Task Start(string name, string seed)
    {
        Named<UiDisclosure>("GameSettings").Open();
        Named<LineEdit>("NewWorldName").Text = name;
        Named<LineEdit>("NewWorldSeed").Text = seed;
        await Click(Button("StartGame"));
        await Click(Button("Specialization_woodworking"));
        await Click(Button("ConfirmSpecialization"));
        Check(Current is not null, "New game did not start");
    }

    private async Task WorldsFromGame()
    {
        await Click(Button("HudMenu"));
        await Click(Button("OpenMyWorlds"));
    }

    private async Task English()
    {
        var map = (MapSelectionScreen)typeof(Playfield).GetField("_mapSelection", Private)!.GetValue(_game)!;
        ((UiDisclosure)map.AdditionalSettings.GetParent()).Open();
        await Frames();
        await Click(_game.FindChildren("*", "Button", true, false).OfType<Button>().Single(b => b.Text == "English"));
    }

    private async Task Screenshot(string file)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_output, file + ".png"));
    }

    private bool Text(string value) => Labels(_game).Any(l => l.IsVisibleInTree() && l.Text == value);
    private static IEnumerable<Label> Labels(Node node) => node.FindChildren("*", "Label", true, false).OfType<Label>();
    private Button Button(string name) => Named<Button>(name);
    private T Named<T>(string name) where T : Node => _game.FindChildren(name, "", true, false).OfType<T>()
        .Single(n => n is not Control control || control.IsVisibleInTree());

    private async Task<Vector2> Reveal(BaseButton button)
    {
        for (Node? node = button.GetParent(); node is not null; node = node.GetParent())
            if (node is ScrollContainer scroll) scroll.EnsureControlVisible(button);
        await Frames();
        Check(!button.Disabled && button.IsVisibleInTree(), "Unavailable button " + button.Name);
        return button.GetGlobalRect().GetCenter();
    }

    private async Task Click(BaseButton button)
    {
        Vector2 pos = await Reveal(button);
        Input.WarpMouse(pos);
        await Frames(2);
        foreach (bool down in new[] { true, false })
        {
            Input.ParseInputEvent(new InputEventMouseButton { WindowId = GetWindow().GetWindowId(), Position = pos,
                GlobalPosition = pos, ButtonIndex = MouseButton.Left, Pressed = down });
            await Frames(2);
        }
    }

    private async Task Activate(BaseButton button)
    {
        await Reveal(button);
        button.GrabFocus();
        foreach (bool down in new[] { true, false })
        {
            Input.ParseInputEvent(new InputEventKey { WindowId = GetWindow().GetWindowId(), Keycode = Key.Enter, Pressed = down });
            await Frames(2);
        }
    }

    private async Task Tap(BaseButton button)
    {
        Vector2 pos = await Reveal(button);
        foreach (bool down in new[] { true, false })
        {
            Input.ParseInputEvent(new InputEventScreenTouch { WindowId = GetWindow().GetWindowId(), Position = pos, Index = 0, Pressed = down });
            await Frames(2);
        }
    }

    private async Task Frames(int count = 4)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static void Check(bool condition, string error)
    {
        if (!condition) throw new InvalidOperationException(error);
    }
}
