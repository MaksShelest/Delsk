using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;

// Scripted player actions on a genuine ordinary start; NPCs keep their ordinary
// controllers. No state injection, free assets, changed recipes, XP or timers.
string output=Path.GetFullPath(args.Length>0?args[0]:throw new ArgumentException("OUTPUT [map key]"));
string map=args.Length>1?args[1]:PlayMaps.CompactValley;
if(Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new ArgumentException("Use a new output; previous attempts must remain");
Directory.CreateDirectory(output);
var clock=Stopwatch.StartNew();
var profile=EconomyProfile.Read(File.ReadAllText("config/vnext/economy.g1-npc.json"));
var balance=VNextBalanceReader.Read(File.ReadAllBytes("config/vnext/balance.b1.json")).Balance!;
var raw=RunArtifacts.LoadScenario("scenarios/vnext/"+map+".json");
var resolution=OrdinaryNewGame.Resolve(raw,profile,new(3,Seed:42),vnextBalance:balance);
var definition=resolution.Scenario;
var config=JsonSerializer.Deserialize<AutonomousConfiguration>(File.ReadAllText("scenarios/baseline-controllers.json"),SimulationJson.Options)!.ForOrdinary(definition,1,resolution.Controllers);
bool resumed=args.Length>2;
var session=resumed?CheckpointStore.Load(args[2]).Session:new GameSession(new(definition));
if(resumed)
{
    if(JsonSerializer.Serialize(session.Engine.Configuration(),SimulationJson.Options)!=JsonSerializer.Serialize(definition,SimulationJson.Options))throw new ArgumentException("Resume configuration differs");
    session.RestoreControllers(c=>c.Kind==SelfBallotController.Kind?SelfBallotController.Restore(c):AlgorithmicController.Restore(c));
}
else session.AttachControllers(config.Npcs.Select(n=>AlgorithmicController.WithEconomy(n,profile.Npc))
    .Select(n=>(IController)new AlgorithmicController(n,AlgorithmicController.RuntimeSeed(definition.Seed,n.ActorId),definition.Participants.Select(p=>p.Id)))
    .Append(new SelfBallotController(new(1))));
var milestones=new List<object>();string stage="initial";
bool stopRequested=false;
using var stopSignal=OperatingSystem.IsWindows()?null:PosixSignalRegistration.Create(PosixSignal.SIGTERM,context=>{context.Cancel=true;Volatile.Write(ref stopRequested,true);});
try
{
    if(!resumed)Require(session.Engine.Observe(new(1)).OwnBuildings.Length==0,"Ordinary player already owns a building");
    if(!resumed)Require(Stock(Pack(),"wood")==0,"Ordinary backpack not empty");
    Milestone(resumed?"resumed-ordinary-start":"ordinary-start");
    if(!session.Engine.Observe(new(1)).OwnBuildings.Any(b=>b.Kind==BuildingKind.Production))
    {
    stage="earn-and-supply";Earn(session.Engine.Observe(new(1)).Plots.Any(p=>p.OwnerId==new ParticipantId(1))?50000:60000);
    var origin=session.Engine.Observe(new(1)).Participant.Position;
    var owned=session.Engine.Observe(new(1)).Plots.Where(p=>!p.IsShopSite&&p.OwnerId==new ParticipantId(1)).ToArray();
    var existingLot=definition.Layout!.IndustrialLots.FirstOrDefault(l=>owned.Any(p=>l.Cells.Contains(p.Position)));
    var anchors=definition.Layout.IndustrialLots.Select(l=>l.Cells.OrderBy(c=>c.Z).ThenBy(c=>c.X).First()).ToHashSet();
    var plot=(existingLot is null?null:session.Engine.Observe(new(1)).Plots.Single(p=>p.Position==existingLot.Cells.OrderBy(c=>c.Z).ThenBy(c=>c.X).First()))??session.Engine.Observe(new(1)).Plots.Where(p=>!p.IsShopSite&&p.OwnerId is null&&anchors.Contains(p.Position))
        .OrderBy(p=>Math.Abs(p.Position.X-origin.X)+Math.Abs(p.Position.Z-origin.Z)).ThenBy(p=>p.Id.Value).First();
    Move(plot.Position);if(plot.OwnerId is null)Do(new(ActionKind.BuyPlot,Plot:plot.Id));
    var ground=new InventoryId(session.Engine.StorageOf(new(1)).Ground.Single(g=>g.PlotId==plot.Id.Value).InventoryId);
    SupplyGround(ground,"wood",30);
    Move(definition.CapitalDemand!.Terminal);Do(new(ActionKind.BuyImport,ItemId:"steel",Quantity:3));
    var pickup=session.Engine.ProductionContentOf(new(1))!.Pickup!.Value;
    Do(new(ActionKind.Transfer,Source:pickup,Destination:Pack(),ItemId:"steel",Quantity:3));Idle();Put(ground,"steel",3);
    Do(new(ActionKind.Build,BuildingTypeId:"woodworking",Target:plot.Position));
    Milestone("construction-in-progress");
    var project=session.Engine.StorageOf(new(1)).Construction.Single();Advance(project.CompletesTick);
    Milestone("first-enterprise");
    }
    var factory=session.Engine.Observe(new(1)).OwnBuildings.Single(b=>b.Kind==BuildingKind.Production);
    if(!session.Engine.Observe(new(1)).OwnBuildings.Any(b=>b.Kind==BuildingKind.Warehouse))
    {
    stage="production-and-experience";
    Wood(10);Put(factory.InputId,"wood",10);
    Do(new(ActionKind.SetProductionEnabled,Enterprise:new(factory.Id.Value),Enabled:true));
    Wait(()=>session.Engine.Observe(new(1)).OwnEnterprises.Single().CompletedBatches>=5,1200);
    Do(new(ActionKind.SetProductionEnabled,Enterprise:new(factory.Id.Value),Enabled:false));
    Wait(()=>session.Engine.Observe(new(1)).OwnEnterprises.Single().BatchId==0,200);
    Require(session.Engine.DevelopmentOf(new(1))!.Experience.Single(i=>i.Industry=="woodworking").ProductionXp>0,"No actual production XP");
    Milestone("first-output-and-production-xp");
    stage="warehouse";Earn(25000);
    Move(factory.Position);Do(new(ActionKind.Transfer,Source:factory.OutputId!.Value,Destination:Pack(),ItemId:"plank",Quantity:10));Idle();
    var factoryContour=definition.Layout!.IndustrialLots.Single(l=>l.Cells.Contains(factory.Position)).Cells.ToHashSet();
    var warehousePlot=session.Engine.Observe(new(1)).Plots.Where(p=>!p.IsShopSite&&p.OwnerId is null&&!factoryContour.Contains(p.Position))
        .OrderBy(p=>Math.Abs(p.Position.X-factory.Position.X)+Math.Abs(p.Position.Z-factory.Position.Z)).ThenBy(p=>p.Id.Value).First();
    Move(warehousePlot.Position);Do(new(ActionKind.BuyPlot,Plot:warehousePlot.Id));
    var warehouseGround=new InventoryId(session.Engine.StorageOf(new(1)).Ground.Single(g=>g.PlotId==warehousePlot.Id.Value).InventoryId);
    Put(warehouseGround,"plank",10);Do(new(ActionKind.Build,BuildingTypeId:"warehouse",Target:warehousePlot.Position));
    Advance(session.Engine.StorageOf(new(1)).Construction.Single().CompletesTick);
    Milestone("paid-warehouse");
    }
    if(session.Engine.FleetOf(new(1))!.Vehicles.Length==0)
    {
    stage="transport";Earn(190000);
    var dealer=definition.Layout!.CivicObjects.Single(c=>c.Kind=="dealership");Move(dealer.Entry);
    var stock=session.Engine.InspectFleet()!.Stock.First(v=>v.ModelId=="sputnik20");
    Do(new(ActionKind.BuyVehicle,VehicleId:stock.Id));Wait(()=>session.Engine.FleetOf(new(1))!.Vehicles.Length>0,20);
    Milestone("paid-vehicle");
    }
    stage="complete";
    var replay=GameSession.Replay(definition,session.CommandTape,session.Engine.Tick);
    Require(replay.Engine.ComputeStateHash()==session.Engine.ComputeStateHash(),"Walkthrough replay mismatch");
    RunArtifacts.Export(session,definition,Path.Combine(output,"exports"),"headless","h3-scripted-player",clock.Elapsed.TotalMilliseconds);
    Write("succeeded",null);
}
catch(Exception error)
{
    try{CheckpointStore.Save(session,Path.Combine(output,"last-safe.save.json"));}catch(Exception saveError){File.WriteAllText(Path.Combine(output,"save-failure.txt"),saveError.ToString());}
    Write(error is OperationCanceledException?"interrupted":"failed",error.ToString());throw;
}

void Write(string status,string? error)=>File.WriteAllText(Path.Combine(output,"walkthrough.json"),JsonSerializer.Serialize(new
{
    status,error,stage,map,resumed,resumeSource=resumed?Path.GetFullPath(args[2]):null,tick=session.Engine.Tick,milestones,elapsedSeconds=clock.Elapsed.TotalSeconds,
    scope="Scripted human through ordinary commands with three autonomous NPCs. No Developer. Natural earning/land/import/construction/production XP/warehouse/vehicle; later delivery/road chain is a separate acceptance gate.",
    stateHash=session.Engine.ComputeStateHash(),definition.MaxTicks
},new JsonSerializerOptions(SimulationJson.Options){WriteIndented=true}));
void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
InventoryId Pack()=>session.Engine.Observe(new(1)).Participant.InventoryId;
long Stock(InventoryId inventory,string item)=>session.Engine.Observe(new(1)).OwnInventories.Single(i=>i.Id==inventory).Stock.Where(s=>s.ItemId==item).Sum(s=>s.Quantity-s.Reserved);
void Tick(){session.Step();if(session.Engine.WaitingForBallots)Require(session.PumpVoting(),"Unresolved ordinary ballot");if(Volatile.Read(ref stopRequested))throw new OperationCanceledException("Bounded host requested a stop after the committed tick; continue last-safe.save.json");}
void Wait(Func<bool> done,int bound){while(!done()&&bound-->0)Tick();Require(done(),"Walkthrough action exceeded its explicit tick budget at "+stage);}
void Idle()=>Wait(()=>!session.Engine.Observe(new(1)).Participant.Busy,1000);
void Advance(long tick){while(session.Engine.Tick<tick)Tick();}
void Do(GameAction action)
{
    var id=session.Send(new(1),action);Tick();
    Require(session.Engine.Result(id)?.Status==ResultStatus.Accepted,$"{stage}: {action.Kind}: {session.Engine.Result(id)?.ReasonCode}");
}
void Move(Cell target){Idle();if(session.Engine.Observe(new(1)).Participant.Position==target)return;Do(new(ActionKind.MoveTo,Target:target));Idle();}
void Wood(long quantity)
{
    var source=definition.ResourceNodes.Single(n=>n.ItemId=="wood"&&n.Rules!.Public).Position;
    if(Stock(Pack(),"wood")>=quantity)return;
    Move(source);long target=quantity;int budget=1200;
    while(Stock(Pack(),"wood")<target&&budget-->0)
    {
        var site=session.Engine.ResourceSites().Single(r=>r.Position==source);
        long q=Math.Min(target-Stock(Pack(),"wood"),site.Stock-site.Reserved);
        if(q==0){Tick();continue;}
        string id=session.Send(new(1),new(ActionKind.Harvest,ItemId:"wood",Quantity:q));Tick();
        var result=session.Engine.Result(id)!;
        if(result.Status==ResultStatus.Accepted)Idle();
        else Require(result.ReasonCode=="ResourceDepleted","Harvest failed: "+result.ReasonCode);
    }
    Require(Stock(Pack(),"wood")>=target,"Public wood did not become available within the action budget");
}
void Put(InventoryId target,string item,long quantity)
{
    Move(session.Engine.Observe(new(1)).OwnInventories.Single(i=>i.Id==target).Position);
    Do(new(ActionKind.Transfer,Source:Pack(),Destination:target,ItemId:item,Quantity:quantity));Idle();
}
void SupplyGround(InventoryId ground,string item,long quantity)
{
    while(Stock(ground,item)<quantity)
    {
        long q=Math.Min(10,quantity-Stock(ground,item));Wood(q);Put(ground,item,q);
    }
}
void Earn(long target)
{
    // This is a scripted choice to earn by raw spot sales, not a claim of an
    // optimal industrial strategy. The external900s segment deadline remains.
    long end=checked(session.Engine.Tick+30000);
    while(session.Engine.Observe(new(1)).Account.AvailableMinor<target&&session.Engine.Tick<end)
    {
        long held=Stock(Pack(),"wood");if(held==0){Wood(10);held=Stock(Pack(),"wood");}
        Move(definition.CapitalDemand!.Terminal);
        var offer=session.Engine.Observe(new(1)).CapitalOffers.Where(o=>o.ItemId=="wood"&&o.RemainingQuantity>0&&o.CreatedTick<session.Engine.Tick)
            .OrderByDescending(o=>o.UnitPriceMinor).FirstOrDefault();
        if(offer is null){Advance(session.Engine.Tick+60);continue;}
        Do(new(ActionKind.SellAvailableToCapital,OfferId:offer.Id,ItemId:"wood",Quantity:Math.Min(held,offer.RemainingQuantity)));Idle();
        Console.WriteLine($"H3 earning: tick={session.Engine.Tick}; cash={session.Engine.Observe(new(1)).Account.AvailableMinor}; target={target}");
    }
    Require(session.Engine.Observe(new(1)).Account.AvailableMinor>=target,"Natural earning budget exhausted");
}
void Milestone(string name)
{
    CheckpointStore.Save(session,Path.Combine(output,name+".save.json"));
    var copy=CheckpointStore.Decode(CheckpointStore.Encode(session));
    Require(copy.Engine.ComputeStateHash()==session.Engine.ComputeStateHash(),"Milestone save mismatch");
    milestones.Add(new{name,tick=session.Engine.Tick,cash=session.Engine.Observe(new(1)).Account.AvailableMinor});
    Write("running",null);Console.WriteLine($"H3 {name}: tick={session.Engine.Tick}");
}
