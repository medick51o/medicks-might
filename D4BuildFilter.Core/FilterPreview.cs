namespace D4BuildFilter.Core;

/// <summary>A read-only presentation of the final import payload. No compiler options are consulted.</summary>
public sealed record FilterPreview(string Name, int Version, IReadOnlyList<FilterPreviewRule> Rules)
{
    public const string EvidenceNote = "Encoded intent from the final import code, in emission order. "
        + "Conditions within each rule apply together. Game acceptance and execution are NOT PROVEN.";

    public string Summary => $"{Rules.Count} encoded rules · {Rules.Count(r => r.Enabled)} enabled";
    public string HideSummary => Rules.Any(r => r.Enabled && r.Visibility == (int)Visibility.HideAll)
        ? string.Join("\n", Rules.Where(r => r.Enabled && r.Visibility == (int)Visibility.HideAll)
            .Select(r => $"{r.Heading} · {r.Scope}; " + string.Join("; ",
                r.Conditions.Where(c => c.Type is not (1 or 5)).Select(c => c.Description))))
        : "No enabled Hide rule is encoded.";

    public static FilterPreview FromImportCode(string importCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(importCode);
        var decoded = FilterDecoder.Decode(importCode);
        var rules = new List<FilterPreviewRule>();
        foreach (var rule in decoded.Rules)
        {
            var conditions = rule.Conditions.Select(FilterPreviewCondition.FromDecoded).ToArray();
            var colorName = FilterColors.TryGetEntry(rule.Color, out var entry)
                ? entry!.FullName : $"Custom (#{rule.Color:X8})";
            string action = rule.Visibility switch
            {
                (int)Visibility.Show => "Show",
                (int)Visibility.Recolor => "Show (recolor)",
                (int)Visibility.HideAll => "Hide",
                _ => $"Unknown action ({rule.Visibility})",
            };
            var slots = conditions.Where(c => c.Type == 5).Select(c => c.Description).ToArray();
            var rarities = conditions.Where(c => c.Type == 1).Select(c => c.Description).ToArray();
            string scope = string.Join(" AND ", slots.Length == 0 ? ["Slots: unrestricted"] : slots)
                + "; " + string.Join(" AND ", rarities.Length == 0 ? ["Rarities: unrestricted"] : rarities);
            string protection;
            if (!rule.Enabled)
                protection = "Disabled in the payload; supplies no protection or hide action.";
            else if (rule.Visibility == (int)Visibility.HideAll)
            {
                var earlierShows = rules.Where(r => r.Enabled && r.IsShow).Select(r => $"#{r.Order} {r.Name}").ToArray();
                protection = earlierShows.Length == 0
                    ? "No earlier enabled Show rule is encoded."
                    : "Candidate protections: " + string.Join(", ", earlierShows)
                        + ". Each must match its own conditions; earlier Hide rules may also apply.";
                if (conditions.Any(c => c.Type == 1 && c.MaskOrCount is { } mask && (mask & Rarity.Mythic) == 0))
                    protection += " Mythic is excluded from this Hide rule's rarity mask.";
                if (conditions.Any(c => c.Type == 0 && c.MaskOrCount == 1 && c.Max == 899))
                    protection += " This range can hide every drop in its selected scope below 900 unless an earlier Show rule matches. "
                        + "This preview does not establish whether power 900 is attainable.";
            }
            else if (rule.Visibility == (int)Visibility.Show || rule.Visibility == (int)Visibility.Recolor)
                protection = "Conditional Show: requires this rule's conditions; earlier rules may take precedence.";
            else
                protection = "Unknown action; no protection can be inferred.";

            rules.Add(new(rules.Count + 1, rule.Name, rule.Visibility, action, rule.Enabled,
                rule.Color, colorName, scope, Array.AsReadOnly(conditions), protection));
        }
        return new(decoded.Name, decoded.Version, rules.AsReadOnly());
    }
}

public sealed record FilterPreviewRule(int Order, string Name, int Visibility, string Action,
    bool Enabled, uint Color, string ColorName, string Scope,
    IReadOnlyList<FilterPreviewCondition> Conditions, string ProtectionNote)
{
    public bool IsShow => Visibility == (int)global::D4BuildFilter.Core.Visibility.Show
        || Visibility == (int)global::D4BuildFilter.Core.Visibility.Recolor;
    public string ColorHex => $"#{Color & 0xFFFFFF:X6}";
    public string Heading => $"{Order}. {Action}{(Enabled ? "" : " [disabled]")} · {Name}"
        + (Visibility == (int)global::D4BuildFilter.Core.Visibility.Recolor ? $" · {ColorName}" : "");
    public string Details => Conditions.Count == 0 ? "No conditions encoded."
        : string.Join("\n", Conditions.Select(c => c.Description));
    public string DisplayText => $"{Heading}\n{Scope}\n{Details}\n{ProtectionNote}";
}

