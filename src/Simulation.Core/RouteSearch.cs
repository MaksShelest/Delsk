namespace Simulation.Core;

/// <summary>Canonical weighted grid search shared by authoritative and public route forecasts.</summary>
public static class RouteSearch
{
    private static readonly Cell[] Directions = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    public static Cell[] Find(Cell source,Cell target,Func<Cell,bool> allow,Func<Cell,Cell,long> cost)
    {
        var queue=new PriorityQueue<Cell,(long,int)>();var distances=new Dictionary<Cell,long>{{source,0}};
        var previous=new Dictionary<Cell,Cell>();int ordinal=0;queue.Enqueue(source,(0,ordinal++));
        while(queue.TryDequeue(out var cell,out var priority))
        {
            if(priority.Item1!=distances[cell])continue;
            if(cell==target){var path=new List<Cell>{cell};while(cell!=source){cell=previous[cell];path.Add(cell);}path.Reverse();return path.ToArray();}
            foreach (var direction in Directions)
            {
                var next = new Cell(cell.X + direction.X, cell.Z + direction.Z);
                if(!allow(next))continue;long distance=checked(priority.Item1+cost(cell,next));
                if(distances.TryGetValue(next,out var old)&&old<=distance)continue;
                distances[next]=distance;previous[next]=cell;queue.Enqueue(next,(distance,ordinal++));
            }
        }
        return [];
    }
    private readonly record struct Oriented(Cell Cell,Cell Heading);
    public static Cell[] FindOriented(Cell source,Cell target,Func<Cell,bool> allow,Func<Cell,Cell,long> cost,Func<Cell,bool> mayReverse,Cell heading,Func<Cell,Cell,Cell,long>? turnCost=null)
    {
        var start=new Oriented(source,heading);var queue=new PriorityQueue<Oriented,(long,int)>();
        var distances=new Dictionary<Oriented,long>{{start,0}};var previous=new Dictionary<Oriented,Oriented>();int ordinal=0;queue.Enqueue(start,(0,ordinal++));
        while(queue.TryDequeue(out var state,out var priority))
        {
            if(priority.Item1!=distances[state])continue;
            if(state.Cell==target){var path=new List<Cell>{state.Cell};while(state!=start){state=previous[state];path.Add(state.Cell);}path.Reverse();return path.ToArray();}
            foreach (var direction in Directions)
            {
                if(direction.X==-state.Heading.X&&direction.Z==-state.Heading.Z&&!mayReverse(state.Cell))continue;
                var next=new Oriented(new(state.Cell.X+direction.X,state.Cell.Z+direction.Z),direction);if(!allow(next.Cell))continue;
                long distance=checked(priority.Item1+cost(state.Cell,next.Cell)+(turnCost?.Invoke(state.Cell,state.Heading,direction)??0));if(distances.TryGetValue(next,out var old)&&old<=distance)continue;
                distances[next]=distance;previous[next]=state;queue.Enqueue(next,(distance,ordinal++));
            }
        }
        return [];
    }
}
