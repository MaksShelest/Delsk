namespace Simulation.Core;

public record OwnCapitalRequest(long OfferId, string ItemId, long Quantity, int Window, long? CutoffTick);
public record OwnCapitalTrading(OwnCapitalRequest[] Requests, SettlementView[] Settlements);

public sealed partial class SimulationEngine
{
    /// <summary>Read-only availability of manual movement, including collision and occupation.</summary>
    public bool CanTravel(ParticipantId actor, int x, int z)
    {
        if (x is < -1 or > 1 || z is < -1 or > 1) throw new ArgumentOutOfRangeException(nameof(x));
        if (!_actors.TryGetValue(actor.Value, out var state)) throw new ArgumentException("Participant not available");
        if ((state.Job is not null && !IsHarvest(state.Job)) || HasSale(state.Id)) return false;
        return CanAxis(x, state.OffsetX, new(state.Position.X + x, state.Position.Z)) ||
            CanAxis(z, state.OffsetZ, new(state.Position.X, state.Position.Z + z));

        bool CanAxis(int direction, int offset, Cell next) => direction != 0 &&
            (direction > 0 ? offset < TravelUnitsPerCell - 1 || _map.Walkable(next) : offset > 0 || _map.Walkable(next));
    }

    /// <summary>Only this participant's pending capital requests and settlements. No foreign holds.</summary>
    public OwnCapitalTrading CapitalTradingOf(ParticipantId actor)
    {
        if (!_actors.ContainsKey(actor.Value)) throw new ArgumentException("Participant not available");
        var requests = _offers.Values.Where(o => o.Requests.ContainsKey(actor.Value)).OrderBy(o => o.Definition.Id)
            .Select(o => new OwnCapitalRequest(o.Definition.Id, o.Definition.ItemId, o.Requests[actor.Value].Quantity, o.Window, o.Cutoff)).ToArray();
        return new(requests, _settlements.Where(s => s.Seller == actor).OrderByDescending(s => s.Id).Take(12).ToArray());
    }
}
