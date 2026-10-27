namespace Simulation.Core;

public static partial class ScenarioValidation
{
    private static void ValidateLogisticsUpgrade(ScenarioDefinition s)
    {
        if (s.Logistics?.Upgrade is not { } upgrade) return;
        var settings = s.Logistics!;
        if (upgrade.PriceMinor <= 0 || upgrade.CargoCapacity < settings.CargoCapacity ||
            upgrade.MinimumCrew < settings.MinimumCrew || upgrade.CrewCapacity < settings.CrewCapacity ||
            upgrade.CrewCapacity < upgrade.MinimumCrew || upgrade.CrewCapacity > 1000 ||
            upgrade.Materials.IsDefault || upgrade.Materials.Length > s.Items.Length ||
            upgrade.Materials.Any(m => m is null || m.Quantity <= 0 || !s.Items.Any(i => i.Id == m.ItemId)) ||
            upgrade.Materials.Select(m => m.ItemId).Distinct(StringComparer.Ordinal).Count() != upgrade.Materials.Length)
            throw new ArgumentException("Invalid logistics upgrade");
        try
        {
            _ = checked(upgrade.CrewCapacity * settings.WageMinor *
                SimulationEngine.MaximumFreightTicks(s,upgrade.CargoCapacity,2));
            foreach (var m in upgrade.Materials) _ = checked(m.Quantity * s.Items.Single(i => i.Id == m.ItemId).UnitVolume);
        }
        catch (OverflowException ex) { throw new ArgumentException("Logistics upgrade arithmetic overflow", ex); }
    }
}

