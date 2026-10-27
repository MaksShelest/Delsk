namespace Simulation.Core;

public readonly record struct TravelState(int OffsetX, int OffsetZ, int HeadingX, int HeadingZ, int FacingX, int FacingZ);

public sealed partial class SimulationEngine
{
    public const int TravelUnitsPerCell = 1000;
    public const int TravelQuantaPerTick = 10;
    public const int TravelCenter = 500;
    private int _travelQuantum;
    public int TravelQuantum => _travelQuantum;

    public TravelState TravelOf(ParticipantId actor)
    {
        var state = _actors[actor.Value];
        return new(state.OffsetX, state.OffsetZ, state.HeadingX, state.HeadingZ, state.FacingX, state.FacingZ);
    }

    /// <summary>Курс применяется сразу и не ждёт фазы команды. Ноль останавливает.</summary>
    public void SetTravelHeading(ParticipantId actor, int x, int z)
    {
        if (WaitingForBallots) { if (x != 0 || z != 0) throw new InvalidOperationException("ElectionInProgress"); return; }
        if (_stepping) throw new InvalidOperationException("Step is not reentrant");
        if (x is < -1 or > 1 || z is < -1 or > 1) throw new ArgumentOutOfRangeException(nameof(x));
        if (!_actors.TryGetValue(actor.Value, out var state)) throw new ArgumentException("Participant not available");
        state.HeadingX = x;
        state.HeadingZ = z;
        if (DrivenVehicle(state.Id) is {} turning && (x!=0 || z!=0) && x==-turning.FacingX && z==-turning.FacingZ) { state.HeadingX=state.HeadingZ=0; SyncDrivenVehicle(state); return; }
        if (x != 0 || z != 0) { state.FacingX = x; state.FacingZ = z; }
        SyncDrivenVehicle(state);
    }

    /// <summary>Один квант текущего тика. true означает, что бюджет тика исчерпан и пора вызвать Step.</summary>
    public bool MoveTravelQuantum()
    {
        if (WaitingForBallots) return false;
        if (_stepping) throw new InvalidOperationException("Step is not reentrant");
        int distance = QuantumDistance();
        if (distance > 0)
            foreach (var actor in _actors.Values.OrderBy(a => a.Id))
                TravelActor(actor, distance);
        _travelQuantum++;
        if (_travelQuantum < TravelQuantaPerTick) return false;
        _travelQuantum = 0;
        return true;
    }

    internal object? TravelDeviation()
    {
        var actors = _actors.Values.OrderBy(a => a.Id)
            .Where(a => a.OffsetX != TravelCenter || a.OffsetZ != TravelCenter || a.HeadingX != 0 || a.HeadingZ != 0)
            .Select(a => new { a.Id, a.OffsetX, a.OffsetZ, a.HeadingX, a.HeadingZ })
            .ToArray();
        if (actors.Length == 0 && _travelQuantum == 0) return null;
        return new { Quantum = _travelQuantum, Actors = actors };
    }

    private int QuantumDistance()
    {
        int total = TravelUnitsPerCell / _config.MoveTicksPerCell;
        int share = total / TravelQuantaPerTick;
        int extra = total % TravelQuantaPerTick;
        return share + (_travelQuantum < extra ? 1 : 0);
    }

    private void TravelActor(ActorState actor, int distance)
    {
        if (actor.HeadingX == 0 && actor.HeadingZ == 0) return;
        if (DrivenVehicle(actor.Id) is {} vehicle && (vehicle.Installation is not null || VehicleTransferring(vehicle.CargoInventory.Value))) return;
        if (Fleet is not null) { var next = new Cell(actor.Position.X+actor.HeadingX,actor.Position.Z+actor.HeadingZ); int speed=FleetSpeed(actor.Position,next,DrivenVehicle(actor.Id) is {} v?Model(v.ModelId):null); distance=((_travelQuantum+1)*speed*TravelUnitsPerCell/(80*TravelQuantaPerTick))-(_travelQuantum*speed*TravelUnitsPerCell/(80*TravelQuantaPerTick)); }
        if ((actor.Job is not null && !IsHarvest(actor.Job)) || HasSale(actor.Id)) return;
        int step = actor.HeadingX != 0 && actor.HeadingZ != 0 ? checked(distance * 707 / 1000) : distance;
        var before = actor.Position;
        MoveAxis(actor, checked(actor.HeadingX * step), alongX: true);
        MoveAxis(actor, checked(actor.HeadingZ * step), alongX: false);
        SyncDrivenVehicle(actor);
        if (actor.Position != before)
        {
            _economy.Inventories[actor.Inventory].Position = actor.Position;
            int transferPhase=_phase;_phase=9;
            try { foreach(var t in _transfers.Values.Where(t=>t.Owner.Value==actor.Id).ToArray()) CancelTransfer(t,"LeftTransferLocation"); }
            finally { _phase=transferPhase; }
            if (actor.Job is { } movingJob && IsHarvest(movingJob) && _resourceClaims.TryGetValue(movingJob.Id, out var claim) && claim.Position != actor.Position &&
                _resources.TryGetValue((actor.Position,movingJob.Item), out var nextSite) && nextSite.Definition.ItemId == movingJob.Item &&
                movingJob.EquipmentId == 0 && ResourceAccess(actor.Id, nextSite) && nextSite.Stock - ResourceReserved(actor.Position,movingJob.Item) >= claim.Quantity)
                _resourceClaims[movingJob.Id] = claim with { Position = actor.Position };
            if (actor.Job is { } job && IsHarvest(job) &&
                (!_resourceClaims.TryGetValue(job.Id, out var activeClaim) || activeClaim.Position != actor.Position ||
                 job.EquipmentId != 0 && _equipment[job.EquipmentId].Position != actor.Position))
            {
                // Travel happens between committed ticks, including immediately after load.
                int previousPhase = _phase; _phase = 9;
                try { CancelHarvest(actor, "LeftResourceArea"); }
                finally { _phase = previousPhase; }
            }
        }
    }

    private void MoveAxis(ActorState actor, int delta, bool alongX)
    {
        if (delta == 0) return;
        int offset = alongX ? actor.OffsetX : actor.OffsetZ;
        int cell = alongX ? actor.Position.X : actor.Position.Z;
        int other = alongX ? actor.Position.Z : actor.Position.X;
        offset = checked(offset + delta);
        while (offset >= TravelUnitsPerCell)
        {
            int next = cell + 1;
            if (!_map.Walkable(alongX ? new Cell(next, other) : new Cell(other, next))) { offset = TravelUnitsPerCell - 1; break; }
            offset -= TravelUnitsPerCell;
            cell = next;
        }
        while (offset < 0)
        {
            int next = cell - 1;
            if (!_map.Walkable(alongX ? new Cell(next, other) : new Cell(other, next))) { offset = 0; break; }
            offset += TravelUnitsPerCell;
            cell = next;
        }
        if (alongX) { actor.OffsetX = offset; actor.Position = new Cell(cell, actor.Position.Z); }
        else { actor.OffsetZ = offset; actor.Position = new Cell(actor.Position.X, cell); }
    }
}
