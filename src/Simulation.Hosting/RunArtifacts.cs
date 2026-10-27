using System.Globalization;
using System.Text;
using System.Text.Json;
using Simulation.Core;

namespace Simulation.Hosting;

public static class RunArtifacts
{
    public static ScenarioDefinition LoadScenario(string path,EconomyProfile? economy=null)
    {
        if (new FileInfo(path).Length > MapDesignReader.MaximumBytes) throw new ArgumentException("Scenario file too large");
        var bytes=File.ReadAllBytes(path);
        if(MapDesignReader.IsDesign(bytes))return MapDesignReader.Read(bytes,economy??throw new ArgumentException("Map design needs --economy A2 supported profile"));
        var scenario = JsonSerializer.Deserialize<ScenarioDefinition>(bytes, SimulationJson.Options)
            ?? throw new ArgumentException("Scenario is empty");
        ScenarioValidation.Validate(scenario); return scenario;
    }
    public static void Export(GameSession session, ScenarioDefinition definition, string directory,
        string timingPolicy, string script, double elapsedMilliseconds, string status = "succeeded", string? failure = null, object? modelMetadata = null, object? economyProvenance = null,double? startupMilliseconds=null)
    {
        Directory.CreateDirectory(directory);
        var state = session.Engine.Inspect();
        var json = new JsonSerializerOptions(SimulationJson.Options) { WriteIndented = true };
        WriteJson("manifest.json", new
        {
            ScenarioId = definition.Id, definition.Seed, definition.EconomyProfileId, SchemaVersion = SimulationEngine.CheckpointSchemaVersion,
            SimulationEngine.RulesVersion, ConfigHash = session.Engine.ConfigHash, TimingPolicy = timingPolicy, Script = script,
            Controller = session.ControllerStates.Any(c => c.Kind.StartsWith("hybrid-", StringComparison.Ordinal)) ? "hybrid" : session.HasControllers ? "algorithmic" : session.PlaybackFinalTick.HasValue ? "recorded-inputs" : "scripted",
            Controllers = session.ControllerStates, ControllerStateHash = session.ComputeControllerStateHash(), Model = modelMetadata, RuntimeVersion = Environment.Version.ToString(),
            Commit = Environment.GetEnvironmentVariable("DELSK_COMMIT"), TickSeconds = 1,
            MapId = definition.Id, MapDesign=definition.Layout is {} layout?new{layout.Version,layout.SourceSha256,layout.SourceId}:null, NewGame=definition.NewGame, ParticipantCount=definition.Participants.Length, NpcCount=definition.NewGame is null?(int?)null:definition.Participants.Length-1, Difficulty = definition.NewGame?.Difficulty, DifficultyReason = "Per-actor policy/settings and module versions are recorded in controllers; no inferred global difficulty",
            Stage = "Package8", DeveloperBuild=SimulationEngine.IsDeveloperBuild, ResearchModified=session.Engine.ResearchModified,
            CleanComparisonEligible=!SimulationEngine.IsDeveloperBuild && !session.Engine.ResearchModified,
            EconomyProvenance = economyProvenance,
            Author = "Шелестов Максим Евгеньевич"
        });
        WriteJson("summary.json", new { Status = status, Failure = failure, state.Tick, StateHash = session.Engine.ComputeStateHash(),
            ControllerStateHash = session.ComputeControllerStateHash(),
            ControllerDecisionTurns = session.ControllerTurns.Length,
            ControllerRejections = session.ControllerTurns.Sum(t => t.Feedback.Count(r => r.Status == ResultStatus.Rejected)),
            StartupMilliseconds=startupMilliseconds, ElapsedMilliseconds = elapsedMilliseconds, AverageMillisecondsPerTick = state.Tick > 0 ? elapsedMilliseconds / state.Tick : (double?)null,
            state.InitialMoneyMinor, state.ExternalMoneyInMinor, state.ExternalMoneyOutMinor, state.Accounts, state.Flows,
            state.Settlements, state.MarketOffers, state.MarketBids, state.MarketAllocations, state.BuyOffers, state.SupplyRequests, state.DeliveryContracts, state.Logistics, state.CapitalDemand, state.Elections, state.Population, state.Unemployed, state.EmploymentGroups, state.PopulationGrowth, RamBytes = (long?)null, RamMeasurementReason = "not measured" });
        WriteJson("development.json",session.Engine.InspectDevelopment()!);
        WriteJson("workshops.json",session.Engine.InspectWorkshops()!);
        WriteJson("demolition.json",session.Engine.InspectDemolition()!);
        WriteJson("scenario.json", definition);
        WriteJson("public-economy.json", session.Engine.PublicEconomy());
        WriteJson("research-modifications.json",session.Engine.ResearchModifications);
        WriteJson("physical-state.json",new { Resources=session.Engine.ResourceSites(),Transfers=definition.Participants.SelectMany(p=>session.Engine.TransfersOf(new(p.Id))).OrderBy(t=>t.Id).ToArray(),Blocked=session.Engine.BlockedCells() });
        WriteJson("logistics.json", new { Systems = state.Logistics });
        WriteJson("capital-demand.json", state.CapitalDemand!);
        WriteJson("elections.json", state.Elections);
        if(session.Engine.InspectFleet() is {} fleet)WriteJson("fleet.json",fleet);
        if(session.Engine.InspectRoads() is {} roads)WriteJson("roads.json",roads);
        if(session.Engine.InspectBilling() is {} billing)WriteJson("billing.json",billing);
        if(session.Engine.InspectServices() is {} services)WriteJson("services.json",services);
        if(session.Engine.InspectProductionContent() is {} content)WriteJson("production-content.json",content);
        WriteJson("support.json", session.Engine.InspectSupport());
        WriteJson("negotiations.json", session.Engine.InspectNegotiations());
        WriteLines("public-events.jsonl", session.Engine.PublicEventsSince(0));
        WriteJson("delivery.json", new { state.BuyOffers, state.SupplyRequests, Contracts = state.DeliveryContracts });
        WriteJson("market.json", new { Offers = state.MarketOffers, Bids = state.MarketBids, Allocations = state.MarketAllocations });
        WriteJson("final-state.json", state); // Inspection export; this is not a loadable checkpoint (P12).
        WriteLines("events.jsonl", session.Engine.EventsSince(0));
        WriteLines("commands.jsonl", session.CommandTape);
        WriteLines("ledger.jsonl", session.Engine.Ledger());
        WriteJson("controllers.json", session.ControllerStates);
        WriteLines("decisions.jsonl", session.ControllerTurns);
        WriteLines("model-decisions.jsonl", session.ControllerTurns.Where(t => t.Decision.ModelTrace is not null)
            .Select(t => new { t.Tick, t.Actor, Trace = t.Decision.ModelTrace }));
        using var agents = new StreamWriter(Path.Combine(directory, "agents.csv"), false, new UTF8Encoding(false));
        agents.WriteLine("tick,actor_id,cash_minor,stock_base_value_minor,stock_units,headcount,enterprises,equipment,busy,goal,reason");
        foreach (var row in session.ControllerSamples)
            agents.WriteLine(FormattableString.Invariant($"{row.Tick},{row.ActorId},{row.CashMinor},{row.StockBaseValueMinor},{row.StockUnits},{row.Headcount},{row.Enterprises},{row.Equipment},{row.Busy},{CsvText(row.Goal)},{CsvText(row.Reason)}"));
        using var csv = new StreamWriter(Path.Combine(directory, "metrics.csv"), false, new UTF8Encoding(false));
        csv.WriteLine("tick,participant_cash_minor,wages_minor,treasury_minor,harvested,produced,exported,state_hash,population,unemployed");
        foreach (var sample in session.Samples)
            csv.WriteLine(string.Join(',', new[] { sample.Tick, sample.ParticipantCashMinor, sample.WageExpenseMinor,
                sample.EnergyExpenseMinor, sample.Harvested, sample.Produced, sample.Exported }.Select(x => x.ToString(CultureInfo.InvariantCulture)))
                + "," + sample.StateHash + "," + sample.Population.ToString(CultureInfo.InvariantCulture) + "," + sample.Unemployed.ToString(CultureInfo.InvariantCulture));
        using var labor = new StreamWriter(Path.Combine(directory, "labor.csv"), false, new UTF8Encoding(false));
        labor.WriteLine("tick,group_id,owner_id,headcount,capacity,wage_minor,attraction_wage_minor,target,ready,reason,credit");
        foreach (var sample in session.LaborSamples)
            foreach (var group in sample.EmploymentGroups)
                labor.WriteLine(FormattableString.Invariant($"{sample.Tick},{group.Id.Value},{group.OwnerId.Value},{group.Headcount},{group.Capacity},{group.WageMinor},{group.AttractionWageMinor},{group.TargetHeadcount},{group.Ready},{group.ReadinessReason},{group.MobilityCredit}"));
        static string CsvText(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        void WriteJson(string name, object value) => File.WriteAllText(Path.Combine(directory, name), JsonSerializer.Serialize(value, json), new UTF8Encoding(false));
        void WriteLines<T>(string name, IEnumerable<T> values)
        {
            using var writer = new StreamWriter(Path.Combine(directory, name), false, new UTF8Encoding(false));
            foreach (var value in values) writer.WriteLine(JsonSerializer.Serialize(value, SimulationJson.Options));
        }
    }
}
