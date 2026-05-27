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
    public void All_options_on_is_nine_rules()
    {
        // purple + gold + silver + 2 item-power + GA + charms + codex + hide = 9
        Assert.Equal(9, RuleCount(new FilterOptions()));
    }

    [Theory]
    [InlineData(nameof(FilterOptions.HideRest), 8)]        // -1
    [InlineData(nameof(FilterOptions.ItemPowerTiers), 7)]  // -2 (orange + cyan)
    [InlineData(nameof(FilterOptions.SilverTier), 8)]      // -1
    [InlineData(nameof(FilterOptions.BuildUniques), 8)]    // -1
    [InlineData(nameof(FilterOptions.GreaterAffixes), 8)]  // -1
    [InlineData(nameof(FilterOptions.CharmsSeals), 8)]     // -1
    [InlineData(nameof(FilterOptions.Codex), 8)]           // -1
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
            GreaterAffixes = false, CharmsSeals = false, Codex = false, HideRest = false,
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
    public void Filter_title_is_embedded_as_the_in_game_name()
    {
        const string title = "Loot Filters By Medick -- Maxroll Ball Lightning";
        var o = FilterCompiler.Compile(new[] { SampleBuild() }, new FilterOptions(), "Filter", title);
        var dec = FilterDecoder.Decode(o.ImportCode);
        Assert.Equal(title, dec.Name);   // D4 shows this as the filter name on import
    }
}
