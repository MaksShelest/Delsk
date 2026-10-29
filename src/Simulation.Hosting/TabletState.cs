using Simulation.Core;

namespace Simulation.Hosting;

public enum TabletKind { Closed, Capital, Enterprise, Market, Player, Object, CityMap, Notices, Tutorial }

/// <summary>Client interaction state, not an economic clock or a second world.</summary>
public sealed class TabletState
{
    public TabletKind Kind { get; private set; }
    public long EnterpriseId { get; private set; }
    public Cell Location { get; private set; }
    public bool PauseTime { get; private set; }
    public bool IsOpen => Kind != TabletKind.Closed;
    public bool BlocksTime => IsOpen && PauseTime;
    public bool IsPaused(bool externalPause) => externalPause || BlocksTime;

    public bool OpenCapital(PlayerView view, ScenarioDefinition scenario)
    {
        if (!IsCapital(scenario, view.Participant.Position)) return false;
        Open(TabletKind.Capital, view.Participant.Position, 0); return true;
    }
    public bool OpenEnterprise(PlayerView view, long id)
    {
        var building = view.Buildings.SingleOrDefault(b => b.Id.Value == id && b.Kind == BuildingKind.Production && b.Footprint.Contains(view.Participant.Position));
        if (building is null) return false;
        Open(TabletKind.Enterprise, view.Participant.Position, id); return true;
    }
    public void OpenPlayer(PlayerView view) => Open(TabletKind.Player, view.Participant.Position, 0);
    public void OpenCityMap(PlayerView view) => Open(TabletKind.CityMap, view.Participant.Position, 0);
    public void OpenNotices(PlayerView view) => Open(TabletKind.Notices, view.Participant.Position, 0);
    public void OpenTutorial(PlayerView view) => Open(TabletKind.Tutorial, view.Participant.Position, 0);
    private string _objectKind="";
    public bool OpenObject(PlayerView view, string kind, long id)
    {
        _objectKind=kind;
        bool valid = kind=="ground"?view.Storage?.Ground.Any(g=>g.PlotId==id&&view.Plots.Any(p=>p.Id.Value==id&&p.Position==view.Participant.Position))==true:kind == "equipment" ? view.Equipment?.Any(e => e.Id.Value == id && e.Position == view.Participant.Position) == true
            : view.Buildings.Any(b => b.Id.Value == id && b.Footprint.Contains(view.Participant.Position));
        if (!valid) return false;
        Open(TabletKind.Object, view.Participant.Position, id); return true;
    }
    public bool OpenMarket(PlayerView view, ScenarioDefinition scenario, long shop = 0)
    {
        if (shop == 0 ? !IsCapital(scenario, view.Participant.Position) :
            view.Market?.Shops.Any(s => s.Id.Value == shop && s.Footprint.Contains(view.Participant.Position)) != true) return false;
        Open(TabletKind.Market, view.Participant.Position, shop); return true;
    }
    private void Open(TabletKind kind, Cell cell, long enterprise)
    { Kind = kind; Location = cell; EnterpriseId = enterprise; PauseTime = true; }
    public void Close()
    { Kind = TabletKind.Closed; EnterpriseId = 0; PauseTime = false; }
    public void SetTimeRunning(bool running) { if (IsOpen) PauseTime = !running; }
    public bool ResumeForMovement(bool allowed)
    {
        if (!allowed || !BlocksTime) return false;
        PauseTime = false; return true;
    }
    public bool ValidateLocation(PlayerView view, ScenarioDefinition scenario)
    {
        if (!IsOpen) return false;
        if (Kind is TabletKind.Player or TabletKind.CityMap or TabletKind.Notices or TabletKind.Tutorial) return true;
        if (Kind == TabletKind.Object)
        {
            bool available = view.Participant.Position == Location&&(_objectKind=="building"?view.Buildings.Any(b=>b.Id.Value==EnterpriseId):true);
            if (!available) Close();
            return available;
        }
        bool valid = Kind == TabletKind.Market ? (EnterpriseId == 0 ? view.Participant.Position == Location && IsCapital(scenario, Location) :
            view.Market?.Shops.Any(s => s.Id.Value == EnterpriseId && s.Footprint.Contains(view.Participant.Position)) == true) :
            Kind == TabletKind.Capital ? view.Participant.Position == Location && IsCapital(scenario, Location)
            : view.Buildings.Any(b => b.Id.Value == EnterpriseId && b.Kind == BuildingKind.Production && b.Footprint.Contains(view.Participant.Position));
        if (!valid) Close();
        return valid;
    }
    public static bool IsCapital(ScenarioDefinition scenario, Cell cell) =>
        scenario.CapitalDemand?.Terminal == cell || scenario.CapitalOffers.Any(o => o.Position == cell);
}

public sealed class CapitalTablePreferences
{
    public string ItemId { get; set; } = "";
    public bool HighestPriceFirst { get; set; } = true;
    public CapitalOfferView[] Offers(PlayerView view, Cell location)
    {
        var offers = view.CapitalOffers.Where(o => o.Position == location && o.RemainingQuantity > 0 &&
            (o.ExpiresAtTick is null || o.ExpiresAtTick >= view.Tick) && (ItemId.Length == 0 || o.ItemId == ItemId));
        return (HighestPriceFirst ? offers.OrderByDescending(o => o.UnitPriceMinor) : offers.OrderBy(o => o.UnitPriceMinor)).ThenBy(o => o.ItemId, StringComparer.Ordinal).ThenBy(o => o.Id).ToArray();
    }
}

public readonly record struct TabletProgress(long Done, long Total)
{
    public static TabletProgress Production(EnterpriseView enterprise) => enterprise.BatchId == 0 || enterprise.WorkRequired <= 0
        ? new(0, 1) : new(Math.Clamp(enterprise.WorkDone, 0, enterprise.WorkRequired), enterprise.WorkRequired);
    public static TabletProgress Window(CapitalOfferView offer, long tick, int windowTicks) => offer.CutoffTick is long cutoff
        ? new(Math.Clamp((long)windowTicks - Math.Max(0, cutoff - tick), 0, windowTicks), windowTicks) : new(0, 1);
    public static long Remaining(long deadline, long tick) => Math.Max(0, deadline - tick);
}
