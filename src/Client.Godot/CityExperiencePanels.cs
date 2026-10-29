using Godot;
using Simulation.Hosting;

public partial class CityInboxPanel : VBoxContainer
{
    private readonly string _language;
    private readonly Action<CityNotice> _open,_read;
    private readonly Dictionary<string,(VBoxContainer Box,Label Text,Button Open,Button Read)> _rows=[];
    private readonly Label _empty=new(){AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private CityNotice[] _current=[];
    public CityInboxPanel(string language,Action<CityNotice> open,Action<CityNotice> read)
    {
        Name="CityInbox";_language=language;_open=open;_read=read;
        AddChild(new Label{Text=L("Просмотр не означает оплату. Новые события не ставят мир на паузу. Отметка счёта/заявки выполняется обычной командой при следующем активном тике.","Viewing does not pay. Incoming notices do not pause the world. Invoice/order acknowledgement is an ordinary command applied on the next active tick."),AutowrapMode=TextServer.AutowrapMode.WordSmart});
        _empty.Text=L("Новых событий пока нет.","No notices yet.");AddChild(_empty);
    }
    private string L(string ru,string en)=>PlayerText.Local(_language,ru,en);
    public void ShowData(CityNotice[] notices,long tick,bool pending,ISet<string>? viewedPending=null)
    {
        _current=notices;var keys=notices.Select(n=>n.Key).ToHashSet(StringComparer.Ordinal);
        foreach(var k in _rows.Keys.Where(k=>!keys.Contains(k)).ToArray()){_rows[k].Box.GetParent().RemoveChild(_rows[k].Box);_rows[k].Box.QueueFree();_rows.Remove(k);}
        foreach(var n in notices)
        {
            if(!_rows.TryGetValue(n.Key,out var row))
            {
                var box=new VBoxContainer{Name="Notice_"+n.Key.Replace(':','_')};var text=new Label{AutowrapMode=TextServer.AutowrapMode.WordSmart};box.AddChild(text);var actions=TabletDesign.Flow();box.AddChild(actions);
                var open=new Button{Name="NoticeOpen_"+n.Key.Replace(':','_'),Text=L("Открыть ›","Open ›"),CustomMinimumSize=new(44,44)};
                var read=new Button{Name="NoticeRead_"+n.Key.Replace(':','_'),Text=L("Просмотрено","Mark viewed"),CustomMinimumSize=new(44,44)};
                string key=n.Key;open.Pressed+=()=>{if(_current.FirstOrDefault(v=>v.Key==key) is {} live)_open(live);};read.Pressed+=()=>{if(_current.FirstOrDefault(v=>v.Key==key) is {} live)_read(live);};actions.AddChild(open);actions.AddChild(read);
                AddChild(box);row=(box,text,open,read);_rows.Add(n.Key,row);
            }
            string category=n.Category switch{"bill"=>L("▣ СЧЁТ","▣ INVOICE"),"service"=>L("⇄ ЗАЯВКА / ДОСТАВКА","⇄ ORDER / DELIVERY"),"work"=>L("✓ РАБОТА ЗАВЕРШЕНА","✓ WORK COMPLETE"),_=>L("! ПЕРЕПОЛНЕНИЕ","! OVERFLOW")};
            string status=viewedPending?.Contains(n.Key)==true?L("Просмотр запрошен · включите время","View queued · run time"):n.Unread?L("● Новое","● New"):L("Просмотрено","Viewed");
            if(n.Category=="bill")status+=" · "+(n.Resolved?L("Оплачен","Paid"):L("Не погашен","Unpaid"));
            else if(n.Category=="service")status+=" · "+(n.Resolved?L("Закрыто","Closed"):L("Требует внимания","Needs attention"));
            row.Text.Text=category+" · "+status+"\n"+(n.Item=="demolition"?L("Снос завершён · #","Demolition complete · #")+n.Id:n.Category=="bill"?L("Счёт #","Invoice #")+n.Id+" · "+PlayerText.Money(n.Quantity):n.Category=="work"&&n.Target=="building"?(n.Item=="workshop"?L("Цех","Workshop"):PlayerText.Building(_language,n.Item))+" #"+n.Id:n.Target=="road"?n.Quantity switch{1=>L("Грунтовка","Dirt road"),2=>L("Дорога I","Road I"),_=>L("Магистраль II","Highway II")}:n.Item.Length>0?PlayerText.Item(_language,n.Item)+" ×"+n.Quantity:"#"+n.Id)+
                (n.Deadline>0?"\n"+L("Осталось активного времени: ","Active time remaining: ")+GameDuration.Format(n.Deadline-tick,_language)+" · t"+n.Deadline:"");
            row.Text.AddThemeColorOverride("font_color",n.Category=="overflow"?new Color("a2491e"):n.Category=="bill"?new("a72f43"):n.Category=="service"?new("5b408c"):new("286455"));
            row.Read.Disabled=pending||!n.Unread||viewedPending?.Contains(n.Key)==true;row.Open.Disabled=pending;
        }
        _empty.Visible=notices.Length==0;
    }
}

public partial class CityJourneyPanel : VBoxContainer
{
    public event Action<string>? NavigateRequested;
    public event Action? Changed;
    private readonly CityReadState _state;
    private readonly string _language;
    private readonly OptionButton _step=new(){Name="JourneyStep"};
    private readonly Label _body=new(){Name="JourneyBody",AutowrapMode=TextServer.AutowrapMode.WordSmart};
    private readonly Button _before=new(){Name="JourneyPrevious"},_after=new(){Name="JourneyNext"},_go=new(){Name="JourneyAction"};
    public CityJourneyPanel(string language,CityReadState state)
    {
        Name="CityJourney";_state=state;_language=language;
        AddChild(TabletDesign.Heading(L("Пять шагов к своему хозяйству","Five steps to your own business"),25));
        for(int i=0;i<CityJourney.Steps.Length;i++)_step.AddItem((i+1)+". "+Title(CityJourney.Steps[i]));_step.Select(state.TutorialPage);_step.ItemSelected+=i=>Select((int)i);AddChild(_step);AddChild(_body);
        _go.Pressed+=()=>NavigateRequested?.Invoke(CityJourney.Steps[_state.TutorialPage].Destination);_go.CustomMinimumSize=new(44,48);AddChild(_go);
        var navigation=TabletDesign.Flow();_before.Text=L("‹ Назад","‹ Back");_after.Text=L("Далее ›","Next ›");_before.Pressed+=()=>Select(state.TutorialPage-1);_after.Pressed+=()=>Select(state.TutorialPage+1);navigation.AddChild(_before);navigation.AddChild(_after);AddChild(navigation);
        AddChild(new Label{Text=L("Это памятка, не выдача ресурсов или наград. Время общее с NPC; помощь и пауза сохраняют прежний режим. Esc закрывает текущий экран. Кнопки доступны через Tab и Enter.","This guide grants no resources or rewards. Time is shared with NPCs; help and pause retain the previous mode. Esc closes the current screen. Use Tab and Enter for buttons."),AutowrapMode=TextServer.AutowrapMode.WordSmart});ShowStep();
    }
    private string L(string ru,string en)=>PlayerText.Local(_language,ru,en);
    private string Title(CityLesson s)=>L(s.TitleRu,s.TitleEn);
    private void Select(int index){_state.TutorialPage=Math.Clamp(index,0,4);_step.Select(_state.TutorialPage);ShowStep();Changed?.Invoke();}
    private void ShowStep(){var lesson=CityJourney.Steps[_state.TutorialPage];_body.Text=L(lesson.BodyRu,lesson.BodyEn);_go.Text=L("Открыть: ","Open: ")+Title(lesson);_before.Disabled=_state.TutorialPage==0;_after.Disabled=_state.TutorialPage==4;}
}
