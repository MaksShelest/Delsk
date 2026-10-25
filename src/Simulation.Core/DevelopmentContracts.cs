using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Simulation.Core;

public sealed record IndustryProduct(string RecipeId,string ItemId,long Quantity,long XpPerUnit,int TechnologyLevel);
public sealed record DevelopmentIndustry(string Id,ImmutableArray<IndustryProduct> Products);
public sealed record DevelopmentRules(ImmutableArray<DevelopmentIndustry> Industries,int QuestSlots=2,long QuestReviewTicks=1200,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<long> PublicProjectOffers=default)
{
    public static int Technology(long xp)=>xp>=40000?5:xp>=12000?4:xp>=3000?3:xp>=600?2:1;
    public static long Threshold(int level)=>level switch{1=>0,2=>600,3=>3000,4=>12000,5=>40000,_=>throw new ArgumentOutOfRangeException(nameof(level))};
    public void Validate()
    {
        if(QuestSlots!=2||QuestReviewTicks!=1200||Industries.IsDefaultOrEmpty||Industries.Select(i=>i.Id).Distinct(StringComparer.Ordinal).Count()!=Industries.Length||
            Industries.Any(i=>string.IsNullOrWhiteSpace(i.Id)||i.Products.IsDefaultOrEmpty||i.Products.Any(p=>p.Quantity<=0||p.XpPerUnit<=0||p.TechnologyLevel is <1 or >5)))throw new ArgumentException("Invalid development rules");
    }
}
public sealed record IndustryExperience(ParticipantId Owner,string Industry,long ProductionXp,long QuestXp)
{public long Total=>checked(ProductionXp+QuestXp);public int TechnologyLevel=>DevelopmentRules.Technology(Total);}
public sealed record ExperienceGrant(long Owner,string Industry,string Source,long SourceId,string ItemId,long Quantity,long Xp,long Tick);
public sealed record IndustryQuest(long Id,ParticipantId Owner,string Industry,string Kind,long ContractId,string RecipeId,string ItemId,long Quantity,long Reward,
    long AcceptedTick,long DeadlineTick,long BaselineDelivered,long Delivered,string Status,long? ClosedTick=null);
public sealed record IndustryQuestChoice(string Industry,string Kind,long ContractId,string RecipeId,string ItemId,long Quantity,long Reward,long DeadlineTick,int Batches,bool FirstDelivery,long ReviewTick,long OfferExpiresTick,int ReviewWindow);
public sealed record DevelopmentBuilding(long Id,long Owner,string Industry,ImmutableArray<Cell> Contour,ImmutableArray<DemolitionCell> Cells,bool Claimed=true);
public sealed record DevelopmentProject(ImmutableArray<Cell> Contour,ImmutableArray<DemolitionCell> Cells);
public sealed record DevelopmentState(ImmutableArray<ExperienceGrant> Grants,ImmutableArray<IndustryQuest> Quests,ImmutableArray<DevelopmentBuilding> Buildings,ImmutableArray<long> Preparing=default);
public sealed record DevelopmentProjectView(long Id,long BuildingId,Cell Position,string TypeId,int Level,long InventoryId,long StartedTick,long CompletesTick,long PaidMinor,ImmutableArray<MaterialCost> Materials,DevelopmentProject Plan);
public sealed record DevelopmentObservation(ImmutableArray<IndustryExperience> Experience,ImmutableArray<IndustryQuest> Quests,ImmutableArray<IndustryQuestChoice> Choices,
    ImmutableArray<DevelopmentBuilding> Buildings,ImmutableArray<DevelopmentProjectView> Projects,DevelopmentRules Rules,long ReviewTick,long NextReviewTick,ImmutableArray<long> Preparing);
