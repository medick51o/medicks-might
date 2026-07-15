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
    string Class,
    uint Color,
    uint Dim,
    IReadOnlyList<uint> Pool,
    IReadOnlyDictionary<uint, string> Names,
    IReadOnlyList<string> Dropped,
    IReadOnlyList<uint> UniqueIds,
    IReadOnlyList<string> UniquesTargeted,
    IReadOnlyList<string> UniquesPending,
    IReadOnlyList<SlotPool> SlotPools,
    IReadOnlyList<string> UnmappedSlotPools,
    IReadOnlyList<string> TalismanSets)
{
    /// <summary>Optional source provenance used only for honest within-filter tag collisions.</summary>
    public string Source { get; init; } = "";

    /// <summary>Source slots whose labels could not be resolved to an item type. Any one of these
    /// makes per-slot precision unsafe, because a healthy slot must not suppress combined coverage.</summary>
    public IReadOnlyList<string> UnresolvedSlotPools { get; init; } = [];

    /// <summary>Source charm/set names that a fetcher saw but the local catalog could not resolve.
    /// Compilation uses this signal to discard set scoping and show all charm/seal types.</summary>
    public IReadOnlyList<string> UnknownTalismanSets { get; init; } = [];

    /// <summary>The pool's coarse-affix display names, in pool order.</summary>
    public IEnumerable<string> PoolNames => Pool.Select(id => Names[id]);

    /// <summary>Optional Super Build tier choices. Null preserves the ruled count/priority default.</summary>
    public uint? ChaseColorOverride { get; init; }
    public uint? KeeperColorOverride { get; init; }

    /// <summary>Collision advice is only generated for colors the player explicitly chose.</summary>
    public bool ChaseColorUserChosen { get; init; }
    public bool KeeperColorUserChosen { get; init; }
}

/// <summary>One compiled filter: the base64 import code plus its safety diagnostics and self-check.</summary>
public sealed record FilterOutput(string Label, string ImportCode, int RuleCount, int Bytes,
    bool RoundTripOk, bool IsCopyable, IReadOnlyList<string> Diagnostics)
{
    /// <summary>Non-blocking compile notes that must not affect copy/share safety.</summary>
    public IReadOnlyList<string> Advisories { get; init; } = [];

    /// <summary>True when an emitted Super Build tier differs from its ruled count/priority default.</summary>
    public bool HasCustomBuildColors { get; init; }
}

/// <summary>The deterministic 25-rule auto-fit decision, kept separate from diagnostics because a
/// safe auto-fit is still copy/share-safe. The UI uses this to say exactly what changed; an output
/// that cannot fit is withheld and names the lowest-priority build as the deterministic one to drop.</summary>
public sealed record CompileFitReport(
    int RequestedRuleCount,
    int FinalRuleCount,
    IReadOnlyList<string> DisabledFeatures,
    bool Fits,
    string? BuildToDrop)
{
    public bool WasAdjusted => DisabledFeatures.Count > 0;

    public string Describe(int buildCount, int maxRules)
    {
        var subject = buildCount == 1 ? "1 build needs" : $"{buildCount} builds needs";
        var disabled = string.Join(" and ", DisabledFeatures);
        if (Fits && WasAdjusted)
            return $"{subject} {RequestedRuleCount} rules; the {maxRules}-rule cap forced {disabled} off. "
                + "Deselect a build to get them back.";
        if (!Fits)
            return $"{subject} {RequestedRuleCount} rules; even with "
                + (DisabledFeatures.Count == 0 ? "all available precision" : $"{disabled} off")
                + $", it still needs {FinalRuleCount}. Deselect build '{BuildToDrop}' and compile again.";
        return "";
    }
}

