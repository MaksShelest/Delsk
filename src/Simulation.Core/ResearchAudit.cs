using System.Collections.Immutable;

namespace Simulation.Core;

/// <summary>Audit data only. The mutation entry points are absent from ordinary builds.</summary>
public record ResearchModification(long Sequence,long Tick,long AfterCommandCount,string Kind,ParticipantId Actor,
    InventoryId Inventory,string Item,long OldValue,long NewValue);

public sealed partial class SimulationEngine
{
#if DELSK_DEVELOPER
    public const bool IsDeveloperBuild=true;
#else
    public const bool IsDeveloperBuild=false;
#endif
    private readonly List<ResearchModification> _researchModifications=[];
    public ImmutableArray<ResearchModification> ResearchModifications => [.._researchModifications];
    public bool ResearchModified => _researchModifications.Count>0;
    private long ResearchStockDelta(string item)=>_researchModifications.Where(m=>m.Kind=="Stock" && m.Item==item).Sum(m=>checked(m.NewValue-m.OldValue));
    private int? ResearchWageExponent=>_researchModifications.LastOrDefault(m=>m.Kind=="WageExponent") is { } m ? checked((int)m.NewValue) : null;
    private bool IsResearchEvent(SimulationEvent e)=>e.Type=="ResearchModified" && e.Phase==9 &&
        _researchModifications.Any(m=>m.Sequence==e.EntityId && m.Tick==e.Tick && m.Kind==e.ReasonCode && m.Actor==e.ActorId && m.NewValue-m.OldValue==e.Quantity);
    private void ValidateResearchAudit()
    {
#if !DELSK_DEVELOPER
        if(ResearchModified)throw new CheckpointException("DeveloperSessionUnsupported");
#endif
        if(_researchModifications.Count>1024 || _researchModifications.Where((m,i)=>m.Sequence!=i+1 || m.Tick<0 || m.Tick>Tick ||
            m.AfterCommandCount<0 || m.OldValue<0 || m.NewValue<0 || !_actors.ContainsKey(m.Actor.Value) ||
            (m.Kind!="Money" && m.Kind!="Stock" && m.Kind!="WageExponent" && m.Kind!="HumanSelfBallot" && m.Kind!="IndustryExperience") ||
            (m.Kind=="IndustryExperience" && (Development is null || !Development.Industries.Any(i=>i.Id==m.Item))) ||
            (m.Kind=="Stock" && (!_items.ContainsKey(m.Item) || !_economy.Inventories.TryGetValue(m.Inventory.Value,out var inv) || inv.Owner!=m.Actor.Value)) ||
            (m.Kind=="WageExponent" && (m.NewValue<1 || m.NewValue>5)) || (i>0 && (m.Tick<_researchModifications[i-1].Tick || m.AfterCommandCount<_researchModifications[i-1].AfterCommandCount))).Any() ||
            _events.Count(e=>e.Type=="ResearchModified")!=_researchModifications.Count || _events.Where(e=>e.Type=="ResearchModified").Any(e=>!IsResearchEvent(e)))
            throw new CheckpointException("InvalidResearchAudit");
    }
}
