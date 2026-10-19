using System.Globalization;
using System.Text.Json;
using Godot;
using Simulation.Core;
using Simulation.Hosting;

public partial class PlatformProbe
{
    private int _foodCheckedTicks;
    private string? _foodHash;
    private readonly List<long> _foodRestoredTicks=[];
    private void CheckFoodChain()
    {
        var d=JsonSerializer.Deserialize<ScenarioDefinition>(ResourceText("Fixture.food.json"),SimulationJson.Options)!;
        var tape=ResourceText("Fixture.food.commands.jsonl").Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(l=>JsonSerializer.Deserialize<RecordedCommand>(l,SimulationJson.Options)!).ToArray();
        var hashes=ResourceText("Fixture.food.checkpoints.csv").Split('\n',StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l=>l.Trim().Split(','))
            .ToDictionary(r=>long.Parse(r[0],CultureInfo.InvariantCulture),r=>r[1]);
        var s=new GameSession(new(d));s.ConfigurePlayback(tape,hashes.Keys.Max(),"food-chain");
        bool wip=false,paid=false,alternative=false;
        string path=ProjectSettings.GlobalizePath($"user://platform-food-{SimulationEngine.RulesVersion}.json");
        while(s.Engine.Tick<hashes.Keys.Max())
        {
            s.Step();long tick=s.Engine.Tick;string hash=s.Engine.ComputeStateHash();
            if(hash!=hashes[tick])throw new InvalidOperationException($"Food-chain hash differs at {tick}");
            wip|=s.Engine.Inspect().Enterprises.Any(e=>e.BatchInputs.Length==3);
            paid|=s.Engine.Inspect().MarketAllocations.Any(a=>a.ItemId=="sugar" && a.RemainingQuantity>0);
            alternative|=s.Engine.Inspect().Enterprises.Any(e=>e.RecipeId=="sugar-2" && e.BatchId!=0);
            if(tick is 340 or 405 or 414 or 561 or 562 or 584)
            {
                CheckpointStore.Save(s,path);s=CheckpointStore.Load(path).Session;
                if(s.Engine.ComputeStateHash()!=hash)throw new InvalidOperationException("Food-chain checkpoint changed terms or reservations");
                _foodRestoredTicks.Add(tick);
            }
            _foodCheckedTicks++;
        }
        if(!wip || !paid || !alternative || s.Engine.Inspect().Flows.Single(f=>f.ItemId=="pastry").Exported!=2)
            throw new InvalidOperationException("Food probe missed WIP, purchased ingredient or L2 recipe");
        _foodHash=s.Engine.ComputeStateHash();GD.Print($"DELSK_FOOD_PASS ticks={_foodCheckedTicks} hash={_foodHash}");
    }
}
