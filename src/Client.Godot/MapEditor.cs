using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Text;

/// <summary>Visual delsk-map-design-2 editor. Drafts save freely; ordinary play uses a checked export.</summary>
public partial class MapEditor : Control
{
    public MapDraft Draft{get;private set;}=MapDraft.Create();
    private string _tool="road";
    private string _item="wood";
    private string? _selectedLot;
    private Cell? _hover;
    private Cell? _selected;
    private Vector2 _pan;
    private float _zoom=18;
    private bool _painting,_panning,_preview;
    private Vector2 _panOrigin,_panStart;
    private bool _export;
    private Control _chrome=null!;
    private Node3D _world=null!;
    private Camera3D _camera=null!;
    private Label _properties=null!;
    private Label _status=null!;
    private FileDialog _files=null!;
    private LineEdit _id=null!,_name=null!;
    private SpinBox _width=null!,_height=null!;
    private Button _previewButton=null!;
    public string PropertiesText=>_properties.Text;
    public override void _Ready()
    {
        Theme=CompactTabletDesign.Theme;MouseFilter=MouseFilterEnum.Stop;SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _world=new Node3D{Name="PreviewWorld"};AddChild(_world);
        _camera=new Camera3D{Name="PreviewCamera"};_world.AddChild(_camera);
        _chrome=new Control{Name="EditorChrome",MouseFilter=MouseFilterEnum.Ignore};_chrome.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);AddChild(_chrome);
        BuildChrome();_files=new FileDialog{Access=FileDialog.AccessEnum.Filesystem,Filters=["*.json ; Карта Delsk"]};AddChild(_files);_files.FileSelected+=OnFile;
        var embedded=Text("Play.compact-valley-i6c21-design.json");
        if(embedded is not null)LoadText(embedded);else{Draft=MapDraft.Create();FrameMap();}
        Resized+=()=>{if(!_painting)FrameMap();};
    }
    public void LoadText(string json){Draft=MapDraft.Parse(json);_selected=null;_selectedLot=null;SyncFields();FrameMap();Refresh();}
    public void SelectCell(int x,int z){_tool="select";Select(new(x,z));Refresh();}
    public void SetPreview(bool on){if(_preview==on)return;_preview=on;_chrome.Visible=!on;_previewButton.Visible=on;if(on)RebuildWorld();_camera.Current=on;QueueRedraw();}
    public override void _UnhandledInput(InputEvent e)
    {
        if(e is InputEventKey{Pressed:true,Echo:false,CtrlPressed:true} key)
        {
            if(key.Keycode==Key.Z){Draft.Undo();Refresh();GetViewport().SetInputAsHandled();}
            if(key.Keycode==Key.Y){Draft.Redo();Refresh();GetViewport().SetInputAsHandled();}
        }
    }
    public override void _GuiInput(InputEvent e)
    {
        if(_preview)return;
        if(e is InputEventMouseButton wheel&&wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            float next=Mathf.Clamp(_zoom*(wheel.ButtonIndex==MouseButton.WheelUp?1.1f:.9f),10,40);
            var cell=_hover??new Cell(Draft.Width/2,Draft.Height/2);_zoom=next;_pan=GetLocalMousePosition()-new Vector2(cell.X+.5f,cell.Z+.5f)*_zoom;QueueRedraw();GetViewport().SetInputAsHandled();return;
        }
        if(e is InputEventMouseButton pan&&pan.ButtonIndex is MouseButton.Right or MouseButton.Middle)
        {_panning=pan.Pressed;_panStart=pan.Position;_panOrigin=_pan;if(pan.Pressed)GetViewport().SetInputAsHandled();return;}
        if(e is InputEventMouseButton left&&left.ButtonIndex==MouseButton.Left)
        {
            if(left.Pressed&&MapRect().HasPoint(left.Position)){if(CellAt(left.Position) is{} cell){_painting=true;Apply(cell);}}
            else{_painting=false;Draft.EndStroke();}
            GetViewport().SetInputAsHandled();return;
        }
        if(e is InputEventMouseMotion motion)
        {
            _hover=CellAt(motion.Position);
            if(_panning){_pan=_panOrigin+motion.Position-_panStart;QueueRedraw();}
            else if(_painting&&_hover is{} cell&&_tool is "road" or "erase" or "resource" or "delete")Apply(cell);
            else QueueRedraw();
        }
    }
    public override void _Draw()
    {
        if(_preview)return;
        DrawRect(new Rect2(Vector2.Zero,Size),new Color("e7eee4"));
        var area=MapRect();DrawRect(area,new Color("d5e2cf"));
        var origin=_pan;
        DrawSetTransform(origin);float z=_zoom;
        for(int x=0;x<Draft.Width;x++)for(int row=0;row<Draft.Height;row++)DrawRect(new(x*z+1,row*z+1,z-2,z-2),new Color("8eae78"));
        foreach(var cell in Draft.Roads)DrawRect(Tile(cell,z),new Color("c4b396"));
        foreach(var district in Draft.Districts)foreach(var cell in district.Cells)DrawRect(Tile(cell,z),ColorOf(district.Item,district.PublicCells.Contains(cell)));
        foreach(var lot in Draft.Lots){var box=Footprint(lot.Cells,z);DrawRect(box,new Color("5c5344"));DrawRect(box.GrowIndividual(-2,-2,-2,-2),new Color("d9d3b2"));}
        foreach(var civic in Draft.Civics)DrawRect(Footprint(civic.Footprint,z),new Color(civic.Kind=="town_hall"?"c9b89a":civic.Kind=="terminal"?"b7c4c8":"aeb6ad"));
        foreach(var stall in Draft.Stalls)DrawRect(Tile(stall.Cell,z),new Color("c7a3b4"));
        foreach(var cell in Draft.EquipmentParking)DrawRect(new(cell.X*z+z*.28f,cell.Z*z+z*.28f,z*.44f,z*.44f),new Color("5d6b66"));
        foreach(var cell in Draft.Spawns)DrawRect(new(cell.X*z+z*.35f,cell.Z*z+z*.35f,z*.3f,z*.3f),new Color("e7d56a"));
        if(_hover is{} hover&&_tool is "lot" or "town_hall" or "terminal" or "dealership" or "move")
        {
            var size=_tool switch{"town_hall"=>(3,3),"terminal"=>(2,3),"dealership"=>(5,5),_=>(2,3)};
            var cells=_tool=="move"&&_selectedLot is{} id?Moved(id,hover):DraftCells(hover,size.Item1,size.Item2);
            if(cells.Count>0){var box=Footprint(cells,z);DrawRect(box,new Color(1,1,1,.35f));DrawRect(box.GrowIndividual(-1,-1,-1,-1),new Color(1,1,1,0));}
        }
        if(_selected is{} selected){var mark=Tile(selected,z);DrawRect(mark,new Color(0,0,0,0));DrawPolyline(new[]{mark.Position,mark.Position+new Vector2(mark.Size.X,0),mark.End,mark.Position+new Vector2(0,mark.Size.Y),mark.Position},new Color("243127"),2);}
        DrawSetTransform(Vector2.Zero);
    }
    private void BuildChrome()
    {
        var top=Bar(new Color("f7f8f1"));top.AnchorRight=1;top.OffsetBottom=46;top.OffsetLeft=8;top.OffsetRight=-8;
        void Command(string name,string text,Action action){var button=new Button{Name=name,Text=text};button.Pressed+=action;top.AddChild(button);}
        Command("NewMap","Новая",NewMap);Command("OpenMap","Открыть",()=>Choose(false));Command("SaveDraft","Сохранить",()=>Choose(true));
        Command("UndoChange","Отменить",()=>{Draft.Undo();Refresh();});Command("RedoChange","Повторить",()=>{Draft.Redo();Refresh();});
        Command("ValidateMap","Проверить",Validate);Command("ExportMap","Экспорт",Export);Command("Preview3D","Смотреть в 3D",()=>SetPreview(true));
        Command("BackToMenu","В меню",()=>GetTree().ChangeSceneToFile("res://Playfield.tscn"));
        var left=Panel(8,54,176);var tools=new VBoxContainer();tools.AddThemeConstantOverride("separation",4);left.AddChild(tools);
        var group=new ButtonGroup();
        void Tool(string id,string text){var button=new Button{Name="Tool_"+id,Text=text,ToggleMode=true,ButtonGroup=group,ButtonPressed=id==_tool};button.Toggled+=on=>{if(on)_tool=id;};tools.AddChild(button);}
        Tool("select","Выделение");Tool("road","Дорога");Tool("erase","Стереть дорогу");Tool("resource","Ресурс");Tool("lot","Участок");
        Tool("town_hall","Ратуша");Tool("terminal","Терминал");Tool("dealership","Автосалон");Tool("stall","Лавка");Tool("parking","Стоянка");
        Tool("spawn","Появление");Tool("delete","Удалить");Tool("move","Переместить");
        var resources=new OptionButton{Name="ResourceKind"};foreach(var item in MapDraft.ResourceItems)resources.AddItem(ResourceName(item));
        resources.ItemSelected+=i=>_item=MapDraft.ResourceItems[i];tools.AddChild(resources);
        var grouped=new Button{Name="NewResourceGroup",Text="Новая группа"};grouped.Pressed+=()=>{Draft.BeginResourceGroup(_item);_tool="resource";Refresh();};tools.AddChild(grouped);
        var right=Panel(0,54,252,true);
        var column=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};column.AddThemeConstantOverride("separation",6);right.AddChild(column);
        _name=Edit("Название");_id=Edit("Идентификатор");_name.TextChanged+=value=>Draft.Name=value;_id.TextChanged+=value=>Draft.Id=value.Trim();
        column.AddChild(_name);column.AddChild(_id);
        var size=new HBoxContainer();_width=Number();_height=Number();size.AddChild(_width);size.AddChild(_height);column.AddChild(size);
        var resize=new Button{Name="ResizeMap",Text="Размер"};resize.Pressed+=()=>{Draft.Resize((int)_width.Value,(int)_height.Value);FrameMap();};column.AddChild(resize);
        var scroll=new ScrollContainer{SizeFlagsVertical=SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};column.AddChild(scroll);
        _properties=new Label{Name="PropertiesBody",AutowrapMode=TextServer.AutowrapMode.WordSmart,SizeFlagsHorizontal=SizeFlags.ExpandFill,CustomMinimumSize=new(220,0)};scroll.AddChild(_properties);
        _status=new Label{Name="ValidationText",AutowrapMode=TextServer.AutowrapMode.WordSmart};column.AddChild(_status);
        _previewButton=new Button{Name="LeavePreview",Text="К редактору",Visible=false};_previewButton.SetAnchorsPreset(LayoutPreset.TopRight);_previewButton.OffsetLeft=-150;_previewButton.OffsetTop=12;_previewButton.OffsetRight=-12;_previewButton.OffsetBottom=48;
        _previewButton.Pressed+=()=>SetPreview(false);AddChild(_previewButton);
        LineEdit Edit(string hint){var edit=new LineEdit{PlaceholderText=hint,CustomMinimumSize=new(0,32)};return edit;}
        SpinBox Number()=>new(){MinValue=4,MaxValue=128,Step=1,Rounded=true,CustomMinimumSize=new(70,32)};
    }
    private HBoxContainer Bar(Color color){var bar=new HBoxContainer{Name="EditorToolbar"};bar.AddThemeConstantOverride("separation",6);var panel=new PanelContainer();panel.AddThemeStyleboxOverride("panel",CompactTabletDesign.Surface(color.ToHtml(false),10));panel.SetAnchorsPreset(LayoutPreset.TopWide);panel.OffsetTop=6;panel.OffsetBottom=46;_chrome.AddChild(panel);panel.AddChild(bar);return bar;}
    private MarginContainer Panel(float x,float y,float width,bool rightEdge=false)
    {
        var panel=new PanelContainer();panel.AddThemeStyleboxOverride("panel",CompactTabletDesign.Surface("f7f8f1",12));
        panel.AnchorBottom=1;panel.OffsetTop=y;panel.OffsetBottom=-8;
        if(rightEdge){panel.AnchorLeft=1;panel.AnchorRight=1;panel.OffsetLeft=-(width+8);panel.OffsetRight=-8;}
        else{panel.OffsetLeft=x;panel.OffsetRight=x+width;}
        _chrome.AddChild(panel);
        var margin=new MarginContainer();margin.AddThemeConstantOverride("margin_left",8);margin.AddThemeConstantOverride("margin_right",8);margin.AddThemeConstantOverride("margin_top",8);margin.AddThemeConstantOverride("margin_bottom",8);panel.AddChild(margin);return margin;
    }
    private void NewMap(){Draft=MapDraft.Create(32,25);Draft.Name="Новая карта";_selected=null;_selectedLot=null;SyncFields();FrameMap();}
    private void Choose(bool save){_export=false;_files.FileMode=save?FileDialog.FileModeEnum.SaveFile:FileDialog.FileModeEnum.OpenFile;_files.Title=save?"Сохранить черновик":"Открыть карту";_files.PopupCenteredRatio(.72f);}
    private void Export()
    {
        var notes=Draft.Check(Profile());
        if(notes.Length>0){_status.Text=notes;return;}
        _export=true;_files.FileMode=FileDialog.FileModeEnum.SaveFile;_files.Title="Экспорт delsk-map-design-2";_files.PopupCenteredRatio(.72f);
    }
    private void OnFile(string path)
    {
        if(_files.FileMode==FileDialog.FileModeEnum.OpenFile){LoadText(Godot.FileAccess.GetFileAsString(path));return;}
        if(_export){var notes=Draft.Check(Profile());if(notes.Length>0){_status.Text=notes;return;}}
        using var file=Godot.FileAccess.Open(path,Godot.FileAccess.ModeFlags.Write);file.StoreString(Draft.ToJson());_status.Text=_export?"Экспорт записан. Обычная партия берёт проверенный снимок.":"Черновик сохранён.";
    }
    private void Validate(){var notes=Draft.Check(Profile());_status.Text=notes.Length==0?"Карта готова к обычной партии.":notes;}
    private void Apply(Cell cell)
    {
        if(_tool is "road" or "erase" or "resource" or "delete")Draft.BeginStroke();
        switch(_tool)
        {
            case "road":Draft.PaintRoad(cell);break;case "erase":Draft.EraseRoad(cell);break;case "resource":Draft.PaintResource(cell,_item);break;
            case "lot":Draft.PlaceLot(cell);break;case "town_hall" or "terminal" or "dealership":Draft.PlaceCivic(_tool,cell);break;
            case "stall":Draft.ToggleStall(cell);break;case "parking":Draft.ToggleParking(cell);break;case "spawn":Draft.ToggleSpawn(cell);break;
            case "delete":Draft.DeleteAt(cell);break;case "select":Select(cell);break;case "move" when _selectedLot is{} id:Draft.MoveLot(id,cell);break;
        }
        Refresh();
    }
    private void Select(Cell cell)
    {
        _selected=cell;_selectedLot=Draft.Lots.FirstOrDefault(l=>l.Cells.Contains(cell))?.Id;
        var lines=new List<string>{$"Клетка ({cell.X}, {cell.Z})"};
        if(Draft.Roads.Contains(cell))lines.Add("Дорога, ширина одна клетка.");
        var district=Draft.Districts.FirstOrDefault(d=>d.Cells.Contains(cell));
        if(district is not null)lines.Add($"{ResourceName(district.Item)}, группа {district.Id}, {(district.PublicCells.Contains(cell)?"общая":"покупаемая")}.");
        var lot=Draft.Lots.FirstOrDefault(l=>l.Cells.Contains(cell)||l.Entry==cell);
        if(lot is not null)lines.Add($"Участок {lot.Id}, въезд ({lot.Entry.X}, {lot.Entry.Z}).");
        var civic=Draft.Civics.FirstOrDefault(c=>c.Footprint.Contains(cell));
        if(civic is not null)lines.Add($"{ResourceName(civic.Kind)} занимает {civic.Footprint.Count} клеток.");
        if(Draft.EquipmentParking.Contains(cell))lines.Add("Стоянка техники.");
        if(Draft.Spawns.Contains(cell))lines.Add("Точка появления.");
        lines.Add($"Дорог на карте: {Draft.Roads.Distinct().Count()}.");
        _properties.Text=string.Join('\n',lines);
    }
    private void Refresh(){SyncFields();if(_selected is{} cell)Select(cell);else _properties.Text=$"Дорог: {Draft.Roads.Distinct().Count()}. Участков: {Draft.Lots.Count}. Ресурсов: {Draft.Districts.Select(d=>d.Item).Distinct().Count()}.";QueueRedraw();}
    private void SyncFields(){if(_id.Text!=Draft.Id)_id.Text=Draft.Id;if(_name.Text!=Draft.Name)_name.Text=Draft.Name;_width.Value=Draft.Width;_height.Value=Draft.Height;}
    private void FrameMap(){var area=MapRect();_zoom=Mathf.Clamp(Mathf.Min((area.Size.X-20)/Math.Max(Draft.Width,1),(area.Size.Y-20)/Math.Max(Draft.Height,1)),10,36);_pan=area.Position+(area.Size-new Vector2(Draft.Width,Draft.Height)*_zoom)/2;QueueRedraw();}
    private Rect2 MapRect()=>new(196,54,Math.Max(20,Size.X-196-272),Math.Max(20,Size.Y-62));
    private Cell? CellAt(Vector2 screen){var local=(screen-_pan)/_zoom;int x=(int)Math.Floor(local.X),z=(int)Math.Floor(local.Y);return x>=0&&z>=0&&x<Draft.Width&&z<Draft.Height?new Cell(x,z):null;}
    private static Rect2 Tile(Cell cell,float zoom)=>new(cell.X*zoom+1,cell.Z*zoom+1,zoom-2,zoom-2);
    private static Rect2 Footprint(IReadOnlyList<Cell> cells,float zoom)
    {int x=cells.Min(c=>c.X),z=cells.Min(c=>c.Z),w=cells.Max(c=>c.X)-x+1,h=cells.Max(c=>c.Z)-z+1;return new(x*zoom+1,z*zoom+1,w*zoom-2,h*zoom-2);}
    private List<Cell> DraftCells(Cell origin,int w,int h)=>Enumerable.Range(origin.Z,h).SelectMany(z=>Enumerable.Range(origin.X,w).Select(x=>new Cell(x,z))).Where(c=>c.X>=0&&c.Z>=0&&c.X<Draft.Width&&c.Z<Draft.Height).ToList();
    private List<Cell> Moved(string id,Cell origin){var lot=Draft.Lots.First(l=>l.Id==id);int dx=origin.X-lot.Cells.Min(c=>c.X),dz=origin.Z-lot.Cells.Min(c=>c.Z);return lot.Cells.Select(c=>new Cell(c.X+dx,c.Z+dz)).ToList();}
    private void RebuildWorld()
    {
        foreach(Node child in _world.GetChildren())if(child!=_camera)child.Free();
        var ground=new Node3D{Name="Ground"};_world.AddChild(ground);
        WorldModels.Part(ground,new BoxMesh{Size=new(Draft.Width+2,.2f,Draft.Height+2)},new Color("7ea36d"),new(Draft.Width/2f-.5f,-.12f,Draft.Height/2f-.5f));
        var ink=new Color("5e6b58");
        for(int x=0;x<=Draft.Width;x++)WorldModels.Part(ground,new BoxMesh{Size=new(.025f,.02f,Draft.Height)},ink,new(x-.5f,.02f,Draft.Height/2f-.5f));
        for(int z=0;z<=Draft.Height;z++)WorldModels.Part(ground,new BoxMesh{Size=new(Draft.Width,.02f,.025f)},ink,new(Draft.Width/2f-.5f,.02f,z-.5f));
        foreach(var row in Draft.Roads.GroupBy(c=>c.Z))
        {var cells=row.OrderBy(c=>c.X).ToArray();int begin=0;for(int i=1;i<=cells.Length;i++)if(i==cells.Length||cells[i].X!=cells[i-1].X+1)
        {int length=cells[i-1].X-cells[begin].X+1;WorldModels.Part(ground,new BoxMesh{Size=new(length,.03f,.94f)},new Color("a39578"),new(cells[begin].X+(length-1)/2f,.03f,row.Key));begin=i;}}
        foreach(var lot in Draft.Lots)
        {float x=lot.Cells.Min(c=>c.X)+.5f,z=lot.Cells.Min(c=>c.Z)+1;WorldModels.Part(ground,new BoxMesh{Size=new(1.98f,.012f,2.98f)},new Color("5c5344"),new(x,.01f,z));WorldModels.Part(ground,new BoxMesh{Size=new(1.86f,.02f,2.86f)},new Color("a8a386"),new(x,.02f,z));}
        foreach(var district in Draft.Districts)foreach(var cell in district.Cells){var node=WorldModels.Resource(district.Item);node.Position=new(cell.X,0,cell.Z);_world.AddChild(node);}
        foreach(var civic in Draft.Civics)_world.AddChild(WorldModels.Civic(new(civic.Id,civic.Kind,civic.Footprint.ToArray(),civic.Building.ToArray(),civic.Kind=="dealership"?civic.Parking.ToArray():null,civic.Building.ToArray(),civic.Entry,civic.Kind=="dealership"?Draft.EquipmentParking.ToArray():null)));
        foreach(var stall in Draft.Stalls)WorldModels.Part(ground,new BoxMesh{Size=new(.72f,.04f,.72f)},new Color("b5a3b1"),new(stall.Cell.X,.04f,stall.Cell.Z));
        var dealer=Draft.Civics.FirstOrDefault(c=>c.Kind=="dealership");
        if(dealer is{Parking.Count:>=4}&&Draft.CompactExhibition)
        {
            Place("sputnik20",dealer.Parking[0],-.15f);Place("muravey70",dealer.Parking[0],.15f);Place("eger70",dealer.Parking[1],0);Place("trakt_d1",dealer.Parking[3],0);
            if(Draft.EquipmentParking.Count>=2){Machine("wood",Draft.EquipmentParking[0]);Machine("grain",Draft.EquipmentParking[1]);}
        }
        _camera.Projection=Camera3D.ProjectionType.Perspective;_camera.Position=new(Draft.Width*.35f,18,Draft.Height+6);_camera.LookAt(new(Draft.Width/2f,0,Draft.Height/2f),Vector3.Up);
        void Place(string model,Cell cell,float side){var node=WorldModels.Vehicle(model);node.Position=new(cell.X+side,0,cell.Z);_world.AddChild(node);}
        void Machine(string item,Cell cell){var node=WorldModels.Equipment(item,1);node.Position=new(cell.X,0,cell.Z);_world.AddChild(node);}
    }
    private static EconomyProfile Profile()=>EconomyProfile.Read(Text("Play.i4-mobile-equipment.json")??throw new InvalidOperationException("Нет профиля техники"));
    private static string? Text(string name){using var stream=typeof(MapEditor).Assembly.GetManifestResourceStream(name);if(stream is null)return null;using var reader=new StreamReader(stream,Encoding.UTF8);return reader.ReadToEnd();}
    private static string ResourceName(string item)=>item switch{"wood"=>"Лес","grain"=>"Зерно","milk"=>"Молоко","beet"=>"Свёкла","stone"=>"Камень","sand"=>"Песок","iron_ore"=>"Железная руда","copper_ore"=>"Медная руда","town_hall"=>"Ратуша","terminal"=>"Терминал","dealership"=>"Автосалон",_=>item};
    private static Color ColorOf(string item,bool open)=>item switch{"wood"=>new(open?"3f7950":"2f5a3c"),"grain"=>new(open?"e2c15a":"c49a3a"),"milk"=>new(open?"d5ece8":"8fb8b0"),"beet"=>new(open?"c45b78":"8d3d55"),"stone"=>new(open?"b7bdb8":"7d8480"),"sand"=>new(open?"e6d7a8":"c2ae78"),"iron_ore"=>new(open?"a8948c":"6e625c"),"copper_ore"=>new(open?"d39262":"a56a3e"),_=>new("888888")};
}
