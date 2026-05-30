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
    public void Mythics_are_split_into_own_category_not_purple()
    {
        var rb = new ResolvedBuild("t", "Barbarian", new[]
        {
            new ResolvedVariant("v", new[] { "Strength" },
                new[] { "Banished Lord's Talisman", "Tyrael's Might", "Heir of Perdition" }),
        });
        var b = FilterCompiler.Analyze(rb, FilterColors.Gold, FilterColors.Silver);
        Assert.Contains("Tyrael's Might", b.Mythics);
        Assert.Contains("Heir of Perdition", b.Mythics);
        Assert.Contains("Banished Lord's Talisman", b.UniquesTargeted);  // regular unique stays purple
        Assert.DoesNotContain("Tyrael's Might", b.UniquesTargeted);      // mythic is NOT targeted
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
