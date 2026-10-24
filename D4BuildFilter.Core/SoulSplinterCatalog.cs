namespace D4BuildFilter.Core;

/// <summary>
/// Abyssal soul splinters named in the Season 15 data audit: Hellfire, Pain, and the Black
/// Soulstone, plus the lesser/greater ranks of those same three families so a guide that
/// omits the word "Abyssal" still resolves. Ids are item <c>__snoID__</c> values from
/// DiabloTools/d4data build 3.2.3.73883 (commit 76b722a340d22001171b4cfb0fc39e8efa761654).
/// These items are not uniques (eMagicType is 0 or 1), so they are shown by their own rule
/// rather than the purple unique rule.
/// </summary>
public static class SoulSplinterCatalog
{
    public sealed record Entry(string Name, uint Id);

    public static readonly IReadOnlyList<Entry> All =
    [
        new("Lesser Splinter of Pain", 0x28772b),
        new("Splinter of Pain", 0x28a029),
        new("Greater Splinter of Pain", 0x28a032),
        new("Abyssal Splinter of Pain", 0x28a036),
        new("Lesser Splinter of Hellfire", 0x287750),
        new("Splinter of Hellfire", 0x28a12a),
        new("Greater Splinter of Hellfire", 0x28a12c),
        new("Abyssal Splinter of Hellfire", 0x28a12e),
        new("Lesser Splinter of the Black Soulstone", 0x28774c),
        new("Splinter of the Black Soulstone", 0x28a22f),
        new("Greater Splinter of the Black Soulstone", 0x28a231),
        new("Abyssal Splinter of the Black Soulstone", 0x28a233),
    ];

    private static readonly Dictionary<string, uint> ByName = All.ToDictionary(
        entry => entry.Name, entry => entry.Id, StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(string name, out uint id) => ByName.TryGetValue(name.Trim(), out id);
}
