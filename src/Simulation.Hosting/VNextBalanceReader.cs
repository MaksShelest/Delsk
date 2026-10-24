using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Simulation.Hosting;

public sealed record VNextBalanceRead(VNextBalance? Balance, string? Sha256, ImmutableArray<VNextCatalogDiagnostic> Errors)
{
    public bool IsValid => Balance is not null && Errors.IsEmpty;
}

/// <summary>Isolated closed contract; never relaxes EconomyProfile or SimulationJson.</summary>
public static class VNextBalanceReader
{
    internal static readonly JsonSerializerOptions JsonOptions = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true, MaxDepth = VNextCatalogReader.MaximumDepth,
            RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        o.Converters.Add(new IntegerConverter());
        o.Converters.Add(new OrdinalMap<long>());
        o.Converters.Add(new OrdinalMap<VNextResource>());
        o.Converters.Add(new OrdinalMap<VNextOutfit>());
        return o;
    }
    private sealed class IntegerConverter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
        {
            if (r.TokenType != JsonTokenType.Number || !r.TryGetInt64(out long n)) throw new JsonException("Expected exact Int64 integer.");
            return n;
        }
        public override void Write(Utf8JsonWriter w, long n, JsonSerializerOptions o) => w.WriteNumberValue(n);
    }
    private sealed class OrdinalMap<T> : JsonConverter<ImmutableSortedDictionary<string, T>>
    {
        public override ImmutableSortedDictionary<string, T> Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
        {
            if (r.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected map.");
            var b = ImmutableSortedDictionary.CreateBuilder<string, T>(StringComparer.Ordinal);
            while (r.Read() && r.TokenType != JsonTokenType.EndObject)
            {
                if (r.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected map key.");
                string key = r.GetString()!; r.Read();
                b.Add(key, JsonSerializer.Deserialize<T>(ref r, o)!);
            }
            return b.ToImmutable();
        }
        public override void Write(Utf8JsonWriter w, ImmutableSortedDictionary<string, T> v, JsonSerializerOptions o)
        { w.WriteStartObject(); foreach (var (k, x) in v) { w.WritePropertyName(k); JsonSerializer.Serialize(w, x, o); } w.WriteEndObject(); }
    }
    private sealed class Invalid(string code, string path, string message) : ArgumentException(message)
    { public VNextCatalogDiagnostic Diagnostic { get; } = new(code, path, message); }
    internal static void Require(bool condition, string code, string path, string message)
    { if (!condition) throw new Invalid(code, path, message); }
    public static VNextBalanceRead Load(string path)
    {
        try { return Read(ReadBounded(path)); }
        catch (Invalid e) { return new(null, null, [e.Diagnostic]); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return new(null, null, [new("IoError", "$", e.Message)]); }
    }
    public static byte[] ReadBounded(string path)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require(f.Length <= VNextCatalogReader.MaximumBytes, "TooLarge", "$", "Input exceeds 1 MiB; not read.");
        byte[] bytes = new byte[VNextCatalogReader.MaximumBytes + 1]; int n = 0, read;
        while (n < bytes.Length && (read = f.Read(bytes, n, bytes.Length - n)) > 0) n += read;
        Require(n <= VNextCatalogReader.MaximumBytes, "TooLarge", "$", "Input exceeds 1 MiB.");
        return bytes[..n];
    }
    public static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static VNextBalanceRead Read(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > VNextCatalogReader.MaximumBytes) return new(null, null, [new("TooLarge", "$", "Input exceeds 1 MiB.")]);
        string sha = Digest(bytes.Span);
        try
        {
            var payload = bytes.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? bytes[3..] : bytes;
            using var doc = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = VNextCatalogReader.MaximumDepth });
            Inspect(doc.RootElement, "$");
            var b = JsonSerializer.Deserialize<VNextBalance>(payload.Span, JsonOptions) ?? throw new JsonException("Expected full balance object.");
            Require(b.FormatVersion == VNextBalance.CurrentFormat, "UnsupportedVersion", "$.format_version", "Unsupported full balance format.");
            Require(b.Status == "metadata_with_supported_projection", "InvalidStatus", "$.status", "Expected metadata_with_supported_projection.");
            Require(b.SourceMetadata.SchemaVersion == "delsk-vnext-design-1" && b.SourceMetadata.Status == "design_not_runtime_config",
                "UnsupportedSource", "$.source_metadata", "Unsupported source schema/status.");
            Require(DateOnly.TryParseExact(b.SourceMetadata.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _), "InvalidDate", "$.source_metadata.date", "Expected ISO date.");
            var subset = VNextCatalogReader.Read(b.Catalog().ToUtf8());
            if (!subset.IsValid) return new(null, sha, subset.Diagnostics);
            Validate(b);
            return new(b, sha, []);
        }
        catch (Invalid e) { return new(null, sha, [e.Diagnostic]); }
        catch (OverflowException) { return new(null, sha, [new("Overflow", "$", "Full balance arithmetic exceeds Int64.")]); }
        catch (JsonException e) { return new(null, sha, [new("InvalidJson", e.Path ?? "$", e.Message)]); }
    }
    private static void Inspect(JsonElement e, string path)
    {
        Require(e.ValueKind != JsonValueKind.Null, "NullValue", path, "Null is not allowed.");
        if (e.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in e.EnumerateObject())
            { Require(names.Add(p.Name), "DuplicateKey", path + "." + p.Name, "Duplicate decoded property."); Inspect(p.Value, path + "." + p.Name); }
        }
        else if (e.ValueKind == JsonValueKind.Array)
        { int i = 0; foreach (var x in e.EnumerateArray()) Inspect(x, path + "[" + i++ + "]"); }
        else if (e.ValueKind == JsonValueKind.Number)
        {
            Require(e.TryGetInt64(out long v), "InvalidInteger", path, "Expected exact Int64 integer; no rounding.");
            Require(v >= 0, "OutOfRange", path, "Negative design values are not allowed.");
        }
    }
    private static void Validate(VNextBalance b)
    {
        Require(b.Items.Length<=256 && b.Recipes.Length<=512 && b.Industries.Length<=32 && b.Vehicles.Length<=64 && b.Resources.Count<=32 &&
            b.EnterpriseTiers.Length<=5 && b.RecipeVersions.Length<=3 && b.Warehouses.Length<=3 && b.LoadingPlatforms.Length<=3 && b.RoadProjects.Length<=3 &&
            b.VehicleHandlingKits.Length<=16 && b.WorkshopRecipeModernization.Length<=2 && b.Rules.OverflowWarningsRemaining.Length<=16,
            "CollectionLimit","$","Full balance collection limit exceeded (see mapping).");
        var items = b.Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var industries = b.Industries.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        void Id(string id, string p) => Require(Regex.IsMatch(id, "\\A[a-z][a-z0-9_-]{0,63}\\z", RegexOptions.CultureInvariant), "InvalidId", p, "Invalid ID.");
        void Positive(long n, string p) => Require(n > 0, "OutOfRange", p, "Expected positive value.");
        void Materials(ImmutableSortedDictionary<string, long> m, long price, string p, bool empty = false)
        {
            Require(empty || m.Count > 0, "EmptyCollection", p, "Material set is required.");
            long volume = 0, value = price;
            foreach (var (id, q) in m)
            {
                Id(id, p); Require(items.ContainsKey(id), "UnknownReference", p + "." + id, "Unknown material."); Positive(q, p);
                volume = checked(volume + checked(items[id].UnitVolume * q)); value = checked(value + checked(items[id].ReferencePriceMinor * q));
            }
        }
        void Unique(IEnumerable<long> ids, string p) => Require(ids.Distinct().Count() == ids.Count(), "DuplicateId", p, "Duplicate numeric ID.");
        foreach (var v in b.Vehicles)
        {
            Id(v.Id, "$.vehicles"); Require(!string.IsNullOrWhiteSpace(v.Name) && v.Name.Length <= 160, "InvalidName", "$.vehicles", "Invalid name.");
            Positive(v.Speed, "$.vehicles.speed"); Positive(v.CapacityVolume, "$.vehicles.capacity_volume"); Positive(v.PriceMinor, "$.vehicles.price_minor");
            Positive(v.HandlingMax, "$.vehicles.handling_max"); Positive(v.ClassFactorBp, "$.vehicles.class_factor_bp");
            Require(v.ClassFactorBp<=1000000,"OutOfRange","$.vehicles.class_factor_bp","Class factor exceeds 100x.");
            _ = checked(v.PriceMinor * v.ClassFactorBp);
        }
        Require(b.Vehicles.Length > 0 && b.Vehicles.Select(v => v.Id).Distinct(StringComparer.Ordinal).Count() == b.Vehicles.Length, "DuplicateId", "$.vehicles", "Empty or duplicate vehicle IDs.");
        Unique(b.EnterpriseTiers.Select(t => t.Level), "$.enterprise_tiers");
        foreach (var t in b.EnterpriseTiers)
        {
            Require(t.Level is >= 1 and <= 5 && t.Cells is >= 1 and <= 6 && t.Workshops is >= 1 and <= 5, "OutOfRange", "$.enterprise_tiers", "Tier/cell/workshop bounds.");
            Positive(t.InputCapacity, "$.enterprise_tiers.input_capacity"); Positive(t.OutputCapacity, "$.enterprise_tiers.output_capacity");
            Materials(t.Materials, t.PriceMinor, "$.enterprise_tiers.materials", t.Level == 1);
            if (t.Level > 1) { Positive(t.PriceMinor, "$.enterprise_tiers.price_minor"); Positive(t.Ticks, "$.enterprise_tiers.ticks"); }
            _ = checked(t.Workshops * b.Rules.StaffPerWorkshop);
        }
        Unique(b.Warehouses.Select(t => t.Level), "$.warehouses");
        foreach (var t in b.Warehouses) { Positive(t.Level, "$.warehouses.level"); Positive(t.Capacity, "$.warehouses.capacity"); Positive(t.PriceMinor, "$.warehouses.price_minor"); Positive(t.Ticks, "$.warehouses.ticks"); Materials(t.Materials, t.PriceMinor, "$.warehouses.materials"); }
        Unique(b.RecipeVersions.Select(t => t.Version), "$.recipe_versions");
        foreach (var v in b.RecipeVersions)
        {
            Require(v.Version is >= 1 and <= 3 && v.UnlockLevel is >= 1 and <= 5, "OutOfRange", "$.recipe_versions", "Version/level bounds.");
            Positive(v.InputMultiplier, "$.recipe_versions.input_multiplier"); Positive(v.OutputMultiplier, "$.recipe_versions.output_multiplier");
            Positive(v.WorkMultiplierNum, "$.recipe_versions.work_multiplier_num"); Positive(v.WorkMultiplierDen, "$.recipe_versions.work_multiplier_den");
        }
        foreach (var (id, r) in b.Resources)
        {
            Id(id, "$.resources"); Require(items.TryGetValue(id, out var item) && item.Source == "resource", "UnknownReference", "$.resources." + id, "Expected resource ItemId.");
            Require(!string.IsNullOrWhiteSpace(r.Name) && r.Name.Length <= 160, "InvalidName", "$.resources." + id, "Invalid resource name.");
            Positive(r.PrivateStockPerCell, "$.resources.stock"); Positive(r.PublicStockPerCell, "$.resources.stock");
            Positive(r.RegenOneEveryTicks, "$.resources.regen"); Positive(r.PublicRegenOneEveryTicks, "$.resources.public_regen"); Positive(r.ManualTicks, "$.resources.manual_ticks");
        }
        foreach (var (id, o) in b.AdditionalWorkshopOutfits)
        { Require(industries.Contains(id), "UnknownReference", "$.additional_workshop_outfits." + id, "Unknown industry."); Positive(o.PriceMinor, "$.additional_workshop_outfits.price_minor"); Materials(o.Materials, o.PriceMinor, "$.additional_workshop_outfits.materials"); }
        Unique(b.WorkshopRecipeModernization.Select(t => t.Version), "$.workshop_recipe_modernization");
        foreach (var m in b.WorkshopRecipeModernization)
        { Require(b.RecipeVersions.Any(v => v.Version == m.Version && v.Version > 1), "UnknownReference", "$.workshop_recipe_modernization", "Unknown modernized version."); Positive(m.PriceMinor, "$.workshop_recipe_modernization.price_minor"); Positive(m.Ticks, "$.workshop_recipe_modernization.ticks"); Materials(m.Materials, m.PriceMinor, "$.workshop_recipe_modernization.materials"); }
        Unique(b.LoadingPlatforms.Select(t => t.Level), "$.loading_platforms");
        foreach (var p in b.LoadingPlatforms)
        { Positive(p.Level, "$.loading_platforms.level"); Positive(p.Throughput, "$.loading_platforms.throughput"); Require(p.RequiresStaff is 0 or 1, "OutOfRange", "$.loading_platforms.requires_staff", "Expected 0/1."); Materials(p.Materials, p.PriceMinor, "$.loading_platforms.materials", p.Level == 1); if(p.Level>1) {Positive(p.PriceMinor,"$.loading_platforms.price_minor");Positive(p.Ticks,"$.loading_platforms.ticks");} }
        Require(b.VehicleHandlingKits.Select(k => k.Id).Distinct(StringComparer.Ordinal).Count() == b.VehicleHandlingKits.Length, "DuplicateId", "$.vehicle_handling_kits", "Duplicate kit ID.");
        foreach (var k in b.VehicleHandlingKits)
        { Id(k.Id, "$.vehicle_handling_kits"); Positive(k.Throughput, "$.vehicle_handling_kits.throughput"); Require(k.RequiresStaff == 1, "OutOfRange", "$.vehicle_handling_kits.requires_staff", "A kit requires one operator."); Positive(k.PriceMinor, "$.vehicle_handling_kits.price_minor"); Materials(k.Materials, k.PriceMinor, "$.vehicle_handling_kits.materials"); }
        Unique(b.RoadProjects.Select(t => t.Level), "$.road_projects");
        foreach (var r in b.RoadProjects) { Positive(r.Level, "$.road_projects.level"); Positive(r.PriceMinor, "$.road_projects.price_minor"); Positive(r.Ticks, "$.road_projects.ticks"); Materials(r.Materials, r.PriceMinor, "$.road_projects.materials"); }
        // Every scalar rule/global has a positive domain, except explicit zero-capable initial state/fractions.
        using var doc = JsonDocument.Parse(b.ToUtf8());
        string[] zero = ["start_cash_minor", "wage_minor", "reservation_extra_bp", "demolition_recovery_bp", "penalty_bp", "city_private_road_share_bp", "bootstrap_steel", "bootstrap_glass", "bootstrap_asphalt", "start_treasury_minor_per_4_participants", "start_population_per_participant"];
        foreach (string section in new[] { "rules", "economy_globals" })
            foreach (var f in doc.RootElement.GetProperty(section).EnumerateObject())
            {
                if (f.Value.ValueKind == JsonValueKind.Number)
                {
                    long n = f.Value.GetInt64(); if (!zero.Contains(f.Name, StringComparer.Ordinal)) Positive(n, "$." + section + "." + f.Name);
                    if (f.Name.EndsWith("_bp", StringComparison.Ordinal)) Require(n <= (section == "rules" ? 10000 : 1000000), "OutOfRange", "$." + section + "." + f.Name, "Basis point bound.");
                }
                else foreach (var n in f.Value.EnumerateArray()) Require(n.GetInt64() >= 0, "OutOfRange", "$." + section + "." + f.Name, "Negative array value.");
            }
        Require(b.Rules.MoneyMinorPerCoin == 100 && b.Rules.TickSeconds == 1, "UnsupportedUnit", "$.rules", "This format uses minor=coin/100 and one-second ticks.");
        Require(b.Rules.StaffPerWorkshop is >= 1 and <= 5, "OutOfRange", "$.rules.staff_per_workshop", "Staff bound 1..5.");
        Require(b.EconomyGlobals.MaxPopulationPerParticipant >= b.EconomyGlobals.StartPopulationPerParticipant, "OutOfRange", "$.economy_globals", "Population maximum is below start.");
    }

    /// <summary>Optional independent provenance inputs. A hash declaration alone is not source verification.</summary>
    public static ImmutableArray<VNextCatalogDiagnostic> VerifyProvenance(VNextBalance b, ReadOnlyMemory<byte> source, VNextCatalog catalog)
    {
        var errors = ImmutableArray.CreateBuilder<VNextCatalogDiagnostic>();
        if (!string.Equals(b.SourceSha256, Digest(source.Span), StringComparison.OrdinalIgnoreCase) || !string.Equals(b.SourceSha256,catalog.SourceSha256,StringComparison.OrdinalIgnoreCase))
            errors.Add(new("StaleSource", "$.source_sha256", "Source/full/A1 digests differ."));
        if (!JsonNode.DeepEquals(JsonNode.Parse((b.Catalog() with {SourceSha256=catalog.SourceSha256}).ToUtf8()), JsonNode.Parse(catalog.ToUtf8())))
            errors.Add(new("SubsetConflict", "$", "Full B1 and A1 catalog values differ."));
        // Compare every source section, including future metadata, after only documented unit/default mapping.
        try
        {
            if(source.Length>VNextCatalogReader.MaximumBytes)throw new JsonException("Source too large.");
            using var sourceDoc = JsonDocument.Parse(source, new JsonDocumentOptions {MaxDepth=VNextCatalogReader.MaximumDepth});
            Inspect(sourceDoc.RootElement,"$");
            var n = JsonNode.Parse(source.Span)!.AsObject();
            var actual = JsonNode.Parse(b.ToUtf8())!.AsObject();
            var metadata = actual["source_metadata"]!;
            foreach(string key in new[]{"schema_version","status","date"})
            { if(!JsonNode.DeepEquals(n[key],metadata[key]))errors.Add(new("SourceConflict","$.source_metadata."+key,"Source metadata differs.")); n.Remove(key); }
            foreach(var section in n)
            {
                var expected = section.Value!.DeepClone();
                void Coins(JsonObject obj, string field = "price_minor")
                { if(obj.Remove("coins",out var coins))obj[field]=checked(coins!.GetValue<long>()*100); }
                if(section.Key=="industries")foreach(var x in expected.AsArray())Coins(x!.AsObject(),"construction_price_minor");
                if(new[]{"enterprise_tiers","warehouses","workshop_recipe_modernization","loading_platforms","vehicle_handling_kits","road_projects"}.Contains(section.Key,StringComparer.Ordinal))
                    foreach(var x in expected.AsArray())Coins(x!.AsObject());
                if(section.Key=="additional_workshop_outfits")foreach(var x in expected.AsObject())Coins(x.Value!.AsObject());
                if(section.Key=="enterprise_tiers")foreach(var x in expected.AsArray().Where(x=>x!["level"]!.GetValue<int>()==1)) {x!["price_minor"]=0;x["materials"]=new JsonObject();x["ticks"]=0;}
                if(section.Key=="recipe_versions")foreach(var x in expected.AsArray().Where(x=>x!["version"]!.GetValue<int>()==1))x!["unlock_level"]=1;
                if(section.Key=="vehicles")foreach(var x in expected.AsArray())if(!x!.AsObject().ContainsKey("delivery_service_allowed"))x["delivery_service_allowed"]=true;
                if(section.Key=="loading_platforms")foreach(var x in expected.AsArray()) {if(!x!.AsObject().ContainsKey("service_minor_per_active_tick"))x["service_minor_per_active_tick"]=0;if(x["level"]!.GetValue<int>()==1){x["price_minor"]=0;x["materials"]=new JsonObject();x["ticks"]=0;}}
                if(section.Key=="economy_globals")foreach(var key in new[]{"resource_cell","resource_lease"}) {var obj=expected.AsObject();var price=obj[key+"_coins"]!.GetValue<long>();obj.Remove(key+"_coins");obj[key+"_price_minor"]=checked(price*100);}
                if(!actual.TryGetPropertyValue(section.Key,out var present) || !JsonNode.DeepEquals(expected,present))errors.Add(new("SourceConflict","$."+section.Key,"Source section differs from full mapping."));
            }
            if(n.Count!=15)errors.Add(new("SourceConflict","$","Source must contain exactly the 15 mapped sections."));
        }
        catch(Exception e) when(e is JsonException or InvalidOperationException or ArgumentException or OverflowException or Invalid or NullReferenceException)
        {errors.Add(new("InvalidSource","$",e.Message));}
        return errors.ToImmutable();
    }
}
