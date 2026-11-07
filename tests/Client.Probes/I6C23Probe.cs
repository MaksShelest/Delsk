using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;

/// <summary>Isolated side-tablet and dealer smoke. Never shipped with the ordinary client.</summary>
public partial class I6C23Probe : Control
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private string _output = "";
    private readonly List<string> _checks = [];
    private static T Field<T>(object o, string key) => (T)o.GetType().GetField(key, Flags)!.GetValue(o)!;
    private static void Set(object o, string key, object value) => o.GetType().GetField(key, Flags)!.SetValue(o, value);
    private static object? Call(object o, string key, params object[] args) => o.GetType().GetMethod(key, Flags)!.Invoke(o, args);
    private static IEnumerable<Node> All(Node n) { yield return n; foreach (Node c in n.GetChildren()) foreach (var x in All(c)) yield return x; }
    private void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); _checks.Add(text); GD.Print("I6C23 " + text); }
    private async Task Frames(int n = 3) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Shot(string name)
    {
        await Frames(); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Check(GetViewport().GetTexture().GetImage().SavePng(_output + "/" + name + ".png") == Error.Ok, "capture " + name);
    }
    private static double Channel(float v) { v = Mathf.Clamp(v, 0, 1); return v <= 0.04045f ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
    private static double Contrast(Color a, Color b)
    {
        double left = 0.2126 * Channel(a.R) + 0.7152 * Channel(a.G) + 0.0722 * Channel(a.B);
        double right = 0.2126 * Channel(b.R) + 0.7152 * Channel(b.G) + 0.0722 * Channel(b.B);
        return (Math.Max(left, right) + 0.05) / (Math.Min(left, right) + 0.05);
    }
    private void RequireContrast(Color ink, Color paper, string name) => Check(Contrast(ink, paper) >= 4.5, name + " contrast " + Contrast(ink, paper).ToString("0.00"));
    private async Task PressContext(Playfield app, long id)
    {
        Call(app, "Refresh"); await Frames(2);
        string name = "Context_object_equipment_" + id;
        var button = All(app).OfType<Button>().FirstOrDefault(b => b.Name == name && b.IsVisibleInTree());
        if (button is null)
        {
            All(app).OfType<Button>().First(b => b.Name == "ContextMore").EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2); button = All(app).OfType<Button>().First(b => b.Name == name && b.IsVisibleInTree());
        }
        button.EmitSignal(BaseButton.SignalName.ButtonDown); button.EmitSignal(BaseButton.SignalName.Pressed); await Frames(4);
        Check(All(app).Any(n => n.Name == "ExtractionWorkspace"), "world opens the side tablet");
    }
    private static void Advance(GameSession session, int count = 1)
    {
        session.SetPaused(false);
        for (int i = 0; i < count; i++) session.Step();
    }
    private void Press(Playfield app, GameSession session, string name)
    {
        var button = All(app).OfType<Button>().First(b => b.Visible && b.Name.ToString().Contains(name, StringComparison.Ordinal));
            Check(!button.Disabled, name + " is available: " + button.Text + " / " + button.TooltipText);
        button.EmitSignal(BaseButton.SignalName.Pressed); Advance(session); Call(app, "Refresh");
    }
    public override async void _Ready()
    {
        try
        {
            await Run(); GetTree().Quit();
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
    private async Task Run()
    {
            MouseFilter = MouseFilterEnum.Ignore; var args = OS.GetCmdlineUserArgs();
            _output = args[Array.IndexOf(args, "--output") + 1]; System.IO.Directory.CreateDirectory(_output);
            GetWindow().Size = new(1100, 720); GetWindow().ContentScaleSize = GetWindow().Size;
            var app = GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>(); AddChild(app); await Frames(8);
            var valley = (OrdinaryGameResolution)Call(app, "ResolveNewGame", PlayMaps.CompactValley, 3)!;
            var funded = valley.Scenario with { Participants = valley.Scenario.Participants.Select(p => p.Id == 1 ? p with { CashMinor = 1000000 } : p).ToArray() };
            var session = new GameSession(new(funded));
            string save = typeof(Playfield).GetProperty("SavePath", Flags)!.GetValue(app)!.ToString()!;
            CheckpointStore.Save(session, save);             Call(app, "Load"); Call(app, "ContinueGame"); await Frames(4);
            app.SetProcess(false); session = Field<GameSession>(app, "_session");
            string Go(GameAction action)
            {
                string command = session.Send(new(1), action); Advance(session);
                var result = session.Engine.Result(command);
                Check(result?.Status == ResultStatus.Accepted, action.Kind + " " + (result?.Status.ToString() ?? "pending") + " " + result?.ReasonCode);
                int bound = 1500; while (session.Engine.Observe(new(1)).Participant.Busy && bound-- > 0) session.Step();
                Check(bound > 0, "bounded " + action.Kind); return command;
            }
            var dealer = session.Engine.Configuration().Layout!.CivicObjects.Single(c => c.Kind == "dealership");
            var grain = session.Engine.Configuration().ResourceNodes.First(n => n.ItemId == "grain" && !n.Rules!.Public);
            Go(new(ActionKind.MoveTo, Target: grain.Position)); Go(new(ActionKind.BuyResource, Target: grain.Position));
            Go(new(ActionKind.MoveTo, Target: dealer.Entry));
            var field = session.Engine.InspectFleet()!.Stock.Single(v => v.ModelId == "extractor_field_1");
            Go(new(ActionKind.BuyVehicle, VehicleId: field.Id));
            int wait = 12; while (session.Engine.InspectFleet()!.Vehicles.All(v => v.Id != field.Id) && wait-- > 0) session.Step();
            Check(session.Engine.InspectFleet()!.Vehicles.Any(v => v.Id == field.Id), "field harvester purchased");
            Go(new(ActionKind.MoveTo, Target: session.Engine.Inspect().Inventories.Single(i => i.Id == session.Engine.InspectFleet()!.Vehicles.Single(v => v.Id == field.Id).CargoInventory).Position));
            Go(new(ActionKind.BoardVehicle, VehicleId: field.Id)); Go(new(ActionKind.MoveTo, Target: grain.Position));
            Go(new(ActionKind.PlaceEquipment, Equipment: new(field.Id), ItemId: "grain"));
            Call(app, "Refresh"); await Frames(2); await PressContext(app, field.Id); await Frames(20);
            var panel = All(app).OfType<TabletPanel>().Single(p => p.Name == "GameTablet");
            float width = GetWindow().Size.X; Check(panel.SideDock && panel.Size.X / width is >= 0.34f and <= 0.42f, "side panel is about 38% at 1100");
            Check(width - panel.GetGlobalRect().End.X > width * 0.5f, "world stays visible beside the panel");
            var stage = Field<PresentationStage>(app, "_stage"); var focus = stage.FocusScreenPosition();
            Check(focus is not null && focus.Value.X / 1100f is >= 0.65f and <= 0.80f, "subject sits in the free area");
            string card = string.Join("\n", All(panel).Select(n => n is Label l ? l.Text : n is Button b ? b.Text : ""));
            Check(card.Contains("Полевой комбайн") && card.Contains("Улучшить") && card.Contains("Улучшение техники") && !card.Contains("Улучшить до уровня"), "upgrade is a compact card");
            Check(card.Contains("Начать добычу") && card.Contains("Личный") && card.Contains("Авто"), "work actions are short");
            await Shot("01-side-tablet");
            var scroll = All(panel).OfType<ScrollContainer>().First(s => s.Name == "ExtractionScroll");
            scroll.EnsureControlVisible(All(panel).OfType<Button>().First(b => b.Name.ToString().Contains("I4Object_upgrade_")));
            scroll.ScrollVertical += 32; await Frames(3); await Shot("02-upgrade");
            Press(app, session, "I4Object_start_");
            Advance(session, 4); Call(app, "Refresh");
            var storage = session.Engine.Observe(new(1)).OwnEquipment.Single(e => e.Id.Value == field.Id);
            var cargo = session.Engine.Inspect().Inventories.Single(i => i.Id == storage.StorageId);
            Check(cargo.Stock.Sum(s => s.Quantity) > 0, "harvest reached the cargo");
            Press(app, session, "I4Object_stop_"); Check(session.Engine.Observe(new(1)).OwnEquipment.Single(e => e.Id.Value == field.Id).Mode == EquipmentMode.Off, "stop accepted");
            int idle = 40; while (session.Engine.Observe(new(1)).Participant.Busy && idle-- > 0) Advance(session);
            Check(idle > 0, "equipment is idle"); Call(app, "Refresh");
            long before = session.Engine.Inspect().Inventories.Single(i => i.Id == storage.StorageId).Stock.Sum(s => s.Quantity);
            Press(app, session, "ExtractionTakeAll");
            int moved = 30; long after = before;
            while (moved-- > 0 && after >= before) { Advance(session); after = session.Engine.Inspect().Inventories.Single(i => i.Id == storage.StorageId).Stock.Sum(s => s.Quantity); }
            Check(after < before, "take uses the existing transfer");
            idle = 40; while (session.Engine.Observe(new(1)).Participant.Busy && idle-- > 0) Advance(session);
            Call(app, "Refresh");
            Press(app, session, "I4Object_upgrade_");
            Check(session.Engine.Observe(new(1)).OwnEquipment.Single(e => e.Id.Value == field.Id).Level == 2, "upgrade accepted");
            Call(app, "Refresh"); string upgraded = string.Join("\n", All(panel).OfType<Label>().Select(l => l.Text));
            Check(upgraded.Contains("Максимальный уровень") && !All(panel).OfType<Button>().Any(b => b.Name.ToString().Contains("I4Object_upgrade_")), "maximum level has no dead button");
            All(panel).OfType<Button>().First(b => b.Name == "TabletClose").EmitSignal(BaseButton.SignalName.Pressed); await Frames(20);
            focus = stage.FocusScreenPosition(); Check(focus is not null && focus.Value.X / 1100f is >= 0.45f and <= 0.55f, "camera returns to the usual frame");
            Call(app, "OpenPlayerTablet"); Field<PlayerTablet>(app, "_playerTablet").OpenSection("dealer"); Call(app, "Refresh"); await Frames(6);
            var dealerPanel = All(app).OfType<FleetPanel>().Single();
            string catalog = string.Join("\n", All(dealerPanel).Select(n => n is Label l ? l.Text : n is Button b ? b.Text : ""));
            Check(catalog.Contains("Полевой комбайн") && catalog.Contains("Лесозаготовительная машина") && catalog.Contains("Молочная машина") && catalog.Contains("Горная машина"), "catalog names four classes");
            Check(catalog.Contains("Для покупки подойдите к этой машине на стоянке.") && catalog.Contains("Показать на карте"), "location hint uses the city map");
            var fleetScroll = All(app).OfType<ScrollContainer>().First(s => s.Name == "PlayerPage_fleet");
            fleetScroll.ScrollVertical = 0; await Frames(2);
            var theme = CompactTabletDesign.Theme; var cardPaper = new Color("f8f7f2"); var pressed = new Color("49735f"); var disabled = new Color("eceee8"); var hover = new Color("e8eee6");
            RequireContrast(theme.GetColor("font_color", "Button"), cardPaper, "button");
            RequireContrast(theme.GetColor("font_pressed_color", "Button"), pressed, "pressed");
            RequireContrast(theme.GetColor("font_hover_pressed_color", "Button"), pressed, "hover pressed");
            RequireContrast(theme.GetColor("font_disabled_color", "Button"), disabled, "disabled");
            RequireContrast(theme.GetColor("font_hover_color", "Button"), hover, "hover");
            RequireContrast(theme.GetColor("font_color", "Label"), cardPaper, "label");
            var price = All(dealerPanel).OfType<Label>().First(l => l.Text.Contains("монет"));
            RequireContrast(price.GetThemeColor("font_color"), cardPaper, "price");
            RequireContrast(new Color("556860"), cardPaper, "secondary");
            await Shot("03-dealer");
            var image = GetViewport().GetTexture().GetImage(); int dark = 0, pale = 0;
            for (int y = 90; y < image.GetHeight() - 30; y += 2) for (int x = image.GetWidth() / 4; x < image.GetWidth() - 8; x += 2)
            { var pixel = image.GetPixel(x, y); float lum = 0.2126f * pixel.R + 0.7152f * pixel.G + 0.0722f * pixel.B; if (lum < 0.45f) dark++; else if (lum is > 0.60f and < 0.85f && Mathf.Abs(pixel.R - pixel.G) < 0.06f && Mathf.Abs(pixel.G - pixel.B) < 0.06f) pale++; }
            Check(dark > 300 && dark > pale, "dealer image is not pale-on-pale: dark " + dark + " pale " + pale);
            Call(app, "ChangeLanguage"); await Frames(6);
            Field<PlayerTablet>(app, "_playerTablet").OpenSection("dealer"); Call(app, "Refresh"); await Frames(4);
            string english = string.Join("\n", All(app).Select(n => n is Label l ? l.Text : n is Button b ? b.Text : ""));
            Check(english.Contains("Field harvester") && english.Contains("Show on map") && english.Contains("To buy, walk up to this vehicle on the lot."), "dealer is localized");
            All(app).OfType<Button>().First(b => b.Name == "TabletClose").EmitSignal(BaseButton.SignalName.Pressed); await Frames(2);
            Call(app, "ChangeLanguage"); await Frames(6);
            Call(app, "OpenObjectTablet", "equipment", field.Id); await Frames(8);
            GetWindow().Size = new(960, 440); GetWindow().ContentScaleSize = GetWindow().Size; await Frames(8);
            panel = All(app).OfType<TabletPanel>().Single(p => p.SideDock);
            Check(GetWindow().Size.X - panel.GetGlobalRect().End.X > 80, "phone landscape keeps world visible");
            var close = All(panel).OfType<Button>().First(b => b.Name == "TabletClose").GetGlobalRect();
            Check(close.Position.X >= 0 && close.End.X <= GetWindow().Size.X + 1 && close.Position.Y >= 0 && close.End.Y <= GetWindow().Size.Y + 1, "close stays on screen at 960");
            await Shot("04-side-phone");
            GetWindow().Size = new(800, 360); GetWindow().ContentScaleSize = GetWindow().Size; await Frames(8);
            panel = All(app).OfType<TabletPanel>().Single(p => p.SideDock); var bounds = panel.GetGlobalRect();
            Check(bounds.End.X < GetWindow().Size.X - 40 && bounds.Position.X >= -1, "800x360 does not cover the world");
            close = All(panel).OfType<Button>().First(b => b.Name == "TabletClose").GetGlobalRect();
            Check(close.End.X <= GetWindow().Size.X + 1 && close.End.Y <= GetWindow().Size.Y + 1, "close stays on screen at 800");
            All(panel).OfType<Button>().First(b => b.Name == "TabletClose").EmitSignal(BaseButton.SignalName.Pressed); await Frames(4);
            Go(new(ActionKind.PackEquipment, Equipment: new(field.Id))); Go(new(ActionKind.BoardVehicle, VehicleId: field.Id));
            Call(app, "OpenObjectTablet", "equipment", field.Id); await Frames(40);
            focus = stage.FocusScreenPosition();
            float frameWidth = stage.GetViewport().GetVisibleRect().Size.X;
            float driven = focus is null || frameWidth < 2 ? -1 : focus.Value.X / frameWidth;
            Check(driven is >= 0.60f and <= 0.90f, "driven vehicle stays in frame " + driven.ToString("0.00") + " of " + frameWidth.ToString("0"));
            session.Engine.ValidateInvariants();
            System.IO.File.WriteAllText(_output + "/native.json", System.Text.Json.JsonSerializer.Serialize(new { status = "succeeded", checks = _checks }));
    }
}
