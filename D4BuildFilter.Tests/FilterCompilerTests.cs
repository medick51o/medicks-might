using System.Collections.Generic;
using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Tests;

/// <summary>Analyze + Compile: the option toggles must add/remove exactly the right rules, the
/// gold tier is always on, and every emitted filter must round-trip.</summary>
public class FilterCompilerTests
{
    private static CompiledBuild SampleBuild() =>
        FilterCompiler.Analyze(
            new ResolvedBuild("Test Barb", "Barbarian", new[]
            {
                new ResolvedVariant("v1",
                    new[] { "Strength", "Maximum Life", "Critical Strike Chance",
                            "Critical Strike Damage Multiplier", "Vulnerable Damage Multiplier",
                            "Cooldown Reduction", "War Cry" },
                    new[] { "Banished Lord's Talisman" }),   // a captured unique -> purple rule fires
            }),
            FilterColors.Gold, FilterColors.Silver);

    private static int RuleCount(FilterOptions o) =>
        FilterCompiler.Compile(new[] { SampleBuild() }, o, "t").RuleCount;

    [Fact]
    public void Analyze_maps_affixes_and_unique()
    {
        var b = SampleBuild();
        Assert.Contains(0x001beaceu, b.Pool);                 // Critical Strike Chance
        Assert.Single(b.UniqueIds);                            // Banished Lord's resolved
        Assert.Contains("Banished Lord's Talisman", b.UniquesTargeted);
    }

    [Fact]
    public void Former_uber_uniques_are_targeted_like_any_build_unique()
    {
        // S14 Mythic 3.0: "mythic" is a quality any unique can have / be upgraded to — not a fixed
        // list. So a build's classic ubers are no longer carved into their own category; they get
        // normal per-unique purple targeting like every other build unique.
        var rb = new ResolvedBuild("t", "Barbarian", new[]
        {
            new ResolvedVariant("v", new[] { "Strength" },
                new[] { "Banished Lord's Talisman", "Tyrael's Might", "Heir of Perdition" }),
        });
        var b = FilterCompiler.Analyze(rb, FilterColors.Gold, FilterColors.Silver);
        Assert.Contains("Tyrael's Might", b.UniquesTargeted);          // was carved out pre-S14
        Assert.Contains("Heir of Perdition", b.UniquesTargeted);       // was carved out pre-S14
        Assert.Contains("Banished Lord's Talisman", b.UniquesTargeted);
        Assert.Equal(3, b.UniqueIds.Count);
    }

    [Fact]
    public void Per_tier_rarity_toggles_refine_the_masks_in_place()
    {
        // v1.0.2: the defaults ARE the strict split (Red = legendaries, Pink = rares, both 3+).
        // A per-tier toggle widens/narrows the rarity BITMASK inside the existing rule — ZERO new
        // rules. Here: turn Pink's legendaries back on so Pink becomes rares + legendaries.
        var o = FilterCompiler.Compile(new[] { SampleBuild() },
            new FilterOptions { PinkLegendaries = true }, "t");
        var dec = FilterDecoder.Decode(o.ImportCode);

        // Both tiers are 3+ now, so select by color (Gold = red tier, Silver = pink tier here).
        var red = dec.Rules.Single(r => r.Color == FilterColors.Gold && r.Name.Contains("[3+]"));
        var pink = dec.Rules.Single(r => r.Color == FilterColors.Silver && r.Name.Contains("[3+]"));
        Assert.Equal(Rarity.Legendary,
            (uint)red.Conditions.Single(c => c.Type == 1).MaskOrCount!.Value);          // red untouched
        Assert.Equal(Rarity.Rare | Rarity.Legendary,
            (uint)pink.Conditions.Single(c => c.Type == 1).MaskOrCount!.Value);         // pink widened

        // The whole point: same rule count as the defaults — refinement, not new lines.
        var defaults = FilterCompiler.Compile(new[] { SampleBuild() }, new FilterOptions(), "t");
        Assert.Equal(defaults.RuleCount, o.RuleCount);
    }

