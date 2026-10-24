using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class VNextCatalogTests
{
    private static readonly string Small = """
        {"format_version":"delsk-vnext-catalog-1","id":"small","status":"catalog_only",
         "source_sha256":"0000000000000000000000000000000000000000000000000000000000000000",
         "items":[{"id":"raw","name":"Raw","unit_volume":2,"reference_price_minor":10,"xp_per_unit":0,"source":"resource"},
                  {"id":"made","name":"Made","unit_volume":1,"reference_price_minor":30,"xp_per_unit":1,"source":"production"}],
         "recipes":[{"id":"raw","industry":"raw","unlock_level":1,"inputs":{"raw":2},"output":"made","output_quantity":1,
                     "work_ticks_full_staff":3,"service_minor_per_work_tick":1}],
         "industries":[{"id":"raw","name":"Factory","construction_price_minor":50,"materials":{"raw":2}}]}
        """;
    private static VNextCatalogValidation Read(string json) => VNextCatalogReader.Read(Encoding.UTF8.GetBytes(json));
    private static byte[] ResourceBytes(string name)
    {
        using var stream = typeof(VNextCatalogTests).Assembly.GetManifestResourceStream("Fixture." + name)!;
        using var copy = new MemoryStream(); stream.CopyTo(copy); return copy.ToArray();
    }
    private static string Resource(string name) => Encoding.UTF8.GetString(ResourceBytes(name));
    private static JsonNode Sample() => JsonNode.Parse(Small)!;
    private static void Reject(string json, string code, string path)
    {
        var first = Read(json); var repeated = Read(json);
        Assert.False(first.IsValid); Assert.Null(first.Catalog);
        var error = Assert.Single(first.Diagnostics);
        Assert.Equal(code, error.Code); Assert.Contains(path, error.Path);
        Assert.Equal(first.Diagnostics.ToArray(), repeated.Diagnostics.ToArray());
    }

    [Fact]
    public void SmallCatalogIsImmutableOrdinalAndAllowsIdsSharedAcrossNamespaces()
    {
        var result = Read(Small); Assert.True(result.IsValid);
        Assert.Equal(new(2, 1, 1), result.Counts);
        var catalog = result.Catalog!;
        Assert.Equal("raw", catalog.Items[0].Id);
        Assert.Equal(catalog.Items[0].Id, catalog.Recipes[0].Id);
        Assert.Equal(catalog.Items[0].Id, catalog.Industries[0].Id);
        Assert.Same(StringComparer.Ordinal, catalog.Recipes[0].Inputs.KeyComparer);
        var changed = catalog.Recipes[0].Inputs.SetItem("raw", 99);
        Assert.Equal(2, catalog.Recipes[0].Inputs["raw"]); Assert.Equal(99, changed["raw"]);
    }

    [Fact]
    public void B1SubsetMatchesEverySourceValueAndExactSourceShaIndependently()
    {
        string source = Resource("vnext-design.json");
        using var document = JsonDocument.Parse(source);
        var root = document.RootElement;
        var result = Read(Resource("vnext-catalog.json"));
        Assert.True(result.IsValid); Assert.Equal(new(34, 23, 6), result.Counts);
        var catalog = result.Catalog!;
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(ResourceBytes("vnext-design.json"))), catalog.SourceSha256);
        foreach (var (s, item) in root.GetProperty("items").EnumerateArray().Zip(catalog.Items))
        {
            Assert.Equal(s.GetProperty("id").GetString(), item.Id); Assert.Equal(s.GetProperty("name").GetString(), item.Name);
            Assert.Equal(s.GetProperty("unit_volume").GetInt64(), item.UnitVolume);
            Assert.Equal(s.GetProperty("reference_price_minor").GetInt64(), item.ReferencePriceMinor);
            Assert.Equal(s.GetProperty("xp_per_unit").GetInt64(), item.XpPerUnit); Assert.Equal(s.GetProperty("source").GetString(), item.Source);
        }
        foreach (var (s, recipe) in root.GetProperty("recipes").EnumerateArray().Zip(catalog.Recipes))
        {
            Assert.Equal(s.GetProperty("id").GetString(), recipe.Id); Assert.Equal(s.GetProperty("industry").GetString(), recipe.Industry);
            Assert.Equal(s.GetProperty("unlock_level").GetInt32(), recipe.UnlockLevel);
            Assert.Equal(s.GetProperty("output").GetString(), recipe.Output); Assert.Equal(s.GetProperty("output_quantity").GetInt64(), recipe.OutputQuantity);
            Assert.Equal(s.GetProperty("work_ticks_full_staff").GetInt64(), recipe.WorkTicksFullStaff);
            Assert.Equal(s.GetProperty("service_minor_per_work_tick").GetInt64(), recipe.ServiceMinorPerWorkTick);
            Assert.Equal(s.GetProperty("inputs").EnumerateObject().Count(), recipe.Inputs.Count);
            foreach (var p in s.GetProperty("inputs").EnumerateObject()) Assert.Equal(p.Value.GetInt64(), recipe.Inputs[p.Name]);
        }
        foreach (var (s, industry) in root.GetProperty("industries").EnumerateArray().Zip(catalog.Industries))
        {
            Assert.Equal(s.GetProperty("id").GetString(), industry.Id); Assert.Equal(s.GetProperty("name").GetString(), industry.Name);
            Assert.Equal(s.GetProperty("coins").GetInt64() * 100, industry.ConstructionPriceMinor);
            Assert.Equal(s.GetProperty("materials").EnumerateObject().Count(), industry.Materials.Count);
            foreach (var p in s.GetProperty("materials").EnumerateObject()) Assert.Equal(p.Value.GetInt64(), industry.Materials[p.Name]);
        }
    }

    [Theory]
    [InlineData("format_version", "\"future\"", "UnsupportedVersion")]
    [InlineData("format_version", "1", "InvalidType")]
    [InlineData("status", "\"runtime\"", "InvalidStatus")]
    [InlineData("source_sha256", "\"bad\"", "InvalidSha256")]
    [InlineData("items", "null", "NullValue")]
    [InlineData("recipes", "{}", "InvalidType")]
    [InlineData("industries", "[]", "EmptyCollection")]
    [InlineData("id", "\"Upper\"", "InvalidId")]
    [InlineData("id", "\"\"", "InvalidId")]
    public void StrictHeaderAndSections(string field, string value, string code)
    {
        var n = Sample(); n[field] = JsonNode.Parse(value);
        Reject(n.ToJsonString(), code, field);
    }

    [Theory]
    [InlineData("item")]
    [InlineData("recipe")]
    [InlineData("industry")]
    public void UnknownMissingNullAndDuplicateFieldsAtEachLevel(string kind)
    {
        string section = kind switch { "item" => "items", "recipe" => "recipes", _ => "industries" };
        foreach (string invalid in new[] { "unknown", "missing", "null", "duplicate" })
        {
            var n = Sample(); var obj = n[section]![0]!.AsObject();
            if (invalid == "unknown") obj["typo"] = 1;
            else if (invalid == "missing") obj.Remove("id");
            else if (invalid == "null") obj["id"] = null;
            string json = n.ToJsonString();
            if (invalid == "duplicate")
            {
                string original = obj.ToJsonString();
                json = json.Replace(original, original.Insert(1, "\"id\":\"raw\","), StringComparison.Ordinal);
            }
            Reject(json, invalid switch { "unknown" => "UnknownField", "missing" => "MissingField", "null" => "NullValue", _ => "DuplicateKey" }, section);
        }
    }

    [Fact]
    public void DuplicateDecodedKeysIncludingQuantityMapsAreNotSilentlyOverwritten()
    {
        Reject(Small.Replace("\"raw\":2", "\"raw\":2,\"r\\u0061w\":3", StringComparison.Ordinal), "DuplicateKey", "raw");
        Reject(Small.Replace("\"id\":\"small\"", "\"id\":\"small\",\"id\":\"small\"", StringComparison.Ordinal), "DuplicateKey", "id");
    }

    [Theory]
    [InlineData("items")]
    [InlineData("recipes")]
    [InlineData("industries")]
    public void DuplicateIdsFailWithinTheirOwnCategory(string section)
    {
        var n = Sample(); n[section]!.AsArray().Add(n[section]![0]!.DeepClone());
        Reject(n.ToJsonString(), "DuplicateId", section);
    }

    [Theory]
    [InlineData("industry")]
    [InlineData("output")]
    [InlineData("inputs")]
    [InlineData("materials")]
    public void AllReferencesAreCheckedOrdinally(string field)
    {
        var n = Sample();
        if (field == "materials") n["industries"]![0]![field] = new JsonObject { ["unknown"] = 1 };
        else if (field == "inputs") n["recipes"]![0]![field] = new JsonObject { ["unknown"] = 1 };
        else n["recipes"]![0]![field] = "unknown";
        Reject(n.ToJsonString(), "UnknownReference", field);
    }

    [Theory]
    [InlineData("0", "OutOfRange")]
    [InlineData("-1", "OutOfRange")]
    [InlineData("1.5", "InvalidInteger")]
    [InlineData("1.0", "InvalidInteger")]
    [InlineData("1e0", "InvalidInteger")]
    [InlineData("9223372036854775808", "InvalidInteger")]
    [InlineData("\"2\"", "InvalidType")]
    [InlineData("true", "InvalidType")]
    [InlineData("null", "NullValue")]
    public void NumericValuesAreNotRoundedCoercedOrWrapped(string value, string code)
    {
        var n = Sample(); n["items"]![0]!["unit_volume"] = JsonNode.Parse(value);
        Reject(n.ToJsonString(), code, "unit_volume");
    }

    [Fact]
    public void InvalidNamesSourcesIdsAndLevelsFail()
    {
        foreach (string id in new[] { new string('a', 65), "a.b", "русский", "a\n" })
        { var n = Sample(); n["items"]![0]!["id"] = id; Reject(n.ToJsonString(), "InvalidId", "id"); }
        foreach (string name in new[] { " ", new string('n', 161) })
        { var n = Sample(); n["items"]![0]!["name"] = name; Reject(n.ToJsonString(), "InvalidName", "name"); }
        { var n = Sample(); n["items"]![0]!["source"] = "magic"; Reject(n.ToJsonString(), "InvalidSource", "source"); }
        foreach (int level in new[] { 0, 6 })
        { var n = Sample(); n["recipes"]![0]!["unlock_level"] = level; Reject(n.ToJsonString(), "OutOfRange", "unlock_level"); }
        foreach (string field in new[] { "xp_per_unit", "service_minor_per_work_tick" })
        {
            var n = Sample(); n[field == "xp_per_unit" ? "items" : "recipes"]![0]![field] = -1;
            Reject(n.ToJsonString(), "OutOfRange", field);
        }
    }

    [Fact]
    public void RecipeQuantitiesMustBeOneToEightPositiveDistinctItems()
    {
        var n = Sample(); n["recipes"]![0]!["inputs"] = new JsonObject(); Reject(n.ToJsonString(), "InvalidQuantitySet", "inputs");
        n = Sample(); n["recipes"]![0]!["inputs"]!["raw"] = 0; Reject(n.ToJsonString(), "OutOfRange", "raw");
        n = Sample(); var inputs = n["recipes"]![0]!["inputs"]!.AsObject();
        for (int i = 0; i < 8; i++) inputs["item" + i] = 1;
        Reject(n.ToJsonString(), "InvalidQuantitySet", "inputs");
    }

    [Theory]
    [InlineData("input-volume")]
    [InlineData("input-value")]
    [InlineData("input-sum")]
    [InlineData("output-volume")]
    [InlineData("output-value")]
    [InlineData("output-xp")]
    [InlineData("service")]
    [InlineData("materials-product")]
    [InlineData("materials-sum")]
    public void CheckedBatchAndConstructionTotalsCannotOverflow(string scenario)
    {
        var n = Sample(); var a = n["items"]![0]!; var b = n["items"]![1]!; var recipe = n["recipes"]![0]!;
        if (scenario == "input-volume") a["unit_volume"] = long.MaxValue;
        if (scenario == "input-value") a["reference_price_minor"] = long.MaxValue;
        if (scenario == "input-sum")
        { a["unit_volume"] = long.MaxValue / 2; b["unit_volume"] = 3; recipe["inputs"]!["made"] = 1; }
        if (scenario.StartsWith("output", StringComparison.Ordinal))
        { recipe["output_quantity"] = 2; b[scenario == "output-volume" ? "unit_volume" : scenario == "output-value" ? "reference_price_minor" : "xp_per_unit"] = long.MaxValue; }
        if (scenario == "service") recipe["service_minor_per_work_tick"] = long.MaxValue;
        if (scenario == "materials-product") n["industries"]![0]!["materials"]!["raw"] = long.MaxValue;
        if (scenario == "materials-sum")
        { a["reference_price_minor"] = 1; b["reference_price_minor"] = 1; n["industries"]![0]!["materials"] = new JsonObject { ["raw"] = long.MaxValue / 2, ["made"] = long.MaxValue / 2 }; }
        Reject(n.ToJsonString(), "Overflow", scenario.StartsWith("materials", StringComparison.Ordinal) ? "industries" : "recipes");
    }

    [Fact]
    public void RoundTripPreservesDataAndDigestsDescribeActualBytesNotSemanticNormalization()
    {
        var before = Read(Small); var raw = before.Catalog!.ToUtf8(); var after = VNextCatalogReader.Read(raw);
        Assert.True(after.IsValid); Assert.Equal(raw, after.Catalog!.ToUtf8());
        Assert.Equal(before.Catalog.Items.ToArray(), after.Catalog.Items.ToArray());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(raw)), after.Sha256);
        Assert.NotEqual(before.Sha256, Read(Small + " ").Sha256);
        var n = Sample(); n["items"]![0]!["reference_price_minor"] = 11;
        Assert.NotEqual(before.Sha256, Read(n.ToJsonString()).Sha256);
        byte[] bom = [0xef, 0xbb, 0xbf, .. raw]; var marked = VNextCatalogReader.Read(bom);
        Assert.True(marked.IsValid); Assert.NotEqual(after.Sha256, marked.Sha256);
        Assert.Equal(before.Catalog.SourceSha256, after.Catalog.SourceSha256);
    }

    [Fact]
    public void SizeDepthMalformedUtf8AndIoAreBoundedAndNeverMutateInput()
    {
        Reject("[]", "InvalidType", "$"); Reject("{}", "MissingField", "format_version");
        Reject(Small[..^1], "InvalidJson", "$");
        Assert.Equal("TooLarge", Assert.Single(VNextCatalogReader.Read(new byte[VNextCatalogReader.MaximumBytes + 1]).Diagnostics).Code);
        Assert.True(Read(Small.PadRight(VNextCatalogReader.MaximumBytes, ' ')).IsValid);
        Assert.Equal("TooLarge", Assert.Single(Read(Small + new string('я', VNextCatalogReader.MaximumBytes / 2)).Diagnostics).Code);
        Assert.Equal("InvalidJson", Assert.Single(Read(new string('[', 40) + new string(']', 40)).Diagnostics).Code);
        Assert.Equal("InvalidJson", Assert.Single(VNextCatalogReader.Read(new byte[] { 0xff, 0x00 }).Diagnostics).Code);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, Small); byte[] original = File.ReadAllBytes(path);
            Assert.True(VNextCatalogReader.Load(path).IsValid); Assert.Equal(original, File.ReadAllBytes(path));
            using (var stream = File.OpenWrite(path)) stream.SetLength(VNextCatalogReader.MaximumBytes + 1L);
            var large = VNextCatalogReader.Load(path); Assert.Null(large.Sha256); Assert.Equal("TooLarge", Assert.Single(large.Diagnostics).Code);
        }
        finally { File.Delete(path); }
        Assert.Equal("IoError", Assert.Single(VNextCatalogReader.Load(path).Diagnostics).Code);
    }

    [Fact]
    public void A1DoesNotPretendToValidateA2GraphBootstrapOrCapacity()
    {
        var n = Sample(); n["recipes"]![0]!["output"] = "raw"; // Structural self-cycle; graph belongs to A2.
        n["industries"]![0]!["materials"]!["raw"] = 100; // Above70, still a valid metadata quantity.
        Assert.True(Read(n.ToJsonString()).IsValid);
    }

    [Theory]
    [InlineData("items", "reference_price_minor")]
    [InlineData("recipes", "output_quantity")]
    [InlineData("recipes", "work_ticks_full_staff")]
    [InlineData("industries", "construction_price_minor")]
    public void PositiveFieldsCannotBeZero(string section, string field)
    {
        var n = Sample(); n[section]![0]![field] = 0;
        Reject(n.ToJsonString(), "OutOfRange", field);
    }

    [Fact]
    public void UnknownRootAndNullArrayEntriesFailWithoutHiddenDefaults()
    {
        var n = Sample(); n["world"] = new JsonObject(); Reject(n.ToJsonString(), "UnknownField", "world");
        n = Sample(); n.AsObject().Remove("id"); Reject(n.ToJsonString(), "MissingField", "id");
        n = Sample(); n["items"]![0] = null; Reject(n.ToJsonString(), "NullValue", "items");
    }
}
