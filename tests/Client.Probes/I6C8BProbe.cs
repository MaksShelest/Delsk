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
            GD.Print("I6C8B screenshots ok");
            System.IO.File.WriteAllText(output + "/native.json", System.Text.Json.JsonSerializer.Serialize(new { status = "succeeded", screens = new[] { "ru-1100x720", "ru-1100x720-food", "en-1100x720-dairy", "ru-960x440-wood", "en-800x360-materials", "ru-640x360-food" } }));
            GetTree().Quit(0);
        }
        catch (System.Exception ex)
        {
            GD.PrintErr(ex);
            try { System.IO.File.WriteAllText(output + "/native.json", System.Text.Json.JsonSerializer.Serialize(new { status = "failed", error = ex.ToString() })); }
            catch (System.Exception write) { GD.PrintErr(write); }
            GetTree().Quit(1);
        }

        async Task Show(string language, int width, int height, string name, bool select, string industry = "food")
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
            var screen = new SpecializationScreen(language);
            screen.Confirmed += value => { confirmed = true; chosen = value; };
            screen.Cancelled += () => cancelled = true;
            view.AddChild(screen);
            await Frames(4);
            var confirm = Find<Button>(screen, "ConfirmSpecialization") ?? throw new System.InvalidOperationException("no confirm");
            var cards = Find<GridContainer>(screen, "SpecializationCards") ?? throw new System.InvalidOperationException("no cards");
            if (confirm.Disabled != true) throw new System.InvalidOperationException("confirm starts enabled");
            if (confirmed) throw new System.InvalidOperationException("cardless start");
            int expected = width < 900 || height < 520 ? 1 : 2;
            if (cards.Columns != expected) throw new System.InvalidOperationException($"columns {cards.Columns} at {width}x{height}, viewport {screen.GetViewportRect().Size}");
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
    }

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
