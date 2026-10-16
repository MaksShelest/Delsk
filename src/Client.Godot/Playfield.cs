using System.Globalization;
using System.Text.Json;
using Agents.Algorithmic;
using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Минимальный клиент: человек шлёт те же команды, карточка делит работу, поставки и ставки.</summary>
public partial class Playfield : Control
{
    private GameSession _session = null!;
    private TickScheduler _scheduler = null!;
    private ScenarioDefinition _scenario = null!;
    private ParticipantId _human;
    private string _language = "ru";
    private string _mode = "economy";
    private string _feedback = "";
    private string _cardsSignature = "";
    private bool _manualPause = true;
    private bool _suspended;
    private bool _unfocused;
    private bool _skipNextFrame;
    private bool _left;
    private Label _status = null!;
    private Label _feedbackLabel = null!;
    private VBoxContainer _cards = null!;
    private string SavePath => ProjectSettings.GlobalizePath($"user://play-{_mode}-{SimulationEngine.RulesVersion}.json");

    public override void _Ready()
    {
        if (OS.GetCmdlineUserArgs().Contains("--smoke"))
        {
            _left = true;
            GetTree().ChangeSceneToFile("res://PlatformProbe.tscn");
            return;
        }
        Start("economy");
        BuildChrome();
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (_left || _session is null) return;
        try
        {
            if (_skipNextFrame) { _skipNextFrame = false; return; }
            long prior = _session.Engine.Tick;
            _scheduler.AdvanceElapsed(TimeSpan.FromSeconds(Math.Min(delta, 0.25)));
            if (_session.Engine.Tick != prior) Refresh();
        }
        catch (Exception failure) { _feedback = failure.Message; Refresh(); }
    }

    public override void _Notification(int what)
    {
        if (_left || _session is null) return;
        if (what == NotificationApplicationPaused) _suspended = true;
        else if (what == NotificationApplicationFocusOut) _unfocused = true;
        else if (what == NotificationApplicationResumed) { _suspended = false; _skipNextFrame = true; }
        else if (what == NotificationApplicationFocusIn) { _unfocused = false; _skipNextFrame = true; }
        else return;
        ApplyPause();
        if (what == NotificationApplicationPaused || what == NotificationApplicationFocusOut)
        { try { CheckpointStore.Save(_session, SavePath); } catch (Exception failure) { _feedback = failure.Message; } }
        Refresh();
    }

    private void Start(string mode)
    {
        _mode = mode;
        string scenarioName = mode == "politics" ? "Play.baseline-politics.json" : "Play.baseline-economy.json";
        string controllerName = mode == "politics" ? "Play.baseline-politics-controllers.json" : "Play.baseline-controllers.json";
        _scenario = JsonSerializer.Deserialize<ScenarioDefinition>(Text(scenarioName), SimulationJson.Options)
            ?? throw new InvalidOperationException(scenarioName);
        var config = JsonSerializer.Deserialize<AutonomousConfiguration>(Text(controllerName), SimulationJson.Options)
            ?? throw new InvalidOperationException(controllerName);
        _human = new(config.HumanActorId);
        _session = new GameSession(new SimulationEngine(_scenario));
        _session.AttachControllers(config.Npcs.Select(n => new AlgorithmicController(
            n with { HumanActorId = config.HumanActorId },
            AlgorithmicController.RuntimeSeed(_scenario.Seed, n.ActorId),
            _scenario.Participants.Select(p => p.Id))));
        _scheduler = new TickScheduler(_session);
        _manualPause = true;
        _feedback = "";
        _cardsSignature = "";
        ApplyPause();
        if (_status is not null) CallDeferred(nameof(RebuildChrome));
    }

    private void RebuildChrome()
    {
        if (_left || _session is null) return;
        BuildChrome();
        Refresh();
    }

    private void ApplyPause() => _scheduler.SetPaused(_manualPause || _suspended || _unfocused);

    private void StepOne()
    {
        _scheduler.SetPaused(false);
        _session.Step();
        ApplyPause();
        Refresh();
    }

    private void Submit(GameAction action)
    {
        try
        {
            _session.Send(_human, PlayerDesk.Check(action));
            _feedback = T("feedback.sent");
        }
        catch (Exception failure) { _feedback = failure.Message; }
        Refresh();
    }

