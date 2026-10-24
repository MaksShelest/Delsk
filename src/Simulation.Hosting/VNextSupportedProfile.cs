using System.Collections.Immutable;
using System.Text.Json;
using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>Non-authoritative provenance; all active values are copied into the saved resolved ScenarioDefinition.</summary>
public sealed record VNextProjectionMetadata(string ProjectionVersion, string SourceSha256, string FullBalanceSha256,
    string CatalogSha256, string BaselineProfileSha256, string ProjectionPayloadSha256, int FixedParticipants, string[] ActiveCapabilities, string[] Limitations);

public static class VNextSupportedProfile
{
    public const string ProfileId = "vnext-b1-supported-1";
    public static EconomyProfile Create(VNextBalance b, EconomyProfile baseline, string fullSha, string catalogSha, string baselineSha)
    {
        var analysis=VNextBalanceAnalyzer.Analyze(b);
        if(!analysis.IsValid)throw new ArgumentException("Cannot project a semantically invalid balance: "+analysis.Errors[0].Code);
        if(b.Resources.Values.Select(r=>r.ManualTicks).Distinct().Count()!=1)
            throw new ArgumentException("Current Core has one manual harvest duration; distinct resource durations are not supported.");
        long a=b.EconomyGlobals.CapitalDemandCoefficientBp, d=10000;
        while(d!=0) {long remainder=a%d;a=d;d=remainder;}
        int demandNumerator=checked((int)(b.EconomyGlobals.CapitalDemandCoefficientBp/a)), demandDenominator=checked((int)(10000/a));
        if(demandNumerator>100 || demandDenominator>100)throw new ArgumentException("Demand ratio cannot be represented exactly by current Core bounds.");
        if(b.Items.Any(i=>i.Id.Length>50)||b.Recipes.Any(r=>r.Id.Length>50))throw new ArgumentException("Supported Core IDs are limited to 50 characters; full metadata allows 64.");
        int staff=checked((int)b.Rules.StaffPerWorkshop);
        var recipes=b.Recipes.Select(r=>new RecipeDefinition(r.Id,OutputItem:r.Output,OutputQuantity:r.OutputQuantity,
            FullStaffTicks:r.WorkTicksFullStaff,RequiredStaff:staff,MinimumStaff:1,
            EnergyPerBatchMinor:checked(r.WorkTicksFullStaff*r.ServiceMinorPerWorkTick),
            Inputs:[..r.Inputs.Select(i=>new MaterialCost(i.Key,i.Value))])).ToArray();
        var resources=b.Resources.Select(p=>new ResourceTypeBalance(p.Key,
            new(false,p.Value.PrivateStockPerCell,1,checked((int)p.Value.RegenOneEveryTicks),b.EconomyGlobals.ResourceCellPriceMinor,b.EconomyGlobals.ResourceLeasePriceMinor,checked((int)b.EconomyGlobals.ResourceLeaseTicks)),
            new(true,p.Value.PublicStockPerCell,1,checked((int)p.Value.PublicRegenOneEveryTicks),b.EconomyGlobals.ResourceCellPriceMinor,b.EconomyGlobals.ResourceLeasePriceMinor,checked((int)b.EconomyGlobals.ResourceLeaseTicks)))).ToArray();
        var w=baseline.World;
        // No fake five-level buildings: the legacy builder cannot yet store initial materials on bare ground.
        var world=w with {
            StartingCashMinor=b.Rules.StartCashMinor,PersonalInventoryCapacity=b.Rules.PersonalCapacity,
            PlotPriceMinor=b.Rules.CellPriceMinor,LandReservationTicks=checked((int)b.Rules.ReservationTtl),
            HarvestTicksPerUnit=checked((int)b.Resources.Values.First().ManualTicks),
            Items=b.Items.Select(i=>new ItemDefinition(i.Id,i.UnitVolume,i.ReferencePriceMinor)).ToArray(),Recipes=recipes,Resources=resources,
            BuildingTypes=w.BuildingTypes.Where(t=>t.Kind!=BuildingKind.Production).ToArray(),EquipmentTypes=[],
            Rules=w.Rules with {PlayerWageStepMinor=b.Rules.WageButtonStepMinor,ReservationFeeBasisPoints=checked((int)b.Rules.ReservationExtraBp)},
            CapitalDemand=w.CapitalDemand with {PeriodTicks=checked((int)b.EconomyGlobals.CapitalDemandPeriod),
                BootstrapBaseBudgetMinor=b.EconomyGlobals.CapitalBootstrapBudgetMinorPer4Participants,
                DemandNumerator=demandNumerator,DemandDenominator=demandDenominator},
            Elections=w.Elections with {PeriodTicks=b.Rules.ElectionPeriod}
        };
        var payload=new EconomyProfile(1,ProfileId,world,baseline.Npc);
        var metadata=new VNextProjectionMetadata("supported-projection-1",b.SourceSha256,fullSha,catalogSha,baselineSha,VNextBalanceReader.Digest(Serialize(payload)),4,
            ["34-item-definitions","23-v1-recipes-1-to-8-inputs","linear-staff-existing-executor","proportional-service-per-work","8-resource-site-profiles","uniform-manual-harvest","start-cash-personal-capacity-land-fee","demand-period-budget-coefficient-for-4","election-period"],
            ["Not full B1 and not default game; fixed four-participant demand budget only.",
             "No vNext production building templates: bare ground storage/construction duration need C/D. Use prepared Enterprise fixture.",
             "Unlock level/XP/recipe V2,V3/parallel workshops and nonlinear staff curve are metadata; runtime uses linear headcount, minimum one.",
             "No import inventory/bootstrap/replenishment runtime, five-tier expansion, services, invoices, new fleet or roads.",
             "Baseline population/growth without new cap, labor/market/delivery/logistics/warehouse/shop/infrastructure/NPC/waves/handling/movement/maxTicks stay explicit legacy defaults.",
             "Equipment catalog disabled in this subset; resources are manual. Resource profiles keep each actual ItemId; no resource maps, extra bots or UI added."]);
        return new(1,ProfileId,world,baseline.Npc,metadata);
    }
    public static byte[] Serialize(EconomyProfile profile)=>JsonSerializer.SerializeToUtf8Bytes(profile,new JsonSerializerOptions(SimulationJson.Options){WriteIndented=true});
}
