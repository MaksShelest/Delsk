using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;

/// <summary>Native smoke добычи и покупки ничейных клеток. Кадры снимает Godot.
/// Стартовые деньги подготовлены. Linux software GL не заменяет физический Windows.</summary>
public partial class I6C75Probe : Control
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private string _shots = "";
    private string _output = "";
    private readonly List<string> _checks = [];
    private static T Field<T>(object o, string key) => (T)o.GetType().GetField(key, Flags)!.GetValue(o)!;
    private static void Set(object o, string key, object? value) => o.GetType().GetField(key, Flags)!.SetValue(o, value);
    private static object? Call(object o, string key, params object?[] args) => o.GetType().GetMethod(key, Flags)!.Invoke(o, args);
    private static IEnumerable<Node> All(Node n) { yield return n; foreach (Node c in n.GetChildren()) foreach (var x in All(c)) yield return x; }
    private void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); _checks.Add(text); GD.Print("I6C75 " + text); }
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
    private GameSession Boot(Playfield app)
    {
        var resolved = (OrdinaryGameResolution)Call(app, "ResolveNewGame", PlayMaps.CompactValley, 3)!;
        var funded = resolved.Scenario with { Participants = resolved.Scenario.Participants.Select(p => p.Id == 1 ? p with { CashMinor = 1_000_000 } : p).ToArray() };
        var session = new GameSession(new(funded));
        string save = typeof(Playfield).GetProperty("SavePath", Flags)!.GetValue(app)!.ToString()!;
        CheckpointStore.Save(session, save);
        Call(app, "Load"); Call(app, "ContinueGame");
        session = Field<GameSession>(app, "_session");
        Set(session, "_controllers", Array.Empty<IController>());
        return session;
    }
    private void Idle(GameSession session, GameAction action, long actor = 1)
    {
        session.SetPaused(false);
        string id = session.Send(new(actor), action); session.Step();
        var result = session.Engine.Result(id);
        Check(result?.Status == ResultStatus.Accepted, action.Kind + " " + actor + " " + result?.ReasonCode);
        int bound = 8000;
        while (session.Engine.Observe(new(actor)).Participant.Busy && bound-- > 0) session.Step();
        Check(bound > 0, "arrived " + action.Kind + " " + actor);
        session.SetPaused(true);
    }
    private static ResourceNodeDefinition Nearest(GameSession session, bool Public)
    {
        var here = session.Engine.Observe(new(1)).Participant.Position;
        return session.Engine.Configuration().ResourceNodes
            .Where(n => n.Rules!.Public == Public && n.Rules.InitialOwner is null && session.Engine.ResourceSites().Single(r => r.Position == n.Position && r.ItemId == n.ItemId).Owner is null)
            .OrderBy(n => Math.Abs(n.Position.X - here.X) + Math.Abs(n.Position.Z - here.Z)).ThenBy(n => n.ItemId, StringComparer.Ordinal).First();
    }
    private async Task Show(Playfield app, GameSession session, ResourceNodeDefinition node, string shot)
    {
        session.SetPaused(true); Call(app, "Refresh"); await Frames(3);
        Camera(app, new(node.Position.X, 0, node.Position.Z), 12);
        var buy = All(app).OfType<Button>().First(b => b.Name == "Context_resource_buy_" + node.ItemId);
        Check(buy.Text.Contains("Купить ресурсную клетку") && buy.Text.Contains("150.00"), "buy names the cell price " + buy.Text);
        buy.GrabFocus(); await Frames(3);
        var preview = Field<Cell[]>(Field<PresentationStage>(app, "_stage"), "_preview");
        Check(preview.Length == 1 && preview[0] == node.Position, "buy highlights " + node.ItemId + " " + buy.Text);
        var road = session.Engine.Configuration().Layout!.InitialCityDirtCells.First(c => c != node.Position);
        Set(app, "_hoverX", road.X); Set(app, "_hoverZ", road.Z); Call(app, "Refresh"); await Frames(2);
        preview = Field<Cell[]>(Field<PresentationStage>(app, "_stage"), "_preview");
        Check(preview.Length == 1 && preview[0] == node.Position, "hover does not replace the resource cell");
        await Shot(shot);
        buy.ReleaseFocus();
    }

    public override async void _Ready()
    {
        try
        {
            MouseFilter = MouseFilterEnum.Ignore;
            var args = OS.GetCmdlineUserArgs();
            string root = args[Array.IndexOf(args, "--repository") + 1];
            _output = args[Array.IndexOf(args, "--output") + 1];
            _shots = root + "/docs/verification/i6c75";
            Directory.CreateDirectory(_shots); Directory.CreateDirectory(_output);
            GetWindow().Size = new(1100, 720); GetWindow().ContentScaleSize = GetWindow().Size; await Frames(3);
            var app = GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();
            AddChild(app); await Frames(8); app.SetProcess(false);
            var session = Boot(app);
            var economy = session.Engine.Configuration().Economy ?? throw new InvalidOperationException("economy");
            Check(session.Engine.Configuration().EconomyProfileId == VNextResourceRightsProfile.ProfileId, "profile " + session.Engine.Configuration().EconomyProfileId);
            Check(economy.ResourceRights!.Version == 1, "resource rights 1");
            Check(economy.PlayerWageStepMinor == 2, "wage step stays 2");
            string save = typeof(Playfield).GetProperty("SavePath", Flags)!.GetValue(app)!.ToString()!;
            foreach (bool publicCell in new[] { true, false })
            {
                var node = Nearest(session, publicCell);
                var site = session.Engine.ResourceSites().Single(r => r.Position == node.Position && r.ItemId == node.ItemId);
                Check(site.PurchasePriceMinor == 15_000 && site.PurchasePriceMinor != 10_000, node.ItemId + " keeps its own price");
                Idle(session, new(ActionKind.MoveTo, Target: node.Position));
                Call(app, "Refresh"); await Frames(2);
                Check(All(app).OfType<Label>().Any(l => l.Text.Contains("ничейный")), "unowned summary");
                Check(All(app).OfType<Button>().Any(b => b.Name.ToString().StartsWith("Context_harvest_") && !b.Disabled), "harvest is offered");
                await Show(app, session, node, publicCell ? "unowned-public-ru" : "unowned-private-ru");
                session.SetPaused(false);
                string started = session.Send(new(1), new(ActionKind.StartHarvesting, ItemId: node.ItemId, Quantity: 1));
                session.Step();
                Check(session.Engine.Result(started)?.Status == ResultStatus.Accepted, "cycle started");
                session.SetPaused(true); Call(app, "Refresh"); await Frames(2);
                var waiting = All(app).OfType<Button>().First(b => b.Name == "Context_resource_buy_" + node.ItemId);
                Check(waiting.Disabled, "the buy button is inactive while the cycle runs");
                await Shot(publicCell ? "public-buy-busy-ru" : "private-buy-busy-ru");
                Idle(session, new(ActionKind.StopHarvest));
                long stock = session.Engine.ResourceSites().Single(r => r.Position == node.Position && r.ItemId == node.ItemId).Stock;
                Idle(session, new(ActionKind.BuyResource, Target: node.Position, ItemId: node.ItemId));
                var owned = session.Engine.ResourceSites().Single(r => r.Position == node.Position && r.ItemId == node.ItemId);
                Check(owned.Public == publicCell && owned.Owner == new ParticipantId(1) && owned.Stock == stock, "purchase grants the cell without adding stock");
                Call(app, "Refresh"); await Frames(2);
                Check(All(app).OfType<Label>().Any(l => l.Text.Contains("ваш ресурс")), "own summary");
                Check(!All(app).OfType<Button>().Any(b => b.Name.ToString().StartsWith("Context_resource_buy_")), "an owned cell has no buy button");
                await Shot(publicCell ? "owned-public-ru" : "owned-private-ru");
                CheckpointStore.Save(session, save);
                Call(app, "Load"); Call(app, "ContinueGame");
                session = Field<GameSession>(app, "_session");
                Set(session, "_controllers", Array.Empty<IController>());
                var loaded = session.Engine.ResourceSites().Single(r => r.Position == node.Position && r.ItemId == node.ItemId);
                Check(loaded.Public == publicCell && loaded.Owner == new ParticipantId(1), "loaded rights");
                session.Engine.ValidateInvariants();
                Call(app, "Refresh"); await Frames(3);
                Check(All(app).OfType<Label>().Any(l => l.Text.Contains("ваш ресурс")), "loaded summary");
                await Shot(publicCell ? "owned-public-loaded-ru" : "owned-private-loaded-ru");
                Idle(session, new(ActionKind.MoveTo, Target: node.Position), 2);
                session.SetPaused(false);
                string refused = session.Send(new(2), new(ActionKind.Harvest, ItemId: node.ItemId, Quantity: 1));
                session.Step();
                Check(session.Engine.Result(refused)?.ReasonCode == "NoResourceRights", "other participant cannot harvest");
                session.SetPaused(true);
                var pack = PlayerDesk.Read(session.Engine, new(1));
                long carried = pack.Inventories.Single(i => i.Id == pack.Participant.InventoryId).Stock.Where(s => s.ItemId == node.ItemId).Sum(s => s.Quantity);
                Idle(session, new(ActionKind.Harvest, ItemId: node.ItemId, Quantity: 1));
                pack = PlayerDesk.Read(session.Engine, new(1));
                Check(pack.Inventories.Single(i => i.Id == pack.Participant.InventoryId).Stock.Where(s => s.ItemId == node.ItemId).Sum(s => s.Quantity) > carried, "owner still harvests");
            }
            Call(app, "ChangeLanguage"); Call(app, "Refresh"); await Frames(3);
            Check(All(app).OfType<Label>().Any(l => l.Text.Contains("your resource")), "english ownership");
            await Shot("owned-private-en");
            session.Engine.ValidateInvariants();
            File.WriteAllText(_output + "/native.json", System.Text.Json.JsonSerializer.Serialize(new { status = "succeeded", checks = _checks }));
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