    private void Refresh()
    {
        if (_status is null) return;
        var view = PlayerDesk.Read(_session.Engine, _human);
        var actor = view.Participant;
        _status.Text = $"{T(_manualPause || _suspended || _unfocused ? "paused" : "running")}  {T("tick")}: {_session.Engine.Tick}\n" +
            $"{T("cash")}: {(view.CashMinor / 100m).ToString("0.00", CultureInfo.InvariantCulture)}\n" +
            $"{T("position")}: ({actor.Position.X}, {actor.Position.Z})  {T("busy")}: {T(actor.Busy ? "yes" : "no")}\n" +
            $"{T("npc.title")}: " + string.Join("; ", view.LaborMarket.Where(g => g.OwnerId != _human).Select(g => $"{g.OwnerId.Value} {g.Role} {g.Headcount}/{g.Capacity}"));
        _feedbackLabel.Text = _feedback;
        string signature = CardSignature(view);
        if (signature != _cardsSignature) { _cardsSignature = signature; FillCards(view); }
    }

    private static string CardSignature(PlayerView view) =>
        string.Join(";", view.Enterprises.Select(e => $"{e.Id.Value}:{e.Enabled}:{e.ReasonCode}:{e.RecipeId}")) + "|" +
        string.Join(";", view.Employment.Select(g => $"{g.Id.Value}:{g.Role}:{g.Headcount}:{g.WageMinor}:{g.PendingWageMinor}")) + "|" +
        string.Join(";", view.Logistics.Select(l => $"{l.Id.Value}:{l.EnterpriseId.Value}:{l.Enabled}:{l.InboundEnabled}:{l.ReasonCode}")) + "|" +
        string.Join(";", view.Buildings.Select(b => b.Id.Value)) + "|" +
        string.Join(";", view.Support.Proposals.Select(p => $"{p.Id}:{p.Status}")) + "|" +
        view.Elections.NextElectionId?.Value + ":" + view.Elections.OwnChoice?.Value;

