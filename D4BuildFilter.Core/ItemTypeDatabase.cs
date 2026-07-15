namespace D4BuildFilter.Core;

/// <summary>
/// Diablo 4 item-type IDs (Season 13 / Lord of Hatred), from d4data's <c>itemTypes</c>
/// (DiabloTools/d4data via ThunderEagle/D4LootBench, MIT). These are the uint hashes used by the
/// loot-filter item-type condition (<see cref="Conditions.Types"/>). The 0x0006d1xx space is
/// filter-validated: real community filters use e.g. 0x0006d159 (Dagger) in type-5 conditions.
///
/// Newer-class weapons (Flail = Paladin/Warlock, Glaive/Quarterstaff = Spiritborn) sit OUTSIDE
/// that block — their id is the ItemType <c>__snoID__</c> in d4data, cross-checked against
/// fnuecke's loot-filter-viewer names.json. Flail (0x234a98) is additionally filter-validated:
/// two real in-game exports (GameRant Universal, wudijo endgame — D4LootBench reference codes)
/// carry it in type-5 conditions, proving new types use the raw snoID like the legacy block.
///
/// Used to scope build-affix rules to a specific gear slot (e.g. "Boots AND these affixes"),
/// which removes the cross-slot false positives of the single combined-pool model.
///
/// (Note: d4data maps Charm=0x0022ed05 / Horadric Seal=0x00237e80 — the reverse of the labels on
/// D4Filter.cs's ItemTypes.Charm/Seal. Harmless: the Charms&amp;Seals rule uses both ids together.)
/// </summary>
public static class ItemTypeDatabase
{
    public static readonly IReadOnlyDictionary<string, uint> ByName = new Dictionary<string, uint>
    {
        // Weapons
        ["Axe"] = 0x0006d151,
        ["Two-Handed Axe"] = 0x0006d152,
        ["Mace"] = 0x0006d13a,
        ["Two-Handed Mace"] = 0x0006d144,
        ["Sword"] = 0x0006d14c,
        ["Two-Handed Sword"] = 0x0006d14f,
        ["Dagger"] = 0x0006d159,
        ["Polearm"] = 0x0006d15d,
        ["Scythe"] = 0x0006d154,
        ["Two-Handed Scythe"] = 0x0006d155,
        ["Staff"] = 0x0006d153,
        ["Wand"] = 0x0006d163,
        ["Focus"] = 0x0006d16a,
        ["Bow"] = 0x0006d167,
        ["Crossbow"] = 0x0006d168,
        ["Two-Handed Crossbow"] = 0x0006d169,
        ["Totem"] = 0x0006d16b,
        ["Flail"] = 0x00234a98,          // Paladin/Warlock 1H (d4data snoID 2312856)
        ["Glaive"] = 0x00165271,         // Spiritborn 2H (d4data snoID 1462897)
        ["Quarterstaff"] = 0x0016d22d,   // Spiritborn 2H (d4data snoID 1495597)
        // Armor
        ["Chest Armor"] = 0x0006d16d,
        ["Helm"] = 0x0006d16e,
        ["Pants"] = 0x0006d16f,
        ["Boots"] = 0x0006d170,
        ["Gloves"] = 0x0006d171,
        ["Shield"] = 0x0006d172,
        // Accessories
        ["Ring"] = 0x0006d174,
        ["Amulet"] = 0x0006d175,
        // Special
        ["Charm"] = 0x0022ed05,
        ["Horadric Seal"] = 0x00237e80,
    };

    private static readonly uint[] AllWeapons =
    {
        0x0006d151, 0x0006d152, 0x0006d13a, 0x0006d144, 0x0006d14c, 0x0006d14f, 0x0006d159,
        0x0006d15d, 0x0006d154, 0x0006d155, 0x0006d153, 0x0006d163, 0x0006d167, 0x0006d168, 0x0006d169,
        0x00234a98, 0x00165271, 0x0016d22d,   // Flail, Glaive, Quarterstaff
    };
    private static readonly HashSet<uint> WeaponSet = new(AllWeapons);

    /// <summary>True when every id is a weapon type. Used to merge weapon slots so the Barbarian
    /// arsenal's 4 weapon slots don't each get their own gold+silver rule and blow the 25-rule cap.
    /// Off-hands (Focus/Totem/Shield) are NOT weapons here, so they keep their own rule.</summary>
    public static bool IsWeaponSlot(IReadOnlyList<uint> typeIds) =>
        typeIds.Count > 0 && typeIds.All(WeaponSet.Contains);

    private static readonly HashSet<uint> TwoHandedSet = new(new uint[]
    {
        0x0006d152, // Two-Handed Axe
        0x0006d144, // Two-Handed Mace
        0x0006d14f, // Two-Handed Sword
        0x0006d15d, // Polearm
        0x0006d155, // Two-Handed Scythe
        0x0006d153, // Staff
        0x0006d167, // Bow
        0x0006d169, // Two-Handed Crossbow
        0x00165271, // Glaive (Spiritborn 2H — d4data body slots match Polearm's)
        0x0016d22d, // Quarterstaff (Spiritborn 2H — same)
    });

