using System.Collections.Immutable;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Simulation.Hosting;

/// <summary>Read-only metadata, deliberately not an EconomyProfile or authoritative world state.</summary>
public sealed record VNextCatalog(string FormatVersion, string Id, string Status, string SourceSha256,
    ImmutableArray<VNextCatalogItem> Items, ImmutableArray<VNextCatalogRecipe> Recipes,
    ImmutableArray<VNextCatalogIndustry> Industries)
{
    public const string CurrentFormat = "delsk-vnext-catalog-1";
    public byte[] ToUtf8() => JsonSerializer.SerializeToUtf8Bytes(this, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    });
}

public sealed record VNextCatalogItem(string Id, string Name, long UnitVolume, long ReferencePriceMinor, long XpPerUnit, string Source);
public sealed record VNextCatalogRecipe(string Id, string Industry, int UnlockLevel, ImmutableSortedDictionary<string, long> Inputs,
    string Output, long OutputQuantity, long WorkTicksFullStaff, long ServiceMinorPerWorkTick);
public sealed record VNextCatalogIndustry(string Id, string Name, long ConstructionPriceMinor, ImmutableSortedDictionary<string, long> Materials);
public sealed record VNextCatalogDiagnostic(string Code, string Path, string Message);
public sealed record VNextCatalogCounts(int Items, int Recipes, int Industries);
public sealed record VNextCatalogValidation(VNextCatalog? Catalog, string? Sha256, string? FormatVersion, string? CatalogId,
    VNextCatalogCounts? Counts, ImmutableArray<VNextCatalogDiagnostic> Diagnostics)
{
    public bool IsValid => Catalog is not null && Diagnostics.IsEmpty;
}
