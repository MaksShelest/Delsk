using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>A cosmetic map viewport. All gestures are consumed here, never by world movement.</summary>
public partial class CityMapCanvas : Control
{
    public event Action<Cell>? CellChosen;
    public CityMapPreferences State {get;}
    private CityMapFrame? _map;
    private Cell[] _preview=[];
    private readonly Dictionary<int,Vector2> _touches=[];
    private Vector2 _press,_last;
    private bool _pressed,_dragged,_pinched;
    public CityMapCanvas(CityMapPreferences state)
    {Name="CityMapCanvas";State=state;ClipContents=true;MouseFilter=MouseFilterEnum.Stop;FocusMode=FocusModeEnum.All;CustomMinimumSize=new(220,260);SizeFlagsVertical=SizeFlags.ExpandFill;SizeFlagsHorizontal=SizeFlags.ExpandFill;}
    public void ShowData(CityMapFrame frame,Cell[]? preview=null)
    {_map=frame;State.Bind(frame.Width,frame.Height);_preview=preview??[];QueueRedraw();}
    public void Fit(){State.Fit();QueueRedraw();}
    public void Zoom(double factor){State.ZoomAt(factor,Size.X/2,Size.Y/2,Size.X,Size.Y);QueueRedraw();}
    public Vector2 Point(Cell cell)=>Point(cell.X+.5,cell.Z+.5);
    private Vector2 Point(double x,double z)=>new((float)(Size.X/2+(x-State.CenterX)*State.Unit(Size.X,Size.Y)),(float)(Size.Y/2+(z-State.CenterZ)*State.Unit(Size.X,Size.Y)));
    private void Pick(Vector2 position){if(State.CellAt(position.X,position.Y,Size.X,Size.Y) is {} c)CellChosen?.Invoke(c);}
    public override void _GuiInput(InputEvent e)
    {
        if(_map is null)return;
        if(e is InputEventMouseButton b)
        {
            GrabFocus();
            if(b.Pressed&&b.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown){State.ZoomAt(b.ButtonIndex==MouseButton.WheelUp?1.25:.8,b.Position.X,b.Position.Y,Size.X,Size.Y);QueueRedraw();}
            else if(b.ButtonIndex is MouseButton.Left or MouseButton.Middle)
            {if(b.Pressed){if(_touches.Count==0)_pinched=false;_press=_last=b.Position;_pressed=true;_dragged=b.ButtonIndex==MouseButton.Middle;}else{if(_pressed&&!_dragged&&!_pinched&&_touches.Count==0)Pick(b.Position);_pressed=false;}}
            AcceptEvent();
        }
        else if(e is InputEventMouseMotion motion&&_pressed)
        {if(motion.Position.DistanceTo(_press)>6)_dragged=true;if(_dragged)State.Pan(motion.Position.X-_last.X,motion.Position.Y-_last.Y,Size.X,Size.Y);_last=motion.Position;QueueRedraw();AcceptEvent();}
        else if(e is InputEventScreenTouch touch)
        {
            GrabFocus();if(touch.Pressed){_touches[touch.Index]=touch.Position;if(_touches.Count==1){_press=touch.Position;_dragged=false;_pinched=false;}else _pinched=true;}
            else{if(_touches.Count==1&&!_dragged&&!_pinched&&!touch.Canceled)Pick(touch.Position);_touches.Remove(touch.Index);if(_touches.Count==0)_pressed=false;}
            AcceptEvent();
        }
        else if(e is InputEventScreenDrag drag&&_touches.TryGetValue(drag.Index,out var old))
        {
            if(_touches.Count==2)
            {
                var other=_touches.First(p=>p.Key!=drag.Index).Value;float before=old.DistanceTo(other),after=drag.Position.DistanceTo(other);
                var midpoint=(drag.Position+other)/2;var previous=(old+other)/2;
                State.Pan(midpoint.X-previous.X,midpoint.Y-previous.Y,Size.X,Size.Y);
                if(before>1)State.ZoomAt(after/before,midpoint.X,midpoint.Y,Size.X,Size.Y);_pinched=true;
            }
            else{if(drag.Position.DistanceTo(_press)>8)_dragged=true;if(_dragged)State.Pan(drag.Position.X-old.X,drag.Position.Y-old.Y,Size.X,Size.Y);}
            _touches[drag.Index]=drag.Position;QueueRedraw();AcceptEvent();
        }
        else if(e is InputEventMagnifyGesture magnify){State.ZoomAt(magnify.Factor,magnify.Position.X,magnify.Position.Y,Size.X,Size.Y);QueueRedraw();AcceptEvent();}
        else if(e is InputEventPanGesture pan){State.Pan(-pan.Delta.X*24,-pan.Delta.Y*24,Size.X,Size.Y);QueueRedraw();AcceptEvent();}
        else if(e is InputEventKey {Pressed:true} key)
        {
            bool handled=true;
            switch(key.Keycode){case Key.A:case Key.Left:State.Pan(50,0,Size.X,Size.Y);break;case Key.D:case Key.Right:State.Pan(-50,0,Size.X,Size.Y);break;case Key.W:case Key.Up:State.Pan(0,50,Size.X,Size.Y);break;case Key.S:case Key.Down:State.Pan(0,-50,Size.X,Size.Y);break;case Key.Equal:case Key.Plus:Zoom(1.25);break;case Key.Minus:Zoom(.8);break;case Key.Home:Fit();break;case Key.Enter:case Key.KpEnter:Pick(Size/2);break;default:handled=false;break;}
            if(handled){QueueRedraw();AcceptEvent();}
        }
    }
    public override void _Notification(int what)
    {if(what==NotificationVisibilityChanged||what==NotificationExitTree||what==NotificationApplicationFocusOut){_pressed=false;_touches.Clear();_pinched=false;}}
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero,Size),new Color("1c3034"));if(_map is null)return;
        var unit=(float)State.Unit(Size.X,Size.Y);var bounds=new Rect2(Point(0,0),new(_map.Width*unit,_map.Height*unit));DrawRect(bounds,new Color("718261"));
        foreach(var cell in _map.Blocked){var rect=new Rect2(Point(cell.X,cell.Z),new(unit,unit));if(rect.Intersects(new Rect2(Vector2.Zero,Size)))DrawRect(rect,new Color("354950"));}
        if(unit>=14)
        {
            var start=State.World(0,0,Size.X,Size.Y);var end=State.World(Size.X,Size.Y,Size.X,Size.Y);
            for(int x=Math.Max(0,(int)start.X);x<=Math.Min(_map.Width,(int)end.X+1);x++)DrawLine(Point(x,0),Point(x,_map.Height),new Color(1,1,1,.08f));
            for(int z=Math.Max(0,(int)start.Z);z<=Math.Min(_map.Height,(int)end.Z+1);z++)DrawLine(Point(0,z),Point(_map.Width,z),new Color(1,1,1,.08f));
        }
        foreach(var o in _map.Objects.Where(o=>CityMap.Visible(o,State.Layers,State.Participant)).OrderBy(o=>o.Layer==CityLayer.Property?0:o.Layer==CityLayer.Roads?1:2))
        {
            var color=ColorFor(o,_map.Viewer);
            if(o.Route is {Length:>1} route&&o.Layer is CityLayer.Deliveries or CityLayer.Roads)DrawRoute(route,color);
            foreach(var cell in o.Footprint)
            {
                var rect=new Rect2(Point(cell.X,cell.Z),new(unit,unit));if(!rect.Intersects(new Rect2(Vector2.Zero,Size)))continue;
                if(o.Layer==CityLayer.Property)DrawRect(rect.Grow(-unit*.08f),color,false,Math.Max(1,unit*.09f));
                else if(o.Layer==CityLayer.Roads)DrawRect(rect.Grow(-unit*.12f),color);
                else if(o.Kind=="actor")DrawCircle(Point(cell),Math.Max(2,unit*.19f),color);
                else DrawRect(rect.Grow(-unit*.16f),color);
            }
            if(unit>=16&&o.Layer!=CityLayer.Property&&o.Kind!="road"&&o.Footprint.Length>0)
            {
                string symbol=o.Kind=="actor"?o.Id.ToString():o.Kind=="vehicle"?"V":o.Layer==CityLayer.Resources?"R":o.Layer==CityLayer.Deliveries?"→":o.Kind=="service"?"D":o.Level>0?"I"+o.Level:"◆";
                DrawString(ThemeDB.FallbackFont,Point(o.Entry)+new Vector2(-5,5),symbol,HorizontalAlignment.Left,-1,Math.Clamp((int)(unit*.55f),10,18),Colors.Black);
            }
        }
        if(_preview.Length>1)DrawRoute(_preview,new Color("fff3a3"));
        var selected=CityMap.Selected(_map,State.SelectedKey);if(selected is not null&&(selected.Layer==0||CityMap.Visible(selected,State.Layers,State.Participant)))
        {DrawRect(new Rect2(Point(selected.Entry.X,selected.Entry.Z),new(unit,unit)).Grow(3),new Color("fff4b7"),false,3);DrawCircle(Point(selected.Entry),4,Colors.White);}
    }
    private void DrawRoute(Cell[] route,Color color)
    {for(int i=1;i<route.Length;i++)DrawLine(Point(route[i-1]),Point(route[i]),color,3,true);}
    public static Color ColorFor(CityMapObject o,long viewer=1)=>o.Layer switch
    {CityLayer.Property=>o.Owner>0?MapSymbols.ActorColor(o.Owner,o.Owner==viewer):new("d5c9a7"),CityLayer.Roads=>o.Kind=="bypass"?new("ffb859"):o.Level switch{1=>new("ae9874"),2=>new("c8ccd0"),_=>new("f2e7a5")},CityLayer.Resources=>new("72c6a0"),CityLayer.Deliveries=>new("ecb76c"),CityLayer.Services=>new("d798ef"),CityLayer.People=>MapSymbols.ActorColor(o.Owner,o.Owner==viewer),_=>o.Owner>0?MapSymbols.ActorColor(o.Owner,o.Owner==viewer):new("d5e2e3")};
}
