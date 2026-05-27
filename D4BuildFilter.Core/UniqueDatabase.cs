namespace D4BuildFilter.Core;

/// <summary>
/// Unique display name → in-game loot-filter id (the type-8 "specific unique" id used by
/// the per-unique targeting condition).
///
/// These can ONLY come from in-game exports: the filter's unique id-space is DISTINCT from
/// D4Companion's unique SNOs (verified — rootsxo's real ids appear in no D4C field). Capture
/// one by making a 1-rule D4 filter that selects the unique, exporting, and decoding it
/// (`dotnet run --project D4BuildFilter.Tester -- decode &lt;code&gt;` → the type-8 id). This
/// table grows as more are captured.
/// </summary>
public static class UniqueDatabase
{
    public static readonly IReadOnlyDictionary<string, uint> ByName =
        new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["Banished Lord's Talisman"] = 0x17d2dc,   // captured 2026-05-26 from an in-game export
        };

    public static bool TryGet(string name, out uint id) => ByName.TryGetValue(name, out id);
}
