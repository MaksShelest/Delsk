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
    public string SelectedMap { get; private set; }
    public NpcDifficulty Difficulty {get;private set;}=NpcDifficulty.Medium;
    public LocalGameModel? ModelConfiguration {get;private set;}
    private readonly VBoxContainer _preview = new();
    private readonly string _language;
    private readonly System.Func<string, ScenarioDefinition> _read;
    public MapSelectionScreen(string language, bool canContinue, System.Func<string, ScenarioDefinition> read, string selected = PlayMaps.First, NpcDifficulty difficulty=NpcDifficulty.Medium,LocalGameModel? model=null)
    {
        SelectedMap = selected; _read = read; _language = language;
        Color = new Color("26383d"); SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var scroll=new ScrollContainer{HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);AddChild(scroll);
        var center = new CenterContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill};scroll.AddChild(center);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(600, 0) }; center.AddChild(column);
        column.AddChild(new Label { Text = "Delsk", HorizontalAlignment = HorizontalAlignment.Center });
        column.AddChild(_preview); ShowPreview();
        AddNpcSettings(column,difficulty,model);
        AddButton(column, L("Сменить карту", "Change map"), ShowCatalog);
        AddButton(column, L("Начать", "Start"), () => MapChosen?.Invoke(SelectedMap));
        if (canContinue) AddButton(column, L("Продолжить", "Continue"), () => Continued?.Invoke());
        AddButton(column, L("Настройки экрана", "Display settings"), () => DisplaySettingsRequested?.Invoke());
        AddButton(column,L("Реплики и звук","Speech and audio"),()=>FeedbackSettingsRequested?.Invoke());
        AddButton(column, L("English", "Русский"), () => LanguageChanged?.Invoke());
        AddButton(column, L("Выйти из игры", "Quit"), () => QuitRequested?.Invoke());
    }
    private void AddNpcSettings(VBoxContainer column,NpcDifficulty difficulty,LocalGameModel? model)
    {
        Difficulty=difficulty;ModelConfiguration=model;
        var row=new HBoxContainer();column.AddChild(row);row.AddChild(new Label{Text=L("Сложность NPC","NPC difficulty")});
        var choices=new OptionButton();foreach(var text in new[]{L("Лёгкая","Easy"),L("Средняя","Medium"),L("Сложная","Hard")})choices.AddItem(text);
        choices.Selected=Math.Clamp((int)difficulty-1,0,2);row.AddChild(choices);choices.ItemSelected+=i=>Difficulty=(NpcDifficulty)(i+1);
        var hybrid=new CheckButton{Text=L("Гибрид · локальная модель","Hybrid · local model"),ButtonPressed=model is not null};column.AddChild(hybrid);
        var settings=new HBoxContainer();column.AddChild(settings);
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
    private void ShowPreview()
    {
        foreach (Node child in _preview.GetChildren()) { _preview.RemoveChild(child); child.QueueFree(); }
        var scenario = _read(SelectedMap);
        _preview.AddChild(new Label { Text = PlayMaps.Name(SelectedMap, _language), HorizontalAlignment = HorizontalAlignment.Center });
        _preview.AddChild(new MapPreview(scenario) { CustomMinimumSize = new Vector2(600, 240) });
        _preview.AddChild(new Label { Text = $"{scenario.MapWidth} × {scenario.MapHeight}" + L(" · Мир запустится только по «Начать».", " · The world starts only after Start."), HorizontalAlignment = HorizontalAlignment.Center });
    }
    private void ShowCatalog()
    {
        var dialog = new AcceptDialog { Title = L("Каталог карт", "Map catalog"), Size = new Vector2I(720, 560) };
        dialog.GetOkButton().Text = L("Отмена", "Cancel");
        var scroll = new ScrollContainer(); dialog.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; scroll.AddChild(list);
        foreach (string key in new[] { PlayMaps.First, PlayMaps.Second, PlayMaps.Third, PlayMaps.Balanced })
        {
            string choice = key;
            list.AddChild(new Label { Text = PlayMaps.Name(key, _language) });
            list.AddChild(new MapPreview(_read(key)) { CustomMinimumSize = new Vector2(640, 210) });
            AddButton(list, L("Выбрать", "Select"), () => { SelectedMap = choice; ShowPreview(); dialog.Hide(); dialog.QueueFree(); });
        }
        dialog.Confirmed += () => dialog.QueueFree(); dialog.Canceled += () => dialog.QueueFree();
        AddChild(dialog); dialog.PopupCentered();
    }
    private static void AddButton(Container parent, string text, System.Action action)
    { var button = new Button { Text = text, FocusMode = FocusModeEnum.None }; button.Pressed += action; parent.AddChild(button); }
    private string L(string ru, string en) => PlayerText.Local(_language, ru, en);
}

public partial class MapPreview(ScenarioDefinition scenario) : Control
{
    public override void _Draw()
    {
        float cell = Mathf.Min(Size.X / scenario.MapWidth, Size.Y / scenario.MapHeight);
        var offset = (Size - new Vector2(scenario.MapWidth, scenario.MapHeight) * cell) / 2;
        Rect2 Area(int x, int z) => new(offset + new Vector2(x, z) * cell + Vector2.One, Vector2.One * (cell - 2));
        for (int x = 0; x < scenario.MapWidth; x++) for (int z = 0; z < scenario.MapHeight; z++)
            DrawRect(Area(x,z), new Color("729365"));
        foreach (var plot in scenario.Plots ?? [])
            MapSymbols.Draw(this, plot.IsCorridor ? "corridor" : plot.IsShopSite || plot.ShopParticipantId.HasValue ? "shop-plot" : "plot-free", Area(plot.Position.X,plot.Position.Z));
        foreach (var node in scenario.ResourceNodes)
            MapSymbols.Draw(this, "resource", Area(node.Position.X,node.Position.Z), node.ItemId);
        if (scenario.CapitalDemand is { } demand)
            MapSymbols.Draw(this, "capital", Area(demand.Terminal.X,demand.Terminal.Z));
    }
}
