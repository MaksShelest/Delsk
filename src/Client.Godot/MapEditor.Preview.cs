using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class MapEditor
{
    public void SetPreview(bool on)
    {
        if(_preview==on)return;
        _preview=on;_chrome.Visible=!on;_previewOverlay.Visible=on;
        if(on){RebuildWorld();FrameCamera();}
        _camera.Current=on;QueueRedraw();
    }
    public void ResetPreviewView()=>FrameCamera();
    public void TopPreviewView(){_yaw=0;_pitch=Mathf.DegToRad(80);_distanceGlide=false;ApplyCamera();}
    public void OrbitPreview(float yawDegrees,float pitchDegrees){_yaw+=Mathf.DegToRad(yawDegrees);_pitch+=Mathf.DegToRad(pitchDegrees);_distanceGlide=false;ApplyCamera();}
    private void FrameCamera()
    {
        _focus=new(Draft.Width/2f-.5f,0,Draft.Height/2f-.5f);_yaw=0;_pitch=Mathf.DegToRad(50);_distanceGlide=false;
        _camera.Current=true;
        if(GetViewport().GetVisibleRect().Size.X<32){_distance=_distanceTarget=Mathf.Max(Draft.Width,Draft.Height)*1.2f;ApplyCamera();return;}
        float lo=3,hi=Mathf.Max(Draft.Width,Draft.Height)*4f;
        for(int step=0;step<16;step++){float mid=(lo+hi)*.5f;_distance=mid;ApplyCamera();if(CornersVisible())hi=mid;else lo=mid;}
        _distance=_distanceTarget=hi;ApplyCamera();
    }
    private bool CornersVisible()
    {
        var rect=GetViewport().GetVisibleRect();float margin=18;
        foreach(var corner in new[]{new Vector3(-.5f,0,-.5f),new(Draft.Width-.5f,0,-.5f),new(-.5f,0,Draft.Height-.5f),new(Draft.Width-.5f,0,Draft.Height-.5f)})
        {
            if(_camera.IsPositionBehind(corner))return false;
            var point=_camera.UnprojectPosition(corner);
            if(point.X<margin||point.Y<margin||point.X>rect.Size.X-margin||point.Y>rect.Size.Y-margin)return false;
        }
        return true;
    }
    private void ApplyCamera()
    {
        _pitch=Mathf.Clamp(_pitch,Mathf.DegToRad(18),Mathf.DegToRad(80));
        _focus=new(Mathf.Clamp(_focus.X,-.5f,Draft.Width-.5f),0,Mathf.Clamp(_focus.Z,-.5f,Draft.Height-.5f));
        _distance=Mathf.Clamp(_distance,3,Mathf.Max(Draft.Width,Draft.Height)*4f);_distanceTarget=Mathf.Clamp(_distanceTarget,3,Mathf.Max(Draft.Width,Draft.Height)*4f);
        var offset=new Vector3(Mathf.Sin(_yaw)*Mathf.Cos(_pitch),Mathf.Sin(_pitch),Mathf.Cos(_yaw)*Mathf.Cos(_pitch))*_distance;
        _camera.Fov=75;_camera.Position=_focus+offset;_camera.LookAt(_focus,Vector3.Up);
    }
    private void PreviewInput(InputEvent e)
    {
        if(e is InputEventMouseButton wheel&&wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {_distanceTarget=Mathf.Clamp(_distanceTarget*(wheel.ButtonIndex==MouseButton.WheelUp?0.92f:1.08f),3,Mathf.Max(Draft.Width,Draft.Height)*4f);_distanceGlide=true;GetViewport().SetInputAsHandled();return;}
        if(e is InputEventMouseButton button&&button.ButtonIndex==MouseButton.Right){_orbiting=button.Pressed;GetViewport().SetInputAsHandled();return;}
        if(e is InputEventMouseButton middle&&middle.ButtonIndex==MouseButton.Middle){_sliding=middle.Pressed;GetViewport().SetInputAsHandled();return;}
        if(e is InputEventMouseMotion motion)
        {
            if(_orbiting){_yaw-=motion.Relative.X*.01f;_pitch+=motion.Relative.Y*.008f;ApplyCamera();}
            else if(_sliding)Pan(motion.Relative);
        }
    }
    private void Pan(Vector2 relative)
    {
        var right=new Vector3(Mathf.Cos(_yaw),0,-Mathf.Sin(_yaw));
        var forward=new Vector3(-Mathf.Sin(_yaw),0,-Mathf.Cos(_yaw));
        _focus+=(-right*relative.X+forward*relative.Y)*_distance*.0016f;ApplyCamera();
    }
    private void RebuildWorld()
    {
        WorldArt.MapKey=Draft.Id;
        foreach(Node child in _world.GetChildren())if(child!=_camera)child.Free();
        var ground=new Node3D{Name="Ground"};_world.AddChild(ground);
        WorldModels.Part(ground,new BoxMesh{Size=new(Draft.Width+2,.2f,Draft.Height+2)},new Color("7ea36d"),new(Draft.Width/2f-.5f,-.12f,Draft.Height/2f-.5f));
        var ink=new Color("5e6b58");
        for(int x=0;x<=Draft.Width;x++)WorldModels.Part(ground,new BoxMesh{Size=new(.025f,.02f,Draft.Height)},ink,new(x-.5f,.02f,Draft.Height/2f-.5f));
        for(int z=0;z<=Draft.Height;z++)WorldModels.Part(ground,new BoxMesh{Size=new(Draft.Width,.02f,.025f)},ink,new(Draft.Width/2f-.5f,.02f,z-.5f));
        foreach(var row in Draft.Roads.GroupBy(c=>c.Z))
        {var cells=row.OrderBy(c=>c.X).ToArray();int begin=0;for(int i=1;i<=cells.Length;i++)if(i==cells.Length||cells[i].X!=cells[i-1].X+1)
        {int length=cells[i-1].X-cells[begin].X+1;WorldModels.Part(ground,new BoxMesh{Size=new(length,.03f,.94f)},new Color("a39578"),new(cells[begin].X+(length-1)/2f,.03f,row.Key));begin=i;}}
        foreach(var lot in Draft.Lots)foreach(var cell in lot.Cells)
        {
            WorldModels.Part(ground,new BoxMesh{Size=new(.98f,.012f,.98f)},new Color("5c5344"),new(cell.X,.008f,cell.Z));
            WorldModels.Part(ground,new BoxMesh{Size=new(.9f,.02f,.9f)},new Color("a8a386"),new(cell.X,.02f,cell.Z));
        }
        foreach(var district in Draft.Districts)foreach(var cell in district.Cells)
        {
            var node=WorldSkins.Resource(new PresentationMarker("resource",cell.X,cell.Z,district.Item,0,0){ResourceStock=100,ResourceCapacity=100});
            node.Position=new(cell.X,-.02f,cell.Z);_world.AddChild(node);
        }
        foreach(var civic in Draft.Civics)_world.AddChild(WorldSkins.Civic(new(civic.Id,civic.Kind,civic.Footprint.ToArray(),civic.Building.ToArray(),civic.Kind=="dealership"?civic.Parking.ToArray():null,civic.Building.ToArray(),civic.Entry,civic.Kind=="dealership"?Draft.EquipmentParking.ToArray():null)));
        foreach(var stall in Draft.Stalls)WorldModels.Part(ground,new BoxMesh{Size=new(.72f,.04f,.72f)},new Color("b5a3b1"),new(stall.Cell.X,.04f,stall.Cell.Z));
        bool cozy=WorldSkins.Preferences.ForModel("truck")==AppearancePreferences.Cozy;
        var dealer=Draft.Civics.FirstOrDefault(c=>c.Kind=="dealership");
        if(dealer is{Parking.Count:>=4}&&Draft.CompactExhibition)
        {
            Place("sputnik20",dealer.Parking[0],-.15f);Place("muravey70",dealer.Parking[0],.15f);Place("eger70",dealer.Parking[1],0);Place("trakt_d1",dealer.Parking[3],0);
            if(Draft.EquipmentParking.Count>=2){Machine("wood",Draft.EquipmentParking[0]);Machine("grain",Draft.EquipmentParking[1]);}
        }
        void Place(string model,Cell cell,float side){var node=WorldModels.Vehicle(model,cozy);node.Position=new(cell.X+side,0,cell.Z);_world.AddChild(node);}
        void Machine(string item,Cell cell){var node=WorldSkins.Equipment(item,1);node.Position=new(cell.X,0,cell.Z);_world.AddChild(node);}
    }
}
