namespace D4BuildFilter.Core;

/// <summary>
/// Runeword items from DiabloTools/d4data build 3.2.3.73883
/// (commit 76b722a340d22001171b4cfb0fc39e8efa761654). The id is the item <c>__snoID__</c>,
/// which is the type-8 filter id. Display names are the enUS StringList Name entry.
/// Several bases share one display name; <see cref="IdsForName"/> returns every base so a
/// build that asks for Grief is not limited to the sword.
/// </summary>
public static class RunewordCatalog
{
    public sealed record Entry(string Codename, string Name, string SlotLabel, uint Id);

    public static readonly IReadOnlyList<Entry> All =
    [
        new("Runeword_Grief_1HSword", "Grief", "1HSword", 0x28d6cb),
        new("Runeword_Grief_Axe", "Grief", "Axe", 0x28fe54),
        new("Runeword_Infinity_Crossbow2H", "Infinity", "2HCrossbow", 0x28fba5),
        new("Runeword_Infinity_Sword2H", "Infinity", "2HSword", 0x28fbab),
        new("Runeword_Infinity_Axe2H", "Infinity", "2HAxe", 0x28fba3),
        new("Runeword_Infinity_Mace2H", "Infinity", "2HMace", 0x28fba7),
        new("Runeword_Infinity_2HScythe", "Infinity", "2HScythe", 0x28dbe8),
        new("Runeword_Infinity_Bow", "Infinity", "Bow", 0x28fbb3),
        new("Runeword_Infinity_Staff", "Infinity", "Staff", 0x28fb87),
        new("Runeword_Infinity_Glaive", "Infinity", "Glaive", 0x28fbaf),
        new("Runeword_Infinity_Polearm", "Infinity", "Polearm", 0x28fbad),
        new("Runeword_Infinity_Quarterstaff", "Infinity", "Quarterstaff", 0x28fbb1),
        new("Runeword_Enigma", "Enigma", "Chest", 0x28d642),
    ];

    private static readonly Dictionary<string, Entry> ByCodename = All.ToDictionary(
        entry => entry.Codename, StringComparer.OrdinalIgnoreCase);

    public static bool TryGetByCodename(string codename, out Entry entry) =>
        ByCodename.TryGetValue(codename, out entry!);

    public static IReadOnlyList<uint> IdsForName(string name) =>
        All.Where(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Id)
            .Distinct()
            .ToList();
}
