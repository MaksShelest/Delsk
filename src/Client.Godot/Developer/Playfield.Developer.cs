using Godot;
using Simulation.Developer;

public partial class Playfield
{
    private DeveloperTools? _developer;
    private DeveloperPanel? _developerPanel;
    partial void DeveloperMenu(VBoxContainer menu)
    {
        var button=new Button{Text=L("Разработчик · журнал и вмешательства","Developer · journal and interventions"),FocusMode=FocusModeEnum.None};menu.AddChild(button);
        button.Pressed+=()=>
        {
            if(_developerPanel is not null){_developerPanel.Visible=true;return;}
            _developer=new(_session,_human);_developerPanel=new(_developer,_session,_human,_language,Refresh);
            _developerPanel.Closed+=()=>_developerPanel.Visible=false;AddChild(_developerPanel);_developerPanel.RefreshData();
        };
    }
    partial void DeveloperFrame()
    {
        if(_developerPanel is not {Visible:true} || _suspended || _unfocused || _helpOverlay is not null)return;
        long prior=_session.Engine.Tick;_developer?.PumpAdvance();_developerPanel.RefreshData();
        if(_session.Engine.Tick!=prior)Refresh();
    }
    partial void DeveloperReset()
    {
        _developer?.Dispose();_developer=null;
        if(_developerPanel is not null){RemoveChild(_developerPanel);_developerPanel.QueueFree();_developerPanel=null;}
    }
}
