using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using Simulation.Core;

namespace Simulation.Hosting;

internal record CheckpointEnvelope(int FormatVersion, string PayloadJson, string Sha256);
public record CheckpointLoadResult(GameSession Session, bool UsedBackup, string LoadedPath);

/// <summary>Concrete, bounded single-writer file store. No file or wall-clock dependency enters Core.</summary>
public static class CheckpointStore
{
    public const int MaximumBytes = 64 * 1024 * 1024;
    public const int MaximumDecodedBytes = 512 * 1024 * 1024;
    public const int FormatVersion = 2;
    private static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Encode(GameSession session)
    {
        string payload = session.CreateSessionJson();
        byte[] raw = Encoding.UTF8.GetBytes(payload);
        if (raw.Length > MaximumDecodedBytes) throw new CheckpointException("CheckpointTooLarge");
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen:true)) gzip.Write(raw);
        string encoded = Convert.ToBase64String(compressed.ToArray());
        string json = JsonSerializer.Serialize(new CheckpointEnvelope(FormatVersion, encoded, Digest(encoded)), SimulationJson.Options);
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new CheckpointException("CheckpointTooLarge");
        return json;
    }
    public static GameSession Decode(string json, Action? beforeStep = null)
    {
        if (json is null || Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new CheckpointException("CheckpointTooLarge");
        try
        {
            var envelope = JsonSerializer.Deserialize<CheckpointEnvelope>(json, SimulationJson.Options)
                ?? throw new CheckpointException("EmptyCheckpointEnvelope");
            if (envelope.FormatVersion is not (1 or FormatVersion)) throw new CheckpointException("UnknownCheckpointFormat");
            if (envelope.PayloadJson is null || envelope.Sha256 != Digest(envelope.PayloadJson))
                throw new CheckpointException("CheckpointChecksumMismatch");
            string payload = envelope.PayloadJson;
            if (envelope.FormatVersion == FormatVersion)
            {
                using var input = new MemoryStream(Convert.FromBase64String(payload));
                using var gzip = new GZipStream(input,CompressionMode.Decompress);
                using var output = new MemoryStream();
                byte[] buffer = new byte[8192]; int count;
                while ((count = gzip.Read(buffer)) > 0)
                {
                    if (output.Length + count > MaximumDecodedBytes) throw new CheckpointException("CheckpointTooLarge");
                    output.Write(buffer,0,count);
                }
                payload = new UTF8Encoding(false,throwOnInvalidBytes:true).GetString(output.ToArray());
            }
            return GameSession.FromSessionJson(payload, beforeStep);
        }
        catch (JsonException ex) { throw new CheckpointException("InvalidCheckpointJson", ex); }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or DecoderFallbackException)
        { throw new CheckpointException("InvalidCompressedCheckpoint",ex); }
    }
    public static void Save(GameSession session, string path)
    {
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temporary = full + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(Encode(session));
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            _ = ReadFile(temporary); // Check checksum, schema, references, invariants and hash before installation.
            if (File.Exists(full))
            {
                _ = ReadFile(full); // Never rotate a corrupt main file over an existing valid backup.
                File.Replace(temporary, full, full + ".bak");
            }
            else File.Move(temporary, full); // Same-directory first installation; refuse competing writers.
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static GameSession ReadFile(string path, Action? beforeStep = null)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumBytes) throw new CheckpointException("CheckpointTooLarge");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Decode(reader.ReadToEnd(), beforeStep);
    }
    public static CheckpointLoadResult Load(string path, bool recoverBackup = false, Action? beforeStep = null)
    {
        string full = Path.GetFullPath(path);
        try { return new(ReadFile(full, beforeStep), false, full); }
        catch (Exception ex) when (recoverBackup && ex is CheckpointException or IOException)
        { return new(ReadFile(full + ".bak", beforeStep), true, full + ".bak"); }
    }
}
