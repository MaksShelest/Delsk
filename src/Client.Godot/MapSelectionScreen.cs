using Godot;
using Agents.Algorithmic;
using Agents.Llm;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Single selected map; catalog selection does not create or replace a session.</summary>
public partial class MapSelectionScreen : ColorRect
{
    public event System.Action<string>? MapChosen;
    public event System.Action? Continued;
    public event System.Action? LanguageChanged;
    public event System.Action? QuitRequested;
    public event System.Action? DisplaySettingsRequested;
    public event System.Action? FeedbackSettingsRequested;
    public event System.Action? AppearanceSettingsRequested;
    public string SelectedMap { get; private set; }
    public int NpcCount {get;private set;}=3;
    public NpcDifficulty Difficulty {get;private set;}=NpcDifficulty.Medium;
    public LocalGameModel? ModelConfiguration {get;private set;}
    private readonly VBoxContainer _preview = new();
    private bool _previewAvailable=true;
    private Button? _startButton;
    private readonly System.Func<string,int,ScenarioDefinition>? _readSetup;
    private readonly string _language;
    private readonly System.Func<string, ScenarioDefinition> _read;
    private Control? _catalog;
    private void CloseCatalog(){var catalog=_catalog;_catalog=null;catalog?.GetParent()?.RemoveChild(catalog);catalog?.QueueFree();}
    public override void _UnhandledKeyInput(InputEvent e)
    {
        if(_catalog is not null&&e is InputEventKey{Pressed:true,Keycode:Key.Escape})
        {CloseCatalog();GetViewport().SetInputAsHandled();}
    }
    private readonly Label _selected=new();
    private readonly VBoxContainer _preferences=new();
    public VBoxContainer AdditionalSettings=>_preferences;
    public MapSelectionScreen(string language, bool canContinue, System.Func<string, ScenarioDefinition> read, string selected = PlayMaps.CompactValley, NpcDifficulty difficulty=NpcDifficulty.Medium,LocalGameModel? model=null,int npcCount=3,System.Func<string,int,ScenarioDefinition>? readSetup=null)
    {
        new OrdinaryGameOptions(npcCount).Validate();NpcCount=npcCount;_readSetup=readSetup;
        SelectedMap=PlayMaps.UserCatalog.Contains(selected)?selected:PlayMaps.CompactValley;_read=read;_language=language;
        Theme=CompactTabletDesign.Theme;Color=new Color("e8eee5");SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var margin=new MarginContainer();margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);AddChild(margin);
        var split=new HBoxContainer{Name="MainMenuColumns"};split.AddThemeConstantOverride("separation",20);margin.AddChild(split);
        var scroll=new ScrollContainer{HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,SizeFlagsVertical=SizeFlags.ExpandFill};split.AddChild(scroll);
        var controls=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};controls.AddThemeConstantOverride("separation",8);scroll.AddChild(controls);
        controls.AddChild(TabletDesign.Heading("Delsk",32));
        _selected.Name="SelectedMapName";_selected.AutowrapMode=TextServer.AutowrapMode.WordSmart;controls.AddChild(_selected);
        var roster=new HBoxContainer();controls.AddChild(roster);roster.AddChild(new Label{Text=L("NPC","NPC"),SizeFlagsVertical=SizeFlags.ShrinkCenter});
        var count=new SpinBox{Name="NpcCount",MinValue=3,MaxValue=10,Step=1,Rounded=true,Value=npcCount,CustomMinimumSize=new(90,40)};roster.AddChild(count);
        var total=new Label{Name="ParticipantCount",Text=L("Всего: ","Total: ")+(NpcCount+1),SizeFlagsVertical=SizeFlags.ShrinkCenter};roster.AddChild(total);
        count.ValueChanged+=value=>{NpcCount=checked((int)value);new OrdinaryGameOptions(NpcCount).Validate();total.Text=L("Всего: ","Total: ")+(NpcCount+1);ShowPreview();};
        Difficulty=difficulty;ModelConfiguration=model;
        var difficultyRow=new HBoxContainer();controls.AddChild(difficultyRow);difficultyRow.AddChild(new Label{Text=L("Сложность","Difficulty"),SizeFlagsVertical=SizeFlags.ShrinkCenter});
        var choices=new OptionButton{Name="NpcDifficulty",SizeFlagsHorizontal=SizeFlags.ExpandFill};foreach(var text in new[]{L("Лёгкая","Easy"),L("Средняя","Medium"),L("Сложная","Hard")})choices.AddItem(text);
        choices.Selected=Math.Clamp((int)difficulty-1,0,2);difficultyRow.AddChild(choices);choices.ItemSelected+=i=>Difficulty=(NpcDifficulty)(i+1);
        var launch=new HBoxContainer();controls.AddChild(launch);
        _startButton=AddButton(launch,L("Начать","Start"),()=>{if(_previewAvailable)MapChosen?.Invoke(SelectedMap);});_startButton.Name="StartGame";GameUi.Primary(_startButton);
        var resume=AddButton(launch,L("Продолжить","Continue"),()=>Continued?.Invoke());resume.Name="ContinueGame";resume.Disabled=!canContinue;
        resume.TooltipText=canContinue?"":L("Пока нет сохранённой партии","No saved game yet");
        AddButton(controls,L("Выбрать карту","Choose map"),ShowCatalog).Name="ChooseMap";
        var options=new HFlowContainer();_preferences.AddChild(options);
        AddButton(options,L("Экран","Display"),()=>DisplaySettingsRequested?.Invoke());AddButton(options,L("Звук","Audio"),()=>FeedbackSettingsRequested?.Invoke());
        AddButton(options,L("Оформление","Appearance"),()=>AppearanceSettingsRequested?.Invoke());AddButton(options,L("English","Русский"),()=>LanguageChanged?.Invoke());
        var modelSettings=new VBoxContainer();AddNpcSettings(modelSettings,difficulty,model);_preferences.AddChild(new UiDisclosure(L("Локальная модель","Local model"),modelSettings));
        controls.AddChild(new UiDisclosure(L("Дополнительные настройки","More settings"),_preferences));
        AddButton(controls,L("Выход","Quit"),()=>QuitRequested?.Invoke());
        var previewCard=new PanelContainer{Name="SelectedMapPreview",SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill};
        previewCard.AddThemeStyleboxOverride("panel",CompactTabletDesign.Surface("f7f8f1",16));split.AddChild(previewCard);previewCard.AddChild(_preview);_preview.AddThemeConstantOverride("separation",8);
        UiSafeArea.Track(margin,()=>
        {
            var safe=UiSafeArea.Read(this);bool compact=GetViewportRect().Size.Y<500;int gap=compact?12:24;
            margin.AddThemeConstantOverride("margin_left",gap+(int)safe.Left);margin.AddThemeConstantOverride("margin_right",gap+(int)safe.Right);
            margin.AddThemeConstantOverride("margin_top",gap+(int)safe.Top);margin.AddThemeConstantOverride("margin_bottom",gap+(int)safe.Bottom);
            scroll.CustomMinimumSize=new(Math.Clamp(GetViewportRect().Size.X*.34f,270,360),0);
            split.AddThemeConstantOverride("separation",compact?12:24);
        });
        ShowPreview();
    }
    private void AddNpcSettings(VBoxContainer column,NpcDifficulty difficulty,LocalGameModel? model)
    {
        Difficulty=difficulty;ModelConfiguration=model;
        var hybrid=new CheckButton{Text=L("Гибрид · локальная модель","Hybrid · local model"),ButtonPressed=model is not null};column.AddChild(hybrid);
        var settings=new HFlowContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};column.AddChild(settings);
        var name=new LineEdit{Text=model?.Adapter.Model??"qwen3.5:4b",CustomMinimumSize=new(230,0),TooltipText=L("Модель Ollama · localhost:11434","Ollama model · localhost:11434")};settings.AddChild(name);
        var timeout=new SpinBox{MinValue=1,MaxValue=120,Step=1,Value=(model?.Runtime.TimeoutMilliseconds??30000)/1000d,TooltipText=L("Таймаут одного запроса, секунд","Per-request timeout, seconds")};settings.AddChild(timeout);settings.AddChild(new Label{Text=L("сек.","sec")});
        var budget=new SpinBox{MinValue=1,MaxValue=10,Step=1,Value=model?.Policy.MaximumRequestsPerPeriod??2,TooltipText=L("Запросов на NPC за 120 тиков","Requests per NPC per 120 ticks")};settings.AddChild(budget);settings.AddChild(new Label{Text=L("/120 тиков","/120 ticks")});
        var status=new Label{AutowrapMode=TextServer.AutowrapMode.WordSmart};column.AddChild(status);
        void Update(){settings.Visible=hybrid.ButtonPressed;ModelConfiguration=!hybrid.ButtonPressed?null:LocalGameModel.Default with {Adapter=LocalGameModel.Default.Adapter with {Model=name.Text.Trim()},Runtime=new(3,(int)timeout.Value*1000),Policy=LocalGameModel.Default.Policy with {MaximumRequestsPerPeriod=(int)budget.Value}};status.Text=L("Модель выбирает только разрешённый план или бюллетень. На время запроса мир ждёт; UI работает.","The model selects an allowed plan or ballot. The world waits for the request; UI stays responsive.");}
        hybrid.Toggled+=_=>Update();name.TextChanged+=_=>Update();timeout.ValueChanged+=_=>Update();budget.ValueChanged+=_=>Update();Update();
        var probe=new Button{Text=L("Проверить runtime и модель","Check runtime and model")};column.AddChild(probe);
        probe.Pressed+=async()=>
        {
            probe.Disabled=true;status.Text=L("Проверяю…","Checking…");
            try{var config=ModelConfiguration??LocalGameModel.Default;config.Validate();using var adapter=new OllamaAdapter(config.Adapter);using var cancel=new System.Threading.CancellationTokenSource(3000);
                string result=await adapter.CheckAvailabilityAsync(cancel.Token);if(GodotObject.IsInstanceValid(status))status.Text=result=="Available"?L("Модель найдена. Скорость подтвердится только реальным запросом.","Model found. Speed requires a measured inference."):L("Модель недоступна: ","Model unavailable: ")+result;
            }catch(ArgumentException){if(GodotObject.IsInstanceValid(status))status.Text=L("Проверьте имя модели и настройки.","Check model name and settings.");}
            finally{if(GodotObject.IsInstanceValid(probe))probe.Disabled=false;}
        };
    }
    private bool TrySetup(string key,out string reason)
    {
        try
        {
            var scenario=_readSetup?.Invoke(key,NpcCount)??_read(key);
            if(key==PlayMaps.Second&&scenario.Economy is not {Storage:not null,ProductionContent:not null,Fleet:{EquipmentDealerVersion:1}})
                throw new InvalidOperationException("Modern economy required");
            reason="";return true;
        }
        catch(Exception error)when(error is ArgumentException or InvalidOperationException)
        {reason=key==PlayMaps.Second?L("Скоро · требуется адаптация экономики I6-C2","Coming soon · economy adaptation I6-C2 required"):L("Недоступна с текущим профилем","Unavailable with the current profile");return false;}
    }
    private void ShowPreview()
    {
        foreach(Node child in _preview.GetChildren()){_preview.RemoveChild(child);child.QueueFree();}
        _selected.Text=PlayMaps.Name(SelectedMap,_language);
        // Geography always comes from the real map, even when its new-game profile is unsupported.
        _previewAvailable=TrySetup(SelectedMap,out string reason);var scenario=_previewAvailable?(_readSetup?.Invoke(SelectedMap,NpcCount)??_read(SelectedMap)):_read(SelectedMap);
        _startButton!.Disabled=!_previewAvailable;_startButton.TooltipText=reason;
        _preview.AddChild(TabletDesign.Heading(PlayMaps.Name(SelectedMap,_language),24));
        var map=new MapPreview(scenario){Name="MainMapPreview",CustomMinimumSize=new(180,100),SizeFlagsVertical=SizeFlags.ExpandFill,SizeFlagsHorizontal=SizeFlags.ExpandFill};_preview.AddChild(map);
        _preview.AddChild(new Label{Text=PlayMaps.Description(SelectedMap,_language),AutowrapMode=TextServer.AutowrapMode.WordSmart});
        _preview.AddChild(new Label{Text=$"{scenario.MapWidth} × {scenario.MapHeight}"+L(" клеток · "," cells · ")+scenario.ResourceNodes.Select(n=>n.ItemId).Distinct().Count()+L(" ресурсов"," resources"),AutowrapMode=TextServer.AutowrapMode.WordSmart});
        var status=new Label{Name="MapAvailability",Text=_previewAvailable?L("Рекомендуемая карта · готова к игре","Recommended · ready to play"):reason,AutowrapMode=TextServer.AutowrapMode.WordSmart};
        status.AddThemeColorOverride("font_color",new Color(_previewAvailable?"38633d":"83552c"));_preview.AddChild(status);
    }
    private void ShowCatalog()
    {
        var overlay=new ColorRect{Name="MapCatalog",Color=new Color(0.08f,.13f,.11f,.65f),MouseFilter=MouseFilterEnum.Stop};overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);CloseCatalog();AddChild(overlay);_catalog=overlay;
        var panel=new PanelContainer{Theme=CompactTabletDesign.Theme};panel.AddThemeStyleboxOverride("panel",CompactTabletDesign.Surface("e8eee5",12));
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);overlay.AddChild(panel);UiSafeArea.Panel(panel,12,12,12,100);
        var body=new VBoxContainer();panel.AddChild(body);var header=new HBoxContainer();body.AddChild(header);
        header.AddChild(new Label{Text=L("Каталог карт","Map catalog"),SizeFlagsHorizontal=SizeFlags.ExpandFill});
        var close=AddButton(header,L("Закрыть","Close"),CloseCatalog);close.Name="CloseMapCatalog";
        var scroll=new ScrollContainer{HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,SizeFlagsVertical=SizeFlags.ExpandFill};body.AddChild(scroll);
        var list=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};list.AddThemeConstantOverride("separation",8);scroll.AddChild(list);
        foreach(string key in PlayMaps.UserCatalog)
        {
            string choice=key;bool available=TrySetup(key,out string reason);var scenario=available?(_readSetup?.Invoke(key,NpcCount)??_read(key)):_read(key);
            var card=new PanelContainer{Name="MapCard_"+key};card.AddThemeStyleboxOverride("panel",CompactTabletDesign.Surface("f7f8f1",10));list.AddChild(card);
            var row=new HBoxContainer();row.AddThemeConstantOverride("separation",16);card.AddChild(row);
            var text=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};row.AddChild(text);
            var title=TabletDesign.Heading(PlayMaps.Name(key,_language),20);title.AddThemeColorOverride("font_color",new Color("243c30"));text.AddChild(title);
            var description=new Label{Text=PlayMaps.Description(key,_language),AutowrapMode=TextServer.AutowrapMode.WordSmart};description.AddThemeColorOverride("font_color",new Color("425448"));description.AddThemeFontSizeOverride("font_size",14);text.AddChild(description);
            var select=AddButton(text,L("Выбрать","Select")+" · "+$"{scenario.MapWidth} × {scenario.MapHeight}",()=>{SelectedMap=choice;ShowPreview();CloseCatalog();});select.Name="SelectMap_"+key;
            // A future map can be previewed; Start remains disabled when the profile rejects it.
            if(!available){select.TooltipText=reason;var status=new Label{Text=reason,AutowrapMode=TextServer.AutowrapMode.WordSmart};status.AddThemeColorOverride("font_color",new Color("83552c"));status.AddThemeFontSizeOverride("font_size",13);text.AddChild(status);}
            var map=new MapPreview(scenario){Name="CatalogPreview_"+key,CustomMinimumSize=new(200,125),SizeFlagsVertical=SizeFlags.ExpandFill};row.AddChild(map);
            UiSafeArea.Track(map,()=>map.CustomMinimumSize=new(Math.Clamp(GetViewportRect().Size.X*.30f,200,380),GetViewportRect().Size.Y<500?110:180));
        }
        close.GrabFocus();
    }
    private static Button AddButton(Container parent, string text, System.Action action)
    { var button = new Button { Text = text, FocusMode = FocusModeEnum.All, CustomMinimumSize=new(80,40),AutowrapMode=TextServer.AutowrapMode.Off }; button.Pressed += action; parent.AddChild(button);return button; }
    private string L(string ru, string en) => PlayerText.Local(_language, ru, en);
}

