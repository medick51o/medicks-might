namespace D4BuildFilter.Core;

/// <summary>How a rule renders matching items. Lower-numbered rules in the filter
/// take priority (the game applies rules top-down).</summary>
public enum Visibility
{
    Show = 0,
    Recolor = 2,
    HideAll = 3,
}

/// <summary>Rarity bitmask flags used by the rarity condition.</summary>
public static class Rarity
{
    public const uint Common = 0x01;
    public const uint Magic = 0x02;
    public const uint Rare = 0x04;
    public const uint Legendary = 0x08;
    public const uint Unique = 0x10;
    public const uint Mythic = 0x20;
    public const uint Talisman = 0x40;

    public const uint LegendaryPlus = Legendary | Unique | Mythic | Talisman; // 0x78
    public const uint All = 0x7F;
}

/// <summary>Item-type IDs usable in the item-type condition.</summary>
public static class ItemTypes
{
    public const uint Charm = 0x00237e80;
    public const uint Seal = 0x0022ed05;
}

/// <summary>Builders for individual rule conditions. Each returns the encoded bytes
/// of one condition (a field-4 entry inside a rule).</summary>
public static class Conditions
{
    public static byte[] RarityMask(uint mask) =>
        Wire.Efb(4, Wire.Concat(Wire.Efv(1, 1), Wire.Efv(4, mask)));

    // Condition types verified by decoding a real published filter (rootsxo Minion Barb):
    // type 3 = Codex-of-Power upgrade check, type 4 = Greater Affix count. These are
    // SWAPPED vs the Upsilon schema we started from.

    /// <summary>Codex-of-Power upgrade check (item teaches/upgrades an aspect). Real filter
    /// rule "Codex Upgrades" = type 3 + field6=1.</summary>
    public static byte[] Codex() =>
        Wire.Efb(4, Wire.Concat(Wire.Efv(1, 3), Wire.Efv(6, 1)));

    /// <summary>Item has at least <paramref name="count"/> Greater Affixes. Real filter rule
    /// "Greater Affixes" = type 4 + field4=1 + field6=count (field semantics still tentative).</summary>
    public static byte[] GreaterAffix(int count) =>
        Wire.Efb(4, Wire.Concat(Wire.Efv(1, 4), Wire.Efv(4, 1), Wire.Efv(6, (ulong)count)));

    /// <summary>Item is at least Ancestral tier (the top item-power band). Real filters encode
    /// this as type 2 with value 4; rootsxo gates most of his rules on it for T6+ farming.</summary>
    public static byte[] Ancestral() =>
        Wire.Efb(4, Wire.Concat(Wire.Efv(1, 2), Wire.Efv(4, 4)));

    /// <summary>Item power within the inclusive numeric range [<paramref name="min"/>,
    /// <paramref name="max"/>] — the "Item Power Range" condition. type 0, field4=min, field5=max.
    /// Confirmed from real S13 exports (bmbernie's reverse-engineering: <c>1:0, 4:800, 5:900</c>,
    /// and an in-game "Ancestral Gear" rule that encodes <c>4:900, 5:900</c>).</summary>
    public static byte[] ItemPower(uint min, uint max) =>
        Wire.Efb(4, Wire.Concat(Wire.Efv(1, 0), Wire.Efv(4, min), Wire.Efv(5, max)));

    /// <summary>Bare Item-Power condition (type 0, no bounds) — the filler condition the 3.1
    /// in-game editor emits on set-scoped rules; mirrors the hand-built export byte-for-byte.</summary>
    public static byte[] ItemPowerAny() => Wire.Efb(4, Wire.Efv(1, 0));

    /// <summary>Match specific Unique item(s) by id (per-unique targeting). Real filter rules
    /// "Equipable Uniques"/"Ancestral Uniques" = type 8 + repeated fixed32 ids.</summary>
    public static byte[] Uniques(IEnumerable<uint> ids)
    {
        var inner = Wire.Efv(1, 8);
        foreach (var id in ids) inner = Wire.Concat(inner, Wire.Ef32(2, id));
        return Wire.Efb(4, inner);
    }