/// <summary>The streamlined customization surface — which rules the filter includes.
/// Defaults reproduce the full recommended filter; the WPF app binds toggles to these.</summary>
public sealed record FilterOptions
{
    /// <summary>Affix bar per tier (v1.0.2 strict v3). Red default 3+ — and stays 3+ even under
    /// strict, because the Occultist enchant makes a 3-of-4 legendary one reroll from perfect.
    /// v1.0.2: strict IS the standard now — Red = 3+ legendaries, Pink = 3+ rares (the rarity split
    /// lives in the RedRares/PinkLegendaries defaults below). The looser 2+ tier moved to the opt-in
    /// <see cref="Leveling"/> silver rule; there is no separate "strict" mode toggle anymore.</summary>
    public int RedMinAffixes { get; init; } = 3;
    public int PinkMinAffixes { get; init; } = 3;
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
    /// <summary>Ancestral charms &amp; seals → red. Emitted ABOVE the green rule so ancestral
    /// versions win priority — same Types(Charm, Seal) condition plus an Ancestral gate. Default
    /// on so the rare ancestrals don't drown in the green noise; turn off if you don't want red.</summary>
    public bool CharmsSealsAncestral { get; init; } = true;
    /// <summary>Codex-of-Power upgrades → white.</summary>
    public bool Codex { get; init; } = true;
    /// <summary>v1.0.2 (Medick): show EVERY unique charm in its natural color via a Show rule right
    /// above the hide. Unique charms are keepers; this rescues them before the talisman-aware hide
    /// sweeps them. Seals aren't charms, so they still fall to the hide. One rule (id-list, no cost
    /// per extra charm). Default on.</summary>
    public bool ShowUniqueCharms { get; init; } = true;
    /// <summary>v1.0.2 (Medick's per-charm list): the specific unique-charm ids to SHOW. Null (or a
    /// full set) = show every unique charm by type — one tiny rule that also auto-covers charms a
    /// future patch adds before we catalog them. A subset = the app's checkbox panel with some
    /// unchecked → show exactly the checked ids. Empty = all unchecked (no show rule; unique charms
    /// fall to the hide). Only consulted when <see cref="ShowUniqueCharms"/> is on.</summary>
    public IReadOnlyList<uint>? UniqueCharms { get; init; } = null;
    /// <summary>v1.0.2 (Medick): the unique ITEM ids to HIDE — the boxes the player UNCHECKED in the
    /// "Uniques" list. Regular uniques SHOW by default (the hide rule spares Unique), so this is a
    /// hide-list — the mirror image of the charm show-list. Null/empty = hide none. The emitted rule
    /// is gated on Unique rarity, so a MYTHIC version of any unique is never hidden.</summary>
    public IReadOnlyList<uint>? HideUniques { get; init; } = null;
    /// <summary>Hide everything else (Common/Magic/Rare/Legendary the rules above didn't match).
    /// Never touches Unique or Mythic.</summary>
    public bool HideRest { get; init; } = true;
    /// <summary>v1.0.1 talisman-set scoping (the per-class checkbox list). Null = legacy catch-all
    /// Charms &amp; Seals rules (paste builds / older callers); empty = user unchecked everything
    /// (both charm rules omitted, charms fall to the hide rule); non-empty = both rules scope to
    /// exactly these sets, in the condition shape the 3.1 in-game editor itself exports.</summary>
    public IReadOnlyList<TalismanSet>? TalismanSets { get; init; } = null;
    /// <summary>Per-tier rarity masks. Defaults ARE the standard strict split (v1.0.2): Red =
    /// legendaries only, Pink = rares only, both at 3+ affixes. These change the rarity BITMASK
    /// inside the existing red/pink rules — never the rule count, so the 25-cap is untouched. Both
    /// rarities off = that tier is omitted entirely. AncestralOnly ANDs an Ancestral gate onto just
    /// its own tier.</summary>
    public bool RedRares { get; init; } = false;
    public bool RedLegendaries { get; init; } = true;
    public bool RedAncestralOnly { get; init; } = false;
    public bool PinkRares { get; init; } = true;
    public bool PinkLegendaries { get; init; } = false;
    public bool PinkAncestralOnly { get; init; } = false;
    /// <summary>v1.0.2 (Medick): LEVELING mode. Off by default. On = adds a SILVER tier for 2+ affix
    /// RARES (gearing-up loot) AND forces combined (non-per-slot) tiers so the extra tier fits the
    /// 25-rule cap. Coarse by design — best with a leveling build loaded; endgame stays precise
    /// per-slot with just the 3+ Red/Pink tiers.</summary>
    public bool Leveling { get; init; } = false;
    /// <summary>v1.0.2 (Horadric research + the founder's field report): a MAGIC item with BOTH
    /// affixes on-build is a premium Add-Affix crafting base — players keep them, and the hide
    /// rule was eating them. Shows them gold (the legacy tier color, reborn for crafting).
    /// OPT-IN by founder call: looting blues is an enthusiast move, so the default stays quiet
    /// and the Crafting Coach teaches players to flip it on when they're ready to craft.</summary>
    public bool CubeBases { get; init; } = false;
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

    /// <summary>Plain-words narration of a tier's scope for the UI — "what will this tier actually
    /// highlight?" answered on screen, live, instead of in a tooltip. Includes the affix THRESHOLD,
    /// so the strict button's bar-raise (Red 3+→4+, Pink 2→3) is visible the moment it's clicked.</summary>
    public static string DescribeTierScope(bool tierOn, int minAffixes, bool rares,
        bool legendaries, bool ancestralOnly)
    {
        if (!tierOn || (!rares && !legendaries)) return "off — highlights nothing";
        var what = rares && legendaries ? "rares + legendaries"
            : rares ? "rares only" : "legendaries only";
        var anc = ancestralOnly ? ", ancestral tier only" : "";
        return $"highlights {minAffixes}+ affix {what}{anc}";
    }

