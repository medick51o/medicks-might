using System.Security.Cryptography;
using System.Text;
using D4BuildFilter.Core;
using D4BuildFilter.WPF.ViewModels;
using Xunit;

namespace D4BuildFilter.Tests;

public class V120LayoutTests
{
    [Fact]
    public void Two_builds_share_one_slot_layout_inside_the_cap()
    {
        var output = FilterCompiler.Compile([Build("Blizzard", "Sorcerer", 100), Build("Minion", "Necromancer", 200)],
            new FilterOptions(), "super");
        var rules = FilterDecoder.Decode(output.ImportCode).Rules;

        Assert.True(output.RuleCount <= 25);
        Assert.Equal([
            "Build Uniques (Purple)",
            "Charms&Seals Anc (Red)", "Charms & Seals (Green)",
            "Rare/Leg [3+] (Red)", "Rare/Leg [3+] (Pink)",
            "Codex Upgrades (White)", "Hide the rest"
        ], rules.Select(r => r.Name));
        Assert.DoesNotContain(rules, r => r.Name.Contains("Leg (") && !r.Name.Contains("Rare/Leg"));
        AssertTier(rules[3], Rarity.Legendary, FilterColors.Red);
        AssertTier(rules[4], Rarity.Rare, FilterColors.Pink);
    }

    [Fact]
    public void Multi_build_fixed_tiers_cannot_be_removed_by_single_build_toggles()
    {
        // Reverting the pin lets unchecked inherited toggles delete all four build tiers while the fixed-layout legend still claims them.
        var builds = new[] { Build("Blizzard", "Sorcerer", 100), Build("Minion", "Necromancer", 200) };
        var output = FilterCompiler.Compile(builds,
            new FilterOptions { GoldTier = false, SilverTier = false }, "super");

        Assert.DoesNotContain(FilterDecoder.Decode(output.ImportCode).Rules, IsTier);
        var restored = FilterCompiler.Compile(builds, new FilterOptions(), "super");
        Assert.Equal(2, FilterDecoder.Decode(restored.ImportCode).Rules.Count(IsTier));

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
    public void Five_builds_share_one_layout_and_stay_copyable()
    {
        var builds = new[]
        {
            Build("Blizzard", "Sorcerer", 100), Build("Minion", "Necromancer", 200),
            Build("Heartseeker", "Rogue", 300), Build("Dance of Knives", "Rogue", 400),
            Build("Charge", "Barbarian", 500),
        };

        var three = FilterDecoder.Decode(FilterCompiler.Compile(builds.Take(3).ToList(), new FilterOptions(), "three").ImportCode);
        var threeTiers = three.Rules.Where(IsTier).ToList();
        Assert.Equal(2, threeTiers.Count);
        Assert.All(threeTiers.Where(r => RarityMask(r) == Rarity.Legendary), r => Assert.Equal(FilterColors.Red, r.Color));
        Assert.All(threeTiers.Where(r => RarityMask(r) == Rarity.Rare), r => Assert.Equal(FilterColors.Pink, r.Color));

        var five = FilterCompiler.CompileWithinCap(builds, new FilterOptions(), 25,
            out CompileFitReport fit, "five");
        Assert.True(fit.Fits);
        Assert.True(five.IsCopyable);
        Assert.InRange(five.RuleCount, 1, 25);
        Assert.DoesNotContain(FilterDecoder.Decode(five.ImportCode).Rules, r => r.Name.Contains(" Leg "));
    }

    [Theory]
    [InlineData("Blizzard", "Sorcerer", "Blizzard")]
    [InlineData("Minion", "Necromancer", "Minion")]
    [InlineData("Heartseeker", "Rogue", "HrtSkr")]
    [InlineData("Dance of Knives", "Rogue", "DoK")]
    [InlineData("Mighty Throw Barb", "Barbarian", "MightyThrw")]
    public void Multi_build_names_use_readable_tags_and_curated_overrides(string name, string className, string expectedTag)
    {
        var builds = new[] { Build(name, className, 100), Build("Control", "Druid", 200) };
        Assert.Equal(expectedTag, BuildTagger.Resolve(builds)[0]);
        var rules = FilterDecoder.Decode(FilterCompiler.Compile(builds, new FilterOptions(), "names").ImportCode).Rules;
        Assert.DoesNotContain(rules, r => r.Name.Contains($"{expectedTag} Leg"));
        Assert.Contains(rules, r => r.Name.StartsWith("Rare/Leg [3+]"));
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
        Assert.Equal(["Minion B", "Minion N"], BuildTagger.Resolve(builds));
        Assert.DoesNotContain(names, n => n.Contains("Leg ("));
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
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

        var painted = builds[0] with { KeeperColorOverride = FilterColors.Gold, KeeperColorUserChosen = true };
        var cube = FilterCompiler.Compile([painted, builds[1]], new FilterOptions { CubeBases = true }, "cube");
        var cubeAgain = FilterCompiler.Compile([painted, builds[1]], new FilterOptions { CubeBases = true }, "cube");
        var leveling = FilterCompiler.Compile(builds, new FilterOptions { Leveling = true }, "leveling");
        var defaultKeeper = FilterCompiler.Compile(builds, new FilterOptions { CubeBases = true }, "cube-default");

        Assert.Empty(cube.Diagnostics);
        Assert.Empty(leveling.Diagnostics);
        var cubeWarning = Assert.Single(cube.Advisories, advisory =>
            advisory.Contains("Cube Bases", StringComparison.Ordinal)
            && advisory.Contains("Gold", StringComparison.Ordinal));
        Assert.Equal(cubeWarning, Assert.Single(cubeAgain.Advisories, advisory =>
            advisory.Contains("Cube Bases", StringComparison.Ordinal)
            && advisory.Contains("Gold", StringComparison.Ordinal)));
        Assert.DoesNotContain(defaultKeeper.Advisories, advisory =>
            advisory.Contains("Cube Bases", StringComparison.Ordinal)
            && advisory.Contains("Gold", StringComparison.Ordinal));
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
        Assert.DoesNotContain(vm.SuperBuildAdvisory.Split('\n', StringSplitOptions.RemoveEmptyEntries), line =>
            line.Contains("Cube Bases", StringComparison.Ordinal)
            && line.Contains("keeper", StringComparison.OrdinalIgnoreCase));
        vm.OptGreaterAffixes = true;
        Assert.False(vm.OptLeveling);
        Assert.DoesNotContain(vm.SuperBuildAdvisory, "Leveling", StringComparison.Ordinal);
    }

    [Fact]
    public void Single_build_bytes_remain_identical()
    {
        var build = FilterCompiler.Analyze(new ResolvedBuild("Heartseeker", "Rogue",
            [new ResolvedVariant("Endgame", ["Dexterity", "Critical Strike Chance", "Vulnerable Damage Multiplier"], [])]),
            FilterColors.Red, FilterColors.Pink);
        var output = FilterCompiler.Compile([build], new FilterOptions(), "Filter", "Pinned Single");

        // Recomputed after the shared slot layout and the opt-in defaults. This pins the new bytes;
        // it is not the pre-change hash.
        Assert.Equal("DC4EA9B8932B6E4506229A7BD271CF4B5EF54144B5628F72231ECF29DFA51001",
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

        Assert.Contains("shared layout", card.ProvenanceChip, StringComparison.OrdinalIgnoreCase);
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
