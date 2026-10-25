using System.Collections.Immutable;

namespace Simulation.Core;

public sealed record ExpansionLand(PlotId Plot,Cell Position,long PriceMinor,ParticipantId? Owner);
public sealed record EnterpriseExpansionQuote(BuildingId Building,int CurrentLevel,int TargetLevel,int KnownTechnology,
    long PriceMinor,long Ticks,ImmutableArray<MaterialCost> Materials,ImmutableArray<DemolitionCell> Cells,ImmutableArray<ExpansionLand> Land,ImmutableArray<string> Blockers);

public sealed partial class SimulationEngine
{
    private readonly HashSet<long> _preparingDevelopment=[];
    private bool PreparingDevelopment(long id)=>_preparingDevelopment.Contains(id);
    private void PrepareDevelopment(ActorState actor,CommandEnvelope c,bool cancel=false)
    {
        if(Development is null||!_developmentBuildings.TryGetValue(c.Action.Building.Value,out var state)||state.Owner!=actor.Id||Demolished(state.Id))throw new RuleViolation("NotAvailable");
        if(cancel){_preparingDevelopment.Remove(state.Id);Emit("IndustryPreparationCancelled",actor.Id,state.Id,command:c.CommandId);return;}
        if(PreparingDemolition(state.Id))throw new RuleViolation("PreparingDemolition");if(StorageWorkAt(_buildings[state.Id].Position))throw new RuleViolation("ConstructionInProgress");
        _preparingDevelopment.Add(state.Id);foreach(var e in UnitsFor(state.Id))e.Enabled=e.Batch is not null;
        Emit("IndustryPreparationStarted",actor.Id,state.Id,command:c.CommandId);
    }
    private (long Id,long Owner)? ExpansionClaim(Cell cell)
    {
        if(Development is null)return null;
        foreach(var b in _developmentBuildings.Values.Where(b=>b.Claimed&&!Demolished(b.Id)).OrderBy(b=>b.Id))if(b.Contour.Contains(cell))return(b.Id,b.Owner);
        foreach(var c in _storageConstruction.Values.Where(c=>c.Development is not null).OrderBy(c=>c.Id))if(c.Development!.Contour.Contains(cell))return(c.BuildingId==0?c.Id:c.BuildingId,c.Owner);
        return null;
    }
    private ImmutableArray<Cell> DevelopmentContour(Cell anchor,BuildingTypeDefinition type)
    {
        if(_config.Layout is {} layout)
        {
            var lot=layout.IndustrialLots.SingleOrDefault(l=>l.Cells.Contains(anchor))??throw new RuleViolation("IndustrialContourRequired");
            var first=lot.Cells.OrderBy(c=>c.Z).ThenBy(c=>c.X).First();
            if(anchor!=first)throw new RuleViolation("BuildAtContourAnchor");
            return [..lot.Cells.OrderBy(c=>c.Z).ThenBy(c=>c.X)];
        }
        return [..ScenarioValidation.FootprintCells(anchor,type,type.Levels.Max(l=>l.Level)).OrderBy(c=>c.Z).ThenBy(c=>c.X)];
    }
    private void RequireDevelopmentContour(ImmutableArray<Cell> contour,long owner,long building=0)
    {
        foreach(var cell in contour)
        {
            if(!_plotsByCell.TryGetValue(cell,out var p)||p.Definition.IsCorridor||p.Definition.IsShopSite||p.Definition.ShopParticipantId.HasValue)throw new RuleViolation("InvalidDevelopmentContour");
            if(p.Owner is {} actual&&actual!=owner||p.ReservedBy is {} reserved&&reserved!=owner||p.Building is {} occupied&&occupied!=building)throw new RuleViolation("DevelopmentContourUnavailable");
            if(ExpansionClaim(cell) is {} claim&&claim.Id!=building)throw new RuleViolation("ExpansionRightHeld");
        }
    }
    private void InitializeDevelopmentBuildings()
    {
        if(Development is null)return;
        foreach(var b in _buildings.Values.Where(b=>b.Type.Kind==BuildingKind.Production).OrderBy(b=>b.Id))
        {
            var contour=DevelopmentContour(b.Position,b.Type);RequireDevelopmentContour(contour,b.Owner,b.Id);
            _developmentBuildings.Add(b.Id,new(b.Id,b.Owner,b.Type.Id,contour,[..b.Footprint.OrderBy(c=>c.Z).ThenBy(c=>c.X).Select(c=>new DemolitionCell(c,b.Level))]));
        }
    }
    private void BuyExpansionLand(ActorState actor,CommandEnvelope command)
    {
        if(Development is null||!_developmentBuildings.TryGetValue(command.Action.Building.Value,out var b)||b.Owner!=actor.Id||Demolished(b.Id)||
            !_plots.TryGetValue(command.Action.Plot.Value,out var plot)||!b.Contour.Contains(plot.Definition.Position)||plot.Building is not null)throw new RuleViolation("NotAvailable");
        BuyPlot(actor,command);
    }
    public EnterpriseExpansionQuote? QuoteExpansion(ParticipantId actor,BuildingId id)
    {
        if(Development is null||!_buildings.TryGetValue(id.Value,out var b)||b.Owner!=actor.Value||b.Type.Kind!=BuildingKind.Production||Demolished(b.Id))return null;
        var level=b.Type.Levels.SingleOrDefault(l=>l.Level==b.Level+1);if(level is null)return null;
        var blockers=new List<string>();int known=Experience(b.Owner,b.Type.Id).TechnologyLevel;
        if(known<level.Level)blockers.Add("IndustryTechnologyRequired");
        if(UnitsFor(b.Id).Any(e=>e.Enabled||e.Batch is not null)||_workshopInstallations.Values.Any(w=>w.Enterprise.Value==b.Id))blockers.Add("ProductionNotSafe");
        if(StorageWorkAt(b.Position))blockers.Add("ConstructionInProgress");if(PreparingDemolition(b.Id))blockers.Add("PreparingDemolition");
        var contour=_developmentBuildings[b.Id].Contour;
        try{RequireDevelopmentContour(contour,b.Owner,b.Id);}catch(RuleViolation e){blockers.Add(e.ReasonCode);}
        var cells=ScenarioValidation.FootprintCells(b.Position,b.Type,level.Level);
        var land=cells.Select(c=>_plotsByCell.GetValueOrDefault(c)).Where(p=>p is not null).Select(p=>new ExpansionLand(new(p!.Definition.Id),p.Definition.Position,p.Definition.PriceMinor,p.Owner is {} owner?new ParticipantId(owner):null)).ToImmutableArray();
        if(cells.Any(c=>!contour.Contains(c)||!_plotsByCell.TryGetValue(c,out var p)||p.Owner!=b.Owner))blockers.Add("LandNotOwned");
        if(level.Materials.Any(m=>_economy.Inventories[b.Input].Free(m.ItemId)<m.Quantity))blockers.Add("NoStock");
        if(_economy.Accounts[_actors[b.Owner].Account].Available<level.PriceMinor)blockers.Add("NoMoney");
        return new(id,b.Level,level.Level,known,level.PriceMinor,level.ConstructionTicks,[..level.Materials],
            [..cells.OrderBy(c=>c.Z).ThenBy(c=>c.X).Select(c=>new DemolitionCell(c,level.Level))],land,[..blockers.Distinct(StringComparer.Ordinal)]);
    }
    private void StartDevelopmentConstruction(ActorState actor,CommandEnvelope command,BuildingState? building=null)
    {
        if(Development is null)throw new RuleViolation("NotAvailable");
        RequireIdle(actor);RequireActionLocation(actor,command.Action);
        var type=building?.Type??_buildingTypes[command.Action.BuildingTypeId];var anchor=building?.Position??command.Action.Target;
        if(type.Kind!=BuildingKind.Production)throw new RuleViolation("NotAvailable");
        if(StorageWorkAt(anchor))throw new RuleViolation("ConstructionInProgress");
        var level=type.Levels.SingleOrDefault(l=>l.Level==(building?.Level??0)+1)??throw new RuleViolation("MaxLevel");
        if(building is not null&&command.Action.Quantity!=level.Level)throw new RuleViolation("StaleDevelopmentLevel");
        if(Experience(actor.Id,type.Id).TechnologyLevel<level.Level)throw new RuleViolation("IndustryTechnologyRequired");
        var contour=building is null?DevelopmentContour(anchor,type):_developmentBuildings[building.Id].Contour;
        RequireDevelopmentContour(contour,actor.Id,building?.Id??0);
        var cells=ScenarioValidation.FootprintCells(anchor,type,level.Level);
        foreach(var cell in cells)
        {
            if(!contour.Contains(cell)||!_plotsByCell.TryGetValue(cell,out var p)||p.Owner!=actor.Id)throw new RuleViolation("LandNotOwned");
            if(p.Building.HasValue&&p.Building!=building?.Id)throw new RuleViolation("LandOccupied");
            if(building is not null&&!building.Footprint.Contains(cell)&&_groundStorage.TryGetValue(p.Definition.Id,out var ground))
            {
                var i=_economy.Inventories[ground];
                if(i.Stock.Any(s=>s.Value>0)||i.Overflow.Count>0||i.Incoming>0||i.Reservations.Count>0||_transfers.Values.Any(t=>t.Source==ground||t.Destination==ground))throw new RuleViolation("GroundNotEmpty");
            }
        }
        long inventory=building?.Input??_groundStorage[_plotsByCell[anchor].Definition.Id];
        if(_transfers.Values.Any(t=>t.Source==inventory||t.Destination==inventory))throw new RuleViolation("StorageBusy");
        long id=_nextId,next=checked(id+1),end=checked(Tick+level.ConstructionTicks);var flows=PrepareMaterialFlows(level.Materials);
        var tx=_economy.Begin(Tick);foreach(var material in level.Materials)tx.RemoveFreeStock(inventory,material.ItemId,material.Quantity);
        if(building is not null)foreach(var cell in cells.Where(cell=>!building.Footprint.Contains(cell)))
            tx.ReserveCapacityVolume(_groundStorage[_plotsByCell[cell].Definition.Id],$"development:{id}",GroundRules.GroundCapacity);
        if(level.PriceMinor>0)tx.Pay(actor.Account,_treasury,level.PriceMinor,building is null?"Construction":"BuildingUpgrade");
        CheckBuildingAccess(anchor,type);tx.Commit();
        if(building is not null)_preparingDevelopment.Remove(building.Id);
        _nextId=next;var plan=new DevelopmentProject(contour,[..cells.OrderBy(c=>c.Z).ThenBy(c=>c.X).Select(c=>new DemolitionCell(c,level.Level))]);
        _storageConstruction.Add(id,new(id,actor.Id,anchor,type.Id,level.Level,inventory,building?.Id??0,Tick,end,level.Materials.ToArray(),level.PriceMinor,command.CommandId,plan));
        if(building is not null)_developmentBuildings[building.Id]=_developmentBuildings[building.Id] with{Claimed=true};
        foreach(var pair in flows)_flows[pair.Key].ConstructionInput=pair.Value;
        Emit("StorageConstructionStarted",actor.Id,id,money:level.PriceMinor,reason:end.ToString(System.Globalization.CultureInfo.InvariantCulture),command:command.CommandId);
        EmitMaterialConsumption(actor.Id,id,level.Materials,command.CommandId);
    }
    private void DetachDevelopmentGround(Cell cell)
    {
        if(!_groundStorage.Remove(_plotsByCell[cell].Definition.Id,out var ground))return;
        if(_buildings.Values.Any(b=>b.Input==ground||b.Output==ground)){var tx=_economy.Begin(Tick);tx.SetCapacity(ground,0);tx.Commit();}
        else _economy.Inventories.Remove(ground);
    }
    private void CompleteDevelopmentConstruction(StorageConstruction c)
    {
        var type=_buildingTypes[c.TypeId];var level=type.Levels.Single(l=>l.Level==c.Level);var plan=c.Development!;
        long? output=c.BuildingId==0?_nextId:_buildings[c.BuildingId].Output;
        long next=c.BuildingId==0?checked(_nextId+1):_nextId;
        var tx=_economy.Begin(Tick);tx.SetCapacity(c.InventoryId,level.InputCapacity);
        StageReleaseDevelopmentHolds(tx,c);
        if(c.BuildingId==0)tx.CreateInventory(output!.Value,c.Owner,c.Position,level.OutputCapacity);else tx.SetCapacity(output!.Value,level.OutputCapacity);
        tx.Commit();_nextId=next;
        BuildingState b;
        if(c.BuildingId==0)
        {
            b=new(c.Id,c.Owner,c.Position,type,c.Level,c.InventoryId,output,plan.Cells.Select(x=>x.Position).ToArray());_buildings.Add(b.Id,b);
            _groundStorage.Remove(_plotsByCell[c.Position].Definition.Id);AddProductionBuilding(b,level,0,false);
            Emit("BuildingConstructed",c.Owner,b.Id,reason:type.Id,command:c.CommandId);
        }
        else
        {
            b=_buildings[c.BuildingId];foreach(var cell in plan.Cells.Where(cell=>!b.Footprint.Contains(cell.Position)))DetachDevelopmentGround(cell.Position);
            b.Level=c.Level;b.Footprint=plan.Cells.Select(cell=>cell.Position).ToArray();
            foreach(var e in UnitsFor(b.Id))e.Definition=e.Definition with{Level=b.Level,InputCapacity=level.InputCapacity,OutputCapacity=level.OutputCapacity};
            Emit("BuildingUpgraded",c.Owner,b.Id,reason:c.Level.ToString(System.Globalization.CultureInfo.InvariantCulture),command:c.CommandId);
        }
        foreach(var cell in b.Footprint)_plotsByCell[cell].Building=b.Id;
        _developmentBuildings[b.Id]=new(b.Id,b.Owner,b.Type.Id,plan.Contour,plan.Cells);
        _storageConstruction.Remove(c.Id);Emit("StorageConstructionCompleted",c.Owner,c.Id,command:c.CommandId);ApplyBuildingObstacles();
    }
    private void ReleaseDevelopmentRights(long id)
    {_preparingDevelopment.Remove(id);if(_developmentBuildings.TryGetValue(id,out var b))_developmentBuildings[id]=b with{Claimed=false};}
    private DemolitionCell[] DemolitionCells(BuildingState b)=>_developmentBuildings.TryGetValue(b.Id,out var d)?d.Cells.ToArray():b.Footprint.OrderBy(c=>c.Z).ThenBy(c=>c.X).Select(c=>new DemolitionCell(c,b.Level)).ToArray();
    private void StageReleaseDevelopmentHolds(EconomyTransaction tx,StorageConstruction c)
    {
        if(c.Development is null||c.BuildingId==0)return;
        foreach(var cell in c.Development.Cells.Where(cell=>!_buildings[c.BuildingId].Footprint.Contains(cell.Position)))
            tx.ReleaseCapacity(_groundStorage[_plotsByCell[cell.Position].Definition.Id],$"development:{c.Id}");
    }
    private void AddExpectedDevelopmentHolds(Dictionary<(long Inventory,string Hold),long> capacity)
    {
        foreach(var c in _storageConstruction.Values.Where(c=>c.Development is not null&&c.BuildingId!=0))
            foreach(var cell in c.Development!.Cells.Where(cell=>!_buildings[c.BuildingId].Footprint.Contains(cell.Position)))
                capacity.Add((_groundStorage[_plotsByCell[cell.Position].Definition.Id],$"development:{c.Id}"),GroundRules.GroundCapacity);
    }
    private void ValidateDevelopmentBuildings()
    {
        if(Development is null)return;
        foreach(long id in _preparingDevelopment)if(!_developmentBuildings.ContainsKey(id)||Demolished(id)||StorageWorkAt(_buildings[id].Position))throw new CheckpointException("InvalidExpansionPreparation");
        foreach(var b in _buildings.Values.Where(b=>b.Type.Kind==BuildingKind.Production))
        {
            if(!_developmentBuildings.TryGetValue(b.Id,out var d)||d.Owner!=b.Owner||d.Industry!=b.Type.Id||d.Contour.IsDefaultOrEmpty||d.Cells.IsDefaultOrEmpty||
                d.Cells.Select(c=>c.Position).Distinct().Count()!=d.Cells.Length||!d.Contour.ToHashSet().SetEquals(DevelopmentContour(b.Position,b.Type))||!d.Cells.Select(c=>c.Position).ToHashSet().SetEquals(b.Footprint)||d.Cells.Any(c=>c.Level!=b.Level)||!b.Footprint.ToHashSet().SetEquals(ScenarioValidation.FootprintCells(b.Position,b.Type,b.Level))||d.Cells.Any(c=>!d.Contour.Contains(c.Position))||Demolished(b.Id)&&d.Claimed)
                throw new CheckpointException("InvalidDevelopmentBuilding");
            if(d.Claimed)RequireDevelopmentContour(d.Contour,b.Owner,b.Id);
        }
        if(_developmentBuildings.Keys.Any(id=>!_buildings.ContainsKey(id)))throw new CheckpointException("OrphanDevelopmentBuilding");
        foreach(var c in _storageConstruction.Values.Where(c=>c.Development is not null))
        {
            var plan=c.Development!;
            if(!plan.Cells.Select(x=>x.Position).ToHashSet().SetEquals(ScenarioValidation.FootprintCells(c.Position,_buildingTypes[c.TypeId],c.Level))||plan.Cells.Any(x=>x.Level!=c.Level||!plan.Contour.Contains(x.Position))||plan.Cells.Any(x=>!_plotsByCell.TryGetValue(x.Position,out var p)||p.Owner!=c.Owner))throw new CheckpointException("InvalidDevelopmentProject");
            RequireDevelopmentContour(plan.Contour,c.Owner,c.BuildingId==0?c.Id:c.BuildingId);
        }
    }
}
