using Godot;

/// <summary>One cell is one world unit. Cosmetic bounds only; Core owns navigation and land.</summary>
public static class WorldProportions
{
    public const float PersonHeight=.48f, PersonWidth=.30f, CaptionHeight=.66f;
    public static Aabb Bounds(Node3D node)
    {
        Aabb? result=null;
        void Visit(Node3D n,Transform3D transform)
        {
            if(n is MeshInstance3D mesh)
            {var box=transform*mesh.GetAabb();result=result is {} prior?prior.Merge(box):box;}
            foreach(var child in n.GetChildren().OfType<Node3D>())Visit(child,transform*child.Transform);
        }
        Visit(node,Transform3D.Identity);return result??new Aabb();
    }
    public static Node3D Fit(Node3D geometry,Vector3 maximum)
    {
        var box=Bounds(geometry);var size=box.Size;
        float scale=Mathf.Min(maximum.X/Mathf.Max(size.X,.001f),Mathf.Min(maximum.Y/Mathf.Max(size.Y,.001f),maximum.Z/Mathf.Max(size.Z,.001f)));
        geometry.Scale*=scale;
        geometry.Position=new(-(box.Position.X+size.X/2)*scale,-box.Position.Y*scale,-(box.Position.Z+size.Z/2)*scale);
        var root=new Node3D();root.AddChild(geometry);return root;
    }
}
