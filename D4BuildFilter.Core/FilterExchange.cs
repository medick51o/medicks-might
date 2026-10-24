using System.Text.RegularExpressions;

namespace D4BuildFilter.Core;

/// <summary>
/// Imports an in-game loot-filter export (base64 protobuf), re-encodes it, and merges two
/// exports into the shared 25-rule slot layout. A plain import re-encodes the rules it decoded.
/// It does not reshape them into the build layout, so a filter the game accepted round-trips
/// with the same rule names.
/// </summary>
public static class FilterExchange
{
    private static readonly Regex ImportCharset = new("^[A-Za-z0-9+/]+=*$", RegexOptions.Compiled);
    private static readonly Regex SlotTitle = new(@"^(?<label>.+?) \[\d+\+\]", RegexOptions.Compiled);

    public static bool LooksLikeImportCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim();
        if (trimmed.Length < 40 || !ImportCharset.IsMatch(trimmed)) return false;
        return TryImport(trimmed, out _);
    }

    public static bool TryImport(string text, out DecodedFilter filter)
    {
        filter = new DecodedFilter();
        try
        {
            var bytes = Convert.FromBase64String(text.Trim());
            filter = FilterDecoder.Decode(bytes);
        }
        catch (FormatException)
        {
            return false;
        }

        return filter.Rules.Count > 0
            && filter.Rules.Any(rule => rule.Name.Length > 0 && rule.Visibility is 0 or 2 or 3);
    }

    /// <summary>Re-encode a decoded filter. Rule order, ids, set members, and Greater Affix
    /// markers are preserved. Unknown condition fields are not reconstructed.</summary>
    public static string Export(DecodedFilter filter)
    {
        var rules = filter.Rules.Select(EncodeRule).ToList();
        // Keep a name the game already accepted. Newly compiled filters still clamp at 24.
        return FilterBuilder.ToImportCode(FilterBuilder.MakeFilter(
            string.IsNullOrWhiteSpace(filter.Name) ? "D4BuildFilter" : filter.Name, rules, clampName: false));
    }

    public static bool SemanticallyEqual(DecodedFilter left, DecodedFilter right)
    {
        if (!string.Equals(left.Name, right.Name, StringComparison.Ordinal)) return false;
        if (left.Rules.Count != right.Rules.Count) return false;
        for (int i = 0; i < left.Rules.Count; i++)
            if (!RuleEqual(left.Rules[i], right.Rules[i])) return false;
        return true;
    }

    /// <summary>Union two or more in-game filters into the canonical slot layout.</summary>
    public static FilterOutput MergeIntoLayout(IReadOnlyList<DecodedFilter> filters, string? label = null)
    {
        if (filters.Count == 0)
            throw new ArgumentException("At least one filter is required.", nameof(filters));
        var builds = filters.Select(ToCompiledBuild).ToList();
        var sets = filters.SelectMany(SetsIn).Distinct().ToList();
        var options = new FilterOptions
        {
            PerSlotRules = true,
            ItemPowerTiers = false,
            GreaterAffixes = false,
            ShowUniqueCharms = false,
            CubeBases = false,
            NineHundredOnly = false,
            Leveling = false,
            TalismanSets = sets.Count > 0 ? sets : null,
        };
        return FilterCompiler.Compile(builds, options, label ?? "merged",
            string.IsNullOrWhiteSpace(filters[0].Name) ? "D4BuildFilter" : filters[0].Name);
    }

    public static CompiledBuild ToCompiledBuild(DecodedFilter filter)
    {
        var uniques = new List<uint>();
        var slots = new List<SlotPool>();
        var pool = new List<uint>();
        var poolSeen = new HashSet<uint>();
        uint chase = FilterColors.Red;
        uint keeper = FilterColors.Pink;
        bool sawChase = false;
        bool sawKeeper = false;

        foreach (var rule in filter.Rules)
        {
            if (rule.Name.StartsWith("Build Uniques", StringComparison.OrdinalIgnoreCase)
                && rule.Visibility == (int)Visibility.Recolor)
            {
                var ids = rule.Conditions.FirstOrDefault(c => c.Type == 8)?.Ids;
                if (ids is not null)
                    foreach (var id in ids)
                        if (!uniques.Contains(id)) uniques.Add(id);
                continue;
            }

            if (IsUtility(rule.Name) || rule.Visibility != (int)Visibility.Recolor) continue;
            var affix = rule.Conditions.FirstOrDefault(c => c.Type == 6);
            if (affix is null || affix.Ids.Count == 0) continue;
            var match = SlotTitle.Match(rule.Name);
            if (!match.Success) continue;
            var label = match.Groups["label"].Value.Trim();
            var types = rule.Conditions.FirstOrDefault(c => c.Type == 5)?.Ids ?? new List<uint>();
            slots.Add(new SlotPool(label, types.ToList(), affix.Ids.ToList()));
            foreach (var id in affix.Ids)
                if (poolSeen.Add(id)) pool.Add(id);

            var rarity = rule.Conditions.FirstOrDefault(c => c.Type == 1)?.MaskOrCount;
            if (rarity is ulong mask)
            {
                if (!sawChase && (mask & Rarity.Legendary) != 0) { chase = rule.Color; sawChase = true; }
                if (!sawKeeper && (mask & Rarity.Rare) != 0 && (mask & Rarity.Legendary) == 0)
                { keeper = rule.Color; sawKeeper = true; }
            }
        }

        var setNames = SetsIn(filter).Select(set => set.Name).ToList();
        var className = SetsIn(filter).Select(set => set.Class).FirstOrDefault(TalismanSetDatabase.IsKnownClass)
            ?? "Rogue";
        var names = pool.ToDictionary(id => id, id => "0x" + id.ToString("x"));
        return new CompiledBuild(filter.Name, className, chase, keeper, pool, names,
            [], uniques, [], [], slots, [], setNames);
    }

    private static List<TalismanSet> SetsIn(DecodedFilter filter)
    {
        var ids = filter.Rules
            .SelectMany(rule => rule.Conditions)
            .Where(condition => condition.Type == 9)
            .SelectMany(condition => condition.Ids)
            .Distinct();
        var sets = new List<TalismanSet>();
        foreach (var id in ids)
            if (TalismanSetDatabase.ById.TryGetValue(id, out var set))
                sets.Add(set);
        return sets;
    }

    private static bool IsUtility(string name) =>
        name.StartsWith("Codex", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Hide", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Charms", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Item Power", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Greater", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Unique Charms", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Cube", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("900", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Leveling", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Soul", StringComparison.OrdinalIgnoreCase);

    private static byte[] EncodeRule(DecodedRule rule) =>
        FilterBuilder.MakeRule(rule.Name, (Visibility)rule.Visibility,
            rule.Conditions.Select(EncodeCondition).ToList(), rule.Color, rule.Enabled, clampName: false);

    private static byte[] EncodeCondition(DecodedCondition condition)
    {
        switch (condition.Type)
        {
            case 0 when condition.MaskOrCount is ulong min && condition.Max is ulong max:
                return Conditions.ItemPower((uint)min, (uint)max);
            case 0:
                return Conditions.ItemPowerAny();
            case 1:
                return Conditions.RarityMask((uint)(condition.MaskOrCount ?? 0));
            case 2:
                return Conditions.Ancestral();
            case 3:
                return Conditions.Codex();
            case 4:
                return Conditions.GreaterAffix((int)(condition.Count ?? condition.MaskOrCount ?? 1));
            case 5:
                return Conditions.Types(condition.Ids);
            case 6:
                return EncodeAffixes(condition);
            case 8:
                return Conditions.Uniques(condition.Ids);
            case 9 when condition.SetItems.Count > 0:
                return Conditions.TalismanSetBonus(condition.SetItems
                    .Select(set => (set.SetId, (IReadOnlyList<uint>)set.Items)));
            case 9:
                return Conditions.TalismanSetBonus(condition.Ids);
            default:
                return Conditions.ItemPowerAny();
        }
    }

    private static byte[] EncodeAffixes(DecodedCondition condition)
    {
        var inner = Wire.Efv(1, 6);
        foreach (var id in condition.Ids) inner = Wire.Concat(inner, Wire.Ef32(2, id));
        foreach (var id in condition.GreaterAffixOf)
            inner = Wire.Concat(inner, Wire.Efb(3, Wire.Concat(Wire.Ef32(1, id), Wire.Ef32(2, id))));
        inner = Wire.Concat(inner, Wire.Efv(4, condition.MaskOrCount ?? 1));
        return Wire.Efb(4, inner);
    }

    private static bool RuleEqual(DecodedRule left, DecodedRule right) =>
        left.Name == right.Name
        && left.Visibility == right.Visibility
        && left.Color == right.Color
        && left.Enabled == right.Enabled
        && left.Conditions.Count == right.Conditions.Count
        && left.Conditions.Zip(right.Conditions).All(pair => ConditionEqual(pair.First, pair.Second));

    private static bool ConditionEqual(DecodedCondition left, DecodedCondition right) =>
        left.Type == right.Type
        && left.Ids.SequenceEqual(right.Ids)
        && left.MaskOrCount == right.MaskOrCount
        && left.Count == right.Count
        && left.Max == right.Max
        && left.GreaterAffixOf.SequenceEqual(right.GreaterAffixOf)
        && left.SetItems.Count == right.SetItems.Count
        && left.SetItems.Zip(right.SetItems).All(pair =>
            pair.First.SetId == pair.Second.SetId && pair.First.Items.SequenceEqual(pair.Second.Items));
}