    [Theory]
    // v1.0.2 UX: each tier narrates its own scope in plain words, live — including the affix
    // THRESHOLD, so the strict button's bar-raise (3+→4+ / 2→3) is visible the moment it's clicked.
    [InlineData(true, 3, true, true, false, "highlights 3+ affix rares + legendaries")]
    [InlineData(true, 2, true, true, false, "highlights 2+ affix rares + legendaries")]
    [InlineData(true, 2, false, true, false, "highlights 2+ affix legendaries only")]   // Medick's late-game pink
    [InlineData(true, 4, false, true, true, "highlights 4+ affix legendaries only, ancestral tier only")]
    [InlineData(true, 3, true, false, false, "highlights 3+ affix rares only")]
    [InlineData(true, 3, false, false, false, "off — highlights nothing")]
    [InlineData(false, 3, true, true, false, "off — highlights nothing")]
    public void Tier_summary_narrates_the_scope(bool tierOn, int min, bool rares, bool legs, bool anc, string expected)
        => Assert.Equal(expected, FilterCompiler.DescribeTierScope(tierOn, min, rares, legs, anc));

    [Fact]
    public void Default_is_the_strict_split_red_legendaries_pink_rares()
    {
        // v1.0.2 (Medick): strict IS the standard now — no toggle. Defaults = Red 3+ affix
        // LEGENDARIES, Pink 3+ affix RARES; 2-affix items fall to hidden (the looser 2+ tier is the
        // opt-in Leveling silver). Occultist-enchant logic keeps the bar at 3, not a fake 4+.
        var dec = FilterDecoder.Decode(
            FilterCompiler.Compile(new[] { SampleBuild() }, new FilterOptions(), "t").ImportCode);

        var tiers = dec.Rules.Where(r => r.Name.Contains("[3+]")).ToList();
        Assert.Equal(2, tiers.Count);                                     // red AND pink both at 3+
        Assert.DoesNotContain(dec.Rules, r => r.Name.Contains("[2+]"));   // no 2-affix tier by default
        Assert.DoesNotContain(dec.Rules, r => r.Name.Contains("[4+]"));   // no fake 4+ strictness

        // SampleBuild compiles with the legacy Gold/Silver tier colors — select by color, not name.
        var red = tiers.Single(r => r.Color == FilterColors.Gold);     // the primary (red) tier
        var pink = tiers.Single(r => r.Color == FilterColors.Silver);  // the dim (pink) tier
        Assert.Equal(Rarity.Legendary, (uint)red.Conditions.Single(c => c.Type == 1).MaskOrCount!.Value);
        Assert.Equal(Rarity.Rare, (uint)pink.Conditions.Single(c => c.Type == 1).MaskOrCount!.Value);
        Assert.Equal(3ul, red.Conditions.Single(c => c.Type == 6).MaskOrCount!.Value);
        Assert.Equal(3ul, pink.Conditions.Single(c => c.Type == 6).MaskOrCount!.Value);
    }

    [Fact]
    public void Codex_upgrades_outrank_the_affix_and_ga_tiers()
    {
        // v1.0.2 (Medick's methodology): a codex upgrade is a permanent account-wide aspect unlock,
        // so it must win first-match over the Red/Pink/GA tiers — a 3+ legendary that's ALSO a codex
        // upgrade reads White (codex), never Red.
        var rules = FilterDecoder.Decode(
            FilterCompiler.Compile(new[] { SampleBuild() }, new FilterOptions(), "t").ImportCode).Rules.ToList();
        int codex = rules.FindIndex(r => r.Name.Contains("Codex"));
        int red = rules.FindIndex(r => r.Color == FilterColors.Gold && r.Name.Contains("[3+]"));
        int pink = rules.FindIndex(r => r.Color == FilterColors.Silver && r.Name.Contains("[3+]"));
        int ga = rules.FindIndex(r => r.Name.Contains("Greater Affixes"));
        Assert.True(codex >= 0 && red >= 0 && pink >= 0 && ga >= 0);
        Assert.True(codex < red, "Codex must sit above Red");
        Assert.True(codex < pink, "Codex must sit above Pink");
        Assert.True(codex < ga, "Codex must sit above Greater Affixes");
    }

    private static CompiledBuild LevelingSampleBuild() => FilterCompiler.Analyze(
        new ResolvedBuild("t", "Barbarian", new[]
        {
            new ResolvedVariant("v",
                new[] { "Strength", "Maximum Life", "Critical Strike Chance", "Vulnerable Damage Multiplier" },
                System.Array.Empty<string>(),
                new[]
                {
                    new ResolvedSlot("Boots", new[] { "Strength", "Maximum Life", "Armor", "Movement Speed" }),
                    new ResolvedSlot("Ring", new[] { "Critical Strike Chance", "Vulnerable Damage Multiplier", "Attack Speed" }),
                }),
        }), FilterColors.Red, FilterColors.Pink);

