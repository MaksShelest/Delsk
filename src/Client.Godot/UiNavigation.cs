using Godot;

public static class UiNavigation
{
    public static Action KeepFocus(Control root)
    {
        var focus=root.GetViewport().GuiGetFocusOwner();
        if(focus is not LineEdit edit||edit.GetParent() is not SpinBox spin||spin.Name.ToString().StartsWith('@'))return ()=>{};
        string name=spin.Name;string text=edit.Text;int caret=edit.CaretColumn;
        return ()=>{if(!GodotObject.IsInstanceValid(root)||!root.IsInsideTree())return;
            if(root.FindChild(name,true,false) is SpinBox live){var field=live.GetLineEdit();field.Text=text;field.GrabFocus();field.CaretColumn=Math.Min(caret,text.Length);}};
    }
    public static Action KeepScroll(Control child)
    {
        var scrolls=new List<(ScrollContainer Scroll,int X,int Y)>();
        for(Node? n=child;n is not null;n=n.GetParent())if(n is ScrollContainer s)scrolls.Add((s,s.ScrollHorizontal,s.ScrollVertical));
        return ()=>{foreach(var row in scrolls)if(GodotObject.IsInstanceValid(row.Scroll)){row.Scroll.ScrollHorizontal=row.X;row.Scroll.ScrollVertical=row.Y;}};
    }
    public static void Reveal(Control target)
    {
        target.Visible=true;
        for(Node? n=target;n is Control;n=n.GetParent())
        {
            if(n.GetParent() is VBoxContainer box&&box.GetChildCount()==2&&box.GetChild(1)==n&&box.GetChild(0) is BaseButton {ToggleMode:true} toggle)
            {toggle.ButtonPressed=true;((Control)n).Visible=true;}
            if(n.GetParent() is ScrollContainer scroll)
                Callable.From(()=>{if(GodotObject.IsInstanceValid(scroll)&&GodotObject.IsInstanceValid(target))scroll.EnsureControlVisible(target);}).CallDeferred();
        }
    }
}
