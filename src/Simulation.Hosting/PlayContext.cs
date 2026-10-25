using System.Globalization;
using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>Подсказка и действия текущей клетки. Дальнее наведение не создаёт команду.</summary>
public sealed record ContextChoice(string Key, string Label, GameAction? Command, bool Enabled = true, string Details = "");

public static class PlayContext
{
    public static long ReservationFee(long priceMinor, EconomicRules? rules = null) => (rules ?? new()).ReservationFee(priceMinor);
    private static string L(string lang, string ru, string en) => PlayerText.Local(lang, ru, en);
    private static long Free(InventoryView pack, string item) => pack.Stock.Where(s => s.ItemId == item).Sum(s => s.Quantity - s.Reserved);

    public static ContextChoice[] At(PlayerView view, ScenarioDefinition scenario, string language, bool tablets = false)
    {
        var cell = view.Participant.Position;
        var choices = new List<ContextChoice>();
        var pack = view.Inventories.Single(i => i.Id == view.Participant.InventoryId);
        if(view.Transfers.Length>0)
            choices.Add(new("transfer:stop",L(language,"Остановить передачу","Stop transfer"),new(ActionKind.StopTransfer)));
        foreach(var site in view.Resources.Where(r=>r.Position==cell))
        {
            string status=$"{site.Stock-site.Reserved}/{site.Capacity}" + (site.Public?L(language," · общая добыча"," · public resource"):
                site.Owner is {} owner?L(language," · владелец #"," · owner #")+owner.Value:site.Tenant is {} tenant?L(language," · аренда #"," · tenant #")+tenant.Value+" → "+site.LeaseUntil:L(language," · свободно"," · available"));
            choices.Add(new("resource:info:"+site.ItemId,PlayerText.Item(language,site.ItemId)+" · "+status,null,
                Details:L(language,"Восстановление +","Regeneration +")+site.RegenerationQuantity+" / "+site.RegenerationTicks+L(language," тиков."," ticks.")));
            if(!site.Public && site.Owner is null && site.Tenant is null)
            {
                choices.Add(new("resource:buy:"+site.ItemId,L(language,"Купить ресурсную клетку — ","Buy resource site — ")+PlayerText.Money(site.PurchasePriceMinor),
                    new(ActionKind.BuyResource,Target:cell,ItemId:site.ItemId),view.AvailableCashMinor>=site.PurchasePriceMinor));
                choices.Add(new("resource:lease:"+site.ItemId,L(language,"Арендовать на ","Lease for ")+site.LeaseTicks+L(language," тиков — "," ticks — ")+PlayerText.Money(site.LeasePriceMinor),
                    new(ActionKind.LeaseResource,Target:cell,ItemId:site.ItemId),view.AvailableCashMinor>=site.LeasePriceMinor));
            }
        }
        if (view.Harvest is { } harvest)
            choices.Add(new("harvest:stop", L(language, "Остановить добычу: ", "Stop harvesting: ") + PlayerText.Item(language, harvest.ItemId),
                PlayerDesk.StopHarvest(), Details: L(language, "При остановке незавершённый прогресс теряется. Выход из области ресурса также отменяет добычу.",
                    "Stopping discards unfinished progress. Leaving the resource area also cancels harvesting.")));
        else
        foreach (var node in scenario.ResourceNodes.Where(n => n.Position == cell).OrderBy(n => n.ItemId, StringComparer.Ordinal))
            choices.Add(new("harvest:" + node.ItemId,
                (node.ItemId=="milk" ? L(language,"Забирать поставки", "Collect supplies") : tablets ? L(language, "Добывать", "Harvest") : PlayerText.Get(language, "context.harvest")) + " " + PlayerText.Item(language, node.ItemId) + (tablets ? "" : " ×1"),
                tablets ? PlayerDesk.StartHarvesting(node.ItemId) : PlayerDesk.Harvest(node.ItemId, 1),
                Details: tablets ? L(language, "Повторяется по одной единице до остановки, ухода или заполнения инвентаря.",
                    "Repeats one unit at a time until stopped, leaving the area or inventory full.") : ""));
        var plot = view.Plots.SingleOrDefault(p => p.Position == cell);
        if (plot is { IsCorridor: false, OwnerId: null } &&
            (plot.ShopParticipantId is null || plot.ShopParticipantId == view.Participant.Id) &&
            (plot.ReservedBy is null || plot.ReservedBy == view.Participant.Id))
        {
            choices.Add(new("buy:" + plot.Id.Value,
                string.Format(CultureInfo.InvariantCulture, PlayerText.Get(language, "context.buy"), PlayerText.Money(plot.PriceMinor)),
                PlayerDesk.BuyPlot(plot.Id.Value)));
            if (plot.ReservedBy is null)
                choices.Add(new("reserve:" + plot.Id.Value,
                    string.Format(CultureInfo.InvariantCulture, PlayerText.Get(language, "context.reserve"), PlayerText.Money(ReservationFee(plot.PriceMinor, view.Economy))),
                    PlayerDesk.ReservePlot(plot.Id.Value)));
        }
        if(tablets && plot is not null && view.Storage?.Ground.Any(g=>g.PlotId==plot.Id.Value)==true)choices.Insert(0,new("object:ground:"+plot.Id.Value,L(language,"Хранение на земле · склад","Ground storage · warehouse"),null));
        var building = view.Buildings.SingleOrDefault(b => b.Position == cell);
        if (building?.Kind == BuildingKind.Production)
            choices.Add(new("card:" + building.Id.Value, PlayerText.Get(language, "context.card"), null));
        if (building?.Kind is BuildingKind.Warehouse or BuildingKind.Shop)
        {
            if (tablets) choices.Insert(0,new("object:building:" + building.Id.Value,L(language,"Открыть карточку хранилища","Open storage card"),null));
            var storage = view.Inventories.Single(i => i.Id == building.InputId);
            foreach (var item in pack.Stock.Where(s => s.Quantity > s.Reserved).OrderBy(s => s.ItemId, StringComparer.Ordinal))
                choices.Add(new("transfer:" + item.ItemId,
                    string.Format(CultureInfo.InvariantCulture, PlayerText.Get(language, "context.transfer"), PlayerText.Item(language, item.ItemId)),
                    PlayerDesk.Transfer(view.Participant.InventoryId.Value, building.InputId.Value, item.ItemId, 1),
                    storage.OccupiedVolume + storage.ReservedCapacity + scenario.Items.Single(i => i.Id == item.ItemId).UnitVolume <= storage.Capacity,
                    L(language, "Место в хранилище: ", "Storage capacity: ") + $"{storage.OccupiedVolume + storage.ReservedCapacity}/{storage.Capacity}"));
            foreach (var item in storage.Stock.Where(s => s.Quantity > s.Reserved).OrderBy(s => s.ItemId, StringComparer.Ordinal))
                choices.Add(new("take:" + item.ItemId, L(language, "Забрать ", "Take ") + PlayerText.Item(language, item.ItemId) + " ×1",
                    PlayerDesk.Transfer(building.InputId.Value, view.Participant.InventoryId.Value, item.ItemId, 1),
                    pack.OccupiedVolume + pack.ReservedCapacity + scenario.Items.Single(i => i.Id == item.ItemId).UnitVolume <= pack.Capacity,
                    L(language, "В хранилище: ", "In storage: ") + (item.Quantity - item.Reserved)));
        }
        if (plot?.OwnerId == view.Participant.Id && plot.BuildingId is null && !plot.IsCorridor)
        foreach (var type in scenario.BuildingTypes ?? [])
        {
            if ((type.Kind == BuildingKind.Shop) != (plot.IsShopSite || plot.ShopParticipantId == view.Participant.Id)) continue;
            var level = type.Levels.Single(l => l.Level == 1);
            var issues = new List<string>();
            if (view.AvailableCashMinor < level.PriceMinor)
                issues.Add(L(language, "Деньги: нужно ", "Money needed: ") + PlayerText.Money(level.PriceMinor) + L(language, ", доступно ", ", available ") + PlayerText.Money(view.AvailableCashMinor));
            var materialInventory=type.Kind==BuildingKind.Warehouse&&scenario.Economy?.Storage is not null&&view.Storage?.Ground.FirstOrDefault(g=>g.PlotId==plot?.Id.Value) is {} ground?view.Inventories.Single(i=>i.Id.Value==ground.InventoryId):pack;
            foreach (var cost in level.Materials)
                if (Free(materialInventory, cost.ItemId) < cost.Quantity)
                    issues.Add(PlayerText.Item(language, cost.ItemId) + $": {Free(materialInventory, cost.ItemId)}/{cost.Quantity}");
            if (type.Footprint.Any(offset => !view.Plots.Any(p => p.Position == new Cell(cell.X + offset.X, cell.Z + offset.Z) &&
                p.OwnerId == view.Participant.Id && !p.IsCorridor && p.BuildingId is null &&
                (!p.IsShopSite && p.ShopParticipantId is null || (p.IsShopSite || p.ShopParticipantId == view.Participant.Id) && type.Kind == BuildingKind.Shop))))
                issues.Add(L(language, "Нет свободной собственной площади под всё здание.", "The full footprint must be free and owned."));
            string materials = string.Join(", ", level.Materials.Select(m => PlayerText.Item(language, m.ItemId) + " ×" + m.Quantity));
            string price = PlayerText.Money(level.PriceMinor) + (materials.Length > 0 ? " + " + materials : "");
            choices.Add(new("build:" + type.Id, L(language, "Построить: ", "Build: ") + PlayerText.Building(language, type.Id) + " — " + price,
                PlayerDesk.Build(cell, type.Id), issues.Count == 0, string.Join("; ", issues)));
        }
        if (tablets)
            foreach (var machine in (view.Equipment ?? []).Where(e => e.Position == cell))
                choices.Add(new("object:equipment:" + machine.Id.Value,L(language,"Открыть карточку техники","Open equipment card"),null));
        if(tablets && scenario.Layout?.CivicObjects.Single(c=>c.Kind=="town_hall").Entry==cell)
            choices.Add(new("city",L(language,"Ратуша · город и выборы","Town hall · city and elections"),null));
        if(tablets && scenario.Layout?.CivicObjects.Single(c=>c.Kind=="dealership").Entry==cell)
            choices.Add(new("dealership",L(language,"Автосалон · информация","Vehicle dealer · information"),null));
        if (tablets && TabletState.IsCapital(scenario, cell))
        {
            choices.Add(new("exchange", L(language, "Открыть биржу", "Open exchange"), null));
            choices.Add(new("market:0", L(language, "Рынок участников", "Participant market"), null));
        }
        if (tablets)
            foreach (var shop in view.Market?.Shops.Where(s => s.Footprint.Contains(cell)) ?? [])
                choices.Insert(0, new("market:" + shop.Id.Value, L(language, "Открыть лавку", "Open shop") + " #" + shop.Id.Value, null));
        if (plot?.ReservedBy == view.Participant.Id)
            choices.Add(new("unreserve:" + plot.Id.Value, L(language, "Снять бронь без возврата платы", "Release reservation (no refund)"),
                PlayerDesk.Check(new(ActionKind.CancelPlotReservation, Plot: plot.Id))));
        foreach (var offer in view.CapitalOffers.Where(o => !tablets && o.Position == cell && o.RemainingQuantity > 0 && (o.ExpiresAtTick is null || o.ExpiresAtTick >= view.Tick))
            .OrderBy(o => o.ItemId, StringComparer.Ordinal).ThenBy(o => o.Id))
        {
            long available = Free(pack, offer.ItemId);
            string terms = $"#{offer.Id} · " + PlayerText.Item(language, offer.ItemId) + " · " + PlayerText.Money(offer.UnitPriceMinor) +
                L(language, "/шт. · спрос ", "/unit · demand ") + offer.RemainingQuantity;
            long reserved = pack.Stock.Where(s => s.ItemId == offer.ItemId).Sum(s => s.Reserved);
            terms += L(language, " · свободно ", " · available ") + available + L(language, ", резерв ", ", reserved ") + reserved;
            if (offer.CutoffTick is long close) terms += L(language, " · окно до ", " · window closes ") + close;
            if (offer.ExpiresAtTick is long end) terms += L(language, " · до тика ", " · until tick ") + end;
            string reason = available == 0 ? L(language, "Нет свободного товара в личном инвентаре.", "No unreserved goods in your inventory.") : "";
            choices.Add(new($"sell:{offer.Id}:1", L(language, "Продать 1: ", "Sell 1: ") + terms,
                PlayerDesk.Sell(offer.Id, offer.ItemId, 1), available > 0, reason));
            long max = Math.Min(available, offer.RemainingQuantity);
            if (max > 1) choices.Add(new($"sell:{offer.Id}:max", L(language, "Продать доступное ", "Sell available ") + $"×{max}: " + terms,
                PlayerDesk.Sell(offer.Id, offer.ItemId, max)));
        }
        // Preview only; the shared validator remains authoritative when a queued action runs.
        return choices.Select(choice =>
        {
            bool busy = view.Participant.Busy && choice.Command?.Kind is not (ActionKind.StopHarvest or ActionKind.StopTransfer);
            string reason = busy && choice.Command is not null ? L(language, "Персонаж занят. Дождитесь завершения действия.", "Participant busy. Wait for the current action.") : choice.Details;
            bool enabled = choice.Enabled && (!busy || choice.Command is null);
            if (choice.Command?.Kind is ActionKind.BuyPlot or ActionKind.ReservePlot)
            {
                long cost = choice.Command.Kind == ActionKind.BuyPlot ? plot!.PriceMinor : ReservationFee(plot!.PriceMinor, view.Economy);
                if (view.AvailableCashMinor < cost) { enabled = false; reason = L(language, "Недостаточно денег.", "Not enough money."); }
                if (plot!.IsShopSite && view.Plots.Any(p => p.IsShopSite && p.Id != plot.Id && (p.OwnerId == view.Participant.Id || p.ReservedBy == view.Participant.Id)))
                { enabled = false; reason = PlayerText.Reason(language,"ShopSiteLimit"); }
            }
            if (choice.Command?.Kind is (ActionKind.Harvest or ActionKind.StartHarvesting) && pack.OccupiedVolume + pack.ReservedCapacity + scenario.Items.Single(i => i.Id == choice.Command.ItemId).UnitVolume > pack.Capacity)
            { enabled = false; reason = L(language, "Инвентарь заполнен.", "Inventory full."); }
            if(choice.Command?.Kind is ActionKind.Harvest or ActionKind.StartHarvesting)
            {
                var resource=view.Resources.FirstOrDefault(r=>r.Position==cell && r.ItemId==choice.Command.ItemId);
                if(resource is not null && (!resource.Public && resource.Owner!=view.Participant.Id && resource.Tenant!=view.Participant.Id || resource.Stock<=resource.Reserved))
                {enabled=false;reason=PlayerText.Reason(language,resource.Stock<=resource.Reserved?"ResourceDepleted":"NoResourceRights");}
            }
            return choice with { Enabled = enabled, Details = reason };
        }).ToArray();
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
        var markers = frame.Markers.Where(m => m.X == x && m.Z == z && (m.Kind != "truck-home" || !frame.Trips.Any(t => t.LogisticsId == m.EntityId))).ToArray();
        if (markers.Length == 0) lines.Add(PlayerText.Get(language, "hover.empty"));
        foreach (var marker in markers)
            lines.Add(marker.Kind switch
            {
                "resource-depleted" => L(language,"Ресурс исчерпан; запас восстановится.","Resource depleted; stock will regenerate.")+" "+PlayerText.Item(language,marker.Label),
                "resource" => string.Format(CultureInfo.InvariantCulture, PlayerText.Get(language, "hover.resource"), PlayerText.Item(language, marker.Label)),
                "equipment" => L(language, "Добывающая техника: ", "Extraction equipment: ") + PlayerText.Item(language, marker.Label) + L(language, " · владелец ", " · owner ") + marker.OwnerId,
                "truck-home" => L(language, "Грузовик у предприятия · владелец ", "Truck at its enterprise · owner ") + marker.OwnerId,
                "blocked" => PlayerText.Get(language, "hover.blocked"),
                "corridor" => PlayerText.Get(language, "hover.corridor"),
                "capital" => CapitalHint(view, x, z, language),
                "plot" or "plot-free" => PlotLine(view, marker, language),
                "plot-own" or "plot-foreign" or "plot-reserved" or "shop-plot" => PlotLine(view, marker, language),
                "production" or "warehouse" or "shop" => BuildingHint(view, marker, language),
                "civic"=>marker.Label switch {"town_hall"=>L(language,"Ратуша · город и выборы","Town hall · city and elections"),"dealership"=>L(language,"Будущий автосалон · продажи в пакете E","Future vehicle dealer · sales in package E"),_=>L(language,"Столичный терминал","Capital terminal")},
                _ => marker.Kind
            });
        if (!here && markers.Any(m => m.Kind is "resource" or "resource-depleted" or "plot" or "plot-free" or "plot-own" or "production" or "warehouse" or "shop"))
            lines.Add(PlayerText.Get(language, "context.approach"));
        return string.Join("\n", lines);
    }

