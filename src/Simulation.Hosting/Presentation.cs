using Simulation.Core;

namespace Simulation.Hosting;

public readonly record struct PresentationPoint(float X, float Z);
public record PresentationActor(long Id, bool Human, int X, int Z, bool Busy);
public record PresentationMarker(string Kind, int X, int Z, string Label, long EntityId, long OwnerId);
public record PresentationTrip(long LogisticsId, long OwnerId, string ItemId, string State, int X, int Z);
public record PresentationFrame(long Tick, int MapWidth, int MapHeight, PresentationActor[] Actors,
    PresentationMarker[] Markers, PresentationTrip[] Trips);

/// <summary>Публичная картинка сцены. Чужие договоры поддержки сюда не входят.</summary>
public static class Presentation
{
    public static PresentationPoint Between(int fromX, int fromZ, int toX, int toZ, float fraction)
    {
        if (fraction < 0) fraction = 0;
        if (fraction > 1) fraction = 1;
        return new(fromX + (toX - fromX) * fraction, fromZ + (toZ - fromZ) * fraction);
    }

    public static PresentationFrame Read(ScenarioDefinition scenario, SimulationEngine engine, long humanId)
    {
        var world = engine.Inspect();
        var actors = world.Participants.OrderBy(p => p.Id.Value).Select(p =>
            new PresentationActor(p.Id.Value, p.Id.Value == humanId, p.Position.X, p.Position.Z, p.Busy)).ToArray();
        var markers = new List<PresentationMarker>();
        var blocked = new HashSet<Cell>(scenario.BlockedCells);
        for (int x = 0; x < scenario.MapWidth; x++)
            for (int z = 0; z < scenario.MapHeight; z++)
                if (blocked.Contains(new(x, z)))
                    markers.Add(new("blocked", x, z, "", 0, 0));
        foreach (var plot in world.Plots.OrderBy(p => p.Id.Value))
        {
            string kind = plot.IsCorridor ? "corridor"
                : plot.ShopParticipantId.HasValue ? "shop-plot"
                : plot.ReservedBy.HasValue ? "plot-reserved"
                : plot.OwnerId?.Value == humanId ? "plot-own"
                : plot.OwnerId.HasValue ? "plot-foreign"
                : "plot-free";
            markers.Add(new(kind, plot.Position.X, plot.Position.Z, "", plot.Id.Value, plot.OwnerId?.Value ?? 0));
        }
        foreach (var node in scenario.ResourceNodes)
            markers.Add(new("resource", node.Position.X, node.Position.Z, node.ItemId, 0, 0));
        foreach (var building in world.Buildings.OrderBy(b => b.Id.Value))
            markers.Add(new(building.Kind.ToString().ToLowerInvariant(), building.Position.X, building.Position.Z,
                building.TypeId, building.Id.Value, building.OwnerId.Value));
        foreach (var group in world.CapitalOffers.Where(o => o.RemainingQuantity > 0).GroupBy(o => o.Position))
        {
            string label = string.Join(", ", group.OrderBy(o => o.ItemId, StringComparer.Ordinal).Select(o => $"{o.ItemId} {o.RemainingQuantity}"));
            markers.Add(new("capital", group.Key.X, group.Key.Z, label, group.Min(o => o.Id), 0));
        }
        var trips = world.Logistics.Where(l => l.Trip is not null).OrderBy(l => l.Id.Value).Select(l =>
            new PresentationTrip(l.Id.Value, l.OwnerId.Value, l.Trip!.ItemId, l.Trip.State, l.Trip.Position.X, l.Trip.Position.Z)).ToArray();
        return new(world.Tick, scenario.MapWidth, scenario.MapHeight, actors, markers.ToArray(), trips);
    }
}

public static partial class ScriptedScenarios
{
    /// <summary>Доводит существующий logistics-cycle до первого реального рейса и останавливается.</summary>
    public static void PrepareVisibleTrip(GameSession session, ScenarioDefinition definition)
    {
        const long wood = 20, product = 10, delivery = 10;
        ParticipantId supplier = new(1), buyer = new(2);
        var factory = session.Engine.Observe(supplier).OwnEnterprises.Single();
        var shop = session.Engine.Observe(buyer).OwnBuildings.Single();
        Move(session, supplier, definition.ResourceNodes.Single().Position);
        Execute(session, supplier, new(ActionKind.Harvest, ItemId: "wood", Quantity: wood));
        Wait(session, () => !session.Engine.Observe(supplier).Participant.Busy);
        Move(session, supplier, session.Engine.Observe(supplier).OwnBuildings.Single().Position);
        Execute(session, supplier, new(ActionKind.BuyLogistics, Enterprise: factory.Id));
        Execute(session, buyer, new(ActionKind.CreateBuyOffer, Destination: shop.InputId, ItemId: "plank", Quantity: delivery, UnitPriceMinor: 3000));
        long offer = session.Engine.Observe(buyer).BuyOffers.Single().Id;
        Execute(session, supplier, new(ActionKind.AcceptDeliveryQuantity, BuyOfferId: offer, Quantity: delivery));
        Wait(session, () => session.Engine.Inspect().DeliveryContracts.Length == 1);
        Execute(session, supplier, new(ActionKind.Transfer, ItemId: "wood", Quantity: wood,
            Source: session.Engine.Observe(supplier).Participant.InventoryId, Destination: factory.InputId));
        Execute(session, supplier, new(ActionKind.SetProductionEnabled, Enterprise: factory.Id, Enabled: true));
        Wait(session, () => session.Engine.Observe(supplier).OwnEnterprises.Single().CompletedBatches == product);
        Execute(session, supplier, new(ActionKind.SetProductionEnabled, Enterprise: factory.Id, Enabled: false));
        var system = session.Engine.Observe(supplier).OwnLogistics.Single();
        long contract = session.Engine.Observe(supplier).OwnDeliveryContracts.Single().Id;
        Execute(session, supplier, new(ActionKind.SetLogisticsContract, Logistics: system.Id, ContractId: contract, Enabled: true));
        Wait(session, () => session.Engine.Observe(supplier).OwnLogistics.Single().Trip is not null);
    }
}
