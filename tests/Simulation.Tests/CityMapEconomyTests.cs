using Simulation.Core;
using Simulation.Hosting;
using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace Simulation.Tests;

/// <summary>Показатели карты I6-C5. Считаются из наблюдений, без второго симулятора.</summary>
public class CityMapEconomyTests
{
    private static EnterpriseView Plant(long id,bool enabled,long batch,long done,long need,string reason,string recipe,long finished)
        =>new(new(id),new(1),new(1),new(2),enabled,1,reason,batch,done,need,finished,0,recipe,1,4,1);

    [Fact]
    public void ProductionPercentNeedsAnActiveBatchAndNeverTreatsAStoppedPlantAsFinished()
    {
        Assert.Equal(30,CityMap.BatchPercent(1,30,100));
        Assert.Equal(0,CityMap.BatchPercent(4,0,80));
        Assert.Null(CityMap.BatchPercent(0,0,100));
        Assert.Null(CityMap.BatchPercent(1,30,0));
        Assert.Null(CityMap.BatchPercent(1,101,100));
        Assert.Null(CityMap.BatchPercent(1,-1,100));
        var working=CityMap.Production(Plant(8,true,4,30,100,"Working","plank",2));
        Assert.Equal("Working",working.State);Assert.Equal("plank",working.Detail);Assert.Equal(30,working.Percent());Assert.Equal(2,working.Finished);
        var stopped=CityMap.Production(Plant(9,false,0,0,0,"UserPaused","bread",5));
        Assert.Equal("Stopped",stopped.State);Assert.Null(stopped.Percent());Assert.Equal(CityMapFacts.Absent,stopped.Done);
        var blocked=CityMap.Production(Plant(10,true,0,0,0,"NoInputs","flour",0));
        Assert.Equal("Blocked",blocked.State);Assert.Null(blocked.Percent());Assert.Equal("NoInputs",blocked.Reason);
    }

    [Fact]
    public void CargoFillUsesCapacityAndOccupiedVolumeOnly()
    {
        Assert.Equal(25,CityMap.VolumePercent(20,80));
        Assert.Equal(0,CityMap.VolumePercent(0,10));
        Assert.Equal(100,CityMap.VolumePercent(10,10));
        Assert.Null(CityMap.VolumePercent(1,0));
        Assert.Null(CityMap.VolumePercent(-1,10));
        Assert.Null(CityMap.VolumePercent(11,10));
        var cargo=new InventoryView(new(50),new(1),new(6,6),80,20,5,Array.Empty<StockView>());
        var facts=CityMap.Volume(cargo,"cargo","Parked");
        Assert.Equal(25,facts.Percent());Assert.Equal(5,facts.Ready);Assert.Equal(20,facts.Used);Assert.Equal(80,facts.Room);
    }

    [Fact]
    public void OrdersKeepDeliveredReadyAndInTransitApartAndWarnOnlyInsideARealWindow()
    {
        Assert.Equal(40,CityMap.ObligationPercent(4,10));
        Assert.Null(CityMap.ObligationPercent(-1,10));
        Assert.Null(CityMap.ObligationPercent(4,0));
        Assert.Null(CityMap.ObligationPercent(11,10));
        var open=CityMap.Obligation("Active","plank",4,10,3,2,500,100,0,"contract",true);
        Assert.Equal(40,open.Percent());Assert.Equal(3,open.Ready);Assert.Equal(2,open.Haul);Assert.False(open.Soon);Assert.False(open.Late);
        var late=CityMap.Obligation("Active","plank",4,10,0,1,80,90,0,"contract",true);
        Assert.True(late.Late);Assert.False(late.Soon);Assert.Equal(40,late.Percent());
        var closed=CityMap.Obligation("Fulfilled","plank",10,10,0,0,80,90,0,"contract",false);
        Assert.Equal(100,closed.Percent());Assert.False(closed.Late);
        var waiting=CityMap.Obligation("Pending","grain",CityMapFacts.Absent,6,CityMapFacts.Absent,CityMapFacts.Absent,110,100,10,"service",true);
        Assert.Null(waiting.Percent());Assert.True(waiting.Soon);
        Assert.False(CityMap.DeadlineSoon(100,101,0));
        Assert.True(CityMap.DeadlineSoon(100,110,10));
        Assert.False(CityMap.DeadlineSoon(100,111,10));
        Assert.True(CityMap.DeadlineLate(90,80,true));
        Assert.False(CityMap.DeadlineLate(90,80,false));
    }

