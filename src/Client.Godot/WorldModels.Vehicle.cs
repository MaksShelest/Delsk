using Godot;

public static partial class WorldModels
{
    public static Node3D Vehicle(string model,bool cozy=false)
    {
        var root=new Node3D();
        var color=new Color(model switch{"sputnik20"=>"6a9fbd","muravey70"=>"d0ad58","eger70"=>"779363","vector200"=>"b46c55",_=>"d2a644"});
        if(cozy)color=color.Lightened(.12f);
        bool tracked=model=="trakt_d1",large=model=="vector200";
        float width=large?.62f:.48f,length=large?1.05f:.76f;
        Box(root,new(width,.14f,length),color,new(0,.22f,0));
        Box(root,new(width*.85f,.26f,.3f),color,new(0,.39f,-length*.25f));
        Box(root,new(width*.65f,.13f,.018f),new("b6d4d6"),new(0,.43f,-length*.25f-.155f));
        if(model=="sputnik20")Box(root,new(width*.8f,.18f,.38f),color,new(0,.35f,.09f));
        else if(tracked){Box(root,new(.65f,.12f,.3f),new("726e66"),new(0,.15f,.38f));Box(root,new(.35f,.3f,.35f),color,new(0,.44f,.12f));}
        else {Box(root,new(width,.18f,length*.48f),color,new(0,.36f,length*.2f));Box(root,new(width*.78f,.02f,length*.4f),new("715f4c"),new(0,.455f,length*.2f));}
        foreach(float side in new[]{-1f,1f})
        {
            if(tracked)Box(root,new(.16f,.2f,.75f),new("444847"),new(side*.3f,.16f,0));
            else foreach(float z in new[]{-length*.31f,length*.31f}){var wheel=Cylinder(root,.12f,.085f,new("37403f"),new(side*(width/2+.025f),.14f,z));wheel.RotationDegrees=new(0,0,90);}
        }
        return WorldProportions.Fit(root,tracked?new(.76f,.55f,.86f):new(.38f,.46f,.76f));
    }
}
