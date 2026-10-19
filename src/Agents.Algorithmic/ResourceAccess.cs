using Simulation.Core;

namespace Agents.Algorithmic;

public sealed partial class AlgorithmicController
{
    private static bool AvailableResource(ControllerObservation o, ResourceNodeDefinition node, long quantity = 1) =>
        o.Resources.Any(r=>r.Position==node.Position && r.Stock-r.Reserved>=quantity &&
            (r.Public || r.Owner==o.Participant.Id || r.Tenant==o.Participant.Id && o.Tick<r.LeaseUntil));
}