    /// <summary>Item-power color tiers (orange = top band, cyan = high band). The numeric
    /// "Item Power Range" condition takes [min,max]; we use an open-ended upper bound so each
    /// tier means "this power and up". Tune the thresholds after an in-game check.</summary>
    public const uint ItemPowerOrange = 900;
    public const uint ItemPowerCyan = 850;
    public const uint ItemPowerCap = 4000;
    /// <summary>Upper bound for full-range (min-1) rules — the charm rules and the "Hide the rest"
    /// catch-all. Reads "1 to 900" in the in-game editor (Medick's preference; a bare 0 min "acts
    /// strange"). 900 is the practical item-power ceiling; the open-ended tier rules keep
    /// <see cref="ItemPowerCap"/> since they mean "this power and up".</summary>
    public const uint ItemPowerMax = 900;

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
        // Maxroll charm/seal stats become gear wants only when that same stat appears on gear in
        // another selected profile. Do this here, after variant selection, so unchecked profiles
        // cannot widen the compiled slot pools.
        var gearAffixes = build.Variants.SelectMany(v => v.Affixes)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dualSourceAffixes = build.Variants.SelectMany(v => v.SlotlessAffixes ?? [])
            .Where(gearAffixes.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
                // Engine-unfilterable affixes are intentionally NOT reported as dropped — D4's
                // filter has no ID for them, so there's nothing the user can do, nothing for us
                // to fix in the DB, and nothing they should chase as a coverage gap.
                else if (m.Strategy != MapStrategy.Unfilterable) dropped.Add(src.Trim());
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
        // S14 Mythic 3.0: mythic is a quality any unique can have, not a fixed list — so every build
        // unique is treated uniformly. Known id -> purple per-unique targeting; unknown -> pending
        // (surfaced, never silently dropped). A unique that drops as / upgrades to mythic is still
        // never hidden: the "Hide the rest" rule spares the Unique + Mythic rarities.
        foreach (var un in buildUniques)
            if (UniqueDatabase.TryGet(un, out var uid)) { uniqueIds.Add(uid); uniquesTargeted.Add(un); }
            else uniquesPending.Add(un);

        // The build's talisman SETS (S14 display names), aggregated across the kept variants —
        // drives which per-class set checkboxes come pre-checked in the UI.
        var talismanSets = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var unknownTalismanSets = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in build.Variants)
        {
            if (v.TalismanSets is { } ts)
                foreach (var t in ts) talismanSets.Add(t);
            if (v.UnknownTalismanSets is { } unknown)
                foreach (var t in unknown) unknownTalismanSets.Add(t);
        }

