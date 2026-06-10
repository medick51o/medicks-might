using System.Text.Json;

namespace D4BuildFilter.Core;

/// <summary>
/// Shape check for game-data files BEFORE they're trusted — a truncated download or a stray
/// hand-edit under the local data folder must never brick name resolution (DataFiles falls back
/// to the bundled copy whenever this rejects). Mirrors exactly what the lookups read:
///   Affixes.enUS.json — array entries with a non-empty <c>IdSnoList</c> + <c>DescriptionClean</c>
///   (NameLookup keys every IdSnoList sno → DescriptionClean);
///   Uniques.enUS.json — array entries with <c>Name</c> + (<c>IdNameItem</c> or <c>IdName</c>)
///   (UniqueLookup's keys).
/// <c>Entries</c> counts only rows a lookup would actually key, so the count doubles as the
/// sanity metric GameDataUpdater compares against the bundled copy.
/// </summary>
public static class GameDataValidator
{
    public static (bool Ok, int Entries, string? Error) Validate(string fileName, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return (false, 0, "root is not a JSON array");

            var isUniques = fileName.StartsWith("Uniques", StringComparison.OrdinalIgnoreCase);
            var usable = 0;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                if (isUniques ? IsUsableUnique(e) : IsUsableAffix(e)) usable++;
            }
            return usable > 0
                ? (true, usable, null)
                : (false, 0, "no usable entries — wrong file, or the format drifted");
        }
        catch (JsonException ex)
        {
            return (false, 0, $"not valid JSON: {ex.Message}");
        }
    }

    private static bool IsUsableAffix(JsonElement e) =>
        e.TryGetProperty("DescriptionClean", out var dc) && dc.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(dc.GetString())
        && e.TryGetProperty("IdSnoList", out var list) && list.ValueKind == JsonValueKind.Array
        && list.GetArrayLength() > 0;

    private static bool IsUsableUnique(JsonElement e) =>
        e.TryGetProperty("Name", out var n) && n.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(n.GetString())
        && (HasNonEmptyString(e, "IdNameItem") || HasNonEmptyString(e, "IdName"));

    private static bool HasNonEmptyString(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
        && v.GetString() is { Length: > 0 };
}
