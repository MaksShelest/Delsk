using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;
using System.Text.Json;

/// <summary>Изолированный native smoke экономической карты. В обычный клиент не входит.
/// Предприятия подготовлены на настоящей Малой долине, как в проверке I6-C2.
/// Прогресс партии, груз и поставка появляются только обычными командами.</summary>
public partial class I6C5Probe : Control
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private Playfield _app = null!;
    private GameSession _session = null!;
    private string _shots = "";
    private string _output = "";
    private readonly List<string> _checks = [];
    private static T Field<T>(object o, string key) => (T)o.GetType().GetField(key, Flags)!.GetValue(o)!;
    private static void Set(object o, string key, object value) => o.GetType().GetField(key, Flags)!.SetValue(o, value);
    private static object? Call(object o, string key, params object?[] args) => o.GetType().GetMethod(key, Flags)!.Invoke(o, args);
    private static IEnumerable<Node> All(Node root) { yield return root; foreach (var c in root.GetChildren()) foreach (var n in All(c)) yield return n; }
    private T Named<T>(string name) where T : Node => All(_app).OfType<T>().First(n => n.Name == name && (n is not Control c || c.IsVisibleInTree()));
    private PlayerView ViewOf(long actor) => PlayerDesk.Read(_session.Engine, new(actor));
    private PlayerView View => ViewOf(1);
    private void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); _checks.Add(text); GD.Print("I6C5 " + text); }
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
    private async Task Click(string name) => await Press(Named<Button>(name));
    private void StepWorld()
    {
        if (_session.Engine.WaitingForBallots && View.Elections.NextElectionId is { } election)
            foreach (var person in _session.Engine.Configuration().Participants)
                _session.Send(new(person.Id), new(ActionKind.SetVoteChoice, Election: election, Candidate: new(1)));
        _session.Step();
    }
    private void Command(GameAction action, long actor = 1)
    {
        _session.SetPaused(false); string id = _session.Send(new(actor), action); StepWorld();
        var result = _session.Engine.Result(id);
        Check(result?.Status == ResultStatus.Accepted, "Core " + actor + " " + action.Kind + " " + result?.ReasonCode);
        int limit = 800; while (ViewOf(actor).Participant.Busy && limit-- > 0) StepWorld();
        Check(limit > 0, "bounded real movement/work"); _session.SetPaused(true); Refresh();
    }
    private void Move(Cell at, long actor = 1) { if (ViewOf(actor).Participant.Position != at) Command(new(ActionKind.MoveTo, Target: at), actor); }
    private void Until(Func<bool> done, int bound, string why) { while (!done() && bound-- > 0) StepWorld(); Check(bound > 0 && done(), why); }
    private long Stock(long actor, string item) => ViewOf(actor).Inventories.Single(i => i.Id == ViewOf(actor).Participant.InventoryId).Stock.Where(s => s.ItemId == item).Sum(s => s.Quantity);

    private void PrepareValley()
    {
        var live = _session.Engine.Configuration();
        Check(live.Id == PlayMaps.CompactValleyScenarioId, "ordinary start is the current Small valley");
        var lots = live.Layout!.IndustrialLots;
        var first = lots[0]; var second = lots[1];
        var owned = first.Cells.Concat(second.Cells).ToHashSet();
        static Cell Anchor(IndustrialLot lot) => new(lot.Cells.Min(c => c.X), lot.Cells.Min(c => c.Z));
        var npc = live.Plots!.First(p => p.OwnerId is null && !p.IsCorridor && !p.IsShopSite && !owned.Contains(p.Position));
        var prepared = live with
        {
            Participants = live.Participants.Select(p => p.Id is 1 or 2 ? p with { CashMinor = 1000000 } : p).ToArray(),
            Plots = live.Plots!.Select(p => owned.Contains(p.Position) ? p with { OwnerId = 1L } : p.Id == npc.Id ? p with { OwnerId = 3L } : p).ToArray(),
            Buildings = [new BuildingDefinition(8, 1, Anchor(first), "woodworking"), new BuildingDefinition(9, 1, Anchor(second), "woodworking")]
        };
        var session = new GameSession(new SimulationEngine(prepared));
        Set(_app, "_scenario", prepared); Set(_app, "_session", session);
        Set(_app, "_scheduler", new TickScheduler(session));
        _session = session; _session.SetPaused(true);
        Call(_app, "RebuildChrome");
        Check(View.Enterprises.Count(e => e.OwnerId.Value == 1) == 2, "two real woodworking plants exist");
        Check(View.Plots.Any(p => p.OwnerId?.Value == 3), "an NPC lot keeps its owner");
    }

    private void WorkTheEconomy()
    {
        Until(() => View.CapitalOffers.Any(o => o.ItemId == "wood" && o.RemainingQuantity >= 2 && o.CreatedTick < _session.Engine.Tick), 180,
            "a live wood order exists: " + string.Join(",", View.CapitalOffers.Select(o => o.ItemId + "×" + o.RemainingQuantity)));
        var offer = View.CapitalOffers.First(o => o.ItemId == "wood" && o.RemainingQuantity >= 2 && o.CreatedTick < _session.Engine.Tick);
        Command(new(ActionKind.AcceptDeliveryQuantity, OfferId: offer.Id, OfferWindow: offer.Window, Quantity: 2));
        Until(() => (View.Contracts ?? []).Any(c => c.ItemId == "wood" && c.ClosedTick is null), 40, "the delivery contract is open");
        var contract = View.Contracts!.Single(c => c.ItemId == "wood" && c.ClosedTick is null);
        var wood = _session.Engine.Configuration().ResourceNodes.First(n => n.ItemId == "wood" && n.Rules!.Public);
        Move(wood.Position); Command(new(ActionKind.Harvest, ItemId: "wood", Quantity: 8));
        Check(Stock(1, "wood") >= 8, "harvested wood is in the backpack");
        Move(contract.Position);
        Command(new(ActionKind.DeliverContract, ContractId: contract.Id, Quantity: 1));
        var delivered = View.Contracts!.Single(c => c.Id == contract.Id);
        Check(delivered.DeliveredQuantity == 1 && delivered.Quantity > delivered.DeliveredQuantity, "delivery progress is partial and authoritative");
        var running = View.Buildings.Single(b => b.Id.Value == 8);
        Move(running.Position);
        Command(new(ActionKind.Transfer, Source: View.Participant.InventoryId, Destination: running.InputId, ItemId: "wood", Quantity: 4));
        var fleet = _session.Engine.FleetOf(new(1))!;
        var listing = fleet.Stock.First(s => fleet.Models.First(m => m.Id == s.ModelId).EquipmentTypeId is null);
        Move(fleet.Dealer); Command(new(ActionKind.BuyVehicle, VehicleId: listing.Id));
        _session.AdvanceTo(_session.Engine.Tick + 5); Refresh();
        var car = _session.Engine.InspectFleet()!.Vehicles.Single(v => v.Id == listing.Id);
        var parked = View.Fleet!.Vehicles.Single(v => v.Vehicle.Id == car.Id);
        Move(parked.Position);
        Command(new(ActionKind.Transfer, Source: View.Participant.InventoryId, Destination: car.CargoInventory, ItemId: "wood", Quantity: 2));
        var cargo = View.Inventories.Single(i => i.Id == car.CargoInventory);
        Check(cargo.OccupiedVolume > 0 && cargo.OccupiedVolume <= cargo.Capacity, "vehicle cargo has a real fill");
        Command(new(ActionKind.SetProductionEnabled, Enterprise: new(8), Enabled: true));
        Until(() => View.Enterprises.Single(e => e.Id.Value == 8) is { BatchId: > 0, WorkRequired: > 0 } e && e.WorkDone > 0 && e.WorkDone < e.WorkRequired, 500, "the running plant has an unfinished batch");
        var stopped = View.Enterprises.Single(e => e.Id.Value == 9);
        Check(!stopped.Enabled && stopped.BatchId == 0, "the second plant stays stopped without a batch");
        var free = View.Plots.First(p => p.OwnerId is null && p.ReservedBy is null && !p.IsCorridor && !p.IsShopSite);
        Move(free.Position, 2); Command(new(ActionKind.ReservePlot, Plot: free.Id), 2);
        Check(View.Plots.Single(p => p.Id == free.Id).ReservedBy?.Value == 2, "a plot reservation is real");
    }

    private static void Look(CityMapCanvas canvas, double x, double z, double zoom)
    {
        canvas.State.Fit();
        canvas.State.ZoomAt(zoom, Math.Max(1, canvas.Size.X / 2), Math.Max(1, canvas.Size.Y / 2), Math.Max(1, canvas.Size.X), Math.Max(1, canvas.Size.Y));
        canvas.State.Center(x, z); canvas.QueueRedraw();
    }

    private async Task MapShot(string focus, string file)
    {
        if (focus.Length > 0) await Click("MapFocus" + focus);
        await Shot(file);
    }

    public override async void _Ready()
    {
        try
        {
            MouseFilter = MouseFilterEnum.Ignore;
            var args = OS.GetCmdlineUserArgs();
            string root = args[Array.IndexOf(args, "--repository") + 1];
            _output = args[Array.IndexOf(args, "--output") + 1];
            _shots = root + "/docs/verification/i6c5";
            Directory.CreateDirectory(_shots); Directory.CreateDirectory(_output);
            GetWindow().Size = new(1100, 720); GetWindow().ContentScaleSize = GetWindow().Size;
            _app = GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();
            AddChild(_app); await Frames(10); _app.SetProcess(false);
            Set(_app, "_language", "ru");
            Call(_app, "ShowMaps"); await Frames();
            await Click("ChooseMap"); await Click("SelectMap_compact-valley");
            await Click("StartGame"); await Frames(8); _app.SetProcess(false);
            _session = Field<GameSession>(_app, "_session"); _session.SetPaused(true); Refresh();
            PrepareValley(); await Frames(4);
            WorkTheEconomy(); Refresh(); await Frames(2);
            Call(_app, "OpenCityMap"); await Frames(4);
            var panel = Field<CityMapPanel>(_app, "_cityMapPanel");
            Check(panel.Name == "FullCityMap", "the existing city map is open");
            await Click("MapFit"); await Frames(2); await MapShot("", "01-overview");
            var canvasEarly = panel.Canvas; var plantEarly = View.Buildings.Single(b => b.Id.Value == 8);
            await PressAt(canvasEarly, canvasEarly.Point(plantEarly.Position));
            var prefs = Field<CityMapPreferences>(_app, "_cityMapPreferences");
            Check(prefs.SelectedKey.Contains("production:8:"), "the plant stays selected");
            string plantKey = prefs.SelectedKey;
            var other = View.Buildings.Single(b => b.Id.Value == 9);
            Look(canvasEarly, (plantEarly.Position.X + other.Position.X) / 2d + 1, (plantEarly.Position.Z + other.Position.Z) / 2d + 1, 2);
            await Frames(2);
            await MapShot("Production", "02-production");
            var frame = CityMap.Read(Presentation.Read(_session.Engine.Configuration(), _session.Engine, 1), View);
            var truck = frame.Objects.Single(o => o.Kind == "vehicle" && o.Owner == 1);
            prefs.SelectedKey = truck.Key; Look(canvasEarly, truck.Entry.X + .5, truck.Entry.Z + .5, 2.4);
            await MapShot("Transport", "03-transport");
            var order = frame.Objects.Single(o => o.Kind is "incoming" or "outgoing");
            prefs.SelectedKey = order.Key; Look(canvasEarly, order.Entry.X + .5, order.Entry.Z + .5, 2.4);
            await MapShot("Orders", "04-orders");
            prefs.SelectedKey = plantKey; canvasEarly.Fit(); await Frames(2);
            await MapShot("Property", "05-property");
            Look(canvasEarly, plantEarly.Position.X + 1.5, plantEarly.Position.Z + 1.5, 2);
            var canvas = panel.Canvas;
            await Shot("06-selection");
            await Click("MapShowRoute");
            Check(Field<Cell[]>(canvas, "_preview").Length > 1, "the movement guide is drawn");
            await Shot("07-route");
            double zoom = canvas.State.Zoom; var center = (canvas.State.CenterX, canvas.State.CenterZ); var layers = canvas.State.Layers; var focus = canvas.State.Focus;
            await Click("MapOpenObject"); await Frames(4);
            Check(Field<TabletPanel>(_app, "_tabletPanel") is not null, "the existing card opened from the map");
            Call(_app, "OpenCityMap"); await Frames(3);
            panel = Field<CityMapPanel>(_app, "_cityMapPanel");
            Check(panel.Canvas.State.Zoom == zoom && panel.Canvas.State.CenterX == center.CenterX && panel.Canvas.State.Focus == focus && panel.Canvas.State.Layers == layers, "zoom, camera and filters survive the card");
            GetWindow().Size = new(800, 360); GetWindow().ContentScaleSize = GetWindow().Size; await Frames(4);
            if (All(_app).OfType<Button>().Any(b => b.Name == "MapDetailsToggle" && b.IsVisibleInTree())) await Click("MapDetailsToggle");
            await Shot("08-compact");
            string key = Field<CityMapPreferences>(_app, "_cityMapPreferences").SelectedKey;
            _session.Step(); Refresh(); await Frames(2);
            Check(Field<CityMapPreferences>(_app, "_cityMapPreferences").SelectedKey == key, "a tick does not move the selection");
            var running = View.Enterprises.Single(e => e.Id.Value == 8);
            await File.WriteAllTextAsync(_output + "/native.json", JsonSerializer.Serialize(new
            {
                status = "succeeded",
                map = _session.Engine.Configuration().Id,
                batch = new { running.WorkDone, running.WorkRequired, running.CompletedBatches, running.ReasonCode },
                delivered = View.Contracts!.Single(c => c.ItemId == "wood").DeliveredQuantity,
                checks = _checks,
                scope = "Linux software GL and synthetic input. Prepared cash and two initial plants on the ordinary Small valley, then real harvest, transfer, purchase, delivery and production. Not a physical device measurement."
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

    private async Task PressAt(Control control, Vector2 local)
    {
        var p = control.GetGlobalTransform() * local; int w = GetWindow().GetWindowId();
        Input.ParseInputEvent(new InputEventMouseMotion { WindowId = w, Position = p, GlobalPosition = p });
        Input.ParseInputEvent(new InputEventMouseButton { WindowId = w, Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = true }); await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton { WindowId = w, Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = false }); await Frames();
    }
}