    private static string CapitalHint(PlayerView view, int x, int z, string language)
    {
        string goods = string.Join(", ", view.CapitalOffers.Where(o => o.Position == new Cell(x, z) && o.RemainingQuantity > 0 &&
            (o.ExpiresAtTick is null || o.ExpiresAtTick >= view.Tick)).Select(o => PlayerText.Item(language, o.ItemId)).Distinct());
        return L(language, "Терминал столицы. Продажа из личного инвентаря. ", "Capital terminal. Sell from your inventory. ") +
            (goods.Length > 0 ? L(language, "Товары: ", "Goods: ") + goods : L(language, "Сейчас спроса нет: дождитесь следующего периода.", "No current demand: wait for the next period."));
    }

    private static string BuildingHint(PlayerView view, PresentationMarker marker, string language)
    {
        string text = PlayerText.Building(language, marker.Label) + L(language, " · владелец: ", " · owner: ") +
            (marker.OwnerId == view.Participant.Id.Value ? PlayerText.Get(language, "you") : marker.OwnerId.ToString(CultureInfo.InvariantCulture));
        var own = view.Buildings.SingleOrDefault(b => b.Id.Value == marker.EntityId);
        if (own?.Kind is BuildingKind.Warehouse or BuildingKind.Shop)
        {
            var storage = view.Inventories.Single(i => i.Id == own.InputId);
            string goods = string.Join(", ", storage.Stock.Select(s => PlayerText.Item(language, s.ItemId) + $" ×{s.Quantity}"));
            text += "\n" + L(language, "Хранилище: ", "Storage: ") + (goods.Length > 0 ? goods : PlayerText.Get(language, "inventory.empty")) +
                $" · {storage.OccupiedVolume}/{storage.Capacity}";
        }
        return text;
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
        if (plot.IsCorridor) return state;
        if (plot.IsShopSite) state = L(language, "Общее место лавочки. Максимум одно купленное или забронированное место. ", "Shared shop site. At most one owned or reserved site. ") + state;
        if (plot.ShopParticipantId is { } assigned)
            state = L(language, "Место лавочки участника ", "Shop site for participant ") + assigned.Value + ". " +
                (assigned == view.Participant.Id ? state : L(language, "Чужое место: покупать и строить нельзя.", "Assigned to another participant: no purchase or building."));
        return state + " " + string.Format(CultureInfo.InvariantCulture, PlayerText.Get(language, "plot.price"), PlayerText.Money(plot.PriceMinor), PlayerText.Money(ReservationFee(plot.PriceMinor, view.Economy)));
    }
}
