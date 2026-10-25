using Simulation.Core;

namespace Simulation.Hosting;

public static partial class ScriptedScenarios
{
    /// <summary>Prepared three-cell building only; all outfit/input/payroll and production actions are real.</summary>
    public static void WorkshopsD1(GameSession s,ScenarioDefinition d)
    {
        ParticipantId actor=new(1);EnterpriseView Enterprise()=>s.Engine.Observe(actor).OwnEnterprises.Single();
        void Harvest(string item,long q){Move(s,actor,d.ResourceNodes.Single(n=>n.ItemId==item).Position);Execute(s,actor,new(ActionKind.Harvest,ItemId:item,Quantity:q));Wait(s,()=>!s.Engine.Observe(actor).Participant.Busy);}
        void Put(string item,long q){var e=Enterprise();Move(s,actor,s.Engine.Inspect().Inventories.Single(i=>i.Id==e.InputId).Position);Execute(s,actor,new(ActionKind.Transfer,Source:s.Engine.Observe(actor).Participant.InventoryId,Destination:e.InputId,ItemId:item,Quantity:q));Wait(s,()=>!s.Engine.Observe(actor).Participant.Busy);}
        Harvest("plank",6);Put("plank",6);Harvest("steel",1);Put("steel",1);Execute(s,actor,new(ActionKind.OutfitWorkshop,Enterprise:Enterprise().Id));s.AdvanceTo(Enterprise().Installations.Single().EndTick);
        Harvest("wood",6);Put("wood",6);Harvest("grain",6);Put("grain",6);long second=Enterprise().Workshops.Single(w=>w.Id!=Enterprise().Id.Value).Id;
        Execute(s,actor,new(ActionKind.SetRecipe,Enterprise:Enterprise().Id,WorkshopId:second,RecipeId:"d1-bread"));
        Execute(s,actor,new(ActionKind.AssignWorkshopWorkers,Enterprise:Enterprise().Id,WorkshopId:second,Group:new(8),Quantity:2));
        s.Send(actor,new(ActionKind.SetProductionEnabled,Enterprise:Enterprise().Id,Enabled:true));s.Send(actor,new(ActionKind.SetProductionEnabled,Enterprise:Enterprise().Id,WorkshopId:second,Enabled:true));s.Step();s.AdvanceTo(s.Engine.Tick+7);
        Execute(s,actor,new(ActionKind.SetProductionEnabled,Enterprise:Enterprise().Id,WorkshopId:second,Enabled:false));s.AdvanceTo(s.Engine.Tick+5);
        Execute(s,actor,new(ActionKind.SetProductionEnabled,Enterprise:Enterprise().Id,WorkshopId:second,Enabled:true));
        Execute(s,actor,new(ActionKind.AssignWorkshopWorkers,Enterprise:Enterprise().Id,WorkshopId:second,Group:new(8),Quantity:1));s.AdvanceTo(s.Engine.Tick+180);
        if(Enterprise().Workshops.Any(w=>w.CompletedBatches!=3)||s.Engine.Inspect().Flows.Single(f=>f.ItemId=="plank").Produced!=6||s.Engine.Inspect().Flows.Single(f=>f.ItemId=="bread").Produced!=3)
            throw new InvalidOperationException("Parallel workshops scenario differs");
    }
}
