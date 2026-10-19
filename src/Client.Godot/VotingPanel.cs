using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class VotingPanel:PanelContainer
{
    private readonly Label _summary=new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private readonly Dictionary<ParticipantId,Button> _votes=[];
    private readonly string _language;
    public VotingPanel(string language,ElectionObservation election,System.Action<ParticipantId> cast)
    {
        _language=language;SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);OffsetLeft=80;OffsetRight=-80;OffsetTop=76;OffsetBottom=-55;
        AddThemeStyleboxOverride("panel",new StyleBoxFlat{BgColor=new Color("243a3e"),ContentMarginLeft=12,ContentMarginRight=12,ContentMarginTop=12,ContentMarginBottom=12});
        var box=new VBoxContainer();AddChild(box);
        box.AddChild(new Label{Text=L("Выборы · экономика остановлена","Election · economy stopped"),HorizontalAlignment=HorizontalAlignment.Center});box.AddChild(_summary);
        foreach(var candidate in election.Participants)
        {
            var button=new Button{Text=L("Отдать голос участнику ","Cast ballot for participant ")+candidate.Value,FocusMode=FocusModeEnum.None};
            button.Pressed+=()=>cast(candidate);box.AddChild(button);_votes.Add(candidate,button);
        }
    }
    public void ShowData(ParticipantId actor,ElectionObservation election)
    {
        if(election.Voting is not { } stage)return;
        bool awaiting=stage.Awaiting.Contains(actor);
        _summary.Text=L("Выборы #","Election #")+stage.Election.Value+L(" · снимок тика "," · frozen tick ")+stage.Tick+"\n"+
            L("Вес вашего блока: ","Your block weight: ")+stage.OwnWeight+L(" · ожидаем бюллетени: "," · awaiting ballots: ")+stage.Awaiting.Length+"\n"+
            (stage.RequiredCandidate is { } required ? L("Подписанная поддержка обязывает голосовать за ","Signed support binds your ballot to ")+required.Value+"\n":"")+
            (awaiting?L("Выберите кандидата. Занятость, производство и перевозки не меняются.","Choose a candidate. Workforce, production and logistics remain frozen."):
                L("Ваш бюллетень принят. Ожидаем остальных; следующий экономический тик не нужен.","Your ballot is accepted. Waiting for others without another economic tick."));
        foreach(var pair in _votes){pair.Value.Disabled=!awaiting || stage.RequiredCandidate is { } bound && pair.Key!=bound;pair.Value.TooltipText=pair.Value.Disabled&&awaiting?PlayerText.Reason(_language,"CommittedVote"):"";}
    }
    private string L(string ru,string en)=>PlayerText.Local(_language,ru,en);
}
