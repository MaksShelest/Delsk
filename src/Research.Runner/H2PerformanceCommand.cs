using System.Diagnostics;
using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;
using Agents.Algorithmic;

internal static class H2PerformanceCommand
{
    internal static int Run(string[] args)
    {
        string directory="";GameSession? session=null;var metrics=new SessionMeasurements();var wall=Stopwatch.StartNew();string stage="arguments";
        long initialTick=0;string? error=null;string? finalWorldHash=null,finalControllerHash=null;
        try
        {
            var options=new Dictionary<string,string>();for(int i=0;i<args.Length;i+=2){if(i+1>=args.Length||args[i] is not("--measure-session" or "--output" or "--ticks" or "--operation"))throw new ArgumentException("Invalid measure-session arguments");options.Add(args[i],args[i+1]);}
            directory=options["--output"];if(Directory.Exists(directory)&&Directory.EnumerateFileSystemEntries(directory).Any())throw new ArgumentException("Measurement output must be empty");Directory.CreateDirectory(directory);
            stage="load";Save();var mark=metrics.Start();session=CheckpointStore.Load(options["--measure-session"]).Session;session.RestoreControllers(c=>c.Kind==SelfBallotController.Kind?SelfBallotController.Restore(c):AlgorithmicController.Restore(c));metrics.End("load-total",mark);session.Measurements=metrics;initialTick=session.Engine.Tick;
            SessionMeasurements.Mark phaseMark=default;session.Engine.StepPhaseBoundary+=phase=>{if(phase>=0)metrics.End("core-phase-"+phase,phaseMark);phaseMark=metrics.Start();};
            SessionMeasurements.Mark invariantMark=default;session.Engine.InvariantBoundary+=name=>{if(name!="$begin")metrics.End("invariant-"+name,invariantMark);invariantMark=metrics.Start();};
            long final=options.TryGetValue("--ticks",out var t)?long.Parse(t):initialTick;stage="step";Save();
            while(session.Engine.Tick<final){session.Step();if(session.Engine.Tick%10==0)Save();}
            if(session.Engine.WaitingForBallots&&!session.PumpVoting())throw new InvalidOperationException("WaitingForBallots");
            finalWorldHash=session.Engine.ComputeStateHash();finalControllerHash=session.ComputeControllerStateHash();
            string operation=options.GetValueOrDefault("--operation","step");
            if(operation=="save")
            {stage="save";Save();mark=metrics.Start();CheckpointStore.Save(session,Path.Combine(directory,"world.save.json"));metrics.End("save-with-validation",mark);}
            else if(operation=="export")
            {stage="export";Save();mark=metrics.Start();RunArtifacts.Export(session,session.Engine.Configuration(),Path.Combine(directory,"exports"),"H2-measure","loaded",wall.Elapsed.TotalMilliseconds);metrics.End("export",mark);}
            else if(operation=="tail")
            {
                stage="trace-tail";Save();mark=metrics.Start();int count=0;
                using(var writer=new StreamWriter(Path.Combine(directory,"trace-tail.jsonl")))
                    foreach(var turn in session.ControllerTurnsTail(256))
                    {
                        string observation=JsonSerializer.Serialize(turn.Observation,SimulationJson.Options);
                        string hash=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(observation)));
                        writer.WriteLine(JsonSerializer.Serialize(new{turn.Tick,turn.Actor,ObservationSha256=hash,PreviewCharacters=Math.Min(observation.Length,16384)},SimulationJson.Options));++count;
                    }
                if(count!=Math.Min(256,session.ControllerTurnCount))throw new InvalidOperationException("Incomplete bounded history tail");
                metrics.End("trace-tail-256",mark);
            }
            else if(operation!="step")throw new ArgumentException("Unknown operation");
            stage="succeeded";Save();return 0;
        }
        catch(Exception ex){error=ex.GetType().Name+": "+ex.Message;stage="failed";Save();Console.Error.WriteLine(error);return 1;}
        void Save()
        {
            if(directory.Length==0)return;Directory.CreateDirectory(directory);var process=Process.GetCurrentProcess();
            File.WriteAllText(Path.Combine(directory,"phases.json"),JsonSerializer.Serialize(new{Stage=stage,Error=error,InitialTick=initialTick,Tick=session?.Engine.Tick,FinalWorldHash=finalWorldHash,FinalControllerHash=finalControllerHash,WallSeconds=wall.Elapsed.TotalSeconds,Metrics=metrics.Capture(),WorkingSetBytes=process.WorkingSet64,PeakWorkingSetBytes=process.PeakWorkingSet64,ManagedBytes=GC.GetTotalMemory(false),AllocatedBytes=GC.GetTotalAllocatedBytes(false),ObservationArchive=session?.ObservationArchiveStatistics(),MovementProfile=session?.Engine.Configuration().Economy?.Movement is {Version:2} movement?$"movement-2-multiplier-{movement.Multiplier}-divisor-{movement.SpeedDivisor}":session?.Engine.UsesAcceleratedMovement==true?"movement-1-divisor-32":"legacy-divisor-80",Scope="Host phase measurements; sampling includes hash; nested buckets must not be added. Rules are loaded unchanged from the save."},new JsonSerializerOptions(SimulationJson.Options){WriteIndented=true}));
        }
    }
}
