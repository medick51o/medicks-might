namespace D4BuildFilter.Core;

/// <summary>One gear slot's filter pool: the item-type id(s) to scope to + the build's desired
/// affix ids for that slot. Used by the per-slot rule mode to avoid combined-pool false positives
/// (e.g. boots that happen to roll chest affixes). <paramref name="Label"/> is for display.</summary>
public sealed record SlotPool(string Label, IReadOnlyList<uint> ItemTypeIds, IReadOnlyList<uint> AffixIds);

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
    IReadOnlyList<string> UniquesPending,
    IReadOnlyList<string> Mythics,
    IReadOnlyList<SlotPool> SlotPools)
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
    /// <summary>GOLD tier: rare/legendary carrying ≥3 of the slot's (or, in combined mode, the
    /// build's) affixes → gold "best items". In <see cref="PerSlotRules"/> mode this is one gold
    /// rule per gear slot.</summary>
    public bool GoldTier { get; init; } = true;
    /// <summary>SILVER tier: rare/legendary carrying ≥2 affixes → silver "one roll away". In
    /// <see cref="PerSlotRules"/> mode it's PER-SLOT precise (a silver rule per gear piece, scoped to
    /// that item type) — sits just below the gold tier so 3+ items still go gold (first match wins).
    /// Skipped for a slot that has fewer than 3 ideal affixes (it would duplicate the gold rule).</summary>
    public bool SilverTier { get; init; } = true;
    /// <summary>PRECISE per-slot rules: emit the gold/silver tiers as one rule per gear slot
    /// (ItemType AND that slot's affixes) instead of a single combined pool. Removes cross-slot
    /// false positives (e.g. boots that rolled chest affixes). Falls back to the combined tiers when
    /// the build has no slot data (e.g. pasted builds). Uses more rules — with BOTH tiers on, a big
    /// build (many slots) can approach the 25-rule cap; <see cref="FilterOutput.RuleCount"/> tells.</summary>
    public bool PerSlotRules { get; init; }
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
        var mythics = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var un in buildUniques)
            if (UniqueDatabase.IsMythic(un)) mythics.Add(un);   // own category, left untouched (no rule)
            else if (UniqueDatabase.TryGet(un, out var uid)) { uniqueIds.Add(uid); uniquesTargeted.Add(un); }
            else uniquesPending.Add(un);

        // Per-slot pools (for the precise per-slot rule mode). Group each variant's slots by the
        // item-type id SET they resolve to (so Ring 1 + Ring 2 merge), unioning the build's desired
        // affix ids per group. Weapon slots merge BY HANDEDNESS into "1H Weapons" / "2H Weapons" (a
        // build wants the same stats across its 1-handers and across its 2-handers, and the Barb
        // arsenal's 4 weapon slots as 4 rules × gold+silver would blow the 25-rule cap); ambiguous
        // weapon labels pool generically as "Weapons", and off-hands stay separate. Slots whose label
        // can't resolve are skipped (their affixes still live in the combined pool above). Each group
        // accumulates its type ids so a merged weapon rule scopes to exactly the types the build uses.
        var groups = new Dictionary<string, (string Label, HashSet<uint> TypeSeen, List<uint> TypeIds, HashSet<uint> AffixSeen, List<uint> AffixIds)>();
        foreach (var v in build.Variants)
            if (v.Slots is { } vslots)
                foreach (var rs in vslots)
                {
                    var typeIds = ItemTypeDatabase.ResolveSlot(rs.Slot);
                    if (typeIds is null || typeIds.Count == 0) continue;
                    string key, label;
                    if (ItemTypeDatabase.IsWeaponSlot(typeIds))
                        (key, label) = ItemTypeDatabase.WeaponHandedness(typeIds) switch
                        {
                            "2h" => ("__weapons_2h__", "2H Weapons"),
                            "1h" => ("__weapons_1h__", "1H Weapons"),
                            _    => ("__weapons__",    "Weapons"),
                        };
                    else { key = string.Join(",", typeIds); label = rs.Slot; }
                    if (!groups.TryGetValue(key, out var g))
                    {
                        g = (label, new HashSet<uint>(), new List<uint>(), new HashSet<uint>(), new List<uint>());
                        groups[key] = g;
                    }
                    foreach (var t in typeIds) if (g.TypeSeen.Add(t)) g.TypeIds.Add(t);
                    foreach (var src in rs.Affixes)
                    {
                        var m = AffixMapper.Map(src);
                        if (m.Mapped && g.AffixSeen.Add(m.CoarseId!.Value)) g.AffixIds.Add(m.CoarseId.Value);
                    }
                }
        var slotPools = groups.Values
            .Where(g => g.AffixIds.Count > 0)
            .Select(g => new SlotPool(g.Label, g.TypeIds, g.AffixIds))
            .ToList();

        return new CompiledBuild(build.Build, color, dim, pool, names,
            dropped.ToList(), uniqueIds, uniquesTargeted, uniquesPending, mythics.ToList(), slotPools);
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
                    rules.Add(FilterBuilder.MakeRule("Build Uniques", Visibility.Recolor,
                        new[] { Conditions.RarityMask(Rarity.Unique), Conditions.Uniques(b.UniqueIds) }, FilterColors.Purple));
        // 2/3. Build-affix tiers (the core): GOLD (>=3 affixes) and SILVER (>=2 affixes). Gold is
        //    emitted first so a 3+ item wins gold over the silver rule (D4 = first match wins).
        //  • PER-SLOT mode: each tier becomes one rule PER gear slot = ItemType(slot) AND that slot's
        //    affixes. Precise — a boots rule only matches boots, so chest/ring affixes that rolled on
        //    boots no longer trigger a false "keep".
        //  • COMBINED mode (fallback when a build has no slot data): one pool across all slots.
        //    Simpler but matches by affix COUNT regardless of which slot they belong on (a Blizzard
        //    loot-filter limitation) → some cross-slot false positives.
        // The silver rule is skipped when it would duplicate the gold rule (a pool/slot with <3
        // ideal affixes makes both thresholds collapse to the same count).
        foreach (var b in builds)
        {
            void Emit(string label, IReadOnlyList<uint> affixes, IReadOnlyList<uint>? typeIds)
            {
                byte[][] Scope(int min) => typeIds is null
                    ? Tier(Conditions.RarityMask(RareLeg), Conditions.Affixes(affixes, min))
                    : Tier(Conditions.Types(typeIds), Conditions.RarityMask(RareLeg), Conditions.Affixes(affixes, min));
                int gold = Math.Min(Strict, affixes.Count);
                int silver = Math.Min(Loose, affixes.Count);
                if (opts.GoldTier)
                    rules.Add(FilterBuilder.MakeRule($"{label} [{gold}+]", Visibility.Recolor, Scope(gold), b.Color));
                // Silver only if requested AND it's not an exact duplicate of the gold rule just emitted.
                if (opts.SilverTier && !(opts.GoldTier && silver >= gold))
                    rules.Add(FilterBuilder.MakeRule($"{label} [{silver}+]", Visibility.Recolor, Scope(silver), b.Dim));
            }

            if (opts.PerSlotRules && b.SlotPools.Count > 0)
                foreach (var sp in b.SlotPools) Emit(sp.Label, sp.AffixIds, sp.ItemTypeIds);
            else
                Emit("Rare/Leg", b.Pool, null);
        }
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
