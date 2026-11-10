using System.Collections.Immutable;

namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private bool ParcelPolicy => IndustrialParcelRules.Applies(_config.Economy);

    private ParcelFrame? FrameAt(Cell anchor)
    {
        if (!ParcelPolicy || _config.Layout is not { } layout) return null;
        var lot = layout.IndustrialLots.FirstOrDefault(l => l.Cells.Contains(anchor));
        if (lot is null || IndustrialParcels.TryFrame(lot) is not { } frame || frame.Anchor != anchor) return null;
        return frame;
    }

    private Cell[] BuildingFootprint(Cell anchor, BuildingTypeDefinition type, int level = 1)
    {
        if (FrameAt(anchor) is { } frame)
        {
            var spec = type.Levels.Single(l => l.Level == level);
            var local = spec.Footprint.IsDefault ? type.Footprint : spec.Footprint.ToArray();
            return IndustrialParcels.MapFootprint(frame, local);
        }
        return ScenarioValidation.FootprintCells(anchor, type, level);
    }

    private Cell AnchorOf(IndustrialLot lot) =>
        ParcelPolicy && IndustrialParcels.TryFrame(lot) is { } frame
            ? frame.Anchor
            : lot.Cells.OrderBy(c => c.Z).ThenBy(c => c.X).First();

    private (ImmutableArray<Cell> Anchors, ImmutableArray<Cell> Facing) ParcelAnchors()
    {
        if (_config.Layout is not { } layout) return ([], default);
        var rows = layout.IndustrialLots.Select(lot => (Anchor: AnchorOf(lot), Frame: ParcelPolicy ? IndustrialParcels.TryFrame(lot) : null))
            .OrderBy(row => row.Anchor.Z).ThenBy(row => row.Anchor.X).ToArray();
        var anchors = rows.Select(row => row.Anchor).ToImmutableArray();
        if (!ParcelPolicy) return (anchors, default);
        return (anchors, rows.Select(row => row.Frame is { } frame ? new Cell(frame.RoadX, frame.RoadZ) : default).ToImmutableArray());
    }

    private void BuyParcel(ActorState actor, CommandEnvelope command, IndustrialLot lot)
    {
        var frame = IndustrialParcels.TryFrame(lot) ?? throw new RuleViolation("ParcelGeometry");
        if (_config.Layout is not { } layout || !layout.RoadCorridorCells.Contains(lot.Entry) || !IndustrialParcels.ReachesNetwork(layout, lot.Entry))
            throw new RuleViolation("ParcelEntry");
        var plots = new PlotState[lot.Cells.Length];
        for (int i = 0; i < lot.Cells.Length; i++)
        {
            if (!_plotsByCell.TryGetValue(lot.Cells[i], out var plot)) throw new RuleViolation("ParcelGeometry");
            plots[i] = plot;
        }
        if (plots.Length != IndustrialParcelRules.Cells || plots.Select(p => p.Definition.Id).Distinct().Count() != plots.Length)
            throw new RuleViolation("ParcelGeometry");
        if (plots.Any(p => p.Definition.IsCorridor || p.Definition.IsShopSite || p.Definition.ShopParticipantId.HasValue))
            throw new RuleViolation("ParcelGeometry");
        if (plots.Any(p => p.Definition.PriceMinor != IndustrialParcelRules.CellPriceMinor))
            throw new RuleViolation("ParcelPrice");
        if (plots.Any(p => p.Owner.HasValue || p.Building.HasValue)) throw new RuleViolation("ParcelOccupied");
        if (plots.Any(p => p.ReservedBy.HasValue)) throw new RuleViolation("ParcelReserved");
        if (layout.IndustrialLots.Any(other => other.Id != lot.Id && plots.Any(p => other.Entry == p.Definition.Position || other.Cells.Contains(p.Definition.Position))))
            throw new RuleViolation("ParcelOccupied");
        long price = IndustrialParcelRules.PriceMinor;
        if (_economy.Accounts[actor.Account].Available < price) throw new RuleViolation("NoMoney");
        long first = _nextId;
        int grounds = HasGroundStorage ? plots.Length : 0;
        long next = checked(_nextId + grounds);
        var tx = _economy.Begin(Tick);
        tx.Pay(actor.Account, _treasury, price, "ParcelPurchase");
        if (HasGroundStorage)
            for (int i = 0; i < plots.Length; i++)
                tx.CreateInventory(checked(first + i), actor.Id, plots[i].Definition.Position, GroundRules.GroundCapacity);
        tx.Commit();
        for (int i = 0; i < plots.Length; i++)
        {
            plots[i].Owner = actor.Id;
            plots[i].ReservedBy = null;
            plots[i].Until = null;
            if (HasGroundStorage) _groundStorage.Add(plots[i].Definition.Id, checked(first + i));
        }
        if (HasGroundStorage) _nextId = next;
        var anchor = plots.Single(p => p.Definition.Position == frame.Anchor);
        Emit("ParcelPurchased", actor.Id, anchor.Definition.Id, money: price, reason: lot.Id, command: command.CommandId);
    }
}