    /// <summary>Bucket a weapon slot by handedness so a Barb gets a "1H Weapons" rule (dual-wield)
    /// separate from "2H Weapons" — they want different stats. Returns "2h" (all types 2-handed),
    /// "1h" (none 2-handed), or "" when the slot's types span both (an ambiguous label like
    /// d4builds' "Bludgeoning Weapon"; the caller pools those generically as "Weapons").
    /// maxroll gives exact types ("1HMace"/"2HSword") so its barbs split cleanly.</summary>
    public static string WeaponHandedness(IReadOnlyList<uint> typeIds)
    {
        bool any2h = typeIds.Any(TwoHandedSet.Contains);
        bool all2h = typeIds.All(TwoHandedSet.Contains);
        return all2h ? "2h" : (any2h ? "" : "1h");
    }
    private static readonly uint[] Bludgeoning = { 0x0006d13a, 0x0006d144 };                          // maces
    private static readonly uint[] Slashing = { 0x0006d151, 0x0006d152, 0x0006d14c, 0x0006d14f };       // axes + swords
    private static readonly uint[] OneHanded = { 0x0006d151, 0x0006d13a, 0x0006d14c, 0x0006d159, 0x0006d163, 0x0006d167, 0x0006d168, 0x00234a98 }; // + Flail (dual-wieldable: d4data body slots 14/15 like dagger/sword)
    private static readonly uint[] Ranged = { 0x0006d167, 0x0006d168, 0x0006d169 };                     // bow/xbow
    private static readonly uint[] Offhand = { 0x0006d16a, 0x0006d16b, 0x0006d172 };                    // focus/totem/shield

    /// <summary>Map a build's gear-SLOT label (d4builds gear key or mobalytics gameSlotSlug,
    /// case-insensitive; trailing index digits like "Ring 1" are ignored) to the item-type id(s)
    /// to filter on. Returns null when the slot can't be confidently mapped (caller emits no
    /// item-type condition for that slot).</summary>
    public static IReadOnlyList<uint>? ResolveSlot(string slotLabel)
    {
        if (string.IsNullOrWhiteSpace(slotLabel)) return null;
        var s = slotLabel.Trim().ToLowerInvariant().Replace('-', ' ').Replace('_', ' ');
        while (s.Length > 0 && (char.IsDigit(s[^1]) || s[^1] == ' ')) s = s[..^1];   // drop "Ring 1" -> "ring"
        s = string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return s switch
        {
            "helm" => new[] { 0x0006d16eu },
            "chest armor" or "chest" => new[] { 0x0006d16du },
            "gloves" => new[] { 0x0006d171u },
            "pants" => new[] { 0x0006d16fu },
            "boots" => new[] { 0x0006d170u },
            "amulet" => new[] { 0x0006d175u },
            "ring" => new[] { 0x0006d174u },
            "offhand" or "off hand" => Offhand,
            "focus" => new[] { 0x0006d16au },
            "shield" => new[] { 0x0006d172u },
            "totem" => new[] { 0x0006d16bu },
            "bludgeoning weapon" => Bludgeoning,
            "slashing weapon" => Slashing,
            "dual wield weapon" => OneHanded,
            "ranged weapon" => Ranged,
            "weapon" or "two handed weapon" => AllWeapons,
            // maxroll item-id prefixes give the EXACT weapon type (e.g. "1HMace", "2HSword").
            "mace" or "1hmace" => new[] { 0x0006d13au },
            "2hmace" => new[] { 0x0006d144u },
            "sword" or "1hsword" => new[] { 0x0006d14cu },
            "2hsword" => new[] { 0x0006d14fu },
            "axe" or "1haxe" => new[] { 0x0006d151u },
            "2haxe" => new[] { 0x0006d152u },
            "dagger" or "1hdagger" => new[] { 0x0006d159u },
            "polearm" or "2hpolearm" => new[] { 0x0006d15du },
            "scythe" or "1hscythe" => new[] { 0x0006d154u },
            "2hscythe" => new[] { 0x0006d155u },
            "staff" or "2hstaff" => new[] { 0x0006d153u },
            "wand" or "1hwand" => new[] { 0x0006d163u },
            "bow" or "2hbow" => new[] { 0x0006d167u },
            "crossbow" or "1hcrossbow" => new[] { 0x0006d168u },
            "2hcrossbow" => new[] { 0x0006d169u },
            // Newer-class weapons. Game item ids use bare "Quarterstaff_"/"Glaive_" but "1HFlail_"
            // (per bundled Uniques.enUS.json); the 1H/2H-prefixed aliases are accepted both ways.
            "flail" or "1hflail" => new[] { 0x00234a98u },
            "glaive" or "2hglaive" => new[] { 0x00165271u },
            "quarterstaff" or "2hquarterstaff" => new[] { 0x0016d22du },
            // Maxroll prefixes off-hands with 1H even though these stay separate from weapon pools.
            "1hfocus" => new[] { 0x0006d16au },
            "1hshield" => new[] { 0x0006d172u },
            "1htotem" => new[] { 0x0006d16bu },
            _ => null,
        };
    }
}
