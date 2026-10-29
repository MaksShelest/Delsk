using System.Diagnostics;
using System.Text.Json;
using Simulation.Hosting;
using Simulation.Core;

/// <summary>Opt-in lightweight research heartbeat. No inspection, sampling, commands or Core mutation.
/// Author: Шелестов Максим Евгеньевич.</summary>
internal sealed class RunProgress
{
    private readonly string _path;
    private readonly GameSession _session;
    private readonly long _target,_start;
    private readonly Stopwatch _clock=Stopwatch.StartNew();
    private long _last=-1;
    private double _lastSeconds=-2;
    public RunProgress(string directory,GameSession session,long target)
    {
        Directory.CreateDirectory(directory);_path=Path.Combine(directory,"run-progress.json");
        _session=session;_target=target;_start=session.Engine.Tick;Write(true);
    }
    public void Write(bool force=false,string status="running")
    {
        long tick=_session.Engine.Tick;double seconds=_clock.Elapsed.TotalSeconds;
        if(!force&&(tick==_last||tick%60!=0&&seconds-_lastSeconds<2))return;
        using var process=Process.GetCurrentProcess();
        var value=new{Version=1,Status=status,Tick=tick,Target=_target,StartTick=_start,
            Percent=100d*(tick-_start)/Math.Max(1,_target-_start),ElapsedSeconds=seconds,
            TicksPerSecond=seconds>0?(tick-_start)/seconds:0,WorkingSetBytes=process.WorkingSet64,PeakWorkingSetBytes=process.PeakWorkingSet64,
            _session.WaitingForModel,_session.Engine.WaitingForBallots,HeartbeatAt=DateTimeOffset.UtcNow,
            Scope="Runner process memory; host/observer elapsed time, not a device benchmark"};
        File.WriteAllText(_path+".tmp",JsonSerializer.Serialize(value,SimulationJson.Options));File.Move(_path+".tmp",_path,true);
        _last=tick;_lastSeconds=seconds;
    }
}
