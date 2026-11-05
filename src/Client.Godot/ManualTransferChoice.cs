using Simulation.Core;
using Simulation.Hosting;

/// <summary>UI-only preview of an existing single-item Transfer. Core still validates the submitted command.</summary>
public readonly record struct ManualTransferChoice(GameAction Action,long Maximum,bool Enabled,string Reason)
{
    public static long MaximumQuantity(InventoryView source,InventoryView destination,string item,long unitVolume)
    {
        if(source.Id==destination.Id||unitVolume<=0)return 0;
        long free=source.Stock.Where(s=>s.ItemId==item).Sum(s=>s.Quantity-s.Reserved);
        var room=Int128.Max(0,(Int128)destination.Capacity-destination.OccupiedVolume-destination.ReservedCapacity)/unitVolume;
        return (long)Int128.Min(free,room);
    }
    private static bool Accepts(ScenarioDefinition scenario,PlayerView view,InventoryId destination,string item)
    {
        if(view.ProductionContent is not {} content)return true;
        if(content.Pickup==destination)return false;
        var building=view.Buildings.SingleOrDefault(b=>b.Kind==BuildingKind.Production&&(b.InputId==destination||b.OutputId==destination));
        if(building is null)return true;
        var products=view.Development!.Rules.Industries.Single(i=>i.Id==building.TypeId).Products;
        if(building.OutputId==destination)return products.Any(p=>p.ItemId==item);
        var type=scenario.BuildingTypes!.Single(t=>t.Id==building.TypeId);
        return products.Any(p=>scenario.Recipes.Single(r=>r.Id==p.RecipeId).Uses(item))||type.Levels.SelectMany(l=>l.Materials).Any(m=>m.ItemId==item)||
            type.WorkshopOutfit!.Materials.Any(m=>m.ItemId==item)||content.Rules.Modernizations.SelectMany(m=>m.Materials).Any(m=>m.ItemId==item);
    }
    public static ManualTransferChoice Read(ScenarioDefinition scenario,PlayerView view,InventoryId sourceId,InventoryId destinationId,string item,
        Func<GameAction,ActionAccess> access,bool pending,long? quantity=null)
    {
        var source=view.Inventories.SingleOrDefault(i=>i.Id==sourceId);var destination=view.Inventories.SingleOrDefault(i=>i.Id==destinationId);
        long maximum=source is null||destination is null?0:MaximumQuantity(source,destination,item,scenario.Items.Single(i=>i.Id==item).UnitVolume);
        long q=quantity??maximum;var action=PlayerDesk.Transfer(sourceId.Value,destinationId.Value,item,Math.Max(1,q));string reason="";
        if(pending)reason="Pending";
        else if(view.Transfers.Length>0)reason="TransferActive";
        else if(view.Participant.Busy)reason="ParticipantBusy";
        else if(source is null||destination is null||sourceId==destinationId)reason="InvalidTransfer";
        else if(!Accepts(scenario,view,destinationId,item))reason="InventoryItemNotAllowed";
        else if(source.Stock.Where(s=>s.ItemId==item).Sum(s=>s.Quantity-s.Reserved)<=0)reason="NoFreeStock";
        else if(maximum<=0)reason="NoCapacity";
        else if(q<=0||q>maximum)reason="TransferTooLarge";
        else {var reach=access(action);if(!reach.Allowed)reason=reach.ReasonCode;}
        return new(action,maximum,reason.Length==0,reason);
    }
    public string Explain(string language)=>Reason switch
    {
        "Pending"=>PlayerText.Local(language,"Действие ожидает выполнения.","An action is pending."),
        "TransferActive"=>PlayerText.Local(language,"Дождитесь окончания текущей передачи или остановите её.","Finish or stop the active transfer first."),
        "NoFreeStock"=>PlayerText.Local(language,"Нет свободного товара; зарезервированный запас не переносится.","No unreserved stock is available."),
        "NoCapacity"=>PlayerText.Local(language,"У получателя нет свободного места.","No free capacity at the destination."),
        "TransferTooLarge"=>PlayerText.Local(language,"Уменьшите количество или выберите «Всё».","Reduce the quantity or choose All."),
        "InvalidTransfer"=>PlayerText.Local(language,"Выберите разные источник и получателя.","Choose different source and destination inventories."),
        ""=>"",_=>PlayerText.Reason(language,Reason)
    };
}