        // Per-slot pools (for the precise per-slot rule mode). Group each variant's slots by the
        // item-type id SET they resolve to (so Ring 1 + Ring 2 merge), unioning the build's desired
        // affix ids per group. Weapon slots merge BY HANDEDNESS into "1H Weapons" / "2H Weapons" (a
        // build wants the same stats across its 1-handers and across its 2-handers, and the Barb
        // arsenal's 4 weapon slots as 4 rules × gold+silver would blow the 25-rule cap); ambiguous
        // weapon labels pool generically as "Weapons", and off-hands stay separate. Slots whose label
        // can't resolve are recorded so compilation must use the combined pool instead. Each group
        // accumulates its type ids so a merged weapon rule scopes to exactly the types the build uses.
        var groups = new Dictionary<string, (string Label, HashSet<uint> TypeSeen, List<uint> TypeIds, HashSet<uint> AffixSeen, List<uint> AffixIds)>();
        var unresolvedSlotPools = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in build.Variants)
            if (v.Slots is { } vslots)
                foreach (var rs in vslots)
                {
                    var typeIds = ItemTypeDatabase.ResolveSlot(rs.Slot);
                    if (typeIds is null || typeIds.Count == 0)
                    {
                        unresolvedSlotPools.Add(rs.Slot);
                        continue;
                    }
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
                    foreach (var src in rs.Affixes.Concat(dualSourceAffixes)
                                 .Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        var m = AffixMapper.Map(src);
                        if (m.Mapped && g.AffixSeen.Add(m.CoarseId!.Value)) g.AffixIds.Add(m.CoarseId.Value);
                    }
                }
        var slotPools = groups.Values
            .Where(g => g.AffixIds.Count > 0)
            .Select(g => new SlotPool(g.Label, g.TypeIds, g.AffixIds))
            .ToList();
        // Keep the resolved slot groups that mapped to nothing alongside the live pools. Compile's
        // per-slot path needs their names to warn that no rule protects that gear before HideRest.
        var unmappedSlotPools = groups.Values
            .Where(g => g.AffixIds.Count == 0)
            .Select(g => g.Label)
            .ToList();

        return new CompiledBuild(build.Build, build.Class, color, dim, pool, names,
            dropped.ToList(), uniqueIds, uniquesTargeted, uniquesPending, slotPools,
            unmappedSlotPools, talismanSets.ToList())
        {
            Source = build.Source,
            UnresolvedSlotPools = unresolvedSlotPools.ToList(),
            UnknownTalismanSets = unknownTalismanSets.ToList(),
        };
    }

    /// <summary>
    /// Assemble the filter and produce its import code. D4 applies rules TOP-DOWN, first match
    /// wins, so rules are emitted MOST-SPECIFIC first and a scoped hide-all sits last.
    /// <paramref name="opts"/> selects which rules to include (the user's toggles); the gold
    /// build-affix tier is always on. Multiple builds keep their own colors; when two builds have
    /// an identical tier scope, the first build owns that shared rule so the rule budget is not
    /// charged twice for loot that would already have matched first.
    /// </summary>
    public static FilterOutput Compile(IReadOnlyList<CompiledBuild> builds, FilterOptions opts,
        string label, string filterName = "D4BuildFilter") =>
        CompileCore(builds, opts, label, filterName, null);

    internal static FilterOutput Compile(IReadOnlyList<CompiledBuild> builds, FilterOptions opts,
        string label, string filterName, UnregisteredColorBehavior unregisteredColorBehavior) =>
        CompileCore(builds, opts, label, filterName, unregisteredColorBehavior);

    private static FilterOutput CompileCore(IReadOnlyList<CompiledBuild> builds, FilterOptions opts,
        string label, string filterName, UnregisteredColorBehavior? unregisteredColorBehavior)
    {
        const uint RareLeg = Rarity.Rare | Rarity.Legendary;
        // v1.0.2 per-tier rarity masks (defaults = the classic Rare|Legendary blob).
        uint redMask = (opts.RedRares ? Rarity.Rare : 0) | (opts.RedLegendaries ? Rarity.Legendary : 0);
        uint pinkMask = (opts.PinkRares ? Rarity.Rare : 0) | (opts.PinkLegendaries ? Rarity.Legendary : 0);

        var rules = new List<byte[]>();
        var diagnostics = new List<string>();
        var advisories = new List<string>();
        bool multiBuild = builds.Count > 1;
        var multiTags = multiBuild ? BuildTagger.Resolve(builds) : [];
        var chaseColors = multiBuild
            ? builds.Select((build, index) => build.ChaseColorOverride ?? RuledChaseColor(builds.Count, index)).ToArray()
            : [];
        var keeperColors = multiBuild
            ? builds.Select((build, index) => build.KeeperColorOverride ?? RuledKeeperColor(builds.Count, index)).ToArray()
            : [];
        bool hasCustomBuildColors = multiBuild && builds.Select((build, index) =>
                chaseColors[index] != RuledChaseColor(builds.Count, index)
                || keeperColors[index] != RuledKeeperColor(builds.Count, index))
            .Any(custom => custom);
        // Every recolor rule is named "<what> (<Color>)" so a player can scroll the in-game filter
        // list and toggle by color (e.g. turn "Charms & Seals (Green)" off). D4 caps rule names at
        // 24 chars (FilterBuilder clamps); the names below stay short enough that the color survives.
        byte[] Recolor(string name, byte[][] conds, uint color)
        {
            var colorName = unregisteredColorBehavior is { } behavior
                ? FilterColors.NameOf(color, advisories, behavior)
                : FilterColors.NameOf(color, advisories);
            return FilterBuilder.MakeRule($"{name} ({colorName})", Visibility.Recolor, conds, color);
        }

        // 1. Every loaded build's OWN uniques -> one purple rule. The union keeps both loadouts'
        //    chase items purple without spending one rule per build (shared uniques are deduped).
        var buildUniqueIds = builds.SelectMany(b => b.UniqueIds).Distinct().OrderBy(id => id).ToList();
        if (opts.BuildUniques && buildUniqueIds.Count > 0)
            rules.Add(Recolor("Build Uniques",
                new[] { Conditions.RarityMask(Rarity.Unique), Conditions.Uniques(buildUniqueIds) }, FilterColors.Purple));
        // 1b. Codex-of-Power upgrades -> white, HIGH priority (Medick's methodology, v1.0.2): a codex
        //     upgrade is a permanent, account-wide aspect unlock — worth more than any single 3+ item
        //     or GA drop. Emitted ABOVE the affix / GA / item-power tiers (first-match wins) so a
        //     legendary that is BOTH a 3+ item AND a codex upgrade reads as White (codex), not Red —
        //     the player never grabs a "red" and belatedly notices it was the unlock.
        if (opts.Codex)
            rules.Add(Recolor("Codex Upgrades", new[] { Conditions.Codex() }, FilterColors.White));
        // 1c. Hide Uniques (Medick's list): the specific uniques the player UNCHECKED in the app's
        //     "Uniques" list. Uniques show by default, so this is a hide-list (mirror of the charm
        //     show-list). Gated on Unique rarity so a MYTHIC version of any unique is NEVER hidden;
        //     sits below Build Uniques so an unchecked BUILD unique still shows purple.
        if (opts.HideUniques is { Count: > 0 } hideUniques)
            rules.Add(FilterBuilder.MakeRule("Hide Uniques", Visibility.HideAll,
                new[] { Conditions.RarityMask(Rarity.Unique), Conditions.Uniques(hideUniques) }));
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
        if (multiBuild)
        {
            // Super Builds have a deliberately fixed endgame shape: one combined 3+ legendary
            // rule and one combined 3+ rare rule per build. Two builds use colors by build; three
            // or four use colors by tier. Names carry identity in both schemes.
            for (int i = 0; i < builds.Count; i++)
            {
                var b = builds[i];
                foreach (var deadSlot in b.UnmappedSlotPools)
                    diagnostics.Add($"No affixes could be mapped for {deadSlot} in build '{b.Name}' — that slot has no rule; "
                        + "the filter cannot honestly cover every selected build.");
                if (b.Pool.Count == 0)
                {
                    diagnostics.Add($"No affixes could be mapped for build '{b.Name}' — its tiers were skipped; "
                        + "the filter may hide loot this build wants — consider disabling 'Hide the rest'.");
                    continue;
                }

                var legendaryScope = new[] { Conditions.RarityMask(Rarity.Legendary), Conditions.Affixes(b.Pool, Strict) };
                rules.Add(Recolor($"{multiTags[i]} Leg", legendaryScope, chaseColors[i]));
            }
            for (int i = 0; i < builds.Count; i++)
            {
                var b = builds[i];
                if (b.Pool.Count == 0) continue;
                var rareScope = new[] { Conditions.RarityMask(Rarity.Rare), Conditions.Affixes(b.Pool, Strict) };
                rules.Add(Recolor($"{multiTags[i]} Rare", rareScope, keeperColors[i]));
            }
        }
        else
        {
            var emittedTierScopes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in builds)
            {
                // A wholly unmappable slot is uncovered in BOTH modes. Combined mode can still match
                // the build's other affixes, but it cannot invent a chase signal for this slot. Keep the
                // warning outside the per-slot branch so an auto-fit trade-down cannot erase it.
                foreach (var deadSlot in b.UnmappedSlotPools)
                    diagnostics.Add($"No affixes could be mapped for {deadSlot} in build '{b.Name}' — that slot has no rule; "
                        + "the filter cannot honestly cover every selected build.");
                if (b.UnresolvedSlotPools.Count > 0)
                    diagnostics.Add($"Combined rules are being used for build '{b.Name}' because these source slot labels "
                        + $"could not be resolved: {string.Join(", ", b.UnresolvedSlotPools)}.");

                void Emit(string label, IReadOnlyList<uint> affixes, IReadOnlyList<uint>? typeIds)
                {
                // An empty pool would clamp both thresholds to zero and emit a [0+] rule, which
                // matches every rare/legendary. Fail closed: omitting this tier may miss wanted loot,
                // but it can never silently paint all drops as build matches.
                if (affixes.Count == 0)
                {
                    diagnostics.Add($"No affixes could be mapped for {label} in build '{b.Name}' — that tier was skipped; "
                        + "the filter may hide loot this build wants — consider disabling 'Hide the rest'.");
                    return;
                }

                // v1.0.2: per-tier rarity masks + optional per-tier ancestral gate — refinement
                // INSIDE the existing rules (the rule count never grows; the 25-cap stays safe).
                byte[][] Scope(int min, uint mask, bool ancestralOnly)
                {
                    var conds = typeIds is null
                        ? new List<byte[]> { Conditions.RarityMask(mask), Conditions.Affixes(affixes, min) }
                        : new List<byte[]> { Conditions.Types(typeIds), Conditions.RarityMask(mask), Conditions.Affixes(affixes, min) };
                    if (ancestralOnly) conds.Add(Conditions.Ancestral());
                    return conds.ToArray();
                }
                // Per-tier affix bars come straight from the options (strict v3: the UI preset
                // raises Pink to 3; Red stays 3 — enchant logic). [N+] rule names self-document.
                int gold = Math.Min(opts.RedMinAffixes, affixes.Count);
                int silver = Math.Min(opts.PinkMinAffixes, affixes.Count);
                string ScopeKey(int min, uint mask, bool ancestralOnly) => string.Join("|",
                    typeIds is null ? "*" : string.Join(",", typeIds.OrderBy(id => id)),
                    string.Join(",", affixes.Distinct().OrderBy(id => id)), min, mask, ancestralOnly);
                if (opts.GoldTier && redMask != 0
                    && emittedTierScopes.Add(ScopeKey(gold, redMask, opts.RedAncestralOnly)))
                    rules.Add(Recolor($"{label} [{gold}+]", Scope(gold, redMask, opts.RedAncestralOnly), b.Color));
                // Pink only when requested AND not an exact duplicate of the red rule just emitted
                // (same threshold AND same mask AND same ancestral gate).
                bool duplicatesRed = opts.GoldTier && redMask != 0 && silver >= gold
                    && pinkMask == redMask && opts.PinkAncestralOnly == opts.RedAncestralOnly;
                if (opts.SilverTier && pinkMask != 0 && !duplicatesRed
                    && emittedTierScopes.Add(ScopeKey(silver, pinkMask, opts.PinkAncestralOnly)))
                    rules.Add(Recolor($"{label} [{silver}+]", Scope(silver, pinkMask, opts.PinkAncestralOnly), b.Dim));
                }

                // Leveling forces COMBINED tiers (coarse) so the extra silver tier fits the 25-cap;
                // endgame keeps precise per-slot. Either way Red (3+ leg) + Pink (3+ rare) emit here.
                var slotCoveredAffixes = b.SlotPools.SelectMany(sp => sp.AffixIds).ToHashSet();
                bool completeSlotCoverage = b.UnresolvedSlotPools.Count == 0
                    && b.Pool.All(slotCoveredAffixes.Contains);
                if (opts.PerSlotRules && b.SlotPools.Count > 0 && !opts.Leveling && completeSlotCoverage)
                {
                    foreach (var sp in b.SlotPools) Emit(sp.Label, sp.AffixIds, sp.ItemTypeIds);
                }
                else
                {
                    if (opts.PerSlotRules && b.SlotPools.Count > 0 && !opts.Leveling && !completeSlotCoverage)
                        diagnostics.Add($"Per-slot precision was disabled for build '{b.Name}' because its slot breakdown "
                            + "does not cover every wanted affix; combined rules keep that loot visible.");
                    Emit("Rare/Leg", b.Pool, null);
                }
                // v1.0.2 LEVELING silver: 2+ affix RARES for gearing up — one combined rule BELOW the 3+
                // tiers (a 3+ rare hits Pink first). Rares only (Medick's spec), silver color. Skipped
                // when its bar wouldn't sit under Pink's (tiny pools), so it never duplicates a tier.
                if (opts.Leveling && b.Pool.Count > 0
                    && Math.Min(2, b.Pool.Count) < Math.Min(opts.PinkMinAffixes, b.Pool.Count))
                    rules.Add(Recolor("Leveling [2+]",
                        new[] { Conditions.RarityMask(Rarity.Rare), Conditions.Affixes(b.Pool, Math.Min(2, b.Pool.Count)) },
                        FilterColors.Silver));
            }
        }
        // 4. Item-power tiers (the numeric "Item Power Range" condition: type 0, field4=min, field5=max).
        //    Top band -> orange, high band -> cyan. "Affixes conquer all": these sit BELOW the build
        //    tiers so a real build match wins. Orange precedes cyan (first match wins).
        if (opts.ItemPowerTiers)
        {
            rules.Add(Recolor($"Item Power {ItemPowerOrange}+",
                new[] { Conditions.RarityMask(RareLeg), Conditions.ItemPower(ItemPowerOrange, ItemPowerCap) }, FilterColors.Orange));
            rules.Add(Recolor($"Item Power {ItemPowerCyan}+",
                new[] { Conditions.RarityMask(RareLeg), Conditions.ItemPower(ItemPowerCyan, ItemPowerCap) }, FilterColors.Cyan));
        }
        // 5. Greater Affixes -> blue: any rare/leg with >=1 GA not already matched above.
        if (opts.GreaterAffixes)
            rules.Add(Recolor("Greater Affixes",
                new[] { Conditions.RarityMask(RareLeg), Conditions.GreaterAffix(1) }, FilterColors.Blue));
        // 6a/6b. Charms & Seals. v1.0.1: when a talisman-set selection is present (the per-class
        //     checkbox list), BOTH rules scope to exactly the checked sets via the type-9 set
        //     condition (per-item refinement; pinned to Medick's hand-built 3.1 export). v1.0.2
        //     (Medick, in-game): the paired Item Power floor is 1 (not the game's bare min-0, which
        //     he found "acts strange") — every talisman has power >= 1, so scoping is unchanged.
        //     Unchecked sets get no rule at all and fall to "Hide the rest". An unknown-class build
        //     or any source-recorded unknown set MUST fail open: catalog checkboxes cannot represent
        //     that loot, so collapse to the type rule that rescues every charm and seal above the hide.
        //     Null selection keeps that legacy catch-all behavior for older callers. Ancestral red wins.
        bool classUnknown = builds.Any(b => !TalismanSetDatabase.IsKnownClass(b.Class));
        bool unknownSetsDetected = builds.Any(b => b.UnknownTalismanSets.Count > 0);
        var setScope = !classUnknown && !unknownSetsDetected && opts.TalismanSets is { } tsel
            ? tsel.Select(s => (s.Id, (IReadOnlyList<uint>)s.Items.Select(i => i.Id).ToList())).ToList()
            : null;
        if (opts.CharmsSealsAncestral && setScope is not { Count: 0 })
            rules.Add(Recolor("Charms&Seals Anc",
                setScope is null
                    ? new[] { Conditions.Types(new[] { ItemTypes.Charm, ItemTypes.Seal }), Conditions.Ancestral() }
                    : new[] { Conditions.ItemPower(1, ItemPowerMax), Conditions.TalismanSetBonus(setScope), Conditions.Ancestral() },
                FilterColors.Red));
        if (opts.CharmsSeals && setScope is not { Count: 0 })
            rules.Add(Recolor("Charms & Seals",
                setScope is null
                    ? new[] { Conditions.Types(new[] { ItemTypes.Charm, ItemTypes.Seal }) }
                    : new[] { Conditions.ItemPower(1, ItemPowerMax), Conditions.TalismanSetBonus(setScope) },
                FilterColors.Green));
        // 6c. Cube bases (v1.0.2, Horadric research + the founder's persona spec): the bar for a
        //     blue being worth a STOP is both affixes on-build AND >=1 Greater Affix — plain
        //     2-roll blues aren't worth a speedfarmer's time. Gold = the crafting color. Opt-in.
        //     (GA-on-Magic is contested in the datamine; the armed users are the field experiment.)
        if (opts.CubeBases)
            foreach (var b in builds)
                if (b.Pool.Count > 0)
                    rules.Add(Recolor("Cube Bases", new[] {
                        Conditions.RarityMask(Rarity.Magic),
                        Conditions.Affixes(b.Pool, Math.Min(2, b.Pool.Count)),
                        Conditions.GreaterAffix(1) }, FilterColors.Gold));
        // 7. (Codex Upgrades moved UP to step 1b — Medick's methodology: a codex unlock outranks any
        //    3+ / GA drop, so it must win first-match over the affix tiers.)
        // 7b. Show ALL unique charms in their traditional color (Medick, in-game): unique charms are
        //     keepers he always wants to see, so rescue them ABOVE the hide with a Show rule
        //     (Charm-type AND Unique-rarity). Seals are a different item type, so they aren't matched
        //     here and still fall to the hide. IMPORTANT: the Charm type id is the d4data + fnuecke
        //     cross-checked 0x0022ed05 — NOT D4Filter.cs's ItemTypes.Charm, whose Charm/Seal labels
        //     are reversed (harmless where both ids are used together, but wrong for a charm-only rule).
        if (opts.ShowUniqueCharms)
        {
            var sel = opts.UniqueCharms;
            if (sel is null || sel.Count >= UniqueCharmDatabase.All.Count)
                // All (or unspecified) → show EVERY unique charm by type. One tiny rule, and it
                // auto-covers charms a future patch adds before we catalog them.
                rules.Add(FilterBuilder.MakeRule("Unique Charms", Visibility.Show,
                    new[] { Conditions.Types(new[] { ItemTypeDatabase.ByName["Charm"] }),
                            Conditions.RarityMask(Rarity.Unique) }));
            else if (sel.Count > 0)
                // Curated subset (some unchecked in the app's panel) → show exactly the checked ids.
                rules.Add(FilterBuilder.MakeRule("Unique Charms", Visibility.Show,
                    new[] { Conditions.RarityMask(Rarity.Unique), Conditions.Uniques(sel) }));
            // sel empty (every box unchecked) → no show rule; unique charms fall to the hide.
        }
        // 8. Hide the clutter: Common / Magic / Rare / Legendary AND Talismans (charms & seals) that
        //    nothing above matched. v1.0.2 (Medick, in-game): without the Talisman bit, every charm
        //    that wasn't the build's selected set leaked through and kept showing. NOT Unique (build
        //    ones go purple above; the rest fall through to default) and NOT Mythic — mythics drop
        //    untouched with their natural beam + "tink".
        if (opts.HideRest)
            rules.Add(FilterBuilder.MakeRule("Hide the rest", Visibility.HideAll,
                // D4 won't apply a rule that has ONLY a rarity condition — unwanted items leak through.
                // Pair it with Item Power >= 1 (every other rule already carries a concrete 2nd condition)
                // so the engine honors the catch-all and actually hides. All gear + talismans have power >= 1.
                new[] { Conditions.RarityMask(Rarity.Common | Rarity.Magic | Rarity.Rare | Rarity.Legendary | Rarity.Talisman),
                        Conditions.ItemPower(1, ItemPowerMax) }));

        // S5's generalized advisory pass subsumes the old two-build Gold/Cube Bases diagnostic.
        // Cube Bases is itself an explicit user choice, so it warns against even a default Gold
        // keeper. Leveling is absent here because Super Builds deliberately emit only 3+ tiers.
        if (multiBuild)
            AddMultiBuildColorAdvisories(builds, opts, multiTags, chaseColors, keeperColors, advisories);

        var filterBytes = FilterBuilder.MakeFilter(filterName, rules);
        var ruleCount = ProtobufReader.Read(filterBytes).Count(f => f is { Field: 1, WireType: 2 });
        var buildsWithoutChaseRules = builds.Where(b => b.Pool.Count == 0).ToList();
        var buildsWithUnmappedSlots = builds.Where(b => b.UnmappedSlotPools.Count > 0).ToList();
        bool isCopyable = buildsWithoutChaseRules.Count == 0 && buildsWithUnmappedSlots.Count == 0;
        if (!isCopyable)
        {
            var coverageFailures = new List<string>();
            foreach (var b in buildsWithoutChaseRules)
                coverageFailures.Add(opts.HideRest
                    ? $"'Hide the rest' is enabled while the affix pool is empty for build '{b.Name}'; this could hide everything that build wants"
                    : $"build '{b.Name}' has no mapped chase rule");
            foreach (var b in buildsWithUnmappedSlots)
                coverageFailures.Add($"build '{b.Name}' has no mapped chase rule for "
                    + string.Join(", ", b.UnmappedSlotPools.Select(slot => $"slot '{slot}'")));
            diagnostics.Add("No filter code was produced because " + string.Join("; ", coverageFailures)
                + "; the filter cannot honestly cover every selected build.");
        }
        var code = isCopyable ? FilterBuilder.ToImportCode(filterBytes) : "";
        return new FilterOutput(label, code, rules.Count, filterBytes.Length,
            ruleCount == rules.Count, isCopyable, diagnostics)
        {
            Advisories = advisories,
            HasCustomBuildColors = hasCustomBuildColors,
        };
    }

    private static uint RuledChaseColor(int buildCount, int index) =>
        buildCount == 2 && index == 1 ? FilterColors.Pink : FilterColors.Red;

    private static uint RuledKeeperColor(int buildCount, int index) =>
        buildCount == 2 && index == 1 ? FilterColors.Silver : FilterColors.Gold;

    private sealed record ColorMeaning(string Id, string Label, uint Color, int? BuildIndex = null,
        bool IsChase = false, bool UserChosen = false);

    private static void AddMultiBuildColorAdvisories(IReadOnlyList<CompiledBuild> builds, FilterOptions opts,
        IReadOnlyList<string> tags, IReadOnlyList<uint> chaseColors, IReadOnlyList<uint> keeperColors,
        ICollection<string> advisories)
    {
        var meanings = new List<ColorMeaning>();
        for (int i = 0; i < builds.Count; i++)
        {
            if (builds[i].Pool.Count == 0) continue;
            meanings.Add(new($"build:{i}:chase", $"{tags[i]} chase tier", chaseColors[i], i, true,
                builds[i].ChaseColorUserChosen));
            meanings.Add(new($"build:{i}:keeper", $"{tags[i]} keeper tier", keeperColors[i], i, false,
                builds[i].KeeperColorUserChosen));
        }

        bool talismanRulesActive = opts.TalismanSets is not { Count: 0 };
        if (opts.CharmsSealsAncestral && talismanRulesActive)
            meanings.Add(new("charms:ancestral", "ancestral charms", FilterColors.Red));
        if (opts.CharmsSeals && talismanRulesActive)
            meanings.Add(new("charms:regular", "charms and seals", FilterColors.Green));
        if (opts.GreaterAffixes)
            meanings.Add(new("ga", "Greater Affixes", FilterColors.Blue));
        if (opts.ItemPowerTiers)
        {
            meanings.Add(new("ip:orange", $"Item Power {ItemPowerOrange}+", FilterColors.Orange));
            meanings.Add(new("ip:cyan", $"Item Power {ItemPowerCyan}+", FilterColors.Cyan));
        }
        if (opts.BuildUniques && builds.Any(build => build.UniqueIds.Count > 0))
            meanings.Add(new("uniques", "build uniques", FilterColors.Purple));
        if (opts.Codex)
            meanings.Add(new("codex", "Codex upgrades", FilterColors.White));
        if (opts.CubeBases && builds.Any(build => build.Pool.Count > 0))
            meanings.Add(new("cube", "Cube Bases", FilterColors.Gold, UserChosen: true));

        var warnedPairs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chosen in meanings.Where(meaning => meaning.UserChosen))
        {
            foreach (var other in meanings.Where(meaning => meaning.Id != chosen.Id && meaning.Color == chosen.Color))
            {
                var ids = new[] { chosen.Id, other.Id }.OrderBy(id => id, StringComparer.Ordinal).ToArray();
                if (!warnedPairs.Add($"{ids[0]}|{ids[1]}")) continue;
                var colorName = FilterColors.TryGetEntry(chosen.Color, out var entry)
                    ? entry!.FullName
                    : FilterColors.NameOf(chosen.Color, advisories);
                advisories.Add($"{chosen.Label} and {other.Label} now share {colorName} — you won't be able to tell them apart.");
            }
        }

        foreach (var color in meanings.Where(meaning => meaning.UserChosen)
                     .Select(meaning => meaning.Color).Distinct())
        {
            if (FilterColors.TryGetEntry(color, out var entry) && entry!.DarkGroundRisk)
                advisories.Add($"{entry.FullName}: this color is hard to see on Sanctuary's ground — you may miss drops.");
        }
    }

    /// <summary>
    /// Compatibility overload for callers that only need to know whether per-slot precision was
    /// traded down. New UI should consume the <see cref="CompileFitReport"/> overload below.
    /// </summary>
    public static FilterOutput CompileWithinCap(IReadOnlyList<CompiledBuild> builds, FilterOptions opts,
        int maxRules, out bool autoFittedToCombined, string label = "Filter", string filterName = "D4BuildFilter")
    {
        var output = CompileWithinCap(builds, opts, maxRules, out CompileFitReport report, label, filterName);
        autoFittedToCombined = report.Fits
            && report.DisabledFeatures.Contains("per-slot precision", StringComparer.Ordinal);
        return output;
    }

    /// <summary>Fit in one fixed order: (1) combined rules replace per-slot precision, then
    /// (2) the keeper/Pink tier is disabled for single builds only. Super Build tiers are fixed.
    /// Builds are never removed. If that minimum still exceeds the cap, no import code is returned
    /// and the lowest-priority build (the final build in priority order) is named to drop.</summary>
    public static FilterOutput CompileWithinCap(IReadOnlyList<CompiledBuild> builds, FilterOptions opts,
        int maxRules, out CompileFitReport report, string label = "Filter", string filterName = "D4BuildFilter")
    {
        if (builds.Count > 4)
        {
            var excessBuild = builds.Last().Name;
            var tooManyDiagnostic = $"No filter code was produced because Super Builds support at most 4 builds. "
                + $"Deselect build '{excessBuild}' and compile again.";
            report = new(0, 0, [], false, excessBuild);
            return new FilterOutput(label, "", 0, 0, true, false, [tooManyDiagnostic]);
        }

        var requested = Compile(builds, opts, label, filterName);
        if (requested.RuleCount <= maxRules)
        {
            report = new(requested.RuleCount, requested.RuleCount, [], true, null);
            return requested;
        }

        var disabled = new List<string>();
        var workingOptions = opts;
        var working = requested;

        // ORDER IS LOAD-BEARING. Add future trade-downs only after these two steps.
        if (workingOptions.PerSlotRules && !workingOptions.Leveling)
        {
            var candidateOptions = workingOptions with { PerSlotRules = false };
            var candidate = Compile(builds, candidateOptions, label, filterName);
            if (candidate.RuleCount < working.RuleCount)
            {
                disabled.Add("per-slot precision");
                workingOptions = candidateOptions;
                working = candidate;
                if (working.RuleCount <= maxRules)
                {
                    report = new(requested.RuleCount, working.RuleCount, disabled, true, null);
                    return working;
                }
            }
        }

        if (builds.Count == 1 && workingOptions.SilverTier)
        {
            var candidateOptions = workingOptions with { SilverTier = false };
            var candidate = Compile(builds, candidateOptions, label, filterName);
            if (candidate.RuleCount < working.RuleCount)
            {
                disabled.Add("the keeper (Pink) tier");
                workingOptions = candidateOptions;
                working = candidate;
                if (working.RuleCount <= maxRules)
                {
                    report = new(requested.RuleCount, working.RuleCount, disabled, true, null);
                    return working;
                }
            }
        }

        var buildToDrop = builds.LastOrDefault()?.Name ?? "the lowest-priority build";
        var diagnostic = $"No filter code was produced because {builds.Count} builds still need "
            + $"{working.RuleCount} rules after auto-fit; the {maxRules}-rule cap cannot cover every selected build. "
            + $"Deselect build '{buildToDrop}' and compile again.";
        var diagnostics = working.Diagnostics.Concat([diagnostic]).ToList();
        report = new(requested.RuleCount, working.RuleCount, disabled, false, buildToDrop);
        return working with { ImportCode = "", IsCopyable = false, Diagnostics = diagnostics };
    }
}
