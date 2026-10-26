using System.Collections.Immutable;

namespace Simulation.Core;

public sealed partial class SimulationEngine
{
    private DevelopmentRules? Development=>_config.Economy?.Development;
    private readonly List<ExperienceGrant> _experienceGrants=[];
    private readonly HashSet<(string Source,long Id)> _experienceGrantKeys=[];
    private readonly Dictionary<(long Owner,string Industry),IndustryExperience> _industryExperience=[];
    private readonly Dictionary<long,IndustryQuest> _industryQuests=[];
    private readonly Dictionary<long,DevelopmentBuilding> _developmentBuildings=[];
    private IndustryExperience Experience(long owner,string industry)
    {
        var earned=_industryExperience.GetValueOrDefault((owner,industry))??new(new(owner),industry,0,0);
        return earned with{ResearchXp=_researchModifications.Where(m=>m.Kind=="IndustryExperience"&&m.Actor.Value==owner&&m.Item==industry).Sum(m=>checked(m.NewValue-m.OldValue))};
    }
    private ExperienceGrant? PrepareProductionExperience(EnterpriseState unit,long batch)
    {
        var industry=Development?.Industries.SingleOrDefault(i=>i.Id==unit.Definition.BuildingTypeId);
        if(industry is null)return null;
        var product=industry.Products.Single(p=>p.RecipeId==unit.Recipe.Id);
        long xp=checked(product.XpPerUnit*unit.Recipe.OutputQuantity);var current=Experience(unit.Definition.OwnerId,industry.Id);_=checked(current.Total+xp);
        return new(unit.Definition.OwnerId,industry.Id,"Production",batch,unit.Recipe.OutputItem,unit.Recipe.OutputQuantity,xp,Tick);
    }
    private void GrantExperience(ExperienceGrant grant)
    {
        if(!_experienceGrantKeys.Add((grant.Source,grant.SourceId)))throw new InvalidOperationException("Duplicate experience grant");
        var e=Experience(grant.Owner,grant.Industry);
        _industryExperience[(grant.Owner,grant.Industry)]=grant.Source=="Production"?e with{ProductionXp=checked(e.ProductionXp+grant.Xp)}:e with{QuestXp=checked(e.QuestXp+grant.Xp)};
        _experienceGrants.Add(grant);
        Emit("IndustryExperienceEarned",grant.Owner,grant.SourceId,grant.Industry,grant.Xp,reason:grant.Source);
    }
    private bool IndustryRecipeAvailable(string industry,string recipe,int technology)
    {
        var r=Development?.Industries.SingleOrDefault(i=>i.Id==industry)?.Products.SingleOrDefault(p=>p.RecipeId==recipe);
        return r is not null&&r.TechnologyLevel<=technology&&_buildingTypes.TryGetValue(industry,out var type)&&type.Levels.Any(l=>l.UnlockedRecipes.Contains(recipe,StringComparer.Ordinal));
    }
    private ImmutableArray<IndustryQuestChoice> QuestChoices(long owner)
    {
        if(Development is null)return [];
        long review=Tick/Development.QuestReviewTicks*Development.QuestReviewTicks;
        var choices=new List<IndustryQuestChoice>();
        foreach(var contract in _deliveryContracts.Where(d=>d.Supplier.Value==owner&&d.ClosedTick is null&&d.DeadlineTick>Tick).OrderBy(d=>d.Id))
        {
            if(_industryQuests.Values.Any(q=>q.Owner.Value==owner&&q.ContractId==contract.Id))continue;
            foreach(var industry in Development.Industries.OrderBy(i=>i.Id,StringComparer.Ordinal))
            {
                var product=industry.Products.Where(p=>p.ItemId==contract.ItemId&&IndustryRecipeAvailable(industry.Id,p.RecipeId,Experience(owner,industry.Id).TechnologyLevel)).OrderBy(p=>p.Quantity).ThenBy(p=>p.RecipeId,StringComparer.Ordinal).FirstOrDefault();
                if(product is null)continue;
                bool first=!_industryQuests.Values.Any(q=>q.Owner.Value==owner&&q.Industry==industry.Id&&q.Kind=="FirstDelivery"&&q.Status is "Active" or "Completed");
                foreach(var (batches,reward) in new[]{(5,50L),(15,100L),(30,200L),(60,500L)})
                {
                    long quantity=checked(product.Quantity*batches);
                    if(quantity>contract.Quantity-contract.DeliveredQuantity)continue;
                    if(batches==60&&(Development.PublicProjectOffers.IsDefaultOrEmpty||!Development.PublicProjectOffers.Contains(contract.CapitalOfferId)||contract.DeadlineTick-Tick<3600))continue;
                    choices.Add(new(industry.Id,first&&batches==5?"FirstDelivery":batches==60?"CityProject":"Delivery",contract.Id,product.RecipeId,product.ItemId,quantity,reward,contract.DeadlineTick,batches,first&&batches==5,review,Math.Min(contract.DeadlineTick,checked(review+Development.QuestReviewTicks)),checked((int)(Tick/Development.QuestReviewTicks+1))));
                }
            }
        }
        return [..choices];
    }
    private void AcceptIndustryQuest(ActorState actor,CommandEnvelope command)
    {
        if(Development is null)throw new RuleViolation("NotAvailable");
        if(command.Action.OfferWindow!=Tick/Development.QuestReviewTicks+1)throw new RuleViolation("StaleQuestReview");
        if(_industryQuests.Values.Count(q=>q.Owner.Value==actor.Id&&q.Status=="Active")>=Development.QuestSlots)throw new RuleViolation("QuestSlotsFull");
        var c=QuestChoices(actor.Id).FirstOrDefault(q=>q.ContractId==command.Action.ContractId&&q.Industry==command.Action.BuildingTypeId&&q.Batches==command.Action.Quantity)
            ??throw new RuleViolation("QuestNotAvailable");
        var contract=_deliveryContracts.Single(d=>d.Id==c.ContractId);long id=_nextId,next=checked(id+1);
        var quest=new IndustryQuest(id,new(actor.Id),c.Industry,c.Kind,c.ContractId,c.RecipeId,c.ItemId,c.Quantity,c.Reward,Tick,c.DeadlineTick,contract.DeliveredQuantity,0,"Active");
        _industryQuests.Add(id,quest);_nextId=next;Emit("IndustryQuestAccepted",actor.Id,id,c.Industry,c.Quantity,reason:c.Kind,command:command.CommandId);
    }
    private void AbandonIndustryQuest(ActorState actor,CommandEnvelope command)
    {
        if(!_industryQuests.TryGetValue(command.Action.OfferId,out var q)||q.Owner.Value!=actor.Id||q.Status!="Active")throw new RuleViolation("NotAvailable");
        _industryQuests[q.Id]=q with{Status="Abandoned",ClosedTick=Tick};Emit("IndustryQuestAbandoned",actor.Id,q.Id,command:command.CommandId);
    }
    private void AdvanceIndustryQuests()
    {
        foreach(var original in _industryQuests.Values.Where(q=>q.Status=="Active").OrderBy(q=>q.Id).ToArray())
        {
            var d=_deliveryContracts.Single(d=>d.Id==original.ContractId);long delivered=Math.Min(original.Quantity,checked(d.DeliveredQuantity-original.BaselineDelivered));
            var q=original with{Delivered=delivered};
            if(delivered==q.Quantity)
            {
                _=checked(Experience(q.Owner.Value,q.Industry).Total+q.Reward);
                q=q with{Status="Completed",ClosedTick=Tick};
                GrantExperience(new(q.Owner.Value,q.Industry,"Quest",q.Id,q.ItemId,q.Quantity,q.Reward,Tick));Emit("IndustryQuestCompleted",q.Owner.Value,q.Id,q.Industry,q.Reward);
            }
            else if(d.ClosedTick is not null||Tick>=q.DeadlineTick){q=q with{Status="Expired",ClosedTick=Tick};Emit("IndustryQuestExpired",q.Owner.Value,q.Id);}
            _industryQuests[q.Id]=q;
        }
    }
    public DevelopmentObservation? DevelopmentOf(ParticipantId actor)
    {
        if(Development is null||!_actors.ContainsKey(actor.Value))return null;
        return new([..Development.Industries.OrderBy(i=>i.Id,StringComparer.Ordinal).Select(i=>Experience(actor.Value,i.Id))],
            [.._industryQuests.Values.Where(q=>q.Owner==actor).OrderBy(q=>q.Id)],QuestChoices(actor.Value),
            [.._developmentBuildings.Values.Where(b=>b.Owner==actor.Value&&!Demolished(b.Id)).OrderBy(b=>b.Id)],
            [.._storageConstruction.Values.Where(c=>c.Owner==actor.Value&&c.Development is not null).OrderBy(c=>c.Id).Select(c=>new DevelopmentProjectView(c.Id,c.BuildingId,c.Position,c.TypeId,c.Level,c.InventoryId,c.StartedTick,c.CompletesTick,c.PaidMinor,[..c.Materials],c.Development!))],Development,Tick/Development.QuestReviewTicks*Development.QuestReviewTicks,checked((Tick/Development.QuestReviewTicks+1)*Development.QuestReviewTicks),[.._preparingDevelopment.Where(id=>_buildings[id].Owner==actor.Value).Order()],_config.Layout is {} layout?[..layout.IndustrialLots.Select(l=>l.Cells.OrderBy(c=>c.Z).ThenBy(c=>c.X).First()).OrderBy(c=>c.Z).ThenBy(c=>c.X)]:[]);
    }
    public DevelopmentState? InspectDevelopment()=>Development is null?null:new([.._experienceGrants],
        [.._industryQuests.Values.OrderBy(q=>q.Id)],[.._developmentBuildings.Values.OrderBy(b=>b.Id)],[.._preparingDevelopment.Order()]);
    private void RestoreDevelopment(DevelopmentState? state)
    {
        if((Development is null)!=(state is null))throw new CheckpointException("MissingDevelopmentState");
        if(state is null)return;
        _experienceGrants.Clear();_experienceGrantKeys.Clear();_industryExperience.Clear();_industryQuests.Clear();_developmentBuildings.Clear();
        foreach(var g in state.Grants){if(!_experienceGrantKeys.Add((g.Source,g.SourceId)))throw new CheckpointException("DuplicateExperienceGrant");var e=Experience(g.Owner,g.Industry);_industryExperience[(g.Owner,g.Industry)]=g.Source=="Production"?e with{ProductionXp=checked(e.ProductionXp+g.Xp)}:e with{QuestXp=checked(e.QuestXp+g.Xp)};_experienceGrants.Add(g);}
        foreach(var q in state.Quests)_industryQuests.Add(q.Id,q);
        foreach(var b in state.Buildings)_developmentBuildings.Add(b.Id,b);
        _preparingDevelopment.Clear();foreach(long id in state.Preparing.IsDefault?[]:state.Preparing)_preparingDevelopment.Add(id);
    }
    private void ValidateIndustryDevelopment()
    {
        if(Development is null){if(_experienceGrants.Count+_industryQuests.Count+_developmentBuildings.Count!=0)throw new CheckpointException("UnexpectedDevelopmentState");return;}
        if(_stepping)return; // Append-only grants are checked fully at capture/restore, not events×history each tick.
        if(Development.Industries.Any(i=>_actors.Keys.Any(owner=>Experience(owner,i.Id).Total<0)))throw new CheckpointException("InvalidIndustryExperience");
        var production=_events.Where(e=>e.Type=="ProductionCompleted").ToLookup(e=>e.EntityId);
        var receipts=_events.Where(e=>e.Type=="DeliveryReceived").ToLookup(e=>e.EntityId);
        var accepted=_events.Where(e=>e.Type=="IndustryQuestAccepted").ToLookup(e=>e.EntityId);
        var contracts=_deliveryContracts.ToDictionary(d=>d.Id);
        var rewarded=_experienceGrants.Where(g=>g.Source=="Quest").Select(g=>g.SourceId).ToHashSet();
        if(_experienceGrants.Count(g=>g.Source=="Production")!=production.Sum(g=>g.Count()))throw new CheckpointException("MissingProductionExperience");
        if(_experienceGrants.GroupBy(g=>(g.Source,g.SourceId)).Any(g=>g.Count()!=1))throw new CheckpointException("DuplicateExperienceGrant");
        foreach(var grant in _experienceGrants)
        {
            if(!_actors.ContainsKey(grant.Owner)||!Development.Industries.Any(i=>i.Id==grant.Industry)||grant.Tick<1||grant.Tick>Tick||grant.Xp<=0)throw new CheckpointException("InvalidExperienceGrant");
            if(grant.Source=="Production")
            {
                var p=Development.Industries.Single(i=>i.Id==grant.Industry).Products.Where(p=>p.ItemId==grant.ItemId).FirstOrDefault();
                if(p is null||grant.Xp!=checked(p.XpPerUnit*grant.Quantity)||!production[grant.SourceId].Any(e=>e.ActorId?.Value==grant.Owner&&e.ItemId==grant.ItemId&&e.Quantity==grant.Quantity&&e.Tick==grant.Tick))throw new CheckpointException("InvalidProductionExperience");
            }
            else if(grant.Source!="Quest"||!_industryQuests.TryGetValue(grant.SourceId,out var q)||q.Status!="Completed"||q.Reward!=grant.Xp||q.Owner.Value!=grant.Owner||q.Industry!=grant.Industry||q.ClosedTick!=grant.Tick)throw new CheckpointException("InvalidQuestExperience");
        }
        if(_industryQuests.Values.GroupBy(q=>(q.Owner,q.ContractId)).Any(g=>g.Count()!=1)||_industryQuests.Values.Where(q=>q.Kind=="FirstDelivery"&&q.Status=="Completed").GroupBy(q=>(q.Owner,q.Industry)).Any(g=>g.Count()!=1))throw new CheckpointException("DuplicateQuestReward");
        foreach(var q in _industryQuests.Values)
        {
            var d=contracts.GetValueOrDefault(q.ContractId);int batches=q.Reward switch{50=>5,100=>15,200=>30,500=>60,_=>0};
            var p=Development.Industries.SingleOrDefault(i=>i.Id==q.Industry)?.Products.SingleOrDefault(p=>p.RecipeId==q.RecipeId);
            if(d is null||d.Supplier!=q.Owner||d.ItemId!=q.ItemId||p is null||batches==0||q.Quantity!=checked(p.Quantity*batches)||q.AcceptedTick<1||q.AcceptedTick>Tick||q.DeadlineTick!=d.DeadlineTick||q.BaselineDelivered<0||q.Delivered<0||q.Delivered>q.Quantity||q.Delivered>d.DeliveredQuantity-q.BaselineDelivered||q.Status is not ("Active" or "Completed" or "Expired" or "Abandoned")||
                (q.Status=="Active"?(q.ClosedTick is not null||q.DeadlineTick<=Tick):(q.ClosedTick is null||q.ClosedTick<q.AcceptedTick||q.ClosedTick>Tick))||q.Status=="Completed"&&(q.Delivered!=q.Quantity||!rewarded.Contains(q.Id)))throw new CheckpointException("InvalidIndustryQuest");
            long before=receipts[q.ContractId].Where(e=>e.Tick<q.AcceptedTick).Sum(e=>e.Quantity);
            long cut=q.Status=="Abandoned"?q.ClosedTick!.Value-1:q.ClosedTick??Tick;
            long received=receipts[q.ContractId].Where(e=>e.Tick>=q.AcceptedTick&&e.Tick<=cut).Sum(e=>e.Quantity);
            long known=_experienceGrants.Where(g=>g.Owner==q.Owner.Value&&g.Industry==q.Industry&&g.Tick<q.AcceptedTick).Sum(g=>g.Xp)+_researchModifications.Where(m=>m.Kind=="IndustryExperience"&&m.Actor==q.Owner&&m.Item==q.Industry&&m.Tick<q.AcceptedTick).Sum(m=>m.NewValue-m.OldValue);
            if(q.BaselineDelivered!=before||q.Delivered!=Math.Min(q.Quantity,received)||!IndustryRecipeAvailable(q.Industry,q.RecipeId,DevelopmentRules.Technology(known))||
                !accepted[q.Id].Any(e=>e.Tick==q.AcceptedTick&&e.ActorId==q.Owner&&e.ItemId==q.Industry&&e.Quantity==q.Quantity&&e.ReasonCode==q.Kind)||
                q.Kind is not ("Delivery" or "FirstDelivery" or "CityProject")||q.Kind=="FirstDelivery"&&batches!=5||
                batches==60&&(q.Kind!="CityProject"||Development.PublicProjectOffers.IsDefaultOrEmpty||!Development.PublicProjectOffers.Contains(d.CapitalOfferId)||q.DeadlineTick-q.AcceptedTick<3600))throw new CheckpointException("InvalidQuestReceiptProof");
        }
        foreach(var owner in _actors.Keys)if(_industryQuests.Values.Count(q=>q.Owner.Value==owner&&q.Status=="Active")>Development.QuestSlots)throw new CheckpointException("TooManyIndustryQuests");
    }
}

