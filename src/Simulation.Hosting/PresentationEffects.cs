using Simulation.Core;

namespace Simulation.Hosting;

public record FeedbackPreferences(bool Speech=true,int EffectsVolume=65,int AmbienceVolume=20,int MusicVolume=0)
{
    public void Validate(){if(EffectsVolume is <0 or >100 || AmbienceVolume is <0 or >100 || MusicVolume is <0 or >100)throw new ArgumentException("Invalid feedback volume");}
}
public enum SpeechTopic { Build,Trade,Gather,Vacancy }
public enum FeedbackSound { Click,Harvest,Build,Trade,Important }
public record SpeechCue(long Actor,SpeechTopic Topic,int Variant,int Priority,double Until);

/// <summary>Presentation clock and public facts only. No engine, controller, world RNG or private reason strings.</summary>
public sealed class PresentationEffects
{
    private readonly Dictionary<long,double> _actorNext=[];
    private readonly Dictionary<FeedbackSound,double> _soundNext=[];
    private readonly Dictionary<long,bool> _vacancies=[];
    private readonly List<SpeechCue> _speech=[];
    private readonly List<FeedbackSound> _sounds=[];
    private long _cursor;
    private double _now,_nextSound;
    private bool _laborPrimed,_suppressed;
    public IReadOnlyList<SpeechCue> Speech=>_speech.ToArray();
    public int MaximumSpeech=>2;
    public void Reset(long lastSequence=0){_cursor=lastSequence;_actorNext.Clear();_soundNext.Clear();_vacancies.Clear();_speech.Clear();_sounds.Clear();_laborPrimed=false;_nextSound=0;}
    public void Advance(double seconds,bool suppressed)
    {
        if(!double.IsFinite(seconds) || seconds<0)throw new ArgumentException("Invalid presentation time");
        _suppressed=suppressed;
        // Suspend ages neither cooldowns nor audio; no offline/catch-up effects on resume.
        if(suppressed){_speech.Clear();_sounds.Clear();return;}
        _now+=Math.Min(seconds,.25);_speech.RemoveAll(c=>c.Until<=_now);
    }
    public void Consume(IEnumerable<PublicSimulationEvent> events,long human,FeedbackPreferences settings)
    {
        settings.Validate();if(!settings.Speech)_speech.Clear();
        var candidates=new List<(long Actor,SpeechTopic Topic,int Priority,long Sequence)>();
        foreach(var e in events.OrderBy(e=>e.Sequence))
        {
            if(e.Sequence<=_cursor)continue;_cursor=e.Sequence;
            if(_suppressed)continue;
            if(e.Type is "BuildingConstructed" or "BuildingUpgraded")
            {OfferSound(FeedbackSound.Build);if(e.ActorId is {} a && a.Value!=human)candidates.Add((a.Value,SpeechTopic.Build,3,e.Sequence));}
            if(e.Type is "CapitalSaleSettled" or "MarketPurchaseAllocated" or "DeliveryReceived")
            {OfferSound(FeedbackSound.Trade);if(e.ActorId is {} a && a.Value!=human)candidates.Add((a.Value,SpeechTopic.Trade,2,e.Sequence));}
            if(e.Type=="HarvestStarted" && e.ActorId is {} gather && gather.Value!=human)candidates.Add((gather.Value,SpeechTopic.Gather,1,e.Sequence));
            if(e.Type is "HarvestCompleted" or "ProductionCompleted")OfferSound(FeedbackSound.Harvest);
            if(e.Type is "ElectionTallied" or "VictoryDeclared" || e.Type=="CommandRejected" && e.ActorId?.Value==human)OfferSound(FeedbackSound.Important);
        }
        if(settings.Speech)
            foreach(var c in candidates.OrderByDescending(c=>c.Priority).ThenByDescending(c=>c.Sequence))OfferSpeech(c.Actor,c.Topic,c.Priority,c.Sequence);
    }
    public void ObserveLabor(IEnumerable<PublicEmploymentView> groups,long human,FeedbackPreferences settings)
    {
        var active=groups.Where(g=>g.OwnerId.Value!=human && g.Capacity>0).ToArray();
        foreach(var group in active)
        {
            bool vacant=group.Headcount<group.Capacity;
            bool changed=!_vacancies.TryGetValue(group.Id.Value,out bool old) || old!=vacant;
            _vacancies[group.Id.Value]=vacant;
            if(_laborPrimed && vacant && changed && !_suppressed && settings.Speech)OfferSpeech(group.OwnerId.Value,SpeechTopic.Vacancy,1,group.Id.Value);
        }
        var ids=active.Select(g=>g.Id.Value).ToHashSet();foreach(long id in _vacancies.Keys.Where(id=>!ids.Contains(id)).ToArray())_vacancies.Remove(id);
        _laborPrimed=true;
    }
    private void OfferSpeech(long actor,SpeechTopic topic,int priority,long sequence)
    {
        var current=_speech.FirstOrDefault(c=>c.Actor==actor);
        if(current is not null && current.Priority>=priority)return;
        if(current is null && _actorNext.GetValueOrDefault(actor)>_now)return;
        if(current is not null)_speech.Remove(current);
        if(_speech.Count>=MaximumSpeech)
        {
            var lowest=_speech.OrderBy(c=>c.Priority).ThenBy(c=>c.Until).First();
            if(lowest.Priority>=priority)return;_speech.Remove(lowest);
        }
        int variant=(int)((unchecked((ulong)sequence*0x9E3779B97F4A7C15UL)^(ulong)actor)>>32&1);
        _speech.Add(new(actor,topic,variant,priority,_now+3.2));_actorNext[actor]=_now+8;
    }
    private void OfferSound(FeedbackSound sound)
    {
        if(_sounds.Contains(sound) || _soundNext.GetValueOrDefault(sound)>_now)return;
        _sounds.Add(sound);
    }
    public FeedbackSound[] TakeSounds()
    {
        var result=_suppressed || _now<_nextSound?[]:_sounds.OrderByDescending(s=>s==FeedbackSound.Important?10:(int)s).Take(2).ToArray();_sounds.Clear();
        foreach(var sound in result)_soundNext[sound]=_now+.3;
        if(result.Length>0)_nextSound=_now+.15;
        return result;
    }
    public static string Text(SpeechCue cue,string language)=> (cue.Topic,language=="ru",cue.Variant) switch
    {
        (SpeechTopic.Build,true,0)=>"Готово! Можно работать.",(SpeechTopic.Build,true,_)=>"Хозяйство растёт.",
        (SpeechTopic.Trade,true,0)=>"Сделка состоялась.",(SpeechTopic.Trade,true,_)=>"Товар нашёл покупателя.",
        (SpeechTopic.Gather,true,0)=>"Пора собрать сырьё.",(SpeechTopic.Gather,true,_)=>"За работу!",
        (SpeechTopic.Vacancy,true,_)=>"Есть свободные места.",
        (SpeechTopic.Build,false,0)=>"Ready for work!",(SpeechTopic.Build,false,_)=>"The business is growing.",
        (SpeechTopic.Trade,false,0)=>"Deal completed.",(SpeechTopic.Trade,false,_)=>"Goods found a buyer.",
        (SpeechTopic.Gather,false,0)=>"Time to gather inputs.",(SpeechTopic.Gather,false,_)=>"Let's get to work!",
        _=>"Vacancies available."
    };
}