    private void FillCards(PlayerView view)
    {
        foreach (Node child in _cards.GetChildren()) child.QueueFree();
        if (view.Enterprises.Length == 0) _cards.AddChild(new Label { Text = T("no.enterprise") });
        foreach (var enterprise in view.Enterprises)
        {
            var card = new VBoxContainer();
            _cards.AddChild(card);
            card.AddChild(new Label { Text = $"#{enterprise.Id.Value}  {enterprise.RecipeId}" });
            var work = Section("card.work");
            card.AddChild(work);
            work.AddChild(new Label { Text = $"{enterprise.ReasonCode}  {enterprise.Staff}/{enterprise.StaffCapacity}" });
            Button(work, enterprise.Enabled ? "work.stop" : "work.start", () => Submit(PlayerDesk.Work(enterprise.Id.Value, !enterprise.Enabled)));
            var recipe = _scenario.Recipes.Single(r => r.Id == enterprise.RecipeId);
            Button(work, "transfer.input", () => Submit(PlayerDesk.Transfer(view.Participant.InventoryId.Value, enterprise.InputId.Value, recipe.InputItem, 1)));
            var wages = Section("card.wages");
            card.AddChild(wages);
            var production = view.Employment.Single(g => g.Role == EmploymentRole.Production && g.Id.Value == enterprise.Id.Value);
            WageRow(wages, production);
            var supplies = Section("card.supplies");
            card.AddChild(supplies);
            var logistics = view.Logistics.SingleOrDefault(l => l.EnterpriseId == enterprise.Id);
            if (logistics is null) Button(supplies, "supplies.buy", () => Submit(PlayerDesk.BuyLogistics(enterprise.Id.Value)));
            else
            {
                supplies.AddChild(new Label { Text = logistics.ReasonCode });
                Button(supplies, logistics.Enabled ? "supplies.output.off" : "supplies.output.on",
                    () => Submit(PlayerDesk.Supplies(logistics.Id.Value, LogisticsDirection.Output, !logistics.Enabled)));
                Button(supplies, logistics.InboundEnabled ? "supplies.inbound.off" : "supplies.inbound.on",
                    () => Submit(PlayerDesk.Supplies(logistics.Id.Value, LogisticsDirection.Inbound, !logistics.InboundEnabled)));
                var crew = view.Employment.Single(g => g.Id == logistics.GroupId);
                WageRow(wages, crew);
                foreach (var building in view.Buildings.Where(b => b.Kind is BuildingKind.Warehouse or BuildingKind.Shop))
                {
                    Button(supplies, "supplies.send", () => Submit(PlayerDesk.SendProduct(logistics.Id.Value, building.InputId.Value, true)));
                    Button(supplies, "supplies.fetch", () => Submit(PlayerDesk.FetchInput(logistics.Id.Value, building.InputId.Value, true)));
                }
            }
        }
        _cards.AddChild(new Label { Text = T("support.title") });
        if (view.Support.Proposals.Length == 0) _cards.AddChild(new Label { Text = T("support.none") });
        foreach (var proposal in view.Support.Proposals)
        {
            var row = new HBoxContainer();
            _cards.AddChild(row);
            string terms = string.Join(", ", proposal.Compensation.Select(m => $"{m.Quantity} {m.ItemId}"));
            row.AddChild(new Label { Text = $"#{proposal.Id} {proposal.Status} {proposal.Candidate.Value}->{proposal.Seller.Value} {terms}" });
            if (proposal.Seller == _human && proposal.Status == SupportProposalStatus.Proposed)
                Button(row, "support.accept", () => Submit(PlayerDesk.AcceptSupport(proposal.Id)));
            if (proposal.Status is SupportProposalStatus.Proposed or SupportProposalStatus.AcceptedAwaitingResources)
                Button(row, "support.reject", () => Submit(PlayerDesk.RejectSupport(proposal.Id)));
        }
        if (view.Elections.NextElectionId is { } election)
        {
            _cards.AddChild(new Label { Text = $"{T("vote.title")} #{election.Value}  {view.Elections.OwnChoice?.Value.ToString() ?? T("no")}" });
            var votes = new HBoxContainer();
            _cards.AddChild(votes);
            foreach (var candidate in view.Elections.Participants.Where(p => p != _human))
                Button(votes, "vote.choose", () => Submit(PlayerDesk.Vote(election.Value, candidate.Value)), candidate.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void WageRow(Container parent, EmploymentView group)
    {
        long shown = group.PendingWageMinor ?? group.WageMinor;
        parent.AddChild(new Label { Text = $"{group.Role} {group.Headcount}/{group.Capacity} {shown}" });
        var row = new HBoxContainer();
        parent.AddChild(row);
        Button(row, "wage.raise", () => Submit(PlayerDesk.Wage(group.Id.Value, shown + PlayerDesk.WageStepMinor)));
        Button(row, "wage.lower", () => Submit(PlayerDesk.Wage(group.Id.Value, Math.Max(0, shown - PlayerDesk.WageStepMinor))));
    }

    private VBoxContainer Section(string key)
    {
        var box = new VBoxContainer();
        box.AddChild(new Label { Text = T(key) });
        return box;
    }

    private void BuildChrome()
    {
        foreach (Node child in GetChildren()) child.QueueFree();
        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(column);
        column.AddChild(new Label { Text = T("title") });
        var controls = new HBoxContainer();
        column.AddChild(controls);
        Button(controls, "language", () => { _language = _language == "ru" ? "en" : "ru"; CallDeferred(nameof(RebuildChrome)); });
        Button(controls, "pause", () => { _manualPause = !_manualPause; ApplyPause(); Refresh(); });
        Button(controls, "step", StepOne);
        Button(controls, "save", () => { CheckpointStore.Save(_session, SavePath); _feedback = T("save"); Refresh(); });
        Button(controls, "load", Load);
        Button(controls, "economy", () => Start("economy"));
        Button(controls, "politics", () => Start("politics"));
        _status = new Label();
        column.AddChild(_status);
        _feedbackLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_feedbackLabel);
        var moves = new HBoxContainer();
        column.AddChild(moves);
        Button(moves, "move.north", () => Move(0, -1));
        Button(moves, "move.south", () => Move(0, 1));
        Button(moves, "move.west", () => Move(-1, 0));
        Button(moves, "move.east", () => Move(1, 0));
        foreach (string item in _scenario.ResourceNodes.Select(n => n.ItemId).Distinct(StringComparer.Ordinal))
            Button(moves, "harvest", () => Submit(PlayerDesk.Harvest(item, 1)), item);
        _cards = new VBoxContainer();
        column.AddChild(_cards);
        _cardsSignature = "";
    }

    private void Move(int dx, int dz)
    {
        var actor = PlayerDesk.Read(_session.Engine, _human).Participant;
        int x = Math.Clamp(actor.Position.X + dx, 0, _scenario.MapWidth - 1);
        int z = Math.Clamp(actor.Position.Z + dz, 0, _scenario.MapHeight - 1);
        Submit(PlayerDesk.Move(x, z));
    }

    private void Load()
    {
        var loaded = CheckpointStore.Load(SavePath);
        _session = loaded.Session;
        _session.RestoreControllers(AlgorithmicController.Restore);
        _scheduler = new TickScheduler(_session);
        _manualPause = true;
        ApplyPause();
        _feedback = T("load");
        _cardsSignature = "";
        Refresh();
    }

    private void Button(Container parent, string key, Action action, string suffix = "")
    {
        var button = new Button { Text = suffix.Length == 0 ? T(key) : $"{T(key)} {suffix}" };
        button.Pressed += () => { try { action(); } catch (Exception failure) { _feedback = failure.Message; Refresh(); } };
        parent.AddChild(button);
    }

    private string T(string key) => PlayerText.Get(_language, key);

    private static string Text(string name)
    {
        using var stream = typeof(Playfield).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException(name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
