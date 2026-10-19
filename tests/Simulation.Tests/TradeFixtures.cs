using Simulation.Core;

namespace Simulation.Tests;

internal static class TradeFixtures
{
    // Explicit fixture setup, not a production bypass: every trader owns a real shop and plot.
    internal static ScenarioDefinition WithShops(ScenarioDefinition d,params long[] actors)
    {
        if(actors.Length==0)actors=d.Participants.Select(p=>p.Id).ToArray();
        var plots=(d.Plots??[]).ToList();var buildings=(d.Buildings??[]).ToList();var types=(d.BuildingTypes??[]).ToList();
        var type=types.FirstOrDefault(t=>t.Kind==BuildingKind.Shop && t.Footprint.Length==1);
        if(type is null){type=new("fixture-shop",BuildingKind.Shop,[new(0,0)],[new(1,"",1,[],200,0,0,0)]);types.Add(type);}
        foreach(var e in d.Enterprises)
        {
            string name="fixture-production-"+e.Id;
            var cell=e.Position;
            while(plots.Any(p=>p.Position==cell))cell=new(cell.X+1,cell.Z);
            types.Add(new(name,BuildingKind.Production,[new(0,0)],[new(1,e.RecipeId,1,[],e.InputCapacity,e.OutputCapacity,e.Capacity,e.WageMinor)]));
            plots.Add(new(plots.Select(p=>p.Id).DefaultIfEmpty(0).Max()+1,cell,1,OwnerId:e.OwnerId));
            buildings.Add(new(e.Id,e.OwnerId,cell,name,Staff:e.Staff,Enabled:e.Enabled));
        }
        long plotId=plots.Select(p=>p.Id).DefaultIfEmpty(0).Max()+1,buildingId=buildings.Select(b=>b.Id).DefaultIfEmpty(0).Max()+1;
        int n=0;bool shared=plots.Any(p=>p.IsShopSite);
        foreach(long actor in actors)
        {
            if(buildings.Any(b=>b.OwnerId==actor && types.Single(t=>t.Id==b.TypeId).Kind==BuildingKind.Shop))continue;
            var cell=new Cell(n%d.MapWidth,d.MapHeight+n/d.MapWidth);n++;
            plots.Add(new(plotId++,cell,1,OwnerId:actor,ShopParticipantId:shared?null:actor,IsShopSite:shared));
            buildings.Add(new(buildingId++,actor,cell,type.Id));
        }
        return d with {Enterprises=[],MapHeight=d.MapHeight+(n==0?0:(n+d.MapWidth-1)/d.MapWidth),Plots=[..plots],Buildings=[..buildings],BuildingTypes=[..types]};
    }
    internal static ScenarioDefinition Scenario() => WithShops(Fixtures.Cycle() with {
        Id="participant-trade-tests",MaxTicks=1000,Population=20,Participants=[new(1,100000,new(0,0),20),new(2,100000,new(0,0),20),new(3,100000,new(0,0),20)],
        Enterprises=[],CapitalOffers=[],Elections=new(100),Support=new(new(5,0)),Economy=new(Handling:new(1,1)),
        BuildingTypes=[new("warehouse",BuildingKind.Warehouse,[new(0,0)],[new(1,"",1,[],200,0,0,0)])],
        Plots=[new(1,new(2,0),1,OwnerId:1),new(2,new(4,0),1,OwnerId:2)],Buildings=[new(1,1,new(2,0),"warehouse"),new(2,2,new(4,0),"warehouse")]});
}
