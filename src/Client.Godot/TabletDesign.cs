using Godot;

/// <summary>Scoped visual tokens from the supplied tablet kit. No game state or clock.</summary>
public static class TabletDesign
{
    public static readonly Color Ink=new("24292f"),Muted=new("69766e"),Green=new("24664f");
    private static Theme? _theme;
    public static Theme Theme => _theme ??= Create();
    private static Theme Create()
    {
        var t=(Theme)GD.Load<Theme>("res://tablets_v1/theme.tres").Duplicate(true);
        t.DefaultFontSize=16;
        foreach(string type in new[]{"Label","Button","CheckButton","CheckBox","OptionButton","LineEdit","SpinBox","ItemList","RichTextLabel","TabContainer","TabBar"})
        {
            t.SetColor("font_color",type,Ink);t.SetColor("font_hover_color",type,Ink);t.SetColor("font_focus_color",type,Ink);
            t.SetColor("font_pressed_color",type,new("fffbee"));t.SetColor("font_disabled_color",type,Muted);
        }
        t.SetColor("default_color","RichTextLabel",Ink);t.SetColor("font_pressed_color","CheckButton",Ink);t.SetColor("font_pressed_color","CheckBox",Ink);
        t.SetColor("font_color","PopupMenu",Ink);t.SetColor("font_hover_color","PopupMenu",Ink);t.SetStylebox("panel","PopupMenu",Card());
        t.SetStylebox("panel","PanelContainer",Card());
        foreach(string type in new[]{"Button","OptionButton"})
        {
            t.SetStylebox("normal",type,Box("ffffff",8,9));t.SetStylebox("hover",type,Box("edf3f0",8,9));
            t.SetStylebox("pressed",type,Box("24664f",8,9));t.SetStylebox("disabled",type,Box("f3f4f5",8,9));
        }
        foreach(string state in new[]{"normal","read_only"})t.SetStylebox(state,"LineEdit",Box("f4f4e9",10,10));
        t.SetStylebox("focus","LineEdit",Box("fffdf6",10,10));t.SetColor("font_color","LineEdit",Ink);
        t.SetStylebox("panel","ItemList",Card());t.SetStylebox("selected","ItemList",Box("e2ebd5",8,8));t.SetColor("font_selected_color","ItemList",Ink);
        t.SetStylebox("panel","TabContainer",Box("f7f3e8",12,12));
        foreach(string type in new[]{"TabBar","TabContainer"})
        {
            t.SetStylebox("tab_selected",type,Box("2b7b57",12,12));t.SetStylebox("tab_unselected",type,Box("f4f4e9",12,12));
            t.SetStylebox("tab_hovered",type,Box("e2ebd5",12,12));t.SetColor("font_selected_color",type,new("fffbee"));t.SetColor("font_unselected_color",type,Ink);
        }
        t.SetIcon("arrow","OptionButton",GD.Load<Texture2D>("res://tablets_v1/assets/icons/down.svg"));
        t.SetIcon("updown","SpinBox",GD.Load<Texture2D>("res://tablets_v1/assets/icons/stepper.svg"));
        t.SetStylebox("slider","HSlider",Box("dce5cd",4,0));t.SetStylebox("grabber_area","HSlider",Box("7da568",4,0));t.SetStylebox("grabber_area_highlight","HSlider",Box("2b7b57",4,0));
        t.SetStylebox("background","ProgressBar",Box("dce5cd",5,0));t.SetStylebox("fill","ProgressBar",Box("7da568",5,0));t.SetColor("font_color","ProgressBar",Ink);
        t.SetConstant("separation","VBoxContainer",8);t.SetConstant("separation","HBoxContainer",8);
        t.SetConstant("h_separation","HFlowContainer",8);t.SetConstant("v_separation","HFlowContainer",6);
        return t;
    }
    public static StyleBoxFlat Box(string color,int radius,int margin)=>new(){BgColor=new(color),BorderColor=new("dfe3d1"),
        BorderWidthLeft=1,BorderWidthRight=1,BorderWidthTop=1,BorderWidthBottom=1,
        CornerRadiusTopLeft=radius,CornerRadiusTopRight=radius,CornerRadiusBottomLeft=radius,CornerRadiusBottomRight=radius,
        ContentMarginLeft=margin,ContentMarginRight=margin,ContentMarginTop=margin,ContentMarginBottom=margin};
    public static StyleBoxFlat Card(bool soft=false)=>Box(soft?"eff4e9":"ffffff",8,10);
    public static PanelContainer Wrap(Control content,bool soft=false)
    {var panel=new PanelContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};panel.AddThemeStyleboxOverride("panel",Card(soft));panel.AddChild(content);return panel;}
    public static Label Heading(string text,int size=23)
    {var l=new Label{Text=text,AutowrapMode=TextServer.AutowrapMode.WordSmart};l.AddThemeFontSizeOverride("font_size",size);l.AddThemeFontOverride("font",GD.Load<Font>("res://delsk_sawmill_ui/assets/fonts/Nunito-ExtraBold.ttf"));return l;}
    public static TextureRect Art(string key,Vector2 size)
        =>new(){Texture=Texture(key),CustomMinimumSize=size,ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter=Control.MouseFilterEnum.Ignore};
    public static Texture2D Texture(string key)
    {
        key=key switch{"wood"=>"logs","plank"=>"boards","shop"=>"market","sugar-factory"=>"sugar_factory","woodworking"=>"sawmill","food"=>"bakery",_=>key};
        string path=key is "dairy" or "sugar_factory" or "confectionery" or "bakery"?"res://tablets_v1/assets/"+key+".svg":"res://tablets_v1/assets/icons/"+key+".svg";
        if(key=="sawmill")path="res://delsk_sawmill_ui/assets/sawmill.svg";
        if(key is "metallurgy" or "materials" or "machinery")path="res://game_ui/art/"+key+".svg";
        return ResourceLoader.Exists(path)?GD.Load<Texture2D>(path):GameUiArt.Item(key);
    }
    public static void CompactInputs(Node root)
    {
        foreach(Node child in root.GetChildren())
        {
            if(child is OptionButton select){select.FitToLongestItem=false;select.ClipText=true;select.CustomMinimumSize=new(150,40);select.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;}
            if(child is Button button && child is not OptionButton && child is not CheckButton && button.Text.Length>42)button.AutowrapMode=TextServer.AutowrapMode.WordSmart;
            CompactInputs(child);
        }
    }
    public static HFlowContainer Flow()=>new(){SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
}

/// <summary>Readability at narrow widths, using native box layout rather than scaling text.</summary>
public partial class TabletColumns : BoxContainer
{
    public float Breakpoint {get;set;}=850;
    public TabletColumns(){SizeFlagsHorizontal=SizeFlags.ExpandFill;Resized+=()=>Vertical=Size.X<Breakpoint||GetViewportRect().Size.Y<540;}
}
