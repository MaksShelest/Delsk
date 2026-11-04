using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class Playfield
{
    private RoadWorkCard? _roadWorkCard;
    private long _roadWorkVehicle;
    private string? _roadWorkCommand;
    private float _roadCardTop=108;
    private void OpenRoadWork(long vehicle)
    {
        if(_surface!=Surface.Play||_playChrome is null)return;
        if(_tabletState.IsOpen)CloseTablet();
        CloseRoadWork();StopHeading();_waitRelease=true;
        _roadWorkVehicle=vehicle;_roadWorkCommand=null;
        _roadWorkCard=new(_scenario,_language,vehicle,action=>
        {
            if(WaitingCommand)return;
            Submit(action);_roadWorkCommand=_trackedId;
        },(cell,mode)=>_session.Engine.QuoteRoadCell(_human,vehicle,cell,mode));
        _roadWorkCard.CloseRequested+=CloseRoadWork;
        _roadWorkCard.SelectionChanged+=cell=>_stage?.ShowRoadSelection(cell);
        _playChrome.AddChild(_roadWorkCard);
        _roadWorkCard.SetAnchorsPreset(LayoutPreset.TopRight);
        LayoutRoadWork();_roadWorkCard.Resized+=LayoutRoadWork;_roadWorkCard.MinimumSizeChanged+=LayoutRoadWork;
        ApplyPause();Refresh();
    }
    private void LayoutRoadWork()
    {
        if(_roadWorkCard is null)return;
        var rect=GetViewportRect();float width=Math.Min(460,rect.Size.X-24);
        _roadWorkCard.OffsetLeft=-width-12;_roadWorkCard.OffsetRight=-12;_roadWorkCard.OffsetTop=_roadCardTop;
        _roadWorkCard.Size=new(width,_roadWorkCard.GetCombinedMinimumSize().Y);
    }
    private void CloseRoadWork()
    {
        if(_roadWorkCard is not null){_roadWorkCard.GetParent()?.RemoveChild(_roadWorkCard);_roadWorkCard.QueueFree();_roadWorkCard=null;}
        _stage?.ShowRoadSelection(null);_roadWorkCommand=null;_waitRelease=true;ApplyPause();
    }
    private void RefreshRoadWork(PlayerView view)
    {
        if(_roadWorkCard is null)return;
        if(_surface!=Surface.Play||_tabletState.IsOpen||view.Fleet?.Vehicles.Any(v=>v.Vehicle.Id==_roadWorkVehicle)!=true){CloseRoadWork();return;}
        _roadWorkCard.Visible=_helpOverlay is null;
        string result="";
        if(_roadWorkCommand is {} id)
        {
            var response=_session.Engine.Result(id);
            result=response is null?L("Команда ожидает выполнения…","Command pending…"):
                response.Status==ResultStatus.Rejected?PlayerText.Reason(_language,response.ReasonCode):L("Команда принята.","Command accepted.");
        }
        _roadWorkCard.ShowData(view,WaitingCommand,result);
        LayoutRoadWork();
    }
}
