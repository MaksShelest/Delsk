using Simulation.Core;

namespace Simulation.Hosting;

public static partial class ScriptedScenarios
{
    public static void PhysicalAutomation(GameSession session,ScenarioDefinition definition)
    {
        ParticipantId owner=new(1);
        Execute(session,owner,new(ActionKind.BuyEquipment,EquipmentTypeId:"wood-extractor",Target:new(0,0)));
        var machine=session.Engine.Observe(owner).OwnEquipment.Single();
        Execute(session,owner,new(ActionKind.SetEquipmentMode,Equipment:machine.Id,Mode:EquipmentMode.Automatic));
        Execute(session,owner,new(ActionKind.SetExtractionRoute,Equipment:machine.Id,Stops:[new(0,3)],Enabled:true));
        var factory=session.Engine.Observe(owner).OwnEnterprises.Single();
        var warehouse=session.Engine.Observe(owner).OwnBuildings.Single(b=>b.Kind==BuildingKind.Warehouse);
        Move(session,owner,definition.Buildings!.Single(b=>b.Id==factory.Id.Value).Position);
        Execute(session,owner,new(ActionKind.BuyLogistics,Enterprise:factory.Id));
        var system=session.Engine.Observe(owner).OwnLogistics.Single();
        Execute(session,owner,new(ActionKind.SetTransportRoute,Logistics:system.Id,Source:machine.StorageId,Destination:warehouse.InputId,ItemId:"wood",Enabled:true));
        Wait(session,()=>session.Engine.Observe(owner).OwnInventories.Single(i=>i.Id==warehouse.InputId).Stock.Sum(s=>s.Quantity)==8 &&
            session.Engine.Observe(owner).OwnLogistics.Single().Trip is null && session.Engine.Observe(owner).OwnEquipment.Single().Motion is null);
        Execute(session,owner,new(ActionKind.SetTransportRoute,Logistics:system.Id,Source:machine.StorageId,Destination:warehouse.InputId,ItemId:"wood",Enabled:false));
        Execute(session,owner,new(ActionKind.SetEquipmentMode,Equipment:machine.Id,Mode:EquipmentMode.Off));
        var world=session.Engine.Inspect();
        if(world.Flows.Single(f=>f.ItemId=="wood").Harvested!=8 || session.Engine.ResourceSites().Where(r=>r.Owner==owner).Any(r=>r.Stock!=0) ||
            world.Logistics.Single().CompletedTrips<2 || world.Inventories.Sum(i=>i.ReservedCapacity)!=0 || world.Accounts.Where(a=>a.Kind=="Participant").Sum(a=>a.ReservedMinor)!=0)
            throw new InvalidOperationException($"Physical conservation failed: harvested={world.Flows.Single(f=>f.ItemId=="wood").Harvested}; stocks={string.Join(',',session.Engine.ResourceSites().Where(r=>r.Owner==owner).Select(r=>r.Stock))}; trips={world.Logistics.Single().CompletedTrips}; capacity={world.Inventories.Sum(i=>i.ReservedCapacity)}; moneyHolds={world.Accounts.Sum(a=>a.ReservedMinor)}; machine={world.Equipment.Single().Position}; trip={world.Logistics.Single().Trip?.State}");
        var replay=GameSession.Replay(definition,session.CommandTape,session.Engine.Tick);
        if(replay.Engine.ComputeStateHash()!=session.Engine.ComputeStateHash())throw new InvalidOperationException("Physical automation replay mismatch");
    }
}
