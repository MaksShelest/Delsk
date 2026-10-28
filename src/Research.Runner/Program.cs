using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Simulation.Core;
using Agents.Algorithmic;
using Simulation.Hosting;

if(args.Contains("--export-npc-economy-profile",StringComparer.Ordinal)||args.Contains("--export-service-runs-profile",StringComparer.Ordinal)||args.Contains("--export-service-profile",StringComparer.Ordinal)||args.Contains("--export-billing-profile",StringComparer.Ordinal)||args.Contains("--export-road-profile",StringComparer.Ordinal)||args.Contains("--export-fleet-profile",StringComparer.Ordinal)||args.Contains("--export-production-profile",StringComparer.Ordinal)||args.Contains("--export-storage-profile",StringComparer.Ordinal)||args.Contains("--export-workshop-profile",StringComparer.Ordinal)||args.Contains("--export-development-profile",StringComparer.Ordinal))return StorageProfileCommand.Run(args);
if(args.Contains("--export-map-design",StringComparer.Ordinal))return MapDesignCommand.Run(args);

// Catalog-only commands must return before any model runtime, controller or world exists.
if (args.Contains("--validate-vnext-catalog", StringComparer.Ordinal)) return VNextCatalogCommand.Run(args);
if (args.Contains("--validate-vnext-balance", StringComparer.Ordinal)) return VNextBalanceCommand.Run(args);