public partial class MapPreview(ScenarioDefinition scenario) : Control
{
    public override void _Ready(){MouseFilter=MouseFilterEnum.Ignore;Resized+=QueueRedraw;}
    public override void _Draw()
    {
        float cell = Mathf.Min(Size.X / scenario.MapWidth, Size.Y / scenario.MapHeight);
        var offset = (Size - new Vector2(scenario.MapWidth, scenario.MapHeight) * cell) / 2;
        const float padding=0;
        Rect2 Area(int x, int z) => new(offset + new Vector2(x, z) * cell + Vector2.One*padding, Vector2.One * (cell - 2*padding));
        DrawRect(new Rect2(offset,new Vector2(scenario.MapWidth,scenario.MapHeight)*cell),new Color("729365"));
        if(scenario.Layout is {} layout)
        {foreach(var c in layout.RoadCorridorCells)DrawRect(Area(c.X,c.Z),new Color("88947c"));foreach(var c in layout.InitialCityDirtCells)DrawRect(Area(c.X,c.Z),new Color("a39578"));foreach(var site in layout.CivicObjects)foreach(var c in site.Footprint)DrawRect(Area(c.X,c.Z),new Color(site.Kind=="dealership"?"947b6c":"c6bda2"));}
        foreach(var blocked in scenario.BlockedCells)DrawRect(Area(blocked.X,blocked.Z),new Color("566b55"));
        foreach(var plot in scenario.Plots??[])
            DrawRect(Area(plot.Position.X,plot.Position.Z),new Color(plot.IsCorridor?"88947c":plot.IsShopSite||plot.ShopParticipantId.HasValue?"a795b0":"bdac80"));
        if(scenario.Layout is {} designed)
        foreach(var lot in designed.IndustrialLots)
        {
            var cells=lot.Cells.ToHashSet();
            foreach(var c in cells)
            {
                var r=Area(c.X,c.Z);var color=new Color("8b7955");float line=Math.Max(.5f,cell*.06f);
                if(!cells.Contains(new(c.X-1,c.Z)))DrawLine(r.Position,new(r.Position.X,r.End.Y),color,line);
                if(!cells.Contains(new(c.X+1,c.Z)))DrawLine(new(r.End.X,r.Position.Y),r.End,color,line);
                if(!cells.Contains(new(c.X,c.Z-1)))DrawLine(r.Position,new(r.End.X,r.Position.Y),color,line);
                if(!cells.Contains(new(c.X,c.Z+1)))DrawLine(new(r.Position.X,r.End.Y),r.End,color,line);
            }
        }
        foreach(var building in scenario.Buildings??[])
        foreach(var relative in scenario.BuildingTypes!.Single(t=>t.Id==building.TypeId).Footprint)
            DrawRect(Area(building.Position.X+relative.X,building.Position.Z+relative.Z),new Color("886d57"));
        foreach (var node in scenario.ResourceNodes)
            MapSymbols.Draw(this, "resource", Area(node.Position.X,node.Position.Z), node.ItemId);
        if (scenario.CapitalDemand is { } demand)
            MapSymbols.Draw(this, "capital", Area(demand.Terminal.X,demand.Terminal.Z));
    }
}
