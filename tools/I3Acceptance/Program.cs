using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using Agents.Algorithmic;
using Simulation.Core;
using Simulation.Hosting;

// Ordinary start, actual NPCs, no state injection. Long earning is recorded separately
// from prepared mechanic tests and from synthetic native UI checks.
string output=Path.GetFullPath(args[0]),map=args.Length>1?args[1]:PlayMaps.CompactValley;
if(Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new ArgumentException("Use a new output directory; previous attempts are retained");
Directory.CreateDirectory(output);var timer=Stopwatch.StartNew();string stage="start";
var profile=EconomyProfile.Read(File.ReadAllText("config/vnext/economy.g1-npc.json"));
var balance=VNextBalanceReader.Read(File.ReadAllBytes("config/vnext/balance.b1.json")).Balance!;
var resolution=OrdinaryNewGame.Resolve(RunArtifacts.LoadScenario("scenarios/vnext/"+map+".json"),profile,new(3,Seed:42),vnextBalance:balance);
var definition=resolution.Scenario;
var session=args.Length>2?CheckpointStore.Load(args[2]).Session:new GameSession(new(definition));
Require(JsonSerializer.Serialize(session.Engine.Configuration(),SimulationJson.Options)==JsonSerializer.Serialize(definition,SimulationJson.Options),"Resume configuration differs from the selected ordinary map");
if(args.Length>2)session.RestoreControllers(c=>c.Kind==SelfBallotController.Kind?SelfBallotController.Restore(c):AlgorithmicController.Restore(c));
else
{
    var controllers=JsonSerializer.Deserialize<AutonomousConfiguration>(File.ReadAllText("scenarios/baseline-controllers.json"),SimulationJson.Options)!.ForOrdinary(definition,1,resolution.Controllers);
    session.AttachControllers(controllers.Npcs.Select(n=>AlgorithmicController.WithEconomy(n,profile.Npc)).Select(n=>(IController)new AlgorithmicController(n,AlgorithmicController.RuntimeSeed(definition.Seed,n.ActorId),definition.Participants.Select(p=>p.Id))).Append(new SelfBallotController(new(1))));
    Require(View().CashMinor==20000&&View().Fleet!.Vehicles.IsEmpty&&View().Inventories.All(i=>i.Stock.Length==0),"Not an ordinary empty start");
}
var milestones=new List<object>();long lastSave=session.Engine.Tick;bool stop=false;
using var signal=OperatingSystem.IsWindows()?null:PosixSignalRegistration.Create(PosixSignal.SIGTERM,c=>{c.Cancel=true;stop=true;});
try
{
    stage="natural-earning";
    long paverPrice=View().Fleet!.Models.Single(m=>m.Id=="trakt_d1").PriceMinor;
    if(!View().Fleet!.Vehicles.Any(v=>v.Model.MaterialsOnly))
    {
        if(!View().Buildings.Any(b=>b.TypeId=="materials")){Earn(60000);SellPack();EnsureFactory();Milestone("naturally-funded-materials-enterprise");}
        EarnWithGravel(paverPrice+10000);
    }
    Milestone("earned-paver-and-materials-budget");
    stage="buy-paver";
    var paver=View().Fleet!.Vehicles.FirstOrDefault(v=>v.Model.MaterialsOnly)?.Vehicle;
    if(paver is null)
    {
        Move(View().Fleet!.Dealer);Wait(()=>View().Fleet!.Stock.Any(v=>v.ModelId=="trakt_d1"),3000);
        Do(new(ActionKind.BuyVehicle,VehicleId:View().Fleet!.Stock.First(v=>v.ModelId=="trakt_d1").Id));Wait(()=>View().Fleet!.Vehicles.Any(v=>v.Model.MaterialsOnly),10);
        paver=View().Fleet!.Vehicles.First(v=>v.Model.MaterialsOnly).Vehicle;
    }
    Move(View().Fleet!.Vehicles.Single(v=>v.Vehicle.Id==paver.Id).Position);Do(new(ActionKind.BoardVehicle,VehicleId:paver.Id));
    var park=definition.Layout!.RoadCenterCells.Where(c=>!View().Blocked.Contains(c)&&!definition.Layout.CivicObjects.Any(o=>o.Footprint.Contains(c)))
        .OrderBy(c=>Math.Abs(c.X-View().Participant.Position.X)+Math.Abs(c.Z-View().Participant.Position.Z)).First();
    Move(park);Do(new(ActionKind.LeaveVehicle));Milestone("bought-boarded-and-drove-paver");
    stage="lawful-gravel-production";
    var factory=EnsureFactory();
    var recipe=definition.Recipes.Single(r=>r.Id=="crush_stone");foreach(var input in recipe.Ingredients){Acquire(input.ItemId,input.Quantity);Put(factory.InputId,input.ItemId,input.Quantity);}
    Do(new(ActionKind.SetProductionEnabled,Enterprise:new(factory.Id.Value),Enabled:true));Wait(()=>Stock(factory.OutputId!.Value,"gravel")>=2,1200);
    Do(new(ActionKind.SetProductionEnabled,Enterprise:new(factory.Id.Value),Enabled:false));Wait(()=>View().Enterprises.Single(e=>e.Id.Value==factory.Id.Value).BatchId==0,300);
    Move(factory.Position);Do(PlayerDesk.Transfer(factory.OutputId!.Value.Value,Pack().Value,"gravel",2));Idle();Put(paver.CargoInventory,"gravel",2);
    Acquire("asphalt",1);Put(paver.CargoInventory,"asphalt",1);Milestone("loaded-actual-produced-and-purchased-materials");
    stage="road-work";Move(View().Fleet!.Vehicles.Single(v=>v.Vehicle.Id==paver.Id).Position);Do(new(ActionKind.BoardVehicle,VehicleId:paver.Id));
    var target=definition.Layout.RoadCenterCells.Where(c=>View().Roads!.Cells.Any(r=>r.Cell==c&&r.Level==1)&&!definition.Layout.CivicObjects.Any(o=>o.Footprint.Contains(c)))
        .OrderBy(c=>Math.Abs(c.X-park.X)+Math.Abs(c.Z-park.Z)).First();
    Move(target);var quote=session.Engine.QuoteRoadCell(new(1),paver.Id,target,EquipmentMode.Personal);Require(quote.CanStart,"Road quote: "+quote.Reason);
    Do(new(ActionKind.StartRoadCell,Target:target,VehicleId:paver.Id,EquipmentLevel:quote.Next!.Level));
    Wait(()=>View().Roads!.OwnWorks.Single(w=>w.VehicleId==paver.Id).Batch is not null,10);Milestone("actual-construction-in-progress");
    Wait(()=>View().Roads!.Cells.Any(r=>r.Cell==target&&r.Level==2),40);Milestone("road-complete");
    Require(View().Roads!.Cells.Single(r=>r.Cell==target).Contributions.Last().Builder==new ParticipantId(1),"Lost builder rights");
    var neighbor=definition.Layout.InitialCityDirtCells.First(c=>Math.Abs(c.X-target.X)+Math.Abs(c.Z-target.Z)==1);Move(neighbor);Move(target);
    var copy=CheckpointStore.Decode(CheckpointStore.Encode(session));copy.RestoreControllers(c=>c.Kind==SelfBallotController.Kind?SelfBallotController.Restore(c):AlgorithmicController.Restore(c));
    for(int n=0;n<3;n++){Tick();copy.Step();Require(session.Engine.ComputeStateHash()==copy.Engine.ComputeStateHash(),"Loaded continuation differs");Require(session.ComputeControllerStateHash()==copy.ComputeControllerStateHash(),"Loaded NPC continuation differs");}
    stage="complete";Milestone("continued-after-save-load");Write("succeeded",null);
}
catch(Exception error){CheckpointStore.Save(session,Path.Combine(output,"last-safe.save.json"));Write(error is OperationCanceledException?"interrupted":"failed",error.ToString());throw;}

PlayerView View()=>PlayerDesk.Read(session.Engine,new(1));
InventoryId Pack()=>View().Participant.InventoryId;
long Stock(InventoryId inventory,string item)=>View().Inventories.Single(i=>i.Id==inventory).Stock.Where(s=>s.ItemId==item).Sum(s=>s.Quantity-s.Reserved);
void Require(bool condition,string why){if(!condition)throw new InvalidOperationException(why);}
void Tick()
{
    if(stop)throw new OperationCanceledException("Bounded segment stopped at a committed boundary; resume last-safe.save.json");
    session.Step();if(session.Engine.WaitingForBallots)Require(session.PumpVoting(),"Unresolved ballot");
    if(session.Engine.Tick-lastSave>=1000){lastSave=session.Engine.Tick;CheckpointStore.Save(session,Path.Combine(output,"progress.save.json"));Write("running",null);Console.WriteLine($"{map}: {stage} t={session.Engine.Tick} cash={View().AvailableCashMinor} elapsed={timer.Elapsed.TotalSeconds:0}s");}
}
void Wait(Func<bool> done,int bound){while(!done()&&bound-->0)Tick();Require(done(),"Tick budget at "+stage);}
void Idle()=>Wait(()=>!View().Participant.Busy,1500);
void Do(GameAction action){var id=session.Send(new(1),PlayerDesk.Check(action));Tick();Require(session.Engine.Result(id)?.Status==ResultStatus.Accepted,$"{action.Kind}: {session.Engine.Result(id)?.ReasonCode}");}
void Move(Cell cell){Idle();if(View().Participant.Position!=cell){Do(PlayerDesk.Move(cell.X,cell.Z));Idle();}}
void Put(InventoryId destination,string item,long amount){Move(View().Inventories.Single(i=>i.Id==destination).Position);Do(PlayerDesk.Transfer(Pack().Value,destination.Value,item,amount));Idle();}
void Acquire(string item,long amount)
{
    if(definition.ResourceNodes.FirstOrDefault(n=>n.ItemId==item&&n.Rules!.Public) is {} source)
    {
        Move(source.Position);Wait(()=>View().Resources.Single(r=>r.Position==source.Position).Stock-View().Resources.Single(r=>r.Position==source.Position).Reserved>=amount,1200);
        Do(new(ActionKind.Harvest,ItemId:item,Quantity:amount));Idle();return;
    }
    Move(View().ProductionContent!.Terminal);Wait(()=>View().ProductionContent!.Imports.Any(i=>i.ItemId==item&&i.Remaining>=amount),2400);
    Do(new(ActionKind.BuyImport,ItemId:item,Quantity:amount));Do(PlayerDesk.Transfer(View().ProductionContent!.Pickup!.Value.Value,Pack().Value,item,amount));Idle();
}
void Earn(long target)
{
    long end=session.Engine.Tick+60000;
    while(View().AvailableCashMinor<target&&session.Engine.Tick<end)
    {
        var held=View().Inventories.Single(i=>i.Id==Pack()).Stock.FirstOrDefault(i=>i.Quantity>i.Reserved);
        if(held is null)
        {
            var node=definition.ResourceNodes.Where(n=>n.Rules!.Public&&n.ItemId!="milk").OrderByDescending(n=>definition.Items.Single(i=>i.Id==n.ItemId).BasePriceMinor/(decimal)definition.Items.Single(i=>i.Id==n.ItemId).UnitVolume).ThenBy(n=>Math.Abs(n.Position.X-View().ProductionContent!.Terminal.X)+Math.Abs(n.Position.Z-View().ProductionContent!.Terminal.Z)).First();
            long amount=View().Inventories.Single(i=>i.Id==Pack()).Capacity/definition.Items.Single(i=>i.Id==node.ItemId).UnitVolume;Acquire(node.ItemId,amount);held=View().Inventories.Single(i=>i.Id==Pack()).Stock.Single(i=>i.ItemId==node.ItemId);
        }
        Move(View().ProductionContent!.Terminal);
        var offer=View().CapitalOffers.Where(o=>o.ItemId==held.ItemId&&o.RemainingQuantity>0&&o.CreatedTick<session.Engine.Tick).OrderByDescending(o=>o.UnitPriceMinor).FirstOrDefault();
        if(offer is null){for(int n=0;n<60;n++)Tick();continue;}
        Do(PlayerDesk.Sell(offer.Id,held.ItemId,Math.Min(held.Quantity-held.Reserved,offer.RemainingQuantity)));Idle();
    }
    Require(View().AvailableCashMinor>=target,"Natural earning budget exhausted");
}
void Milestone(string name){CheckpointStore.Save(session,Path.Combine(output,name+".save.json"));milestones.Add(new{name,tick=session.Engine.Tick,cash=View().AvailableCashMinor});Write("running",null);}
void Write(string status,string? error)=>File.WriteAllText(Path.Combine(output,"walkthrough.json"),JsonSerializer.Serialize(new{status,error,stage,map,tick=session.Engine.Tick,cash=View().AvailableCashMinor,milestones,elapsedSeconds=timer.Elapsed.TotalSeconds,scope="Scripted ordinary Hosting commands, unchanged starting 200 coins, actual three NPCs. No Developer or free materials."},new JsonSerializerOptions(SimulationJson.Options){WriteIndented=true}));

BuildingView EnsureFactory()
{
    var factory=View().Buildings.FirstOrDefault(b=>b.TypeId=="materials");
    if(factory is null)
    {
        var anchors=definition.Layout!.IndustrialLots.Select(l=>l.Cells.OrderBy(c=>c.Z).ThenBy(c=>c.X).First()).ToHashSet();
        var plot=View().Plots.Where(p=>p.OwnerId is null&&p.ReservedBy is null&&!p.IsShopSite&&anchors.Contains(p.Position)).OrderBy(p=>Math.Abs(p.Position.X-View().ProductionContent!.Terminal.X)+Math.Abs(p.Position.Z-View().ProductionContent!.Terminal.Z)).First();
        Move(plot.Position);Do(new(ActionKind.BuyPlot,Plot:plot.Id));var ground=new InventoryId(View().Storage!.Ground.Single(g=>g.PlotId==plot.Id.Value).InventoryId);
        var rule=definition.BuildingTypes!.Single(t=>t.Id=="materials").Levels[0];
        foreach(var material in rule.Materials)
        {
            long remaining=material.Quantity;while(remaining>0){long amount=Math.Min(remaining,10);Acquire(material.ItemId,amount);Put(ground,material.ItemId,amount);remaining-=amount;}
        }
        Move(plot.Position);Do(new(ActionKind.Build,Target:plot.Position,BuildingTypeId:"materials"));Wait(()=>View().Buildings.Any(b=>b.TypeId=="materials"),120);
        factory=View().Buildings.Single(b=>b.TypeId=="materials");
    }
    return factory;
}
void SellPack()
{
    while(View().Inventories.Single(i=>i.Id==Pack()).Stock.Any(s=>s.Quantity>s.Reserved))
    {
        var held=View().Inventories.Single(i=>i.Id==Pack()).Stock.First(s=>s.Quantity>s.Reserved);Move(View().ProductionContent!.Terminal);
        var offer=View().CapitalOffers.Where(o=>o.ItemId==held.ItemId&&o.RemainingQuantity>0&&o.CreatedTick<session.Engine.Tick).OrderByDescending(o=>o.UnitPriceMinor).FirstOrDefault();
        if(offer is null){for(int n=0;n<60;n++)Tick();continue;}
        Do(PlayerDesk.Sell(offer.Id,held.ItemId,Math.Min(held.Quantity-held.Reserved,offer.RemainingQuantity)));Idle();
    }
}
void EarnWithGravel(long target)
{
    var factory=EnsureFactory();SellPack();
    while(View().AvailableCashMinor<target)
    {
        if(Stock(factory.OutputId!.Value,"gravel")==0)
        {
            Acquire("stone",12);Put(factory.InputId,"stone",12);
            Do(new(ActionKind.SetProductionEnabled,Enterprise:new(factory.Id.Value),Enabled:true));
            Wait(()=>Stock(factory.OutputId.Value,"gravel")>=20,1200);
            Do(new(ActionKind.SetProductionEnabled,Enterprise:new(factory.Id.Value),Enabled:false));
            Wait(()=>View().Enterprises.Single(e=>e.Id.Value==factory.Id.Value).BatchId==0,300);
        }
        Move(factory.Position);long quantity=Math.Min(20,Stock(factory.OutputId.Value,"gravel"));Do(PlayerDesk.Transfer(factory.OutputId.Value.Value,Pack().Value,"gravel",quantity));Idle();SellPack();
    }
}