    [Fact]
    public void MapReadKeepsOwnFiguresAndDropsForeignRecipesPricesAndContracts()
    {
        var s=StorageC1Tests.Overflowing();string before=s.Engine.ComputeStateHash();
        var view=PlayerDesk.Read(s.Engine,new(1));
        var frame=Presentation.Read(s.Engine.Configuration(),s.Engine,1);
        var own=Plant(901,true,4,30,100,"Working","plank",2);
        var stopped=Plant(902,false,0,0,0,"UserPaused","bread",1);
        var foreign=new EnterpriseView(new(900),new(2),new(3),new(4),true,2,"Working",1,50,50,9,0,"secret-recipe",2,4,1);
        var cargo=new InventoryView(new(50),new(1),new(6,6),80,20,0,new[]{new StockView("hidden-ore",7,0)});
        var model=new VehicleModel("flatbed","Борт",10,80,1000,10);
        var owned=new VehicleView(new OwnedVehicle(77,new(1),"flatbed",new(50)),model,new(6,6),"Parked",default);
        var fleet=new FleetObservation(new(0,0),ImmutableArray.Create(model),default,ImmutableArray.Create(owned),default,default,default,default);
        var contract=new DeliveryContractView(9,0,0,new(1),new(1),null,new(4,4),"plank",10,424242,default,default,default,0,"",1,500,4,Status:"Active",PreparedQuantity:3,InTransitQuantity:2);
        var foreignContract=new DeliveryContractView(10,0,0,new(2),new(3),null,new(5,5),"secret-goods",8,777001,default,default,default,0,"",1,400,1);
        view=view with
        {
            Enterprises=view.Enterprises.Concat(new[]{own,stopped,foreign}).ToArray(),
            Inventories=view.Inventories.Append(cargo).ToArray(),
            Contracts=(view.Contracts??Array.Empty<DeliveryContractView>()).Append(contract).Append(foreignContract).ToArray(),
            Fleet=fleet
        };
        frame=frame with
        {
            Markers=frame.Markers.Concat(new[]
            {
                new PresentationMarker("production",1,1,"woodworking",901,1){Level=1},
                new PresentationMarker("production",2,2,"bakery",902,1){Level=1},
                new PresentationMarker("production",3,3,"bakery",900,2){Level=2}
            }).ToArray(),
            Vehicles=frame.Vehicles.Add(new PresentationVehicle(77,1,"flatbed",6,6,0,0,0,-1,false))
        };
        var map=CityMap.Read(frame,view);
        Assert.Equal(before,s.Engine.ComputeStateHash());
        var running=Assert.Single(map.Objects,o=>o.Id==901&&o.Kind=="production");
        Assert.Equal(30,running.Facts!.Percent());Assert.Equal("Working",running.Facts.State);Assert.Equal("plank",running.Facts.Detail);
        var idle=Assert.Single(map.Objects,o=>o.Id==902);
        Assert.Equal("Stopped",idle.Facts!.State);Assert.Null(idle.Facts.Percent());
        var hidden=Assert.Single(map.Objects,o=>o.Id==900);
        Assert.Null(hidden.Facts);Assert.Equal(2,hidden.Owner);
        var truck=Assert.Single(map.Objects,o=>o.Key=="vehicle:77");
        Assert.Equal(25,truck.Facts!.Percent());Assert.Equal("cargo",truck.Facts.Family);
        var incoming=Assert.Single(map.Objects,o=>o.Kind=="incoming"&&o.Id==9);
        Assert.Equal(40,incoming.Facts!.Percent());Assert.Equal(3,incoming.Facts.Ready);Assert.Equal(2,incoming.Facts.Haul);Assert.Equal(6,incoming.Quantity);
        Assert.DoesNotContain(map.Objects,o=>o.Id==10||o.Label=="secret-goods");
        string data=JsonSerializer.Serialize(map,SimulationJson.Options);
        Assert.DoesNotContain("secret-recipe",data,StringComparison.Ordinal);
        Assert.DoesNotContain("secret-goods",data,StringComparison.Ordinal);
        Assert.DoesNotContain("hidden-ore",data,StringComparison.Ordinal);
        Assert.DoesNotContain("424242",data,StringComparison.Ordinal);
        Assert.DoesNotContain("777001",data,StringComparison.Ordinal);
        Assert.DoesNotContain("stock",data,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("escrow",data,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unitPrice",data,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(("building",901L),CityMap.Card(running,1));
        Assert.Null(CityMap.Card(hidden,1));
        Assert.Equal("fleet",CityMap.Section(truck,1));
        Assert.Equal("holdings",CityMap.Section(incoming,1));
    }

    [Fact]
    public void SelectionZoomFiltersAndFocusSurviveAFreshRead()
    {
        var p=new CityMapPreferences();p.Bind(32,25);p.ZoomAt(2.5,40,30,200,160);p.SelectedKey="production:901:1:1:woodworking";p.Participant=1;p.ApplyFocus(CityMapFocus.Orders);
        var memory=p.Capture()!;
        var again=new CityMapPreferences();again.Bind(32,25);again.Restore(memory);
        Assert.Equal(CityMapFocus.Orders,again.Focus);Assert.Equal(CityLayer.Deliveries,again.Layers);
        Assert.Equal("production:901:1:1:woodworking",again.SelectedKey);Assert.Equal(1,again.Participant);Assert.Equal(p.Zoom,again.Zoom);Assert.Equal(p.CenterX,again.CenterX);Assert.Equal(p.CenterZ,again.CenterZ);
        string legacy=JsonSerializer.Serialize(memory with{Focus=CityMapFocus.All});
        var read=JsonSerializer.Deserialize<CityMapMemory>(legacy)!;
        Assert.Equal(CityMapFocus.All,read.Focus);
        again.Restore(read);Assert.Equal(CityMapFocus.All,again.Focus);Assert.Equal(again.SelectedKey,p.SelectedKey);
        Assert.Throws<ArgumentException>(()=>again.Restore(memory with{Focus=(CityMapFocus)99}));
        var plant=new CityMapObject("production:1:0:0:wood",CityLayer.Industry,"production",1,1,new(0,0),new[]{new Cell(0,0)},"wood");
        var truck=new CityMapObject("vehicle:2",CityLayer.People,"vehicle",2,1,new(1,1),new[]{new Cell(1,1)},"truck");
        var order=new CityMapObject("incoming:3",CityLayer.Deliveries,"incoming",3,1,new(2,2),new[]{new Cell(2,2)},"plank");
        Assert.True(CityMap.Shown(plant,CityLayer.All,0,CityMapFocus.Production));
        Assert.False(CityMap.Shown(truck,CityLayer.All,0,CityMapFocus.Production));
        Assert.True(CityMap.Shown(truck,CityLayer.All,0,CityMapFocus.Transport));
        Assert.True(CityMap.Shown(order,CityLayer.All,0,CityMapFocus.Orders));
        Assert.False(CityMap.Shown(plant,CityLayer.People,0,CityMapFocus.All));
        Assert.Equal(("equipment",5L),CityMap.Card(plant with{Kind="equipment",Id=5,Key="equipment:5"},1));
        Assert.Null(CityMap.Card(plant,2));
        Assert.Equal("services",CityMap.Section(order with{Kind="delivery"},1));
        Assert.Equal("purchases",CityMap.Section(order with{Kind="pickup"},1));
    }
}
