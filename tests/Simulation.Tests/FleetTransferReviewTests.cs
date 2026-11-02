using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class FleetTransferReviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransferBetweenTwoOwnedVehiclesCompletesAndContinuesExactly(bool roads)
    {
        var session = new GameSession(new(roads ? RoadE2Tests.Prepared() : FleetE1Tests.Prepared()));
        // The first two dealer listings share an ordinary two-car display cell.
        var first = FleetE1Tests.Buy(session, "sputnik20");
        var second = FleetE1Tests.Buy(session, "muravey70");
        Assert.Equal(StorageC1Tests.Inventory(session, first.CargoInventory).Position,
            StorageC1Tests.Inventory(session, second.CargoInventory).Position);
        StorageC1Tests.Harvest(session, "wood", 5);
        StorageC1Tests.Put(session, first.CargoInventory, "wood", 5);
        StorageC1Tests.Step(session, new(ActionKind.Transfer, Source: first.CargoInventory,
            Destination: second.CargoInventory, ItemId: "wood", Quantity: 5));
        Assert.Single(session.Engine.TransfersOf(new(1)));
        var restored = CheckpointStore.Decode(CheckpointStore.Encode(session));

        session.Step();
        restored.Step();

        Assert.Empty(session.Engine.TransfersOf(new(1)));
        Assert.Equal(0, FleetE1Tests.Stock(session, first.CargoInventory, "wood"));
        Assert.Equal(5, FleetE1Tests.Stock(session, second.CargoInventory, "wood"));
        Assert.Equal(session.Engine.ComputeStateHash(), restored.Engine.ComputeStateHash());
        var replay = GameSession.Replay(session.Engine.Configuration(), session.CommandTape, session.Engine.Tick);
        Assert.Equal(session.Engine.ComputeStateHash(), replay.Engine.ComputeStateHash());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyTheDrivenEndpointSuppliesItsHandlingKit(bool driveDestination)
    {
        var session = new GameSession(new(FleetE1Tests.Prepared()));
        var source = FleetE1Tests.Buy(session, "sputnik20");
        var destination = FleetE1Tests.Buy(session, "muravey70");
        var kit = session.Engine.Configuration().Economy!.Fleet!.Kits.Single(k => k.Level == 1);
        foreach (var material in kit.Materials)
            StorageC1Tests.Harvest(session, material.ItemId, material.Quantity);
        StorageC1Tests.Move(session, StorageC1Tests.Inventory(session, destination.CargoInventory).Position);
        StorageC1Tests.Step(session, new(ActionKind.InstallVehicleKit, VehicleId: destination.Id, EquipmentLevel: 1));
        session.AdvanceTo(session.Engine.InspectFleet()!.Vehicles.Single(v => v.Id == destination.Id).Installation!.CompletionTick);
        StorageC1Tests.Harvest(session, "wood", 10);
        StorageC1Tests.Put(session, source.CargoInventory, "wood", 10);
        StorageC1Tests.Step(session, new(ActionKind.BoardVehicle, VehicleId: driveDestination ? destination.Id : source.Id));
        StorageC1Tests.Step(session, new(ActionKind.Transfer, Source: source.CargoInventory,
            Destination: destination.CargoInventory, ItemId: "wood", Quantity: 10));
        var restored = CheckpointStore.Decode(CheckpointStore.Encode(session));
        session.Step(); restored.Step();

        Assert.Equal(driveDestination ? 10 : 5, FleetE1Tests.Stock(session, destination.CargoInventory, "wood"));
        Assert.Equal(session.Engine.ComputeStateHash(), restored.Engine.ComputeStateHash());
        StorageC1Tests.Idle(session); StorageC1Tests.Idle(restored);
        Assert.Equal(10, FleetE1Tests.Stock(session, destination.CargoInventory, "wood"));
        Assert.Equal(session.Engine.ComputeStateHash(), restored.Engine.ComputeStateHash());
        Assert.Equal(session.Engine.ComputeStateHash(),
            GameSession.Replay(session.Engine.Configuration(), session.CommandTape, session.Engine.Tick).Engine.ComputeStateHash());
    }
}
