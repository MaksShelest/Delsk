using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Text.Json;

public partial class Playfield
{
    private CityReadState _cityRead=new();
    private CityInboxPanel? _cityInbox;
    private CityJourneyPanel? _journey;
    private CityNotice[] _cityNotices=[];
    private Button? _cityNoticeButton;
    private readonly Dictionary<string,string> _noticeViews=[];
    private void LoadCityExperience(bool fresh)
    {
        _noticeViews.Clear();_cityRead=new();_cityMapPreferences.Reset();if(fresh){SaveCityExperience();return;}
        var file=new ConfigFile();if(file.Load("user://city-experience-"+_mode+".cfg")!=Error.Ok)return;
        try
        {
            if(JsonSerializer.Deserialize<CityReadMemory>(file.GetValue("ux","memory","null").AsString()) is {} memory)_cityRead=CityReadState.Restore(memory);
            _cityMapPreferences.Bind(_scenario.MapWidth,_scenario.MapHeight);
            if(JsonSerializer.Deserialize<CityMapMemory>(file.GetValue("ux","map","null").AsString()) is {} map)_cityMapPreferences.Restore(map);
        }
        catch(Exception e) when(e is JsonException or ArgumentException){_cityRead=new();_cityMapPreferences.Reset();}
    }
    private void SaveCityExperience()
    {
        var file=new ConfigFile();file.SetValue("ux","memory",JsonSerializer.Serialize(_cityRead.Capture()));file.SetValue("ux","map",JsonSerializer.Serialize(_cityMapPreferences.Capture()));
        if(file.Save("user://city-experience-"+_mode+".cfg")!=Error.Ok)_feedback=L("Не удалось сохранить настройки карты и памятки.","Could not save map and guide preferences.");
    }
    private void OpenCityNotices()
    {if(_surface!=Surface.Play)return;_tabletState.OpenNotices(PlayerDesk.Read(_session.Engine,_human));PrepareTablet();}
    private void OpenCityJourney()
    {if(_surface!=Surface.Play)return;_tabletState.OpenTutorial(PlayerDesk.Read(_session.Engine,_human));PrepareTablet();}
    private void ReadCityNotice(CityNotice notice)
    {
        if(!notice.Unread)return;
        if(notice.ViewAction is {} action)
        {
            if(!_noticeViews.ContainsKey(notice.Key))_noticeViews[notice.Key]=_session.Send(_human,PlayerDesk.Check(action));
            Refresh();return;
        }
        _cityRead.MarkSeen(notice.Key);SaveCityExperience();Refresh();
    }
    private void OpenCityNotice(CityNotice notice)
    {
        ReadCityNotice(notice);
        if(notice.Target is "building" or "ground"){OpenPlayerTablet();_playerTablet?.OpenHolding(notice.Target,notice.Id);}
        else if(notice.Target=="road"&&notice.Position is {} cell)
        {_cityMapPreferences.Layers|=CityLayer.Roads;_cityMapPreferences.Participant=0;_cityMapPreferences.SelectedKey=$"road:{cell.X}:{cell.Z}";OpenCityMap();_cityMapPreferences.Center(cell.X+.5,cell.Z+.5);_cityMapPanel?.Canvas.Zoom(4);}
        else OpenCitySection(notice.Target,notice.Id);
    }
    private void UpdateCityExperience(PlayerView view)
    {
        foreach(var key in _noticeViews.Keys.ToArray())if(_session.Engine.Result(_noticeViews[key]) is {} result){if(result.Status==ResultStatus.Rejected)_tabletError=PlayerText.Reason(_language,result.ReasonCode);_noticeViews.Remove(key);}
        _cityNotices=CityNotices.Read(_scenario,view,_cityRead);
        if(_cityNoticeButton is not null)
        {int count=_cityNotices.Count(n=>n.Unread);_cityNoticeButton.Text=L("События","Notices")+(count>0?" · ● "+count:"");_cityNoticeButton.TooltipText=L("Счета · заявки · работы · переполнение","Invoices · orders · completed work · overflow");}
        _cityInbox?.ShowData(_cityNotices,view.Tick,WaitingCommand,_noticeViews.Keys.ToHashSet(StringComparer.Ordinal));
    }
}
