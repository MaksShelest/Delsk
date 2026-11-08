using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>Read-only client actions. AccessFor is the same participant-safe query used by NPCs.</summary>
public static class ContextActions
{
    public static string Scope(PlayerView v) => v.Participant.Id.Value+":"+v.Participant.Position+":"+
        string.Join(',',v.Buildings.Where(b=>b.Footprint.Contains(v.Participant.Position)).Select(b=>"b"+b.Id.Value)
        .Concat((v.Equipment??[]).Where(e=>e.Position==v.Participant.Position).Select(e=>"e"+e.Id.Value))
        .Concat((v.Fleet?.Vehicles??[]).Where(e=>e.Position==v.Participant.Position).Select(e=>"v"+e.Vehicle.Id)));

    public static ContextChoice[] Read(SimulationEngine engine,ParticipantId actor,ScenarioDefinition scenario,string language)
    {
        var v=PlayerDesk.Read(engine,actor);var cell=v.Participant.Position;
        string L(string ru,string en)=>PlayerText.Local(language,ru,en);
        var choices=PlayContext.At(v,scenario,language,true).Where(c=>!c.Key.StartsWith("resource:info:",StringComparison.Ordinal)).ToList();
        choices.RemoveAll(c=>c.Command?.Kind==ActionKind.BoardVehicle&&(v.Equipment??[]).Any(e=>e.Id.Value==c.Command.VehicleId&&e.Deployed==true));
        for(int n=0;n<choices.Count;n++)
        {
            var c=choices[n];
            if(c.Key.StartsWith("card:",StringComparison.Ordinal))choices[n]=c with{Label=L("Открыть предприятие","Open enterprise")};
            if(c.Key.StartsWith("object:building:",StringComparison.Ordinal))choices[n]=c with{Label=L("Открыть склад","Open storage")};
            if(c.Command?.Kind==ActionKind.StartHarvesting&&c.Command.Equipment.Value==0)
                choices[n]=c with{Label=L("Добыть","Harvest")};
            if(c.Key.StartsWith("vehicle:",StringComparison.Ordinal)&&v.Fleet?.Vehicles.FirstOrDefault(e=>"vehicle:"+e.Vehicle.Id==c.Key) is {} vehicle&&vehicle.Model.EquipmentTypeId is {} type)
                choices[n]=c with{Label=L("Техника: ","Equipment: ")+PlayerText.Item(language,scenario.EquipmentTypes!.Single(e=>e.Id==type).ItemId)+" · "+(vehicle.Vehicle.Driver==0?L("сесть","board"):L("выйти","leave"))};
        }
        void Add(string key,string text,GameAction action)=>choices.Add(new(key,text,action));

        foreach(var e in (v.Equipment??[]).Where(e=>e.Position==cell&&e.Deployed is not null))
        {
            var type=scenario.EquipmentTypes!.Single(t=>t.Id==e.TypeId);var level=type.Levels.Single(l=>l.Level==e.Level);
            if(e.Deployed==true)
            {
                if(e.Mode!=EquipmentMode.Off||e.PersonalJobId!=0)
                    Add("machine:stop:"+e.Id.Value,L("Остановить технику","Stop equipment"),new(ActionKind.SetEquipmentMode,Equipment:e.Id,Mode:EquipmentMode.Off));
                else
                {
                    Add("machine:start:"+e.Id.Value,L("Начать добычу","Start mining"),new(ActionKind.StartHarvesting,Equipment:e.Id,ItemId:type.ItemId,Quantity:level.CycleQuantity));
                    Add("machine:auto:"+e.Id.Value,L("Автоматическая добыча","Automatic extraction"),new(ActionKind.SetEquipmentMode,Equipment:e.Id,Mode:EquipmentMode.Automatic));
                }
                Add("machine:pack:"+e.Id.Value,L("Забрать технику","Pick up equipment"),new(ActionKind.PackEquipment,Equipment:e.Id));
            }
            else Add("machine:place:"+e.Id.Value,L("Разместить технику","Place equipment"),new(ActionKind.PlaceEquipment,Equipment:e.Id));
        }
        // The world dock offers routes into the existing goods chooser, never an item matrix.
        choices.RemoveAll(c=>c.Command?.Kind==ActionKind.Transfer||c.Key.StartsWith("build:",StringComparison.Ordinal));
        var pack=v.Inventories.Single(i=>i.Id==v.Participant.InventoryId);
        foreach(var inventory in v.Inventories.Where(i=>i.Id!=pack.Id&&i.OwnerId==actor))
        {
            void Transfer(InventoryView from,InventoryView to,string verb)
            {
                if(v.Participant.Busy)return;
                bool possible=from.Stock.Any(s=>s.Quantity>s.Reserved&&
                    ManualTransferChoice.Read(scenario,v,from.Id,to.Id,s.ItemId,a=>engine.AccessFor(actor,a),false,1).Enabled);
                if(possible)choices.Add(new($"local-transfer:{from.Id.Value}:{to.Id.Value}",verb+" · "+InventoryName(v,inventory.Id,language),null));
            }
            Transfer(pack,inventory,L("Положить","Store"));Transfer(inventory,pack,L("Забрать","Take"));
        }
        if(v.Fleet is {} fleet)
        {
        var ordered=fleet.Stock.Where(s=>engine.AccessFor(actor,new(ActionKind.BuyVehicle,VehicleId:s.Id)).Allowed)
            .OrderBy(s=>Math.Abs(s.Position.X-cell.X)+Math.Abs(s.Position.Z-cell.Z)).ThenBy(s=>s.Id).ToArray();
        var car=ordered.FirstOrDefault(s=>fleet.Models.Single(m=>m.Id==s.ModelId).EquipmentTypeId is null);
        // Beside a display, proximity wins. At the entrance retain a compact catalog sample.
        var offers=cell!=fleet.Dealer?ordered.Take(3):ordered.Where(s=>s!=car)
            .OrderBy(s=>v.AvailableCashMinor<fleet.Models.Single(m=>m.Id==s.ModelId).PriceMinor).Take(car is null?3:2);
        foreach(var listing in (cell!=fleet.Dealer||car is null?offers:offers.Prepend(car)))
        {
            var action=new GameAction(ActionKind.BuyVehicle,VehicleId:listing.Id);
            // Core defines the dealer's physical interaction point (not a UI radius).
            if(!engine.AccessFor(actor,action).Allowed)continue;
            var model=fleet.Models.Single(m=>m.Id==listing.ModelId);
            bool enabled=!v.Participant.Busy&&fleet.OwnRequests.Length==0&&v.AvailableCashMinor>=model.PriceMinor;
            string name=model.EquipmentTypeId is {} type?L("Добыча: ","Extract: ")+PlayerText.Item(language,scenario.EquipmentTypes!.Single(t=>t.Id==type).ItemId)+
                (model.EquipmentLevel==1?L(" · малая"," · small"):L(" · большая"," · large")):model.Name;
            choices.Add(new("dealer:buy:"+listing.Id,name+" · "+L("Купить за ","Buy for ")+PlayerText.Money(model.PriceMinor)+L(" монет"," coins"),action,enabled,
                enabled?"":PlayerText.Reason(language,v.Participant.Busy?"ParticipantBusy":fleet.OwnRequests.Length>0?"NotAvailable":"NoMoney")));
        }
        }
        return choices.Select(c=>
        {
            if(c.Command is not {} a)return c;
            var access=engine.AccessFor(actor,a);
            if(!access.Allowed)return c with{Enabled=false,Details=PlayerText.Reason(language,access.ReasonCode)};
            if(a.Kind==ActionKind.Transfer)
            {
                var dest=v.Inventories.FirstOrDefault(i=>i.Id==a.Destination);
                if(dest is not null&&dest.OccupiedVolume+dest.ReservedCapacity+scenario.Items.Single(i=>i.Id==a.ItemId).UnitVolume>dest.Capacity)
                    return c with{Enabled=false,Details=PlayerText.Reason(language,"NoCapacity")};
            }
            if(v.Participant.Busy&&a.Kind is not (ActionKind.StopHarvest or ActionKind.StopTransfer or ActionKind.SetEquipmentMode))
                return c with{Enabled=false,Details=PlayerText.Reason(language,"ParticipantBusy")};
            if(!c.Enabled&&c.Details.Length==0)return c with{Details=PlayerText.Reason(language,a.Kind is ActionKind.BuyResource or ActionKind.LeaseResource?"NoMoney":"NotAvailable")};
            return c;
        }).Where(c=>c.Enabled||c.Command?.Kind is ActionKind.BuyVehicle or ActionKind.BuyPlot or ActionKind.ReservePlot or ActionKind.BuyResource or ActionKind.LeaseResource)
        .OrderBy(Priority).ThenBy(c=>c.Key,StringComparer.Ordinal).ToArray();
    }

