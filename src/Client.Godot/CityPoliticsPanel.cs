using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Public election and own bilateral support. Persistent controls retain focus during refresh.</summary>
public partial class CityPoliticsPanel : VBoxContainer
{
    private readonly string _language;private readonly Action<GameAction> _submit;
    private readonly Label _summary=GameUi.Text(),_empty=GameUi.Hint("");
    private readonly VBoxContainer _support=new();private readonly HFlowContainer _choices=TabletDesign.Flow();
    private readonly Dictionary<long,(Control Root,Label Text,Button Accept,Button Reject)> _rows=[];
    private readonly Dictionary<long,Button> _votes=[];private PlayerView? _view;private bool _pending;
    public CityPoliticsPanel(string language,Action<GameAction> submit)
    {
        _language=language;_submit=submit;AddChild(GameUi.Card(_summary));AddChild(_choices);AddChild(TabletDesign.Heading(L("Договоры поддержки","Support agreements"),20));AddChild(_empty);AddChild(_support);
    }
    private string L(string ru,string en)=>GameUi.L(_language,ru,en);
    private void Send(GameAction action){if(_pending)return;_pending=true;foreach(var b in GetTreeButtons())b.Disabled=true;_submit(action);}
    private IEnumerable<Button> GetTreeButtons()=>_votes.Values.Concat(_rows.Values.SelectMany(r=>new[]{r.Accept,r.Reject}));
    public void ShowData(PlayerView view,bool pending)
    {
        _view=view;_pending=pending;var election=view.Elections.NextElectionId;
        _summary.Text=election is null?L("Сейчас выборы не назначены.","No election is scheduled."):L("Следующие выборы · ваш выбор: ","Next election · your choice: ")+(view.Elections.OwnChoice is {} own?WorldSkins.ActorName(own.Value,_language):L("не выбран","not selected"));
        _choices.Visible=election is not null;
        foreach(var candidate in view.Elections.Participants.Where(p=>p!=view.Participant.Id))
        {
            if(!_votes.TryGetValue(candidate.Value,out var b)){long id=candidate.Value;b=GameUi.Button("CityVote_"+id,WorldSkins.ActorName(id,_language),()=>{if(_view?.Elections.NextElectionId is {} e)Send(PlayerDesk.Vote(e.Value,id));});_choices.AddChild(b);_votes.Add(id,b);}
            b.Disabled=pending;b.SetPressedNoSignal(view.Elections.OwnChoice==candidate);
        }
        var live=view.Support.Proposals.Select(p=>p.Id).ToHashSet();foreach(long id in _rows.Keys.Except(live).ToArray()){var r=_rows[id];_support.RemoveChild(r.Root);r.Root.QueueFree();_rows.Remove(id);}
        _empty.Text=L("Нет предложений поддержки. Новое соглашение можно предложить в переговорах.","No support proposals. Offer an agreement in Negotiations.");_empty.Visible=live.Count==0;
        foreach(var p in view.Support.Proposals)
        {
            if(!_rows.TryGetValue(p.Id,out var row))
            {long id=p.Id;var box=new VBoxContainer();var text=GameUi.Text();box.AddChild(text);var actions=TabletDesign.Flow();box.AddChild(actions);var accept=GameUi.Button("CitySupportAccept_"+id,L("Принять","Accept"),()=>Send(PlayerDesk.AcceptSupport(id)));var reject=GameUi.Button("CitySupportReject_"+id,L("Отклонить","Reject"),()=>Send(PlayerDesk.RejectSupport(id)));actions.AddChild(accept);actions.AddChild(reject);var card=GameUi.Card(box);_support.AddChild(card);row=(card,text,accept,reject);_rows.Add(id,row);}
            string status=p.Status switch{SupportProposalStatus.Proposed=>L("Ожидает ответа","Awaiting reply"),SupportProposalStatus.AcceptedAwaitingResources=>L("Соберите компенсацию и подпишите в переговорах","Prepare compensation and sign in Negotiations"),_=>L("Соглашение завершено или закрыто","Agreement completed or closed")};
            row.Text.Text=WorldSkins.ActorName(p.Candidate.Value,_language)+" → "+WorldSkins.ActorName(p.Seller.Value,_language)+" · "+status+"\n"+string.Join(" · ",p.Compensation.Select(m=>PlayerText.Item(_language,m.ItemId)+" ×"+m.Quantity));
            row.Accept.Visible=p.Seller==view.Participant.Id&&p.Status==SupportProposalStatus.Proposed;row.Reject.Visible=p.Status is SupportProposalStatus.Proposed or SupportProposalStatus.AcceptedAwaitingResources;row.Accept.Disabled=row.Reject.Disabled=pending;
        }
        if(IsInsideTree()&&Size.X>0)GameUi.FitControls(this,Size.X);
    }
}
