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
    public string Language {get;set;}="ru";
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
        foreach(var o in _map.Objects.Where(o=>CityMap.Shown(o,State.Layers,State.Participant,State.Focus)).OrderBy(o=>o.Layer==CityLayer.Property?0:o.Layer==CityLayer.Roads?1:2))
        {
            var color=ColorFor(o,_map.Viewer);
            if(o.Route is {Length:>1} route&&o.Layer==CityLayer.Roads)DrawRoute(route,color,false);
            else if(o.Route is {Length:>1} delivery&&o.Layer==CityLayer.Deliveries)DrawRoute(delivery,new("c47b2b"),true);
            else if(o.Route is {Length:>1} travel&&o.Kind is "vehicle" or "equipment")DrawRoute(travel,new("8fd0c6"),true);
            foreach(var cell in o.Footprint)
            {
                var rect=new Rect2(Point(cell.X,cell.Z),new(unit,unit));if(!rect.Intersects(new Rect2(Vector2.Zero,Size)))continue;
                if(o.Layer==CityLayer.Property)DrawLand(rect,o,color,unit);
                else if(o.Layer==CityLayer.Roads){DrawRect(rect,color);DrawRect(rect,color.Darkened(0.28f),false,Math.Max(1f,unit*.05f));}
                else if(o.Kind=="actor")DrawCircle(Point(cell),Math.Max(2,unit*.19f),color);
                else DrawRect(rect.Grow(-unit*.16f),color);
                if(o.Facts?.Percent() is int pct&&unit>=8)
                {
                    var bar=new Rect2(rect.Position.X+2,rect.End.Y-Math.Max(3,unit*.16f),Math.Max(0,rect.Size.X-4),Math.Max(3,unit*.12f));
                    DrawRect(bar,new Color(0,0,0,.45f));DrawRect(new Rect2(bar.Position,new Vector2(bar.Size.X*pct/100f,bar.Size.Y)),new Color("f4d35e"));
                }
                if(o.Facts is {Late:true})DrawCircle(rect.GetCenter(),Math.Max(3,unit*.16f),new Color("e15a4a"));
                else if(o.Facts is {Soon:true})DrawArc(rect.GetCenter(),Math.Max(4,unit*.28f),0,Mathf.Tau,16,new Color("e39b2b"),Math.Max(2,unit*.08f));
            }
            if(o.Footprint.Length>0&&o.Layer!=CityLayer.Property&&o.Kind!="road")
                Mark(o,unit,o.Key==State.SelectedKey);
        }
        if(_preview.Length>1){DrawRoute(_preview,new Color("16343a"),false,7);DrawRoute(_preview,new Color("f4fbff"),false,3);}
        var selected=CityMap.Selected(_map,State.SelectedKey);if(selected is not null&&(selected.Layer==0||CityMap.Shown(selected,State.Layers,State.Participant,State.Focus)))
        {DrawRect(new Rect2(Point(selected.Entry.X,selected.Entry.Z),new(unit,unit)).Grow(3),new Color("fff4b7"),false,3);DrawCircle(Point(selected.Entry),4,Colors.White);}
    }
    private void Mark(CityMapObject o,float unit,bool selected)
    {
        var center=Point(o.Entry);
        bool named=selected||unit>=20;
        if(o.Layer==CityLayer.Resources||o.Kind is "resource" or "resource-depleted")
        {
            float side=Mathf.Max(8,unit*.62f);
            ResourceIcon.Paint(this,o.Label,new Rect2(center-new Vector2(side,side)/2,new Vector2(side,side)));
            if(named)Caption(center+new Vector2(0,side*.55f),PlayerText.Item(Language,o.Label),unit);
            return;
        }
        if(o.Kind is "vehicle" or "equipment")
        {
            var body=new Rect2(center-new Vector2(unit*.2f,unit*.12f),new Vector2(unit*.4f,unit*.24f));
            DrawRect(body,new Color("24312d"));
            DrawRect(body.Grow(-1),o.Kind=="equipment"?new Color("d7c27a"):new Color("f4f7f2"));
            return;
        }
        if(o.Layer==CityLayer.Deliveries)
        {
            DrawLine(center+new Vector2(-unit*.18f,0),center+new Vector2(unit*.16f,0),new Color("24312d"),Math.Max(1,unit*.08f));
            DrawLine(center+new Vector2(unit*.16f,0),center+new Vector2(0,-unit*.12f),new Color("24312d"),Math.Max(1,unit*.08f));
            DrawLine(center+new Vector2(unit*.16f,0),center+new Vector2(0,unit*.12f),new Color("24312d"),Math.Max(1,unit*.08f));
            return;
        }
        if(o.Kind is "civic" or "capital" or "production" or "warehouse" or "shop" or "project")
        {
            float w=Mathf.Max(6,unit*.34f),h=Mathf.Max(5,unit*.26f);
            DrawColoredPolygon([center+new Vector2(-w*.55f,h*.15f),center+new Vector2(0,-h*.55f),center+new Vector2(w*.55f,h*.15f)],new Color("24312d"));
            DrawRect(new Rect2(center+new Vector2(-w*.35f,-h*.05f),new Vector2(w*.7f,h*.55f)),new Color("f7f4ea"));
            if(named)Caption(center+new Vector2(0,h*.85f),PlaceName(o),unit);
            return;
        }
        if(o.Kind=="actor"&&named)Caption(center+new Vector2(0,unit*.28f),o.Id.ToString(),unit);
        else if(o.Kind=="service"&&named)Caption(center,PlayerText.Local(Language,"Услуга","Service"),unit);
    }
    private void Caption(Vector2 at,string text,float unit)
    {
        if(text.Length==0)return;
        int size=Math.Clamp((int)(unit*.42f),9,14);
        DrawString(ThemeDB.FallbackFont,at+new Vector2(0,size*.35f),text,HorizontalAlignment.Center,unit*2.2f,size,new Color("17211e"));
    }
    private string PlaceName(CityMapObject o)
    {
        string L(string ru,string en)=>Language.StartsWith("en",StringComparison.Ordinal)?en:ru;
        string name=o.Kind switch
        {
            "civic"=>o.Label switch{"cityhall" or "town_hall"=>L("Ратуша","Hall"),"capital_terminal" or "terminal"=>L("Терминал","Terminal"),"dealership"=>L("Автосалон","Dealer"),"bank"=>L("Банк","Bank"),_=>L("Город","City")},
            "capital"=>L("Ратуша","Hall"),
            "production"=>PlayerText.Building(Language,o.Label),
            "warehouse"=>L("Склад","Store"),
            "shop"=>L("Лавка","Shop"),
            "project"=>L("Стройка","Site"),
            _=>""
        };
        return o.Level>1?name+" "+o.Level:name;
    }
    private void DrawLand(Rect2 rect,CityMapObject o,Color color,float unit)
    {
        float width=Math.Max(1,unit*(o.Facts?.State=="Own"?.16f:.09f));
        if(o.Facts?.State=="Reserved"||o.Status=="Reserved")DashRect(rect.Grow(-unit*.08f),o.Holder>0?MapSymbols.ActorColor(o.Holder,o.Holder==(_map?.Viewer??0)):color,width);
        else if(o.Facts?.State=="Free"||o.Owner<=0&&o.Holder<=0)DrawRect(rect.Grow(-unit*.1f),new Color("efe6cf"),false,width);
        else DrawRect(rect.Grow(-unit*.08f),color,false,width);
    }
    private void DrawRoute(Cell[] route,Color color,bool dashed,float width=3)
    {for(int i=1;i<route.Length;i++){if(dashed)Dash(Point(route[i-1]),Point(route[i]),color,width);else DrawLine(Point(route[i-1]),Point(route[i]),color,width,true);}}
    private void Dash(Vector2 a,Vector2 b,Color color,float width)
    {
        var delta=b-a;float len=delta.Length();if(len<1)return;var step=delta/len;
        for(float t=0;t<len;t+=10)DrawLine(a+step*t,a+step*Mathf.Min(t+6,len),color,width,true);
    }
    private void DashRect(Rect2 rect,Color color,float width)
    {
        Dash(rect.Position,new(rect.End.X,rect.Position.Y),color,width);Dash(new(rect.End.X,rect.Position.Y),rect.End,color,width);
        Dash(rect.End,new(rect.Position.X,rect.End.Y),color,width);Dash(new(rect.Position.X,rect.End.Y),rect.Position,color,width);
    }
    public static Color ColorFor(CityMapObject o,long viewer=1)=>o.Layer switch
    {CityLayer.Property=>o.Owner>0?MapSymbols.ActorColor(o.Owner,o.Owner==viewer):o.Holder>0?MapSymbols.ActorColor(o.Holder,o.Holder==viewer):new("efe6cf"),CityLayer.Roads=>o.Kind=="bypass"?new("ffb859"):o.Level switch{1=>new("ae9874"),2=>new("c8ccd0"),_=>new("f2e7a5")},CityLayer.Resources=>new("72c6a0"),CityLayer.Deliveries=>o.Facts is {Late:true}?new("e15a4a"):o.Facts is {Soon:true}?new("e39b2b"):new("ecb76c"),CityLayer.Services=>new("d798ef"),CityLayer.People=>MapSymbols.ActorColor(o.Owner,o.Owner==viewer),_=>o.Owner>0?MapSymbols.ActorColor(o.Owner,o.Owner==viewer):new("d5e2e3")};
}