    private static string InventoryName(PlayerView v,InventoryId id,string language)
    {
        string L(string ru,string en)=>PlayerText.Local(language,ru,en);
        if(v.Fleet?.Vehicles.Any(e=>e.Vehicle.CargoInventory==id)==true)return L("кузов","cargo");
        if(v.Buildings.Any(b=>b.InputId==id&&b.Kind==BuildingKind.Warehouse))return L("склад","storage");
        if(v.Buildings.Any(b=>b.InputId==id&&b.Kind==BuildingKind.Shop))return L("лавка","shop");
        if(v.Buildings.Any(b=>b.OutputId==id))return L("выпуск","output");
        if(v.Buildings.Any(b=>b.InputId==id))return L("вход","input");
        if(v.Equipment?.Any(e=>e.StorageId==id)==true)return L("техника","equipment");
        return L("запас #","stock #")+id.Value;
    }
    public static int Priority(ContextChoice c)=>c.Command?.Kind switch
    {
        ActionKind.StopHarvest or ActionKind.StopTransfer=>0,
        ActionKind.SetEquipmentMode when c.Command.Mode==EquipmentMode.Off=>1,
        ActionKind.StartHarvesting when c.Command.Equipment.Value!=0=>5,
        ActionKind.BoardVehicle or ActionKind.LeaveVehicle=>10,
        ActionKind.BuyVehicle=>18,
        ActionKind.PlaceEquipment=>12,ActionKind.PackEquipment=>14,
        ActionKind.StartHarvesting or ActionKind.Harvest=>15,
        _=>c.Key.StartsWith("road-work:",StringComparison.Ordinal)?3:c.Key.StartsWith("object:",StringComparison.Ordinal)||c.Key.StartsWith("card:",StringComparison.Ordinal)?20:c.Command?.Kind==ActionKind.Transfer?30:c.Key.StartsWith("section:",StringComparison.Ordinal)?90:40
    };
    public static string ResultText(string language,CommandResult? result)=>result is null?PlayerText.Get(language,"command.sent"):result.Status==ResultStatus.Rejected?PlayerText.Reason(language,result.ReasonCode):PlayerText.Get(language,"command.done");
    public static string Group(ContextChoice c)=>c.Command?.Kind switch
    {
        ActionKind.BoardVehicle or ActionKind.LeaveVehicle or ActionKind.TurnVehicle=>"transport",
        ActionKind.Harvest or ActionKind.StartHarvesting or ActionKind.StopHarvest or ActionKind.Transfer or ActionKind.StopTransfer or ActionKind.PlaceEquipment or ActionKind.PackEquipment or ActionKind.SetEquipmentMode or ActionKind.BuyResource or ActionKind.LeaseResource=>"resources",
        ActionKind.Build or ActionKind.ReservePlot or ActionKind.BuyPlot or ActionKind.CancelPlotReservation=>"production",
        ActionKind.SellAvailableToCapital=>"trade",
        _=>c.Key.StartsWith("section:",StringComparison.Ordinal)?"management":c.Key.StartsWith("market:",StringComparison.Ordinal)||c.Key=="exchange"?"trade":c.Key.StartsWith("road-work:",StringComparison.Ordinal)||c.Key.StartsWith("card:",StringComparison.Ordinal)?"production":c.Key=="dealership"?"transport":"interaction"
    };
    public static string GroupName(string key,string language)=>key switch
    {
        "transport"=>PlayerText.Local(language,"Транспорт","Transport"),"resources"=>PlayerText.Local(language,"Ресурсы и хранение","Resources and storage"),
        "production"=>PlayerText.Local(language,"Производство и строительство","Production and construction"),"trade"=>PlayerText.Local(language,"Торговля и договоры","Trade and contracts"),
        "management"=>PlayerText.Local(language,"Управление","Management"),_=>PlayerText.Local(language,"Взаимодействие","Interaction")
    };
}

