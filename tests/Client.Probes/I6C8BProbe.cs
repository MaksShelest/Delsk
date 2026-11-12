using Godot;

/// <summary>Кадры экрана специализации. Linux software GL не заменяет Windows или Android.</summary>
public partial class I6C8BProbe : Control
{
    public override void _Ready() => _ = Run();

    private async Task Run()
    {
        string output = ".";
        var args = OS.GetCmdlineUserArgs();
        int option = System.Array.IndexOf(args, "--output");
        if (option >= 0 && option + 1 < args.Length) output = args[option + 1];
        DirAccess.MakeDirRecursiveAbsolute(output);
        try
        {
            await Show("ru", 1100, 720, "ru-1100x720", select: false);
            await Show("ru", 1100, 720, "ru-1100x720-food", select: true);
            await Show("en", 1100, 720, "en-1100x720-dairy", select: true, industry: "dairy");
            await Show("ru", 960, 440, "ru-960x440-wood", select: true, industry: "woodworking");
            await Show("en", 800, 360, "en-800x360-materials", select: true, industry: "materials");
            await Show("ru", 640, 360, "ru-640x360-food", select: true);
            await Show("en", 360, 640, "en-360x640", select: true, grants: false);
            await Show("ru", 960, 440, "ru-advanced-960x440", select: true, friendly: false);
            await ShowPlayerScreens();
            GD.Print("I6C8B screenshots ok");
            System.IO.File.WriteAllText(output + "/native.json", System.Text.Json.JsonSerializer.Serialize(new { status = "succeeded", screens = System.IO.Directory.GetFiles(output, "*.png").Select(System.IO.Path.GetFileName).Order().ToArray() }));
            GetTree().Quit(0);
        }
        catch (System.Exception ex)
        {
            GD.PrintErr(ex);
            try { System.IO.File.WriteAllText(output + "/native.json", System.Text.Json.JsonSerializer.Serialize(new { status = "failed", error = ex.ToString() })); }
            catch (System.Exception write) { GD.PrintErr(write); }
            GetTree().Quit(1);
        }

        async Task Show(string language, int width, int height, string name, bool select, string industry = "food", bool friendly = true, bool grants = true)
        {
            var view = new SubViewport
            {
                Name = "Shot",
                Size = new Vector2I(width, height),
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                HandleInputLocally = true
            };
            AddChild(view);
            await Frames(1);
            bool confirmed = false;
            bool cancelled = false;
            string chosen = "";
            var screen = new SpecializationScreen(language, friendly, grants);
            screen.Confirmed += value => { confirmed = true; chosen = value; };
            screen.Cancelled += () => cancelled = true;
            view.AddChild(screen);
            await Frames(4);
            var confirm = Find<Button>(screen, "ConfirmSpecialization") ?? throw new System.InvalidOperationException("no confirm");
            var cards = Find<GridContainer>(screen, "SpecializationCards") ?? throw new System.InvalidOperationException("no cards");
            if (confirm.Disabled != true) throw new System.InvalidOperationException("confirm starts enabled");
            if (confirmed) throw new System.InvalidOperationException("cardless start");
            int expected = friendly ? (width < 600 ? 1 : 2) : (width < 900 || height < 520 ? 1 : 2);
            if (cards.Columns != expected) throw new System.InvalidOperationException($"columns {cards.Columns} at {width}x{height}, viewport {screen.GetViewportRect().Size}");
            Check(Fits(confirm, view), "Start button stays in the viewport");
            Check((Find<Control>(screen, "StartingGrantHelp") is not null) == (friendly && grants), "Grant is described only when available in simple mode");
            foreach (string key in new[] { "woodworking", "food", "dairy", "materials" })
            {
                var option = Find<Button>(screen, "Specialization_" + key)!;
                Check((option.Icon is not null) == friendly, "Industry illustration follows the UI mode");
                Check(option.Text.Count(c => c == '\n') == (friendly ? 1 : 3), "Simple cards keep one production chain");
            }
            if (select)
            {
                var card = Find<Button>(screen, "Specialization_" + industry) ?? throw new System.InvalidOperationException(industry);
                card.EmitSignal(Button.SignalName.Pressed);
                await Frames(2);
                if (confirmed || cancelled) throw new System.InvalidOperationException("card press started or cancelled the game");
                if (confirm.Disabled) throw new System.InvalidOperationException("confirm stayed disabled");
                if (!card.Text.Contains('→')) throw new System.InvalidOperationException("chain missing");
            }
            await Shot(view, output + "/" + name + ".png");
            if (select)
            {
                confirm.EmitSignal(Button.SignalName.Pressed);
                await Frames(1);
                if (!confirmed || chosen != industry) throw new System.InvalidOperationException("confirm did not keep " + industry);
            }
            else
            {
                var back = Find<Button>(screen, "BackToMapSelection") ?? throw new System.InvalidOperationException("no back");
                back.EmitSignal(Button.SignalName.Pressed);
                await Frames(1);
                if (!cancelled || confirmed) throw new System.InvalidOperationException("back did not return");
            }
            view.QueueFree();
            await Frames(1);
        }

        async Task ShowPlayerScreens()
        {
            var view = new SubViewport { Size = new(1100, 720), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(view);
            var app = GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();
            view.AddChild(app);
            await Frames(5);
            var start = Find<Button>(app, "StartGame")!;
            var npc = Find<SpinBox>(app, "NpcCount")!;
            Check(!npc.IsVisibleInTree() && Fits(start, view), "Simple home screen prioritizes starting the game");
            Find<UiDisclosure>(app, "GameSettings")!.Open();
            await Frames(2);
            Check(npc.IsVisibleInTree(), "Optional setup remains reachable");
            view.Size = new(405, 720);
            await Frames(5);
            Check(Fits(start, view), "Start stays visible in a portrait viewport");
            await Shot(view, output + "/menu-portrait.png");
            view.Size = new(1100, 720);
            await Frames(3);
            await Shot(view, output + "/menu-desktop.png");
            start.EmitSignal(Button.SignalName.Pressed);
            await Frames(5);
            Find<Button>(app, "Specialization_woodworking")!.EmitSignal(Button.SignalName.Pressed);
            Find<Button>(app, "ConfirmSpecialization")!.EmitSignal(Button.SignalName.Pressed);
            await Frames(8);
            var grant = Find<Control>(app, "ProvincialProgram");
            if (grant is not null) grant.Hide();
            Find<Button>(app, "HudTablet")!.EmitSignal(Button.SignalName.Pressed);
            await Frames(6);
            var goal = Find<Button>(app, "OverviewGoal")!;
            Check(!goal.Disabled && goal.Text == "Открыть программу", "Overview offers a working first action");
            Check(goal.GetThemeFontSize("font_size") >= 18, "Simple tablet action uses the readable theme");
            await Shot(view, output + "/business-desktop.png");
            view.Size = new(640, 360);
            await Frames(6);
            Check(Fits(Find<Button>(app, "TabletClose")!, view), "Tablet close stays inside a narrow viewport");
            var picker = Find<OptionButton>(app, "PlayerSectionPicker")!;
            Check(Fits(picker, view), "Mobile section picker stays inside the tablet header");
            Check(Enumerable.Range(0, picker.ItemCount).Any(i => picker.GetItemText(i) == "Продать товары"), "Selling is reachable in mobile navigation");
            Check(Find<ScrollContainer>(app, "PlayerPage_overview")!.GetGlobalRect().Encloses(goal.GetGlobalRect()), "First action is fully visible without scrolling");
            await Shot(view, output + "/business-narrow.png");
            var mode = typeof(Playfield).GetMethod("SetSimple", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            mode.Invoke(app, [false]);
            await Frames(3);
            Check(!Find<Label>(app, "OverviewGoalNext")!.Visible && goal.GetThemeFontSize("font_size") == 16, "Full tablet restores its original presentation");
            mode.Invoke(app, [true]);
            await Frames(3);
            goal.EmitSignal(Button.SignalName.Pressed);
            await Frames(4);
            Check(Find<Control>(app, "ProvincialProgram")!.IsVisibleInTree(), "Overview opens the support program");
            Check(Find<TabletPanel>(app, "GameTablet") is null, "Starting support returns the player to the world");
            view.QueueFree();
            await Frames(2);
        }
    }

    private static bool Fits(Control control, SubViewport view) => new Rect2(Vector2.Zero, view.Size).Encloses(control.GetGlobalRect());
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(SubViewport view, string path)
    {
        await Frames(2);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = view.GetTexture().GetImage();
        if (image.GetWidth() <= 0 || image.SavePng(path) != Error.Ok) throw new System.InvalidOperationException(path);
    }

    private static T? Find<T>(Node node, string name) where T : Node
    {
        if (node.Name == name && node is T found) return found;
        foreach (Node child in node.GetChildren())
        {
            var nested = Find<T>(child, name);
            if (nested is not null) return nested;
        }
        return null;
    }
}
