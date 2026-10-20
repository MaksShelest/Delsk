using Godot;
using Simulation.Hosting;

/// <summary>Small reused player pool. It never controls time or asks the simulation for events.</summary>
public partial class FeedbackAudio : Node
{
    private readonly AudioStreamPlayer[] _effects=[new(),new()];
    private readonly AudioStreamPlayer _ambience=new(),_music=new();
    private readonly Dictionary<FeedbackSound,AudioStream> _streams=[];
    private FeedbackPreferences _settings=new();
    private bool _active,_focused=true;
    private double _clickNext;
    public override void _Ready()
    {
        foreach(var sound in Enum.GetValues<FeedbackSound>())_streams[sound]=ResourceLoader.Load<AudioStream>("res://audio/"+sound.ToString().ToLowerInvariant()+".wav",cacheMode:ResourceLoader.CacheMode.Ignore);
        foreach(var player in _effects)AddChild(player);AddChild(_ambience);AddChild(_music);
        _ambience.Stream=Loop("ambience");_music.Stream=Loop("music");Apply(_settings);
    }
    private static AudioStreamWav Loop(string name)
    {var stream=ResourceLoader.Load<AudioStreamWav>("res://audio/"+name+".wav",cacheMode:ResourceLoader.CacheMode.Ignore);stream.LoopMode=AudioStreamWav.LoopModeEnum.Forward;stream.LoopBegin=0;stream.LoopEnd=stream.Data.Length/2;return stream;}
    private static float Db(int volume)=>volume==0?-80:Mathf.LinearToDb(volume/100f);
    public void Apply(FeedbackPreferences settings)
    {
        settings.Validate();_settings=settings;
        foreach(var player in _effects){player.VolumeDb=Db(settings.EffectsVolume);if(settings.EffectsVolume==0)player.Stop();}
        _ambience.VolumeDb=Db(settings.AmbienceVolume);_music.VolumeDb=Db(settings.MusicVolume);UpdateLoops();
    }
    public void SetActive(bool active,bool focused)
    {
        if((!active && _active) || (!focused && _focused))foreach(var player in _effects)player.Stop();
        _active=active;_focused=focused;UpdateLoops();
    }
    private void UpdateLoops()
    {
        foreach(var (player,volume) in new[]{(_ambience,_settings.AmbienceVolume),(_music,_settings.MusicVolume)})
        {player.StreamPaused=!_active || !_focused || volume==0;if(_active && _focused && volume>0 && !player.Playing)player.Play();}
    }
    public void Click()
    {
        double now=Time.GetTicksMsec()/1000d;if(now<_clickNext || !_focused)return;_clickNext=now+.08;Play(FeedbackSound.Click,true);
    }
    public void Play(FeedbackSound sound,bool ui=false)
    {
        if(!_focused || !ui && !_active || _settings.EffectsVolume==0)return;
        var player=_effects.FirstOrDefault(p=>!p.Playing);if(player is null)return;
        player.Stream=_streams[sound];player.Play();
    }
    public override void _ExitTree()
    {
        var owned=_streams.Values.Concat(new[]{_ambience.Stream,_music.Stream}).Where(s=>s is not null).Distinct().ToArray();
        foreach(var player in _effects.Concat(new[]{_ambience,_music})){player.Stop();player.Stream=null;}
        _streams.Clear();foreach(var stream in owned)stream.Dispose();
    }

}
