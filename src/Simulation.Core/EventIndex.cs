namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    // Derived indexes over an immutable append-only journal. Never persisted or substituted for checks.
    private readonly Dictionary<string,List<SimulationEvent>> _eventsByType=new(StringComparer.Ordinal);
    private readonly Dictionary<(string,long),List<SimulationEvent>> _eventsByTypeEntity=[];
    private int _indexedEvents;
    private void IndexEvents()
    {
        while(_indexedEvents<_events.Count)
        {
            var e=_events[_indexedEvents++];
            if(!_eventsByType.TryGetValue(e.Type,out var type))_eventsByType.Add(e.Type,type=[]);type.Add(e);
            if(!_eventsByTypeEntity.TryGetValue((e.Type,e.EntityId),out var entity))_eventsByTypeEntity.Add((e.Type,e.EntityId),entity=[]);entity.Add(e);
        }
    }
    private void ResetEventIndex(){_indexedEvents=0;_eventsByType.Clear();_eventsByTypeEntity.Clear();}
    private IReadOnlyList<SimulationEvent> EventsOfType(string type)
    {IndexEvents();return _eventsByType.TryGetValue(type,out var rows)?rows:[];}
    private IReadOnlyList<SimulationEvent> EventsOfType(string type,long entity)
    {IndexEvents();return _eventsByTypeEntity.TryGetValue((type,entity),out var rows)?rows:[];}
}