/// <summary>Ephemeral selection, never serialized into the game. No numeric popup IDs.</summary>
public sealed class ContextSelection
{
    public string Scope {get;private set;}="";
    public string SelectedKey {get;private set;}="";
    public bool Expanded {get;set;}
    public bool Pending {get;set;}
    public ContextChoice[] Choices {get;private set;}=[];
    public void Update(string scope,ContextChoice[] choices)
    {
        if(scope!=Scope){Scope=scope;Expanded=false;SelectedKey="";}
        Choices=choices;
        if(!choices.Any(c=>c.Key==SelectedKey))SelectedKey=choices.FirstOrDefault()?.Key??"";
    }
    public void Select(string key){if(Choices.Any(c=>c.Key==key))SelectedKey=key;}
    public void Cycle(int delta)
    {if(Choices.Length>0)SelectedKey=Choices[(Array.FindIndex(Choices,c=>c.Key==SelectedKey)+delta+Choices.Length)%Choices.Length].Key;}
    public bool TryChoose(ContextChoice pressed,out ContextChoice? current)
    {
        current=Choices.FirstOrDefault(c=>c.Key==pressed.Key);
        if(current is not {Enabled:true}||Pending&&current.Command is not null||current.Command!=pressed.Command)return false;
        SelectedKey=current.Key;if(current.Command is not null)Pending=true;return true;
    }
    public string[] PrimaryKeys(bool friendly = false)
    {
        if (!friendly)
        {
            var local = Choices.Where(c => ContextActions.Priority(c) < 80).OrderBy(c => !c.Enabled).Take(3).Select(c => c.Key).ToArray();
            return local.Length > 0 ? local : Choices.Take(1).Select(c => c.Key).ToArray();
        }
        var ranked = Choices.OrderBy(c => c.Enabled ? 0 : 1).ThenBy(FriendlyGuidance.Rank).ThenBy(c => c.Key, StringComparer.Ordinal);
        var picked = ranked.Where(c => FriendlyGuidance.Rank(c) < 70 && (c.Enabled || FriendlyGuidance.ShowDisabled(c))).Take(3).Select(c => c.Key).ToArray();
        return picked.Length > 0 ? picked : Choices.Take(1).Select(c => c.Key).ToArray();
    }
}