try
{
    using var models = new ModelServices();
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int i = 0; i < args.Length; i += 2)
    {
        if (i + 1 >= args.Length || args[i] is not ("--scenario" or "--output" or "--script" or "--mode" or "--seed" or
            "--replay" or "--ticks" or "--checkpoint" or "--checkpoint-at" or "--load" or "--recover-backup" or "--controllers" or "--model-mode" or "--model-config" or "--economy" or "--new-game" or "--validate-only" or "--profile" or "--human-ballot" or "--research" or "--research-setup" or "--model-wait"
#if DELSK_DEVELOPER
            or "--research-modifications"
#endif
            ))
            throw new ArgumentException("Usage: --scenario PATH --output DIRECTORY [--economy config/economy.json] [--new-game OPTIONS_JSON (new baseline/hybrid only)] [--validate-only true] [--profile true] [--script cycle|recovery|development|labor|equipment|autonomous|market|pickup|delivery|capital-delivery|logistics|logistics-upgrade|own-storage|inbound|own-production|logistics-queue|capital-demand|elections|stopped-elections|physical-automation|participant-trade|food-chain|support|baseline|hybrid|model-safety] [--mode headless|paced] [--model-mode off|ollama --model-config PATH] [--seed N] [--replay COMMANDS --ticks N] [--checkpoint FILE --checkpoint-at T] [--load FILE --ticks FINAL_TICK --recover-backup true]");
        if (!options.TryAdd(args[i], args[i + 1])) throw new ArgumentException("Duplicate argument");
    }
    foreach (string flag in new[] { "--profile", "--validate-only", "--research", "--model-wait", "--recover-backup" })
        if (options.TryGetValue(flag,out var value) && !bool.TryParse(value,out _)) throw new ArgumentException(flag + " requires true or false");
    foreach(string flag in new[]{"--profile","--validate-only","--research","--model-wait","--recover-backup"})
        if(options.ContainsKey(flag))options[flag]=bool.Parse(options[flag]).ToString().ToLowerInvariant();
    if ((options.ContainsKey("--load") || options.ContainsKey("--replay")) &&
        (options.GetValueOrDefault("--profile","false") == "true" || options.GetValueOrDefault("--validate-only","false") == "true"))
        throw new ArgumentException("Profile/validate-only require a new run");
    if(options.ContainsKey("--research-setup") && (options.ContainsKey("--load") || options.ContainsKey("--replay")))throw new ArgumentException("Research setup applies only to new runs");
    byte[]? economyBytes = options.TryGetValue("--economy", out var economyPath) ? VNextBalanceReader.ReadBounded(economyPath) : null;
    EconomyProfile? economics = economyBytes is null ? null : EconomyProfile.Read(System.Text.Encoding.UTF8.GetString(economyBytes));
    if (economics is not null && (options.ContainsKey("--load") || options.ContainsKey("--replay")))
        throw new ArgumentException("--economy only applies to a new run; load/replay use the saved resolved configuration");
    string directory = options.GetValueOrDefault("--output", "artifacts/cycle");
    string script = options.GetValueOrDefault("--script", "cycle");
    string mode = options.GetValueOrDefault("--mode", "headless");
    if(options.ContainsKey("--new-game") && (options.ContainsKey("--load") || options.ContainsKey("--replay") || options.ContainsKey("--seed") || options.ContainsKey("--research-setup") ||
        script is not ("baseline" or "hybrid") || economics is null))throw new ArgumentException("--new-game OPTIONS requires new baseline/hybrid with --economy; no load/replay/seed/research-setup overrides");
    if (script is not ("cycle" or "recovery" or "development" or "labor" or "equipment" or "autonomous" or "market" or "pickup" or "delivery" or "capital-delivery" or "logistics" or "logistics-upgrade" or "own-storage" or "inbound" or "own-production" or "logistics-queue" or "capital-demand" or "elections" or "stopped-elections" or "physical-automation" or "participant-trade" or "food-chain" or "support" or "baseline" or "hybrid" or "model-safety" or "vnext-supported-fixture" or "storage-c1" or "demolition-c2" or "workshops-d1") || mode is not ("headless" or "paced"))
        throw new ArgumentException("Invalid script or mode");
    if (options.ContainsKey("--controllers") && (script is not ("autonomous" or "baseline" or "hybrid" or "model-safety") || options.ContainsKey("--load") || options.ContainsKey("--replay")))
        throw new ArgumentException("--controllers applies only to a new autonomous run");
    if ((options.ContainsKey("--model-config") || options.ContainsKey("--model-mode")) &&
        (options.ContainsKey("--replay") || !options.ContainsKey("--load") && script != "hybrid"))
        throw new ArgumentException("Model options require a live hybrid session; replay never creates a model runtime");
    if (options.ContainsKey("--checkpoint-at") && !options.ContainsKey("--checkpoint"))
        throw new ArgumentException("--checkpoint-at requires --checkpoint");
    long? saveTick = options.TryGetValue("--checkpoint-at", out string? saveAt) ? long.Parse(saveAt, CultureInfo.InvariantCulture) : null;
    if (saveTick < 0) throw new ArgumentException("Checkpoint tick must be nonnegative");
    long nextDue = Stopwatch.GetTimestamp();
    void Pace()
    {
        nextDue = checked(nextDue + Stopwatch.Frequency);
        long remaining = nextDue - Stopwatch.GetTimestamp();
        if (remaining > 0) Thread.Sleep(TimeSpan.FromSeconds((double)remaining / Stopwatch.Frequency));
    }
    var watch = Stopwatch.StartNew();
    OrdinaryGameResolution? ordinary=null;
    GameSession session;
    ScenarioDefinition definition;
    long? target = options.TryGetValue("--ticks", out string? ticks) ? long.Parse(ticks, CultureInfo.InvariantCulture) : null;
    RecordedCommand[]? tape = options.TryGetValue("--replay", out string? replayPath) ?
        File.ReadLines(replayPath).Select(line => JsonSerializer.Deserialize<RecordedCommand>(line, SimulationJson.Options)
            ?? throw new ArgumentException("Invalid command tape")).ToArray() : null;
    if(options.TryGetValue("--human-ballot",out var humanBallot) && humanBallot!="self")throw new ArgumentException("Only --human-ballot self is supported");
    bool saved = false;
    if (options.TryGetValue("--load", out string? loadPath))
    {
        if (options.ContainsKey("--seed") || options.ContainsKey("--scenario"))
            throw new ArgumentException("Loaded configuration/seed cannot be replaced; start a new scenario instead");
        bool recover = options.TryGetValue("--recover-backup", out string? recovery) && bool.Parse(recovery);
        var loaded = CheckpointStore.Load(loadPath, recover, mode == "paced" ? Pace : null);
        session = loaded.Session; definition = session.Engine.Configuration();
        models.Restore(session, options);
        if (loaded.UsedBackup) Console.WriteLine($"Recovered backup: {loaded.LoadedPath}; tick={session.Engine.Tick}");
        target ??= session.PlaybackFinalTick ?? throw new ArgumentException("Load without a recorded plan requires --ticks FINAL_TICK");
        if (tape is not null)
        {
            if (session.PlaybackFinalTick.HasValue) throw new ArgumentException("Checkpoint already contains its recorded plan");
            var submitted = session.CommandTape.ToHashSet();
            session.ConfigurePlayback(tape.Where(c => c.SubmittedAtTick >= session.Engine.Tick && !submitted.Contains(c)).ToArray(), target.Value);
        }
        script = session.HasControllers ? "autonomous" : session.PlaybackLabel ?? "loaded-state";
        session.SetPaused(false); mode = "loaded-" + mode;
    }
    else
    {
        definition = RunArtifacts.LoadScenario(options.GetValueOrDefault("--scenario", script == "model-safety" ? "scenarios/model-safety.json" : script is "baseline" or "hybrid" ? "scenarios/baseline-economy.json" : script == "autonomous" ? "scenarios/autonomous-economy.json" : "scenarios/economic-cycle.json"),economics);
        if(options.TryGetValue("--new-game",out var newGamePath))
        {
            if(new FileInfo(newGamePath).Length>8192)throw new ArgumentException("New-game options too large");
            var gameOptions=OrdinaryGameOptions.Read(File.ReadAllText(newGamePath));
            VNextBalance? full=null;
            if(economics!.VNext is not null)
            { var read=VNextBalanceReader.Load(Path.Combine(Path.GetDirectoryName(economyPath!)!,"balance.b1.json"));if(!read.IsValid || read.Sha256!=economics.VNext.FullBalanceSha256)throw new ArgumentException("Full balance/profile mismatch");full=read.Balance; }
            ordinary=OrdinaryNewGame.Resolve(definition,economics!,gameOptions,vnextBalance:full);definition=ordinary.Scenario;
        }
        else if (economics is not null) definition = economics.Apply(definition);
        if(options.TryGetValue("--research-setup",out var setupPath))definition=ResearchObservation.ApplySetup(definition,setupPath);
        if (options.GetValueOrDefault("--validate-only", "false") == "true")
        {
            var validationSession = new GameSession(new SimulationEngine(definition));
            if (script == "hybrid") models.Configure(validationSession, definition, options.GetValueOrDefault("--controllers", "scenarios/baseline-controllers.json"), options, false, economics,ordinary);
            else AutonomousRun.Configure(validationSession, definition, options.GetValueOrDefault("--controllers", "scenarios/baseline-controllers.json"), economics: economics,ordinary:ordinary);
            Console.WriteLine($"Economy/scenario valid: {economics?.Id ?? definition.Id}"); return 0;
        }
        if (options.TryGetValue("--seed", out string? seed)) definition = definition with { Seed = ulong.Parse(seed, CultureInfo.InvariantCulture) };
        if (target.HasValue && tape is null && !saveTick.HasValue && script is not ("autonomous" or "baseline" or "hybrid" or "model-safety"))
            throw new ArgumentException("--ticks requires --load, --replay or checkpointed fixture playback");
        session = new(new SimulationEngine(definition), beforeStep: mode == "paced" ? Pace : null);
        if (script is ("autonomous" or "baseline" or "hybrid" or "model-safety") && tape is null)
        {
            string controllers = options.GetValueOrDefault("--controllers", script == "autonomous" ? "scenarios/autonomous-controllers.json" : "scenarios/baseline-controllers.json");
            long horizon = script is "hybrid" or "model-safety" ? models.Configure(session, definition, controllers, options, script == "model-safety", economics,ordinary)
                : AutonomousRun.Configure(session, definition, controllers, economics: economics, selfBallots: options.GetValueOrDefault("--human-ballot")=="self",ordinary:ordinary);
            if(definition.Elections?.PauseForBallots==true && options.GetValueOrDefault("--human-ballot")!="self")
                throw new ArgumentException("Frozen elections require an explicit --human-ballot self policy in headless runs");
            target ??= horizon;
            if (script == "model-safety") { target = options.ContainsKey("--ticks") ? target : 60; mode = "ControlledFakeS7"; }
            // Выключенная модель остаётся тем же headless/paced, что и baseline.
            // LiveHeadlessStress и Realtime обозначают только включённый runtime.
            if (script == "hybrid" && options.GetValueOrDefault("--model-mode", "off") != "off")
                mode = mode == "paced" ? "Realtime" : "LiveHeadlessStress";
        }
        else if (tape is not null || saveTick.HasValue)
        {
            if (tape is null)
            {
                // Existing fixture scripts are synchronous. Record their complete plan before checkpointed playback.
                var recording = new GameSession(new SimulationEngine(definition));
                RunScript(recording, definition, script); tape = recording.CommandTape; target ??= recording.Engine.Tick;
            }
            if (!target.HasValue) throw new ArgumentException("Replay requires --ticks FINAL_TICK");
            session.ConfigurePlayback(tape, target.Value, script); mode = "recorded-" + mode;
        }
    }
