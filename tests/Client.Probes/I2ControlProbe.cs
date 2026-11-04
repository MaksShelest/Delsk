using Agents.Algorithmic;
using Godot;
using Simulation.Core;
using Simulation.Hosting;
using System.Reflection;
using System.Text.Json;

// Isolated native probe. All economic actions use ordinary authenticated commands.
public partial class I2ControlProbe : Control
{
    private static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private Playfield _app=null!;private GameSession _session=null!;private long _vehicle;private string _output="";
    private readonly List<string> _checks=[];
    private static T Field<T>(object o,string key)=>(T)o.GetType().GetField(key,Flags)!.GetValue(o)!;
    private static void Call(object o,string method,params object[] args)=>o.GetType().GetMethod(method,Flags)!.Invoke(o,args);
    private static void Set(object o,string field,object value)=>o.GetType().GetField(field,Flags)!.SetValue(o,value);
    private void Progress(string name)=>System.IO.File.AppendAllText(_output+"/progress.jsonl",JsonSerializer.Serialize(new{phase=name,utc=DateTime.UtcNow})+"\n");
    private void Check(bool value,string name)
    {if(!value)throw new InvalidOperationException(name);_checks.Add(name);System.IO.File.AppendAllText(_output+"/progress.jsonl",JsonSerializer.Serialize(new{name,tick=_session?.Engine.Tick})+"\n");}
    private async Task Frames(int count=2){for(int n=0;n<count;n++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private Cell ActorCell=>_session.Engine.Observe(new(1)).Participant.Position;
    private TrafficVehicle Traffic=>_session.Engine.InspectRoads()!.Traffic.Single(t=>t.VehicleId==_vehicle);
    private void Step(GameAction action)
    {var id=_session.Send(new(1),action);_session.Step();Check(_session.Engine.Result(id)!.Status==ResultStatus.Accepted,"ordinary "+action.Kind);Call(_app,"Refresh");}
    private void Move(Cell target)
    {
        Step(new(ActionKind.MoveTo,Target:target));int bound=300;
        while(_session.Engine.Observe(new(1)).Participant.Busy&&bound-->0)_session.Step();
        Check(bound>0&&ActorCell==target,"route arrived "+target);Call(_app,"Refresh");
    }
    private static Key KeyFor(Cell direction)=>direction.X>0?Key.D:direction.X<0?Key.A:direction.Z>0?Key.S:Key.W;
    private async Task KeyEvent(Key key,bool down)
    {
        Input.ParseInputEvent(new InputEventKey{WindowId=GetWindow().GetWindowId(),Keycode=key,PhysicalKeycode=key,Pressed=down});await Frames(1);
        _app._Process(0);_app._Process(0);
    }
    private async Task Quanta(int count,GameSession? copy=null)
    {
        for(int n=0;n<count;n++)
        {
            _session.AdvanceTravelQuantum();copy?.AdvanceTravelQuantum();
            if(copy is not null)Check(copy.Engine.ComputeStateHash()==_session.Engine.ComputeStateHash(),"mid-turn exact continuation");
        }
        // Normalize every Core quantum; render once per sampled input interval.
        // Scene() independently waits for interpolation and compares physical coordinates.
        Call(_app,"Refresh");await Frames(1);
    }
    private async Task Scene(string name)
    {
        Call(_app,"Refresh");var stage=Field<PresentationStage>(_app,"_stage");
        var frame=Presentation.Read(_session.Engine.Configuration(),_session.Engine,1);var v=frame.Vehicles.Single(v=>v.Id==_vehicle);
        var expected=new Vector3(v.X+(v.OffsetX-500)/1000f,.07f,v.Z+(v.OffsetZ-500)/1000f);
        var node=stage.GetNode<Node3D>("Vehicle_"+_vehicle);
        float angle=Mathf.Atan2(-v.FacingX,-v.FacingZ);
        bool Aligned()=>node.Position.DistanceTo(expected)<.003f&&Math.Abs(Mathf.AngleDifference(node.Rotation.Y,angle))<.003f;
        for(int n=0;n<20&&!Aligned();n++)await Frames(1);
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        Check(Aligned(),"Core/scene position and bearing "+name);
        GetViewport().GetTexture().GetImage().SavePng(_output+"/"+name+".png");
    }
    private void Finish(JsonElement plan,string map,string phase)
    {
        System.IO.File.WriteAllText(_output+"/native.json",JsonSerializer.Serialize(new{status="succeeded",map,phase,npcCount=plan.GetProperty("count").GetInt32(),tick=_session.Engine.Tick,stateHash=_session.Engine.ComputeStateHash(),checks=_checks,preparedFunding=true,scope="Linux Mono, software GL, synthetic native keyboard/touch. Ordinary purchase/board commands; prepared funds, no earning or physical device claim."}));GetTree().Quit();
    }
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();string Arg(string key)=>args[Array.IndexOf(args,key)+1];
            _output=Arg("--output");System.IO.Directory.CreateDirectory(_output);Progress("bootstrap");string fixture=Arg("--fixture");string phase=args.Contains("--phase")?Arg("--phase"):"road";
            var plan=JsonDocument.Parse(System.IO.File.ReadAllText(fixture+"-plan.json")).RootElement;
            Cell Read(string key){var p=plan.GetProperty(key);return new(p.GetProperty("x").GetInt32(),p.GetProperty("z").GetInt32());}
            var at=Read("cell");var direction=Read("direction");string map=plan.GetProperty("map").GetString()!;
            _app=GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();AddChild(_app);await Frames();_app.SetProcess(false);Progress("client-ready");
            Call(_app,"DismissMaps");Set(_app,"_mapKey",PlayMaps.KeyForScenario(map));
            string save=typeof(Playfield).GetProperty("SavePath",Flags)!.GetValue(_app)!.ToString()!;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(save)!);System.IO.File.Copy(fixture+".save.json",save,true);Progress("save-copied");
            Call(_app,"Load");Progress("session-loaded");await Frames();Call(_app,"ContinueGame");await Frames();_app.SetProcess(false);
            _app.Notification((int)NotificationApplicationFocusIn);_app._Process(0);_app._Process(0);
            _session=Field<GameSession>(_app,"_session");_session.SetPaused(false);Progress("loaded");
            Check(_session.Engine.UsesFreeVehicleControl,"I2 saved profile");Check(_session.Engine.Configuration().Participants.Length==plan.GetProperty("count").GetInt32()+1,"NPC roster");
            var layout=_session.Engine.Configuration().Layout!;var road=layout.InitialCityDirtCells.ToHashSet();var corridor=layout.RoadCorridorCells.ToHashSet();
            Cell[] axes=[new(1,0),new(0,1),new(-1,0),new(0,-1)];
            Cell Neighbour(Cell c,Cell v,int n=1)=>new(c.X+v.X*n,c.Z+v.Z*n);
            int Distance(Cell a,Cell b)=>Math.Abs(a.X-b.X)+Math.Abs(a.Z-b.Z);
            if(phase=="road")
            {
            var stock=_session.Engine.FleetOf(new(1))!.Stock.First(v=>v.ModelId=="sputnik20");_vehicle=stock.Id;
            Step(new(ActionKind.BuyVehicle,VehicleId:_vehicle));for(int n=0;n<5;n++)_session.Step();
            Move(_session.Engine.FleetOf(new(1))!.Vehicles.Single().Position);Step(new(ActionKind.BoardVehicle,VehicleId:_vehicle));
            Move(new(at.X-3*direction.X,at.Z-3*direction.Z));Move(at);
            _app._Process(0);_app._Process(0);
            var forward=KeyFor(direction);var reverse=KeyFor(new(-direction.X,-direction.Z));
            await KeyEvent(forward,true);Check(_session.Engine.TravelOf(new(1)).HeadingX==direction.X&&_session.Engine.TravelOf(new(1)).HeadingZ==direction.Z,"physical WASD sampled");
            await Quanta(10);await KeyEvent(forward,false);await Quanta(10);await Scene("right-lane");
            Cell turnAt=ActorCell;await KeyEvent(reverse,true);await Quanta(1);
            var copy=CheckpointStore.Decode(CheckpointStore.Encode(_session));copy.RestoreControllers(c=>AlgorithmicController.Restore(c));
            int remaining=Math.Max(0,Traffic.Steering!.TurnQuanta-Traffic.Steering.TurnQuantum);await Quanta(remaining,copy);
            Check(ActorCell==turnAt,"local U-turn");await Scene("uturn");await Quanta(10);await KeyEvent(reverse,false);await Quanta(15);
            var junction=road.Where(c=>axes.Count(v=>road.Contains(Neighbour(c,v)))>=3).OrderBy(c=>Distance(c,ActorCell)).First();
            Move(junction);var entry=_session.Engine.InspectFleet()!.Vehicles.Single();
            var branch=axes.First(v=>road.Contains(Neighbour(junction,v))&&(v.X!=-entry.FacingX||v.Z!=-entry.FacingZ));
            var crossKey=KeyFor(branch);await KeyEvent(crossKey,true);await Quanta(9);await KeyEvent(crossKey,false);await Quanta(12);
            Check(ActorCell!=junction&&road.Contains(ActorCell),"native intersection crossing");await Scene("intersection");
                if(phase=="road")
                {
                    string next=_output+"/after-road";CheckpointStore.Save(_session,next+".save.json");
                    string stateHash=_session.Engine.ComputeStateHash();
                    Check(CheckpointStore.Load(next+".save.json").Session!.Engine.ComputeStateHash()==stateHash,"road boundary save/readback");
                    System.IO.File.WriteAllText(next+"-plan.json",JsonSerializer.Serialize(new{cell=at,direction,map,count=plan.GetProperty("count").GetInt32(),vehicleId=_vehicle,stateHash},SimulationJson.Options));
                    Finish(plan,map,phase);return;
                }
            }
            else
            {
                _vehicle=plan.GetProperty("vehicleId").GetInt64();
                Check(_session.Engine.ComputeStateHash()==plan.GetProperty("stateHash").GetString(),"road/exit exact saved boundary");
                Check(_session.Engine.InspectFleet()!.Vehicles.Single(v=>v.Id==_vehicle).Driver==1,"loaded physical driver");
            }
            var actors=_session.Engine.Inspect().Participants.Where(p=>p.Id.Value!=1).Select(p=>p.Position).ToArray();
            bool Surface(Cell c)=>corridor.Contains(c)&&!road.Contains(c)&&!_session.Engine.Configuration().BlockedCells.Contains(c)&&
                !_session.Engine.ResourceSites().Any(r=>r.Position==c&&!r.Public&&r.Owner?.Value!=1&&r.Tenant?.Value!=1)&&actors.All(a=>Distance(a,c)>2);
            if(phase=="focus")
            {
                var focusCell=road.Where(c=>axes.Count(v=>road.Contains(Neighbour(c,v)))==2&&
                    axes.Any(v=>road.Contains(Neighbour(c,v))&&road.Contains(Neighbour(c,v,-1))&&
                        new[]{new Cell(-v.Z,v.X),new Cell(v.Z,-v.X)}.Any(side=>Surface(Neighbour(c,side)))))
                    .OrderBy(c=>Distance(c,ActorCell)).First();
                Move(focusCell);var body=_session.Engine.InspectFleet()!.Vehicles.Single(v=>v.Id==_vehicle);
                var side=new[]{new Cell(-body.FacingZ,body.FacingX),new Cell(body.FacingZ,-body.FacingX)}.First(v=>Surface(Neighbour(focusCell,v)));
                var focusKey=KeyFor(side);
            await KeyEvent(focusKey,true);await Quanta(3);await KeyEvent(focusKey,false);Check(Traffic.ExitQuanta==0,"short exit cancelled");await Quanta(15);
            await KeyEvent(focusKey,true);await Quanta(3);_app.Notification((int)NotificationApplicationFocusOut);
            long frozen=_session.Engine.Tick;await Quanta(20);Check(_session.Engine.Tick==frozen&&Traffic.ExitQuanta==0,"focus cancels exit and freezes time");
            await KeyEvent(focusKey,false);_app.Notification((int)NotificationApplicationFocusIn);_app._Process(0);_app._Process(0);
                string next=_output+"/after-focus";System.IO.File.Copy(save,next+".save.json",true);
                System.IO.File.WriteAllText(next+"-plan.json",JsonSerializer.Serialize(new{cell=at,direction,map,count=plan.GetProperty("count").GetInt32(),vehicleId=_vehicle,stateHash=_session.Engine.ComputeStateHash()},SimulationJson.Options));
                Finish(plan,map,phase);return;
            }
            var ends=from c in road from outward in axes
                where axes.Count(v=>road.Contains(Neighbour(c,v)))==1&&!road.Contains(Neighbour(c,outward))&&road.Contains(Neighbour(c,outward,-1))&&road.Contains(Neighbour(c,outward,-2))
                from side in new[]{new Cell(-outward.Z,outward.X),new Cell(outward.Z,-outward.X)} where Surface(Neighbour(c,side))
                orderby Distance(c,ActorCell),c.Z,c.X select new{cell=c,outward,side};
            var end=ends.First();Move(Neighbour(end.cell,end.outward,-2));Move(end.cell);
            var exit=end.side;var exitKey=KeyFor(exit);
            await KeyEvent(exitKey,true);int guard=45;
            while(road.Contains(ActorCell)&&guard-->0)_session.AdvanceTravelQuantum();
            Call(_app,"Refresh");await Frames(1);Check(guard>0,"held exit to passable surface: "+Traffic.Reason);
            await KeyEvent(exitKey,false);await Quanta(15);await Scene("offroad");
            var returnKey=KeyFor(new(-exit.X,-exit.Z));await KeyEvent(returnKey,true);guard=55;
            while(!road.Contains(ActorCell)&&guard-->0)_session.AdvanceTravelQuantum();
            Call(_app,"Refresh");await Frames(1);Check(guard>0,"return to road: "+Traffic.Reason);
            await KeyEvent(returnKey,false);await Quanta(15);await Scene("returned");
            var stick=Field<VirtualJoystick>(_app,"_stick");var centre=stick.GetGlobalRect().GetCenter();
            Input.ParseInputEvent(new InputEventScreenTouch{WindowId=GetWindow().GetWindowId(),Index=0,Position=centre+new Vector2(50,0),Pressed=true});await Frames(2);_app._Process(0);
            Check(_session.Engine.TravelOf(new(1)).HeadingX==1,"native touch joystick sampled");
            Input.ParseInputEvent(new InputEventScreenTouch{WindowId=GetWindow().GetWindowId(),Index=0,Position=centre+new Vector2(220,0),Pressed=false});await Frames(2);_app._Process(0);
            Check(_session.Engine.TravelOf(new(1)).HeadingX==0&&_session.Engine.TravelOf(new(1)).HeadingZ==0,"touch release outside control stops");
            await Quanta(15);
            IEnumerable<Node> Descendants(Node n)=>n.GetChildren().SelectMany(child=>new[]{child}.Concat(Descendants(child)));
            Check(Descendants(_app).OfType<Button>().Where(b=>b.IsVisibleInTree()).All(b=>!b.GetGlobalRect().Intersects(stick.GetGlobalRect())),"joystick leaves action buttons accessible");
            foreach(string language in new[]{"ru","en"})
            {
                Set(_app,"_language",language);Call(_app,"OpenHelp","basics");await Frames(2);
                Check(!stick.IsVisibleInTree(),"joystick hidden in help "+language);
                var help=Field<HelpOverlay>(_app,"_helpOverlay");var text=(string)help.GetType().GetMethod("MovementHelp",Flags)!.Invoke(help,[])!;
                Check(text.Contains(language=="ru"?"0,5 с":"0.5 s"),"I2 help "+language);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(_output+"/help-"+language+".png");Call(_app,"CloseHelp");
            }
            Step(new(ActionKind.LeaveVehicle));Check(_session.Engine.InspectFleet()!.Vehicles.Single().Driver==0,"stop and leave");
            CheckpointStore.Save(_session,_output+"/final.save.json");Check(System.IO.File.Exists(_output+"/final.save.json"),"native save; exact readback in separate bounded runner");
            Finish(plan,map,phase);
        }
        catch(Exception error)
        {if(_output.Length>0)System.IO.File.WriteAllText(_output+"/failure.json",JsonSerializer.Serialize(new{error=error.ToString(),checks=_checks}));GD.PushError(error.ToString());GetTree().Quit(1);}
    }
}
