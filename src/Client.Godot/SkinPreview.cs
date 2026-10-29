using Godot;

/// <summary>A tiny isolated rendering viewport; it has no session or game commands.</summary>
public partial class SkinPreview : SubViewportContainer
{
    private readonly Node3D _world=new();
    private readonly Camera3D _camera=new() { Current=true, Projection=Camera3D.ProjectionType.Orthogonal };
    private Node3D? _model;
    public SkinPreview()
    {
        Stretch=true;CustomMinimumSize=new(260,185);SizeFlagsHorizontal=SizeFlags.ExpandFill;MouseFilter=MouseFilterEnum.Ignore;
        var viewport=new SubViewport {Size=new(360,210),OwnWorld3D=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Always,Msaa3D=Viewport.Msaa.Msaa2X};AddChild(viewport);viewport.AddChild(_world);
        _world.AddChild(new WorldEnvironment {Environment=new Godot.Environment {BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new("fffdf9"),AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=new("f1e7cd"),AmbientLightEnergy=.32f}});
        _world.AddChild(new DirectionalLight3D {RotationDegrees=new(-48,-35,0),LightColor=new("fff2d4"),LightEnergy=.75f,ShadowEnabled=true});
        _world.AddChild(_camera);
    }
    public void ShowModel(Node3D model,bool person=false,bool capital=false)
    {
        if(_model is not null){_world.RemoveChild(_model);_model.QueueFree();}
        _model=model;
        if(person && model.GetMeta("skin_set").AsString()=="cozy")model.RotationDegrees=new(0,180,0);
        _world.AddChild(model);
        foreach(var label in model.GetChildren().OfType<Label3D>())label.Visible=false;
        var bounds=WorldProportions.Bounds(model);var target=bounds.GetCenter()+model.Position;
        _camera.Size=Mathf.Max(.65f,Mathf.Max(bounds.Size.Y,Mathf.Max(bounds.Size.X,bounds.Size.Z))*1.25f);
        _camera.Position=target+new Vector3(person ? .8f : 2,person ? .4f : 1.7f,3);
        if(IsInsideTree())_camera.LookAt(target,Vector3.Up);
    }
}
