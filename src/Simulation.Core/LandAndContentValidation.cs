namespace Simulation.Core;

public static partial class ScenarioValidation
{
    internal static Cell[] FootprintCells(Cell anchor, BuildingTypeDefinition type) => type.Footprint
        .Select(c => new Cell(checked(anchor.X + c.X), checked(anchor.Z + c.Z)))
        .OrderBy(c => c.X).ThenBy(c => c.Z).ToArray();

    private static void ValidateLandAndContent(ScenarioDefinition s, GridMap map,
        Dictionary<string, ItemDefinition> items, Dictionary<string, RecipeDefinition> recipes, HashSet<long> actors)
    {
        var plots = s.Plots ?? []; var types = s.BuildingTypes ?? []; var buildings = s.Buildings ?? [];
        if (s.LandReservationTicks is < 1 or > 1000000 || s.MaxActivePlotReservations is < 1 or > 64 ||
            plots.Length > s.MapWidth * s.MapHeight || types.Length > 64 || buildings.Length > 1024 ||
            plots.Any(p => p is null) || types.Any(t => t is null) || buildings.Any(b => b is null))
            throw new ArgumentException("Invalid land/content settings");
        if (plots.Select(p => p.Id).Distinct().Count() != plots.Length ||
            plots.Select(p => p.Position).Distinct().Count() != plots.Length || plots.Any(p => p.Id is <= 0 or >= 1000 ||
            !map.Walkable(p.Position) || p.PriceMinor < 0 || (!p.IsCorridor && p.PriceMinor == 0) ||
            (p.OwnerId.HasValue && !actors.Contains(p.OwnerId.Value)) ||
            (p.ShopParticipantId.HasValue && (!actors.Contains(p.ShopParticipantId.Value) ||
                (p.OwnerId.HasValue && p.OwnerId != p.ShopParticipantId))) ||
            (p.IsShopSite && p.ShopParticipantId.HasValue) ||
            (p.IsCorridor && (p.OwnerId.HasValue || p.ShopParticipantId.HasValue || p.IsShopSite)) ||
            (!p.IsCorridor && (s.ResourceNodes.Any(n => n.Position == p.Position) ||
                s.CapitalOffers.Any(o => o.Position == p.Position)))))
            throw new ArgumentException("Invalid plots or protected public cells");
        if (plots.Any(p => p.IsShopSite) && (plots.Count(p => p.IsShopSite) < actors.Count ||
            plots.Any(p => p.ShopParticipantId.HasValue) || plots.Where(p => p.IsShopSite && p.OwnerId.HasValue).GroupBy(p => p.OwnerId).Any(g => g.Count() > 1)))
            throw new ArgumentException("Shared shop sites: at least one per participant, no assignments or duplicate ownership");
        var shopSlots = plots.Where(p => p.ShopParticipantId.HasValue).Select(p => p.ShopParticipantId);
        if (shopSlots.Distinct().Count() != shopSlots.Count()) throw new ArgumentException("Duplicate shop slot");
        if (s.Enterprises.Length > 0 && (plots.Length > 0 || buildings.Length > 0))
            throw new ArgumentException("Land scenarios use Buildings instead of legacy Enterprises");
        if (types.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() != types.Length)
            throw new ArgumentException("Duplicate building type");
        try
        {
            foreach (var type in types)
            {
                if (string.IsNullOrWhiteSpace(type.Id) || type.Id.Length > 50 || !Enum.IsDefined(type.Kind) ||
                    type.Footprint is null || type.Footprint.Length is < 1 or > 64 ||
                    type.Footprint.Distinct().Count() != type.Footprint.Length || !type.Footprint.Contains(new Cell(0, 0)) ||
                    type.Footprint.Any(c => c.X is < 0 or > 127 || c.Z is < 0 or > 127) ||
                    type.Levels is null || type.Levels.Length is < 1 or > 16 || type.Levels.Any(l => l is null) ||
                    !type.Levels.Select(l => l.Level).Order().SequenceEqual(Enumerable.Range(1, type.Levels.Length)))
                    throw new ArgumentException("Invalid building footprint or levels");
                var reached = new HashSet<Cell> { new(0, 0) }; var queue = new Queue<Cell>(reached);
                while (queue.TryDequeue(out var c))
                    foreach (var n in new Cell[] { new(c.X + 1, c.Z), new(c.X - 1, c.Z), new(c.X, c.Z + 1), new(c.X, c.Z - 1) })
                        if (type.Footprint.Contains(n) && reached.Add(n)) queue.Enqueue(n);
                if (reached.Count != type.Footprint.Length) throw new ArgumentException("Disconnected footprint");
                BuildingLevelDefinition? previous = null;
                foreach (var level in type.Levels.OrderBy(l => l.Level))
                {
                    if (level.ConstructionTicks>0&&(s.Economy?.Storage is null||type.Kind!=BuildingKind.Warehouse) || level.ConstructionTicks<0 || level.ConstructionTicks>1000000 || (s.Economy?.Storage is not null && type.Kind==BuildingKind.Warehouse && (type.Footprint.Length!=1 || level.ConstructionTicks==0 || (type.Walls??[]).Length>0)) || level.PriceMinor < 0 || level.InputCapacity <= 0 || level.OutputCapacity < 0 ||
                        level.StaffCapacity is < 0 or > 1000000 || level.WageMinor < 0 ||
                        level.WorkRateMultiplier is < 1 or > 16 || level.Materials is null || level.Materials.Length > 32 ||
                        level.Materials.Any(m => m is null || !items.ContainsKey(m.ItemId) || m.Quantity <= 0) ||
                        level.Materials.Select(m => m.ItemId).Distinct().Count() != level.Materials.Length ||
                        level.RecipeId is null ||
                        (previous is not null && (level.InputCapacity < previous.InputCapacity ||
                            level.OutputCapacity < previous.OutputCapacity || level.StaffCapacity < previous.StaffCapacity)))
                        throw new ArgumentException("Invalid building level/costs");
                    if(!level.AdditionalRecipeIds.IsDefault && (level.AdditionalRecipeIds.Length>8 || level.AdditionalRecipeIds.Any(id=>id is null || !recipes.ContainsKey(id)) ||
                        level.UnlockedRecipes.Distinct(StringComparer.Ordinal).Count()!=1+level.AdditionalRecipeIds.Length))throw new ArgumentException("Invalid additional recipes");
                    if (type.Kind == BuildingKind.Production)
                    {
                        foreach(var recipeId in type.Levels.Where(l=>l.Level<=level.Level).SelectMany(l=>l.UnlockedRecipes).Distinct(StringComparer.Ordinal))
                        {
                            if(!recipes.TryGetValue(recipeId,out var recipe) || level.StaffCapacity<recipe.RequiredStaff || level.OutputCapacity<=0 ||
                                recipe.Ingredients.Aggregate(0L,(total,i)=>checked(total+checked(i.Quantity*items[i.ItemId].UnitVolume)))>level.InputCapacity ||
                                checked(recipe.OutputQuantity*items[recipe.OutputItem].UnitVolume)>level.OutputCapacity)
                                throw new ArgumentException("Building recipe cannot fit its level");
                            _=checked((long)recipe.RequiredStaff*level.WorkRateMultiplier);_=checked(recipe.FullStaffTicks*level.StaffCapacity);
                            _=checked(level.StaffCapacity*level.WageMinor+recipe.EnergyPerBatchMinor);
                        }
                        if(!recipes.ContainsKey(level.RecipeId))throw new ArgumentException("Missing default recipe");
                    }
                    else if (!level.AdditionalRecipeIds.IsDefaultOrEmpty || level.RecipeId != "" || level.StaffCapacity != 0 || level.WageMinor != 0 ||
                        level.OutputCapacity != 0 || level.WorkRateMultiplier != 1)
                        throw new ArgumentException("Storage cannot create production or employment");
                    long materialVolume = level.Materials.Aggregate(0L, (n, m) => checked(n + checked(m.Quantity * items[m.ItemId].UnitVolume)));
                    if (previous is not null && materialVolume > previous.InputCapacity)
                        throw new ArgumentException("Upgrade materials cannot fit the built-in input");
                    _ = checked(level.StaffCapacity * level.WageMinor +
                        (type.Kind == BuildingKind.Production ? recipes[level.RecipeId].EnergyPerBatchMinor : 0));
                    previous = level;
                }
            }
            var byCell = plots.ToDictionary(p => p.Position);
            var byType = types.ToDictionary(t => t.Id, StringComparer.Ordinal);
            var used = new HashSet<Cell>();
            if (buildings.Select(b => b.Id).Distinct().Count() != buildings.Length)
                throw new ArgumentException("Duplicate building ID");
            foreach (var building in buildings)
            {
                if (building.Id is <= 0 or >= 1000 || !actors.Contains(building.OwnerId) || building.TypeId is null ||
                    !byType.TryGetValue(building.TypeId, out var type) || building.Level < 1 || building.Level > type.Levels.Length)
                    throw new ArgumentException("Invalid initial building");
                var level = type.Levels.Single(l => l.Level == building.Level);
                if (building.Staff < 0 || building.Staff > level.StaffCapacity ||
                    (type.Kind != BuildingKind.Production && (building.Staff != 0 || building.Enabled)))
                    throw new ArgumentException("Invalid initial building employment");
                if (type.Kind == BuildingKind.Shop &&
                    (!byCell.TryGetValue(building.Position, out var site) || !site.IsShopSite && site.ShopParticipantId != building.OwnerId))
                    throw new ArgumentException("Shop must use its assigned trading site");
                foreach (var cell in FootprintCells(building.Position, type))
                    if (!byCell.TryGetValue(cell, out var plot) || plot.IsCorridor || plot.OwnerId != building.OwnerId ||
                        (plot.IsShopSite && type.Kind != BuildingKind.Shop) ||
                        (plot.ShopParticipantId.HasValue && (type.Kind != BuildingKind.Shop || plot.ShopParticipantId != building.OwnerId)) ||
                        !used.Add(cell)) throw new ArgumentException("Building must occupy its own exclusive footprint");
            }
        }
        catch (OverflowException ex) { throw new ArgumentException("Land/content arithmetic overflow", ex); }
    }
}
