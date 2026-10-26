using Godot;
using Simulation.Core;
using Simulation.Developer;
using Simulation.Hosting;
using System.Reflection;
using System.Text.Json;

/// <summary>Only copied into an isolated test stage; absent from shipped clients.</summary>
public partial class HudGodProbe : Control
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static T Field<T>(Playfield game, string name) =>
        (T)(typeof(Playfield).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!);
    private static void Call(Playfield game, string name, params object[] args) =>
        typeof(Playfield).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(game, args);
    private static IEnumerable<Node> All(Node root)
    {
        foreach (Node child in root.GetChildren()) { yield return child; foreach (var next in All(child)) yield return next; }
    }
    private static T Named<T>(Node root, string name) where T : Node => All(root).OfType<T>().Single(n => n.Name == name);
    private static void Press(Node root, string name) => Named<Button>(root, name).EmitSignal(BaseButton.SignalName.Pressed);
    private static string[][] Columns(ResourceInventoryHud hud) => hud.GetChildren().OfType<VBoxContainer>()
        .Select(c => c.GetChildren().OfType<HBoxContainer>().Select(r => r.GetMeta("item").AsString()).ToArray()).ToArray();
    private static void Choose(OptionButton button, int index)
    {
        button.Select(index); button.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
    }

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            string output = args[Array.IndexOf(args, "--output") + 1];
            GetWindow().Size = new(1280, 720); GetWindow().ContentScaleSize = new(1280, 720);
            var game = GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();
            AddChild(game);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            game.SetProcess(false);
            Call(game, "Start", "economy", true);
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Call(game, "OpenDebug");
            var session = Field<GameSession>(game, "_session");
            long tick = session.Engine.Tick;
            Check(session.Paused, "Debug did not pause the world");
            Press(game, "GodModeButton");
            var overlay = Named<Control>(game, "DeveloperOverlay");
            var panel = Named<DeveloperPanel>(game, "DeveloperPanel");
            Check(overlay.Visible && panel.IsVisibleInTree(), "God mode did not open from Debug");
            Check(!All(panel).Any(n=>n.Name=="GodIndustry"),"Legacy profile unexpectedly has industry XP controls");
            var actor = Named<OptionButton>(panel, "GodActor");
            Check(actor.GetSelectedId() == 1, "God mode did not default to the human");
            Named<SpinBox>(panel, "GodMoneyTarget").Value = 123.45;
            panel.RefreshData();
            Check(Math.Abs(Named<SpinBox>(panel, "GodMoneyTarget").Value - 123.45) < 0.001, "Refresh overwrote typed money");
            Press(panel, "GodMoneyApply");
            Check(PlayerDesk.Read(session.Engine, new(1)).CashMinor == 12345, "Money input was not converted from coins");
            var items = session.Engine.Configuration().Items;
            Choose(Named<OptionButton>(panel, "GodItem"), Array.FindIndex(items, i => i.Id == "wood"));
            Named<SpinBox>(panel, "GodStockTarget").Value = 2;
            Press(panel, "GodStockApply");
            var human = session.Engine.Observe(new(1));
            var pack = human.OwnInventories.Single(i => i.Id == human.Participant.InventoryId);
            Check(pack.Stock.Single(s => s.ItemId == "wood").Quantity == 2, "Stock target did not reach the actual backpack");
            Check(session.Engine.Tick == tick && session.Paused && session.Engine.ResearchModified, "Editing advanced time or skipped research marking");
            string hash = session.Engine.ComputeStateHash();
            Named<SpinBox>(panel, "GodStockTarget").Value = 1000000000000;
            Press(panel, "GodStockApply");
            Check(session.Engine.ComputeStateHash() == hash, "Rejected capacity edit changed the world");
            Check(Named<Label>(panel, "DeveloperStatus").Text.Contains("Не применено"), "Rejected edit was not explained");
            Choose(actor, 1);
            Named<SpinBox>(panel, "GodMoneyTarget").Value = 2.50;
            Press(panel, "GodMoneyApply");
            Check(PlayerDesk.Read(session.Engine, new(actor.GetSelectedId())).CashMinor == 250, "Participant switch edited the wrong account");
            Check(PlayerDesk.Read(session.Engine, new(1)).CashMinor == 12345, "Participant switch also changed the human");

            var hud = Field<ResourceInventoryHud>(game, "_hudGoods");
            hud.ShowInventory(pack with { Stock = [] }, "ru");
            Check(!hud.Visible && hud.GetChildCount() == 0, "Empty inventory left visible zero resources");
            StockView[] owned =
            [
                new("wood", 2, 2), new("plank", 3, 0), new("grain", 0, 0), new("bread", 4, 0),
                new("milk", 1, 0), new("cheese", 5, 0), new("beet", 1, 0), new("sugar", 1, 0), new("pastry", 1, 0)
            ];
            hud.ShowInventory(pack with { Stock = owned }, "ru");
            var columns = Columns(hud);
            Check(columns.Length == 5 && columns[0].SequenceEqual(new[] { "wood", "plank" }), "Wood/plank pairing lost");
            Check(columns[1].SequenceEqual(new[] { "bread" }) && columns.All(c => c.Length <= 2), "Zero grain visible or column exceeds two goods");
            Check(columns[2].SequenceEqual(new[] { "milk", "cheese" }) && columns[3].SequenceEqual(new[] { "beet", "sugar" }), "Food chains were not paired");
            Check(All(hud).OfType<ResourceIcon>().Count() == 8, "Reserved physical stock hidden or zero stock shown");
            var first = hud.GetChild(1);
            hud.ShowInventory(pack with { Stock = owned }, "ru");
            Check(hud.GetChild(1) == first, "Unchanged inventory rebuilt the HUD");
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(hud.GetCombinedMinimumSize().Y <= 42.1, "Paired resources expanded the 54px HUD");
            hud.ShowInventory(pack with { Stock = owned.Append(new("grain", 6, 0)).Append(new("clay", 1, 0)).ToArray() }, "en");
            Check(Columns(hud)[1].SequenceEqual(new[] { "grain", "bread" }) && Columns(hud).Last().SequenceEqual(new[] { "clay" }), "New stock or unknown resource was lost");
            Check(All(hud).OfType<ResourceIcon>().Single(i => i.Item == "wood").TooltipText.Contains(PlayerText.Item("en", "wood")), "Resource tooltip did not change language");
            hud.ShowInventory(pack, "ru");

            var tools = Field<DeveloperTools>(game, "_developer");
            tools.StartAdvance(tick + 2);
            game._UnhandledInput(new InputEventKey { Pressed = true, Keycode = Key.Escape });
            Check(!overlay.Visible && tools.AdvanceTarget is null, "Escape did not close/cancel advance");
            Check(Field<object>(game, "_surface").ToString() == "Debug" && session.Paused, "Escape left Debug or unpaused the world");
            Press(game, "GodModeButton");
            Press(panel, "DeveloperClose");
            Check(!overlay.Visible && session.Paused, "Close button did not preserve Debug pause");
            session.Engine.ValidateInvariants();
            // Drain deferred frees from HUD rebuilds and dispose the owned client
            // before terminating the test process; immediate Quit leaves RIDs pending.
            game.QueueFree();
            for(int i=0;i<4;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            Check(!GodotObject.IsInstanceValid(game),"Probe client was not released");
            string Embedded(string name)
            {
                using var stream=typeof(Playfield).Assembly.GetManifestResourceStream(name)??throw new InvalidOperationException(name);
                using var reader=new System.IO.StreamReader(stream);return reader.ReadToEnd();
            }
            var developmentProfile=EconomyProfile.Read(Embedded("Play.d2-development.json"));
            var developmentMap=JsonSerializer.Deserialize<ScenarioDefinition>(Embedded("Play.small-river.json"),SimulationJson.Options)!;
            var developmentSession=new GameSession(new SimulationEngine(developmentProfile.Apply(developmentMap)));
            using var developmentTools=new DeveloperTools(developmentSession,new(1));
            var experiencePanel=new DeveloperPanel(developmentTools,developmentSession,new(1),"ru",()=>{});AddChild(experiencePanel);
            var industryChoice=Named<OptionButton>(experiencePanel,"GodIndustry");
            string industryId=developmentSession.Engine.Configuration().Economy!.Development!.Industries[industryChoice.Selected].Id;
            Named<SpinBox>(experiencePanel,"GodExperienceTarget").Value=600;Press(experiencePanel,"GodExperienceApply");
            Check(developmentSession.Engine.DevelopmentOf(new(1))!.Experience.Single(e=>e.Industry==industryId).Total==600,"Native XP set failed");
            Named<SpinBox>(experiencePanel,"GodExperienceTarget").Value=50;Press(experiencePanel,"GodExperienceAdd");
            Check(developmentSession.Engine.DevelopmentOf(new(1))!.Experience.Single(e=>e.Industry==industryId).Total==650&&developmentSession.Engine.ResearchModified,"Native XP add/audit failed");
            experiencePanel.QueueFree();for(int i=0;i<4;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            Check(!GodotObject.IsInstanceValid(experiencePanel),"Experience controls were not released");
            var report = new
            {
                status = "succeeded", platform = OS.GetName(), display = DisplayServer.GetName(),
                checks = new[] { "debug-entry", "human-default", "coin-conversion", "stock-edit", "capacity-rejection-atomic", "actor-switch",
                    "empty-hidden", "zero-filter", "chain-pairs", "reserved-physical-stock", "hud-cache", "54px-height", "new-and-unknown-stock", "localized-tooltip", "escape-cancel", "close-keeps-debug-pause", "invariants", "probe-cleanup", "legacy-xp-controls-absent", "industry-xp-set-add" },
                scope = "Native headless Godot controls and real Developer transactions; no screenshot, hardware FPS or visual acceptance claim"
            };
            System.IO.File.WriteAllText(System.IO.Path.Combine(output, "hud-god-check.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("DELSK_HUD_GOD_PASS");
            GetTree().Quit(0);
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
