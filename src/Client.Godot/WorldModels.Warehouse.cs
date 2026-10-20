using Godot;
using Simulation.Core;
using Simulation.Hosting;

public static partial class WorldModels
{
    public static Node3D Warehouse(PresentationMarker marker)
    {
        var root=new Node3D();var walls=marker.Walls.ToHashSet();
        foreach(var cell in marker.Footprint)
        {
            Box(root,new(.98f,.06f,.98f),new("a1a493"),new(cell.X,.03f,cell.Z));
            if(!walls.Contains(cell))continue;
            foreach(var d in new[]{new Cell(-1,0),new Cell(1,0),new Cell(0,-1),new Cell(0,1)})
            {
                if(walls.Contains(new(cell.X+d.X,cell.Z+d.Z)))continue;
                var edge=new Vector3(cell.X+d.X*.46f,.28f,cell.Z+d.Z*.46f);
                Box(root,d.X==0?new(.94f,.045f,.045f):new(.045f,.045f,.94f),new("8a735b"),edge);
                Box(root,d.X==0?new(.94f,.045f,.045f):new(.045f,.045f,.94f),new("8a735b"),edge+new Vector3(0,-.16f,0));
            }
            foreach(float x in new[]{-.46f,.46f})foreach(float z in new[]{-.46f,.46f})Box(root,new(.045f,.45f,.045f),new("715e48"),new(cell.X+x,.25f,cell.Z+z));
        }
        // Loading bay at the actual walkable anchor. No fence/collider crosses legal cell transitions.
        foreach(float z in new[]{-.36f,.36f})Box(root,new(.055f,.95f,.055f),new("819b8e"),new(-.2f,.5f,z));
        Box(root,new(.035f,.07f,.78f),new("78958a"),new(-.2f,1.01f,0));
        if(marker.Warehouse is {Known:false})
            root.AddChild(new Label3D{Text="?",Position=new(0,.7f,0),FontSize=22,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled});
        return root;
    }
    public static Node3D WarehouseStock(PresentationMarker marker,StockPile[] piles)
    {
        var root=new Node3D();var cells=marker.Footprint.Where(c=>c!=new Cell(0,0)).ToArray();if(cells.Length==0)cells=[new(0,0)];
        foreach(var pile in piles)
        {
            var cell=cells[pile.Slot%cells.Length];int slot=pile.Slot/cells.Length;
            var at=new Vector3(cell.X+(slot%4-1.5f)*.2f,.12f,cell.Z+(slot/4-2.5f)*.14f);
            if(cells.Length==1 && cell==new Cell(0,0))
            {float across=(slot%8-3.5f)*.1f;at=(slot/8) switch {0=>new Vector3(-.36f,.12f,across),1=>new Vector3(across,.12f,-.36f),_=>new Vector3(.36f,.12f,across)};}
            for(int layer=0;layer<pile.Layers;layer++)
            {
                Color color=pile.ItemId switch {"wood"=>new("9e784e"),"plank"=>new("cb9d61"),"grain"=>new("d9bd62"),"bread"=>new("b97c45"),"milk"=>new("e8f0eb"),"beet"=>new("9c486d"),"cheese"=>new("e7c34c"),"sugar"=>new("ededde"),"pastry"=>new("d88d9e"),_=>new("8a9b96")};
                var point=at+new Vector3(0,layer*.075f,0);
                if(pile.ItemId=="wood"){var log=Cylinder(root,.055f,.17f,color,point);log.RotationDegrees=new(0,0,90);}
                else if(pile.ItemId is "grain" or "bread" or "beet")Part(root,new SphereMesh{Radius=.075f,Height=.1f,RadialSegments=6,Rings=3},color,point);
                else if(pile.ItemId=="milk")Cylinder(root,.04f,.1f,color,point);
                else Box(root,new(.17f,.055f,.1f),color,point);
            }
        }
        return root;
    }
}
