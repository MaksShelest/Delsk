using System.Text.Json;
using Simulation.Core;

namespace Simulation.Hosting;

public sealed record SavedWorldSummary(string MapId, long Tick, int Participants, long CashMinor, int Enterprises);
public sealed record SavedWorld(string Id, string? Name, string? CustomMapName, string Mode,
    DateTimeOffset? SavedAt, SavedWorldSummary? Summary);

/// <summary>File catalog for one edition and rules version. Checkpoints remain the source of truth.</summary>
public sealed class SavedWorldStore
{
    public const int MaximumNameLength = 60;
    private const string CheckpointFile = "checkpoint.json";
    private const string CardFile = "world.json";
    private static readonly string[] Modes = ["economy", "politics", "trip"];
    private readonly string _userDirectory;
    private readonly string _editionSuffix;
    private readonly string _directory;

    private sealed record Stamp(long Length, DateTime WrittenUtc);
    private sealed record Card(string? Name, string? CustomMapName, string Mode, Stamp? Checkpoint, SavedWorldSummary? Summary);

    public SavedWorldStore(string userDirectory)
    {
        _userDirectory = Path.GetFullPath(userDirectory);
        _editionSuffix = SimulationEngine.IsDeveloperBuild ? "-developer" : "";
        _directory = Path.Combine(_userDirectory, $"worlds-v1-{SimulationEngine.RulesVersion}{_editionSuffix}");
    }

    public string LegacyPath(string mode)
    {
        if (!Modes.Contains(mode, StringComparer.Ordinal)) throw new ArgumentException("Unknown game mode", nameof(mode));
        return Path.Combine(_userDirectory, $"play-{mode}-{SimulationEngine.RulesVersion}{_editionSuffix}.json");
    }

    public static bool IsImported(string id) => Modes.Any(mode => id == "legacy-" + mode);

    public static bool ValidName(string name) => name.Trim().Length is > 0 and <= MaximumNameLength && !name.Any(char.IsControl);

    public IReadOnlyList<SavedWorld> List()
    {
        if (!Directory.Exists(_directory)) return [];
        return Directory.EnumerateDirectories(_directory)
            .Select(Path.GetFileName).OfType<string>().Where(ValidId).Select(Read)
            .OrderByDescending(world => world.SavedAt).ThenBy(world => world.Id, StringComparer.Ordinal).ToArray();
    }

    public string? LastActive()
    {
        var id = ReadSmallFile(Path.Combine(_directory, "last-active"));
        return id is not null && ValidId(id) && Directory.Exists(WorldDirectory(id)) ? id : null;
    }

    public void SetActive(string id)
    {
        using var guard = LockCatalog();
        RequireWorld(id);
        WriteSmallFile(Path.Combine(_directory, "last-active"), id);
    }

    public string Create(GameSession session, string? name = null, string mode = "economy")
    {
        ValidateName(name);
        if (!Modes.Contains(mode, StringComparer.Ordinal)) throw new ArgumentException("Unknown game mode", nameof(mode));
        using var guard = LockCatalog();
        string id = Guid.NewGuid().ToString("N");
        Install(id, staged =>
        {
            string path = Path.Combine(staged, CheckpointFile);
            CheckpointStore.Save(session, path);
            WriteCard(staged, Describe(session, name?.Trim(), mode, FileStamp(path)));
        });
        WriteSmallFile(Path.Combine(_directory, "last-active"), id);
        return id;
    }

    public void Save(string id, GameSession session)
    {
        using var guard = LockCatalog();
        string folder = RequireWorld(id);
        string path = Path.Combine(folder, CheckpointFile);
        var card = ReadCard(folder);
        CheckpointStore.Save(session, path, preserveDamagedMain: true, protectExistingWorld: true);
        WriteCard(folder, Describe(session, card?.Name, ModeOf(card, id), FileStamp(path)));
    }

    public SavedWorld Get(string id) => Read(id);

    public string CheckpointPath(string id) => Path.Combine(WorldDirectory(id), CheckpointFile);

    public CheckpointLoadResult Load(string id)
    {
        // File replacement is atomic. Hold the catalog lock while choosing between the two generations.
        using var guard = LockCatalog();
        return LoadCheckpoint(Path.Combine(RequireWorld(id), CheckpointFile));
    }

    public void Rename(string id, string name)
    {
        ValidateName(name);
        using var guard = LockCatalog();
        string folder = RequireWorld(id);
        var card = ReadCard(folder) ?? new(null, null, DefaultMode(id), null, null);
        WriteCard(folder, card with { Name = name.Trim() });
    }

    public void Delete(string id)
    {
        using var guard = LockCatalog();
        string folder = RequireWorld(id);
        string deleted = Path.Combine(_directory, ".deleted-" + id);
        // The rename removes the world from the catalog and records legacy deletion in one operation.
        Directory.Move(folder, deleted);
        if (IsImported(id))
        {
            foreach (var file in Directory.EnumerateFiles(deleted)) File.Delete(file);
        }
        else Directory.Delete(deleted, recursive: true);
    }

