using System.Text.Json.Serialization;

namespace Simulation.Core;

public sealed record StorageRules(long GroundCapacity=70,long OverflowGraceTicks=1200,int[]? WarningOffsets=null)
{
    public void Validate()
    {
        if(GroundCapacity<=0 || OverflowGraceTicks is <1 or >1000000 || WarningOffsets is null || WarningOffsets.Any(n=>n<=0||n>=OverflowGraceTicks) || WarningOffsets.Distinct().Count()!=WarningOffsets.Length)throw new ArgumentException("Invalid storage rules");
    }
}
public sealed record OverflowLot(long Id,long Owner,string ItemId,long Quantity,string Source,long CreatedTick,long ExpiresTick,int[] Warned);
public sealed record GroundStorage(long PlotId,long InventoryId);
public sealed record StorageConstruction(long Id,long Owner,Cell Position,string TypeId,int Level,long InventoryId,long BuildingId,long StartedTick,long CompletesTick,MaterialCost[] Materials,long PaidMinor,string CommandId);
public sealed record StorageState(GroundStorage[] Ground,StorageConstruction[] Construction);
