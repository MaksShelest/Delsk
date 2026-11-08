using Agents.Algorithmic;
using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class MapEditor
{
    private void BuildChrome()
    {
        var top=Bar();
        void Command(string name,string text,System.Action action,bool primary=false,string hint="")
        {
            var button=new Button{Name=name,Text=text,TooltipText=hint,CustomMinimumSize=new(0,32)};button.AddThemeFontSizeOverride("font_size",13);
            if(primary)GameUi.Primary(button);button.Pressed+=action;top.AddChild(button);
        }
        Command("NewMap",L("Новая","New"),NewMap);
        Command("SaveDraft",L("Сохранить","Save"),()=>SaveQuick(false),true,L("Сохранить карту в «Мои карты».","Save the map to My maps."));
        Command("ValidateMap",L("Проверить","Check"),Validate,false,L("Показать ошибки и предупреждения.","Show errors and warnings."));
        Command("PlayMap",L("Играть","Play"),Play,true,L("Проверить карту и начать обычную партию.","Check the map and start an ordinary game."));
        Command("Preview3D","3D",()=>SetPreview(true),false,L("Смотреть карту в 3D.","View the map in 3D."));
        Command("ViewLeft","↶ 90°",()=>RotateView(-1),false,L("Повернуть только изображение. Координаты не меняются.","Rotate the view only. Coordinates stay the same."));
        Command("ViewRight","↷ 90°",()=>RotateView(1),false,L("Повернуть только изображение. Координаты не меняются.","Rotate the view only. Coordinates stay the same."));
        Command("UndoChange",L("Отменить","Undo"),()=>{Draft.Undo();Refresh();});
        Command("RedoChange",L("Повторить","Redo"),()=>{Draft.Redo();Refresh();});
        Command("OpenMap",L("Открыть","Open"),()=>Choose("open"),false,L("Открыть файл JSON.","Open a JSON file."));
        Command("ImportMap",L("Импорт","Import"),()=>Choose("import"),false,L("Импортировать JSON в черновик.","Import JSON into the draft."));
        Command("ExportMap",L("Экспорт","Export"),()=>Choose("export"),false,L("Экспортировать проверенный JSON.","Export the checked JSON."));
        Command("Language",_language=="ru"?"EN":"RU",()=>Callable.From(ToggleLanguage).CallDeferred());
        Command("BackToMenu",L("В меню","Menu"),()=>GetTree().ChangeSceneToFile("res://Playfield.tscn"));
        var left=Panel(8,54,LeftMargin-16);
        var scroll=new ScrollContainer{Name="ToolScroll",SizeFlagsVertical=SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};left.AddChild(scroll);
        var tools=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};tools.AddThemeConstantOverride("separation",6);scroll.AddChild(tools);
        var group=new ButtonGroup();
        void Group(string title,params (string Id,string Icon,string Ru,string En,string HintRu,string HintEn)[] items)
        {
            var column=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};column.AddThemeConstantOverride("separation",3);
            foreach(var item in items)column.AddChild(ToolButton(item,group));
            tools.AddChild(new UiDisclosure(title,column,true));
        }
        var edit=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};edit.AddThemeConstantOverride("separation",3);
        edit.AddChild(ToolButton(("select","select","Выделить","Select","Щёлкните клетку: виден весь участок, его площадь и въезд.","Click a cell to see the whole lot, its area and entrance."),group));
        edit.AddChild(ToolButton(("move","move","Переместить","Move","Сначала выделите участок, затем щёлкните клетку, куда перенести его целиком.","Select a lot, then click where the whole lot should move."),group));
        edit.AddChild(ToolButton(("erase","erase","Стереть","Erase","Сотрите дорогу, одну клетку участка или другой объект.","Erase a road, one lot cell or another object."),group));
        edit.AddChild(ToolCommand("RotateContent","rotate",L("Повернуть","Rotate"),L("Повернуть содержимое карты на 90°. Это меняет саму карту и отменяется одним шагом.","Rotate the map contents by 90°. This changes the world and is one undo step."),RotateContent));
        tools.AddChild(new UiDisclosure(L("Редактирование","Edit"),edit,true));
        Group(L("Территория","Land"),
            ("road","road","Дорога","Road","Ведите мышью: дорога шириной в одну клетку.","Drag to paint a one-cell-wide road."),
            ("lot","lot","Участок","Lot","Клетка, прямоугольник или шаблон 2×3. Форму можно менять, не удаляя участок.","Paint a cell, drag a rectangle, or place the 2×3 template. Reshape a lot without deleting it."),
            ("resource","resource","Ресурс","Resource","Выберите ресурс и закрасьте его область.","Choose a resource, then paint its area."),
            ("stall","stall","Лавка","Stall","Поставьте или снимите торговую клетку.","Place or remove a market stall."));
        Group(L("Город","Town"),
            ("town_hall","town","Ратуша","Town hall","Поставьте ратушу 3×3.","Place the 3×3 town hall."),
            ("terminal","terminal","Биржа","Exchange","Поставьте биржу и терминал.","Place the exchange terminal."),
            ("dealership","dealer","Автосалон","Dealership","Поставьте автосалон 5×5.","Place the 5×5 dealership."),
            ("parking","parking","Места техники","Machine bays","Две клетки у автосалона для добывающих машин, по одной на клетку. Это не городская парковка.","Two dealership cells for extractor machines, one each. This is not a town car park."));
        Group(L("Служебные","Markers"),
            ("spawn","spawn","Появление","Spawns","Точки появления стоят на дороге, по числу участников.","Spawn points sit on roads, one per participant."),
            ("entry","entry","Въезд","Entrance","Выделите участок и щёлкните соседнюю клетку дороги.","Select a lot, then click a neighboring road cell."),
            ("turn","turn","Разворот","Turn","Точка разворота на уже нарисованной дороге.","A turn marker on an existing road."));
        _lotModes=new HBoxContainer{Visible=_tool=="lot"};_lotModes.AddThemeConstantOverride("separation",3);
        var lotGroup=new ButtonGroup();LotMode("cell",L("Клетка","Cell"),lotGroup);LotMode("rect",L("Прямоугольник","Rectangle"),lotGroup);LotMode("template",L("2×3","2×3"),lotGroup);
        tools.AddChild(_lotModes);
        _resourceList=new VBoxContainer{Visible=_tool=="resource",SizeFlagsHorizontal=SizeFlags.ExpandFill};_resourceList.AddThemeConstantOverride("separation",2);
        var resources=new ButtonGroup();
        foreach(var item in MapDraft.ResourceItems)
        {
            string id=item;var button=new Button{Name="Resource_"+item,Text=PlayerText.Item(_language,item),ToggleMode=true,ButtonGroup=resources,ButtonPressed=item==_item,Alignment=HorizontalAlignment.Left,Icon=GameUiArt.Item(item),ExpandIcon=false,CustomMinimumSize=new(0,28)};
            button.AddThemeConstantOverride("icon_max_width",18);button.AddThemeFontSizeOverride("font_size",13);
            button.TooltipText=PlayerText.Item(_language,item);button.Pressed+=()=>_item=id;_resourceList.AddChild(button);
        }
        var grouped=new Button{Name="NewResourceGroup",Text=L("Новая группа","New group"),CustomMinimumSize=new(0,28)};grouped.AddThemeFontSizeOverride("font_size",13);
        grouped.Pressed+=()=>{Draft.BeginResourceGroup(_item);_tool="resource";Refresh();};_resourceList.AddChild(grouped);tools.AddChild(_resourceList);
        var right=Panel(0,54,RightMargin-16,true);
        var column=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill};column.AddThemeConstantOverride("separation",4);right.AddChild(column);
        _name=Field(L("Название","Name"));_id=Field(L("Идентификатор","Identifier"));
        _name.TextChanged+=value=>Draft.Name=value;_id.TextChanged+=value=>{Draft.Id=value.Trim();if(UserMaps.IsUserId(Draft.Id))_savedId=Draft.Id;};
        column.AddChild(_name);column.AddChild(_id);
        var size=new HBoxContainer();_width=Number();_height=Number();size.AddChild(_width);size.AddChild(_height);column.AddChild(size);
        var resize=new Button{Name="ResizeMap",Text=L("Размер","Size"),CustomMinimumSize=new(0,30)};resize.Pressed+=()=>{Draft.Resize((int)_width.Value,(int)_height.Value);FrameMap();Refresh();};column.AddChild(resize);
        column.AddChild(new Label{Text=L("Участники","Participants")});
        _participants=new SpinBox{Name="Participants",MinValue=4,MaxValue=64,Step=1,Rounded=true,CustomMinimumSize=new(0,30)};
        _participants.ValueChanged+=value=>{Draft.SetParticipants((int)value);Refresh();};column.AddChild(_participants);
        var fit=new Button{Name="FitMap",Text=L("По размеру","Fit map"),CustomMinimumSize=new(0,30)};fit.Pressed+=FitMap;column.AddChild(fit);
        var propertyScroll=new ScrollContainer{Name="PropertyScroll",CustomMinimumSize=new(0,90),SizeFlagsVertical=SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};column.AddChild(propertyScroll);
        _properties=new Label{Name="PropertiesBody",AutowrapMode=TextServer.AutowrapMode.WordSmart,SizeFlagsHorizontal=SizeFlags.ExpandFill};_properties.AddThemeFontSizeOverride("font_size",13);propertyScroll.AddChild(_properties);
        _issueSummary=new Label{Name="IssueSummary",AutowrapMode=TextServer.AutowrapMode.WordSmart};_issueSummary.AddThemeFontSizeOverride("font_size",13);column.AddChild(_issueSummary);
        var issueScroll=new ScrollContainer{Name="IssueScroll",CustomMinimumSize=new(0,140),SizeFlagsVertical=SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};column.AddChild(issueScroll);
        _issues=new VBoxContainer{Name="IssueList",SizeFlagsHorizontal=SizeFlags.ExpandFill};_issues.AddThemeConstantOverride("separation",2);issueScroll.AddChild(_issues);
        _status=new Label{Name="ValidationText",AutowrapMode=TextServer.AutowrapMode.WordSmart};_status.AddThemeFontSizeOverride("font_size",14);_status.AddThemeColorOverride("font_color",new Color("1a241c"));column.AddChild(_status);
        var files=new HBoxContainer();
        var copy=new Button{Name="CopySavePath",Text=L("Путь","Path"),CustomMinimumSize=new(0,28),Disabled=true};copy.Pressed+=()=>{if(SavedPath is{} path)DisplayServer.ClipboardSet(path);};files.AddChild(copy);
        var folder=new Button{Name="ShowSaveFolder",Text=L("Папка","Folder"),CustomMinimumSize=new(0,28),Disabled=true};folder.Pressed+=()=>{if(SavedPath is{} path)OS.ShellShowInFileManager(path);};files.AddChild(folder);
        var backup=new Button{Name="BackupJson",Text=L("Копия JSON","JSON copy"),CustomMinimumSize=new(0,28),TooltipText=L("Резервная копия черновика. Для игры она не проверяется.","A draft backup. It is not checked for play.")};backup.Pressed+=()=>Choose("copy");files.AddChild(backup);
        column.AddChild(files);
        BuildPreviewOverlay();SyncFields();ShowIssues();
        LineEdit Field(string hint)=>new(){PlaceholderText=hint,CustomMinimumSize=new(0,30)};
        SpinBox Number()=>new(){MinValue=4,MaxValue=128,Step=1,Rounded=true,CustomMinimumSize=new(70,30)};
    }
    private Button ToolButton((string Id,string Icon,string Ru,string En,string HintRu,string HintEn) item,ButtonGroup group)
    {
        var button=new Button{Name="Tool_"+item.Id,Text=L(item.Ru,item.En),ToggleMode=true,ButtonGroup=group,ButtonPressed=item.Id==_tool,Alignment=HorizontalAlignment.Left,Icon=MapEditorIcons.Get(item.Icon),ExpandIcon=false,CustomMinimumSize=new(0,30),SizeFlagsHorizontal=SizeFlags.ExpandFill};
        button.AddThemeConstantOverride("icon_max_width",18);button.AddThemeFontSizeOverride("font_size",13);button.TooltipText=L(item.HintRu,item.HintEn);
        button.Toggled+=on=>{if(on){_tool=item.Id;if(_lotModes is not null)_lotModes.Visible=_tool=="lot";if(_resourceList is not null)_resourceList.Visible=_tool=="resource";}};
        return button;
    }
    private Button ToolCommand(string name,string icon,string text,string hint,System.Action action)
    {
        var button=new Button{Name=name,Text=text,Alignment=HorizontalAlignment.Left,Icon=MapEditorIcons.Get(icon),ExpandIcon=false,CustomMinimumSize=new(0,30),SizeFlagsHorizontal=SizeFlags.ExpandFill,TooltipText=hint};
        button.AddThemeConstantOverride("icon_max_width",18);button.AddThemeFontSizeOverride("font_size",13);button.Pressed+=action;return button;
    }
    private void LotMode(string id,string text,ButtonGroup group)
    {
        var button=new Button{Name="LotMode_"+id,Text=text,ToggleMode=true,ButtonGroup=group,ButtonPressed=id==_lotMode,CustomMinimumSize=new(0,28)};button.AddThemeFontSizeOverride("font_size",12);
        button.TooltipText=id switch{"cell"=>L("Одна клетка, затем кисть по соседним.","One cell, then paint neighbors."),"rect"=>L("Протяните прямоугольник.","Drag a rectangle."),_=>L("Готовый участок 2×3.","Ready-made 2×3 lot.")};
        button.Toggled+=on=>{if(on)_lotMode=id;};_lotModes.AddChild(button);
    }
    private void BuildPreviewOverlay()
    {
        _previewOverlay=new Control{Name="PreviewOverlay",MouseFilter=MouseFilterEnum.Ignore,Visible=false};_previewOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);AddChild(_previewOverlay);
        var row=new HBoxContainer{Name="PreviewTools"};row.SetAnchorsPreset(LayoutPreset.TopRight);row.OffsetLeft=-430;row.OffsetTop=12;row.OffsetRight=-12;row.OffsetBottom=48;row.AddThemeConstantOverride("separation",6);
        void Add(string name,string text,System.Action action){var button=new Button{Name=name,Text=text,MouseFilter=MouseFilterEnum.Stop,CustomMinimumSize=new(0,34)};button.Pressed+=action;row.AddChild(button);}
        Add("ResetView",L("Сбросить вид","Reset view"),ResetPreviewView);Add("TopView",L("Вид сверху","Top view"),TopPreviewView);Add("LeavePreview",L("К редактору","Back to editor"),()=>SetPreview(false));
        _previewOverlay.AddChild(row);
        var hintBox=new PanelContainer{Name="PreviewHint",MouseFilter=MouseFilterEnum.Ignore};
        hintBox.AddThemeStyleboxOverride("panel",new StyleBoxFlat{BgColor=new Color("f7f8f1"),ContentMarginLeft=10,ContentMarginRight=10,ContentMarginTop=6,ContentMarginBottom=6,CornerRadiusTopLeft=8,CornerRadiusTopRight=8,CornerRadiusBottomLeft=8,CornerRadiusBottomRight=8});
        hintBox.SetAnchorsPreset(LayoutPreset.BottomLeft);hintBox.OffsetLeft=16;hintBox.OffsetTop=-52;hintBox.OffsetRight=520;hintBox.OffsetBottom=-12;
        var hint=new Label{Text=L("ПКМ — вращение, СКМ — сдвиг, колесо — масштаб","RMB orbit, MMB pan, wheel zoom")};
        hint.AddThemeColorOverride("font_color",new Color("1a241c"));hint.AddThemeFontSizeOverride("font_size",14);hintBox.AddChild(hint);_previewOverlay.AddChild(hintBox);
    }
    private void ShowIssues()
    {
        if(_issues is null)return;
        bool audited=!_painting;
        var notes=_painting?Draft.Notes(_language,DealerVersion()):Draft.Review(Economy(),_language);
        Present(notes,audited);
    }
    private void Present(IReadOnlyList<MapNote> notes,bool audited)
    {
        foreach(Node child in _issues.GetChildren())child.QueueFree();
        int errors=notes.Count(n=>!n.Warning),warnings=notes.Count(n=>n.Warning);
        _issueSummary.Text=!audited?L("Черновик изменён. Проверка запуска будет после штриха.","The draft changed. The launch check follows the stroke.")
            :errors==0&&warnings==0?L("Ошибок нет. Карта готова к обычной партии.","No errors. The map is ready for an ordinary game.")
            :L($"Ошибок: {errors}. Предупреждений: {warnings}.",$"Errors: {errors}. Warnings: {warnings}.");
        IssueText=string.Join('\n',notes.Select(n=>n.Text));
        if(FindChild("PlayMap",true,false) is Button play){play.Disabled=errors>0||!audited;if(errors>0)play.TooltipText=notes.First(n=>!n.Warning).Text;}
        if(_pendingFix is{} pending)
        {
            var row=new HBoxContainer();
            var apply=new Button{Name="ApplyFix",Text=L("Применить","Apply")};apply.Pressed+=()=>Callable.From(()=>{if(Draft.ApplyFix(pending)){_pendingFix=null;Refresh();}}).CallDeferred();row.AddChild(apply);
            var cancel=new Button{Name="CancelFix",Text=L("Отмена","Cancel")};cancel.Pressed+=()=>Callable.From(()=>{_pendingFix=null;QueueRedraw();ShowIssues();}).CallDeferred();row.AddChild(cancel);
            _issues.AddChild(row);
        }
        int index=0;
        void Section(string title,bool warning)
        {
            var rows=notes.Where(n=>n.Warning==warning).ToArray();if(rows.Length==0)return;
            var heading=new Label{Text=title};heading.AddThemeColorOverride("font_color",new Color(warning?"83552c":"7a2e2e"));heading.AddThemeFontSizeOverride("font_size",13);_issues.AddChild(heading);
            foreach(var note in rows)
            {
                var line=new HBoxContainer();
                var at=note.At;var button=new Button{Name="Issue_"+index,Text=note.Text,Alignment=HorizontalAlignment.Left,AutowrapMode=TextServer.AutowrapMode.WordSmart,SizeFlagsHorizontal=SizeFlags.ExpandFill};
                button.AddThemeFontSizeOverride("font_size",12);
                if(at is{} cell){int x=cell.X,z=cell.Z;button.Pressed+=()=>Callable.From(()=>FocusCell(x,z)).CallDeferred();}
                line.AddChild(button);
                if(note.Fix is{} fix){string code=fix;var repair=new Button{Name="Fix_"+index,Text=L("Исправить","Fix")};repair.Pressed+=()=>Callable.From(()=>{_pendingFix=code;_status.Text=Draft.DescribeFix(code,_language)??"";QueueRedraw();ShowIssues();}).CallDeferred();line.AddChild(repair);}
                _issues.AddChild(line);index++;
            }
        }
        Section(L("Ошибки","Errors"),false);Section(L("Предупреждения","Warnings"),true);
    }
    private int DealerVersion(){try{return Economy().World.Rules.Fleet?.EquipmentDealerVersion??2;}catch(Exception){return 2;}}
    private void Validate(){var notes=Draft.Review(Economy(),_language);Present(notes,true);_status.Text=notes.All(n=>n.Warning)?L("Карта готова к обычной партии.","The map is ready for an ordinary game."):L("Игра пока недоступна. В списке указана причина.","Play is not available yet. The list names the reason.");}
    private void Play()
    {
        AssignUserId();SyncFields();var notes=Draft.Review(Economy(),_language);Present(notes,true);
        if(notes.Any(n=>!n.Warning)){_status.Text=L("Игра не запущена. В списке указана причина.","The game was not started. The list names the reason.");return;}
        SaveQuick(true);
    }
    private void NewMap(){Draft=MapDraft.Create(32,25);Draft.Name=L("Новая карта","New map");_selected=null;_selectedLot=null;_savedId=null;SavedPath=null;SyncFields();FrameMap();Refresh();}
    private void ToggleLanguage(){_language=_language=="ru"?"en":"ru";foreach(Node child in _chrome.GetChildren())child.Free();if(IsInstanceValid(_previewOverlay))_previewOverlay.QueueFree();BuildChrome();}
    private void AssignUserId()
    {
        if(UserMaps.IsUserId(Draft.Id)){_savedId=Draft.Id;return;}
        Draft.Id=_savedId??UserMaps.SuggestId(string.IsNullOrWhiteSpace(Draft.Name)?L("Новая карта","New map"):Draft.Name,ExistingIds());
        _savedId=Draft.Id;
    }
    private void SaveQuick(bool thenPlay)
    {
        AssignUserId();var path=System.IO.Path.Combine(MapDirectory(),Draft.Id+".json");
        if(System.IO.File.Exists(path)&&UserMaps.NeedsOverwriteConfirm(System.IO.File.ReadAllText(path),Draft.Id)){ConfirmOverwrite(path,thenPlay);return;}
        if(WriteMap(path)&&thenPlay)ShowPlayChoices();
    }
    private bool WriteMap(string path)
    {
        try
        {
            var directory=System.IO.Path.GetDirectoryName(path)!;System.IO.Directory.CreateDirectory(directory);
            var temporary=path+".tmp";System.IO.File.WriteAllText(temporary,Draft.ToJson());System.IO.File.Move(temporary,path,true);
            SavedPath=path;_savedId=Draft.Id;_status.Text=L("Карта сохранена.","Map saved.")+"\n"+path;SyncFields();
            if(FindChild("CopySavePath",true,false) is Button copy)copy.Disabled=false;
            if(FindChild("ShowSaveFolder",true,false) is Button folder)folder.Disabled=false;
            return true;
        }
        catch(Exception error){_status.Text=L("Не удалось сохранить карту. Проверьте доступ к папке. ","The map could not be saved. Check folder access. ")+error.Message;return false;}
    }
    private void ConfirmOverwrite(string path,bool thenPlay)
    {
        var dialog=new ConfirmationDialog{OkButtonText=L("Заменить","Replace"),CancelButtonText=L("Отмена","Cancel"),DialogText=L("В каталоге уже есть другая карта с этим именем. Заменить её?","Another map already uses this name. Replace it?")};
        AddChild(dialog);dialog.Confirmed+=()=>{if(WriteMap(path)&&thenPlay)ShowPlayChoices();dialog.QueueFree();};dialog.Canceled+=()=>dialog.QueueFree();dialog.PopupCentered(new Vector2I(480,160));
    }
    private void ShowPlayChoices()
    {
        if(SavedPath is null)return;
        var dialog=new AcceptDialog{Title=L("Играть на этой карте","Play this map"),OkButtonText=L("Играть","Play")};
        var box=new VBoxContainer();box.AddThemeConstantOverride("separation",6);
        int maximum=Math.Clamp(Draft.MaxParticipants-1,3,10);
        box.AddChild(new Label{Text=L("Количество NPC","NPC count")});
        var count=new SpinBox{Name="PlayNpcCount",MinValue=3,MaxValue=maximum,Value=Math.Min(3,maximum),Rounded=true,CustomMinimumSize=new(120,32)};box.AddChild(count);
        box.AddChild(new Label{Text=L("Сложность","Difficulty")});
        var difficulty=new OptionButton{Name="PlayDifficulty"};foreach(var text in new[]{L("Лёгкая","Easy"),L("Средняя","Medium"),L("Сложная","Hard")})difficulty.AddItem(text);difficulty.Selected=1;box.AddChild(difficulty);
        dialog.AddChild(box);AddChild(dialog);
        dialog.Confirmed+=()=>{string path=SavedPath;int npcs=(int)count.Value;var level=(NpcDifficulty)(difficulty.Selected+1);dialog.QueueFree();MapHandoff.QueuePlay(path,npcs,level);GetTree().ChangeSceneToFile("res://Playfield.tscn");};
        dialog.PopupCentered(new Vector2I(420,230));
    }
    private System.Collections.Generic.IEnumerable<string> ExistingIds()
    {
        var directory=MapDirectory();if(!System.IO.Directory.Exists(directory))yield break;
        foreach(var file in System.IO.Directory.EnumerateFiles(directory,"*.json"))
        {
            string id="";try{id=MapDraft.Parse(System.IO.File.ReadAllText(file)).Id;}catch(Exception){continue;}
            if(id.Length>0)yield return id;
        }
    }
    private static string MapDirectory(){var path=ProjectSettings.GlobalizePath("user://maps");System.IO.Directory.CreateDirectory(path);return path;}
    private static EconomyProfile Economy()
    {
        var mobile=EconomyProfile.Read(Text("Play.i4-mobile-equipment.json")??throw new System.InvalidOperationException("Нет профиля техники"));
        var economy=VNextWageScheduleProfile.Create(VNextEquipmentClassesProfile.Create(mobile));
        var read=VNextBalanceReader.Read(System.Text.Encoding.UTF8.GetBytes(Text("Play.vnext-balance.json")??throw new System.InvalidOperationException("Нет баланса")));
        if(!read.IsValid||economy.VNext is null||read.Sha256!=economy.VNext.FullBalanceSha256)throw new System.InvalidOperationException("Full balance/profile mismatch");
        return economy;
    }
    private HBoxContainer Bar()
    {
        var bar=new HBoxContainer{Name="EditorToolbar"};bar.AddThemeConstantOverride("separation",4);
        var panel=new PanelContainer();panel.AddThemeStyleboxOverride("panel",CompactTabletDesign.Surface("f7f8f1",8));panel.SetAnchorsPreset(LayoutPreset.TopWide);panel.OffsetTop=6;panel.OffsetLeft=8;panel.OffsetRight=-8;panel.OffsetBottom=46;
        var scroll=new ScrollContainer{HorizontalScrollMode=ScrollContainer.ScrollMode.Auto,VerticalScrollMode=ScrollContainer.ScrollMode.Disabled,CustomMinimumSize=new(0,34)};
        _chrome.AddChild(panel);panel.AddChild(scroll);scroll.AddChild(bar);return bar;
    }
    private MarginContainer Panel(float x,float y,float width,bool rightEdge=false)
    {
        var panel=new PanelContainer();panel.AddThemeStyleboxOverride("panel",CompactTabletDesign.Surface("f7f8f1",12));
        panel.AnchorBottom=1;panel.OffsetTop=y;panel.OffsetBottom=-8;
        if(rightEdge){panel.AnchorLeft=1;panel.AnchorRight=1;panel.OffsetLeft=-(width+8);panel.OffsetRight=-8;}
        else{panel.OffsetLeft=x;panel.OffsetRight=x+width;}
        _chrome.AddChild(panel);
        var margin=new MarginContainer();margin.AddThemeConstantOverride("margin_left",8);margin.AddThemeConstantOverride("margin_right",8);margin.AddThemeConstantOverride("margin_top",8);margin.AddThemeConstantOverride("margin_bottom",8);panel.AddChild(margin);return margin;
    }
    private FileDialog CreateFiles()
    {
        var dialog=new FileDialog{Name="Files",Access=FileDialog.AccessEnum.Filesystem,UseNativeDialog=false,Filters=["*.json ; Delsk JSON"],Theme=DialogTheme()};
        dialog.AboutToPopup+=()=>PaintDialog(dialog);return dialog;
    }
    private void Choose(string job)
    {
        _fileJob=job;_files.FileMode=job is "open" or "import"?FileDialog.FileModeEnum.OpenFile:FileDialog.FileModeEnum.SaveFile;
        _files.Title=job switch{"export"=>L("Экспортировать проверенный JSON","Export checked JSON"),"copy"=>L("Резервная копия черновика","Draft backup"),"import"=>L("Импортировать JSON","Import JSON"),_=>L("Открыть файл JSON","Open a JSON file")};
        _files.PopupCenteredRatio(.72f);
    }
    private void OnFile(string path)
    {
        try
        {
            if(_fileJob=="copy"){System.IO.File.WriteAllText(path,Draft.ToJson());_status.Text=L("Резервная копия черновика записана. Это не проверенный игровой экспорт.","Draft backup written. This is not a checked game export.")+"\n"+path;return;}
            if(_fileJob is "open" or "import")
            {
                LoadText(System.IO.File.ReadAllText(path));
                if(_fileJob=="import"){_savedId=null;SavedPath=null;}
                else if(UserMaps.IsUserId(Draft.Id)){_savedId=Draft.Id;SavedPath=path;}
                _status.Text=L("Файл открыт.","File opened.");return;
            }
            var notes=Draft.Check(Economy(),_language);if(notes.Length>0){_status.Text=notes;ShowIssues();return;}
            System.IO.File.WriteAllText(path,Draft.ToJson());_status.Text=L("JSON экспортирован.","JSON exported.");
        }
        catch(Exception error){_status.Text=L("Не удалось прочитать или записать файл. ","The file could not be read or written. ")+error.Message;}
    }
    private static Theme DialogTheme()
    {
        var theme=new Theme();var ink=new Color("1a241c");var muted=new Color("5c675f");
        var paper=new StyleBoxFlat{BgColor=new Color("f7f8f1")};
        var hover=new StyleBoxFlat{BgColor=new Color("d5e4d2")};var selected=new StyleBoxFlat{BgColor=new Color("c5d8c2")};
        foreach(var type in new[]{"Label","Button","LineEdit","CheckBox","CheckButton","OptionButton","ItemList","Tree","PopupMenu","FileDialog"})
        {
            theme.SetColor("font_color",type,ink);theme.SetColor("font_hover_color",type,ink);theme.SetColor("font_hovered_color",type,ink);
            theme.SetColor("font_hovered_selected_color",type,ink);theme.SetColor("font_pressed_color",type,ink);
            theme.SetColor("font_focus_color",type,ink);theme.SetColor("font_disabled_color",type,muted);theme.SetColor("font_placeholder_color",type,muted);
            theme.SetColor("font_uneditable_color",type,muted);theme.SetColor("font_selected_color",type,ink);theme.SetColor("font_outline_color",type,ink);
        }
        foreach(var type in new[]{"ItemList","Tree"})
        {
            theme.SetStylebox("panel",type,paper);theme.SetStylebox("hovered",type,hover);theme.SetStylebox("hovered_selected",type,selected);
            theme.SetStylebox("selected",type,selected);theme.SetStylebox("selected_focus",type,selected);theme.SetStylebox("cursor",type,selected);
            theme.SetColor("guide_color",type,new Color("6d7d72"));theme.SetColor("title_button_color",type,ink);
        }
        theme.SetStylebox("panel","FileDialog",new StyleBoxFlat{BgColor=new Color("f4f7f2"),ContentMarginLeft=8,ContentMarginRight=8,ContentMarginTop=8,ContentMarginBottom=8});
        theme.SetStylebox("panel","Panel",new StyleBoxFlat{BgColor=new Color("f7f8f1")});
        return theme;
    }
    private static void PaintDialog(Node node)
    {
        if(node is Control control)
        {
            foreach(var key in new[]{"font_color","font_selected_color","font_hovered_color","font_hovered_selected_color","font_hover_color","font_pressed_color","font_focus_color"})
                control.AddThemeColorOverride(key,new Color("1a241c"));
            control.AddThemeColorOverride("font_disabled_color",new Color("5c675f"));control.AddThemeColorOverride("font_placeholder_color",new Color("5c675f"));
        }
        foreach(var child in node.GetChildren())PaintDialog(child);
    }
}

