using Agents.Algorithmic;
using Agents.Llm;
using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class Playfield
{
    private NpcDifficulty _newDifficulty=NpcDifficulty.Medium;
    private LocalGameModel? _newModel;
    private GameControllerRuntime? _controllers;
    private HybridController[] Hybrids => _controllers?.Hybrids ?? [];
    private sealed class GameControllerRuntime : IDisposable
    {
        private readonly OllamaAdapter _adapter;
        public ModelRuntime Runtime { get; }
        public HybridController[] Hybrids { get; set; } = [];

        public GameControllerRuntime(LocalGameModel config, IEnumerable<long> actors)
        {
            config.Validate();
            _adapter = new(config.Adapter);
            try { Runtime = new(_adapter, actors, config.Runtime); }
            catch { _adapter.Dispose(); throw; }
        }

        public void Dispose()
        {
            Runtime.Dispose();
            _adapter.Dispose();
        }
    }
    private PanelContainer? _modelWaitPanel;
    private Label? _modelWaitText;
    private Button? _modelRetry;
    private bool ModelBarrier => _session is not null && (_session.WaitingForModel || Hybrids.Any(h=>h.WaitingFailure is not null));
    private void DisposeModel()
    {
        _controllers?.Dispose();
        _controllers = null;
        DropModelWait();
    }
    private void DropModelWait(){_modelWaitPanel?.QueueFree();_modelWaitPanel=null;_modelWaitText=null;_modelRetry=null;}

    private GameControllerRuntime? AttachGameControllers(GameSession session, ScenarioDefinition scenario, AutonomousConfiguration config, bool ordinary)
    {
        var policies = config.Npcs.Select(n => new AlgorithmicController(n with
        {
            HumanActorId = config.HumanActorId,
            Difficulty = ordinary ? _newDifficulty : n.Difficulty,
            PoliticalBudgetMinor = ordinary ? 5000 : n.PoliticalBudgetMinor
        }, AlgorithmicController.RuntimeSeed(scenario.Seed, n.ActorId), scenario.Participants.Select(p => p.Id))).ToArray();
        if (!ordinary || _newModel is not { } model)
        {
            session.AttachControllers(policies);
            return null;
        }

        var binding = new GameControllerRuntime(model, config.Npcs.Select(n => n.ActorId));
        try
        {
            binding.Hybrids = policies.Select(p => new HybridController(p, binding.Runtime, gameModel: model)).ToArray();
            session.AttachControllers(binding.Hybrids);
            return binding;
        }
        catch { binding.Dispose(); throw; }
    }

    private static GameControllerRuntime? RestoreGameControllers(GameSession session)
    {
        var saved = session.ControllerStates.Where(c => c.Kind == HybridController.PolicyKind).ToArray();
        GameControllerRuntime? binding = null;
        var restored = new List<HybridController>();
        try
        {
            if (saved.Length > 0)
            {
                var model = LocalGameModel.FromCheckpoint(saved[0]) ?? throw new CheckpointException("MissingGameModelConfiguration");
                if (saved.Any(c => LocalGameModel.FromCheckpoint(c) != model)) throw new CheckpointException("ConflictingGameModelConfiguration");
                binding = new GameControllerRuntime(model, saved.Select(c => c.Actor.Value));
            }
            session.RestoreControllers(c =>
            {
                if (c.Kind == SelfBallotController.Kind) return SelfBallotController.Restore(c);
                if (c.Kind != HybridController.PolicyKind) return AlgorithmicController.Restore(c);
                var hybrid = HybridController.Restore(c, binding!.Runtime);
                restored.Add(hybrid);
                return hybrid;
            });
            if (binding is not null) binding.Hybrids = restored.ToArray();
            return binding;
        }
        catch { binding?.Dispose(); throw; }
    }
    private bool ModelFrame()
    {
        _controllers?.Runtime.ProgressWaiting();bool wait=ModelBarrier;
        if(!wait){if(_modelWaitPanel is not null){DropModelWait();ApplyPause();}return false;}
        if(_modelWaitPanel is null)
        {
            StopHeading();_waitRelease=true;_modelWaitPanel=new PanelContainer{ZIndex=90};_modelWaitPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
            _modelWaitPanel.OffsetLeft=-330;_modelWaitPanel.OffsetRight=330;_modelWaitPanel.OffsetTop=-185;_modelWaitPanel.OffsetBottom=-15;
            var column=new VBoxContainer();_modelWaitPanel.AddChild(column);_modelWaitText=new Label{AutowrapMode=TextServer.AutowrapMode.WordSmart,CustomMinimumSize=new(630,60)};column.AddChild(_modelWaitText);
            _modelRetry=new Button{Text=L("Повторить запрос (одна попытка)","Retry request (one attempt)")};column.AddChild(_modelRetry);
            _modelRetry.Pressed+=()=>{foreach(var h in Hybrids)h.RetryWaiting();};
            var fallback=new Button{Text=L("Продолжить алгоритмом · до конца партии","Continue algorithmically · for this game")};column.AddChild(fallback);
            fallback.Pressed+=()=>{foreach(var h in Hybrids)h.ContinueAlgorithmically();DropModelWait();ApplyPause();Refresh();};
            var cancel=new Button{Text=L("Отменить ожидание и открыть меню","Cancel waiting and open menu")};column.AddChild(cancel);
            cancel.Pressed+=()=>{foreach(var h in Hybrids)h.ContinueAlgorithmically();DropModelWait();OpenMenu();};
            AddChild(_modelWaitPanel);
        }
        string? error=Hybrids.Select(h=>h.WaitingFailure).FirstOrDefault(e=>e is not null);
        _modelWaitText!.Text=error is null?L("Локальная модель: мир ожидает решения. Интерфейс доступен.","Local model: world waiting for a decision. UI remains available."):
            L("Запрос не завершён: ","Request failed: ")+error+L(". Выберите повтор или алгоритмический fallback.",". Choose retry or algorithmic fallback.");
        _modelRetry!.Disabled=!Hybrids.Any(h=>h.CanRetry);ApplyPause();return true;
    }
    private void ObserveModelFallbacks()
    {
        _session.ControllerDecided+=turn=>
        {
            if(turn.Decision.ModelTrace is {Resolution:"FallbackResolved"} trace)
                _feedback=L("Модель: применён алгоритмический fallback · ","Model: algorithmic fallback applied · ")+trace.ReasonCode;
        };
    }
    public override void _ExitTree()
    {
        RestoreQuitHandling();
        DisposeModel();
        DisposeFeedback();
        GameUiArt.Release();
    }
}
