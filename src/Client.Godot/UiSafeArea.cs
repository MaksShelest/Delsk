using Godot;
using Simulation.Hosting;
public static class UiSafeArea
{
    public static ScreenInsets Read(Control control)
    {
        if(DisplayServer.GetName()=="headless"||Engine.IsEmbeddedInEditor())return default;
        var safe=DisplayServer.GetDisplaySafeArea();var window=DisplayServer.WindowGetSize();var origin=DisplayServer.WindowGetPosition();var size=control.GetViewportRect().Size;
        return ScreenInsets.Calculate(size.X,size.Y,origin.X,origin.Y,window.X,window.Y,safe.Position.X,safe.Position.Y,safe.Size.X,safe.Size.Y);
    }
    public static void Track(Control control,Action apply)
    {
        void Bind(){var viewport=control.GetViewport();viewport.SizeChanged+=apply;control.TreeExiting+=()=>viewport.SizeChanged-=apply;apply();}
        if(control.IsInsideTree())Bind();else control.Ready+=Bind;
    }
    public static void Panel(Control panel,float top=66,float bottom=18,float margin=12,float wideMargin=64)
    {
        Track(panel,()=>{var s=Read(panel);float side=panel.GetViewportRect().Size.X>=1360?wideMargin:margin;
            panel.OffsetLeft=side+(float)s.Left;panel.OffsetRight=-side-(float)s.Right;panel.OffsetTop=top+(float)s.Top;panel.OffsetBottom=-bottom-(float)s.Bottom;});
    }
    public static void Keyboard(Node root)
    {foreach(Node child in root.GetChildren()){if(child is BaseButton b)b.FocusMode=Control.FocusModeEnum.All;Keyboard(child);}}
    public static void Popup(AcceptDialog dialog,Control parent,Vector2I requested)
    {
        var safe=Read(parent);var viewport=parent.GetViewportRect().Size;
        var available=new Vector2I(Math.Max(100,(int)(viewport.X-safe.Left-safe.Right-24)),Math.Max(100,(int)(viewport.Y-safe.Top-safe.Bottom-64)));
        var size=new Vector2I(Math.Min(requested.X,available.X),Math.Min(requested.Y,available.Y));
        dialog.DialogAutowrap=true;dialog.MinSize=Vector2I.Zero;TabletDesign.CompactInputs(dialog);Keyboard(dialog);
        dialog.Popup(new Rect2I(new((int)safe.Left+12+(available.X-size.X)/2,(int)safe.Top+32+(available.Y-size.Y)/2),size));
    }
    public static void CenterColumn(ScrollContainer scroll,MarginContainer margin,float maximumWidth)
    {
        Track(scroll,()=>{var safe=Read(scroll);float width=scroll.GetViewportRect().Size.X;int side=(int)Math.Max(12,(width-safe.Left-safe.Right-maximumWidth)/2);
            margin.AddThemeConstantOverride("margin_left",side+(int)safe.Left);margin.AddThemeConstantOverride("margin_right",side+(int)safe.Right);margin.AddThemeConstantOverride("margin_top",16+(int)safe.Top);margin.AddThemeConstantOverride("margin_bottom",16+(int)safe.Bottom);});
    }
}