#if DELSK_DEVELOPER
    if(options.TryGetValue("--research-modifications",out var researchPath))
    {
        if(tape is null || options.ContainsKey("--load") || new FileInfo(researchPath).Length>1024*1024)throw new ArgumentException("Research modifications require a fresh replay and at most 1MiB of audit data");
        session.ConfigureResearchPlayback(JsonSerializer.Deserialize<ResearchModification[]>(File.ReadAllText(researchPath),SimulationJson.Options)??throw new ArgumentException("InvalidResearchReplay"));
    }
#endif
    double startupMilliseconds=watch.Elapsed.TotalMilliseconds;
    bool observe=options.GetValueOrDefault("--research")=="true";
    bool waitForModel=options.GetValueOrDefault("--model-wait")=="true";
    if(observe && (target is null || options.GetValueOrDefault("--profile")=="true"))throw new ArgumentException("Research recording requires --ticks and no --profile");
    using var observer=observe?new ResearchObservation(directory,session,definition,target!.Value):null;
    if(waitForModel)mode="ResearchWaitAtDecision-v1";
    try
    {
        if (saveTick.HasValue)
        {
            if (saveTick.Value < session.Engine.Tick) throw new ArgumentException("Checkpoint tick precedes loaded tick");
            if (saveTick.Value == session.Engine.Tick) Save(session);
            session.TickCompleted += s => { if (s.Engine.Tick == saveTick.Value) Save(s); };
        }
        if (script == "model-safety" && session.HasControllers) await models.RunSafety(session, target!.Value);
        else if (options.GetValueOrDefault("--profile", "false") == "true")
        {
            if (mode != "headless" || script is not ("baseline" or "autonomous") || target is null || saveTick.HasValue)
                throw new ArgumentException("--profile true requires a new headless baseline/autonomous run without checkpoint-at");
            var profile = SessionProfiler.Run(session,target.Value,c=>c.Kind==SelfBallotController.Kind ? SelfBallotController.Restore(c) : AlgorithmicController.Restore(c));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,"profile.json"),JsonSerializer.Serialize(profile,new JsonSerializerOptions(SimulationJson.Options) {WriteIndented=true}));
        }
        else if (target.HasValue)
        {
            while(session.Engine.Tick<target.Value)
            {
                observer?.Boundary();
                while(waitForModel && session.WaitingForModel)
                {observer?.Boundary();models.ProgressWaiting();observer?.Progress("waiting-model");Thread.Sleep(50);}
                long prior=session.Engine.Tick;session.Step();
                if(session.Engine.WaitingForBallots)
                {
                    if(!session.HasControllers && (session.PlaybackFinalTick is null || prior==session.Engine.Tick))throw new InvalidOperationException("ElectionWaitingForBallots");
                    if(session.HasControllers) {session.PumpVoting();if(session.Engine.WaitingForBallots)Thread.Sleep(1);}
                }
                if(!session.Engine.WaitingForBallots && session.Engine.Tick<target.Value)observer?.Capture();
                observer?.Progress(session.Engine.WaitingForBallots?"voting":"running");
            }
            session.AdvanceTo(target.Value); // Submit replay inputs at the final voting boundary first.
            while(session.Engine.WaitingForBallots)
            {
                if(!session.HasControllers)throw new InvalidOperationException("ElectionWaitingForBallots");
                observer?.Boundary();models.ProgressWaiting();session.PumpVoting();observer?.Progress("voting");Thread.Sleep(10);
            }
            observer?.Capture(); // Include recorded inputs at the final barrier.
        }
        else RunScript(session, definition, script);
        // Research profiles may deliberately eliminate profitable production. Fixed fixture targets are not economic rules.
        if (definition.EconomyProfileId is null) BaselineValidation.Check(session,definition);
        if (saveTick.HasValue && !saved) throw new ArgumentException("Checkpoint tick was not reached");
        if (!saveTick.HasValue && options.ContainsKey("--checkpoint")) Save(session);
    }
    catch (ResearchCancelledException)
    {
        watch.Stop();observer?.Capture();observer?.Progress("exporting");
        RunArtifacts.Export(session,definition,directory,mode,script,watch.Elapsed.TotalMilliseconds,"cancelled","Researcher cancelled between steps",models.Metadata);
        observer?.Progress("cancelled");return 2;
    }
    catch (Exception ex)
    {
        observer?.Progress("failed");
        watch.Stop(); RunArtifacts.Export(session, definition, directory, mode, script, watch.Elapsed.TotalMilliseconds, "failed", ex.Message, models.Metadata);
        throw;
    }
    observer?.Progress("exporting");
    watch.Stop(); RunArtifacts.Export(session, definition, directory, mode, script, watch.Elapsed.TotalMilliseconds, modelMetadata: models.Metadata, economyProvenance: economics?.VNext is { } provenance ? new { Metadata=provenance, ProfileFileSha256=VNextBalanceReader.Digest(economyBytes!), ResolvedConfigHash=session.Engine.ConfigHash } : null,startupMilliseconds:startupMilliseconds);
    if (economics is not null)
        File.WriteAllText(Path.Combine(directory,"economy-profile.json"),JsonSerializer.Serialize(economics,new JsonSerializerOptions(SimulationJson.Options) {WriteIndented=true}));
    observer?.Progress("completed");
    Console.WriteLine(JsonSerializer.Serialize(new { Status = "succeeded", Tick = session.Engine.Tick,
        StateHash = session.Engine.ComputeStateHash(), Output = Path.GetFullPath(directory), CheckpointSaved = saved }, SimulationJson.Options));
    return 0;
    void Save(GameSession s) { CheckpointStore.Save(s, options["--checkpoint"]); saved = true; }
    static void RunScript(GameSession s, ScenarioDefinition d, string script)
    {
        if(script=="workshops-d1") ScriptedScenarios.WorkshopsD1(s,d);
        else if(script=="demolition-c2") ScriptedScenarios.DemolitionC2(s,d);
        else if(script=="storage-c1")ScriptedScenarios.StorageC1(s,d);
        else if (script == "vnext-supported-fixture") ScriptedScenarios.VNextSupportedProduction(s,d);
        else if (script == "cycle") ScriptedScenarios.EconomicCycle(s, d);
        else if (script == "recovery") ScriptedScenarios.ZeroCashRecovery(s, d);
        else if (script == "development") ScriptedScenarios.Development(s, d);
        else if (script == "labor") ScriptedScenarios.LaborDynamics(s, d);
        else if (script == "logistics") ScriptedScenarios.LogisticsCycle(s, d);
        else if (script == "own-storage") ScriptedScenarios.OwnStorageCycle(s, d);
        else if (script == "own-production") ScriptedScenarios.OwnProductionCycle(s, d);
        else if (script == "logistics-queue") ScriptedScenarios.LogisticsQueueCycle(s, d);
        else if (script == "support") ScriptedScenarios.SupportCycle(s, d);
        else if(script=="food-chain")ScriptedScenarios.FoodChain(s,d);
        else if(script=="participant-trade")ScriptedScenarios.ParticipantTrade(s,d);
        else if(script=="physical-automation")ScriptedScenarios.PhysicalAutomation(s,d);
        else if (script == "stopped-elections") ScriptedScenarios.StoppedElections(s,d);
        else if (script == "elections") ScriptedScenarios.ElectionsCycle(s, d);
        else if (script == "capital-demand") ScriptedScenarios.CapitalDemandCycle(s, d);
        else if (script == "inbound") ScriptedScenarios.InboundCycle(s, d);
        else if (script == "logistics-upgrade") ScriptedScenarios.LogisticsUpgradeCycle(s, d);
        else if (script is "delivery" or "capital-delivery" or "logistics") ScriptedScenarios.DeliveryLifecycle(s, d, script == "capital-delivery");
        else if (script == "pickup") ScriptedScenarios.PickupLifecycle(s, d);
        else if (script == "market") ScriptedScenarios.MarketWindow(s, d);
        else ScriptedScenarios.EquipmentCycle(s, d);
    }
}
catch (Exception ex) { Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}"); return 1; }