/// <summary>Preserves decoded counts and IDs, including unfamiliar fields, alongside readable text.</summary>
public sealed record FilterPreviewCondition(int Type, IReadOnlyList<uint> Ids, ulong? MaskOrCount,
    ulong? Count, ulong? Max, IReadOnlyList<uint> GreaterAffixOf,
    IReadOnlyList<FilterPreviewSetItems> SetItems, IReadOnlyList<string> Unknown, string Description)
{
    private static readonly IReadOnlyDictionary<uint, string> TypeNames = ItemTypeDatabase.ByName
        .GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.First().Key);

    internal static FilterPreviewCondition FromDecoded(DecodedCondition condition)
    {
        var c = condition;
        string description = c.Type switch
        {
            0 => $"Item power: {Value(c.MaskOrCount)}–{Value(c.Max)} (inclusive; unspecified bounds are not inferred)",
            1 => "Rarities: " + DescribeRarities(c.MaskOrCount),
            2 => c.MaskOrCount == 4 ? "Ancestral item property (encoded value 4)"
                : $"Item properties: {Value(c.MaskOrCount)} (uninterpreted)",
            3 => $"Codex upgrade condition: {Value(c.Count)}",
            4 => $"Greater Affixes: count {Value(c.Count)}, selector {Value(c.MaskOrCount)} (semantics tentative)",
            5 => "Slots: " + (c.Ids.Count == 0 ? "empty type list (not unrestricted)"
                : string.Join(", ", c.Ids.Select(id => TypeNames.TryGetValue(id, out var name) ? name : Hex(id)))),
            6 or 7 => $"{(c.Type == 6 ? "Required" : "Optional")} affixes: minimum {Value(c.MaskOrCount)} of {c.Ids.Count} encoded IDs"
                + $" ({c.Ids.Distinct().Count()} distinct) · {IdsText(c.Ids)}",
            8 => $"Unique IDs: {c.Ids.Count} · {IdsText(c.Ids)}",
            9 => $"Talisman sets: {c.Ids.Count} · " + string.Join(", ", c.Ids.Select(id =>
                SetItemBonusDatabase.TryGetName(id, out var name) ? $"{name} ({Hex(id)})" : Hex(id))),
            _ => $"Unknown condition type {c.Type}",
        };
        // Never silently discard a decoder field merely because it is unexpected for a known type.
        if (c.Ids.Count > 0 && c.Type is not (5 or 6 or 7 or 8 or 9))
            description += $"; IDs: {IdsText(c.Ids)}";
        if (c.MaskOrCount.HasValue && c.Type is not (0 or 1 or 2 or 4 or 6 or 7))
            description += $"; field 4: {c.MaskOrCount}";
        if (c.Count.HasValue && c.Type is not (3 or 4)) description += $"; field 6: {c.Count}";
        if (c.Max.HasValue && c.Type != 0) description += $"; field 5: {c.Max}";
        if (c.GreaterAffixOf.Count > 0)
            description += $"; must be Greater Affixes ({c.GreaterAffixOf.Count}): {IdsText(c.GreaterAffixOf)}";
        foreach (var set in c.SetItems)
            description += $"; set {Hex(set.SetId)} members ({set.Items.Count}): {IdsText(set.Items)}";
        if (c.Unknown.Count > 0) description += "; Uninterpreted fields: " + string.Join(", ", c.Unknown);
        return new(c.Type, Array.AsReadOnly(c.Ids.ToArray()), c.MaskOrCount, c.Count, c.Max,
            Array.AsReadOnly(c.GreaterAffixOf.ToArray()),
            Array.AsReadOnly(c.SetItems.Select(s => new FilterPreviewSetItems(s.SetId,
                Array.AsReadOnly(s.Items.ToArray()))).ToArray()),
            Array.AsReadOnly(c.Unknown.ToArray()), description);
    }

    private static string DescribeRarities(ulong? mask)
    {
        if (mask is not { } value) return "mask unspecified";
        var names = new List<string>();
        (uint Bit, string Name)[] known = [(Rarity.Common, "Common"), (Rarity.Magic, "Magic"),
            (Rarity.Rare, "Rare"), (Rarity.Legendary, "Legendary"), (Rarity.Unique, "Unique"),
            (Rarity.Mythic, "Mythic"), (Rarity.Talisman, "Talisman")];
        foreach (var (bit, name) in known)
            if ((value & bit) != 0) names.Add(name);
        if ((value & ~(ulong)Rarity.All) != 0) names.Add($"unknown bits 0x{value & ~(ulong)Rarity.All:X}");
        return (names.Count == 0 ? "none" : string.Join(", ", names)) + $" (mask 0x{value:X})";
    }

    private static string Value(ulong? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unspecified";
    private static string Hex(uint id) => $"0x{id:X8}";
    private static string IdsText(IEnumerable<uint> ids) => string.Join(", ", ids.Select(Hex));
}

public sealed record FilterPreviewSetItems(uint SetId, IReadOnlyList<uint> Items);
