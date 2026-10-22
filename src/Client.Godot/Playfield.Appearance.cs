using Godot;
using Simulation.Hosting;
using System.Text.Json;

public partial class Playfield
{
    private AppearancePreferences _appearance=new();
    private AcceptDialog? _appearanceDialog;
    private const string AppearancePath="user://appearance.json";
    private void InitializeAppearance()
    {
        try
        {
            if(Godot.FileAccess.FileExists(AppearancePath))
                _appearance=(JsonSerializer.Deserialize<AppearancePreferences>(Godot.FileAccess.GetFileAsString(AppearancePath))??new()).Normalize();
        }
        catch(Exception error) when(error is JsonException or ArgumentException or InvalidOperationException)
        {GD.PushWarning("Appearance settings reset: "+error.Message);_appearance=new();}
        WorldSkins.Configure(_appearance);
    }
    private void SaveAppearance()
    {
        _appearance=_appearance.Normalize();WorldSkins.Configure(_appearance);
        try
        {
            string path=ProjectSettings.GlobalizePath(AppearancePath),temp=path+".tmp";
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(temp,JsonSerializer.Serialize(_appearance));System.IO.File.Move(temp,path,true);
        }
        catch(Exception error) when(error is System.IO.IOException or UnauthorizedAccessException)
        {_feedback=L("Не удалось сохранить оформление.","Could not save appearance.");GD.PushWarning(error.Message);}
        if(_session is not null)Refresh();
    }
    private void OpenAppearanceSettings()
    {
        if(_appearanceDialog is not null){_appearanceDialog.PopupCentered();return;}
        var dialog=new AcceptDialog {Name="AppearanceSettings",Title=L("Оформление и персонажи","Appearance and characters"),Size=new(820,690),MinSize=new(640,460),Theme=(Theme)TabletDesign.Theme.Duplicate(true)};
        var arrow=GD.Load<Texture2D>("res://tablets_v1/assets/icons/down.svg").GetImage();arrow.Resize(18,18,Image.Interpolation.Lanczos);
        dialog.Theme.SetIcon("arrow","OptionButton",ImageTexture.CreateFromImage(arrow));
        dialog.AddThemeStyleboxOverride("panel",TabletDesign.Card());
        dialog.AddThemeStyleboxOverride("embedded_border",TabletDesign.Box("f7f3e8",18,16));
        dialog.AddThemeColorOverride("title_color",Colors.White);
        _appearanceDialog=dialog;
        var scroll=new ScrollContainer {HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};dialog.AddChild(scroll);
        var column=new VBoxContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill};scroll.AddChild(column);
        column.AddChild(TabletDesign.Heading(L("Выберите оформление","Choose your look"),26));
        column.AddChild(new Label {Text=L("Основной — прежние модели. Дополнительный — новые уютные образы.","Primary keeps the original models. Additional uses the new cozy artwork."),AutowrapMode=TextServer.AutowrapMode.WordSmart});
        var worldSet=new Button {Name="WorldSkinSet",FocusMode=FocusModeEnum.None};column.AddChild(worldSet);
        var peopleSet=new Button {Name="PeopleSkinSet",FocusMode=FocusModeEnum.None};column.AddChild(peopleSet);
        var row=new HBoxContainer();column.AddChild(row);
        var type=new OptionButton {Name="ModelType",SizeFlagsHorizontal=SizeFlags.ExpandFill};row.AddChild(type);
        foreach(string key in AppearancePreferences.ModelKeys)type.AddItem(ModelTitle(key));
        var changeModel=new Button {Name="ChangeBuildingSkin",FocusMode=FocusModeEnum.None};row.AddChild(changeModel);
        var who=new OptionButton {Name="AppearanceActor"};column.AddChild(who);
        long human=_session is null?1:_human.Value;
        var ids=_session is null?new[]{human}:_scenario.Participants.Select(p=>p.Id).Order().ToArray();
        foreach(long id in ids)who.AddItem(id==human?L("Ваш персонаж","Your character"):L("Участник #","Participant #")+id+" · "+WorldSkins.ActorName(id,_language));
        int humanSlot=Array.IndexOf(ids,human);who.Select(Math.Max(0,humanSlot));
        var profileRow=new HBoxContainer();column.AddChild(profileRow);
        var profile=new OptionButton {Name="CharacterSkin",SizeFlagsHorizontal=SizeFlags.ExpandFill};profileRow.AddChild(profile);
        for(int i=0;i<AppearanceCatalog.People.Length;i++)profile.AddItem(AppearanceCatalog.Name(i,_language));
        var next=new Button {Name="NextCharacterSkin",Text=L("Следующий образ ›","Next look ›"),FocusMode=FocusModeEnum.None};profileRow.AddChild(next);
        var previews=new HBoxContainer();column.AddChild(previews);var buildingPreview=new SkinPreview();var personPreview=new SkinPreview();previews.AddChild(buildingPreview);previews.AddChild(personPreview);
        column.AddChild(new Label {Text=L("Выбор применяется сразу и сохраняется для следующих запусков.","Applied immediately and kept for your next launch."),AutowrapMode=TextServer.AutowrapMode.WordSmart});
        string SetName(string set)=>set==AppearancePreferences.Cozy?L("Дополнительный","Additional"):L("Основной","Primary");
        void Update()
        {
            worldSet.Text=L("Здания, природа и техника: ","Buildings, nature and vehicles: ")+SetName(_appearance.WorldSet);
            peopleSet.Text=L("Набор персонажей: ","Character set: ")+SetName(_appearance.PeopleSet);
            string key=AppearancePreferences.ModelKeys[type.Selected];changeModel.Text=L("Сменить: ","Switch: ")+SetName(_appearance.ForModel(key));
            long id=ids[Math.Max(0,who.Selected)];profile.Select(_appearance.ForActor(id));
            for(int i=0;i<ids.Length;i++)who.SetItemText(i,ids[i]==human?L("Ваш персонаж","Your character"):L("Участник #","Participant #")+ids[i]+" · "+WorldSkins.ActorName(ids[i],_language));
            buildingPreview.ShowModel(WorldSkins.Preview(key),capital:key=="capital");personPreview.ShowModel(WorldSkins.Person(id,id==human,L("Вы","You"),_language),true);
        }
        string Toggle(string set)=>set==AppearancePreferences.Cozy?AppearancePreferences.Classic:AppearancePreferences.Cozy;
        worldSet.Pressed+=()=>{_appearance=_appearance with {WorldSet=Toggle(_appearance.WorldSet),ModelSets=[]};SaveAppearance();Update();};
        peopleSet.Pressed+=()=>{_appearance=_appearance with {PeopleSet=Toggle(_appearance.PeopleSet)};SaveAppearance();Update();};
        type.ItemSelected+=_=>Update();who.ItemSelected+=_=>Update();
        changeModel.Pressed+=()=>{string key=AppearancePreferences.ModelKeys[type.Selected];_appearance=_appearance.WithModel(key,Toggle(_appearance.ForModel(key)));SaveAppearance();Update();};
        profile.ItemSelected+=index=>{_appearance=_appearance.WithActor(ids[who.Selected],(int)index);SaveAppearance();Update();};
        next.Pressed+=()=>{long id=ids[who.Selected];_appearance=_appearance.WithActor(id,(_appearance.ForActor(id)+1)%AppearanceCatalog.People.Length);SaveAppearance();Update();};
        void Close(){_appearanceDialog=null;dialog.QueueFree();}
        dialog.Confirmed+=Close;dialog.Canceled+=Close;AddChild(dialog);dialog.PopupCentered();Update();
    }
    private string ModelTitle(string key)=>key switch
    {
        "shop"=>L("Торговая лавочка","Trading stall"),"capital"=>L("Ратуша и терминал","Town hall and terminal"),"truck"=>L("Грузовик","Delivery truck"),"equipment"=>L("Добывающая техника","Harvesting equipment"),
        _ when key.StartsWith("resource:",StringComparison.Ordinal)=>L("Ресурс: ","Resource: ")+PlayerText.Item(_language,key[9..]),
        _=>PlayerText.Building(_language,key)
    };
}