static class MapEditorIcons
{
    private static readonly System.Collections.Generic.Dictionary<string,Texture2D> Cache=new();
    public static Texture2D Get(string id)
    {
        if(Cache.TryGetValue(id,out var cached))return cached;
        string body=id switch
        {
            "select"=>"<rect x='2.5' y='2.5' width='11' height='11' fill='none' stroke='#243127' stroke-width='1.6' stroke-dasharray='2 1.5'/>",
            "move"=>"<path d='M8 1.5v13M1.5 8h13M8 1.5 6 3.5M8 1.5 10 3.5M8 14.5 6 12.5M8 14.5 10 12.5M1.5 8 3.5 6M1.5 8 3.5 10M14.5 8 12.5 6M14.5 8 12.5 10' fill='none' stroke='#243127' stroke-width='1.4' stroke-linecap='round'/>",
            "erase"=>"<path d='M3 11 9 3l4 4-6 8H5z' fill='#d9d3b2' stroke='#243127' stroke-width='1.3'/><path d='M3 13h10' stroke='#243127' stroke-width='1.4'/>",
            "rotate"=>"<path d='M8 3a5 5 0 1 1-4 2' fill='none' stroke='#243127' stroke-width='1.6'/><path d='M3 2v4h4' fill='none' stroke='#243127' stroke-width='1.6'/>",
            "road"=>"<path d='M3 12 12 3' stroke='#8a7352' stroke-width='3' stroke-linecap='round'/><path d='M6 10l1-1M9 7l1-1' stroke='#f4f7f2' stroke-width='1'/>",
            "lot"=>"<rect x='2' y='3' width='5' height='10' fill='#d9d3b2' stroke='#5c5344'/><rect x='8' y='3' width='6' height='6' fill='#efe6c4' stroke='#5c5344'/>",
            "resource"=>"<circle cx='8' cy='8' r='5' fill='#3f7950'/><path d='M8 4v8M5 7h6' stroke='#e7f2df' stroke-width='1.2'/>",
            "stall"=>"<path d='M3 7h10l-1 6H4z' fill='#c7a3b4' stroke='#6d4c5a'/><path d='M2 7h12' stroke='#6d4c5a' stroke-width='1.4'/>",
            "town"=>"<path d='M3 13V7l5-4 5 4v6z' fill='#c9b89a' stroke='#5c5344'/><path d='M7 13V9h2v4' stroke='#5c5344'/>",
            "terminal"=>"<rect x='2' y='4' width='12' height='8' fill='#b7c4c8' stroke='#4d5c62'/><path d='M5 4v8M8 4v8M11 4v8' stroke='#4d5c62'/>",
            "dealer"=>"<path d='M2 10h12l-1 3H3z' fill='#8aa9b7'/><path d='M4 10 6 6h4l2 4' fill='#d3e4d8' stroke='#4d5c62'/>",
            "parking"=>"<rect x='3' y='2' width='10' height='12' rx='2' fill='#5d6b66'/><path d='M6 11V5h3a2 2 0 0 1 0 4H6' fill='none' stroke='#f4f7f2' stroke-width='1.3'/>",
            "spawn"=>"<circle cx='8' cy='6' r='2.4' fill='#e7d56a' stroke='#6d5a20'/><path d='M8 8v5' stroke='#6d5a20' stroke-width='1.5'/>",
            "entry"=>"<path d='M2 8h8M8 5l3 3-3 3' fill='none' stroke='#c9842a' stroke-width='1.6' stroke-linecap='round'/><rect x='11' y='4' width='3' height='8' fill='#d9d3b2' stroke='#5c5344'/>",
            _=>"<circle cx='8' cy='8' r='4' fill='none' stroke='#243127' stroke-width='1.5'/>"
        };
        using var image=new Image();image.LoadSvgFromString($"<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16' viewBox='0 0 16 16'>{body}</svg>");
        var texture=ImageTexture.CreateFromImage(image);Cache[id]=texture;return texture;
    }
}
