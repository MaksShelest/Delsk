using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Text;

/// <summary>Редактор своей карты. Черновик сохраняется свободно, обычная партия запускается только после проверки.</summary>
public partial class MapEditor : Control
{
    public const float LeftMargin=248;
    public const float RightMargin=292;
    public MapDraft Draft{get;private set;}=MapDraft.Create();
    private string _tool="road";
    private string _lotMode="cell";
    private string _districtRoads="north";
    private string _item="wood";
    private string _language="ru";
    private string? _selectedLot;
    private string? _savedId;
    private Cell? _hover;
    private Cell? _selected;
    private Cell? _dragFrom;
    private Vector2 _pan;
    private float _zoom=18;
    private int _quarter;
    private bool _painting,_panning,_dragging,_preview,_pendingPlay;
    private Vector2 _panOrigin,_panStart;
    private string _fileJob="open";
    private Control _chrome=null!;
    private Control _previewOverlay=null!;
    private Node3D _world=null!;
    private Camera3D _camera=null!;
    private Label _properties=null!;
    private Label _status=null!;
    private Label _issueSummary=null!;
    private VBoxContainer _issues=null!;
    private Control _lotModes=null!;
    private Control _resourceList=null!;
    private FileDialog _files=null!;
    private LineEdit _id=null!,_name=null!;
    private SpinBox _width=null!,_height=null!,_participants=null!;
    private float _zoomTarget=18;
    private Vector2 _zoomAnchorMap,_zoomAnchorScreen,_framedSize;
    private bool _zooming;
    private string? _pendingFix;
    private float _yaw,_pitch=Mathf.DegToRad(50),_distance=24,_distanceTarget=24;
    private bool _distanceGlide;
    private Vector3 _focus;
    private bool _orbiting,_sliding;
    public int ViewQuarter=>_quarter;
    public string IssueText{get;private set;}="";
    public string StatusText=>_status is null?"":_status.Text;
    public string PropertiesText=>_properties is null?"":_properties.Text;
    public Vector3 PreviewCameraPosition=>_camera.Position;
    public Vector3 PreviewFocus=>_focus;
    public string? SavedPath{get;private set;}
    public override void _Ready()
    {
        Theme=CompactTabletDesign.Theme;MouseFilter=MouseFilterEnum.Stop;SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _world=new Node3D{Name="PreviewWorld"};AddChild(_world);
        _camera=new Camera3D{Name="PreviewCamera",Current=false};_world.AddChild(_camera);
        _chrome=new Control{Name="EditorChrome",MouseFilter=MouseFilterEnum.Ignore};_chrome.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);AddChild(_chrome);
        BuildChrome();
        _files=CreateFiles();AddChild(_files);_files.FileSelected+=OnFile;
        var edit=MapHandoff.ConsumeEdit();
        if(edit is not null && Godot.FileAccess.FileExists(edit)){LoadText(Godot.FileAccess.GetFileAsString(edit));SavedPath=edit;if(UserMaps.IsUserId(Draft.Id))_savedId=Draft.Id;}
        else
        {
            var embedded=Text("Play.compact-valley-i6c21-design.json");
            if(embedded is not null)LoadText(embedded);else{Draft=MapDraft.Create();FrameMap();}
        }
        Resized+=()=>{if(_framedSize.DistanceTo(Size)>1){_framedSize=Size;if(!_painting&&!_zooming)FrameMap();}};
    }
    public void LoadText(string json){Draft=MapDraft.Parse(json);_selected=null;_selectedLot=null;SyncFields();FrameMap();Refresh();}
    public void SelectCell(int x,int z){_tool="select";FocusCell(x,z);}
    public void FocusCell(int x,int z){_selected=new(x,z);_selectedLot=Draft.Lots.FirstOrDefault(l=>l.Cells.Contains(_selected.Value)||l.Entry==_selected.Value)?.Id;Refresh();FrameOn(_selected.Value);}
    public void Edit(System.Action<MapDraft> change){change(Draft);Refresh();}
    public void RotateView(int sign){_quarter=((_quarter+sign)%4+4)%4;FrameMap();}
    public void RotateContent(){Draft.RotateContent();_selected=null;FrameMap();Refresh();}
    public void ShowExportDialog()=>Choose("export");
    public override void _UnhandledInput(InputEvent e)
    {
        if(e is InputEventKey{Pressed:true,Echo:false,CtrlPressed:true} key)
        {
            if(key.Keycode==Key.Z){Draft.Undo();Refresh();GetViewport().SetInputAsHandled();}
            if(key.Keycode==Key.Y){Draft.Redo();Refresh();GetViewport().SetInputAsHandled();}
        }
    }
    public override void _Process(double delta)
    {
        if(_preview&&_distanceGlide)
        {
            _distance=Mathf.Lerp(_distance,_distanceTarget,1f-Mathf.Exp(-14f*(float)delta));
            if(Mathf.Abs(_distance-_distanceTarget)<.05f){_distance=_distanceTarget;_distanceGlide=false;}
            ApplyCamera();
        }
        if(_preview||!_zooming)return;
        float next=Mathf.Lerp(_zoom,_zoomTarget,1f-Mathf.Exp(-16f*(float)delta));
        if(Mathf.Abs(next-_zoomTarget)<.04f){next=_zoomTarget;_zooming=false;}
        _zoom=next;var pan=MapView.ZoomOffset(_zoomAnchorScreen.X,_zoomAnchorScreen.Y,_zoomAnchorMap.X,_zoomAnchorMap.Y,next);_pan=new(pan.X,pan.Y);QueueRedraw();
    }
    public override void _GuiInput(InputEvent e)
    {
        if(_preview){PreviewInput(e);return;}
        if(_files is not null&&_files.Visible)return;
        if(e is InputEventMouseButton wheel&&wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            var cursor=wheel.Position;
            if(!MapRect().HasPoint(cursor)){ScrollAt(cursor,wheel.ButtonIndex==MouseButton.WheelUp);GetViewport().SetInputAsHandled();return;}
            float factor=wheel.ButtonIndex==MouseButton.WheelUp?1.08f:1f/1.08f;
            var anchor=MapView.MapPoint(_pan.X,_pan.Y,Mathf.Max(.01f,_zoom),cursor.X,cursor.Y);
            _zoomTarget=Mathf.Clamp((_zooming?_zoomTarget:_zoom)*factor,6,72);
            _zoomAnchorMap=new(anchor.X,anchor.Y);_zoomAnchorScreen=cursor;_zooming=true;GetViewport().SetInputAsHandled();return;
        }
        if(e is InputEventMouseButton pan&&pan.ButtonIndex is MouseButton.Right or MouseButton.Middle)
        {_panning=pan.Pressed;_panStart=pan.Position;_panOrigin=_pan;if(pan.Pressed)GetViewport().SetInputAsHandled();return;}
        if(e is InputEventMouseButton left&&left.ButtonIndex==MouseButton.Left)
        {
            if(left.Pressed&&MapRect().HasPoint(left.Position)&&CellAt(left.Position) is{} cell)
            {
                if(_tool=="lot"&&_lotMode=="rect"){_dragFrom=cell;_dragging=true;}
                else{_painting=true;Apply(cell);}
            }
            else
            {
                if(_dragging&&_dragFrom is{} from&&_hover is{} to){var id=Draft.PlaceLotRectangle(from,to);if(id is not null)_selectedLot=id;}
                _dragging=false;_painting=false;Draft.EndStroke();Refresh();
            }
            GetViewport().SetInputAsHandled();return;
        }
        if(e is InputEventMouseMotion motion)
        {
            _hover=CellAt(motion.Position);
            if(_panning)_pan=_panOrigin+motion.Position-_panStart;
            else if(_painting&&_hover is{} cell&&_tool is "road" or "erase" or "resource" or "lot"&&_lotMode=="cell")Apply(cell);
            QueueRedraw();
        }
    }
    public override void _Draw()
    {
        if(_preview)return;
        DrawRect(new Rect2(Vector2.Zero,Size),new Color("e7eee4"));
        var area=MapRect();DrawRect(area,new Color("d5e2cf"));
        DrawSetTransform(_pan);float zoom=_zoom;
        for(int x=0;x<Draft.Width;x++)for(int row=0;row<Draft.Height;row++)DrawRect(Tile(new(x,row),zoom),new Color("8eae78"));
        foreach(var cell in Draft.Roads)DrawRect(Tile(cell,zoom),new Color("c4b396"));
        foreach(var district in Draft.Districts)foreach(var cell in district.Cells)DrawRect(Tile(cell,zoom),ColorOf(district.Item,district.PublicCells.Contains(cell)));
        foreach(var lot in Draft.Lots)
        {
            bool active=lot.Id==_selectedLot;
            foreach(var cell in lot.Cells){var tile=Tile(cell,zoom);DrawRect(tile,new Color(active?"efe6c4":"d9d3b2"));DrawRect(tile,new Color("5c5344"),false,Mathf.Max(1,zoom*.06f));}
            DrawRect(Tile(lot.Entry,zoom),new Color("c9842a"),false,Mathf.Max(1.5f,zoom*.08f));
        }
        foreach(var civic in Draft.Civics)foreach(var cell in civic.Footprint)DrawRect(Tile(cell,zoom),new Color(civic.Kind=="town_hall"?"c9b89a":civic.Kind=="terminal"?"b7c4c8":"aeb6ad"));
        foreach(var stall in Draft.Stalls)DrawRect(Tile(stall.Cell,zoom),new Color("c7a3b4"));
        foreach(var cell in Draft.EquipmentParking){var tile=Tile(cell,zoom);DrawRect(new(tile.Position+tile.Size*.28f,tile.Size*.44f),new Color("5d6b66"));}
        foreach(var cell in Draft.Spawns){var tile=Tile(cell,zoom);DrawRect(new(tile.Position+tile.Size*.32f,tile.Size*.36f),new Color("e7d56a"));}
        foreach(var cell in Ghost()){var tile=Tile(cell,zoom);DrawRect(tile,new Color(1,1,1,.28f));DrawRect(tile,new Color("243127"),false,1);}
        if(_pendingFix is not null)foreach(var cell in Draft.FixPreview(_pendingFix)){var tile=Tile(cell,zoom);DrawRect(tile,new Color("c9842a"),false,Mathf.Max(2,zoom*.1f));}
        if(_selected is{} selected){var mark=Tile(selected,zoom);DrawPolyline([mark.Position,mark.Position+new Vector2(mark.Size.X,0),mark.End,mark.Position+new Vector2(0,mark.Size.Y),mark.Position],new Color("243127"),2);}
        DrawSetTransform(Vector2.Zero);
    }
    private void Apply(Cell cell)
    {
        if(_tool is "road" or "erase" or "resource" or "lot")Draft.BeginStroke();
        switch(_tool)
        {
            case "road":Draft.PaintRoad(cell);break;
            case "erase":Draft.DeleteAt(cell);break;
            case "resource":Draft.PaintResource(cell,_item);break;
            case "lot" when _lotMode=="cell":_selectedLot=Draft.AddLotCell(cell,_selectedLot)??_selectedLot;break;
            case "lot" when _lotMode=="template":Draft.PlaceLot(cell);_selectedLot=Draft.Lots.LastOrDefault()?.Id;break;
            case "lot" when _lotMode=="district":Draft.PlaceStandardDistrict(cell,_districtRoads);_selectedLot=Draft.Lots.LastOrDefault()?.Id;break;
            case "town_hall" or "terminal" or "dealership":Draft.PlaceCivic(_tool,cell);break;
            case "stall":Draft.ToggleStall(cell);break;
            case "parking":Draft.ToggleParking(cell);break;
            case "spawn":Draft.ToggleSpawn(cell);break;
            case "entry" when _selectedLot is{} id:Draft.SetEntry(id,cell);break;
            case "turn":Draft.ToggleTurn(cell);break;
            case "select":_selected=cell;_selectedLot=Draft.Lots.FirstOrDefault(l=>l.Cells.Contains(cell)||l.Entry==cell)?.Id;break;
            case "move" when _selectedLot is{} id:Draft.MoveLot(id,cell);break;
        }
        Refresh();
    }
    private List<Cell> Ghost()
    {
        if(_hover is not {} hover)return [];
        if(_tool=="lot"&&_lotMode=="cell")return [hover];
        if(_tool=="lot"&&_lotMode=="template")return InsideRect(hover,2,3);
        if(_tool=="lot"&&_lotMode=="district")
        {
            try{return IndustrialParcels.District(hover,_districtRoads).SelectMany(l=>l.Cells).Where(c=>c.X>=0&&c.Z>=0&&c.X<Draft.Width&&c.Z<Draft.Height).ToList();}
            catch(ArgumentException){return [];}
        }
        if(_tool=="lot"&&_lotMode=="rect"&&_dragFrom is{} from)return InsideRect(new(Math.Min(from.X,hover.X),Math.Min(from.Z,hover.Z)),Math.Abs(hover.X-from.X)+1,Math.Abs(hover.Z-from.Z)+1);
        if(_tool is "town_hall" or "terminal" or "dealership"){var size=_tool switch{"town_hall"=>(3,3),"terminal"=>(2,3),_=>(5,5)};return InsideRect(hover,size.Item1,size.Item2);}
        if(_tool=="move"&&_selectedLot is{} id)return Moved(id,hover);
        return [];
    }
    private void Refresh()
    {
        SyncFields();
        if(_lotModes is not null)_lotModes.Visible=_tool=="lot";
        if(_resourceList is not null)_resourceList.Visible=_tool=="resource";
        if(_selected is{} cell)Describe(cell);else _properties.Text=L($"Дорог: {Draft.Roads.Distinct().Count()}. Участков: {Draft.Lots.Count}. Ресурсов: {Draft.Districts.Select(d=>d.Item).Distinct().Count()}.",$"Roads: {Draft.Roads.Distinct().Count()}. Lots: {Draft.Lots.Count()}. Resources: {Draft.Districts.Select(d=>d.Item).Distinct().Count()}.");
        ShowIssues();QueueRedraw();
    }
    private void Describe(Cell cell)
    {
        var lines=new List<string>{L($"Клетка ({cell.X}, {cell.Z})",$"Cell ({cell.X}, {cell.Z})")};
        if(Draft.Roads.Contains(cell))lines.Add(L("Дорога, ширина одна клетка.","Road, one cell wide."));
        var district=Draft.Districts.FirstOrDefault(d=>d.Cells.Contains(cell));
        if(district is not null)lines.Add($"{PlayerText.Item(_language,district.Item)}, {district.Id}, {(district.PublicCells.Contains(cell)?L("общая","public"):L("покупаемая","purchasable"))}.");
        var lot=Draft.Lots.FirstOrDefault(l=>l.Id==_selectedLot)??Draft.Lots.FirstOrDefault(l=>l.Cells.Contains(cell)||l.Entry==cell);
        if(lot is not null)
        {
            bool reached=Draft.Reaches(lot.Entry);
            lines.Add(L($"Участок {lot.Id}. Площадь: {lot.Cells.Count}. Въезд ({lot.Entry.X}, {lot.Entry.Z}).",$"Lot {lot.Id}. Area: {lot.Cells.Count}. Entrance ({lot.Entry.X}, {lot.Entry.Z})."));
            lines.Add(reached?L("Доступен от первой точки появления.","Reachable from the first spawn."):L("Нет пути от первой точки появления.","No path from the first spawn."));
        }
        var civic=Draft.Civics.FirstOrDefault(c=>c.Footprint.Contains(cell));
        if(civic is not null)lines.Add($"{CivicLabel(civic.Kind)} · {civic.Footprint.Count}");
        if(Draft.EquipmentParking.Contains(cell))lines.Add(L("Место техники автосалона.","Dealership machine bay."));
        if(Draft.Spawns.Contains(cell))lines.Add(L("Точка появления.","Spawn point."));
        lines.Add(L($"Дорог на карте: {Draft.Roads.Distinct().Count()}.",$"Roads on the map: {Draft.Roads.Distinct().Count()}."));
        _properties.Text=string.Join('\n',lines);
    }
    private void SyncFields()
    {
        if(_id is null)return;
        if(_id.Text!=Draft.Id)_id.Text=Draft.Id;if(_name.Text!=Draft.Name)_name.Text=Draft.Name;
        _width.SetValueNoSignal(Draft.Width);_height.SetValueNoSignal(Draft.Height);_participants.SetValueNoSignal(Draft.MaxParticipants);
    }
    public void FitMap(){FrameMap();}
    private void ScrollAt(Vector2 cursor,bool up)
    {
        foreach(var name in new[]{"ToolScroll","PropertyScroll","IssueScroll"})
        {
            if(FindChild(name,true,false) is not ScrollContainer scroll)continue;
            if(!scroll.GetGlobalRect().HasPoint(GetGlobalTransformWithCanvas()*cursor))continue;
            scroll.ScrollVertical+=up?-48:48;return;
        }
    }
    private void FrameMap()
    {
        var area=MapRect();var size=MapView.DisplaySize(Draft.Width,Draft.Height,_quarter);
        _zooming=false;_zoom=_zoomTarget=Mathf.Clamp(Mathf.Min((area.Size.X-16)/Math.Max(size.Width,1),(area.Size.Y-16)/Math.Max(size.Height,1)),6,72);
        _pan=area.Position+(area.Size-new Vector2(size.Width,size.Height)*_zoom)/2;_framedSize=Size;QueueRedraw();
    }
    private void FrameOn(Cell cell)
    {
        var area=MapRect();var screen=MapView.ToScreen(Draft.Width,Draft.Height,_quarter,cell);
        _pan=area.Position+area.Size/2-new Vector2(screen.X+.5f,screen.Y+.5f)*_zoom;QueueRedraw();
    }
    private Rect2 MapRect()=>new(LeftMargin,54,Math.Max(20,Size.X-LeftMargin-RightMargin),Math.Max(20,Size.Y-62));
    private Cell? CellAt(Vector2 screen)
    {
        var local=(screen-_pan)/_zoom;int x=(int)Math.Floor(local.X),y=(int)Math.Floor(local.Y);
        var size=MapView.DisplaySize(Draft.Width,Draft.Height,_quarter);
        return x>=0&&y>=0&&x<size.Width&&y<size.Height?MapView.ToWorld(Draft.Width,Draft.Height,_quarter,x,y):null;
    }
    private Rect2 Tile(Cell cell,float zoom){var screen=MapView.ToScreen(Draft.Width,Draft.Height,_quarter,cell);return new(screen.X*zoom+1,screen.Y*zoom+1,zoom-2,zoom-2);}
    private List<Cell> InsideRect(Cell origin,int w,int h)=>Enumerable.Range(origin.Z,h).SelectMany(z=>Enumerable.Range(origin.X,w).Select(x=>new Cell(x,z))).Where(c=>c.X>=0&&c.Z>=0&&c.X<Draft.Width&&c.Z<Draft.Height).ToList();
    private List<Cell> Moved(string id,Cell origin){var lot=Draft.Lots.First(l=>l.Id==id);if(lot.Cells.Count==0)return [];int dx=origin.X-lot.Cells.Min(c=>c.X),dz=origin.Z-lot.Cells.Min(c=>c.Z);return lot.Cells.Select(c=>new Cell(c.X+dx,c.Z+dz)).ToList();}
    private string L(string ru,string en)=>PlayerText.Local(_language,ru,en);
    private string CivicLabel(string kind)=>kind switch{"town_hall"=>L("Ратуша","Town hall"),"terminal"=>L("Биржа","Exchange"),"dealership"=>L("Автосалон","Dealership"),_=>kind};
    private static string? Text(string name){using var stream=typeof(MapEditor).Assembly.GetManifestResourceStream(name);if(stream is null)return null;using var reader=new StreamReader(stream,Encoding.UTF8);return reader.ReadToEnd();}
    private static Color ColorOf(string item,bool open)=>item switch{"wood"=>new(open?"3f7950":"2f5a3c"),"grain"=>new(open?"e2c15a":"c49a3a"),"milk"=>new(open?"d5ece8":"8fb8b0"),"beet"=>new(open?"c45b78":"8d3d55"),"stone"=>new(open?"b7bdb8":"7d8480"),"sand"=>new(open?"e6d7a8":"c2ae78"),"iron_ore"=>new(open?"a8948c":"6e625c"),"copper_ore"=>new(open?"d39262":"a56a3e"),_=>new("888888")};
}