    [Fact]
    public void Leveling_adds_a_silver_two_plus_rare_tier_and_is_off_by_default()
    {
        // v1.0.2 (Medick): Leveling ON adds a SILVER tier for 2+ affix RARES (gearing-up loot),
        // sitting below the 3+ Red/Pink tiers. Off by default.
        var def = FilterDecoder.Decode(FilterCompiler.Compile(new[] { LevelingSampleBuild() }, new FilterOptions(), "t").ImportCode);
        Assert.DoesNotContain(def.Rules, r => r.Name.Contains("Leveling"));

        var lvl = FilterDecoder.Decode(FilterCompiler.Compile(new[] { LevelingSampleBuild() }, new FilterOptions { Leveling = true }, "t").ImportCode);
        var silver = lvl.Rules.Single(r => r.Name.Contains("Leveling"));
        Assert.Equal(FilterColors.Silver, silver.Color);
        Assert.Equal(Rarity.Rare, (uint)silver.Conditions.Single(c => c.Type == 1).MaskOrCount!.Value);   // rares only
        Assert.Equal(2ul, silver.Conditions.Single(c => c.Type == 6).MaskOrCount!.Value);                 // 2+ affixes
    }

    [Fact]
    public void Leveling_forces_combined_tiers_to_fit_the_cap()
    {
        // Leveling flips the tiers to COMBINED (one "Rare/Leg" pair) even with per-slot on, so the
        // extra silver tier fits the 25-rule cap. Endgame (no Leveling) keeps precise per-slot rules.
        var lvl = FilterDecoder.Decode(
            FilterCompiler.Compile(new[] { LevelingSampleBuild() }, new FilterOptions { Leveling = true, PerSlotRules = true }, "t").ImportCode);
        Assert.Contains(lvl.Rules, r => r.Name.StartsWith("Rare/Leg"));    // combined tier
        Assert.DoesNotContain(lvl.Rules, r => r.Name.StartsWith("Boots")); // no per-slot tiers

        var endgame = FilterDecoder.Decode(
            FilterCompiler.Compile(new[] { LevelingSampleBuild() }, new FilterOptions { PerSlotRules = true }, "t").ImportCode);
        Assert.Contains(endgame.Rules, r => r.Name.StartsWith("Boots")); // per-slot intact without Leveling
    }

    [Fact]
    public void Per_tier_ancestral_gates_only_its_own_tier()
    {
        var o = FilterCompiler.Compile(new[] { SampleBuild() },
            new FilterOptions { RedAncestralOnly = true }, "t");
        var dec = FilterDecoder.Decode(o.ImportCode);
        // Both tiers are 3+ now — pick red vs pink by color (Gold = red, Silver = pink).
        var red = dec.Rules.Single(r => r.Color == FilterColors.Gold && r.Name.Contains("[3+]"));
        var pink = dec.Rules.Single(r => r.Color == FilterColors.Silver && r.Name.Contains("[3+]"));
        Assert.Contains(red.Conditions, c => c.Type == 2);        // ancestral gate on red
        Assert.DoesNotContain(pink.Conditions, c => c.Type == 2); // pink unaffected
    }

    [Fact]
    public void Both_rarities_off_removes_that_tier_entirely()
    {
        // Unchecking Rares AND Legendaries on pink = pink is off (same as the master toggle).
        var o = FilterCompiler.Compile(new[] { SampleBuild() },
            new FilterOptions { PinkRares = false, PinkLegendaries = false }, "t");
        var dec = FilterDecoder.Decode(o.ImportCode);
        Assert.DoesNotContain(dec.Rules, r => r.Name.Contains("[2+]"));
        Assert.Equal(10, o.RuleCount);                            // matches SilverTier=false baseline
    }

    [Fact]
    public void Analyze_aggregates_talisman_sets_from_variants()
    {
        var rb = new ResolvedBuild("t", "Barbarian", new[]
        {
            new ResolvedVariant("v1", new[] { "Strength" }, System.Array.Empty<string>(),
                TalismanSets: new[] { "Sescheron's Fury" }),
            new ResolvedVariant("v2", new[] { "Strength" }, System.Array.Empty<string>(),
                TalismanSets: new[] { "Sescheron's Fury", "Mastery" }),
        });
        var b = FilterCompiler.Analyze(rb, FilterColors.Red, FilterColors.Pink);
        Assert.Equal(new[] { "Mastery", "Sescheron's Fury" }, b.TalismanSets);   // deduped, sorted
    }

