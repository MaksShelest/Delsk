using Godot;

/// <summary>One quantity, editable by slider, exact input and +/- buttons.</summary>
public partial class QuantityInput : HBoxContainer
{
    private readonly SpinBox _number=new(){MinValue=1,MaxValue=1000000,Value=1,Step=1,CustomMinimumSize=new(120,0)};
    private readonly HSlider _slider=new(){MinValue=1,MaxValue=1000000,Value=1,Step=1,SizeFlagsHorizontal=SizeFlags.ExpandFill,CustomMinimumSize=new(140,0)};
    public event System.Action<long>? Changed;
    private long _value=1;
    private bool _syncing;
    public long Value {get=>_value;set=>Set(value);}
    public void Limit(long maximum){_syncing=true;_number.MaxValue=_slider.MaxValue=System.Math.Max(1,System.Math.Min(1000000,maximum));_syncing=false;Set(_value);}
    public QuantityInput()
    {
        var minus=new Button{Text="−",FocusMode=FocusModeEnum.None};var plus=new Button{Text="+",FocusMode=FocusModeEnum.None};
        AddChild(minus);AddChild(_slider);AddChild(_number);AddChild(plus);
        minus.Pressed+=()=>Set(Value-1);plus.Pressed+=()=>Set(Value+1);
        _number.ValueChanged+=v=>{if(!_syncing)Set((long)v);};_slider.ValueChanged+=v=>{if(!_syncing)Set((long)v);};
    }
    private void Set(long value){long next=System.Math.Clamp(value,1,(long)_number.MaxValue);bool changed=next!=_value;_value=next;_number.SetValueNoSignal(next);_slider.SetValueNoSignal(next);if(changed)Changed?.Invoke(next);}
}
