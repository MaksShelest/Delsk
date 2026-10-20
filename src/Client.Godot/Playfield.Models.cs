using Agents.Algorithmic;
using Agents.Llm;
using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class Playfield
{
    private NpcDifficulty _newDifficulty=NpcDifficulty.Medium;
    private LocalGameModel? _newModel;
    private ModelRuntime? _modelRuntime;
    private OllamaAdapter? _modelAdapter;
    private HybridController[] _hybrids=[];
    private PanelContainer? _modelWaitPanel;
    private Label? _modelWaitText;
    private Button? _modelRetry;
    private bool ModelBarrier => _session is not null && (_session.WaitingForModel || _hybrids.Any(h=>h.WaitingFailure is not null));
    private void DisposeModel()
    {
        _modelRuntime?.Dispose();_modelAdapter?.Dispose();_modelRuntime=null;_modelAdapter=null;_hybrids=[];
        DropModelWait();
    }
    private void DropModelWait(){_modelWaitPanel?.QueueFree();_modelWaitPanel=null;_modelWaitText=null;_modelRetry=null;}
    private void MakeRuntime(LocalGameModel config,IEnumerable<long> actors)
    {config.Validate();_modelAdapter=new(config.Adapter);_modelRuntime=new(_modelAdapter,actors,config.Runtime);}
    private void AttachGameControllers(AutonomousConfiguration config,bool ordinary)
    {
        var policies=config.Npcs.Select(n=>new AlgorithmicController(n with {HumanActorId=config.HumanActorId,Difficulty=ordinary?_newDifficulty:n.Difficulty,PoliticalBudgetMinor=ordinary?5000:n.PoliticalBudgetMinor},
            AlgorithmicController.RuntimeSeed(_scenario.Seed,n.ActorId),_scenario.Participants.Select(p=>p.Id))).ToArray();
        if(ordinary && _newModel is {} model)
        {
            MakeRuntime(model,config.Npcs.Select(n=>n.ActorId));_hybrids=policies.Select(p=>new HybridController(p,_modelRuntime!,gameModel:model)).ToArray();_session.AttachControllers(_hybrids);
        }
        else _session.AttachControllers(policies);
    }
    private void RestoreGameControllers()
    {
        var saved=_session.ControllerStates.Where(c=>c.Kind==HybridController.PolicyKind).ToArray();
        if(saved.Length>0)
        {
            var model=LocalGameModel.FromCheckpoint(saved[0])??throw new CheckpointException("MissingGameModelConfiguration");
            if(saved.Any(c=>LocalGameModel.FromCheckpoint(c)!=model))throw new CheckpointException("ConflictingGameModelConfiguration");
            MakeRuntime(model,saved.Select(c=>c.Actor.Value));
        }
        var restored=new List<HybridController>();
        _session.RestoreControllers(c=>{if(c.Kind!=HybridController.PolicyKind)return AlgorithmicController.Restore(c);var h=HybridController.Restore(c,_modelRuntime!);restored.Add(h);return h;});
        _hybrids=restored.ToArray();
    }
    private bool ModelFrame()
    {
        _modelRuntime?.ProgressWaiting();bool wait=ModelBarrier;
        if(!wait){if(_modelWaitPanel is not null){DropModelWait();ApplyPause();}return false;}
        if(_modelWaitPanel is null)
        {
            StopHeading();_waitRelease=true;_modelWaitPanel=new PanelContainer{ZIndex=90};_modelWaitPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
            _modelWaitPanel.OffsetLeft=-330;_modelWaitPanel.OffsetRight=330;_modelWaitPanel.OffsetTop=-185;_modelWaitPanel.OffsetBottom=-15;
            var column=new VBoxContainer();_modelWaitPanel.AddChild(column);_modelWaitText=new Label{AutowrapMode=TextServer.AutowrapMode.WordSmart,CustomMinimumSize=new(630,60)};column.AddChild(_modelWaitText);
            _modelRetry=new Button{Text=L("Повторить запрос (одна попытка)","Retry request (one attempt)")};column.AddChild(_modelRetry);
            _modelRetry.Pressed+=()=>{foreach(var h in _hybrids)h.RetryWaiting();};
            var fallback=new Button{Text=L("Продолжить алгоритмом · до конца партии","Continue algorithmically · for this game")};column.AddChild(fallback);
            fallback.Pressed+=()=>{foreach(var h in _hybrids)h.ContinueAlgorithmically();DropModelWait();ApplyPause();Refresh();};
            var cancel=new Button{Text=L("Отменить ожидание и открыть меню","Cancel waiting and open menu")};column.AddChild(cancel);
            cancel.Pressed+=()=>{foreach(var h in _hybrids)h.ContinueAlgorithmically();DropModelWait();OpenMenu();};
            AddChild(_modelWaitPanel);
        }
        string? error=_hybrids.Select(h=>h.WaitingFailure).FirstOrDefault(e=>e is not null);
        _modelWaitText!.Text=error is null?L("Локальная модель: мир ожидает решения. Интерфейс доступен.","Local model: world waiting for a decision. UI remains available."):
            L("Запрос не завершён: ","Request failed: ")+error+L(". Выберите повтор или алгоритмический fallback.",". Choose retry or algorithmic fallback.");
        _modelRetry!.Disabled=!_hybrids.Any(h=>h.CanRetry);ApplyPause();return true;
    }
    private void ObserveModelFallbacks()
    {
        _session.ControllerDecided+=turn=>
        {
            if(turn.Decision.ModelTrace is {Resolution:"FallbackResolved"} trace)
                _feedback=L("Модель: применён алгоритмический fallback · ","Model: algorithmic fallback applied · ")+trace.ReasonCode;
        };
    }
    public override void _ExitTree()=>DisposeModel();
}
