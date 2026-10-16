using System.Text;
using System.Text.Json;

namespace Agents.Llm;

public static class StrictIntentParser
{
    private static readonly HashSet<string> Fields = ["decisionId", "actorId", "observationVersion", "sessionEpoch", "optionId", "reasonSummary"];
    public static bool TryParse(string? text, ModelRequest request, out ModelIntent? intent, out string reason)
    {
        intent = null; reason = "InvalidIntent";
        if (text is null || Encoding.UTF8.GetByteCount(text) > 4096) return false;
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in root.EnumerateObject()) if (!Fields.Contains(p.Name) || !seen.Add(p.Name)) return false;
            if (!seen.SetEquals(Fields)) return false;
            var d = new ModelIntent(root.GetProperty("decisionId").GetString()!, root.GetProperty("actorId").GetInt64(),
                root.GetProperty("observationVersion").GetInt64(), root.GetProperty("sessionEpoch").GetInt64(),
                root.GetProperty("optionId").GetString()!, root.GetProperty("reasonSummary").GetString()!);
            if (d.ReasonSummary is null || d.ReasonSummary.Length > 256 || d.ReasonSummary.Any(char.IsControl)) return false;
            if (d.DecisionId != request.DecisionId || d.ActorId != request.ActorId ||
                d.ObservationVersion != request.ObservationVersion || d.SessionEpoch != request.SessionEpoch)
            { reason = "DecisionIdentityMismatch"; return false; }
            if (!request.Options.Any(o => o.Id == d.OptionId)) { reason = "UnknownOption"; return false; }
            intent = d; reason = "Accepted"; return true;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or OverflowException)
        { return false; }
    }
}
