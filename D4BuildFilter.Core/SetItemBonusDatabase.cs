namespace D4BuildFilter.Core;

/// <summary>
/// Diablo 4 talisman / set-bonus IDs — the snoIDs the loot-filter "Talisman Set Bonus" condition
/// (type 9) accepts. Source: DiabloTools/d4data <c>json/base/meta/SetItemBonus/*.set.json</c>
/// (build 3.0.3.72031, MIT). One snoID per <c>SetItemBonusDefinition</c>; ingested 2026-05-28.
///
/// FILTER-SPACE NOTE: the unresolved ID <c>0x234a98</c> (2312856) that appeared in Raxx's community
/// filter as a "blue talisman" type sits inside the confirmed talisman snoID range (0x22fb15 →
/// 0x23b61a) but is NOT one of the public dumped sets — likely a cut "Generic 04/05" entry.
/// Mapped here as <c>"Unknown Generic Talisman Set"</c> so decoded filters resolve to a label
/// instead of UNKNOWN.
/// </summary>
public static class SetItemBonusDatabase
{
    /// <summary>Display name → snoID. Names are derived from the d4data filenames (e.g.
    /// <c>Talisman_Barb_01.set.json</c> → "Talisman: Barbarian Set 01") since the source JSONs
    /// don't ship a localized display string. Numeric suffix preserved so all five tiers per
    /// class stay distinct in the UI.</summary>
    public static readonly IReadOnlyDictionary<string, uint> ByName =
        new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            // Barbarian (5 tiers)
            ["Talisman: Barbarian Set 01"] = 0x22fb15,
            ["Talisman: Barbarian Set 02"] = 0x22fb41,
            ["Talisman: Barbarian Set 03"] = 0x22fce9,
            ["Talisman: Barbarian Set 04"] = 0x22fceb,
            ["Talisman: Barbarian Set 05"] = 0x22fcee,
            // Druid
            ["Talisman: Druid Set 01"] = 0x230326,
            ["Talisman: Druid Set 02"] = 0x230380,
            ["Talisman: Druid Set 03"] = 0x230383,
            ["Talisman: Druid Set 04"] = 0x230386,
            ["Talisman: Druid Set 05"] = 0x230389,
            // Necromancer
            ["Talisman: Necromancer Set 01"] = 0x230c68,
            ["Talisman: Necromancer Set 02"] = 0x230d68,
            ["Talisman: Necromancer Set 03"] = 0x230d6a,
            ["Talisman: Necromancer Set 04"] = 0x230d6c,
            ["Talisman: Necromancer Set 05"] = 0x230d6e,
            // Rogue
            ["Talisman: Rogue Set 01"] = 0x230a6a,
            ["Talisman: Rogue Set 02"] = 0x230aa2,
            ["Talisman: Rogue Set 03"] = 0x230ac8,
            ["Talisman: Rogue Set 04"] = 0x230aca,
            ["Talisman: Rogue Set 05"] = 0x230acc,
            // Sorcerer
            ["Talisman: Sorcerer Set 01"] = 0x2243bf,
            ["Talisman: Sorcerer Set 02"] = 0x2242e0,
            ["Talisman: Sorcerer Set 03"] = 0x225065,
            ["Talisman: Sorcerer Set 04"] = 0x2250e4,
            ["Talisman: Sorcerer Set 05"] = 0x225135,
            // Spiritborn
            ["Talisman: Spiritborn Set 01"] = 0x231392,
            ["Talisman: Spiritborn Set 02"] = 0x2314de,
            ["Talisman: Spiritborn Set 03"] = 0x2314e0,
            ["Talisman: Spiritborn Set 04"] = 0x2314e3,
            ["Talisman: Spiritborn Set 05"] = 0x2314e7,
            // Paladin
            ["Talisman: Paladin Set 01"] = 0x23b4cb,
            ["Talisman: Paladin Set 02"] = 0x23b56a,
            ["Talisman: Paladin Set 03"] = 0x23b56c,
            ["Talisman: Paladin Set 04"] = 0x23b56e,
            ["Talisman: Paladin Set 05"] = 0x23b570,
            // Warlock
            ["Talisman: Warlock Set 01"] = 0x23b610,
            ["Talisman: Warlock Set 02"] = 0x23b613,
            ["Talisman: Warlock Set 03"] = 0x23b616,
            ["Talisman: Warlock Set 04"] = 0x23b618,
            ["Talisman: Warlock Set 05"] = 0x23b61a,
            // Generic talisman sets (small talismans, not class-bound). 04/05/07/08 missing from
            // the dump (cut content?). Mystery ID 0x234a98 lives in this gap — mapped below.
            ["Talisman: Generic 1"] = 0x232f8a,
            ["Talisman: Generic 2"] = 0x2331e6,
            ["Talisman: Generic 3"] = 0x233c93,
            ["Talisman: Generic 6"] = 0x235120,
            ["Talisman: Generic 9"] = 0x235942,
            ["Unknown Generic Talisman Set"] = 0x234a98,   // seen in Raxx's filter; missing from d4data dump
        };

    /// <summary>Reverse lookup: snoID → display name. Used by the decoder to label type-9
    /// conditions in community filters with a human-readable set name instead of a hex blob.</summary>
    public static readonly IReadOnlyDictionary<uint, string> ById =
        ByName.GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.First().Key);

    public static bool TryGet(string name, out uint id) => ByName.TryGetValue(name, out id);

    public static bool TryGetName(uint id, out string name)
    {
        if (ById.TryGetValue(id, out var n)) { name = n; return true; }
        name = $"Talisman Set 0x{id:x6}";
        return false;
    }
}
