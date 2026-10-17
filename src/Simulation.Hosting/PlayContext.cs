using System.Globalization;
using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>Подсказка и действия текущей клетки. Дальнее наведение не создаёт команду.</summary>
public sealed record ContextChoice(string Key, string Label, GameAction? Command);

public static class PlayContext
{
    public static long ReservationFee(long priceMinor) => checked((priceMinor + 9) / 10);

    public static ContextChoice[] At(PlayerView view, ScenarioDefinition scenario, string language)
    {
        var cell = view.Participant.Position;
        var choices = new List<ContextChoice>();
        foreach (var node in scenario.ResourceNodes.Where(n => n.Position == cell).OrderBy(n => n.ItemId, StringComparer.Ordinal))
            choices.Add(new("harvest:" + node.ItemId,
                PlayerText.Get(language, "context.harvest") + " " + PlayerText.Item(language, node.ItemId) + " ×1",
                PlayerDesk.Harvest(node.ItemId, 1)));
        var plot = view.Plots.SingleOrDefault(p => p.Position == cell);
        if (plot is { IsCorridor: false, ShopParticipantId: null, OwnerId: null } &&
            (plot.ReservedBy is null || plot.ReservedBy == view.Participant.Id))
        {
            choices.Add(new("buy:" + plot.Id.Value,
                string.Format(CultureInfo.InvariantCulture, PlayerText.Get(language, "context.buy"), plot.PriceMinor),
                PlayerDesk.BuyPlot(plot.Id.Value)));
            if (plot.ReservedBy is null)
                choices.Add(new("reserve:" + plot.Id.Value,
                    string.Format(CultureInfo.InvariantCulture, PlayerText.Get(language, "context.reserve"), ReservationFee(plot.PriceMinor)),
                    PlayerDesk.ReservePlot(plot.Id.Value)));
        }
        var building = view.Buildings.SingleOrDefault(b => b.Position == cell);
        if (building?.Kind == BuildingKind.Production)
            choices.Add(new("card:" + building.Id.Value, PlayerText.Get(language, "context.card"), null));
        if (building?.Kind is BuildingKind.Warehouse or BuildingKind.Shop)
        {
            var pack = view.Inventories.Single(i => i.Id == view.Participant.InventoryId);
            var item = pack.Stock.Where(s => s.Quantity > 0).OrderBy(s => s.ItemId, StringComparer.Ordinal).FirstOrDefault();
            if (item is not null)
                choices.Add(new("transfer:" + item.ItemId,
                    string.Format(CultureInfo.InvariantCulture, PlayerText.Get(language, "context.transfer"), PlayerText.Item(language, item.ItemId)),
                    PlayerDesk.Transfer(view.Participant.InventoryId.Value, building.InputId.Value, item.ItemId, 1)));
        }
        return choices.ToArray();
    }

    public static int Keep(int index, ContextChoice[] previous, ContextChoice[] next)
    {
        if (next.Length == 0 || index < 0 || index >= previous.Length) return 0;
        int found = Array.FindIndex(next, choice => choice.Key == previous[index].Key);
        return found >= 0 ? found : 0;
    }

    public static string Hover(PresentationFrame frame, PlayerView view, int x, int z, string language)
    {
        var here = view.Participant.Position.X == x && view.Participant.Position.Z == z;
        var lines = new List<string>();
        var markers = frame.Markers.Where(m => m.X == x && m.Z == z).ToArray();
        if (markers.Length == 0) lines.Add(PlayerText.Get(language, "hover.empty"));
        foreach (var marker in markers)
            lines.Add(marker.Kind switch
            {
                "resource" => string.Format(CultureInfo.InvariantCulture, PlayerText.Get(language, "hover.resource"), PlayerText.Item(language, marker.Label)),
                "blocked" => PlayerText.Get(language, "hover.blocked"),
                "corridor" => PlayerText.Get(language, "hover.corridor"),
                "capital" => string.Format(CultureInfo.InvariantCulture, PlayerText.Get(language, "hover.capital"), marker.Label),
                "plot" or "plot-free" => PlotLine(view, marker, language),
                "plot-own" or "plot-foreign" or "plot-reserved" or "shop-plot" => PlotLine(view, marker, language),
                "production" or "warehouse" or "shop" => string.Format(CultureInfo.InvariantCulture, PlayerText.Get(language, "hover.building"),
                    PlayerText.Get(language, "legend." + marker.Kind), marker.OwnerId == view.Participant.Id.Value ? PlayerText.Get(language, "you") : marker.OwnerId.ToString(CultureInfo.InvariantCulture)),
                _ => marker.Kind
            });
        if (!here && markers.Any(m => m.Kind is "resource" or "plot" or "plot-free" or "plot-own" or "production" or "warehouse" or "shop"))
            lines.Add(PlayerText.Get(language, "context.approach"));
        return string.Join("\n", lines);
    }

    private static string PlotLine(PlayerView view, PresentationMarker marker, string language)
    {
        var plot = view.Plots.SingleOrDefault(p => p.Id.Value == marker.EntityId);
        if (plot is null) return PlayerText.Get(language, "plot.foreign");
        string state = plot.IsCorridor ? PlayerText.Get(language, "hover.corridor")
            : plot.OwnerId == view.Participant.Id ? PlayerText.Get(language, "plot.own")
            : plot.OwnerId.HasValue ? PlayerText.Get(language, "plot.foreign")
            : plot.ReservedBy.HasValue ? string.Format(CultureInfo.InvariantCulture, PlayerText.Get(language, "plot.reserved"), plot.ReservationUntilTick)
            : PlayerText.Get(language, "plot.free");
        if (plot.IsCorridor || plot.ShopParticipantId.HasValue) return state;
        return state + " " + string.Format(CultureInfo.InvariantCulture, PlayerText.Get(language, "plot.price"), plot.PriceMinor, ReservationFee(plot.PriceMinor));
    }
}
