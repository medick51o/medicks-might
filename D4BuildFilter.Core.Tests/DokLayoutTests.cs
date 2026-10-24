using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Core.Tests;

/// <summary>The Maxroll Dance of Knives planner, with leveling profiles dropped, compiles to the
/// same 25-rule shape as the hand-built filter. Ids the planner equips are kept. A hand edit that
/// copies an affix onto a slot the current planner does not roll stays a superset only when that
/// affix is actually on the slot.</summary>
public class DokLayoutTests
{
    [Fact]
    public void Five_variants_match_the_hand_built_rule_list()
    {
        var golden = FilterDecoder.Decode(FilterImportTests.Fixture("dok_5variant_target.b64"));
        var output = CompileDok();
        Assert.True(output.IsCopyable);
        Assert.Equal(25, output.RuleCount);
        var got = FilterDecoder.Decode(output.ImportCode);

        Assert.Equal(golden.Rules.Select(rule => rule.Name), got.Rules.Select(rule => rule.Name));
        for (int i = 0; i < golden.Rules.Count; i++)
        {
            Assert.Equal(golden.Rules[i].Visibility, got.Rules[i].Visibility);
            Assert.Equal(golden.Rules[i].Conditions.Select(condition => condition.Type),
                got.Rules[i].Conditions.Select(condition => condition.Type));
        }

        FilterImportTests.AssertSuperset(golden.Rules[0], got.Rules[0]); // uniques, superset
        FilterImportTests.AssertSuperset(golden.Rules[1], got.Rules[1]); // charm sets
        FilterImportTests.AssertSuperset(golden.Rules[2], got.Rules[2]);
        for (int i = 3; i <= 20; i++)
        {
            var wantTypes = golden.Rules[i].Conditions.First(condition => condition.Type == 5).Ids;
            var gotTypes = got.Rules[i].Conditions.First(condition => condition.Type == 5).Ids;
            Assert.True(wantTypes.All(gotTypes.Contains), golden.Rules[i].Name);
        }

        var other = got.Rules.Single(rule => rule.Name == "Other Gear [3+] (Red)");
        var goldenOther = golden.Rules.Single(rule => rule.Name == "Other Gear [3+] (Red)");
        Assert.True(goldenOther.Conditions.Single(condition => condition.Type == 6).Ids
            .All(other.Conditions.Single(condition => condition.Type == 6).Ids.Contains));
    }

    [Fact]
    public void Compile_within_cap_never_returns_a_copyable_filter_over_25()
    {
        var output = FilterCompiler.CompileWithinCap([AnalyzeDok()],
            new FilterOptions
            {
                PerSlotRules = true,
                ItemPowerTiers = true,
                GreaterAffixes = true,
                ShowUniqueCharms = true,
                CubeBases = true,
                Leveling = true,
                TalismanSets = TalismanSetDatabase.ForClass("Rogue"),
            }, 25, out CompileFitReport fit);
        if (output.IsCopyable)
            Assert.InRange(output.RuleCount, 1, 25);
        else
            Assert.True(string.IsNullOrEmpty(output.ImportCode));
        Assert.True(fit.FinalRuleCount <= 25 || !fit.Fits);
    }

    private static FilterOutput CompileDok()
    {
        var analyzed = AnalyzeDok();
        var sets = TalismanSetDatabase.DefaultSelectionForClass(analyzed.Class, analyzed.TalismanSets, out _);
        return FilterCompiler.Compile([analyzed], new FilterOptions
        {
            PerSlotRules = true,
            TalismanSets = sets,
        }, "dok", "Dance of Knives Endgame");
    }

    private static CompiledBuild AnalyzeDok()
    {
        var parsed = MaxrollFetcher.Parse(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "dok-planner.json")),
            NameLookup.Default(), UniqueLookup.Default());
        var kept = parsed with
        {
            Variants = parsed.Variants
                .Where(variant => !variant.Name.Contains("Leveling", StringComparison.OrdinalIgnoreCase))
                .ToList(),
        };
        return FilterCompiler.Analyze(kept, FilterColors.Red, FilterColors.Pink);
    }
}
