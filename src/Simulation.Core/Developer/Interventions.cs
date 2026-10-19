namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    /// <summary>Developer package only. Absolute validated targets make replay idempotence checks explicit.</summary>
    public ResearchModification DeveloperModify(string kind,ParticipantId actor,long newValue,long afterCommandCount,
        InventoryId inventory=default,string item="",ResearchModification? expected=null)
    {
        if(_stepping || !HasCommittedBoundary() || _travelQuantum!=0 || WaitingForBallots)
            throw new InvalidOperationException("UnsafeInterventionBoundary");
        if(newValue<0 || newValue>1000000000000L || afterCommandCount<0 || !_actors.TryGetValue(actor.Value,out var owner) || _researchModifications.Count>=1024)
            throw new ArgumentException("InvalidIntervention");
        long old=kind switch
        {
            "Money"=>_economy.Accounts[owner.Account].Balance,
            "Stock" when _economy.Inventories.TryGetValue(inventory.Value,out var inv) && inv.Owner==actor.Value && _items.ContainsKey(item) && !_logistics.Values.Any(l=>l.Cargo==inventory.Value)=>inv.Count(item),
            "WageExponent"=>EconomyRules.WageAttractionExponent,
            "HumanSelfBallot"=>0,
            _=>throw new ArgumentException("UnsupportedIntervention")
        };
        if(kind=="WageExponent" && newValue is <1 or >5)throw new ArgumentException("InvalidWageExponent");
        if(kind=="HumanSelfBallot" && newValue!=1)throw new ArgumentException("InvalidHumanPolicy");
        var modification=new ResearchModification(_researchModifications.Count+1,Tick,afterCommandCount,kind,actor,inventory,item,old,newValue);
        if(expected is not null && modification!=expected)throw new CheckpointException("InterventionReplayMismatch");
        long delta=checked(newValue-old);
        long valueDelta=kind=="Money"?delta:kind=="Stock"?checked(delta*_items[item].BasePriceMinor):0;
        _=checked(PublicEconomy().CapitalMinor+valueDelta);_=checked(CapitalOf(actor).TotalMinor+valueDelta);
        if(kind=="Money" && delta>0)_=checked(_economy.InitialMoney+_economy.ExternalIn+delta);
        var tx=_economy.Begin(Tick);
        if(kind=="Money" && delta!=0)
        {if(delta>0)tx.CreditExternal(owner.Account,delta,"DeveloperMoney");else tx.DebitExternal(owner.Account,-delta,"DeveloperMoney");}
        if(kind=="Stock" && delta!=0)
        {
            _=checked(ResearchStockDelta(item)+delta);
            if(delta>0)tx.AddStock(inventory.Value,item,delta);else tx.RemoveFreeStock(inventory.Value,item,-delta);
        }
        tx.Commit();_researchModifications.Add(modification);_phase=9;
        Emit("ResearchModified",actor.Value,modification.Sequence,item,delta,reason:kind);
        ValidateInvariants();return modification;
    }
}
