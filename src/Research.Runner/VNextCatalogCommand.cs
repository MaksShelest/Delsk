using System.Text.Json;
using Simulation.Hosting;

internal static class VNextCatalogCommand
{
    public static int Run(string[] args)
    {
        string? temporary = null;
        try
        {
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length || args[i] is not ("--validate-vnext-catalog" or "--output") ||
                    string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                    !options.TryAdd(args[i], args[i + 1]))
                    throw new ArgumentException("Use only --validate-vnext-catalog PATH --output DIRECTORY, once each.");
            }
            if (options.Count != 2) throw new ArgumentException("Catalog and output are required.");
            string output = Path.GetFullPath(options["--output"]);
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                throw new IOException("Output must be a new or empty dedicated directory; existing results were not changed.");
            Directory.CreateDirectory(output);
            // Own temporary file + non-overwriting atomic move, also safe against competing writers.
            temporary = Path.Combine(output, ".catalog-" + Guid.NewGuid().ToString("N") + ".tmp");
            var result = VNextCatalogReader.Load(options["--validate-vnext-catalog"]);
            var report = new
            {
                status = result.IsValid ? "succeeded" : "failed", scope = "structure",
                formatVersion = result.FormatVersion, catalogId = result.CatalogId, sha256 = result.Sha256,
                counts = result.Counts, diagnostics = result.Diagnostics
            };
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(file, report, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
            File.Move(temporary, Path.Combine(output, "catalog-validation.json"), overwrite: false);
            temporary = null;
            if (result.IsValid) Console.WriteLine("Catalog structure valid: " + result.CatalogId);
            else foreach (var d in result.Diagnostics) Console.Error.WriteLine($"{d.Code} {d.Path}: {d.Message}");
            return result.IsValid ? 0 : 2;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { Console.Error.WriteLine("Catalog validation: " + e.Message); return 2; }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
