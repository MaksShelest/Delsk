using Godot;

/// <summary>I6-A opt-in tokens. The established tablet kit is unchanged.</summary>
public static class CompactTabletDesign
{
    private static Theme? _theme;
    public static Theme Theme=>_theme??=Create();
    private static Theme Create()
    {
        var theme=(Theme)TabletDesign.Theme.Duplicate(true);theme.DefaultFontSize=16;
        foreach(string type in new[]{"Label","Button","OptionButton","CheckButton","CheckBox","LineEdit","RichTextLabel"})
        {
            theme.SetColor("font_color",type,new("24292f"));theme.SetColor("font_hover_color",type,new("24292f"));
            theme.SetColor("font_focus_color",type,new("24292f"));theme.SetColor("font_disabled_color",type,new("7a8189"));
        }
        foreach(string type in new[]{"Button","OptionButton"})
        {
            theme.SetStylebox("normal",type,Surface("ffffff",8));theme.SetStylebox("hover",type,Surface("edf3f0",8));
            theme.SetStylebox("pressed",type,Surface("22664f",8));theme.SetStylebox("disabled",type,Surface("f3f4f5",8));
            theme.SetStylebox("focus",type,new StyleBoxFlat{BgColor=Colors.Transparent,BorderColor=new("22664f"),BorderWidthLeft=2,BorderWidthRight=2,BorderWidthTop=2,BorderWidthBottom=2});
        }
        theme.SetStylebox("panel","PanelContainer",Surface("ffffff",10));theme.SetStylebox("panel","TabContainer",Surface("fafafa",4));
        var arrow=new Image();arrow.LoadSvgFromString("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\" viewBox=\"0 0 16 16\"><path d=\"m4 6 4 4 4-4\" fill=\"none\" stroke=\"#52625c\" stroke-width=\"2\" stroke-linecap=\"round\"/></svg>");
        theme.SetIcon("arrow","OptionButton",ImageTexture.CreateFromImage(arrow));
        foreach(string type in new[]{"VBoxContainer","HBoxContainer"})theme.SetConstant("separation",type,8);
        theme.SetConstant("h_separation","HFlowContainer",8);theme.SetConstant("v_separation","HFlowContainer",6);
        theme.SetStylebox("background","ProgressBar",Surface("e9eeeb",0));theme.SetStylebox("fill","ProgressBar",Surface("408269",0));
        return theme;
    }
    public static StyleBoxFlat Surface(string color,int margin)=>new(){BgColor=new(color),BorderColor=new("e0e4e2"),
        BorderWidthLeft=1,BorderWidthRight=1,BorderWidthTop=1,BorderWidthBottom=1,
        CornerRadiusTopLeft=8,CornerRadiusTopRight=8,CornerRadiusBottomLeft=8,CornerRadiusBottomRight=8,
        ContentMarginLeft=margin,ContentMarginRight=margin,ContentMarginTop=margin,ContentMarginBottom=margin};
    public static PanelContainer Card(Control content)
    {var panel=new PanelContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};panel.AddThemeStyleboxOverride("panel",Surface("ffffff",10));panel.AddChild(content);return panel;}
    public static Button Button(string name,string text,Action action)
    {
        var button=new Button{Name=name,Text=text,CustomMinimumSize=new(40,40),SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin};
        button.Pressed+=()=>{if(!button.Disabled)action();};return button;
    }
    /// <summary>Content-sized controls inside player pages, including reused legacy pages.</summary>
    public static void FitControls(Node root,float availableWidth)
    {
        foreach(var node in root.GetChildren())
        {
            if(node is HouseholdObjectCard)continue;
            if(node is Button button&&button is not OptionButton&&button is not CheckButton&&button is not CheckBox&&button is not ContextActionButton)
            {
                float natural=button.GetThemeFont("font").GetStringSize(button.Text,fontSize:button.GetThemeFontSize("font_size")).X+24;
                float width=Mathf.Min(Mathf.Max(40,natural),Mathf.Max(40,availableWidth));
                button.CustomMinimumSize=new(width,40);button.SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin;
                button.AutowrapMode=natural>width?TextServer.AutowrapMode.WordSmart:TextServer.AutowrapMode.Off;
            }
            if(node is OptionButton choice){choice.CustomMinimumSize=new(0,40);choice.FitToLongestItem=false;choice.ClipText=true;}
            FitControls(node,availableWidth);
        }
    }
}
