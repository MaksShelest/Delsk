using Simulation.Core;
namespace Simulation.Hosting;
public static partial class ScriptedScenarios
{
    /// <summary>Prepared sources; actual investments, released cells, partial rescue and exact expiry.</summary>
    public static void DemolitionC2(GameSession s,ScenarioDefinition d)
    {
        ParticipantId actor=new(1);
        void Harvest(string item,long q){Move(s,actor,d.ResourceNodes.Single(n=>n.ItemId==item).Position);Execute(s,actor,new(ActionKind.Harvest,ItemId:item,Quantity:q));Wait(s,()=>!s.Engine.Observe(actor).Participant.Busy);}
        void Put(InventoryId inventory,string item,long q){Move(s,actor,s.Engine.Inspect().Inventories.Single(i=>i.Id==inventory).Position);Execute(s,actor,new(ActionKind.Transfer,Source:s.Engine.Observe(actor).Participant.InventoryId,Destination:inventory,ItemId:item,Quantity:q));Wait(s,()=>!s.Engine.Observe(actor).Participant.Busy);}
        Harvest("wood",30);Harvest("steel",3);Move(s,actor,new(0,0));Execute(s,actor,new(ActionKind.Build,Target:new(0,0),BuildingTypeId:"c2-factory"));var b=s.Engine.Observe(actor).OwnBuildings.Single(b=>b.TypeId=="c2-factory");Put(b.InputId,"wood",15);Put(b.InputId,"steel",2);Execute(s,actor,new(ActionKind.UpgradeBuilding,Building:b.Id));Harvest("grain",200);Put(b.InputId,"grain",200);Harvest("plank",100);Put(b.OutputId!.Value,"plank",100);
        Execute(s,actor,new(ActionKind.PrepareDemolition,Building:b.Id));var quote=s.Engine.QuoteDemolition(actor,b.Id.Value)!;Execute(s,actor,new(ActionKind.DemolishBuilding,Building:b.Id,DemolitionQuote:quote.Token));
        var lot=s.Engine.Observe(actor).OwnInventories.SelectMany(i=>i.Overflow??[]).Single(l=>l.ItemId=="wood");Execute(s,actor,new(ActionKind.TakeOverflow,Source:b.InputId,OfferId:lot.Id,ItemId:lot.ItemId,Quantity:1));Wait(s,()=>!s.Engine.Observe(actor).Participant.Busy);s.AdvanceTo(lot.ExpiresTick+1);
        if(s.Engine.Inspect().Flows.Single(f=>f.ItemId=="wood").Lost!=17||s.Engine.Inspect().Flows.Single(f=>f.ItemId=="wood").ConstructionRecovered!=18||s.Engine.DemolitionOf(actor)!.Receipts.Single().PriceMinor!=9000)throw new InvalidOperationException("Demolition scenario differs");
    }
}
