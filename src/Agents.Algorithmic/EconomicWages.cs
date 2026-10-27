using Simulation.Core;

namespace Agents.Algorithmic;

public sealed partial class AlgorithmicController
{
    private IEnumerable<Candidate> EconomicWages(ControllerObservation o)
    {
        var settings=_state.Settings;
        foreach(var line in o.Enterprises.SelectMany(ProductionWorkplaces))
        {
            var e=line.Unit;var group=o.EmploymentGroups.Single(g=>g.Id==line.Group);
            if(group.PendingWageMinor is not null)continue;
            var baseline=HiringEconomics.Evaluate(o,e,group,group.WageMinor,settings);
            var peers=o.LaborMarket.Where(g=>g.OwnerId!=Actor && g.Headcount>0).Select(g=>g.WageMinor).Order().ToArray();
            long reference=peers.Length==0?settings.BaseWageMinor:peers[peers.Length/2];
            var wages=new[]{settings.BaseWageMinor,Math.Min(settings.MaximumWageMinor,group.WageMinor+settings.WageRaiseMinor),
                Math.Min(settings.MaximumWageMinor,reference+settings.WageRaiseMinor)}.Distinct();
            var choice=wages.Where(w=>w!=group.WageMinor).Select(w=>HiringEconomics.Evaluate(o,e,group,w,settings))
                .Where(x=>x.Batches>0 && x.ProfitMinor>0 && x.ProfitMinor>baseline.ProfitMinor &&
                    x.PayrollMinor+x.EnergyMinor<=Math.Max(0,o.Account.AvailableMinor-settings.CashReserveMinor))
                .OrderByDescending(x=>x.ProfitMinor).ThenBy(x=>x.WageMinor).FirstOrDefault();
            if(choice is null)continue;
            yield return WageCandidate(o,group,choice.WageMinor,250000,
                $"Hiring net={choice.ProfitMinor};delta={choice.ProfitMinor-baseline.ProfitMinor};payroll={choice.PayrollMinor};input={choice.InputMinor};transport={choice.TransportMinor};N~{choice.ExpectedStaff};delay={choice.DelayTicks}");
        }
        // Extraction/transport threshold crews: evaluate the whole group against a finite funded job,
        // never an average wage race. Existing obligations remain the first executor priority.
        foreach(var group in o.EmploymentGroups.Where(g=>g.Role is EmploymentRole.Extraction or EmploymentRole.Logistics && g.Ready && g.PendingWageMinor is null && g.Headcount<g.Capacity))
        {
            long wage=Math.Min(settings.MaximumWageMinor,Math.Max(settings.BaseWageMinor,group.WageMinor+settings.WageRaiseMinor));
            int target=HiringEconomics.ExpectedStaff(o,group,wage);
            long revenue=0,cost=long.MaxValue;string reason="";
            if(group.Role==EmploymentRole.Extraction)
            {
                var machine=o.Equipment.Single(m=>m.GroupId==group.Id);var type=o.EquipmentTypes.Single(t=>t.Id==machine.TypeId);var level=type.Levels.Single(l=>l.Level==machine.Level);
                if(group.Headcount>=level.MinimumCrew || target<level.MinimumCrew || machine.Mode!=EquipmentMode.Automatic)continue;
                var site=o.Resources.Single(r=>r.Position==machine.Position);long q=Math.Min(level.CycleQuantity,site.Stock-site.Reserved);
                var offer=BestOffer(o,type.ItemId,q,machine.Position);if(offer is null || q<level.CycleQuantity)continue;
                revenue=Math.Min(q,offer.RemainingQuantity)*offer.UnitPriceMinor;cost=(long)target*wage*level.CycleTicks;reason="ExtractionThreshold";
            }
            else
            {
                var truck=o.OwnLogistics.Single(l=>l.GroupId==group.Id);int minimum=FleetCrewMinimum(o,truck);
                if(group.Headcount>=minimum || target<minimum || truck.Trip is not null)continue;
                var job=o.OwnDeliveryContracts.Where(c=>c.Supplier==Actor && c.ClosedTick is null && c.UnprovidedQuantity>0).OrderBy(c=>c.DeadlineTick).FirstOrDefault();if(job is null)continue;
                var source=Inventory(o,truck.SourceInventory);long q=Math.Min(Free(source,job.ItemId),Math.Min(job.UnprovidedQuantity,FleetCargoCapacity(o,truck)/Volume(o,job.ItemId)));
                int distance=Distance(o.Map,source.Position,job.Position);if(q<=0 || distance<0)continue;
                long ticks=2L*distance*FreightCellEstimate(o)+2*q*Volume(o,job.ItemId)/Math.Max(1,o.Economics?.Handling.WorkerVolumePerTick??1)+2;
                if(o.Tick+ticks+(o.Economics?.Labor.SalaryChangeDelayTicks??20)>job.DeadlineTick)continue;
                revenue=q*job.UnitPriceMinor;cost=ticks*target*wage;reason="FundedDeliveryThreshold";
            }
            if(wage>group.WageMinor && revenue>cost && cost<=o.Account.AvailableMinor-settings.CashReserveMinor)
                yield return WageCandidate(o,group,wage,250000,$"{reason};revenue={revenue};wholeCrew={cost}");
        }
    }
}
