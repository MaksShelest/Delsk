using Godot;
using Simulation.Core;
using Simulation.Hosting;

/// <summary>Shared ordinary-game composition. Presentation only; Core remains the command authority.</summary>
public static class GameUi
{
    public static string L(string language,string ru,string en)=>PlayerText.Local(language,ru,en);
    public static Label Text(string text="",int size=16)
    {var label=new Label{Text=text,AutowrapMode=TextServer.AutowrapMode.WordSmart,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};label.AddThemeFontSizeOverride("font_size",size);label.AddThemeColorOverride("font_color",TabletDesign.Ink);return label;}
    public static Label Hint(string text)
    {var label=Text(text,14);label.AddThemeColorOverride("font_color",new("556860"));return label;}
    public static void Price(Label label){label.AddThemeColorOverride("font_color",new("1b5c40"));label.AddThemeFontSizeOverride("font_size",18);}
    public static PanelContainer Card(Control content)=>CompactTabletDesign.Card(content);
    public static string Reason(string language,string reason)
    {
        string text=PlayerText.Reason(language,reason);
        if(text==reason)text=TabletText.Reason(language,reason);
        return text==reason?L(language,"Проверьте условия действия и доступность ресурсов.","Check the action's conditions and available resources."):text;
    }
    public static string Industry(string language,string type,int level=1)=>type switch
    {
        "woodworking"=>L(language,level==1?"Лесопилка":"Деревообрабатывающий комбинат",level==1?"Sawmill":"Woodworks"),
        "food"=>L(language,level==1?"Пекарня":"Пищевой комбинат",level==1?"Bakery":"Food works"),
        "metallurgy"=>L(language,"Металлургический цех","Metal works"),
        "materials"=>L(language,"Стройматериалы","Building materials"),
        "dairy"=>L(language,"Молочное производство","Dairy"),
        "machinery"=>L(language,"Механическая мастерская","Machine workshop"),
        _=>PlayerText.Building(language,type)
    };
    public static string InventoryName(PlayerView view,long id,string language)
    {
        if(view.Participant.InventoryId.Value==id)return L(language,"Рюкзак","Backpack");
        if(view.ProductionContent?.Pickup?.Value==id)return L(language,"Выдача импорта","Import pickup");
        var vehicle=view.Fleet?.Vehicles.FirstOrDefault(v=>v.Vehicle.CargoInventory.Value==id);
        if(vehicle is not null)return L(language,"Кузов · ","Cargo · ")+(vehicle.Model.EquipmentTypeId is null?vehicle.Model.Name:L(language,"добыча ","extract ")+PlayerText.Item(language,view.Fleet!.EquipmentTypes.Single(t=>t.Id==vehicle.Model.EquipmentTypeId).ItemId));
        var b=view.Buildings.FirstOrDefault(b=>b.InputId.Value==id||b.OutputId?.Value==id);
        if(b is not null)return (b.Kind==BuildingKind.Production?Industry(language,b.TypeId,b.Level):PlayerText.Building(language,b.TypeId))+
            (b.Kind==BuildingKind.Production?(b.OutputId?.Value==id?L(language," · продукция"," · goods"):L(language," · сырьё"," · materials")):"")+$" ({b.Position.X},{b.Position.Z})";
        var inv=view.Inventories.FirstOrDefault(i=>i.Id.Value==id);
        return L(language,"Запас","Storage")+(inv is null?"":$" ({inv.Position.X},{inv.Position.Z})");
    }
    public static void Primary(Button button)
    {
        foreach(var pair in new[]{("normal","49735f"),("hover","3d6552"),("pressed","315441")})
            button.AddThemeStyleboxOverride(pair.Item1,CompactTabletDesign.Surface(pair.Item2,9));
        foreach(string state in new[]{"font_color","font_hover_color","font_pressed_color","font_hover_pressed_color","font_focus_color"})button.AddThemeColorOverride(state,new Color("f4f7f2"));
        button.AddThemeColorOverride("font_disabled_color",new("556860"));
    }
    public static Button Button(string name,string caption,Action action,bool primary=false)
    {var button=CompactTabletDesign.Button(name,caption,action);if(primary)Primary(button);return button;}
    public static void Command(Button button,Action action)
    {button.Pressed+=()=>{if(button.Disabled)return;button.Disabled=true;action();};}
    public static PanelContainer Intro(string title,string purpose,string art="",int artSize=100)
    {
        var row=new HBoxContainer();if(art.Length>0)row.AddChild(TabletDesign.Art(art,new(artSize,artSize)));
        var words=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};words.AddChild(TabletDesign.Heading(title,23));words.AddChild(Hint(purpose));row.AddChild(words);return Card(row);
    }
    public static void FitControls(Node root,float width)
    {
        width=Math.Max(80,width);
        foreach(var child in root.GetChildren())
        {
            float available=width;
            if(root is GridContainer grid)available=Math.Max(80,(width-8*(grid.Columns-1))/grid.Columns);
            if(root is TabletColumns columns&&!columns.Vertical)available=Math.Max(80,width/Math.Max(1,columns.GetChildren().OfType<Control>().Count(c=>c.Visible))-8);
            if(child is Button b&&b is not OptionButton&&b is not CheckButton&&b is not CheckBox&&b.Name!="DemolitionHold"&&!b.Name.ToString().StartsWith("PlayerNav_")&&!b.Name.ToString().StartsWith("PlayerGroup_")&&!b.Name.ToString().StartsWith("MapFocus"))
            {
                float natural=b.GetThemeFont("font").GetStringSize(b.Text,fontSize:b.GetThemeFontSize("font_size")).X+24;
                float limit=Math.Max(80,available-4);b.CustomMinimumSize=new(Math.Min(Math.Max(40,natural),limit),Math.Max(40,Math.Min(48,b.CustomMinimumSize.Y)));
                b.SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin;b.AutowrapMode=natural>limit?TextServer.AutowrapMode.WordSmart:TextServer.AutowrapMode.Off;
            }
            if(child is OptionButton option){option.FitToLongestItem=false;option.ClipText=true;option.CustomMinimumSize=new(Math.Min(150,available),40);}
            if(child is Label label&&label.AutowrapMode!=TextServer.AutowrapMode.Off&&root is HBoxContainer)label.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
            FitControls(child,available);
        }
    }
}

