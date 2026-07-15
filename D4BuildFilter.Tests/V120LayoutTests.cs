using System.Security.Cryptography;
using System.Text;
using D4BuildFilter.Core;
using D4BuildFilter.WPF.ViewModels;
using Xunit;

namespace D4BuildFilter.Tests;

public class V120LayoutTests
{
    [Fact]
    public void Two_build_layout_is_the_fixed_13_rule_colors_by_build_shape()
    {
        var output = FilterCompiler.Compile([Build("Blizzard", "Sorcerer", 100), Build("Minion", "Necromancer", 200)],
            new FilterOptions(), "super");
        var rules = FilterDecoder.Decode(output.ImportCode).Rules;

        Assert.Equal(13, rules.Count);
        Assert.Equal([
            "Build Uniques (Purple)", "Codex Upgrades (White)",
            "Blizzard Leg (Red)", "Minion Leg (Pink)",
            "Blizzard Rare (Gold)", "Minion Rare (Silver)",
            "Item Power 900+ (Orange)", "Item Power 850+ (Cyan)", "Greater Affixes (Blue)",
            "Charms&Seals Anc (Red)", "Charms & Seals (Green)", "Unique Charms", "Hide the rest"
        ], rules.Select(r => r.Name));
        AssertTier(rules[2], Rarity.Legendary, FilterColors.Red);
        AssertTier(rules[3], Rarity.Legendary, FilterColors.Pink);
        AssertTier(rules[4], Rarity.Rare, FilterColors.Gold);
        AssertTier(rules[5], Rarity.Rare, FilterColors.Silver);
    }

    [Fact]
    public void Multi_build_fixed_tiers_cannot_be_removed_by_single_build_toggles()
    {
        // Reverting the pin lets unchecked inherited toggles delete all four build tiers while the fixed-layout legend still claims them.
        var builds = new[] { Build("Blizzard", "Sorcerer", 100), Build("Minion", "Necromancer", 200) };
        var output = FilterCompiler.Compile(builds,
            new FilterOptions { GoldTier = false, SilverTier = false }, "super");

        Assert.Equal(4, FilterDecoder.Decode(output.ImportCode).Rules.Count(IsTier));

        var vm = new MainViewModel(startTierListFetches: false)
        {
            OptGoldTier = false,
            OptSilverTier = false,
        };
        vm.Ingest(Resolved("Blizzard", "Sorcerer"), "Test");
        vm.IngestSecond(Resolved("Minion", "Necromancer"));
        Assert.True(vm.OptGoldTier);
        Assert.True(vm.OptSilverTier);
        vm.DropSecondBuildCommand.Execute(null);
        Assert.False(vm.OptGoldTier);
        Assert.False(vm.OptSilverTier);
    }

    [Fact]
    public void Three_and_four_builds_use_colors_by_tier_and_fifth_is_refused()
    {
        var builds = new[]
        {
            Build("Blizzard", "Sorcerer", 100), Build("Minion", "Necromancer", 200),
            Build("Heartseeker", "Rogue", 300), Build("Dance of Knives", "Rogue", 400),
            Build("Charge", "Barbarian", 500),
        };

        var three = FilterDecoder.Decode(FilterCompiler.Compile(builds.Take(3).ToList(), new FilterOptions(), "three").ImportCode);
        var threeTiers = three.Rules.Where(IsTier).ToList();
        Assert.Equal(6, threeTiers.Count);
        Assert.All(threeTiers.Where(r => RarityMask(r) == Rarity.Legendary), r => Assert.Equal(FilterColors.Red, r.Color));
        Assert.All(threeTiers.Where(r => RarityMask(r) == Rarity.Rare), r => Assert.Equal(FilterColors.Gold, r.Color));

        var four = FilterCompiler.CompileWithinCap(builds.Take(4).ToList(), new FilterOptions(), 25,
            out CompileFitReport fit, "four");
        Assert.True(fit.Fits);
        Assert.Equal(17, four.RuleCount);

        var five = FilterCompiler.CompileWithinCap(builds, new FilterOptions(), 25,
            out CompileFitReport refused, "five");
        Assert.False(refused.Fits);
        Assert.False(five.IsCopyable);
        Assert.Empty(five.ImportCode);
    }

    [Theory]
    [InlineData("Blizzard", "Sorcerer", "Blizzard")]
    [InlineData("Minion", "Necromancer", "Minion")]
    [InlineData("Heartseeker", "Rogue", "HrtSkr")]
    [InlineData("Dance of Knives", "Rogue", "DoK")]
    [InlineData("Mighty Throw Barb", "Barbarian", "MightyThrw")]
    public void Multi_build_names_use_readable_tags_and_curated_overrides(string name, string className, string expectedTag)
    {
        var rules = FilterDecoder.Decode(FilterCompiler.Compile(
            [Build(name, className, 100), Build("Control", "Druid", 200)], new FilterOptions(), "names").ImportCode).Rules;

        Assert.Contains(rules, r => r.Name == $"{expectedTag} Leg (Red)");
        Assert.Contains(rules, r => r.Name == $"{expectedTag} Rare (Gold)");
        Assert.All(rules, r => Assert.True(r.Name.Length <= FilterBuilder.MaxNameLength));
    }

