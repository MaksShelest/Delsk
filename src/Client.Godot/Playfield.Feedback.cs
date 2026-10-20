using Godot;
using Simulation.Hosting;

public partial class Playfield
{
    private readonly PresentationEffects _effects=new();
    private FeedbackAudio? _feedbackAudio;
    private FeedbackPreferences _feedbackPreferences=new();
    private void InitializeFeedback()
    {
        var file=new ConfigFile();
        if(file.Load("user://feedback.cfg")==Error.Ok)
        {
            try{var p=new FeedbackPreferences(file.GetValue("feedback","speech",true).AsBool(),file.GetValue("feedback","effects",65).AsInt32(),file.GetValue("feedback","ambience",20).AsInt32(),file.GetValue("feedback","music",0).AsInt32());p.Validate();_feedbackPreferences=p;}
            catch(ArgumentException){_feedbackPreferences=new();}
        }
        _feedbackAudio=new();AddChild(_feedbackAudio);_feedbackAudio.Apply(_feedbackPreferences);GetTree().NodeAdded+=BindFeedbackButton;
    }
    private void BindFeedbackButton(Node node)
    {
        if(node is not BaseButton button || !IsAncestorOf(button) || button.HasMeta("feedback_bound"))return;
        button.SetMeta("feedback_bound",true);button.Pressed+=()=>_feedbackAudio?.Click();
    }
    private void FeedbackPause()
    {_feedbackAudio?.SetActive(_session is not null && !_session.Paused && _surface==Surface.Play && !_tabletState.IsOpen && _helpOverlay is null,!_suspended && !_unfocused);}
    private void AdvanceFeedback(double delta)
    {
        bool suppressed=_session.Paused || _surface!=Surface.Play || _tabletState.IsOpen || _helpOverlay is not null || _suspended || _unfocused;
        _effects.Advance(delta,suppressed);FeedbackPause();
        _stage?.ShowSpeech(_effects.Speech,_language,!suppressed && _feedbackPreferences.Speech);
        foreach(var sound in _effects.TakeSounds())_feedbackAudio?.Play(sound);
    }
    private void SaveFeedback()
    {
        var file=new ConfigFile();file.SetValue("feedback","speech",_feedbackPreferences.Speech);file.SetValue("feedback","effects",_feedbackPreferences.EffectsVolume);
        file.SetValue("feedback","ambience",_feedbackPreferences.AmbienceVolume);file.SetValue("feedback","music",_feedbackPreferences.MusicVolume);
        if(file.Save("user://feedback.cfg")!=Error.Ok)_feedback=L("Не удалось сохранить настройки звука.","Could not save audio settings.");
        _feedbackAudio?.Apply(_feedbackPreferences);if(!_feedbackPreferences.Speech)_stage?.ShowSpeech([], _language,false);
    }
    private void OpenFeedbackSettings()
    {
        var dialog=new AcceptDialog{Title=L("Реплики и звук","Speech and audio"),Size=new(540,310)};
        var column=new VBoxContainer();dialog.AddChild(column);
        var speech=new CheckButton{Text=L("Реплики NPC","NPC speech"),ButtonPressed=_feedbackPreferences.Speech};column.AddChild(speech);
        speech.Toggled+=enabled=>{_feedbackPreferences=_feedbackPreferences with {Speech=enabled};SaveFeedback();};
        foreach(var (title,value,set) in new (string,int,System.Action<int>)[]{(L("Эффекты","Effects"),_feedbackPreferences.EffectsVolume,v=>_feedbackPreferences=_feedbackPreferences with {EffectsVolume=v}),
            (L("Окружение","Ambience"),_feedbackPreferences.AmbienceVolume,v=>_feedbackPreferences=_feedbackPreferences with {AmbienceVolume=v}),
            (L("Музыка (необязательно)","Music (optional)"),_feedbackPreferences.MusicVolume,v=>_feedbackPreferences=_feedbackPreferences with {MusicVolume=v})})
        {
            var row=new HBoxContainer();column.AddChild(row);row.AddChild(new Label{Text=title,CustomMinimumSize=new(225,0)});
            var slider=new HSlider{MinValue=0,MaxValue=100,Step=1,Value=value,CustomMinimumSize=new(210,36)};row.AddChild(slider);var label=new Label{Text=value+"%"};row.AddChild(label);
            slider.ValueChanged+=v=>{set((int)v);label.Text=(int)v+"%";SaveFeedback();};
        }
        column.AddChild(new Label{Text=L("0% — выключено. Настройки не меняют симуляцию.","0% disables a channel. Settings do not change simulation."),AutowrapMode=TextServer.AutowrapMode.WordSmart});
        dialog.Confirmed+=()=>dialog.QueueFree();dialog.Canceled+=()=>dialog.QueueFree();AddChild(dialog);dialog.PopupCentered();
    }
    private void DisposeFeedback(){GetTree().NodeAdded-=BindFeedbackButton;}
}
