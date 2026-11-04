using System.Diagnostics;
using System.Text.Json;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;

// Real ordinary game, three live algorithmic participants, unchanged 200 coins and no Developer reference.
string output=Path.GetFullPath(args[0]);
if(Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new ArgumentException("Use a fresh output directory");
Directory.CreateDirectory(output);var timer=Stopwatch.StartNew();string stage="start";
var profile=EconomyProfile.Load("config/vnext/economy.i4-mobile-equipment.json");
var balance=VNextBalanceReader.Read(File.ReadAllBytes("config/vnext/balance.b1.json")).Balance!;
var resolution=OrdinaryNewGame.Resolve(RunArtifacts.LoadScenario("scenarios/vnext/compact-valley.json"),profile,new(3,Seed:42),vnextBalance:balance);
var definition=resolution.Scenario;var session=new GameSession(new(definition));
var controllers=JsonSerializer.Deserialize<AutonomousConfiguration>(File.ReadAllText("scenarios/baseline-controllers.json"),SimulationJson.Options)!.ForOrdinary(definition,1,resolution.Controllers);
session.AttachControllers(controllers.Npcs.Select(n=>AlgorithmicController.WithEconomy(n,profile.Npc))
    .Select(n=>(IController)new AlgorithmicController(n,AlgorithmicController.RuntimeSeed(definition.Seed,n.ActorId),definition.Participants.Select(p=>p.Id)))
    .Append(new SelfBallotController(new(1))));
var milestones=new List<object>();
try
{
    Require(View().CashMinor==20000&&View().Inventories.All(i=>i.Stock.Length==0),"Not an ordinary empty start");
    stage="city-purchase";Move(View().Fleet!.Dealer);
    var model=View().Fleet!.Models.Single(m=>m.EquipmentTypeId=="extract_wood"&&m.EquipmentLevel==1);
    var listing=View().Fleet!.Stock.Single(v=>v.ModelId==model.Id);
    Do(new(ActionKind.BuyVehicle,VehicleId:listing.Id));Wait(()=>View().Fleet!.Vehicles.Any(v=>v.Vehicle.Id==listing.Id),10);
    var vehicle=View().Fleet!.Vehicles.Single(v=>v.Vehicle.Id==listing.Id).Vehicle;var eid=new EquipmentId(vehicle.Id);
    Require(View().CashMinor==5000,"Small equipment was not bought from the original 200 coins");
    Require(View().Equipment!.Single().Position==listing.Position,"Purchase teleported to a resource");Milestone("bought-with-starting-money");
    stage="earn-land";Earn(17000);SellPack();
    var site=View().Resources.Where(r=>r.ItemId=="wood"&&!r.Public&&r.Owner is null&&r.Tenant is null)
        .OrderBy(r=>Math.Abs(r.Position.X-listing.Position.X)+Math.Abs(r.Position.Z-listing.Position.Z)).First();
    Move(site.Position);Do(new(ActionKind.BuyResource,Target:site.Position));Milestone("legally-bought-first-site");
    stage="physical-drive";Move(listing.Position);Do(new(ActionKind.BoardVehicle,VehicleId:vehicle.Id));
    Do(new(ActionKind.MoveTo,Target:site.Position));ExactContinuation("moving");Idle();
    Require(View().Equipment!.Single().Position==site.Position,"Machine did not arrive");Milestone("physically-arrived");
    stage="mining";Require(session.Engine.EquipmentPlacementReason(new(1),eid)=="Allowed","Placement rejected");
    Do(new(ActionKind.PlaceEquipment,Equipment:eid));Do(new(ActionKind.StartHarvesting,Equipment:eid,ItemId:"wood",Quantity:2));
    ExactContinuation("working");Wait(()=>Stock(vehicle.CargoInventory,"wood")>=8,30);
    Do(new(ActionKind.SetEquipmentMode,Equipment:eid,Mode:EquipmentMode.Off));
    Do(new(ActionKind.Transfer,Source:vehicle.CargoInventory,Destination:Pack(),ItemId:"wood",Quantity:6));Idle();
    Require(Stock(Pack(),"wood")>=6,"No actual unloaded goods");Milestone("mined-and-unloaded");
    stage="earn-second-site";SellPack();Earn(17000);SellPack();
    var second=View().Resources.Where(r=>r.ItemId=="wood"&&!r.Public&&r.Owner is null&&r.Tenant is null)
        .OrderBy(r=>Math.Abs(r.Position.X-site.Position.X)+Math.Abs(r.Position.Z-site.Position.Z)).First();
    Move(second.Position);Do(new(ActionKind.BuyResource,Target:second.Position));
    Move(site.Position);long cargo=Stock(vehicle.CargoInventory,"wood");
    var original=View().Equipment!.Single();Do(new(ActionKind.PackEquipment,Equipment:eid));
    Require(Stock(vehicle.CargoInventory,"wood")==cargo&&cargo>0,"Packing lost goods");
    stage="relocate";Do(new(ActionKind.BoardVehicle,VehicleId:vehicle.Id));Move(second.Position);
    Do(new(ActionKind.PlaceEquipment,Equipment:eid));
    var relocated=View().Equipment!.Single();Require(relocated.Id==original.Id&&relocated.StorageId==original.StorageId&&relocated.GroupId==original.GroupId,"Relocation recreated equipment");
    Do(new(ActionKind.StartHarvesting,Equipment:eid,ItemId:"wood",Quantity:2));Wait(()=>Stock(vehicle.CargoInventory,"wood")>cargo,10);
    Do(new(ActionKind.SetEquipmentMode,Equipment:eid,Mode:EquipmentMode.Off));Milestone("same-instance-mines-second-site");
    stage="replay";
    var replay=GameSession.Replay(definition,session.CommandTape,session.Engine.Tick);
    Require(session.Engine.ComputeStateHash()==replay.Engine.ComputeStateHash(),"Full ordinary replay differs");
    RunArtifacts.Export(session,definition,Path.Combine(output,"export"),"ordinary-i4", "ordinary-i4",timer.Elapsed.TotalMilliseconds);
    stage="complete";Write("succeeded",null);
}
catch(Exception ex){CheckpointStore.Save(session,Path.Combine(output,"last-safe.save.json"));Write("failed",ex.ToString());throw;}

PlayerView View()=>PlayerDesk.Read(session.Engine,new(1));
InventoryId Pack()=>View().Participant.InventoryId;
long Stock(InventoryId id,string item)=>View().Inventories.Single(i=>i.Id==id).Stock.Where(s=>s.ItemId==item).Sum(s=>s.Quantity-s.Reserved);
void Require(bool condition,string why){if(!condition)throw new InvalidOperationException(stage+": "+why);}
void Tick()=>session.Step();
void Wait(Func<bool> done,int budget){while(!done()&&budget-->0)Tick();Require(done(),"Tick budget exhausted at t"+session.Engine.Tick);}
void Idle()=>Wait(()=>!View().Participant.Busy,1500);
void Do(GameAction action){var id=session.Send(new(1),PlayerDesk.Check(action));Tick();Require(session.Engine.Result(id)?.Status==ResultStatus.Accepted,action.Kind+": "+session.Engine.Result(id)?.ReasonCode);}
void Move(Cell target){Idle();if(View().Participant.Position!=target){Do(new(ActionKind.MoveTo,Target:target));Idle();}}
void SellPack()
{
    Move(View().ProductionContent!.Terminal);
    int budget=1200;
    while(View().Inventories.Single(i=>i.Id==Pack()).Stock.Any(s=>s.Quantity>s.Reserved)&&budget-->0)
    {
        var held=View().Inventories.Single(i=>i.Id==Pack()).Stock.First(s=>s.Quantity>s.Reserved);
        var offer=View().CapitalOffers.Where(o=>o.ItemId==held.ItemId&&o.RemainingQuantity>0&&o.CreatedTick<session.Engine.Tick).OrderByDescending(o=>o.UnitPriceMinor).FirstOrDefault();
        if(offer is null){Tick();continue;}
        Do(PlayerDesk.Sell(offer.Id,held.ItemId,Math.Min(held.Quantity-held.Reserved,offer.RemainingQuantity)));Idle();
    }
    Require(budget>0,"No funded capital demand for held goods");
}
void Earn(long target)
{
    long end=session.Engine.Tick+12000;
    while(View().AvailableCashMinor<target&&session.Engine.Tick<end)
    {
        SellPack();if(View().AvailableCashMinor>=target)break;
        var source=View().Resources.Where(r=>r.Public&&r.ItemId!="milk"&&r.Stock-r.Reserved>0)
            .OrderByDescending(r=>definition.Items.Single(i=>i.Id==r.ItemId).BasePriceMinor/(decimal)definition.Items.Single(i=>i.Id==r.ItemId).UnitVolume)
            .ThenBy(r=>Math.Abs(r.Position.X-View().ProductionContent!.Terminal.X)+Math.Abs(r.Position.Z-View().ProductionContent!.Terminal.Z)).First();
        Move(source.Position);long amount=Math.Min(20/definition.Items.Single(i=>i.Id==source.ItemId).UnitVolume,View().Resources.Single(r=>r.Position==source.Position).Stock-View().Resources.Single(r=>r.Position==source.Position).Reserved);
        if(amount<=0)continue;Do(new(ActionKind.Harvest,ItemId:source.ItemId,Quantity:amount));Idle();SellPack();
    }
    Require(View().AvailableCashMinor>=target,"Natural earning limit exhausted");
}
void ExactContinuation(string name)
{
    var path=Path.Combine(output,name+".save.json");CheckpointStore.Save(session,path);
    var copy=CheckpointStore.Load(path).Session;
    copy.RestoreControllers(c=>c.Kind==SelfBallotController.Kind?SelfBallotController.Restore(c):AlgorithmicController.Restore(c));
    for(int n=0;n<3;n++){Tick();copy.Step();Require(session.Engine.ComputeStateHash()==copy.Engine.ComputeStateHash(),"Loaded world differs");Require(session.ComputeControllerStateHash()==copy.ComputeControllerStateHash(),"Loaded controllers differ");}
}
void Milestone(string name){CheckpointStore.Save(session,Path.Combine(output,name+".save.json"));milestones.Add(new{name,tick=session.Engine.Tick,cash=View().AvailableCashMinor});Console.WriteLine(name+" t"+session.Engine.Tick);Write("running",null);}
void Write(string status,string? error)=>File.WriteAllText(Path.Combine(output,"walkthrough.json"),JsonSerializer.Serialize(new{status,error,stage,tick=session.Engine.Tick,cash=View().AvailableCashMinor,milestones,elapsedSeconds=timer.Elapsed.TotalSeconds,scope="Real ordinary compact-valley game; original 200 coins, three actual NPCs, ordinary commands, no Developer resources."},new JsonSerializerOptions(SimulationJson.Options){WriteIndented=true}));