public static partial class ScenarioValidation
{
    private static void ValidateDevelopmentContent(ScenarioDefinition s)
    {
        if(s.Economy?.Development is not {} rules){if((s.BuildingTypes??[]).Any(t=>t.Levels.Any(l=>!l.Footprint.IsDefault)))throw new ArgumentException("Level footprints require development rules");return;}
        if(s.Economy.Workshops is null||s.Economy.Storage is null||s.Enterprises.Length!=0)throw new ArgumentException("Development requires workshop/ground rules and physical buildings");
        foreach(var type in (s.BuildingTypes??[]).Where(t=>t.Kind==BuildingKind.Production))
            if(!rules.Industries.Any(i=>i.Id==type.Id))throw new ArgumentException("Unregistered development industry");
        foreach(var industry in rules.Industries)
        {
            if(!(s.BuildingTypes??[]).Any(t=>t.Id==industry.Id&&t.Kind==BuildingKind.Production)||industry.Products.Select(p=>p.RecipeId).Distinct(StringComparer.Ordinal).Count()!=industry.Products.Length)
                throw new ArgumentException("Invalid industry catalog");
            foreach(var p in industry.Products)
                if(!s.Recipes.Any(r=>r.Id==p.RecipeId&&r.OutputItem==p.ItemId&&r.OutputQuantity==p.Quantity))throw new ArgumentException("Industry product differs from recipe");
            if(industry.Products.GroupBy(p=>p.ItemId).Any(g=>g.Select(p=>p.XpPerUnit).Distinct().Count()!=1))throw new ArgumentException("Inconsistent product experience");
            var type=s.BuildingTypes!.Single(t=>t.Id==industry.Id);HashSet<Cell>? previous=null;
            foreach(var level in type.Levels.OrderBy(l=>l.Level))
            {
                var shape=level.Footprint.IsDefault?type.Footprint:level.Footprint.ToArray();var cells=shape.ToHashSet();
                if(cells.Count!=shape.Length||!cells.Contains(new(0,0))||cells.Any(c=>c.X<0||c.Z<0)||previous is not null&&!previous.IsSubsetOf(cells))throw new ArgumentException("Invalid development level geometry");
                var reached=new HashSet<Cell>{new(0,0)};var pending=new Queue<Cell>();pending.Enqueue(new(0,0));
                while(pending.TryDequeue(out var cell))foreach(var n in new[]{new Cell(cell.X+1,cell.Z),new Cell(cell.X-1,cell.Z),new Cell(cell.X,cell.Z+1),new Cell(cell.X,cell.Z-1)})if(cells.Contains(n)&&reached.Add(n))pending.Enqueue(n);
                if(reached.Count!=cells.Count)throw new ArgumentException("Disconnected development footprint");previous=cells;
            }
        }
        if(!rules.PublicProjectOffers.IsDefault&&(rules.PublicProjectOffers.Distinct().Count()!=rules.PublicProjectOffers.Length||rules.PublicProjectOffers.Any(id=>!s.CapitalOffers.Any(o=>o.Id==id))))throw new ArgumentException("Unknown public project demand");
    }
}
