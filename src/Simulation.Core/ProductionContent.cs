using System.Collections.Immutable;

namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private ProductionContentRules? Content=>_config.Economy?.ProductionContent;
    private readonly Dictionary<string,ImportStock> _imports=new(StringComparer.Ordinal);
    private readonly Dictionary<long,InventoryId> _importPickups=[];
    private readonly Dictionary<long,int> _workshopTechnologies=[];
    private readonly Dictionary<long,ModernizationProject> _modernizations=[];
    private Cell ImportTerminal=>_config.CapitalDemand!.Terminal;
    private readonly Dictionary<string,long> _importTotals=new(StringComparer.Ordinal);
    private long ImportQuantity(string item)=>_importTotals.GetValueOrDefault(item);
    private void IndexImports(){_importTotals.Clear();foreach(var group in _events.Where(e=>e.Type=="ImportPurchased").GroupBy(e=>e.ItemId,StringComparer.Ordinal))_importTotals.Add(group.Key,group.Sum(e=>e.Quantity));}
    private int RecipeVersion(string id)=>Content?.Versions.SingleOrDefault(v=>v.RecipeId==id)?.Version??1;
    private int WorkshopVersion(long id)=>_workshopTechnologies.GetValueOrDefault(id,1);
    private bool Modernizing(long id)=>_modernizations.Values.Any(p=>p.Workshop==id);
    private void RequireProductionItem(long inventory,string item)
    {
        if(Content is null)return;
        if(_importPickups.Values.Any(i=>i.Value==inventory))throw new RuleViolation("ImportPickupOnly");
        var building=_buildings.Values.SingleOrDefault(b=>b.Type.Kind==BuildingKind.Production&&!Demolished(b.Id)&&(b.Input==inventory||b.Output==inventory));
        if(building is null)return;
        var products=Development!.Industries.Single(i=>i.Id==building.Type.Id).Products;
        bool allowed=building.Output==inventory?products.Any(p=>p.ItemId==item):
            products.Select(p=>_config.Recipes.Single(r=>r.Id==p.RecipeId)).Any(r=>r.Uses(item))||
            building.Type.Levels.SelectMany(l=>l.Materials).Any(m=>m.ItemId==item)||building.Type.WorkshopOutfit!.Materials.Any(m=>m.ItemId==item)||Content.Modernizations.SelectMany(m=>m.Materials).Any(m=>m.ItemId==item);
        if(!allowed)throw new RuleViolation("InventoryItemNotAllowed");
    }
    private void InitializeProductionContent()
    {
        if(Content is null)return;
        foreach(var rule in Content.Imports)_imports.Add(rule.ItemId,new(rule.ItemId,rule.Package,rule.RefreshTicks));
    }
    public ProductionContentState? InspectProductionContent()=>Content is null?null:new([.._imports.Values.OrderBy(s=>s.ItemId,StringComparer.Ordinal)],
        [.._importPickups.OrderBy(p=>p.Key).Select(p=>new ImportPickup(new(p.Key),p.Value))],
        [.._workshopTechnologies.OrderBy(p=>p.Key).Select(p=>new WorkshopTechnology(p.Key,p.Value))],[.._modernizations.Values.OrderBy(p=>p.Id)]);
    public ProductionContentObservation? ProductionContentOf(ParticipantId actor)=>Content is null?null:new(InspectProductionContent()!.Imports,
        _importPickups.TryGetValue(actor.Value,out var pickup)?pickup:null,
        [.._workshopTechnologies.Where(p=>ProductionUnit(p.Key).Definition.OwnerId==actor.Value).OrderBy(p=>p.Key).Select(p=>new WorkshopTechnology(p.Key,p.Value))],
        [.._modernizations.Values.Where(p=>ProductionUnit(p.Workshop).Definition.OwnerId==actor.Value).OrderBy(p=>p.Id)],Content,ImportTerminal);
    private void BuyImport(ActorState actor,CommandEnvelope command)
    {
        if(Content is null)throw new RuleViolation("NotAvailable");
        RequireIdle(actor);RequireActionLocation(actor,command.Action);
        string item=command.Action.ItemId;long quantity=command.Action.Quantity;
        var rule=Content.Imports.SingleOrDefault(r=>r.ItemId==item)??throw new RuleViolation("NotAvailable");
        if(quantity<=0||quantity>_imports[item].Remaining)throw new RuleViolation("ImportStockUnavailable");
        long payment=checked(quantity*rule.UnitPriceMinor),next=_nextId;
        bool created=!_importPickups.TryGetValue(actor.Id,out var pickup);if(created)pickup=new(next++);
        // Ownership changes at the terminal. Handling and travel use the existing physical Transfer.
        long imported=checked(ImportQuantity(item)+quantity);
        _=checked(_flows[item].Initial+_flows[item].Harvested+_flows[item].Produced+imported);
        var tx=_economy.Begin(Tick);
        if(created)tx.CreateInventory(pickup.Value,actor.Id,ImportTerminal,1000000000);
        tx.DebitExternal(actor.Account,payment,"ImportPurchase");tx.AddStock(pickup.Value,item,quantity);tx.Commit();
        _nextId=next;if(created)_importPickups.Add(actor.Id,pickup);
        _importTotals[item]=imported;
        _imports[item]=_imports[item] with{Remaining=_imports[item].Remaining-quantity};
        Emit("ImportPurchased",actor.Id,pickup.Value,item,quantity,payment,command:command.CommandId);
    }
    private void ModernizeWorkshop(ActorState actor,CommandEnvelope command)
    {
        var unit=OwnProductionUnit(actor,command.Action);long parent=ProductionParent(unit.Definition.Id);
        if(Content is null||!_buildings.TryGetValue(parent,out var building))throw new RuleViolation("NotAvailable");
        RequireIdle(actor);RequireActionLocation(actor,command.Action);
        if(Modernizing(unit.Definition.Id)||StorageWorkAt(building.Position))throw new RuleViolation("ConstructionInProgress");
        if(PreparingDevelopment(parent))throw new RuleViolation("PreparingDevelopment");
        if(unit.Enabled||unit.Batch is not null)throw new RuleViolation("ProductionNotSafe");
        int version=checked((int)command.Action.Quantity);
        if(version!=WorkshopVersion(unit.Definition.Id)+1)throw new RuleViolation("InvalidRecipeVersion");
        var spec=Content.Modernizations.SingleOrDefault(m=>m.Version==version)??throw new RuleViolation("InvalidRecipeVersion");
        int technology=Content.Versions.Where(v=>v.Version==version).Select(v=>v.TechnologyLevel).Distinct().Single();
        if(Experience(actor.Id,building.Type.Id).TechnologyLevel<technology)throw new RuleViolation("RecipeLocked");
        long id=_nextId,next=checked(id+1),end=checked(Tick+spec.Ticks);
        var flows=PrepareMaterialFlows(spec.Materials.ToArray());var tx=_economy.Begin(Tick);
        foreach(var m in spec.Materials)tx.RemoveFreeStock(building.Input,m.ItemId,m.Quantity);
        tx.Pay(actor.Account,_treasury,spec.PriceMinor,"WorkshopModernization");tx.Commit();
        _nextId=next;_modernizations.Add(id,new(id,parent,unit.Definition.Id,version,Tick,end,command.CommandId));
        foreach(var f in flows)_flows[f.Key].ConstructionInput=f.Value;
        Emit("WorkshopModernizationStarted",actor.Id,parent,quantity:id,money:spec.PriceMinor,reason:unit.Definition.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),command:command.CommandId);
        EmitMaterialConsumption(actor.Id,parent,spec.Materials.ToArray(),command.CommandId);
    }
    private void AdvanceProductionContent()
    {
        if(Content is null)return;
        foreach(var rule in Content.Imports)
        {
            var stock=_imports[rule.ItemId];if(Tick<stock.NextRefreshTick)continue;
            long remaining=Math.Min(checked(rule.Package*2),checked(stock.Remaining+rule.Package));
            _imports[rule.ItemId]=stock with{Remaining=remaining,NextRefreshTick=checked(stock.NextRefreshTick+rule.RefreshTicks)};
            Emit("ImportRestocked",item:rule.ItemId,quantity:remaining-stock.Remaining);
        }
        foreach(var p in _modernizations.Values.Where(p=>p.EndTick<=Tick).OrderBy(p=>p.Id).ToArray())
        {
            _workshopTechnologies[p.Workshop]=p.Version;_modernizations.Remove(p.Id);
            Emit("WorkshopModernizationCompleted",ProductionUnit(p.Workshop).Definition.OwnerId,p.Enterprise,quantity:p.Version,reason:p.Workshop.ToString(System.Globalization.CultureInfo.InvariantCulture),command:p.CommandId);
        }
    }
    private void RestoreProductionContent(ProductionContentState? saved)
    {
        if((Content is null)!=(saved is null))throw new CheckpointException("MissingProductionContentState");
        if(saved is null)return;_imports.Clear();
        foreach(var s in saved.Imports)_imports.Add(s.ItemId,s);
        foreach(var p in saved.Pickups)_importPickups.Add(p.Owner.Value,p.Inventory);
        foreach(var t in saved.Technologies)_workshopTechnologies.Add(t.Workshop,t.Version);
        foreach(var p in saved.Projects)_modernizations.Add(p.Id,p);
    }
    private void ValidateProductionContent()
    {
        if(Content is null)return;
        if(_imports.Count!=Content.Imports.Length)throw new CheckpointException("InvalidImportStock");
        foreach(var rule in Content.Imports)
        {
            long remaining=rule.Package,next=rule.RefreshTicks;
            foreach(var e in _events.Where(e=>e.ItemId==rule.ItemId&&e.Type is "ImportPurchased" or "ImportRestocked"))
            {
                if(e.Type=="ImportRestocked")
                {if(e.Tick!=next||e.Phase!=0||e.Quantity!=Math.Min(rule.Package,rule.Package*2-remaining))throw new CheckpointException("InvalidImportRefresh");remaining+=e.Quantity;next=checked(next+rule.RefreshTicks);}
                else
                {
                    if(e.Quantity<=0||e.Quantity>remaining||e.AmountMinor!=checked(e.Quantity*rule.UnitPriceMinor)||e.ActorId is not {} owner||
                        !_importPickups.TryGetValue(owner.Value,out var pickup)||pickup.Value!=e.EntityId||e.CommandId is not {} id||!_seenCommands.TryGetValue(id,out var command)||
                        command.ActorId!=owner||command.Action.Kind!=ActionKind.BuyImport||command.Action.ItemId!=rule.ItemId||command.Action.Quantity!=e.Quantity||
                        !_results.TryGetValue(id,out var result)||result.Status!=ResultStatus.Accepted||result.AppliedTick!=e.Tick)throw new CheckpointException("InvalidImportReceipt");
                    remaining-=e.Quantity;
                }
            }
            if(_imports.GetValueOrDefault(rule.ItemId)!=new ImportStock(rule.ItemId,remaining,next)||next<=Tick)throw new CheckpointException("InvalidImportStock");
        }
        foreach(var p in _importPickups)
            if(!_actors.ContainsKey(p.Key)||!_economy.Inventories.TryGetValue(p.Value.Value,out var inv)||inv.Owner!=p.Key||inv.Position!=ImportTerminal||
                inv.Capacity!=1000000000||!_events.Any(e=>e.Type=="ImportPurchased"&&e.ActorId?.Value==p.Key&&e.EntityId==p.Value.Value))throw new CheckpointException("InvalidImportPickup");
        if(_modernizations.Values.GroupBy(p=>p.Workshop).Any(g=>g.Count()!=1))throw new CheckpointException("DuplicateModernization");
        foreach(var p in _modernizations.Values)
        {
            var spec=Content.Modernizations.SingleOrDefault(m=>m.Version==p.Version);
            if(spec is null||!_buildings.TryGetValue(p.Enterprise,out var b)||Demolished(b.Id)||ProductionParent(p.Workshop)!=b.Id||
                p.Version!=WorkshopVersion(p.Workshop)+1||p.StartedTick<1||p.StartedTick>Tick||p.EndTick!=checked(p.StartedTick+spec.Ticks)||p.EndTick<=Tick||
                !_events.Any(e=>e.Type=="WorkshopModernizationStarted"&&e.EntityId==b.Id&&e.Quantity==p.Id&&e.CommandId==p.CommandId&&e.AmountMinor==spec.PriceMinor))throw new CheckpointException("InvalidModernizationProject");
        }
        foreach(var t in _workshopTechnologies)
            if(t.Value is <2 or >3||!ProductionUnits().Any(u=>u.Definition.Id==t.Key)||
                !_events.Any(e=>e.Type=="WorkshopModernizationCompleted"&&e.ReasonCode==t.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)&&e.Quantity==t.Value))throw new CheckpointException("UnpaidWorkshopTechnology");
        var completed=new Dictionary<long,int>();var pending=new Dictionary<long,ModernizationProject>();
        foreach(var e in _events.Where(e=>e.Type is "WorkshopModernizationStarted" or "WorkshopModernizationCompleted"))
        {
            if(e.CommandId is not {} id||!_seenCommands.TryGetValue(id,out var command)||command.Action.Kind!=ActionKind.ModernizeWorkshop||
                !_results.TryGetValue(id,out var result)||result.Status!=ResultStatus.Accepted||e.ActorId!=command.ActorId||command.Action.Enterprise.Value!=e.EntityId||
                !_buildings.TryGetValue(e.EntityId,out var b)||b.Owner!=command.ActorId.Value)throw new CheckpointException("InvalidModernizationReceipt");
            long workshop=command.Action.WorkshopId==0?b.Id:command.Action.WorkshopId;int version=checked((int)command.Action.Quantity);
            var spec=Content.Modernizations.SingleOrDefault(m=>m.Version==version);
            if(spec is null||ProductionParent(workshop)!=b.Id||e.ReasonCode!=workshop.ToString(System.Globalization.CultureInfo.InvariantCulture))throw new CheckpointException("InvalidModernizationReceipt");
            if(e.Type=="WorkshopModernizationStarted")
            {
                long known=_experienceGrants.Where(g=>g.Owner==b.Owner&&g.Industry==b.Type.Id&&g.Tick<e.Tick).Sum(g=>g.Xp)+_researchModifications.Where(m=>m.Kind=="IndustryExperience"&&m.Actor.Value==b.Owner&&m.Item==b.Type.Id&&m.Tick<e.Tick).Sum(m=>m.NewValue-m.OldValue);
                var materials=_events.Where(m=>m.Type=="ConstructionMaterialConsumed"&&m.CommandId==id).Select(m=>new MaterialCost(m.ItemId,m.Quantity)).OrderBy(m=>m.ItemId,StringComparer.Ordinal);
                if(e.Tick!=result.AppliedTick||e.Phase!=1||e.AmountMinor!=spec.PriceMinor||e.Quantity<1000||version!=completed.GetValueOrDefault(workshop,1)+1||pending.ContainsKey(workshop)||
                    DevelopmentRules.Technology(known)<(version==2?4:5)||!materials.SequenceEqual(spec.Materials.OrderBy(m=>m.ItemId,StringComparer.Ordinal)))throw new CheckpointException("InvalidModernizationInvestment");
                pending.Add(workshop,new(e.Quantity,b.Id,workshop,version,e.Tick,checked(e.Tick+spec.Ticks),id));
            }
            else
            {
                if(!pending.Remove(workshop,out var project)||e.Tick!=project.EndTick||e.Phase!=0||e.Quantity!=version)throw new CheckpointException("InvalidModernizationCompletion");
                completed[workshop]=version;
            }
        }
        if(!completed.OrderBy(t=>t.Key).SequenceEqual(_workshopTechnologies.OrderBy(t=>t.Key))||!pending.Values.OrderBy(p=>p.Id).SequenceEqual(_modernizations.Values.OrderBy(p=>p.Id))||
            _events.Where(e=>e.Type=="ImportPurchased").Sum(e=>e.AmountMinor)!=_economy.Ledger.Where(l=>l.Reason=="ImportPurchase").Sum(l=>l.AmountMinor))throw new CheckpointException("ProductionContentReceiptMismatch");
        foreach(var unit in ProductionUnits())if(RecipeVersion(unit.Recipe.Id)>WorkshopVersion(unit.Definition.Id))throw new CheckpointException("WorkshopVersionLocked");
    }
}
