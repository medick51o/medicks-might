namespace D4BuildFilter.Core;

/// <summary>
/// A resolved build analyzed into the pieces the encoder needs:
/// its filterable coarse-affix pool, its targetable build uniques, and the
/// affixes/uniques we couldn't map yet (the in-game "capture these IDs" to-do list).
/// </summary>
public sealed record CompiledBuild(
    string Name,
    uint Color,
    uint Dim,
    IReadOnlyList<uint> Pool,
    IReadOnlyDictionary<uint, string> Names,
    IReadOnlyList<string> Dropped,
    IReadOnlyList<uint> UniqueIds,
    IReadOnlyList<string> UniquesTargeted,
    IReadOnlyList<string> UniquesPending)
{
    /// <summary>The pool's coarse-affix display names, in pool order.</summary>
    public IEnumerable<string> PoolNames => Pool.Select(id => Names[id]);
}

/// <summary>One compiled filter: the base64 import code plus a self-check.</summary>
public sealed record FilterOutput(string Label, string ImportCode, int RuleCount, int Bytes, bool RoundTripOk);

/// <summary>The streamlined customization surface — which rules the filter includes.
/// Defaults reproduce the full recommended filter; the WPF app binds toggles to these.</summary>
public sealed record FilterOptions
{
    /// <summary>Gate the build-affix + greater-affix tiers to Ancestral (top item-power) gear —
    /// great for T6+ farming, hides everything while leveling.</summary>
    public bool StrictEndgame { get; init; }
    /// <summary>The build's own uniques → purple.</summary>
    public bool BuildUniques { get; init; } = true;
    /// <summary>Rare/legendary with ≥2 build affixes → silver (the "one reroll away" tier).</summary>
    public bool SilverTier { get; init; } = true;
    /// <summary>Item-power tiers: 900+ → orange, 850+ → cyan.</summary>
    public bool ItemPowerTiers { get; init; } = true;
    /// <summary>Any rare/legendary with a Greater Affix → blue.</summary>
    public bool GreaterAffixes { get; init; } = true;
    /// <summary>Charms &amp; Seals → green.</summary>
    public bool CharmsSeals { get; init; } = true;
    /// <summary>Codex-of-Power upgrades → white.</summary>
    public bool Codex { get; init; } = true;
    /// <summary>Hide everything else (Common/Magic/Rare/Legendary the rules above didn't match).
    /// Never touches Unique or Mythic.</summary>
    public bool HideRest { get; init; } = true;
}

/// <summary>
/// Turns a resolved maxroll build into a Diablo 4 loot-filter import code. This is the
/// shared core both the Tester console and the WPF app call, so they produce identical,
/// in-game-validated output. Rule assembly mirrors the model documented in the project:
/// match by affix COUNT (>=3 gold / >=2 silver), rare+leg only; uniques/charms handled
/// separately; mythics never touched; rules emitted most-specific-first (D4 applies them
/// top-down, first match wins) with a scoped hide-all last.
/// </summary>
public static class FilterCompiler
{
    /// <summary>Bright tier: item carries at least this many of the build's pool affixes.</summary>
    public const int Strict = 3;
    /// <summary>Dim tier: "one reroll from great" — at least this many pool affixes.</summary>
    public const int Loose = 2;

    /// <summary>Item-power color tiers (orange = top band, cyan = high band). The numeric
    /// "Item Power Range" condition takes [min,max]; we use an open-ended upper bound so each
    /// tier means "this power and up". Tune the thresholds after an in-game check.</summary>
    public const uint ItemPowerOrange = 900;
    public const uint ItemPowerCyan = 850;
    public const uint ItemPowerCap = 4000;

    /// <summary>
    /// Reduce a resolved build to its filterable affix pool + targetable uniques.
    /// Affixes map through <see cref="AffixMapper"/> (deduped by coarse id, first hit wins);
    /// unmapped ones land in <see cref="CompiledBuild.Dropped"/>. Build uniques resolve to
    /// type-8 filter ids via <see cref="UniqueDatabase"/>; those without an id yet are reported.
    /// </summary>
    public static CompiledBuild Analyze(ResolvedBuild build, uint color, uint dim)
    {
        var pool = new List<uint>();
        var names = new Dictionary<uint, string>();
        var seen = new HashSet<uint>();
        var dropped = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in build.Variants)
            foreach (var src in v.Affixes)
            {
                var m = AffixMapper.Map(src);
                if (m.Mapped)
                {
                    if (seen.Add(m.CoarseId!.Value))
                    {
                        pool.Add(m.CoarseId.Value);
                        names[m.CoarseId.Value] = m.CoarseName!;
                    }
                }
                else dropped.Add(src.Trim());
            }

