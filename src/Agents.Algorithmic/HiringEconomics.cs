using Simulation.Core;

namespace Agents.Algorithmic;

public record HiringEstimate(long WageMinor,int ExpectedStaff,long Batches,long RevenueMinor,long InputMinor,
    long EnergyMinor,long PayrollMinor,long TransportMinor,long ProfitMinor,int DelayTicks,string Reason);

/// <summary>Conservative finite forecast from published terms and own stock, never a workforce prediction from world PRNG.</summary>
public static class HiringEconomics
{
    private static long Limit(Int128 value)=>(long)Int128.Clamp(value,long.MinValue,long.MaxValue);
    private static long Ceil(Int128 value,long divisor)=>value<=0?0:Limit((value+divisor-1)/divisor);
    private static long Free(ControllerInventory i,string item)=>i.Stock.Where(s=>s.ItemId==item).Sum(s=>s.Quantity-s.Reserved);
    public static int ExpectedStaff(ControllerObservation o,EmploymentView group,long wage)
    {
        int exponent=o.Economics?.WageExponent??3;
        Int128 Weight(int capacity,long pay){Int128 a=capacity;for(int i=0;i<exponent;i++)a*=pay;return a;}
        var competitors=o.LaborMarket.Where(g=>g.Id!=group.Id).Select(g=>(g.Capacity,g.WageMinor)).ToList();
        // Water filling at public capacity bounds; rounding ties are deliberately not predicted.
        long people=o.Population;Int128 own=Weight(group.Capacity,wage);
        while(true)
        {
            Int128 total=own+competitors.Aggregate((Int128)0,(sum,g)=>sum+Weight(g.Capacity,g.WageMinor));
            if(total==0)return 0;
            var full=competitors.Where(g=>(Int128)people*Weight(g.Capacity,g.WageMinor)>=total*g.Capacity).ToArray();
            if(full.Length==0)return (int)Int128.Min(group.Capacity,(Int128)people*own/total);
            foreach(var g in full){people=Math.Max(0,people-g.Capacity);competitors.Remove(g);}
        }
    }
    public static HiringEstimate Evaluate(ControllerObservation o,EnterpriseView e,EmploymentView group,long wage,ControllerSettings settings)
    {
        var r=o.Map.Recipes.Single(r=>r.Id==e.RecipeId);var input=o.Inventories.Single(i=>i.Id==e.InputId);var output=o.Inventories.Single(i=>i.Id==e.OutputId);
        var rules=o.Economics?.Labor??new();var handling=o.Economics?.Handling??new();int horizon=settings.WageReviewTicks;
        int distance=AlgorithmicController.Distance(o.Map,o.Participant.Position,input.Position);
        int delay=wage==group.WageMinor?0:(int)Math.Min(horizon,Math.Max(0,distance)*o.Map.MoveTicksPerCell+rules.SalaryChangeDelayTicks+rules.StepTicks);
        int target=ExpectedStaff(o,group,wage);
        // Apply the same numerical mobility limit as an upper estimate, without choosing any world's random tie.
        long moves=(group.MobilityCredit+(long)(Math.Max(0,horizon-delay)/rules.StepTicks)*group.Capacity*rules.MobilityNumerator)/rules.MobilityDenominator;
        target=(int)Math.Clamp(target,Math.Max(0,group.Headcount-moves),Math.Min(group.Capacity,group.Headcount+moves));
        HiringEstimate Empty(string reason)=>new(wage,target,0,0,0,0,0,0,0,delay,reason);
        if(!e.Enabled)return Empty("ProductionPaused");
        if(e.BatchId==0 && r.Ingredients.Any(i=>Free(input,i.ItemId)<i.Quantity))return Empty("NoInputs");
        if(e.BatchId==0 && output.Capacity-output.OccupiedVolume-output.ReservedCapacity<r.OutputQuantity*o.Map.Items.Single(i=>i.Id==r.OutputItem).UnitVolume)return Empty("NoOutputSpace");
        if(target<r.MinimumStaff && group.Headcount<r.MinimumStaff)return Empty("NoWorkers");
        long supply=r.Ingredients.Select(i=>Free(input,i.ItemId)/i.Quantity).Min()+(e.BatchId==0?0:1);
        int n0=group.Headcount>=r.MinimumStaff?group.Headcount:0,n1=target>=r.MinimumStaff?target:0;
        int full=o.Buildings.Any(b=>b.Id.Value==e.Id.Value)?e.StaffCapacity:r.RequiredStaff;
        long required=checked(r.FullStaffTicks*full);
        long work=Limit(((Int128)Math.Min(n0,full)*delay+(Int128)Math.Min(n1,full)*(horizon-delay))*e.WorkRateMultiplier);
        long batches=Math.Min(supply,work/required);
        var contracts=o.OwnDeliveryContracts.Where(c=>c.Supplier==o.Participant.Id && c.ItemId==r.OutputItem && c.ClosedTick is null && c.UnprovidedQuantity>0).OrderBy(c=>c.DeadlineTick).ToArray();
        var offers=o.CapitalOffers.Where(a=>a.ItemId==r.OutputItem && a.RemainingQuantity>0 && (a.ExpiresAtTick is null || a.ExpiresAtTick>o.Tick+delay+r.FullStaffTicks)).OrderByDescending(a=>a.UnitPriceMinor).ToArray();
        long demand=Limit(contracts.Aggregate((Int128)0,(sum,c)=>sum+c.UnprovidedQuantity)+offers.Aggregate((Int128)0,(sum,a)=>sum+a.RemainingQuantity));
        demand=Math.Max(0,demand-Free(output,r.OutputItem));
        var terminal=contracts.FirstOrDefault()?.Position??offers.FirstOrDefault()?.Position;
        if(terminal is null || demand==0)return Empty("NoDemand");
        int route=AlgorithmicController.Distance(o.Map,output.Position,terminal.Value);if(route<0)return Empty("NoPath");
        var carrier=o.Inventories.Single(i=>i.Id==o.Participant.InventoryId);
        long volume=o.Map.Items.Single(i=>i.Id==r.OutputItem).UnitVolume;
        long cargo=carrier.Capacity/volume;if(cargo==0)return Empty("NoCargoSpace");
        long trip=checked(2L*route*o.Map.MoveTicksPerCell+2*Ceil(cargo*volume,handling.PersonalVolumePerTick)+o.Map.TradeWindowTicks+3);
        long shippable=Limit((Int128)cargo*Math.Max(1,horizon/Math.Max(1,trip)));
        batches=Math.Min(batches,Math.Min(demand,shippable)/r.OutputQuantity);if(batches<=0)return Empty("SupplyOrDemandLimit");
        long units=batches*r.OutputQuantity,remaining=units;Int128 revenue=0;
        foreach(var c in contracts){long take=Math.Min(remaining,c.UnprovidedQuantity);revenue+=(Int128)take*c.UnitPriceMinor;remaining-=take;}
        foreach(var a in offers){long take=Math.Min(remaining,a.RemainingQuantity);revenue+=(Int128)take*a.UnitPriceMinor;remaining-=take;}
        Int128 raw=0;
        foreach(var i in r.Ingredients)
        {
            long price=Math.Max(o.Map.Items.Single(x=>x.Id==i.ItemId).BasePriceMinor,o.CapitalPrices.Where(p=>p.ItemId==i.ItemId).Select(p=>p.UnitPriceMinor).DefaultIfEmpty(0).Max());
            raw+=(Int128)batches*i.Quantity*price;
        }
        long needed=Limit((Int128)batches*required),first=Math.Min(needed,checked((long)Math.Min(n0,full)*delay*e.WorkRateMultiplier));
        Int128 payroll=(Int128)Ceil(first,Math.Max(1,Math.Min(n0,full)*e.WorkRateMultiplier))*n0*wage+
            (Int128)Ceil(needed-first,Math.Max(1,Math.Min(n1,full)*e.WorkRateMultiplier))*n1*wage;
        // Manual shipping has an opportunity cost even when no transport payroll is charged.
        long opportunity=o.CapitalOffers.Where(a=>o.Resources.Any(n=>n.ItemId==a.ItemId && n.Stock>n.Reserved && (n.Public || n.Owner==o.Participant.Id || n.Tenant==o.Participant.Id)))
            .Select(a=>a.UnitPriceMinor/Math.Max(1,o.Map.HarvestTicksPerUnit+2L*route*o.Map.MoveTicksPerCell/Math.Max(1,cargo))).DefaultIfEmpty(0).Max();
        Int128 transport=(Int128)Ceil(units,cargo)*trip*opportunity;
        Int128 energy=(Int128)batches*r.EnergyPerBatchMinor;
        return new(wage,target,batches,Limit(revenue),Limit(raw),Limit(energy),Limit(payroll),Limit(transport),Limit(revenue-raw-energy-payroll-transport),delay,"PublishedDemandEstimate");
    }
}
