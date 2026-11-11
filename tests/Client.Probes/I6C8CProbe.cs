using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Кадры окна программы провинции. Linux software GL не заменяет Windows или Android.</summary>
public partial class I6C8CProbe : Control
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
            var session = Open();
            var offered = PlayerDesk.Read(session.Engine, new(1));
            if (offered.ProvincialProgram is not { Status: "Offered" }) throw new System.InvalidOperationException("program was not offered");
            await Show(offered, "ru", 1100, 720, output + "/ru-1100x720-offered.png", join: true);
            session.Send(new(1), new GameAction(ActionKind.AcceptProvincialSupport));
            session.Step();
            var open = PlayerDesk.Read(session.Engine, new(1));
            if (open.ProvincialProgram is not { Status: "Open", NextStep: "buy-parcel" }) throw new System.InvalidOperationException(open.ProvincialProgram?.Status + " " + open.ProvincialProgram?.NextStep);
            if (open.AvailableCashMinor != 20_000) throw new System.InvalidOperationException("cash changed on accept");
            await Show(open, "ru", 1100, 720, output + "/ru-1100x720-open.png", join: false);
            await Show(open, "en", 1100, 720, output + "/en-1100x720-open.png", join: false);
            await Show(open, "ru", 960, 440, output + "/ru-960x440-open.png", join: false);
            await Show(open, "en", 800, 360, output + "/en-800x360-open.png", join: false);
            GD.Print("I6C8C screenshots ok");
            System.IO.File.WriteAllText(output + "/native.json", System.Text.Json.JsonSerializer.Serialize(new
            {
                status = "succeeded",
                screens = new[] { "ru-1100x720-offered", "ru-1100x720-open", "en-1100x720-open", "ru-960x440-open", "en-800x360-open" }
            }));
            GetTree().Quit(0);
        }
        catch (System.Exception ex)
        {
            GD.PrintErr(ex);
            try { System.IO.File.WriteAllText(output + "/native.json", System.Text.Json.JsonSerializer.Serialize(new { status = "failed", error = ex.ToString() })); }
            catch (System.Exception write) { GD.PrintErr(write); }
            GetTree().Quit(1);
        }
    }

    private static GameSession Open()
    {
        var profile = VNextProvincialSupportProfile.ForOrdinaryMap(EconomyProfile.Read(Text("Play.i4-mobile-equipment.json")));
        var source = MapDesignReader.Read(System.Text.Encoding.UTF8.GetBytes(Text("Play.compact-valley-i6c8c-design.json")), profile);
        var balance = VNextBalanceReader.Read(System.Text.Encoding.UTF8.GetBytes(Text("Play.vnext-balance.json")));
        if (!balance.IsValid) throw new System.InvalidOperationException("balance");
        var resolved = OrdinaryNewGame.Resolve(source, profile, new(3, StartingIndustry: "woodworking"), vnextBalance: balance.Balance);
        return new(new(resolved.Scenario));
    }

    private static string Text(string name)
    {
        var assembly = typeof(Playfield).Assembly;
        using var stream = assembly.GetManifestResourceStream(name) ?? throw new System.InvalidOperationException(name);
        return new System.IO.StreamReader(stream).ReadToEnd();
    }

    private async Task Show(PlayerView player, string language, int width, int height, string path, bool join)
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
        GameAction? offered = null;
        var panel = new ProvincialProgramPanel();
        view.AddChild(panel);
        await Frames(2);
        panel.Show(player, language, false, action => offered = action);
        await Frames(2);
        var title = Find<Label>(panel, "ProvincialTitle") ?? throw new System.InvalidOperationException("no title");
        if (!title.Text.Contains(language == "en" ? "Provincial" : "провинции")) throw new System.InvalidOperationException(title.Text);
        var body = Find<Label>(panel, "ProvincialBody") ?? throw new System.InvalidOperationException("no body");
        if (!body.Text.Contains(language == "en" ? "Capital funding" : "Финансирование")) throw new System.InvalidOperationException(body.Text);
        var rect = panel.GetGlobalRect();
        if (rect.Position.X < 0 || rect.Position.Y < 0 || rect.End.X > width + 1 || rect.End.Y > height + 1)
            throw new System.InvalidOperationException($"panel {rect} outside {width}x{height}");
        var button = Find<Button>(panel, "JoinProvincialProgram") ?? throw new System.InvalidOperationException("no join");
        if (join)
        {
            if (!button.Visible) throw new System.InvalidOperationException("join hidden");
            button.EmitSignal(Button.SignalName.Pressed);
            await Frames(1);
            if (offered is not null) throw new System.InvalidOperationException("first press sent the command");
            button.EmitSignal(Button.SignalName.Pressed);
            await Frames(1);
            if (offered?.Kind != ActionKind.AcceptProvincialSupport) throw new System.InvalidOperationException("join did not accept");
        }
        else if (button.Visible) throw new System.InvalidOperationException("join stayed visible");
        await Shot(view, path);
        view.QueueFree();
        await Frames(1);
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