    [Fact]
    public void Scoped_charm_rules_mirror_the_in_game_shape()
    {
        // v1.0.1 (Medick's checkbox design): checked sets pack into ONE green rule + ONE ancestral
        // red rule. The type-9 set condition (per-item refinement) mirrors his hand-built 3.1 export;
        // v1.0.2 pairs it with an Item Power floor of 1 (not the game's bare min-0 — Medick found 0
        // "acts strange"). Unchecked sets aren't listed (→ hidden by the talisman-aware hide rule).
        var sesch = TalismanSetDatabase.ById[0x22fb15u];
        var o = FilterCompiler.Compile(new[] { SampleBuild() },
            new FilterOptions { TalismanSets = new[] { sesch } }, "t");
        var dec = FilterDecoder.Decode(o.ImportCode);

        var green = dec.Rules.Single(r => r.Name == "Charms & Seals (Green)");
        Assert.Contains(green.Conditions, c => c.Type == 0 && c.MaskOrCount == 1 && c.Max == 900);   // Item Power 1..900, not min-0
        var t9 = green.Conditions.Single(c => c.Type == 9);
        Assert.Equal(new[] { 0x22fb15u }, t9.Ids.ToArray());
        var (setId, items) = Assert.Single(t9.SetItems);
        Assert.Equal(0x22fb15u, setId);
        Assert.Equal(5, items.Count);

        var anc = dec.Rules.Single(r => r.Name.Contains("Anc"));
        Assert.Contains(anc.Conditions, c => c.Type == 9);
        Assert.Contains(anc.Conditions, c => c.Type == 2);      // ancestral gate intact
        Assert.True(o.RoundTripOk);
    }

    [Fact]
    public void Empty_talisman_selection_omits_the_charm_rules()
    {
        // All boxes unchecked = the charm rules vanish entirely (charms fall to the hide rule).
        var o = FilterCompiler.Compile(new[] { SampleBuild() },
            new FilterOptions { TalismanSets = System.Array.Empty<TalismanSet>() }, "t");
        var dec = FilterDecoder.Decode(o.ImportCode);
        Assert.DoesNotContain(dec.Rules, r => r.Name.Contains("Seals"));   // set charm rules gone (Unique Charms stays)
        Assert.Equal(9, o.RuleCount);   // the 11 defaults minus green minus ancestral-red
    }

    [Fact]
    public void Hide_rest_never_hides_unique_or_mythic_items()
    {
        // S14 safety net: "mythic" is now a quality on uniques. The catch-all hide rule must only
        // ever hide up to Legendary — never Unique or Mythic — so a mythic drop is never filtered
        // away (it keeps its natural beam) no matter how the game tags it internally.
        var o = FilterCompiler.Compile(new[] { SampleBuild() }, new FilterOptions(), "t");
        var hide = FilterDecoder.Decode(o.ImportCode).Rules.Single(r => r.Visibility == (int)Visibility.HideAll);
        var rarity = hide.Conditions.Single(c => c.Type == 1);   // type 1 = Rarity
        var mask = (uint)rarity.MaskOrCount!.Value;
        Assert.Equal(0u, mask & (Rarity.Unique | Rarity.Mythic));
    }

    [Fact]
    public void S14_backfilled_uniques_resolve_to_type8_ids()
    {
        // 3.1.0.72592 backfill (d4data via D4LootBench, 2026-07-01): clean-name aliases for the six
        // mojibake-drift entries (scrapers emit clean names, the old keys had baked-in Ã/Â bytes)
        // plus the new S14 "(Crucible)" weapon variants under their own ids.
        Assert.True(UniqueDatabase.TryGet("Mjölnic Ryng", out _));
        Assert.True(UniqueDatabase.TryGet("Kilt of Blackwing", out _));
        Assert.True(UniqueDatabase.TryGet("The Grandfather (Crucible)", out var id));
        Assert.Equal(0x27b547u, id);
    }

    [Fact]
    public void Hide_rest_carries_an_item_power_condition_so_d4_actually_applies_it()
    {
        // Bug (S14, in-game): a catch-all HIDE rule with ONLY a rarity condition doesn't apply —
        // items leak through. Conditions are ANDed, so since adding Item Power fixed it in-game the
        // rarity was already matching; D4 just won't honor a rarity-only rule. Every other rule pairs
        // rarity with a concrete second condition (affixes/types/power); the hide rule needs one too.
        var o = FilterCompiler.Compile(new[] { SampleBuild() }, new FilterOptions(), "t");
        var hide = FilterDecoder.Decode(o.ImportCode).Rules.Single(r => r.Visibility == (int)Visibility.HideAll);
        Assert.Contains(hide.Conditions, c => c.Type == 1);                        // rarity still present
        Assert.Contains(hide.Conditions, c => c.Type == 0 && c.MaskOrCount == 1 && c.Max == 900);  // Item Power 1..900
    }