    [Fact]
    public void Tag_collisions_add_class_initial_only_and_recompiles_are_deterministic()
    {
        var builds = new[] { Build("Minion Build", "Barbarian", 100), Build("Minion Guide", "Necromancer", 200) };

        var first = FilterCompiler.Compile(builds, new FilterOptions(), "names");
        var second = FilterCompiler.Compile(builds, new FilterOptions(), "names");
        var names = FilterDecoder.Decode(first.ImportCode).Rules.Select(r => r.Name).ToList();

        Assert.Equal(first.ImportCode, second.ImportCode);
        Assert.Contains("Minion B Leg (Red)", names);
        Assert.Contains("Minion N Leg (Pink)", names);
        Assert.DoesNotContain(names, n => n.Any(char.IsDigit) && n.Contains("Minion", StringComparison.Ordinal));
    }

    [Fact]
    public void Terminal_tag_pass_resolves_new_and_identical_identity_collisions_deterministically()
    {
        // Reverting the terminal pass leaves duplicate Minion-B and identical-identity tier rule names in the rendered filter.
        var threeWay = new[]
        {
            Build("Minion Barb", "Barbarian", 100) with { Source = "Maxroll" },
            Build("Minion Guide", "Necromancer", 200),
            Build("Minion B", "Rogue", 300),
        };
        var first = BuildTagger.Resolve(threeWay);
        var second = BuildTagger.Resolve(threeWay);
        Assert.Equal(first, second);
        Assert.Equal(3, first.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var identical = new[]
        {
            Build("Minion", "Necromancer", 400) with { Source = "Maxroll" },
            Build("Minion", "Necromancer", 500) with { Source = "Maxroll" },
        };
        Assert.Equal(2, BuildTagger.Resolve(identical).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var tierNames = FilterDecoder.Decode(FilterCompiler.Compile(identical, new FilterOptions(), "identical").ImportCode)
            .Rules.Where(IsTier).Select(r => r.Name).ToList();
        Assert.Equal(tierNames.Count, tierNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Malformed_override_rows_fall_back_without_clipping_color_suffixes()
    {
        // Reverting validation lets empty, duplicate, parenthesized, or oversized tags escape and lets D4's 24-char clamp amputate (Color).
        const string overrides = """
        {"overrides":[
          {"source":"Test","class":"Rogue","name":"Healthy","tag":"Good"},
          {"source":"Test","class":"Rogue","name":"Heartseeker","tag":"WayTooLongTag"},
          {"source":"Test","class":"Sorcerer","name":"Blizzard","tag":""},
          {"source":"Test","class":"Druid","name":"First Shared","tag":"Shared"},
          {"source":"Test","class":"Rogue","name":"Death Trap","tag":"Shared"},
          {"source":"Test","class":"Necromancer","name":"Minion","tag":"Min(ion"},
          {"source":"Test","class":"Rogue","tag":"MissingName"}
        ]}
        """;
        var builds = new[]
        {
            Build("Healthy", "Rogue", 100) with { Source = "Test" },
            Build("Heartseeker", "Rogue", 200) with { Source = "Test" },
            Build("Blizzard", "Sorcerer", 300) with { Source = "Test" },
            Build("First Shared", "Druid", 400) with { Source = "Test" },
            Build("Death Trap", "Rogue", 500) with { Source = "Test" },
            Build("Minion", "Necromancer", 600) with { Source = "Test" },
        };

        var tags = BuildTagger.ResolveWithOverrides(builds, overrides);
        Assert.Equal(["Good", "HrtSkr", "Blizzard", "Shared", "Death Trap", "Minion"], tags);
        Assert.Equal("HrtSkr", Assert.Single(BuildTagger.ResolveWithOverrides([builds[1]], "not json")));
        foreach (var tag in tags)
        {
            var bytes = FilterBuilder.MakeFilter("t",
                [FilterBuilder.MakeRule($"{tag} Rare (Silver)", Visibility.Recolor, [])]);
            var name = FilterDecoder.Decode(FilterBuilder.ToImportCode(bytes)).Rules.Single().Name;
            Assert.EndsWith("(Silver)", name);
            Assert.True(name.Length <= FilterBuilder.MaxNameLength);
        }
    }

    [Fact]
    public void Cube_option_warns_against_default_gold_without_phantom_leveling_collision()
    {
        // Reverting this fix loses the option-vs-default Cube warning or restores a Leveling warning for a tier Super Builds never emit.
        var builds = new[] { Build("Blizzard", "Sorcerer", 100), Build("Minion", "Necromancer", 200) };

        var cube = FilterCompiler.Compile(builds, new FilterOptions { CubeBases = true }, "cube");
        var cubeAgain = FilterCompiler.Compile(builds, new FilterOptions { CubeBases = true }, "cube");
        var leveling = FilterCompiler.Compile(builds, new FilterOptions { Leveling = true }, "leveling");

        Assert.Empty(cube.Diagnostics);
        Assert.Empty(leveling.Diagnostics);
        var cubeWarning = Assert.Single(cube.Advisories, advisory =>
            advisory.Contains("Cube Bases", StringComparison.Ordinal)
            && advisory.Contains("Gold", StringComparison.Ordinal));
        Assert.Equal(cubeWarning, Assert.Single(cubeAgain.Advisories, advisory =>
            advisory.Contains("Cube Bases", StringComparison.Ordinal)
            && advisory.Contains("Gold", StringComparison.Ordinal)));
        Assert.DoesNotContain(leveling.Advisories, advisory =>
            advisory.Contains("Leveling", StringComparison.Ordinal));

        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(new ResolvedBuild("Blizzard", "Sorcerer",
            [new ResolvedVariant("Endgame", ["Intelligence", "Cooldown Reduction", "Maximum Life"], [])]), "Test",
        [
            new ResolvedBuild("Minion", "Necromancer",
                [new ResolvedVariant("Endgame", ["Intelligence", "Armor", "Maximum Life"], [])])
        ]);
        Assert.False(vm.MultiBuildLevelingEnabled);
        Assert.False(vm.OptLeveling);
        vm.OptCubeBases = true;
        Assert.Single(vm.SuperBuildAdvisory.Split('\n', StringSplitOptions.RemoveEmptyEntries), line =>
            line.Contains("Cube Bases", StringComparison.Ordinal)
            && line.Contains("Gold", StringComparison.Ordinal));
        vm.OptGreaterAffixes = false;
        vm.OptGreaterAffixes = true;
        Assert.Single(vm.SuperBuildAdvisory.Split('\n', StringSplitOptions.RemoveEmptyEntries), line =>
            line.Contains("Cube Bases", StringComparison.Ordinal)
            && line.Contains("Gold", StringComparison.Ordinal));
    }

    [Fact]
    public void Single_build_bytes_remain_identical()
    {
        var build = FilterCompiler.Analyze(new ResolvedBuild("Heartseeker", "Rogue",
            [new ResolvedVariant("Endgame", ["Dexterity", "Critical Strike Chance", "Vulnerable Damage Multiplier"], [])]),
            FilterColors.Red, FilterColors.Pink);
        var output = FilterCompiler.Compile([build], new FilterOptions(), "Filter", "Pinned Single");

        Assert.Equal("1B44D5072544C40E8FA97C1B6D6BC28A0E799A30A56D28FEA3D12B740950B935",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output.ImportCode))));
    }

    [Fact]
    public void Witness_card_states_the_active_scheme_and_shares_the_compiled_code()
    {
        var builds = new[] { Build("Blizzard", "Sorcerer", 100), Build("Minion", "Necromancer", 200) };
        var output = FilterCompiler.Compile(builds, new FilterOptions(), "super");
        var card = WitnessCardComposer.Compose(new WitnessCardRequest("Blizzard", "Sorcerer", "Maxroll", "Endgame", "S",
            output, 25, "https://example.test", BuildIdentities:
            [WitnessCardComposer.Identity("Blizzard", "Sorcerer"), WitnessCardComposer.Identity("Minion", "Necromancer")])).Card!;

        Assert.Contains("colors by build", card.ProvenanceChip, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(output.ImportCode, card.ImportCode);
    }

    private static CompiledBuild Build(string name, string className, uint seed)
    {
        uint[] pool = [seed + 1, seed + 2, seed + 3, seed + 4];
        return new CompiledBuild(name, className, FilterColors.Red, FilterColors.Pink,
            pool, pool.ToDictionary(id => id, id => $"Affix {id}"), [], [seed + 50], ["Unique"], [], [], [], []);
    }

    private static ResolvedBuild Resolved(string name, string className) => new(name, className,
        [new ResolvedVariant("Endgame", ["Maximum Life", "Armor", "Critical Strike Chance"], [])]);

    private static bool IsTier(DecodedRule rule) => rule.Conditions.Any(c => c.Type == 6)
        && rule.Conditions.Any(c => c.Type == 1);

    private static uint RarityMask(DecodedRule rule) => (uint)rule.Conditions.Single(c => c.Type == 1).MaskOrCount!.Value;

    private static void AssertTier(DecodedRule rule, uint rarity, uint color)
    {
        Assert.Equal(rarity, RarityMask(rule));
        Assert.Equal(color, rule.Color);
        Assert.Equal(3ul, rule.Conditions.Single(c => c.Type == 6).MaskOrCount);
    }
}
