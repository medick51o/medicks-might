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
    /// <paramref name="strictEndgame"/> gates the build-affix + greater-affix rules on Ancestral
    /// tier so only top item-power gear highlights (great for T6+ farming, hides everything while
    /// leveling). Pass one <see cref="CompiledBuild"/> today; the list is structured for many
    /// (each its own color), up to the 25-rule cap.
    /// </summary>
    public static FilterOutput Compile(IReadOnlyList<CompiledBuild> builds, bool strictEndgame,
        string label, string filterName = "D4BuildFilter")
    {
        const uint RareLeg = Rarity.Rare | Rarity.Legendary;

        byte[][] Tier(params byte[][] conds) =>
            strictEndgame ? conds.Append(Conditions.Ancestral()).ToArray() : conds;

        var rules = new List<byte[]>();
        // 1. The build's OWN uniques -> purple (per-unique type-8). Dormant until we have ids.
        foreach (var b in builds)
            if (b.UniqueIds.Count > 0)
                rules.Add(FilterBuilder.MakeRule($"{b.Name} build uniques", Visibility.Recolor,
                    new[] { Conditions.RarityMask(Rarity.Unique), Conditions.Uniques(b.UniqueIds) }, FilterColors.Purple));
        // 2. Rare/leg with >=3 build affixes -> gold (keepers).
        foreach (var b in builds)
            rules.Add(FilterBuilder.MakeRule($"{b.Name} rare/leg [{Strict}+]", Visibility.Recolor,
                Tier(Conditions.RarityMask(RareLeg), Conditions.Affixes(b.Pool, Strict)), b.Color));
        // 3. Rare/leg with >=2 build affixes -> silver (one reroll from great).
        foreach (var b in builds)
            rules.Add(FilterBuilder.MakeRule($"{b.Name} rare/leg [{Loose}+]", Visibility.Recolor,
                Tier(Conditions.RarityMask(RareLeg), Conditions.Affixes(b.Pool, Loose)), b.Dim));
        // 4. Item-power tier -> orange. INTERIM: "Ancestral" (type 2 = 4) is the only item-power
        //    signal we can encode today — it's exactly what rootsxo uses. Stands in for the future
        //    900 / 850 split, which needs D4's numeric "Item Power Range" condition (not yet captured).
        rules.Add(FilterBuilder.MakeRule("Ancestral (high item power)", Visibility.Recolor,
            new[] { Conditions.RarityMask(RareLeg), Conditions.Ancestral() }, FilterColors.Orange));
        // 5. Greater Affixes -> blue: any rare/leg with >=1 GA not already matched above.
        rules.Add(FilterBuilder.MakeRule("Greater Affixes", Visibility.Recolor,
            Tier(Conditions.RarityMask(RareLeg), Conditions.GreaterAffix(1)), FilterColors.Blue));
        // 6. Charms & Seals -> green (rootsxo idea): surface them by item type, any rarity,
        //    so the hide rule doesn't eat them. Not tier-gated.
        rules.Add(FilterBuilder.MakeRule("Charms & Seals", Visibility.Recolor,
            new[] { Conditions.Types(new[] { ItemTypes.Charm, ItemTypes.Seal }) }, FilterColors.Green));
        // 7. Any remaining Codex-of-Power upgrade -> white (pick up for the aspect, then salvage).
        rules.Add(FilterBuilder.MakeRule("Codex Upgrades", Visibility.Recolor,
            new[] { Conditions.Codex() }, FilterColors.White));
        // 8. Hide the clutter: Common / Magic / Rare / Legendary that nothing above matched.
        //    NOT Unique (build ones purple above; rest fall through to default) and NOT Mythic —
        //    mythics drop untouched with their natural beam + "tink".
        rules.Add(FilterBuilder.MakeRule("Hide the rest", Visibility.HideAll,
            new[] { Conditions.RarityMask(Rarity.Common | Rarity.Magic | Rarity.Rare | Rarity.Legendary) }));

        var filterBytes = FilterBuilder.MakeFilter(filterName, rules);
        var code = FilterBuilder.ToImportCode(filterBytes);
        var ruleCount = ProtobufReader.Read(filterBytes).Count(f => f is { Field: 1, WireType: 2 });
        return new FilterOutput(label, code, rules.Count, filterBytes.Length, ruleCount == rules.Count);
    }
}