    [Fact]
    public void Unique_charms_show_in_traditional_color_above_the_hide()
    {
        // v1.0.2 (Medick): every unique charm is a keeper, rescued by a Show rule that sits just
        // above the hide. Seals are a different item type, so they aren't in this rule and still
        // hide. Charm type must be the d4data/fnuecke id 0x0022ed05, NOT the reversed seal id.
        var o = FilterCompiler.Compile(new[] { SampleBuild() }, new FilterOptions(), "t");
        var rules = FilterDecoder.Decode(o.ImportCode).Rules.ToList();
        var showIdx = rules.FindIndex(r => r.Name == "Unique Charms");
        var hideIdx = rules.FindIndex(r => r.Visibility == (int)Visibility.HideAll);
        Assert.True(showIdx >= 0, "Unique Charms rule present");
        Assert.True(showIdx < hideIdx, "Unique Charms sits above Hide the rest");
        var show = rules[showIdx];
        Assert.Equal((int)Visibility.Show, show.Visibility);
        Assert.Equal((ulong)Rarity.Unique, show.Conditions.Single(c => c.Type == 1).MaskOrCount!.Value);
        var typeIds = show.Conditions.Single(c => c.Type == 5).Ids;
        Assert.Contains(0x0022ed05u, typeIds);        // real Charm id (d4data + fnuecke)
        Assert.DoesNotContain(0x00237e80u, typeIds);  // NOT the Horadric Seal id
    }

    [Fact]
    public void Unique_charm_database_catalogs_the_s14_charms()
    {
        Assert.True(UniqueCharmDatabase.All.Count >= 100);   // ~110 Talisman_Charm_Unique_* forms
        Assert.All(UniqueCharmDatabase.All, c => Assert.False(string.IsNullOrWhiteSpace(c.Name)));
        Assert.Equal(UniqueCharmDatabase.All.Count, UniqueCharmDatabase.ById.Count);  // ids unique
        Assert.Contains(UniqueCharmDatabase.All, c => c.Name == "Banished Lord's Talisman");
    }

    [Fact]
    public void Curated_unique_charm_subset_shows_exactly_those_ids()
    {
        // App panel with some unchecked → show ONLY the checked ids (type-8 by id).
        var pick = UniqueCharmDatabase.All.Take(3).Select(c => c.Id).ToList();
        var o = FilterCompiler.Compile(new[] { SampleBuild() },
            new FilterOptions { UniqueCharms = pick }, "t");
        var show = FilterDecoder.Decode(o.ImportCode).Rules.Single(r => r.Name == "Unique Charms");
        Assert.Equal((int)Visibility.Show, show.Visibility);
        var ids = show.Conditions.Single(c => c.Type == 8).Ids;   // type-8 = Unique-by-id
        Assert.Equal(pick.OrderBy(x => x), ids.OrderBy(x => x));
    }

    [Fact]
    public void Full_unique_charm_selection_uses_the_compact_type_rule()
    {
        // All boxes checked (== the whole catalog) → collapse to the tiny type-based show-all rule,
        // NOT a 110-id list. (Same shape as the null/default.)
        var all = UniqueCharmDatabase.All.Select(c => c.Id).ToList();
        var o = FilterCompiler.Compile(new[] { SampleBuild() },
            new FilterOptions { UniqueCharms = all }, "t");
        var show = FilterDecoder.Decode(o.ImportCode).Rules.Single(r => r.Name == "Unique Charms");
        Assert.Contains(show.Conditions, c => c.Type == 5);      // item-type rule, not an id list
        Assert.DoesNotContain(show.Conditions, c => c.Type == 8);
    }

    [Fact]
    public void Empty_unique_charm_selection_omits_the_show_rule()
    {
        // Every box unchecked → no show rule; unique charms fall to the hide.
        var o = FilterCompiler.Compile(new[] { SampleBuild() },
            new FilterOptions { UniqueCharms = System.Array.Empty<uint>() }, "t");
        Assert.DoesNotContain(FilterDecoder.Decode(o.ImportCode).Rules, r => r.Name == "Unique Charms");
    }

