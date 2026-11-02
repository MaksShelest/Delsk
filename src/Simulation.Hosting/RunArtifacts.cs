using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
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
        string timingPolicy, string script, double elapsedMilliseconds, string status = "succeeded", string? failure = null, object? modelMetadata = null, object? economyProvenance = null,double? startupMilliseconds=null,bool compressControllerTurns=false)
    {
        Directory.CreateDirectory(directory);
        var state = session.Engine.Inspect();
        var json = new JsonSerializerOptions(SimulationJson.Options) { WriteIndented = true };
        WriteJson("manifest.json", new
        {
            ScenarioId = definition.Id, definition.Seed, definition.EconomyProfileId, SchemaVersion = session.Engine.ActiveCheckpointSchemaVersion,
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
            ControllerDecisionTurns = session.ControllerTurnCount,
            ControllerRejections = session.ControllerTurnMetadata.Sum(t => t.Feedback.Count(r => r.Status == ResultStatus.Rejected)),
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
        if(session.Engine.InspectBilling() is {} billing)
        {
            WriteJson("billing.json",billing);
            // Private research export, like final-state/ledger; never a public game DTO.
            WriteJson("personal-accounts.json",definition.Participants.Select(p=>new{Actor=p.Id,Personal=session.Engine.AccountOf(new(p.Id))}).ToArray());
        }
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
        if(compressControllerTurns)
        {
            long bytes;string digest;
            using(var file=File.Create(Path.Combine(directory,"decisions.jsonl.gz")))
            using(var zip=new GZipStream(file,CompressionLevel.Fastest))
            using(var measured=new HistoryDigestStream(zip))
            {session.WriteControllerTurns(measured);bytes=measured.Bytes;digest=measured.Finish();}
            WriteJson("decisions-storage.json",new{Version=1,Format="gzip",UncompressedBytes=bytes,Sha256=digest,
                CompressedBytes=new FileInfo(Path.Combine(directory,"decisions.jsonl.gz")).Length});
        }
        else
        {using var turns=File.Create(Path.Combine(directory,"decisions.jsonl"));session.WriteControllerTurns(turns);}
        WriteLines("model-decisions.jsonl", session.ControllerTurnMetadata.Where(t => t.Decision.ModelTrace is not null)
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
        if(compressControllerTurns)
        {
            agents.Flush();csv.Flush();labor.Flush();
            WriteJson("export-complete.json",new{Complete=true,Tick=state.Tick,ControllerDecisionTurns=session.ControllerTurnCount,ControllerHistory="decisions.jsonl.gz"});
        }
        static string CsvText(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        void WriteJson(string name, object value) {using var output=File.Create(Path.Combine(directory,name));JsonSerializer.Serialize(output,value,json);}
        void WriteLines<T>(string name, IEnumerable<T> values)
        {
            using var writer = new StreamWriter(Path.Combine(directory, name), false, new UTF8Encoding(false));
            foreach (var value in values) writer.WriteLine(JsonSerializer.Serialize(value, SimulationJson.Options));
        }
    }
    private sealed class HistoryDigestStream(Stream destination):Stream
    {
        private readonly IncrementalHash _hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        public long Bytes{get;private set;}
        public string Finish()=>Convert.ToHexStringLower(_hash.GetHashAndReset());
        public override void Write(byte[] buffer,int offset,int count)=>Write(buffer.AsSpan(offset,count));
        public override void Write(ReadOnlySpan<byte> buffer){_hash.AppendData(buffer);Bytes=checked(Bytes+buffer.Length);destination.Write(buffer);}
        public override void Flush()=>destination.Flush();
        public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;
        public override long Length=>throw new NotSupportedException();
        public override long Position{get=>Bytes;set=>throw new NotSupportedException();}
        public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();
        protected override void Dispose(bool disposing){if(disposing)_hash.Dispose();base.Dispose(disposing);}
    }

}