public sealed partial class SimulationEngine
{
    private void ValidateLogisticsInvariants()
    {
        var facilities=new HashSet<long>();var cargos=new HashSet<long>();var trips=new HashSet<long>();
        foreach(var l in _logistics.Values)
        {
            ValidateLogisticsQueue(l);
            if(l.Id<1000 || !facilities.Add(l.Enterprise) || !cargos.Add(l.Cargo) ||
                !_enterprises.TryGetValue(l.Enterprise,out var enterprise) || enterprise.Definition.OwnerId!=l.Owner || enterprise.Output!=l.Source ||
                l.Group.Id!=l.Id || l.Group.Owner!=l.Owner || l.Group.Role!=EmploymentRole.Logistics || l.Group.Capacity!=l.CrewCapacity ||
                !_economy.Inventories.TryGetValue(l.Cargo,out var cargo) || cargo.Owner!=l.Owner || cargo.Capacity!=l.CargoCapacity || l.Completed<0 ||
                l.Contract<0 || l.Destination<0 || l.InboundSource<0 || l.Contract!=0 && l.Destination!=0 ||
                l.Destination!=0 && !IsOwnLogisticsDestination(l,l.Destination) || l.InboundSource!=0 && !IsOwnLogisticsStorage(l.Owner,l.InboundSource) ||
                l.Contract!=0 && !_deliveryContracts.Any(d=>d.Id==l.Contract && d.Supplier.Value==l.Owner))throw new InvalidOperationException("Invalid logistics system");
            var home=_economy.Inventories[l.Source].Position;
            if(l.Trip is not {} t)
            {if(l.VehicleId==0 && (cargo.Stock.Count!=0 || cargo.Reservations.Count!=0 || cargo.Position!=home))throw new InvalidOperationException("Idle cargo retained");continue;}
            var f=t.Freight??throw new InvalidOperationException("Missing freight state");
            var job=l.Queue.Single(j=>j.Key==t.JobKey);
            long expectedSource=job.Source.Value!=0?job.Source.Value:job.InboundSource.Value!=0?job.InboundSource.Value:l.Source;
            long expectedDestination=job.Source.Value!=0?job.Destination.Value:job.InboundSource.Value!=0?enterprise.Input:job.OwnDestination.Value;
            if(f.GoodsSource.Value!=expectedSource || f.Destination.Value!=expectedDestination || t.OwnDestination!=f.Destination ||
                t.InboundSource.Value!=0 && t.InboundSource!=f.GoodsSource || job.Source.Value!=0 && t.ItemId!=job.ItemId)
                throw new InvalidOperationException("Freight does not match its committed queue job");
            var d=t.ContractId==0?null:_deliveryContracts.SingleOrDefault(d=>d.Id==t.ContractId && d.Supplier.Value==l.Owner);
            if(!trips.Add(t.Id)||t.Id<1000 || !_items.TryGetValue(t.ItemId,out var item) || t.Quantity<=0 ||
                t.Quantity>l.CargoCapacity/item.UnitVolume || t.PreparedTick<1 || t.PreparedTick>Tick || t.LoadTick<t.PreparedTick || t.ArrivalTick<t.LoadTick || t.EndTick<t.ArrivalTick ||
                t.StockHold!=$"logistics-stock:{t.Id}" || t.ReturnHold!=$"logistics-return:{t.Id}" || t.WageHold!=$"logistics-wage:{t.Id}" ||
                f.Loaded<0 || f.Received<0 || f.Returned<0 || f.Cancelled<0 || checked(f.Loaded+f.Cancelled)>t.Quantity || f.Cargo<0 ||
                f.WorkRemainder<0 || f.WorkRemainder>=item.UnitVolume || f.LastWorkedTick<0 || f.LastWorkedTick>Tick || f.StageStartedTick<t.PreparedTick || f.StageStartedTick>Tick ||
                t.CommittedWageMinor<=0 || t.CommittedWageMinor!=l.Group.Wage || t.WagesPaidMinor<0 || t.WagesPaidMinor>t.WageBudgetMinor ||
                t.State is not ("FetchingEmpty" or "Loading" or "Outbound" or "ReturningInput" or "Unloading" or "ReturningEmpty" or "ReturningCargo" or "UnloadingReturn" or "Finishing") ||
                !_economy.Inventories.TryGetValue(f.GoodsSource.Value,out var source) || source.Owner!=l.Owner ||
                (t.ContractId!=0 ? d is null || d.ItemId!=t.ItemId || t.OwnDestination.Value!=0 :
                    !_buildings.Values.Any(b=>b.Owner==l.Owner && b.Input==f.Destination.Value)) ||
                cargo.Position!=t.Position || t.Path.IsDefaultOrEmpty || t.PathIndex<0 || t.PathIndex>=t.Path.Length || t.Path[t.PathIndex]!=t.Position ||
                t.SegmentTicks<0 || t.SegmentTicks>=(t.PathIndex<t.Path.Length-1?FreightMoveTicks(l,t.Path[t.PathIndex],t.Path[t.PathIndex+1]):1))
                throw new InvalidOperationException("Invalid physical freight");
            if(t.State is "ReturningEmpty" or "Finishing" && (f.Cargo!=0 || FreightRemainingSource(t)!=0) ||
                t.State is "Outbound" or "ReturningInput" && (FreightRemainingSource(t)!=0 || f.Cargo<=0))
                throw new InvalidOperationException("Freight stage contradicts goods");
            var destination=d?.Position??_economy.Inventories[f.Destination.Value].Position;
            ValidateFreightPath(f.ToSource,l.VehicleId==0?home:f.ToSource[0],source.Position);ValidateFreightPath(f.ToDestination,source.Position,destination);
            ValidateFreightPath(f.ToHome,destination,home);ValidateFreightPath(f.ToGoodsSource,destination,source.Position);ValidateFreightPath(f.SourceToHome,source.Position,home);
            var expected=t.State switch {"FetchingEmpty"=>f.ToSource,"Loading"=>f.ToSource,"Outbound" or "ReturningInput" or "Unloading"=>f.ToDestination,
                "ReturningCargo"=>f.ToGoodsSource,_=>t.Path};
            if(!t.Path.SequenceEqual(expected) || t.Path.Any(c=>!_map.Walkable(c)) || t.State=="UnloadingReturn" && t.Position!=source.Position)throw new InvalidOperationException("Freight path mismatch");
            long volume=checked(t.Quantity*item.UnitVolume),handling=checked((volume-1)/(MinimumFreightHandling(l))+1);
            long approach=FreightTravelTicks(l,f.ToSource),delivery=FreightTravelTicks(l,f.ToDestination),returning=FreightTravelTicks(l,f.ToHome);
            long bound=checked(approach+delivery+(d is not null?Math.Max(returning,delivery+FreightTravelTicks(l,f.SourceToHome)):returning)+(d is not null?3:2)*handling);
            if(t.WageBudgetMinor!=checked(bound*l.CrewCapacity*t.CommittedWageMinor) ||
                l.Group.PayEffectiveTick is {} effective && effective<Math.Max(Tick+1,t.EndTick+1) ||
                !_events.Any(e=>e.Type=="LogisticsTripPrepared" && e.EntityId==t.Id && e.AmountMinor==t.WageBudgetMinor && e.Quantity==t.Quantity && e.ItemId==t.ItemId))
                throw new InvalidOperationException("Freight committed parameters mismatch");
            if(t.State is "ReturningCargo" or "UnloadingReturn" || f.Returned>0)
            {
                var rejected=_events.Where(e=>e.Type=="LogisticsDeliveryRejected" && e.EntityId==t.Id).ToArray();
                if(d is null || d.Status!="Expired" || rejected.Length!=1 || rejected[0].Quantity!=f.Loaded-f.Received || rejected[0].Tick<d.DeadlineTick || rejected[0].Tick>Tick)
                    throw new InvalidOperationException("Invalid late freight receipt");
            }
            if(_events.Where(e=>e.Type=="FreightUnitsReceived" && e.EntityId==t.Id).Sum(e=>e.Quantity)!=f.Received ||
                _events.Where(e=>e.Type=="LogisticsCargoReturned" && e.EntityId==t.Id).Sum(e=>e.Quantity)!=f.Returned)
                throw new InvalidOperationException("Freight receipt counters mismatch");
            if(_events.Where(e=>e.Type=="LogisticsCargoLoaded" && e.EntityId==t.Id).Sum(e=>e.Quantity)!=f.Loaded ||
                _events.Where(e=>e.Type=="LogisticsWagesPaid" && e.EntityId==t.Id).Sum(e=>e.AmountMinor)!=t.WagesPaidMinor)
                throw new InvalidOperationException("Freight history mismatch");
            CheckFreightHold(source.Reservations,t.StockHold,t.ItemId,FreightRemainingSource(t));
            CheckFreightHold(cargo.Reservations,t.CargoHold,t.ItemId,f.Cargo);
            if(cargo.Count(t.ItemId)!=f.Cargo || cargo.Stock.Count!=(f.Cargo>0?1:0) || cargo.CapacityHolds.Count!=0 ||
                source.CapacityHolds.GetValueOrDefault(t.ReturnHold)!=checked(f.Cargo*item.UnitVolume) ||
                _economy.Accounts[_actors[l.Owner].Account].Holds.GetValueOrDefault(t.WageHold)!=checked(t.WageBudgetMinor-t.WagesPaidMinor))
                throw new InvalidOperationException("Freight inventory or payroll mismatch");
            if(t.ContractId==0 && _economy.Inventories[f.Destination.Value].CapacityHolds.GetValueOrDefault(t.DestinationHold)!=
                checked((t.Quantity-f.Received-f.Cancelled)*item.UnitVolume))throw new InvalidOperationException("Freight destination hold mismatch");
        }
    }
    private void ValidateFreightPath(System.Collections.Immutable.ImmutableArray<Cell> path,Cell from,Cell to)
    {
        if(path.IsDefaultOrEmpty || path[0]!=from || path[^1]!=to || path.Any(c=>!_map.Walkable(c)) ||
            path.Zip(path.Skip(1)).Any(p=>Math.Abs(p.First.X-p.Second.X)+Math.Abs(p.First.Z-p.Second.Z)!=1))
            throw new InvalidOperationException("Invalid freight path");
    }
    private static void CheckFreightHold(Dictionary<string,StockReservation> holds,string key,string item,long quantity)
    {
        if(quantity==0?holds.ContainsKey(key):!holds.TryGetValue(key,out var h)||h!=new StockReservation(item,quantity))
            throw new InvalidOperationException("Freight stock hold mismatch");
    }
    private void AddExpectedLogisticsHolds(Dictionary<(long Inventory,string Hold),long> capacity,
        Dictionary<(long Inventory,string Hold),StockReservation> stock,Dictionary<(long Account,string Hold),long> money)
    {
        foreach(var l in _logistics.Values.Where(l=>l.Trip is not null))
        {
            var t=l.Trip!;var f=t.Freight!;long volume=_items[t.ItemId].UnitVolume,remaining=FreightRemainingSource(t);
            if(remaining>0)stock.Add((f.GoodsSource.Value,t.StockHold),new(t.ItemId,remaining));
            if(f.Cargo>0){stock.Add((l.Cargo,t.CargoHold),new(t.ItemId,f.Cargo));capacity.Add((f.GoodsSource.Value,t.ReturnHold),checked(f.Cargo*volume));}
            if(t.ContractId==0 && t.Quantity>f.Received+f.Cancelled)capacity.Add((f.Destination.Value,t.DestinationHold),checked((t.Quantity-f.Received-f.Cancelled)*volume));
            if(t.WageBudgetMinor>t.WagesPaidMinor)money.Add((_actors[l.Owner].Account,t.WageHold),t.WageBudgetMinor-t.WagesPaidMinor);
        }
    }
}