    [Fact]
    public void Unique_item_database_catalogs_regular_uniques()
    {
        Assert.True(UniqueItemDatabase.All.Count >= 300);   // ~329 canonical regular uniques
        Assert.All(UniqueItemDatabase.All, u => Assert.False(string.IsNullOrWhiteSpace(u.Name)));
        Assert.Equal(UniqueItemDatabase.All.Count, UniqueItemDatabase.ById.Count);   // ids unique
    }

    [Fact]
    public void Unchecked_uniques_get_a_mythic_safe_hide_rule()
    {
        // Uniques show by default (no rule). Unchecking some → ONE "Hide Uniques" rule targeting
        // exactly those ids, gated on Unique rarity so a Mythic version (0x20) is never caught.
        var def = FilterDecoder.Decode(FilterCompiler.Compile(new[] { SampleBuild() }, new FilterOptions(), "t").ImportCode);
        Assert.DoesNotContain(def.Rules, r => r.Name == "Hide Uniques");   // default: nothing hidden

        var hide = UniqueItemDatabase.All.Take(3).Select(u => u.Id).ToList();
        var dec = FilterDecoder.Decode(FilterCompiler.Compile(new[] { SampleBuild() },
            new FilterOptions { HideUniques = hide }, "t").ImportCode);
        var rule = dec.Rules.Single(r => r.Name == "Hide Uniques");
        Assert.Equal((int)Visibility.HideAll, rule.Visibility);
        Assert.Equal((ulong)Rarity.Unique, rule.Conditions.Single(c => c.Type == 1).MaskOrCount!.Value);  // Unique only → mythics safe
        Assert.Equal(hide.OrderBy(x => x), rule.Conditions.Single(c => c.Type == 8).Ids.OrderBy(x => x));

        // must sit below Build Uniques so an unchecked BUILD unique still shows purple, not hidden.
        var rb = new ResolvedBuild("t", "Barbarian", new[]
        {
            new ResolvedVariant("v", new[] { "Strength" }, new[] { "Banished Lord's Talisman" }),
        });
        var b = FilterCompiler.Analyze(rb, FilterColors.Red, FilterColors.Pink);
        var rules = FilterDecoder.Decode(FilterCompiler.Compile(new[] { b },
            new FilterOptions { HideUniques = hide }, "t").ImportCode).Rules.ToList();
        Assert.True(rules.FindIndex(r => r.Name.Contains("Build Uniques")) < rules.FindIndex(r => r.Name == "Hide Uniques"));
    }

    [Fact]
    public void Hide_rest_also_sweeps_unmatched_talismans()
    {
        // v1.0.2 (Medick, in-game): charms/seals that weren't the build's selected set leaked past
        // the catch-all because it didn't match the Talisman rarity — so "everything was showing".
        // The hide mask must now include Talisman (still never Unique/Mythic).
        var o = FilterCompiler.Compile(new[] { SampleBuild() }, new FilterOptions(), "t");
        var hide = FilterDecoder.Decode(o.ImportCode).Rules.Single(r => r.Visibility == (int)Visibility.HideAll);
        var mask = (uint)hide.Conditions.Single(c => c.Type == 1).MaskOrCount!.Value;
        Assert.NotEqual(0u, mask & Rarity.Talisman);
        Assert.Equal(0u, mask & (Rarity.Unique | Rarity.Mythic));
    }

    [Fact]
    public void Chase_tier_and_ancestral_are_red_keeper_tier_is_pink()
    {
        // S14 recolor + v1.0.2 strict default: the "chase" tier (3+ legendaries) and ancestral
        // charms share RED (a value signal); the "keeper" tier (3+ rares) is PINK. Both are 3+ now,
        // so there are two [3+] rules — one Red, one Pink.
        var rb = new ResolvedBuild("t", "Barbarian", new[]
        {
            new ResolvedVariant("v",
                new[] { "Strength", "Maximum Life", "Critical Strike Chance",
                        "Critical Strike Damage Multiplier", "Vulnerable Damage Multiplier",
                        "Cooldown Reduction", "War Cry" },
                System.Array.Empty<string>()),
        });
        var b = FilterCompiler.Analyze(rb, FilterColors.Red, FilterColors.Pink);
        var dec = FilterDecoder.Decode(
            FilterCompiler.Compile(new[] { b }, new FilterOptions(), "t").ImportCode);
        Assert.Contains(dec.Rules, r => r.Name.Contains("[3+]") && r.Color == FilterColors.Red);
        Assert.Contains(dec.Rules, r => r.Name.Contains("[3+]") && r.Color == FilterColors.Pink);
        Assert.Equal(FilterColors.Red, dec.Rules.Single(r => r.Name.Contains("Anc")).Color);
    }

