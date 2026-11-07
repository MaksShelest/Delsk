using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

/// <summary>Изолированный native smoke простого и расширенного интерфейса.
/// Кадры снимает настоящий Godot. Linux software GL не заменяет Windows или Android.</summary>
public partial class I6C7Probe : Control
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private Playfield _app = null!;
    private GameSession _session = null!;
    private string _shots = "";
    private string _output = "";
    private readonly List<string> _checks = [];
    private readonly Dictionary<string, string> _notes = [];
    private static T Field<T>(object o, string key) => (T)o.GetType().GetField(key, Flags)!.GetValue(o)!;
    private static void Set(object o, string key, object? value) => o.GetType().GetField(key, Flags)!.SetValue(o, value);
    private static object? Call(object o, string key, params object?[] args) => o.GetType().GetMethod(key, Flags)!.Invoke(o, args);
    private static IEnumerable<Node> All(Node root) { yield return root; foreach (var c in root.GetChildren()) foreach (var n in All(c)) yield return n; }
    private T Named<T>(string name) where T : Node => All(_app).OfType<T>().First(n => n.Name == name && (n is not Control c || c.IsVisibleInTree()));
    private PlayerView View => PlayerDesk.Read(_session.Engine, new(1));
    private void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); if (_checks.Count == 0 || _checks[^1] != text) { _checks.Add(text); GD.Print("I6C7 " + text); } }
    private async Task Frames(int n = 3) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Shot(string name)
    {
        await Frames();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Check(GetViewport().GetTexture().GetImage().SavePng(_shots + "/" + name + ".png") == Error.Ok, "capture " + name);
    }
    private void Refresh() => Call(_app, "Refresh");
    private async Task Press(Control control)
    {
        UiNavigation.Reveal(control); await Frames();
        Check(control is not BaseButton button || !button.Disabled, "enabled " + control.Name);
        var p = control.GetGlobalRect().GetCenter(); int w = GetWindow().GetWindowId();
        Input.ParseInputEvent(new InputEventMouseMotion { WindowId = w, Position = p, GlobalPosition = p });
        Input.ParseInputEvent(new InputEventMouseButton { WindowId = w, Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = true }); await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton { WindowId = w, Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = false }); await Frames();
    }
    private void StepWorld()
    {
        if (_session.Engine.WaitingForBallots && View.Elections.NextElectionId is { } election)
            foreach (var person in _session.Engine.Configuration().Participants)
                _session.Send(new(person.Id), new(ActionKind.SetVoteChoice, Election: election, Candidate: new(1)));
        _session.Step();
    }
    private string TryCommand(GameAction action)
    {
        _session.SetPaused(false);
        string id = _session.Send(new(1), action);
        StepWorld();
        var result = _session.Engine.Result(id);
        int limit = 2500;
        while (View.Participant.Busy && limit-- > 0) StepWorld();
        _session.SetPaused(true); Refresh();
        return (limit > 0 ? result?.Status.ToString() : "busy-timeout") + " " + result?.ReasonCode;
    }
    private void Command(GameAction action)
    {
        string status = TryCommand(action);
        Check(status.StartsWith("Accepted", StringComparison.Ordinal), "Core " + action.Kind + " " + status);
    }
    private async Task Resize(int width, int height)
    {
        GetWindow().Size = new(width, height);
        GetWindow().ContentScaleSize = GetWindow().Size;
        await Frames(4);
        if (_app is not null)
        {
            Refresh();
            // Подпись часов меняет собственный минимум кнопки. Размер появляется на следующем кадре.
            await Frames(2);
        }
    }
    private void Inside(string name)
    {
        var control = Named<Control>(name);
        var view = GetViewportRect();
        Check(view.Encloses(control.GetGlobalRect()) && control.Size.X >= 28 && control.Size.Y >= 28, name + " fully visible " + (int)view.Size.X + "x" + (int)view.Size.Y);
        if (control is Button button && button.Text.Length > 0)
        {
            // Свой минимум кнопки уже включает текст и поля темы. Сравнение с голой
            // шириной строки ложно ругает глифы вроде ◷, которые шрифт темы рисует уже.
            float minimum = button.GetCombinedMinimumSize().X;
            Check(button.ClipText == false && button.Size.X + 1 >= minimum, name + " keeps its label " + button.Text + " " + (int)button.Size.X + "/" + (int)minimum);
        }
    }

    public override async void _Ready()
    {
        try
        {
            MouseFilter = MouseFilterEnum.Ignore;
            var args = OS.GetCmdlineUserArgs();
            string root = args[Array.IndexOf(args, "--repository") + 1];
            _output = args[Array.IndexOf(args, "--output") + 1];
            _shots = root + "/docs/verification/i6c7/after";
            Directory.CreateDirectory(_shots); Directory.CreateDirectory(_output);
            string staleFailure = Path.Combine(_shots, "failure.png");
            if (File.Exists(staleFailure)) File.Delete(staleFailure);
            await Resize(1100, 720);
            _app = GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();
            AddChild(_app); await Frames(8); _app.SetProcess(false);
            Call(_app, "ShowMaps"); await Frames();
            await Press(Named<Button>("ChooseMap")); await Press(Named<Button>("SelectMap_compact-valley"));
            await Press(Named<Button>("StartGame")); await Frames(6); _app.SetProcess(false);
            _session = Field<GameSession>(_app, "_session"); _session.SetPaused(true); Refresh();
            await SimpleHud();
            await TabletPages();
            await Advanced();
            await Language();
            await Narrow();
            await PlayChain();
            await File.WriteAllTextAsync(_output + "/native.json", JsonSerializer.Serialize(new
            {
                status = "succeeded", checks = _checks, notes = _notes,
                scope = "Linux software GL and synthetic input. One ordinary Small valley. Full unassisted factory playthrough is not claimed. Not a physical Windows or Android device."
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

    private async Task SimpleHud()
    {
        Check(Field<bool>(_app, "_friendly"), "simple mode is the default");
        Check(Named<Button>("OpenCityMap").Visible && Named<Button>("HudTablet").Text.Contains("Хозяйство", StringComparison.Ordinal), "map and business are the simple navigation");
        Check(!All(_app).OfType<Button>().Any(b => b.Name == "HudPlots" && b.IsVisibleInTree()), "plot outlines left the simple HUD");
        Check(!All(_app).OfType<Button>().Any(b => b.Name == "HudHelp" && b.IsVisibleInTree()), "help left the simple HUD and stays in the menu");
        Check(Named<Label>("StarterGoalTitle").Text.Length > 0, "the current goal is visible");
        await Shot("01-hud-simple-1100");
        await Shot("02-goal");
        await Press(Named<Button>("StarterGoalWhere")); await Frames(4);
        Check(Field<TabletState>(_app, "_tabletState").Kind == TabletKind.CityMap, "show-where opens the existing city map");
        await Shot("03-show-where");
        Call(_app, "CloseTablet"); await Frames(2);
        Inside("OpenCityMap"); Inside("HudTablet"); Inside("HudMenu"); Inside("AccountNotification"); Inside("HudClock");
    }

    private async Task TabletPages()
    {
        await Press(Named<Button>("HudTablet")); await Frames(3);
        var tablet = Field<PlayerTablet>(_app, "_playerTablet");
        Check(Named<Button>("PlayerSell").IsVisibleInTree(), "sell stays on the actions path");
        await Shot("04-household");
        tablet.OpenSection("quests"); await Frames(2); await Shot("05-actions");
        tablet.OpenSection("negotiations"); await Frames(2);
        Check(Named<Button>("PlayerNav_negotiations").ButtonPressed, "negotiations remain reachable");
        await Shot("06-more-negotiations");
        tablet.OpenSection("account"); await Frames(2); await Shot("07-account");
        tablet.OpenSection("inventory"); await Frames(2);
        var quantity = Named<SpinBox>("BackpackTransferQuantity");
        quantity.Value = 4;
        tablet.OpenSection("overview"); await Frames(2);
        tablet.OpenSection("inventory"); await Frames(2);
        Check(Math.Abs(Named<SpinBox>("BackpackTransferQuantity").Value - 4) < .01, "entered quantity survives the section change");
        Call(_app, "CloseTablet"); await Frames(2);
    }

    private async Task Advanced()
    {
        await Press(Named<Button>("HudMenu")); await Frames(2);
        await Press(Named<Button>("InterfaceAdvanced")); await Frames(2);
        await Press(All(_app).OfType<Button>().First(b => b.IsVisibleInTree() && b.Text == "Продолжить")); await Frames(3);
        Check(!Field<bool>(_app, "_friendly"), "advanced mode is on");
        Check(Named<Button>("HudPlots").IsVisibleInTree() && Named<Button>("HudHelp").IsVisibleInTree(), "advanced HUD keeps plots and help");
        Check(Named<Button>("HudTablet").Text.Contains("Планшет", StringComparison.Ordinal), "advanced tablet title is restored");
        await Shot("08-hud-advanced-1100");
        await Press(Named<Button>("HudTablet")); await Frames(2);
        Check(Named<Button>("PlayerGroup_economy").IsVisibleInTree() && Named<Button>("PlayerNav_negotiations").IsVisibleInTree(), "advanced groups keep the old sections");
        await Shot("09-advanced-tablet");
        Call(_app, "CloseTablet"); await Frames();
        await Press(Named<Button>("HudMenu")); await Frames();
        await Press(Named<Button>("InterfaceAdvanced")); await Frames();
        await Press(All(_app).OfType<Button>().First(b => b.IsVisibleInTree() && b.Text == "Продолжить")); await Frames(2);
        Check(Field<bool>(_app, "_friendly"), "simple mode returns immediately");
    }

    private async Task Language()
    {
        Set(_app, "_language", "en");
        Call(_app, "RebuildChrome"); await Frames(6); _app.SetProcess(false); Refresh();
        Check(Named<Button>("HudTablet").Text.Contains("Business", StringComparison.Ordinal), "English business label");
        Check(Named<Label>("StarterGoalTitle").Text.Length > 0, "English goal");
        await Shot("10-hud-en");
        Set(_app, "_language", "ru");
        Call(_app, "RebuildChrome"); await Frames(6); _app.SetProcess(false); Refresh();
    }

    private async Task Narrow()
    {
        foreach (var (name, w, h) in new[] { ("800", 800, 480), ("640", 640, 360), ("360", 360, 640), ("landscape", 960, 440) })
        {
            await Resize(w, h);
            Inside("OpenCityMap"); Inside("HudTablet"); Inside("HudMenu"); Inside("AccountNotification"); Inside("HudClock");
            await Shot("11-hud-" + name);
        }
        await Resize(1100, 720);
    }

    private async Task PlayChain()
    {
        var scenario = _session.Engine.Configuration();
        var view = View;
        var anchors = (scenario.Layout?.IndustrialLots ?? []).Select(l => l.Cells.OrderBy(c => c.Z).ThenBy(c => c.X).First()).ToHashSet();
        var free = view.Plots.Where(p => p.OwnerId is null && p.ReservedBy is null && !p.IsCorridor && !p.IsShopSite && p.ExpansionFor is null).OrderBy(p => p.PriceMinor).ThenBy(p => p.Id.Value).ToArray();
        var cheapest = free[0];
        var anchor = free.FirstOrDefault(p => anchors.Contains(p.Position)) ?? cheapest;
        var type = (scenario.BuildingTypes ?? []).Where(t => t.Kind == BuildingKind.Production).OrderBy(t => t.Levels.Single(l => l.Level == 1).PriceMinor).First();
        var level = type.Levels.Single(l => l.Level == 1);
        _notes["cash"] = view.AvailableCashMinor.ToString(CultureInfo.InvariantCulture);
        _notes["cheapestPlot"] = cheapest.PriceMinor.ToString(CultureInfo.InvariantCulture);
        _notes["anchorPlot"] = anchor.PriceMinor.ToString(CultureInfo.InvariantCulture);
        _notes["catalogBuilding"] = type.Id + " " + level.PriceMinor.ToString(CultureInfo.InvariantCulture);
        _notes["catalogMaterials"] = string.Join(", ", level.Materials.Select(m => m.ItemId + "x" + m.Quantity));
        var site = view.Resources.Where(r => r.Public && r.Stock > r.Reserved).OrderBy(r => Math.Abs(r.Position.X - view.Participant.Position.X) + Math.Abs(r.Position.Z - view.Participant.Position.Z)).First();
        Command(new(ActionKind.MoveTo, Target: site.Position));
        Refresh(); await Frames(2);
        var harvest = All(_app).OfType<Button>().First(b => b.IsVisibleInTree() && b.Text.Contains("Добыть", StringComparison.Ordinal));
        Check(harvest.Text.Contains(PlayerText.Item("ru", site.ItemId), StringComparison.Ordinal), "context names the real resource");
        await Shot("12-harvest-context");
        Command(PlayerDesk.Harvest(site.ItemId, 1));
        Check(View.Inventories.Single(i => i.Id == View.Participant.InventoryId).Stock.Any(s => s.ItemId == site.ItemId && s.Quantity > 0), "harvest put a real unit in the backpack");
        await Shot("13-after-harvest");
        var offer = View.CapitalOffers.Where(o => o.ItemId == site.ItemId && o.RemainingQuantity > 0).OrderBy(o => o.Id).FirstOrDefault();
        if (offer is null) _notes["sell"] = "no live capital offer for " + site.ItemId;
        else
        {
            string moved = TryCommand(new(ActionKind.MoveTo, Target: offer.Position));
            string sold = moved.StartsWith("Accepted", StringComparison.Ordinal)
                ? TryCommand(new(ActionKind.SellAvailableToCapital, OfferId: offer.Id, ItemId: site.ItemId, Quantity: 1))
                : "not attempted after " + moved;
            _notes["sell"] = sold;
            await Shot("14-sell");
        }
        var plot = view.AvailableCashMinor >= anchor.PriceMinor ? anchor : cheapest;
        Check(View.AvailableCashMinor >= plot.PriceMinor, "cash still buys the selected plot");
        Command(new(ActionKind.MoveTo, Target: plot.Position));
        Command(PlayerDesk.BuyPlot(plot.Id.Value));
        Check(View.Plots.Single(p => p.Id == plot.Id).OwnerId == View.Participant.Id, "plot purchase is real");
        Call(_app, "OpenPlayerTablet"); await Frames(2);
        var pages = Field<PlayerTablet>(_app, "_playerTablet");
        pages.OpenSection("overview"); await Frames(2);
        var goal = Named<Label>("OverviewGoalTitle").Text;
        _notes["goalAfterPlot"] = goal;
        await Shot("15-goal-after-plot");
        pages.OpenHolding("ground", plot.Id.Value); await Frames(3);
        var gap = All(_app).OfType<Label>().FirstOrDefault(l => l.Name.ToString().StartsWith("BuildGap_", StringComparison.Ordinal) && l.IsVisibleInTree());
        _notes["buildGap"] = gap?.Text ?? "no production build control on this cell";
        _notes["buildCompleted"] = "false";
        await Shot("16-shortage");
        Call(_app, "CloseTablet"); await Frames();
        _notes["fullPlaythrough"] = "not confirmed: prepared commands, one harvest, plot purchase, build left at the catalog shortage";
    }
}
