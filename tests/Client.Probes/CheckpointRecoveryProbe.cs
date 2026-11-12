using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Godot;
using Simulation.Core;
using Simulation.Hosting;
using File = System.IO.File;

/// <summary>Exercises the normal Continue button with isolated user save files.</summary>
public partial class CheckpointRecoveryProbe : Control
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public override void _Ready() => _ = Run();

    private async Task Run()
    {
        string[] args = OS.GetCmdlineUserArgs();
        string output = args[Array.IndexOf(args, "--output") + 1];
        Directory.CreateDirectory(output);
        try
        {
            var game = await OpenGame();
            string path = (string)typeof(Playfield).GetProperty("SavePath", Private)!.GetValue(game)!;
            var resolved = (OrdinaryGameResolution)Call(game, "ResolveNewGame", PlayMaps.CompactValley, 3, true)!;
            var original = new GameSession(new(resolved.Scenario));
            CheckpointStore.Save(original, path + ".bak");
            byte[] backup = File.ReadAllBytes(path + ".bak");
            Call(game, "ShowMaps");
            await Frame();
            await ContinueFromMenu(game);
            Check(Current(game)?.Engine.ComputeStateHash() == original.Engine.ComputeStateHash(), "Backup-only slot loaded a different world");
            Check(game.FindChild("RecoveredSaveNotice", true, false) is Label { Visible: true }, "Recovery notice was missing");
            Call(game, "SaveGame");
            Check(CheckpointStore.Load(path).Session.Engine.Tick == 0, "Backup-only slot did not get a new primary");
            game.QueueFree();
            await Frame();

            byte[] damaged = "{truncated"u8.ToArray();
            File.WriteAllBytes(path, damaged);
            game = await OpenGame();
            await ContinueFromMenu(game);
            var recovered = Current(game)!;
            Check(recovered.Engine.Tick == 0, "Damaged primary did not fall back to the backup");
            recovered.SetPaused(false);
            recovered.Step();
            Call(game, "SaveGame");
            Check(CheckpointStore.Load(path).Session.Engine.Tick == 1, "Recovered progress could not be saved");
            Check(File.ReadAllBytes(path + ".bak").SequenceEqual(backup), "The good backup was overwritten");
            string quarantine = Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".corrupt-*").Single();
            Check(File.ReadAllBytes(quarantine).SequenceEqual(damaged), "Damaged bytes were lost");

            var unknownPolicy = new GameSession(new(resolved.Scenario));
            unknownPolicy.AttachControllers([new UnknownPolicy()]);
            CheckpointStore.Save(unknownPolicy, path);
            try { Call(game, "Load"); throw new InvalidOperationException("Unknown policy was accepted"); }
            catch (TargetInvocationException failure) when (failure.InnerException is CheckpointException { Message: "UnknownControllerVersion" }) { }
            Check(ReferenceEquals(Current(game), recovered), "An unsuccessful restore replaced the running session");
            game.QueueFree();
            await Frame();

            File.WriteAllBytes(path, damaged);
            File.WriteAllBytes(path + ".bak", damaged);
            game = await OpenGame();
            await ContinueFromMenu(game);
            Check(Current(game) is null && game.FindChild("SaveError", true, false) is AcceptDialog { Visible: true },
                "Failed recovery did not leave a usable menu with an error");
            GD.Print("CHECKPOINT_RECOVERY_PASS backup-only damaged-primary preserve-bytes controller-rollback double-failure");
            File.WriteAllText(Path.Combine(output, "native.json"), JsonSerializer.Serialize(new { status = "succeeded", cases = 5 }));
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            File.WriteAllText(Path.Combine(output, "native.json"), JsonSerializer.Serialize(new { status = "failed", error = error.ToString() }));
            GetTree().Quit(1);
        }
    }

    private async Task<Playfield> OpenGame()
    {
        var game = GD.Load<PackedScene>("res://Playfield.tscn").Instantiate<Playfield>();
        AddChild(game);
        game.SetProcess(false);
        await Frame();
        return game;
    }

    private async Task ContinueFromMenu(Playfield game)
    {
        var button = game.FindChild("ContinueGame", true, false) as Button;
        Check(button is { Disabled: false }, "Continue was unavailable despite the saved files");
        button!.EmitSignal(Button.SignalName.Pressed);
        await Frame();
        await Frame();
    }

    private async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    private static GameSession? Current(Playfield game) => (GameSession?)typeof(Playfield).GetField("_session", Private)!.GetValue(game);
    private static object? Call(Playfield game, string method, params object[] args) => typeof(Playfield).GetMethod(method, Private)!.Invoke(game, args);
    private static void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }

    private sealed class UnknownPolicy : IController
    {
        public ParticipantId Actor => new(2);
        public ControllerCheckpoint Capture() => new("unsupported-fixture", 1, Actor, "{}", []);
        public ControllerDecision Decide(ControllerObservation observation, ImmutableArray<CommandResult> ownResults) => throw new NotSupportedException();
        public void CommandSubmitted(string commandId) => throw new NotSupportedException();
    }
}
