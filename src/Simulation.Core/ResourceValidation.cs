namespace Simulation.Core;

public static partial class ScenarioValidation
{
    private static void ValidateResourceSettings(ScenarioDefinition s)
    {
        foreach(var type in s.BuildingTypes??[])
            if(type.Walls is {} walls && (walls.Length>type.Footprint.Length || walls.Distinct().Count()!=walls.Length || walls.Contains(new Cell(0,0)) || walls.Any(c=>!type.Footprint.Contains(c))))
                throw new ArgumentException("Invalid building walls/gate");
        if (s.ResourceNodes.Select(n => (n.Position,n.ItemId)).Distinct().Count() != s.ResourceNodes.Length)
            throw new ArgumentException("Duplicate resource cell");
        foreach (var node in s.ResourceNodes)
        {
            var r = node.Rules ?? new();
            if (r.Capacity is < 1 or > 1000000000 || r.RegenerationQuantity < 0 || r.RegenerationQuantity > r.Capacity ||
                r.RegenerationTicks is < 1 or > 1000000 || r.LeaseTicks is < 1 or > 1000000 ||
                r.PurchasePriceMinor <= 0 || r.LeasePriceMinor <= 0 || r.Public && r.InitialOwner.HasValue ||
                r.InitialOwner.HasValue && !s.Participants.Any(p => p.Id == r.InitialOwner))
                throw new ArgumentException("Invalid resource stock or rights settings");
        }
    }
}
