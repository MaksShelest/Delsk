using System.Collections.Immutable;

namespace Simulation.Core;

public sealed record ImportSupply(string ItemId,long Package,long RefreshTicks,long UnitPriceMinor);
public sealed record RecipeVersionLink(string BaseRecipe,string RecipeId,int Version,int TechnologyLevel);
public sealed record WorkshopModernization(int Version,long PriceMinor,ImmutableArray<MaterialCost> Materials,long Ticks);
public sealed record ProductionContentRules(ImmutableArray<ImportSupply> Imports,ImmutableArray<RecipeVersionLink> Versions,ImmutableArray<WorkshopModernization> Modernizations);
public sealed record ImportStock(string ItemId,long Remaining,long NextRefreshTick);
public sealed record ImportPickup(ParticipantId Owner,InventoryId Inventory);
public sealed record WorkshopTechnology(long Workshop,int Version);
public sealed record ModernizationProject(long Id,long Enterprise,long Workshop,int Version,long StartedTick,long EndTick,string CommandId);
public sealed record ProductionContentState(ImmutableArray<ImportStock> Imports,ImmutableArray<ImportPickup> Pickups,ImmutableArray<WorkshopTechnology> Technologies,ImmutableArray<ModernizationProject> Projects);
public sealed record ProductionContentObservation(ImmutableArray<ImportStock> Imports,InventoryId? Pickup,ImmutableArray<WorkshopTechnology> Technologies,ImmutableArray<ModernizationProject> Projects,ProductionContentRules Rules,Cell Terminal);
