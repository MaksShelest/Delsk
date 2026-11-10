using Simulation.Core;

namespace Agents.Algorithmic;

public sealed partial class AlgorithmicController
{
    private static bool AvailableResource(ControllerObservation o, ResourceNodeDefinition node, long quantity = 1) =>
        o.Resources.Any(r => r.Position == node.Position && r.Stock - r.Reserved >= quantity &&
            ResourceRightsRules.Manual(Open(o), r.Public, r.Owner?.Value, r.Tenant?.Value, r.LeaseUntil, o.Participant.Id.Value, o.Tick));

    /// <summary>Своя клетка под технику. Купленная публичная клетка годится только при новой политике.</summary>
    private static bool OwnedMachineLand(ControllerObservation o, ResourceSiteView site) =>
        site.Owner == o.Participant.Id && (!site.Public || Open(o));

    /// <summary>Покупка под технику остаётся только у ничейных частных клеток. Публичные не скупаются.</summary>
    private static bool PurchasableMachineLand(ResourceSiteView site) =>
        !site.Public && site.Owner is null && site.Tenant is null;

    private static bool MachineSite(ControllerObservation o, ResourceSiteView site) =>
        OwnedMachineLand(o, site) || PurchasableMachineLand(site);

    private static bool Open(ControllerObservation o) => ResourceRightsRules.Applies(o.Economics?.ResourceRights);
}
