using System.Text.Json;

namespace D4BuildFilter.Core;

/// <summary>
/// Resolves a maxroll unique item id (e.g. "Amulet_Unique_Generic_101") to its display
/// name using Diablo4Companion's <c>Uniques.enUS.json</c>, keyed by <c>IdNameItem</c> (the
/// base-item name maxroll uses), with <c>IdName</c> as a fallback.
///
/// Charms, seals and mythics aren't keyed here, so they naturally don't resolve — exactly
/// what we want: mythics stay untouched, charms/sets are a different condition type.
/// </summary>
public sealed class UniqueLookup
{
    private readonly IReadOnlyDictionary<string, string> _byItem;
    private UniqueLookup(IReadOnlyDictionary<string, string> m) => _byItem = m;

    public int Count => _byItem.Count;

    /// <summary>Display name for a maxroll unique item id, or null if it's not a known gear unique.</summary>
    public string? Resolve(string itemId) => _byItem.TryGetValue(itemId, out var n) ? n : null;

    public static UniqueLookup FromFile(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (!e.TryGetProperty("Name", out var nameEl)) continue;
            var name = nameEl.GetString();
            if (string.IsNullOrWhiteSpace(name)) continue;

            void AddKey(string? key)
            {
                if (string.IsNullOrWhiteSpace(key)) return;
                map.TryAdd(key, name);
                foreach (var part in key.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    map.TryAdd(part, name);
            }

            foreach (var key in new[] { "IdNameItem", "IdName" })
                if (e.TryGetProperty(key, out var kEl) && kEl.GetString() is { Length: > 0 } k)
                    AddKey(k);   // IdNameItem wins; IdName only fills gaps
            if (e.TryGetProperty("IdNameItemList", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var item in list.EnumerateArray())
                    AddKey(item.GetString());
        }
        return new UniqueLookup(map);
    }

    // Cached singleton — see NameLookup.Default() for rationale (static file, parse once, thread-safe).
    private static volatile Lazy<UniqueLookup> _default = new(Load);

    private static UniqueLookup Load() =>
        FromFile(DataFiles.Find(GameDataStore.UniquesFile)
            ?? throw new FileNotFoundException(
                "Uniques.enUS.json not found (bundle it in Data\\ or install Diablo4Companion)."));

    public static UniqueLookup Default() => _default.Value;

    /// <summary>Forget the cached lookup so the next <see cref="Default"/> re-reads from disk —
    /// called after "Update game data" installs fresh files.</summary>
    public static void Invalidate() => _default = new(Load);
}
