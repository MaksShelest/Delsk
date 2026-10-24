using Simulation.Core;

namespace Simulation.Hosting;

public static partial class ScriptedScenarios
{
    /// <summary>Prepared building/staff fixture, not autonomous acquisition from 200 coins.</summary>
    public static void VNextSupportedProduction(GameSession session, ScenarioDefinition definition)
    {
        if(definition.EconomyProfileId!=VNextSupportedProfile.ProfileId)throw new ArgumentException("Fixture requires explicit --economy config/vnext/economy.b1-supported.json.");
        ParticipantId actor=new(1);
        Execute(session,actor,new(ActionKind.Harvest,ItemId:"stone",Quantity:3));
        Wait(session,()=>!session.Engine.Observe(actor).Participant.Busy);
        var e=session.Engine.Observe(actor).OwnEnterprises.Single();
        Execute(session,actor,new(ActionKind.Transfer,ItemId:"stone",Quantity:3,Source:session.Engine.Observe(actor).Participant.InventoryId,Destination:e.InputId));
        long before=session.Engine.Tick;
        Execute(session,actor,new(ActionKind.SetProductionEnabled,Enterprise:e.Id,Enabled:true));
        Wait(session,()=>session.Engine.Observe(actor).OwnEnterprises.Single().CompletedBatches==1);
        if(session.Engine.Tick-before!=20)throw new InvalidOperationException("Expected 20 paid full-staff work ticks.");
        Execute(session,actor,new(ActionKind.SetProductionEnabled,Enterprise:e.Id,Enabled:false));
        session.AdvanceTo(50);
        var state=session.Engine.Inspect();
        if(state.Inventories.Single(i=>i.Id==e.OutputId).Stock.Single(s=>s.ItemId=="gravel").Quantity!=5 ||
            state.Flows.Single(f=>f.ItemId=="stone").RecipeInput!=3 || session.Engine.Observe(actor).Account.BalanceMinor!=17960 ||
            session.Engine.Ledger().Where(l=>l.Reason=="Wages").Sum(l=>l.AmountMinor)!=2000 ||
            session.Engine.Ledger().Where(l=>l.Reason=="Energy").Sum(l=>l.AmountMinor)!=40)
            throw new InvalidOperationException("Fixture product/consumption/payroll/service mismatch.");
        session.Engine.ValidateInvariants();
    }
}
