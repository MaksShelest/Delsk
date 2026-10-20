using Godot;
using Simulation.Core;

public partial class Playfield
{
    private VotingPanel? _votingPanel;
    private long _announcedElection;
    private string _electionNotice="";
    private void RefreshVoting()
    {
        var election=_session.Engine.Observe(_human).Elections;
        if(election.Results.Length==0){_announcedElection=0;_electionNotice="";}
        if(election.Results.LastOrDefault() is { } result && result.Id.Value>_announcedElection)
        {
            _announcedElection=result.Id.Value;
            _electionNotice=L("Выборы #","Election #")+result.Id.Value+": "+(result.Winner is { } winner?
                L("победил участник ","participant won: ")+winner.Value:L("строгого большинства нет; следующие выборы назначены.","no strict majority; next election scheduled."));
        }
        if(election.Voting is null)
        {if(_votingPanel is not null){RemoveChild(_votingPanel);_votingPanel.QueueFree();_votingPanel=null;}ApplyPause();return;}
        if(_votingPanel is null)
        {
            _votingPanel=new(_language,election,candidate=>
            {
                try{_session.Send(_human,new(ActionKind.SetVoteChoice,Election:_session.Engine.Observe(_human).Elections.Voting!.Election,Candidate:candidate));}
                catch(Exception failure){_feedback=failure.Message;}
                ApplyPause();Refresh();
            });AddChild(_votingPanel);
        }
        _votingPanel.Visible=_surface==Surface.Play && _helpOverlay is null;
        _votingPanel.ShowData(_human,election);ApplyPause();
    }
    partial void DeveloperMenu(VBoxContainer menu);
    partial void DeveloperDebug(HBoxContainer toolbar);
    partial void DeveloperInput(InputEventKey key, ref bool handled);
    partial void DeveloperFrame();
    partial void DeveloperReset();
}