    [Fact]
    public void Rule_names_carry_their_color_label_for_in_game_scanning()
    {
        // Color is appended to each rule name so players can scroll the in-game filter list and
        // toggle by color ("Charms & Seals (Green)" -> off). D4 hard-caps rule names at 24 chars,
        // so the color must fit, not get clipped off the end.
        var rb = new ResolvedBuild("t", "Barbarian", new[]
        {
            new ResolvedVariant("v",
                new[] { "Strength", "Maximum Life", "Critical Strike Chance",
                        "Critical Strike Damage Multiplier", "Vulnerable Damage Multiplier",
                        "Cooldown Reduction", "War Cry" },
                new[] { "Banished Lord's Talisman" }),
        });
        var b = FilterCompiler.Analyze(rb, FilterColors.Red, FilterColors.Pink);
        var dec = FilterDecoder.Decode(FilterCompiler.Compile(new[] { b }, new FilterOptions(), "t").ImportCode);
        Assert.Contains(dec.Rules, r => r.Name == "Build Uniques (Purple)");
        Assert.Contains(dec.Rules, r => r.Name.Contains("[3+]") && r.Name.EndsWith("(Red)"));
        Assert.Contains(dec.Rules, r => r.Name.Contains("[3+]") && r.Name.EndsWith("(Pink)"));  // pink is 3+ rares now
        Assert.Contains(dec.Rules, r => r.Name == "Charms & Seals (Green)");
        Assert.Contains(dec.Rules, r => r.Name.Contains("Anc") && r.Name.EndsWith("(Red)"));
        Assert.All(dec.Rules, r => Assert.True(r.Name.Length <= FilterBuilder.MaxNameLength, $"'{r.Name}' = {r.Name.Length}"));
    }

    [Fact]
    public void Cube_bases_rule_shows_two_affix_magic_items()
    {
        // v1.0.2 (cube research + Medick's persona spec): the bar for a blue being worth a STOP is
        // both affixes on-build AND at least one Greater Affix — plain 2-roll blues aren't worth a
        // speedfarmer's time; the GA version is what the sweaty crowd cubes on stream. OPT-IN
        // (default OFF); the Crafting Coach teaches players to arm it. Honesty note: GA-on-Magic is
        // contested in the datamine — if it never fires in the field, the armed sweats will tell us.
        var o = FilterCompiler.Compile(new[] { SampleBuild() }, new FilterOptions { CubeBases = true }, "t");
        var dec = FilterDecoder.Decode(o.ImportCode);
        var cube = dec.Rules.Single(r => r.Name == "Cube Bases (Gold)");
        Assert.Equal(Rarity.Magic, (uint)cube.Conditions.Single(c => c.Type == 1).MaskOrCount!.Value);
        Assert.Equal(2ul, cube.Conditions.Single(c => c.Type == 6).MaskOrCount!.Value);
        Assert.Equal(1ul, cube.Conditions.Single(c => c.Type == 4).Count!.Value);   // ≥1 GA — the founder's bar
        Assert.Equal(FilterColors.Gold, cube.Color);
        var names = dec.Rules.Select(r => r.Name).ToList();
        Assert.True(names.IndexOf("Cube Bases (Gold)") < names.FindIndex(n => n.Contains("Hide")),
            "cube-bases must sit above the hide rule");

        var off = FilterCompiler.Compile(new[] { SampleBuild() }, new FilterOptions(), "t");
        Assert.DoesNotContain(FilterDecoder.Decode(off.ImportCode).Rules, r => r.Name.Contains("Cube"));
    }

    [Fact]
    public void Default_options_yield_eleven_rules_and_cube_bases_is_opt_in()
    {
        // purple + red + pink + 2 item-power + GA + red-ancestral-charms + green-charms + codex +
        // unique-charms + hide = 11. Cube-bases (gold) is the opt-in 12th — unchecked by default.
        Assert.Equal(11, RuleCount(new FilterOptions()));
        Assert.Equal(12, RuleCount(new FilterOptions { CubeBases = true }));
    }

