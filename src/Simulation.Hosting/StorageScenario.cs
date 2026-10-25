using Simulation.Core;
namespace Simulation.Hosting;

public static partial class ScriptedScenarios
{
    /// <summary>Prepared C1 material sources; real authenticated transfers, cost/time, delivery and loss.</summary>
    public static void StorageC1(GameSession s,ScenarioDefinition d)
    {
        ParticipantId owner=new(1),buyer=new(2);
        void Harvest(string item,long quantity,ParticipantId actor)
        {Move(s,actor,d.ResourceNodes.Single(n=>n.ItemId==item).Position);Execute(s,actor,new(ActionKind.Harvest,ItemId:item,Quantity:quantity));Wait(s,()=>!s.Engine.Observe(actor).Participant.Busy);}
        void Put(ParticipantId actor,long target,string item,long q)
        {Move(s,actor,s.Engine.Inspect().Inventories.Single(i=>i.Id.Value==target).Position);Execute(s,actor,new(ActionKind.Transfer,Source:s.Engine.Observe(actor).Participant.InventoryId,Destination:new(target),ItemId:item,Quantity:q));Wait(s,()=>!s.Engine.Observe(actor).Participant.Busy);}
        long pile=s.Engine.StorageOf(owner).Ground.Single().InventoryId;
        foreach(var level in d.BuildingTypes!.Single(t=>t.Id=="warehouse").Levels)
        {
            foreach(var m in level.Materials){Harvest(m.ItemId,m.Quantity,owner);Put(owner,pile,m.ItemId,m.Quantity);}
            var building=s.Engine.Observe(owner).OwnBuildings.FirstOrDefault(b=>b.Kind==BuildingKind.Warehouse);
            Execute(s,owner,building is null?new(ActionKind.Build,Target:new(0,0),BuildingTypeId:"warehouse"):new(ActionKind.UpgradeBuilding,Building:building.Id));
            var job=s.Engine.StorageOf(owner).Construction.Single();s.AdvanceTo(job.CompletesTick);
        }
        var warehouse=s.Engine.Observe(buyer).OwnBuildings.Single(b=>b.Kind==BuildingKind.Warehouse).InputId;
        Harvest("grain",200,buyer);Put(buyer,warehouse.Value,"grain",200);Harvest("plank",20,owner);
        Execute(s,buyer,new(ActionKind.CreateBuyOffer,Destination:warehouse,ItemId:"plank",Quantity:20,UnitPriceMinor:10));
        long offer=s.Engine.Observe(buyer).BuyOffers.Single().Id;Execute(s,owner,new(ActionKind.AcceptDeliveryQuantity,BuyOfferId:offer,Quantity:20));s.AdvanceTo(s.Engine.Tick+6);
        var contract=s.Engine.Inspect().DeliveryContracts.Single();Move(s,owner,contract.Position);Execute(s,owner,new(ActionKind.DeliverContract,ContractId:contract.Id,Quantity:10));Wait(s,()=>!s.Engine.Observe(owner).Participant.Busy);
        var lot=s.Engine.Observe(buyer).OwnInventories.Single(i=>i.Id==warehouse).Overflow!.First();Move(s,buyer,contract.Position);
        Execute(s,buyer,new(ActionKind.TakeOverflow,Source:warehouse,OfferId:lot.Id,ItemId:lot.ItemId,Quantity:3));Wait(s,()=>!s.Engine.Observe(buyer).Participant.Busy);
        Execute(s,buyer,new(ActionKind.Transfer,Source:warehouse,Destination:s.Engine.Observe(buyer).Participant.InventoryId,ItemId:"grain",Quantity:7));Wait(s,()=>!s.Engine.Observe(buyer).Participant.Busy);
        Execute(s,owner,new(ActionKind.DeliverContract,ContractId:contract.Id,Quantity:10));Wait(s,()=>!s.Engine.Observe(owner).Participant.Busy);
        var second=s.Engine.Observe(buyer).OwnInventories.Single(i=>i.Id==warehouse).Overflow!.First();Execute(s,buyer,new(ActionKind.TakeOverflow,Source:warehouse,OfferId:second.Id,ItemId:second.ItemId,Quantity:3));Wait(s,()=>!s.Engine.Observe(buyer).Participant.Busy);
        long expires=s.Engine.Observe(buyer).OwnInventories.Single(i=>i.Id==warehouse).Overflow!.Max(l=>l.ExpiresTick);s.AdvanceTo(expires+1);
        if(s.Engine.Inspect().Flows.Single(f=>f.ItemId=="plank").Lost!=7||s.Engine.Inspect().DeliveryContracts.Single().PaidMinor!=200||s.Engine.Observe(owner).OwnInventories.Single(i=>i.Id.Value==pile).Capacity!=400)throw new InvalidOperationException("C1 storage scenario failed");
    }
}
