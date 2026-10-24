using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Simulation.Hosting;

/// <summary>Strict, bounded structural validation only. Never constructs a world or changes runtime JSON options.</summary>
public static class VNextCatalogReader
{
    public const int MaximumBytes = 1024 * 1024;
    public const int MaximumDepth = 32;
    private sealed class Invalid(string code, string path, string message) : Exception(message)
    { public VNextCatalogDiagnostic Diagnostic { get; } = new(code, path, message); }
    private static readonly Regex IdPattern = new("\\A[a-z][a-z0-9_-]{0,63}\\z", RegexOptions.CultureInvariant);
    private static readonly Regex ShaPattern = new("\\A[0-9a-fA-F]{64}\\z", RegexOptions.CultureInvariant);

    public static VNextCatalogValidation Load(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumBytes) return Failure("TooLarge", "$", "Catalog exceeds 1 MiB; file was not read.");
            // Also bound the read if the source grows after the length check.
            byte[] bytes = new byte[MaximumBytes + 1];
            int length = 0, read;
            while (length < bytes.Length && (read = stream.Read(bytes, length, bytes.Length - length)) > 0) length += read;
            if (length > MaximumBytes) return Failure("TooLarge", "$", "Catalog exceeds 1 MiB.");
            return Read(bytes.AsMemory(0, length));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return Failure("IoError", "$", e.Message); }
    }

    public static VNextCatalogValidation Read(ReadOnlyMemory<byte> utf8)
    {
        if (utf8.Length > MaximumBytes) return Failure("TooLarge", "$", "Catalog exceeds 1 MiB.");
        string digest = Convert.ToHexStringLower(SHA256.HashData(utf8.Span));
        VNextCatalog? parsed = null;
        try
        {
            // Accept an UTF-8 BOM, while retaining it in the exact-file digest.
            var json = utf8.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? utf8[3..] : utf8;
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaximumDepth });
            Unique(document.RootElement, "$");
            parsed = Parse(document.RootElement);
            ValidateReferencesAndArithmetic(parsed);
            return new(parsed, digest, parsed.FormatVersion, parsed.Id, Counts(parsed), []);
        }
        catch (Invalid e)
        { return new(null, digest, parsed?.FormatVersion, parsed?.Id, parsed is null ? null : Counts(parsed), [e.Diagnostic]); }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        { return new(null, digest, null, null, null, [new("InvalidJson", "$", e.Message)]); }
    }

    private static VNextCatalogValidation Failure(string code, string path, string message) => new(null, null, null, null, null, [new(code, path, message)]);
    private static VNextCatalogCounts Counts(VNextCatalog c) => new(c.Items.Length, c.Recipes.Length, c.Industries.Length);
    private static string Key(string path, string name) => path + "[" + JsonSerializer.Serialize(name) + "]";
    private static void Require(bool valid, string code, string path, string message)
    { if (!valid) throw new Invalid(code, path, message); }

    private static void Unique(JsonElement node, string path)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in node.EnumerateObject())
            {
                Require(names.Add(p.Name), "DuplicateKey", Key(path, p.Name), "Duplicate JSON property.");
                Unique(p.Value, Key(path, p.Name));
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (var item in node.EnumerateArray()) Unique(item, path + "[" + i++ + "]");
        }
    }

    private static void Object(JsonElement node, string path, params string[] fields)
    {
        Kind(node, JsonValueKind.Object, path);
        foreach (var p in node.EnumerateObject())
            Require(fields.Contains(p.Name, StringComparer.Ordinal), "UnknownField", Key(path, p.Name), "Unknown catalog field.");
        foreach (string field in fields)
        {
            Require(node.TryGetProperty(field, out var value), "MissingField", path + "." + field, "Required field is missing.");
            Require(value.ValueKind != JsonValueKind.Null, "NullValue", path + "." + field, "Null is not allowed.");
        }
    }

    private static void Kind(JsonElement n, JsonValueKind kind, string path)
    {
        Require(n.ValueKind != JsonValueKind.Null, "NullValue", path, "Null is not allowed.");
        Require(n.ValueKind == kind, "InvalidType", path, "Expected " + kind + ".");
    }
    private static string Text(JsonElement n, string path)
    { Kind(n, JsonValueKind.String, path); return n.GetString()!; }
    private static string Id(JsonElement n, string path)
    {
        string value = Text(n, path);
        Require(IdPattern.IsMatch(value), "InvalidId", path, "Use [a-z][a-z0-9_-]{0,63}.");
        return value;
    }
    private static string Name(JsonElement n, string path)
    {
        string value = Text(n, path);
        Require(!string.IsNullOrWhiteSpace(value) && value.Length <= 160, "InvalidName", path, "Name must contain 1..160 characters.");
        return value;
    }
    private static long Number(JsonElement n, string path, long minimum = 1, long maximum = long.MaxValue)
    {
        Kind(n, JsonValueKind.Number, path);
        Require(n.TryGetInt64(out long value), "InvalidInteger", path, "Expected a JSON integer in the Int64 range; no rounding.");
        Require(value >= minimum && value <= maximum, "OutOfRange", path, $"Expected {minimum}..{maximum}.");
        return value;
    }
    private static ImmutableArray<T> List<T>(JsonElement n, string path, Func<JsonElement, string, T> parse)
    {
        Kind(n, JsonValueKind.Array, path);
        Require(n.GetArrayLength() > 0, "EmptyCollection", path, "Catalog sections must be nonempty.");
        var result = ImmutableArray.CreateBuilder<T>(); int i = 0;
        foreach (var item in n.EnumerateArray()) result.Add(parse(item, path + "[" + i++ + "]"));
        return result.ToImmutable();
    }
    private static ImmutableSortedDictionary<string, long> Quantities(JsonElement n, string path, int maximum)
    {
        Kind(n, JsonValueKind.Object, path);
        int count = n.EnumerateObject().Count();
        Require(count >= 1 && count <= maximum, "InvalidQuantitySet", path, $"Use 1..{maximum} different items.");
        var result = ImmutableSortedDictionary.CreateBuilder<string, long>(StringComparer.Ordinal);
        foreach (var p in n.EnumerateObject())
        {
            Require(IdPattern.IsMatch(p.Name), "InvalidId", Key(path, p.Name), "Invalid item ID.");
            result.Add(p.Name, Number(p.Value, Key(path, p.Name)));
        }
        return result.ToImmutable();
    }
    private static VNextCatalog Parse(JsonElement n)
    {
        Object(n, "$", "format_version", "id", "status", "source_sha256", "items", "recipes", "industries");
        string version = Text(n.GetProperty("format_version"), "$.format_version");
        Require(version == VNextCatalog.CurrentFormat, "UnsupportedVersion", "$.format_version", "Unsupported catalog format.");
        string status = Text(n.GetProperty("status"), "$.status");
        Require(status == "catalog_only", "InvalidStatus", "$.status", "Only catalog_only is supported.");
        string sha = Text(n.GetProperty("source_sha256"), "$.source_sha256");
        Require(ShaPattern.IsMatch(sha), "InvalidSha256", "$.source_sha256", "Expected 64 hexadecimal SHA-256 characters.");
        return new(version, Id(n.GetProperty("id"), "$.id"), status, sha,
            List(n.GetProperty("items"), "$.items", (x, p) =>
            {
                Object(x, p, "id", "name", "unit_volume", "reference_price_minor", "xp_per_unit", "source");
                string source = Text(x.GetProperty("source"), p + ".source");
                Require(source is "resource" or "import" or "production", "InvalidSource", p + ".source", "Use resource, import or production.");
                return new VNextCatalogItem(Id(x.GetProperty("id"), p + ".id"), Name(x.GetProperty("name"), p + ".name"),
                    Number(x.GetProperty("unit_volume"), p + ".unit_volume"), Number(x.GetProperty("reference_price_minor"), p + ".reference_price_minor"),
                    Number(x.GetProperty("xp_per_unit"), p + ".xp_per_unit", 0), source);
            }),
            List(n.GetProperty("recipes"), "$.recipes", (x, p) =>
            {
                Object(x, p, "id", "industry", "unlock_level", "inputs", "output", "output_quantity", "work_ticks_full_staff", "service_minor_per_work_tick");
                return new VNextCatalogRecipe(Id(x.GetProperty("id"), p + ".id"), Id(x.GetProperty("industry"), p + ".industry"),
                    (int)Number(x.GetProperty("unlock_level"), p + ".unlock_level", 1, 5), Quantities(x.GetProperty("inputs"), p + ".inputs", 8),
                    Id(x.GetProperty("output"), p + ".output"), Number(x.GetProperty("output_quantity"), p + ".output_quantity"),
                    Number(x.GetProperty("work_ticks_full_staff"), p + ".work_ticks_full_staff"), Number(x.GetProperty("service_minor_per_work_tick"), p + ".service_minor_per_work_tick", 0));
            }),
            List(n.GetProperty("industries"), "$.industries", (x, p) =>
            {
                Object(x, p, "id", "name", "construction_price_minor", "materials");
                return new VNextCatalogIndustry(Id(x.GetProperty("id"), p + ".id"), Name(x.GetProperty("name"), p + ".name"),
                    Number(x.GetProperty("construction_price_minor"), p + ".construction_price_minor"), Quantities(x.GetProperty("materials"), p + ".materials", int.MaxValue));
            }));
    }

    private static void UniqueIds(IEnumerable<string> ids, string path)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal); int i = 0;
        foreach (string id in ids)
        { Require(seen.Add(id), "DuplicateId", path + "[" + i++ + "].id", "Duplicate ID: " + id); }
    }
    private static void ValidateReferencesAndArithmetic(VNextCatalog catalog)
    {
        UniqueIds(catalog.Items.Select(x => x.Id), "$.items");
        UniqueIds(catalog.Recipes.Select(x => x.Id), "$.recipes");
        UniqueIds(catalog.Industries.Select(x => x.Id), "$.industries");
        var items = catalog.Items.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var industries = catalog.Industries.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        VNextCatalogItem Item(string id, string path)
        { Require(items.ContainsKey(id), "UnknownReference", path, "Unknown ItemId: " + id); return items[id]; }
        (long Volume, long Value) Totals(IEnumerable<KeyValuePair<string, long>> quantities, string path)
        {
            long volume = 0, value = 0;
            foreach (var (id, quantity) in quantities)
            {
                var item = Item(id, Key(path, id));
                volume = checked(volume + checked(quantity * item.UnitVolume));
                value = checked(value + checked(quantity * item.ReferencePriceMinor));
            }
            return (volume, value);
        }
        string current = "$";
        try
        {
            for (int i = 0; i < catalog.Recipes.Length; i++)
            {
                var r = catalog.Recipes[i]; current = "$.recipes[" + i + "]";
                Require(industries.Contains(r.Industry), "UnknownReference", current + ".industry", "Unknown industry: " + r.Industry);
                var input = Totals(r.Inputs, current + ".inputs");
                var output = Item(r.Output, current + ".output");
                _ = checked(r.OutputQuantity * output.UnitVolume);
                _ = checked(r.OutputQuantity * output.ReferencePriceMinor);
                _ = checked(r.OutputQuantity * output.XpPerUnit);
                _ = checked(input.Value + checked(r.WorkTicksFullStaff * r.ServiceMinorPerWorkTick));
            }
            for (int i = 0; i < catalog.Industries.Length; i++)
            {
                current = "$.industries[" + i + "]";
                var industry = catalog.Industries[i];
                _ = checked(Totals(industry.Materials, current + ".materials").Value + industry.ConstructionPriceMinor);
            }
        }
        catch (OverflowException) { throw new Invalid("Overflow", current, "Catalog batch/construction arithmetic exceeds Int64."); }
    }
}
