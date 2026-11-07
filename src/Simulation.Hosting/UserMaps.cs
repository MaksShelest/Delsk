using System.Text;
using System.Text.RegularExpressions;
using Simulation.Core;

namespace Simulation.Hosting;

/// <summary>Имена и запуск собственных карт. Каталог файлов остаётся у клиента, без второй экономики.</summary>
public static class UserMaps
{
    public static readonly string[] Reserved =
    [
        "draft_map", "compact_valley", "compact_valley_i6c2", "compact_valley_i6c21", "woodland_region_i6c2",
        "small_river", "large_districts", "user_map"
    ];

    public static bool IsReserved(string id) => Reserved.Contains(id);

    public static bool IsUserId(string id) => Regex.IsMatch(id, "\\Auser_[a-z0-9_]{1,55}\\z") && !IsReserved(id);

    public static bool NeedsOverwriteConfirm(string? existingJson, string id)
    {
        if (string.IsNullOrWhiteSpace(existingJson)) return false;
        try { return MapDraft.Parse(existingJson).Id != id; }
        catch (Exception) { return true; }
    }

    public static string SuggestId(string name, IEnumerable<string> taken)
    {
        var letters = new Dictionary<char, string>
        {
            ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e", ['ж'] = "zh",
            ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o",
            ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "h", ['ц'] = "ts",
            ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sch", ['ы'] = "y", ['э'] = "e", ['ю'] = "yu", ['я'] = "ya"
        };
        var builder = new StringBuilder();
        foreach (char raw in name.Trim().ToLowerInvariant())
        {
            if (letters.TryGetValue(raw, out var latin)) builder.Append(latin);
            else if (raw is >= 'a' and <= 'z' or >= '0' and <= '9') builder.Append(raw);
            else if (raw is ' ' or '-' or '_') builder.Append('_');
        }
        var slug = builder.ToString().Trim('_');
        while (slug.Contains("__", StringComparison.Ordinal)) slug = slug.Replace("__", "_", StringComparison.Ordinal);
        if (slug.Length == 0) slug = "map";
        if (!slug.StartsWith("user_", StringComparison.Ordinal)) slug = "user_" + slug;
        if (slug.Length > 50) slug = slug[..50].Trim('_');
        var used = taken.ToHashSet(StringComparer.Ordinal);
        var id = slug;
        var number = 2;
        while (IsReserved(id) || used.Contains(id) || !Regex.IsMatch(id, "\\A[a-z0-9_]{1,60}\\z"))
        {
            var suffix = "_" + number;
            var stem = slug.Length + suffix.Length > 60 ? slug[..(60 - suffix.Length)].Trim('_') : slug;
            id = stem + suffix;
            number++;
        }
        return id;
    }

    public static OrdinaryGameResolution Start(MapDraft draft, EconomyProfile economy, OrdinaryGameOptions options, VNextBalance? balance)
    {
        var problems = draft.Problems();
        if (problems.Count > 0) throw new ArgumentException(string.Join('\n', problems));
        var scenario = MapDesignReader.Read(Encoding.UTF8.GetBytes(draft.ToJson()), economy);
        return OrdinaryNewGame.Resolve(scenario, economy, options, vnextBalance: balance);
    }
}
