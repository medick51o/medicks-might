using System.Text.Json;

namespace D4BuildFilter.Core;

/// <summary>
/// Resolves a maxroll explicit <c>nid</c> to its display name using a Diablo4Companion-format
/// <c>Affixes.enUS.json</c>. Key by every SNO in each entry's <c>IdSnoList</c> (NOT <c>IdSno</c>
/// alone — that resolves almost nothing) → <c>DescriptionClean</c>. ~77-81% of a build's nids
/// resolve; the misses are unique/aspect inherents and gem bonuses, which aren't generically
/// filterable anyway, so dropping them is correct.
///
/// Interim data source. For shipping, swap in a d4data-derived file (same shape) instead of
/// depending on a D4Companion install. See project memory: project_d4_lootfilter_compiler.
/// </summary>
public sealed class NameLookup
{
    private readonly IReadOnlyDictionary<long, string> _byNid;

    private NameLookup(IReadOnlyDictionary<long, string> byNid) => _byNid = byNid;

    /// <summary>How many affixes loaded from this file resolved to a non-empty name.</summary>
    public int Count => _byNid.Count;

    /// <summary>Affix display name for a maxroll explicit nid, or null when unmapped.</summary>
    public string? Resolve(long nid) => _byNid.TryGetValue(nid, out var name) ? name : null;

    /// <summary>Load from a D4Companion-format Affixes.enUS.json file.</summary>
    public static NameLookup FromFile(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var map = new Dictionary<long, string>();
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            if (!entry.TryGetProperty("DescriptionClean", out var dcEl)) continue;
            var dc = dcEl.GetString();
            if (string.IsNullOrWhiteSpace(dc)) continue;
            if (!entry.TryGetProperty("IdSnoList", out var listEl) || listEl.ValueKind != JsonValueKind.Array) continue;

            foreach (var snoEl in listEl.EnumerateArray())
            {
                // IdSnoList entries are strings, e.g. "1829570".
                if (long.TryParse(snoEl.GetString(), out var sno))
                    map[sno] = dc;   // last write wins; collisions are rare and resolve to the same text
            }
        }
        return new NameLookup(map);
    }

    // Cached: the data file is static for the life of the process, so parse it once. Previously
    // Default() re-read + re-parsed the JSON on every Maxroll compile. Lazy is thread-safe by
    // default (LazyThreadSafetyMode.ExecutionAndPublication) — important once this runs server-side.
    private static readonly Lazy<NameLookup> _default = new(() =>
        FromFile(DataFiles.Find("Affixes.enUS.json")
            ?? throw new FileNotFoundException(
                "Affixes.enUS.json not found. Expected it bundled in Data\\ next to the app, " +
                "or a Diablo4Companion install under Downloads.")));

    /// <summary>Cached lookup loaded from the bundled file (next to the app) or a D4Companion install.</summary>
    public static NameLookup Default() => _default.Value;
}
