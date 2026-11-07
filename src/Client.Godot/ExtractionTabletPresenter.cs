using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Боковая карточка добывающей техники: одна прокрутка и только текущие команды.</summary>
public partial class ExtractionTabletPresenter : VBoxContainer
{
    public event Action? MapRequested;
    public event Action<long>? FocusRequested;
    public VBoxContainer WorkActions{get;}=new();public VBoxContainer TeamActions{get;}=new();public VBoxContainer ExtraActions{get;}=new();
    private readonly string _language;private readonly ScenarioDefinition _scenario;private readonly long _id;
    private readonly Label _name=TabletDesign.Heading("",18),_level=GameUi.Text("",15),_state=GameUi.Text("",15),_resource=GameUi.Text(),_hint=GameUi.Hint(""),_crew=GameUi.Text(),_team=GameUi.Text();
    private readonly Label _cargoTitle=GameUi.Text("",16),_upgradeLines=GameUi.Text(),_upgradeReason=GameUi.Hint(""),_modeCaption=GameUi.Hint("");
    private readonly ProgressBar _progress=new(){ShowPercentage=false,CustomMinimumSize=new(0,10)},_fill=new(){ShowPercentage=false,CustomMinimumSize=new(0,10)};
    private readonly UiResourceList _stock=new();private readonly QuickStockTransfer _take;private readonly InventoryTransferPanel _transfer;
    private readonly VBoxContainer _art=new(),_upgradeSlot=new();
    private readonly HFlowContainer _primary=new(),_modes=new();
    private readonly OptionButton _switcher=new(){Name="ExtractionSwitch",SizeFlagsHorizontal=SizeFlags.ExpandFill};
    private readonly Dictionary<string,(Button Button,VBoxContainer Page)> _pages=[];
    private readonly VBoxContainer _artHost;private TextureRect? _picture;private bool _focused,_switching,_applying,_compact;private string _switchKey="";
    private PlayerView? _shown;private bool _shownPending;
    public ExtractionTabletPresenter(ScenarioDefinition scenario,string language,long id,Action<GameAction> submit,Func<GameAction,ActionAccess> access,Control legacyDetails)
    {
        Name="ExtractionWorkspace";_scenario=scenario;_language=language;_id=id;Theme=CompactTabletDesign.Theme;
        SizeFlagsHorizontal=SizeFlags.ExpandFill;SizeFlagsVertical=SizeFlags.ExpandFill;
        var hero=new HBoxContainer();_art.SizeFlagsHorizontal=SizeFlags.ShrinkBegin;hero.AddChild(_art);
        var words=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};words.AddChild(_name);words.AddChild(_level);words.AddChild(_state);hero.AddChild(words);AddChild(hero);
        _switcher.ItemSelected+=_=>ChooseMachine();AddChild(_switcher);
        var navigation=TabletDesign.Flow();AddChild(navigation);
        var scroll=new ScrollContainer{Name="ExtractionScroll",SizeFlagsVertical=SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.ShowNever};
        AddChild(scroll);var host=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};scroll.AddChild(host);
        VBoxContainer Page(string key,string caption)
        {
            var page=new VBoxContainer{Name="ExtractionPage_"+key,SizeFlagsHorizontal=SizeFlags.ExpandFill,Visible=false};
            var button=GameUi.Button("ExtractionNav_"+key,caption,()=>Select(key));button.ToggleMode=true;navigation.AddChild(button);host.AddChild(page);_pages.Add(key,(button,page));return page;
        }
        var work=Page("work",L("Работа","Work"));
        work.AddChild(_resource);work.AddChild(_progress);work.AddChild(_fill);work.AddChild(_hint);work.AddChild(_primary);work.AddChild(_modeCaption);work.AddChild(_modes);
        var upgrade=new VBoxContainer();upgrade.AddChild(TabletDesign.Heading(L("Улучшение техники","Equipment upgrade"),16));upgrade.AddChild(_upgradeLines);upgrade.AddChild(_upgradeSlot);upgrade.AddChild(_upgradeReason);work.AddChild(GameUi.Card(upgrade));
        var goods=new VBoxContainer();goods.AddChild(_cargoTitle);goods.AddChild(_stock);
        _take=new(scenario,language,"ExtractionTake",L("Забрать всё","Take all"),submit,access);_take.UseProductionCard(false,L("Груз пока пуст","Cargo is empty"));goods.AddChild(_take);work.AddChild(GameUi.Card(goods));
        work.AddChild(new Control{CustomMinimumSize=new(0,12)});
        var team=Page("team",L("Работники","Crew"));team.AddChild(GameUi.Hint(L("В автоматическом режиме работает наёмная команда. Найм и смена ставки идут постепенно, простой не оплачивается.","Automatic mode uses a hired crew. Hiring and wage changes are gradual, and idle time is unpaid.")));team.AddChild(_team);team.AddChild(TeamActions);
        var cargo=Page("cargo",L("Груз","Cargo"));_transfer=new(scenario,language,"ExtractionTransfer",submit,access);cargo.AddChild(_transfer);
        var more=Page("more",L("Ещё","More"));more.AddChild(GameUi.Button("ExtractionMap",L("Карта участка","Site map"),()=>MapRequested?.Invoke()));more.AddChild(legacyDetails);more.AddChild(ExtraActions);
        _modeCaption.Text=L("Режим","Mode");Select("work");_artHost=_art;
    }
    public static string Title(string language,string typeId,int level)=>ClassName(language,typeId)+GameUi.L(language," · уровень "," · level ")+level;
    private static string ClassName(string language,string typeId)=>typeId switch
    {
        "extract_field"=>GameUi.L(language,"Полевой комбайн","Field harvester"),
        "extract_forest"=>GameUi.L(language,"Лесозаготовительная машина","Forestry machine"),
        "extract_dairy"=>GameUi.L(language,"Молочная машина","Dairy machine"),
        "extract_mine"=>GameUi.L(language,"Горная машина","Mining machine"),
        _=>GameUi.L(language,"Техника","Equipment")
    };
    private string L(string ru,string en)=>GameUi.L(_language,ru,en);
    private void Select(string key){foreach(var page in _pages){page.Value.Page.Visible=page.Key==key;page.Value.Button.SetPressedNoSignal(page.Key==key);}}
    private void ChooseMachine()
    {
        if(_switching||_switcher.Selected<0)return;
        long id=_switcher.GetItemMetadata(_switcher.Selected).AsInt64();
        if(id!=_id)FocusRequested?.Invoke(id);
    }
    public override void _Notification(int what)
    {
        if(what!=NotificationResized||_applying||_shown is not {} view||(GetViewportRect().Size.Y<540)==_compact)return;
        _applying=true;ShowData(view,_shownPending);_applying=false;
    }
    public void ShowData(PlayerView view,bool pending)
    {
        _shown=view;_shownPending=pending;
        var machine=view.Equipment!.Single(e=>e.Id.Value==_id);var type=_scenario.EquipmentTypes!.Single(t=>t.Id==machine.TypeId);var level=type.Levels.Single(l=>l.Level==machine.Level);
        var group=view.Employment.Single(g=>g.Id==machine.GroupId);var stock=view.Inventories.Single(i=>i.Id==machine.StorageId);var model=view.Fleet!.Vehicles.Single(v=>v.Vehicle.Id==_id).Model;
        bool shortScreen=GetViewportRect().Size.Y<540;_compact=shortScreen;
        if(_picture is null){_picture=GameUiArt.Vehicle(model,new(88,58));_artHost.AddChild(_picture);}
        _picture.CustomMinimumSize=shortScreen?new(72,48):new(88,58);
        string current=machine.ResourceItem??type.ItemId;string supported=string.Join(", ",type.CompatibleItems().Select(item=>PlayerText.Item(_language,item)));
        bool classes=type.Id is "extract_field" or "extract_forest" or "extract_dairy" or "extract_mine";
        _name.Text=classes?ClassName(_language,type.Id):L("Добыча: ","Extracting: ")+PlayerText.Item(_language,type.ItemId);
        _name.Visible=!shortScreen;_level.Visible=!shortScreen;
        AddThemeConstantOverride("separation",shortScreen?2:6);
        _level.Text=L("Уровень ","Level ")+machine.Level+(machine.Deployed==true?$" · ({machine.Position.X},{machine.Position.Z})":"");
        _state.Text=State(machine);
        _resource.Text=shortScreen
            ?L("Сейчас: ","Current: ")+PlayerText.Item(_language,current)+" · "+level.CycleQuantity+L(" за цикл"," per cycle")+"\n"+L("Подходит: ","Works with: ")+supported+" · "+stock.OccupiedVolume+"/"+stock.Capacity
            :L("Сейчас: ","Current: ")+PlayerText.Item(_language,current)+"\n"+L("Подходит: ","Works with: ")+supported+"\n"+
            L("Добыча: ","Output: ")+level.CycleQuantity+L(" за цикл · "," per cycle · ")+L("заполнение ","fill ")+stock.OccupiedVolume+"/"+stock.Capacity;
        _fill.Visible=!shortScreen;
        _hint.Text=Explain(machine,pending);
        _progress.MaxValue=Math.Max(1,machine.WorkRequired);_progress.Value=machine.WorkDone;_progress.Visible=machine.WorkRequired>0;
        _fill.MaxValue=Math.Max(1,stock.Capacity);_fill.Value=stock.OccupiedVolume;
        _crew.Text=L("Команда ","Crew ")+group.Headcount+" / "+group.Capacity;
        _team.Text=_crew.Text+"\n"+L("Ставка: ","Wage: ")+PlayerText.Money(group.WageMinor)+(group.PendingWageMinor is {} wage?L(" → объявлено "," → pending ")+PlayerText.Money(wage):"");
        long carried=stock.Stock.Sum(s=>s.Quantity-s.Reserved);
        _cargoTitle.Text=carried<=0?L("Груз пока пуст","Cargo is empty"):L("Груз: ","Cargo: ")+stock.OccupiedVolume+"/"+stock.Capacity;
        _stock.ShowStock(stock,_language,stock.Stock.Where(s=>s.Quantity>0).Select(s=>s.ItemId));_stock.Visible=carried>0;
        _take.ShowData(view,stock.Id,view.Participant.InventoryId,type.CompatibleItems(),pending,"output");
        ShowUpgrade(view,machine,type,level);
        Arrange(machine.Mode);
        _modeCaption.Visible=_modes.GetChildCount()>0;_modes.Visible=_modeCaption.Visible;
        if(!_focused){_focused=true;_transfer.FocusInventory(stock.Id.Value);}_transfer.ShowData(view,pending);
        FillSwitcher(view);
        if(IsInsideTree()&&Size.X>0)GameUi.FitControls(this,Size.X);
    }
    private void ShowUpgrade(PlayerView view,EquipmentView machine,EquipmentTypeDefinition type,EquipmentLevelDefinition level)
    {
        var next=type.Levels.FirstOrDefault(l=>l.Level==machine.Level+1);
        if(next is null)_upgradeLines.Text=L("Максимальный уровень","Maximum level");
        else _upgradeLines.Text=L("Уровень ","Level ")+machine.Level+" → "+next.Level+"\n"+
            L("Добыча: ","Output: ")+level.CycleQuantity+" → "+next.CycleQuantity+L(" за цикл."," per cycle.")+"\n"+
            L("Вместимость: ","Capacity: ")+level.StorageCapacity+" → "+next.StorageCapacity+".\n"+
            L("Стоимость: ","Cost: ")+PlayerText.Money(next.PriceMinor)+L(" монет."," coins.");
        string reason="";
        if(next is not null)
        {
            if(machine.Mode!=EquipmentMode.Off||machine.PersonalJobId!=0||machine.CycleId!=0||machine.Motion is not null)
                reason=L("Сначала остановите технику.","Stop the machine first.");
            else if(view.AvailableCashMinor<next.PriceMinor)
                reason=L("Недостаточно монет: нужно ","Not enough coins: need ")+PlayerText.Money(next.PriceMinor)+L(", свободно ",", available ")+PlayerText.Money(view.AvailableCashMinor)+".";
        }
        _upgradeReason.Text=reason;_upgradeReason.Visible=reason.Length>0;_upgradeSlot.Visible=next is not null;
    }
    private void Arrange(EquipmentMode mode)
    {
        foreach(var button in new Control[]{WorkActions,_primary,_modes,_upgradeSlot}.SelectMany(Buttons).Distinct())
        {
            string name=button.Name.ToString();
            Container host=name.Contains("_upgrade_")?_upgradeSlot:name.Contains("_mode_")?_modes:_primary;
            if(button.GetParent()!=host){button.GetParent()?.RemoveChild(button);host.AddChild(button);}
            if(name.Contains("_upgrade_"))button.Text=L("Улучшить","Upgrade");
            if(name.Contains("_start_")||name.Contains("_place_"))GameUi.Primary(button);
            if(!name.Contains("_mode_"))continue;
            bool selected=name.Contains("_Personal_")&&mode==EquipmentMode.Personal||name.Contains("_Automatic_")&&mode==EquipmentMode.Automatic||name.Contains("_Off_")&&mode==EquipmentMode.Off;
            if(selected){button.AddThemeStyleboxOverride("disabled",CompactTabletDesign.Surface("49735f",8));button.AddThemeColorOverride("font_disabled_color",new("f4f7f2"));}
            else{button.RemoveThemeStyleboxOverride("disabled");button.RemoveThemeColorOverride("font_disabled_color");}
        }
        var upgrade=Buttons(_upgradeSlot).FirstOrDefault();
        if(upgrade is {Disabled:true}&&_upgradeReason.Text.Length==0&&upgrade.TooltipText.Length>0){_upgradeReason.Text=upgrade.TooltipText;_upgradeReason.Visible=true;}
    }
    private static IEnumerable<Button> Buttons(Node node)
    {foreach(var child in node.GetChildren()){if(child is Button button)yield return button;foreach(var nested in Buttons(child))yield return nested;}}
    private void FillSwitcher(PlayerView view)
    {
        var machines=view.Equipment??[];
        if(machines.Length<2){_switcher.Visible=false;return;}
        _switcher.Visible=true;string key=string.Join(",",machines.Select(e=>e.Id.Value+":"+e.TypeId+":"+e.Level));
        _switching=true;
        if(key!=_switchKey)
        {
            _switchKey=key;_switcher.Clear();
            foreach(var machine in machines){_switcher.AddItem(Title(_language,machine.TypeId,machine.Level));_switcher.SetItemMetadata(_switcher.ItemCount-1,machine.Id.Value);}
        }
        for(int i=0;i<_switcher.ItemCount;i++)if(_switcher.GetItemMetadata(i).AsInt64()==_id)_switcher.Select(i);
        _switching=false;
    }
    private string State(EquipmentView machine)=>machine.Deployed!=true?L("Готова к перевозке","Packed for transport"):machine.ReasonCode switch
    {
        "Working"=>L("Работает","Working"),"UserPaused"=>L("Остановлена","Stopped"),"PersonalMode"=>L("Ждёт команду","Waiting for you"),
        "NoStaff" or "BelowMinimumCrew"=>L("Нет работников","No workers"),"NoOutputSpace"=>L("Груз заполнен","Cargo full"),
        "ResourceDepleted"=>L("Ресурс кончился","Resource depleted"),"EquipmentPacked"=>L("Готова к перевозке","Packed for transport"),
        _=>GameUi.Reason(_language,machine.ReasonCode)
    };
    private string Explain(EquipmentView machine,bool pending)
    {
        if(pending)return L("Действие запрошено. Включите время.","Action queued. Run time.");
        if(machine.Deployed!=true)return L("Привезите машину на свой участок с подходящим ресурсом и нажмите «Разместить».","Drive it to your own matching site and choose Place.");
        return machine.ReasonCode switch
        {
            "Working"=>L("Машина работает. Её можно остановить.","The machine is working. You can stop it."),
            "UserPaused"=>L("Остановлена. Выберите режим и начните добычу.","Stopped. Choose a mode and start extraction."),
            "PersonalMode"=>L("Личный режим добывает вашим персонажем. Нажмите «Начать добычу».","Personal mode uses your character. Choose Start harvest."),
            "NoStaff" or "BelowMinimumCrew"=>L("Не хватает работников. Найм идёт постепенно; ставка меняется на вкладке «Работники».","Not enough workers. Hiring is gradual; change the wage on Crew."),
            "NoOutputSpace"=>L("Груз заполнен. Заберите ресурс, чтобы работа продолжилась.","Cargo is full. Take resources so work can continue."),
            "ResourceDepleted"=>L("Ресурс на участке закончился. Подождите, пока он восстановится.","The site is depleted. Wait for it to recover."),
            _=>GameUi.Reason(_language,machine.ReasonCode)
        };
    }
}
