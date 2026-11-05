using Godot;
using Simulation.Core;

/// <summary>Small original vector illustrations, in the palette of the approved sawmill.</summary>
public static class GameUiArt
{
    private static readonly Dictionary<string,Texture2D> Cache=[];
    private static Texture2D Svg(string key,string body,int width,int height)
    {
        if(Cache.TryGetValue(key,out var cached))return cached;
        using var image=new Image();image.LoadSvgFromString($"<svg xmlns='http://www.w3.org/2000/svg' width='{width}' height='{height}' viewBox='0 0 {width} {height}'>{body}</svg>");
        var texture=ImageTexture.CreateFromImage(image);Cache.Add(key,texture);return texture;
    }
    public static void Release(){foreach(var texture in Cache.Values)texture.Dispose();Cache.Clear();}
    public static Texture2D Item(string key)
    {
        string color=key switch{"iron_ore" or "coke" or "steel" or "steel_sheet"=>"8a9696","copper_ore" or "copper" or "wire"=>"b97951","sand" or "limestone" or "soda"=>"d6bf85","stone" or "gravel" or "cement" or "concrete"=>"a4a99e","glass" or "window"=>"92bdc5","motor" or "conveyor"=>"72978f","flour" or "curd" or "cream"=>"e4d9ba",_=>"9baf7d"};
        string shape=key switch
        {
            "motor" or "conveyor"=>"<rect x='9' y='14' width='28' height='23' rx='5' fill='COLOR'/><circle cx='23' cy='25' r='8' fill='#dce4de'/><circle cx='23' cy='25' r='4' fill='#5c7973'/><path d='M10 39h28M37 22h7v8h-7' stroke='#516c64' stroke-width='4'/>",
            "wire"=>"<path d='M10 32c-6-18 28-25 29-8s-28 22-27 6 26-22 23-5-23 18-19 4 16-14 15-3' fill='none' stroke='COLOR' stroke-width='4'/>",
            "glass" or "window"=>"<path d='M10 7h29v35H10z' fill='COLOR' stroke='#688e91' stroke-width='3'/><path d='M24 8v33M11 24h27M14 13l7-3M29 36l6-8' stroke='#edf5e7' stroke-width='3'/>",
            "steel" or "copper" or "beam" or "steel_sheet"=>"<path d='m7 28 8-14h23l5 14-8 10H6z' fill='COLOR'/><path d='m15 14 5 13h23M20 27l-7 11' fill='none' stroke='#647b76' stroke-width='3'/>",
            "flour" or "cement" or "soda"=>"<path d='M15 8h19l-2 8 8 22q-14 9-29 0l7-22z' fill='COLOR' stroke='#a99b79' stroke-width='2'/><path d='M16 16h18M20 29h12' stroke='#b7ac8c' stroke-width='3'/>",
            "furniture"=>"<path d='M10 15h30v19H10z' fill='COLOR' stroke='#8b724d' stroke-width='3'/><path d='M14 33v10M36 33v10M10 24h30' stroke='#8b724d' stroke-width='4'/>",
            _=>"<path d='m6 32 6-15 13-9 14 10 5 17-16 8z' fill='COLOR'/><path d='m12 17 14 9 13-8M26 26l2 17' stroke='#75877a' stroke-width='2' fill='none'/>"
        };
        return Svg("item:"+key,shape.Replace("COLOR","#"+color),48,48);
    }
    public static TextureRect Vehicle(VehicleModel model,Vector2 size)
    {
        bool machine=model.EquipmentTypeId is not null,paver=model.MaterialsOnly,car=model.Id=="sputnik20";
        string color=paver?"d1ab58":machine?"a8b87d":car?"8aa9b7":"749e95";
        string cab=car?"<path d='m66 56 21-25h60l24 25' fill='#9fb8b8' stroke='#648b88' stroke-width='4'/><path d='m91 36-17 20h76l-15-20z' fill='#d3e4d8'/>":"<path d='M139 31h39l22 36v43h-61z' fill='#c7c99c'/><path d='M148 39h23l15 27h-38z' fill='#bfd9d4'/>";
        string equipment=paver?"<path d='M14 108h40v15H8z' fill='#b28c4c'/><rect x='23' y='51' width='108' height='50' rx='7' fill='#cab477'/>":machine?"<path d='m52 72 20-45 28-13 6 12-24 12-14 45' fill='#d4b867' stroke='#a38f53' stroke-width='4'/><path d='m98 12 19 4 8 18-25 5z' fill='#7e9b83'/>":car?"":"<path d='M19 38h111v65H19z' fill='#a8bba4'/><path d='M24 46h101M24 68h101M24 89h101' stroke='#789b8d' stroke-width='3'/>";
        string body="<ellipse cx='111' cy='132' rx='103' ry='12' fill='#d9e2cc'/>"+cab+$"<path d='M20 66h179l12 18v31H17V80z' fill='#{color}'/>"+equipment+"<path d='M21 115h183' stroke='#638175' stroke-width='7'/><circle cx='52' cy='119' r='17' fill='#526c67'/><circle cx='52' cy='119' r='8' fill='#c9d3c3'/><circle cx='172' cy='119' r='17' fill='#526c67'/><circle cx='172' cy='119' r='8' fill='#c9d3c3'/><path d='M204 86h8v13h-8' fill='#e8d087'/>";
        return new(){Texture=Svg("vehicle:"+model.Id,body,224,150),CustomMinimumSize=size,ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter=Control.MouseFilterEnum.Ignore};
    }
}
