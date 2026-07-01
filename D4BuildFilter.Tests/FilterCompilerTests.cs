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
    public void Hide_rest_carries_an_item_power_condition_so_d4_actually_applies_it()
    {
        // Bug (S14, in-game): a catch-all HIDE rule with ONLY a rarity condition doesn't apply —
        // items leak through. Conditions are ANDed, so since adding Item Power fixed it in-game the
        // rarity was already matching; D4 just won't honor a rarity-only rule. Every other rule pairs
        // rarity with a concrete second condition (affixes/types/power); the hide rule needs one too.
        var o = FilterCompiler.Compile(new[] { SampleBuild() }, new FilterOptions(), "t");
        var hide = FilterDecoder.Decode(o.ImportCode).Rules.Single(r => r.Visibility == (int)Visibility.HideAll);
        Assert.Contains(hide.Conditions, c => c.Type == 1);                        // rarity still present
        Assert.Contains(hide.Conditions, c => c.Type == 0 && c.MaskOrCount == 1);  // Item Power range, min 1
    }

    [Fact]
    public void Chase_tier_and_ancestral_are_red_keeper_tier_is_pink()
    {
        // S14 recolor: the 3-affix "chase" tier and ancestral charms share RED (a value signal);
        // the 2-affix "keeper" tier is PINK. (Gold blended into yellow rares; silver was too dull.)
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
        Assert.Equal(FilterColors.Red, dec.Rules.Single(r => r.Name.Contains("[3+]")).Color);
        Assert.Equal(FilterColors.Pink, dec.Rules.Single(r => r.Name.Contains("[2+]")).Color);
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
        Assert.Contains(dec.Rules, r => r.Name.Contains("[2+]") && r.Name.EndsWith("(Pink)"));
        Assert.Contains(dec.Rules, r => r.Name == "Charms & Seals (Green)");
        Assert.Contains(dec.Rules, r => r.Name.Contains("Anc") && r.Name.EndsWith("(Red)"));
        Assert.All(dec.Rules, r => Assert.True(r.Name.Length <= FilterBuilder.MaxNameLength, $"'{r.Name}' = {r.Name.Length}"));
    }

    [Fact]
    public void All_options_on_is_ten_rules()
    {
        // purple + gold + silver + 2 item-power + GA + red-ancestral-charms + green-charms + codex + hide = 10
        // (the CharmsSealsAncestral red rule, default-on, is the 10th — added after this test was written.)
        Assert.Equal(10, RuleCount(new FilterOptions()));
    }

    [Theory]
    [InlineData(nameof(FilterOptions.HideRest), 9)]              // -1
    [InlineData(nameof(FilterOptions.ItemPowerTiers), 8)]        // -2 (orange + cyan)
    [InlineData(nameof(FilterOptions.SilverTier), 9)]            // -1
    [InlineData(nameof(FilterOptions.BuildUniques), 9)]          // -1
    [InlineData(nameof(FilterOptions.GreaterAffixes), 9)]        // -1
    [InlineData(nameof(FilterOptions.CharmsSeals), 9)]           // -1
    [InlineData(nameof(FilterOptions.CharmsSealsAncestral), 9)]  // -1 (red ancestral rule)
    [InlineData(nameof(FilterOptions.Codex), 9)]                 // -1
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
            Codex = false, HideRest = false,
        };
        Assert.Equal(1, RuleCount(o));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compiled_filter_always_roundtrips(bool strict)
    {
        var o = FilterCompiler.Compile(new[] { SampleBuild() },
            new FilterOptions { StrictEndgame = strict }, "t");
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
