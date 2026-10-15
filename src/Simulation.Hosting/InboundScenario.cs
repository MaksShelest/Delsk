using Simulation.Core;

namespace Simulation.Hosting;

public static partial class ScriptedScenarios
{
    public static void InboundCycle(GameSession session, ScenarioDefinition definition)
    {
        ParticipantId owner = new(1);
        var factory = session.Engine.Observe(owner).OwnEnterprises.Single();
        var warehouse = session.Engine.Observe(owner).OwnBuildings.Single(b => b.Kind == BuildingKind.Warehouse);
        Move(session,owner,definition.ResourceNodes.Single().Position);
        Execute(session,owner,new(ActionKind.Harvest,ItemId:"wood",Quantity:30));
        Wait(session,()=>!session.Engine.Observe(owner).Participant.Busy);
        Move(session,owner,warehouse.Position);
        Execute(session,owner,new(ActionKind.Transfer,Source:session.Engine.Observe(owner).Participant.InventoryId,
            Destination:warehouse.InputId,ItemId:"wood",Quantity:30));
        Move(session,owner,definition.Buildings!.Single(b=>b.Id==factory.Id.Value).Position);
        Execute(session,owner,new(ActionKind.BuyLogistics,Enterprise:factory.Id));
        var system=session.Engine.Observe(owner).OwnLogistics.Single();
        Execute(session,owner,new(ActionKind.SetLogisticsInbound,Logistics:system.Id,Source:warehouse.InputId,Enabled:true));
        Wait(session,()=>session.Engine.Observe(owner).OwnLogistics.Single().CompletedTrips==2);
        Execute(session,owner,new(ActionKind.SetLogisticsInbound,Logistics:system.Id,Source:warehouse.InputId,Enabled:false));
        Execute(session,owner,new(ActionKind.SetProductionEnabled,Enterprise:factory.Id,Enabled:true));
        Wait(session,()=>session.Engine.Observe(owner).OwnEnterprises.Single().CompletedBatches==7);
        Execute(session,owner,new(ActionKind.SetProductionEnabled,Enterprise:factory.Id,Enabled:false));
        Execute(session,owner,new(ActionKind.SetLogisticsStorage,Logistics:system.Id,Destination:warehouse.InputId,Enabled:true));
        Wait(session,()=>session.Engine.Observe(owner).OwnLogistics.Single().CompletedTrips==3);
        session.Step();var state=session.Engine.Inspect();
        long Count(InventoryId id,string item)=>state.Inventories.Single(i=>i.Id==id).Stock.SingleOrDefault(s=>s.ItemId==item)?.Quantity??0;
        if(Count(factory.InputId,"wood")!=1||Count(warehouse.InputId,"wood")!=15||Count(warehouse.InputId,"plank")!=7||
            Count(factory.OutputId,"plank")!=0||session.Engine.Observe(owner).Account.BalanceMinor!=78050||
            state.Accounts.Sum(a=>a.BalanceMinor)!=200000||state.EmploymentGroups.Count(g=>g.Role==EmploymentRole.Logistics)!=1||
            state.Accounts.Sum(a=>a.ReservedMinor)!=0||state.Inventories.Sum(i=>i.ReservedCapacity)!=0||
            session.Engine.Ledger().Where(l=>l.Reason=="LogisticsWages").Sum(l=>l.AmountMinor)!=900)
            throw new InvalidOperationException("Inbound production output cycle differs from expected balances");
    }
}