    /// <summary>Match a specific Talisman / set-bonus by snoID (the type-9 "Talisman Set Bonus"
    /// condition). Same shape as <see cref="Uniques"/>: type id + repeated fixed32 snoIDs from
    /// <see cref="SetItemBonusDatabase"/>. The "blue talisman" rule we decoded in Raxx's filter
    /// uses this form.</summary>
    public static byte[] TalismanSetBonus(IEnumerable<uint> ids)
    {
        var inner = Wire.Efv(1, 9);
        foreach (var id in ids) inner = Wire.Concat(inner, Wire.Ef32(2, id));
        return Wire.Efb(4, inner);
    }

    /// <summary>S14 (3.1) form: set(s) WITH per-item refinement. Emits field2 (the set id) plus a
    /// field3 sub-message <c>{1: set id, 2: item id ×N}</c> per set — byte-matching the 3.1
    /// in-game editor's own export (pinned by test against a real hand-built rule, 2026-07-02).</summary>
    public static byte[] TalismanSetBonus(IEnumerable<(uint SetId, IReadOnlyList<uint> Items)> sets)
    {
        var inner = Wire.Efv(1, 9);
        foreach (var (setId, items) in sets)
        {
            inner = Wire.Concat(inner, Wire.Ef32(2, setId));
            var sub = Wire.Ef32(1, setId);
            foreach (var it in items) sub = Wire.Concat(sub, Wire.Ef32(2, it));
            inner = Wire.Concat(inner, Wire.Efb(3, sub));
        }
        return Wire.Efb(4, inner);
    }

    /// <summary>Match items carrying at least <paramref name="minCount"/> of the given affix IDs.</summary>
    public static byte[] Affixes(IEnumerable<uint> ids, int minCount)
    {
        var inner = Wire.Efv(1, 6);
        foreach (var id in ids) inner = Wire.Concat(inner, Wire.Ef32(2, id));
        inner = Wire.Concat(inner, Wire.Efv(4, (ulong)minCount));
        return Wire.Efb(4, inner);
    }

    public static byte[] Types(IEnumerable<uint> ids)
    {
        var inner = Wire.Efv(1, 5);
        foreach (var id in ids) inner = Wire.Concat(inner, Wire.Ef32(2, id));
        return Wire.Efb(4, inner);
    }
}

/// <summary>Assembles rules and a complete filter, then produces the import code.</summary>
public static class FilterBuilder
{
    /// <summary>D4 silently DROPS a filter name OR rule name longer than this on import (the filter
    /// reverts to "Loot Filter #N" and rules to "Rule #N" — verified in-game). So every name we
    /// emit is clamped to this length.</summary>
    public const int MaxNameLength = 24;

    private static string Clamp(string name) =>
        name.Length <= MaxNameLength ? name : name[..MaxNameLength].TrimEnd();

    public static byte[] MakeRule(string name, Visibility visibility, IReadOnlyList<byte[]> conditions,
        uint color = FilterColors.Default, bool enabled = true)
    {
        var rule = Wire.Concat(Wire.Efs(1, Clamp(name)), Wire.Efv(2, (ulong)visibility), Wire.Ef32(3, color));
        foreach (var c in conditions) rule = Wire.Concat(rule, c);
        rule = Wire.Concat(rule, Wire.Efv(5, enabled ? 1ul : 0ul)); // field 5: rule enabled (1) / disabled-but-present (0)
        return Wire.Efb(1, rule);
    }

    /// <summary>Rules must be supplied in encoded order (lowest priority first).</summary>
    public static byte[] MakeFilter(string name, IReadOnlyList<byte[]> rules)
    {
        var data = Array.Empty<byte>();
        foreach (var rule in rules) data = Wire.Concat(data, rule);
        // field 3 is NOT a rule count (a real 13-rule filter sets it to 1); treat as a format flag.
        data = Wire.Concat(data, Wire.Efs(2, Clamp(name)), Wire.Efv(3, 1), Wire.Efv(4, 1));
        return data;
    }

    public static string ToImportCode(byte[] filterBytes) => Convert.ToBase64String(filterBytes);
}
