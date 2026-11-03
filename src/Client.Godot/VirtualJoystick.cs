using Godot;
using Simulation.Hosting;

/// <summary>Touch/mouse input only; the same normalized heading goes to GameSession.</summary>
public partial class VirtualJoystick : Control
{
    private readonly TravelStick _stick=new();
    private Vector2 _point;
    private int _finger=-1;
    public TravelHeading Heading=>_stick.Heading;
    public bool Held=>_finger!=-1;
    private Vector2 Centre=>Size/2;
    public VirtualJoystick()
    {
        Name="TravelJoystick";CustomMinimumSize=new(144,144);MouseFilter=MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);OffsetLeft=20;OffsetTop=-170;OffsetRight=164;OffsetBottom=-26;ZIndex=20;
    }
    public void Clear(){_finger=-1;_point=Vector2.Zero;_stick.Clear();QueueRedraw();}
    private void Sample(Vector2 at)
    {
        _point=(at-Centre).LimitLength(58);var value=_point/58;
        _stick.Sample(Mathf.RoundToInt(value.X*1000),Mathf.RoundToInt(value.Y*1000));QueueRedraw();
    }
    public override void _GuiInput(InputEvent e)
    {
        if(e is InputEventScreenTouch touch)
        {
            if(touch.Pressed&&_finger==-1){_finger=touch.Index;Sample(touch.Position);AcceptEvent();}
            else if(!touch.Pressed&&touch.Index==_finger){Clear();AcceptEvent();}
        }
        else if(e is InputEventScreenDrag drag&&drag.Index==_finger){Sample(drag.Position);AcceptEvent();}
        else if(e is InputEventMouseButton mouse&&mouse.ButtonIndex==MouseButton.Left)
        {if(mouse.Pressed&&_finger==-1){_finger=-2;Sample(mouse.Position);}else if(!mouse.Pressed&&_finger==-2)Clear();AcceptEvent();}
        else if(e is InputEventMouseMotion motion&&_finger==-2){Sample(motion.Position);AcceptEvent();}
    }
    public override void _Input(InputEvent e)
    {
        // A release outside the original control must still return to neutral.
        if(e is InputEventScreenTouch {Pressed:false} touch&&touch.Index==_finger||
            e is InputEventMouseButton {Pressed:false,ButtonIndex:MouseButton.Left}&&_finger==-2)Clear();
    }
    public override void _Draw()
    {
        DrawCircle(Centre,66,new Color(.12f,.18f,.2f,.35f));
        DrawArc(Centre,65,0,Mathf.Tau,48,new Color(.9f,.9f,.8f,.7f),2,true);
        DrawCircle(Centre+_point,25,new Color(.9f,.85f,.65f,.75f));
    }
}