    public string? ImportLegacy(string mode, Action<GameSession>? validateControllers = null)
    {
        string source = LegacyPath(mode);
        string id = "legacy-" + mode;
        if (FileStamp(source) is null && FileStamp(source + ".bak") is null) return null;
        using var guard = LockCatalog();
        if (Directory.Exists(WorldDirectory(id))) return id;
        if (Directory.Exists(Path.Combine(_directory, ".deleted-" + id))) return null;

        var loaded = LoadCheckpoint(source);
        Install(id, staged =>
        {
            string path = Path.Combine(staged, CheckpointFile);
            string installed = loaded.UsedBackup ? path + ".bak" : path;
            File.Copy(loaded.LoadedPath, installed);
            var verified = LoadCheckpoint(path);
            validateControllers?.Invoke(verified.Session);
            if (!verified.UsedBackup) CopyLegacyBackup(source + ".bak", path + ".bak", verified.Session);
            WriteCard(staged, Describe(verified.Session, null, mode, FileStamp(installed)));
        });
        // The original files are retained, including if the migration was interrupted.
        return id;
    }

    private static void CopyLegacyBackup(string source, string destination, GameSession current)
    {
        if (FileStamp(source) is not { Length: <= CheckpointStore.MaximumBytes }) return;
        try
        {
            File.Copy(source, destination);
            var previous = CheckpointStore.Load(destination).Session;
            var tape = previous.CommandTape;
            // The single-file client could have rotated a different campaign into this backup.
            if (previous.SessionId == current.SessionId && previous.Engine.ConfigHash == current.Engine.ConfigHash
                && (previous.Engine.Tick, previous.Engine.TravelQuantum).CompareTo((current.Engine.Tick, current.Engine.TravelQuantum)) <= 0
                && tape.SequenceEqual(current.CommandTape.Take(tape.Length))) return;
        }
        catch (CheckpointException) { }
        File.Delete(destination);
    }

    private void Install(string id, Action<string> prepare)
    {
        string staged = Path.Combine(_directory, ".pending-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staged);
        try
        {
            prepare(staged);
            Directory.Move(staged, WorldDirectory(id));
        }
        finally
        {
            if (Directory.Exists(staged)) Directory.Delete(staged, recursive: true);
        }
    }

    private FileStream LockCatalog()
    {
        Directory.CreateDirectory(_directory);
        return new FileStream(Path.Combine(_directory, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static CheckpointLoadResult LoadCheckpoint(string path) =>
        CheckpointStore.Load(path, recoverBackup: true, beforeStep: null, protectExistingWorld: true);

    private SavedWorld Read(string id)
    {
        string folder = WorldDirectory(id);
        var card = ReadCard(folder);
        Stamp? stamp = null;
        try { stamp = FileStamp(Path.Combine(folder, CheckpointFile)) ?? FileStamp(Path.Combine(folder, CheckpointFile + ".bak")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        bool matches = stamp is not null && stamp == card?.Checkpoint;
        return new(id, card?.Name, card?.CustomMapName, ModeOf(card, id), matches ? stamp!.WrittenUtc : null,
            matches ? card!.Summary : null);
    }

    private static Card Describe(GameSession session, string? name, string mode, Stamp? stamp)
    {
        var engine = session.Engine;
        var scenario = engine.Configuration();
        var own = engine.Observe(new(scenario.NewGame?.HumanActorId ?? scenario.Participants[0].Id));
        string mapId = scenario.NewGame?.SourceMapId ?? scenario.Id;
        var summary = new SavedWorldSummary(mapId, engine.Tick, scenario.Participants.Length,
            own.Account.BalanceMinor, own.OwnEnterprises.Length);
        return new(name, UserMaps.IsUserId(mapId) ? scenario.Layout?.SourceName : null, mode, stamp, summary);
    }

    private string WorldDirectory(string id)
    {
        if (!ValidId(id)) throw new ArgumentException("Invalid saved world ID", nameof(id));
        return Path.Combine(_directory, id);
    }

    private string RequireWorld(string id)
    {
        string folder = WorldDirectory(id);
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("SavedWorldMissing");
        return folder;
    }

    private static string DefaultMode(string id) => IsImported(id) ? id["legacy-".Length..] : "economy";
    private static string ModeOf(Card? card, string id) =>
        card?.Mode is { } mode && Modes.Contains(mode, StringComparer.Ordinal) ? mode : DefaultMode(id);
    private static bool ValidId(string id) => Guid.TryParseExact(id, "N", out _) || IsImported(id);
    private static void ValidateName(string? name)
    {
        if (name is not null && !ValidName(name)) throw new ArgumentException("Invalid saved world name", nameof(name));
    }

    private static Stamp? FileStamp(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? new(file.Length, file.LastWriteTimeUtc) : null;
    }

    private static Card? ReadCard(string folder)
    {
        string? json = ReadSmallFile(Path.Combine(folder, CardFile));
        if (json is null) return null;
        try { return JsonSerializer.Deserialize<Card>(json, SimulationJson.Options); }
        catch (JsonException) { return null; }
    }

    private static string? ReadSmallFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length > 8192) return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private static void WriteCard(string folder, Card card) =>
        WriteSmallFile(Path.Combine(folder, CardFile), JsonSerializer.Serialize(card, SimulationJson.Options));

    private static void WriteSmallFile(string path, string contents)
    {
        string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.Write(contents);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
