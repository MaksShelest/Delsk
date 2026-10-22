using Simulation.Core;

namespace Simulation.Hosting;

public readonly record struct PresentationPoint(float X, float Z);
public record PresentationActor(long Id, bool Human, int X, int Z, bool Busy,
    int OffsetX = SimulationEngine.TravelCenter, int OffsetZ = SimulationEngine.TravelCenter, int FacingX = 0, int FacingZ = -1);
public record PresentationMarker(string Kind, int X, int Z, string Label, long EntityId, long OwnerId)
{
    public int Level {get;init;}=1;
    public Cell[] Footprint {get;init;}=[];
    public Cell[] Walls {get;init;}=[];
    public WarehouseVisual? Warehouse {get;init;}
    public long ResourceStock {get;init;}=1;
    public long ResourceCapacity {get;init;}=1;
    public bool ResourceSparse {get;init;}
}
public record PresentationTrip(long LogisticsId, long OwnerId, string ItemId, string State, int X, int Z, bool Loaded = false);
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
        {
            var travel = engine.TravelOf(p.Id);
            return new PresentationActor(p.Id.Value, p.Id.Value == humanId, p.Position.X, p.Position.Z, p.Busy,
                travel.OffsetX, travel.OffsetZ, travel.FacingX, travel.FacingZ);
        }).ToArray();
        var markers = new List<PresentationMarker>();
        var blocked = new HashSet<Cell>(engine.BlockedCells());
        for (int x = 0; x < scenario.MapWidth; x++)
            for (int z = 0; z < scenario.MapHeight; z++)
                if (blocked.Contains(new(x, z)))
                    markers.Add(new("blocked", x, z, "", 0, 0));
        foreach (var plot in world.Plots.OrderBy(p => p.Id.Value))
        {
            string kind = plot.IsCorridor ? "corridor"
                : plot.IsShopSite || plot.ShopParticipantId.HasValue ? "shop-plot"
                : plot.ReservedBy.HasValue ? "plot-reserved"
                : plot.OwnerId?.Value == humanId ? "plot-own"
                : plot.OwnerId.HasValue ? "plot-foreign"
                : "plot-free";
            markers.Add(new(kind, plot.Position.X, plot.Position.Z, "", plot.Id.Value, plot.OwnerId?.Value ?? 0));
        }
        var resourceSites=engine.ResourceSites();
        var resourceIndex=resourceSites.ToDictionary(r=>(r.Position,r.ItemId));
        var resourceMax=resourceSites.GroupBy(r=>r.ItemId).ToDictionary(g=>g.Key,g=>g.Max(r=>r.Capacity),StringComparer.Ordinal);
        foreach(var node in scenario.ResourceNodes)
        {
            var site=resourceIndex[(node.Position,node.ItemId)];
            markers.Add(new(site.Stock==0?"resource-depleted":"resource",node.Position.X,node.Position.Z,node.ItemId,0,0)
            {ResourceStock=site.Stock,ResourceCapacity=site.Capacity,ResourceSparse=site.Capacity<resourceMax[node.ItemId]});
        }
        foreach (var building in world.Buildings.OrderBy(b => b.Id.Value))
        {
            var type=scenario.BuildingTypes!.Single(t=>t.Id==building.TypeId);
            WarehouseVisual? warehouse=null;
            if(building.Kind==BuildingKind.Warehouse)
            {
                // Inspect is internal to this projection. Foreign inventory values never leave it.
                bool known=building.OwnerId.Value==humanId;
                var stock=known?world.Inventories.Single(i=>i.Id==building.InputId):null;
                warehouse=new(known,stock?.Capacity??0,stock is null?[]:stock.Stock.Where(s=>s.Quantity>0).Select(s=>new VisibleStock(s.ItemId,checked(s.Quantity*scenario.Items.Single(i=>i.Id==s.ItemId).UnitVolume))).ToArray());
            }
            markers.Add(new(building.Kind.ToString().ToLowerInvariant(),building.Position.X,building.Position.Z,building.TypeId,building.Id.Value,building.OwnerId.Value)
                {Level=building.Level,Footprint=[..type.Footprint],Walls=[..type.Walls??[]],Warehouse=warehouse});
        }
        foreach (var group in world.CapitalOffers.Where(o => o.RemainingQuantity > 0).GroupBy(o => o.Position))
        {
            string label = string.Join(", ", group.OrderBy(o => o.ItemId, StringComparer.Ordinal).Select(o => $"{o.ItemId} {o.RemainingQuantity}"));
            markers.Add(new("capital", group.Key.X, group.Key.Z, label, group.Min(o => o.Id), 0));
        }
        // The terminal is a place, including between demand publications and after an order is filled.
        foreach (var terminal in scenario.CapitalOffers.Select(o => o.Position)
            .Concat(scenario.CapitalDemand is { } demand ? [demand.Terminal] : []).Distinct())
            if (!markers.Any(m => m.Kind == "capital" && m.X == terminal.X && m.Z == terminal.Z))
                markers.Add(new("capital", terminal.X, terminal.Z, "", 0, 0));
        foreach (var equipment in world.Equipment.OrderBy(e => e.Id.Value))
        {
            string item = scenario.EquipmentTypes!.Single(t => t.Id == equipment.TypeId).ItemId;
            markers.Add(new("equipment", equipment.Position.X, equipment.Position.Z, item, equipment.Id.Value, equipment.OwnerId.Value){Level=equipment.Level});
        }
        foreach (var logistics in world.Logistics.OrderBy(l => l.Id.Value))
        {
            var home = world.Inventories.Single(i => i.Id == logistics.SourceInventory).Position;
            markers.Add(new("truck-home", home.X, home.Z, "", logistics.Id.Value, logistics.OwnerId.Value));
        }
        var trips = world.Logistics.Where(l => l.Trip is not null).OrderBy(l => l.Id.Value).Select(l =>
            new PresentationTrip(l.Id.Value, l.OwnerId.Value, l.Trip!.ItemId, l.Trip.State, l.Trip.Position.X, l.Trip.Position.Z, world.Inventories.Single(i=>i.Id==l.CargoInventory).Stock.Any(s=>s.Quantity>0))).ToArray();
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
        var shop = session.Engine.Observe(buyer).OwnBuildings.Single(b=>b.Kind==BuildingKind.Shop);
        Move(session, supplier, definition.ResourceNodes.Single().Position);
        Execute(session, supplier, new(ActionKind.Harvest, ItemId: "wood", Quantity: wood));
        Wait(session, () => !session.Engine.Observe(supplier).Participant.Busy);
        Move(session, supplier, session.Engine.Observe(supplier).OwnBuildings.Single(b=>b.Kind==BuildingKind.Production).Position);
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