public partial class UiSections : VBoxContainer
{
    private readonly HFlowContainer _navigation=TabletDesign.Flow();
    private readonly Dictionary<string,(Button Button,VBoxContainer Body)> _pages=[];
    private readonly string _prefix;
    public string Selected{get;private set;}="";
    public UiSections(string prefix){_prefix=prefix;AddChild(_navigation);}
    public VBoxContainer Add(string key,string caption)
    {
        var body=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};var button=GameUi.Button(_prefix+key,caption,()=>Select(key));button.ToggleMode=true;
        _navigation.AddChild(button);AddChild(body);_pages.Add(key,(button,body));if(Selected.Length==0)Selected=key;Select(Selected);return body;
    }
    public void Select(string key)
    {if(!_pages.ContainsKey(key))return;Selected=key;foreach(var p in _pages){p.Value.Body.Visible=p.Key==key;p.Value.Button.SetPressedNoSignal(p.Key==key);}}
    public void SetAvailable(string key,bool available)
    {if(!_pages.TryGetValue(key,out var page))return;page.Button.Visible=available;if(!available){page.Body.Visible=false;if(Selected==key)Select(_pages.First(p=>p.Value.Button.Visible).Key);}}
}

public partial class UiDisclosure : VBoxContainer
{
    private readonly Button _toggle;
    public Control Content{get;}
    public UiDisclosure(string title,Control content,bool expanded=false)
    {
        Content=content;_toggle=GameUi.Button("",title,()=>{});_toggle.ToggleMode=true;_toggle.SetPressedNoSignal(expanded);AddChild(_toggle);AddChild(content);content.Visible=expanded;
        void Caption(bool open)=>_toggle.Text=title+(open?" ▾":" ▸");Caption(expanded);_toggle.Toggled+=open=>{Content.Visible=open;Caption(open);};
    }
    public void Open(){_toggle.ButtonPressed=true;}
}

public partial class UiMetric : VBoxContainer
{
    public Label Value{get;}=GameUi.Text("",24);
    public UiMetric(string title,string hint="")
    {AddChild(GameUi.Hint(title));AddChild(Value);if(hint.Length>0)AddChild(GameUi.Hint(hint));}
}

public partial class UiRecipe : HFlowContainer
{
    private string _signature="";
    public void ShowRecipe(RecipeDefinition recipe,string language)
    {
        string key=language+":"+recipe.Id;if(key==_signature)return;_signature=key;
        foreach(Node child in GetChildren()){RemoveChild(child);child.QueueFree();}
        foreach(var i in recipe.Ingredients){AddChild(TabletDesign.Art(i.ItemId,new(30,30)));AddChild(new Label{Text=PlayerText.Item(language,i.ItemId)+" ×"+i.Quantity,SizeFlagsVertical=SizeFlags.ShrinkCenter});}
        AddChild(new Label{Text="→",SizeFlagsVertical=SizeFlags.ShrinkCenter});AddChild(TabletDesign.Art(recipe.OutputItem,new(30,30)));AddChild(new Label{Text=PlayerText.Item(language,recipe.OutputItem)+" ×"+recipe.OutputQuantity,SizeFlagsVertical=SizeFlags.ShrinkCenter});
    }
}

public partial class UiResourceList : VBoxContainer
{
    private readonly Dictionary<string,(Control Root,Label Count,Label Reserved)> _rows=[];
    public void ShowStock(InventoryView inventory,string language,IEnumerable<string>? include=null)
    {
        var ids=(include??inventory.Stock.Where(s=>s.Quantity>0).Select(s=>s.ItemId)).Distinct(StringComparer.Ordinal).ToArray();
        foreach(string key in _rows.Keys.Except(ids).ToArray()){var row=_rows[key];RemoveChild(row.Root);row.Root.QueueFree();_rows.Remove(key);}
        foreach(string item in ids)
        {
            if(!_rows.TryGetValue(item,out var row))
            {
                var line=new HBoxContainer();line.AddChild(TabletDesign.Art(item,new(28,28)));line.AddChild(GameUi.Text(PlayerText.Item(language,item)));
                var count=new Label{SizeFlagsVertical=SizeFlags.ShrinkCenter};line.AddChild(count);var held=GameUi.Hint("");line.AddChild(held);AddChild(line);row=(line,count,held);_rows.Add(item,row);
            }
            var stock=inventory.Stock.FirstOrDefault(s=>s.ItemId==item);row.Count.Text=((stock?.Quantity??0)-(stock?.Reserved??0)).ToString();
            row.Reserved.Text=(stock?.Reserved??0)>0?GameUi.L(language,"резерв ","held ")+stock!.Reserved:"";row.Reserved.Visible=row.Reserved.Text.Length>0;
        }
    }
}