        // Build-specific unique targeting (type-8): map each equipped gear unique's name to its
        // filter id via UniqueDatabase. Names with a known id -> purple rule; the rest are
        // reported so we know which to capture next (UniqueDatabase grows from in-game exports).
        var buildUniques = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in build.Variants)
            foreach (var u in v.Uniques) buildUniques.Add(u);

        var uniqueIds = new List<uint>();
        var uniquesTargeted = new List<string>();
        var uniquesPending = new List<string>();
        foreach (var un in buildUniques)
            if (UniqueDatabase.TryGet(un, out var uid)) { uniqueIds.Add(uid); uniquesTargeted.Add(un); }
            else uniquesPending.Add(un);

        return new CompiledBuild(build.Build, color, dim, pool, names,
            dropped.ToList(), uniqueIds, uniquesTargeted, uniquesPending);
    }

    /// <summary>
    /// Assemble the filter and produce its import code. D4 applies rules TOP-DOWN, first match
    /// wins, so rules are emitted MOST-SPECIFIC first and a scoped hide-all sits last.
    /// <paramref name="opts"/> selects which rules to include (the user's toggles); the gold
    /// build-affix tier is always on. Pass one <see cref="CompiledBuild"/> today; the list is
    /// structured for many (each its own color), up to the 25-rule cap.
    /// </summary>
    public static FilterOutput Compile(IReadOnlyList<CompiledBuild> builds, FilterOptions opts,
        string label, string filterName = "D4BuildFilter")
    {
        const uint RareLeg = Rarity.Rare | Rarity.Legendary;

        byte[][] Tier(params byte[][] conds) =>
            opts.StrictEndgame ? conds.Append(Conditions.Ancestral()).ToArray() : conds;

        var rules = new List<byte[]>();
        // 1. The build's OWN uniques -> purple (per-unique type-8). Dormant until we have ids.
        if (opts.BuildUniques)
            foreach (var b in builds)
                if (b.UniqueIds.Count > 0)
                    rules.Add(FilterBuilder.MakeRule($"{b.Name} build uniques", Visibility.Recolor,
                        new[] { Conditions.RarityMask(Rarity.Unique), Conditions.Uniques(b.UniqueIds) }, FilterColors.Purple));
        // 2. Rare/leg with >=3 build affixes -> gold (keepers). Always on — the core of the filter.
        foreach (var b in builds)
            rules.Add(FilterBuilder.MakeRule($"{b.Name} rare/leg [{Strict}+]", Visibility.Recolor,
                Tier(Conditions.RarityMask(RareLeg), Conditions.Affixes(b.Pool, Strict)), b.Color));
        // 3. Rare/leg with >=2 build affixes -> silver (one reroll from great).
        if (opts.SilverTier)
            foreach (var b in builds)
                rules.Add(FilterBuilder.MakeRule($"{b.Name} rare/leg [{Loose}+]", Visibility.Recolor,
                    Tier(Conditions.RarityMask(RareLeg), Conditions.Affixes(b.Pool, Loose)), b.Dim));
        // 4. Item-power tiers (the numeric "Item Power Range" condition: type 0, field4=min, field5=max).
        //    Top band -> orange, high band -> cyan. "Affixes conquer all": these sit BELOW the build
        //    tiers so a real build match wins. Orange precedes cyan (first match wins).
        if (opts.ItemPowerTiers)
        {
            rules.Add(FilterBuilder.MakeRule($"Item Power {ItemPowerOrange}+", Visibility.Recolor,
                new[] { Conditions.RarityMask(RareLeg), Conditions.ItemPower(ItemPowerOrange, ItemPowerCap) }, FilterColors.Orange));
            rules.Add(FilterBuilder.MakeRule($"Item Power {ItemPowerCyan}+", Visibility.Recolor,
                new[] { Conditions.RarityMask(RareLeg), Conditions.ItemPower(ItemPowerCyan, ItemPowerCap) }, FilterColors.Cyan));
        }
        // 5. Greater Affixes -> blue: any rare/leg with >=1 GA not already matched above.
        if (opts.GreaterAffixes)
            rules.Add(FilterBuilder.MakeRule("Greater Affixes", Visibility.Recolor,
                Tier(Conditions.RarityMask(RareLeg), Conditions.GreaterAffix(1)), FilterColors.Blue));
        // 6. Charms & Seals -> green: surface them by item type, any rarity, so the hide rule
        //    doesn't eat them. Not tier-gated.
        if (opts.CharmsSeals)
            rules.Add(FilterBuilder.MakeRule("Charms & Seals", Visibility.Recolor,
                new[] { Conditions.Types(new[] { ItemTypes.Charm, ItemTypes.Seal }) }, FilterColors.Green));
        // 7. Any remaining Codex-of-Power upgrade -> white (pick up for the aspect, then salvage).
        if (opts.Codex)
            rules.Add(FilterBuilder.MakeRule("Codex Upgrades", Visibility.Recolor,
                new[] { Conditions.Codex() }, FilterColors.White));
        // 8. Hide the clutter: Common / Magic / Rare / Legendary that nothing above matched.
        //    NOT Unique (build ones purple above; rest fall through to default) and NOT Mythic —
        //    mythics drop untouched with their natural beam + "tink".
        if (opts.HideRest)
            rules.Add(FilterBuilder.MakeRule("Hide the rest", Visibility.HideAll,
                new[] { Conditions.RarityMask(Rarity.Common | Rarity.Magic | Rarity.Rare | Rarity.Legendary) }));

        var filterBytes = FilterBuilder.MakeFilter(filterName, rules);
        var code = FilterBuilder.ToImportCode(filterBytes);
        var ruleCount = ProtobufReader.Read(filterBytes).Count(f => f is { Field: 1, WireType: 2 });
        return new FilterOutput(label, code, rules.Count, filterBytes.Length, ruleCount == rules.Count);
    }
}