    [Theory]
    [InlineData(nameof(FilterOptions.HideRest), 10)]             // -1
    [InlineData(nameof(FilterOptions.ItemPowerTiers), 9)]        // -2 (orange + cyan)
    [InlineData(nameof(FilterOptions.SilverTier), 10)]           // -1
    [InlineData(nameof(FilterOptions.BuildUniques), 10)]         // -1
    [InlineData(nameof(FilterOptions.GreaterAffixes), 10)]       // -1
    [InlineData(nameof(FilterOptions.CharmsSeals), 10)]          // -1
    [InlineData(nameof(FilterOptions.CharmsSealsAncestral), 10)] // -1 (red ancestral rule)
    [InlineData(nameof(FilterOptions.Codex), 10)]                // -1
    [InlineData(nameof(FilterOptions.ShowUniqueCharms), 10)]     // -1 (the unique-charm show rule)
    public void Turning_off_one_option_changes_rule_count(string option, int expected)
    {
        var o = option switch
        {
            nameof(FilterOptions.HideRest) => new FilterOptions { HideRest = false },
            nameof(FilterOptions.ItemPowerTiers) => new FilterOptions { ItemPowerTiers = false },
            nameof(FilterOptions.SilverTier) => new FilterOptions { SilverTier = false },
            nameof(FilterOptions.BuildUniques) => new FilterOptions { BuildUniques = false },
            nameof(FilterOptions.GreaterAffixes) => new FilterOptions { GreaterAffixes = false },
            nameof(FilterOptions.CharmsSeals) => new FilterOptions { CharmsSeals = false },
            nameof(FilterOptions.CharmsSealsAncestral) => new FilterOptions { CharmsSealsAncestral = false },
            nameof(FilterOptions.Codex) => new FilterOptions { Codex = false },
            nameof(FilterOptions.ShowUniqueCharms) => new FilterOptions { ShowUniqueCharms = false },
            _ => new FilterOptions(),
        };
        Assert.Equal(expected, RuleCount(o));
    }

    [Fact]
    public void Everything_optional_off_leaves_only_gold()
    {
        var o = new FilterOptions
        {
            BuildUniques = false, SilverTier = false, ItemPowerTiers = false,
            GreaterAffixes = false, CharmsSeals = false, CharmsSealsAncestral = false,
            Codex = false, HideRest = false, ShowUniqueCharms = false,
        };
        Assert.Equal(1, RuleCount(o));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compiled_filter_always_roundtrips(bool strict)
    {
        var opts = strict
            ? new FilterOptions { RedRares = false, PinkLegendaries = false, PinkMinAffixes = 3 }
            : new FilterOptions();
        var o = FilterCompiler.Compile(new[] { SampleBuild() }, opts, "t");
        Assert.True(o.RoundTripOk);
        var dec = FilterDecoder.Decode(o.ImportCode);
        Assert.Equal(o.RuleCount, dec.Rules.Count);
    }

    [Fact]
    public void Short_title_is_embedded_verbatim_as_the_in_game_name()
    {
        const string title = "Medick's Ball Lightning";   // 23 chars, fits D4's limit
        var o = FilterCompiler.Compile(new[] { SampleBuild() }, new FilterOptions(), "Filter", title);
        Assert.Equal(title, FilterDecoder.Decode(o.ImportCode).Name);
    }

    [Fact]
    public void Long_names_are_clamped_to_24_chars()
    {
        // D4 drops a filter/rule name >24 chars on import; the encoder clamps so they always show.
        const string longTitle = "Loot Filters By Medick -- Maxroll Ball Lightning";  // 48 chars
        var o = FilterCompiler.Compile(new[] { SampleBuild() }, new FilterOptions(), "Filter", longTitle);
        var dec = FilterDecoder.Decode(o.ImportCode);
        Assert.True(dec.Name.Length <= FilterBuilder.MaxNameLength, $"filter name len {dec.Name.Length}");
        Assert.StartsWith("Loot Filters By Medick", dec.Name);
        Assert.All(dec.Rules, r => Assert.True(r.Name.Length <= FilterBuilder.MaxNameLength, $"rule '{r.Name}' len {r.Name.Length}"));
    }

    [Fact]
    public void Rule_name_over_24_is_clamped()
    {
        var rule = FilterBuilder.MakeRule("This rule name is far too long for D4", Visibility.HideAll, new byte[0][]);
        var dec = FilterDecoder.Decode(FilterBuilder.ToImportCode(FilterBuilder.MakeFilter("F", new[] { rule })));
        Assert.True(dec.Rules[0].Name.Length <= FilterBuilder.MaxNameLength);
    }
}
