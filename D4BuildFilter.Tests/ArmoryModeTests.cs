using D4BuildFilter.Core;
using D4BuildFilter.WPF.ViewModels;
using Xunit;

namespace D4BuildFilter.Tests;

/// <summary>Armory Mode is a two-build Super Build: each build keeps distinct legendary and rare
/// rules, uniques are unioned, and the same cap/copy gates judge the combined output.</summary>
public class ArmoryModeTests
{
    private static CompiledBuild Analyze(string name, string className, string unique,
        string talismanSet, params string[] affixes) => FilterCompiler.Analyze(
        new ResolvedBuild(name, className,
        [
            new ResolvedVariant("Endgame", affixes, [unique], TalismanSets: [talismanSet])
        ]),
        FilterColors.Red, FilterColors.Pink);

    [Fact]
    public void Two_build_merge_keeps_separate_tiers_and_unions_purple_uniques()
    {
        var first = Analyze("Speedfarm", "Barbarian", "Banished Lord's Talisman",
            "Berserker's Crucible", "Strength", "Maximum Life", "Armor");
        var second = Analyze("Pit Push", "Sorcerer", "Tyrael's Might",
            "Cain's Wild Lightning", "Strength", "Maximum Life", "Armor");
        var options = new FilterOptions();

        var single = FilterCompiler.Compile([first], options, "single");
        var merged = FilterCompiler.Compile([first, second], options, "armory");
        var decoded = FilterDecoder.Decode(merged.ImportCode);

        Assert.Equal(13, merged.RuleCount);
        Assert.Equal(4, decoded.Rules.Count(r => r.Conditions.Any(c => c.Type == 6)));
        Assert.Equal(merged.RuleCount, decoded.Rules.Count);
        var purple = Assert.Single(decoded.Rules, r => r.Name == "Build Uniques (Purple)");
        var purpleIds = purple.Conditions.Single(c => c.Type == 8).Ids;
        Assert.Contains(first.UniqueIds.Single(), purpleIds);
        Assert.Contains(second.UniqueIds.Single(), purpleIds);
    }

    [Fact]
    public void Auto_fit_and_over_cap_honesty_use_the_merged_rule_count()
    {
        var builds = new[] { SyntheticSlotted("Speedfarm", 100), SyntheticSlotted("Pit Push", 200) };
        var options = new FilterOptions { PerSlotRules = true };
        var precise = FilterCompiler.Compile(builds, options, "armory");
        Assert.Equal(12, precise.RuleCount); // synthetic builds have no purple unique union rule
        Assert.Null(CopySafety.BlockReason(precise, 25));

        var fitted = FilterCompiler.CompileWithinCap(builds, options, 25, out bool autoFit, "armory");
        Assert.False(autoFit);
        Assert.Equal(precise.RuleCount, fitted.RuleCount);

        // A deliberately tighter cap proves the fallback does not call an over-cap merged result safe.
        var stillTooLarge = FilterCompiler.CompileWithinCap(builds, options, 5,
            out CompileFitReport refusal, "armory");
        Assert.False(refusal.Fits);
        Assert.Empty(stillTooLarge.ImportCode);
        Assert.Contains("Pit Push", CopySafety.BlockReason(stillTooLarge, 5));
    }

    [Fact]
    public void One_empty_armory_pool_fails_closed_and_names_the_build_without_changing_single_build_safety()
    {
        var healthy = Analyze("Speedfarm", "Barbarian", "Banished Lord's Talisman",
            "Berserker's Crucible", "Strength");
        var empty = Analyze("Broken Pit Build", "Sorcerer", "Tyrael's Might",
            "Cain's Wild Lightning", "Definitely Not A Real Affix");

        var output = FilterCompiler.Compile([healthy, empty], new FilterOptions(), "armory");

        Assert.False(output.IsCopyable);
        Assert.Empty(output.ImportCode);
        Assert.Contains(output.Diagnostics, d => d.Contains("Broken Pit Build") && d.Contains("Hide the rest"));
        var copyBlock = Assert.IsType<string>(CopySafety.BlockReason(output, 25));
        var shareBlock = WitnessCardComposer.Compose(CardRequest(output));
        Assert.Contains("Broken Pit Build", copyBlock);
        Assert.True(shareBlock.IsBlocked);
        Assert.Equal(copyBlock, shareBlock.BlockReason);

        var healthySingle = FilterCompiler.Compile([healthy], new FilterOptions(), "single");
        var emptySingle = FilterCompiler.Compile([empty], new FilterOptions(), "single");
        Assert.True(healthySingle.IsCopyable);
        Assert.False(emptySingle.IsCopyable);
    }

    [Fact]
    public void Armory_witness_card_names_both_builds_and_classes_carried_by_its_code()
    {
        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(Build("Speedfarm", "Barbarian", "Banished Lord's Talisman",
            "Berserker's Crucible", "Strength"), "Maxroll");
        vm.IngestSecond(Build("Pit Push", "Sorcerer", "Tyrael's Might",
            "Cain's Wild Lightning", "Intelligence"));

        var card = Assert.IsType<WitnessCardViewModel>(vm.CurrentWitnessCard);
        Assert.Equal("Speedfarm + Pit Push", card.BuildName);
        Assert.Equal("Barbarian + Sorcerer", card.ClassName);
        Assert.Equal("Super Build · 2 builds · colors by build", card.ProvenanceChip);
        Assert.Equal(vm.ImportCode, card.ImportCode);
        Assert.Collection(card.Builds,
            first => Assert.Equal(("Speedfarm", "Barbarian"), (first.BuildName, first.ClassName)),
            second => Assert.Equal(("Pit Push", "Sorcerer"), (second.BuildName, second.ClassName)));
        Assert.Contains(card.LegendRows, row => row.ColorName == "Red");
        Assert.Contains(card.LegendRows, row => row.ColorName == "Pink");
        Assert.Contains(card.LegendRows, row => row.ColorName == "Gold");
        Assert.Contains(card.LegendRows, row => row.ColorName == "Silver");
    }

    [Fact]
    public async Task View_model_adds_union_drops_back_and_copy_guard_judges_merged_output()
    {
        var second = Build("Pit Push", "Sorcerer", "Tyrael's Might", "Cain's Wild Lightning", "Intelligence");
        var vm = new MainViewModel(startTierListFetches: false,
            resolveBuild: _ => Task.FromResult((second, "Test")));
        vm.Ingest(Build("Speedfarm", "Barbarian", "Banished Lord's Talisman",
            "Berserker's Crucible", "Strength"), "Test");
        var originalSingleCode = vm.ImportCode;
        vm.ArmorySource = "https://example.test/second";

        await vm.AddSecondBuildCommand.ExecuteAsync(null);

        Assert.True(vm.HasSecondBuild);
        Assert.Contains("2 builds ·", vm.FilterInfo);
        Assert.Contains("Banished Lord's Talisman (Speedfarm)", vm.UniquePurpleLines);
        Assert.Contains("Tyrael's Might (Pit Push)", vm.UniquePurpleLines);
        Assert.Contains(vm.TalismanSetOptions, o => o.Set.Class == "Barbarian");
        Assert.Contains(vm.TalismanSetOptions, o => o.Set.Class == "Sorcerer");
        Assert.Contains("Different classes", vm.ArmoryClassNote);

        vm.DropSecondBuildCommand.Execute(null);

        Assert.False(vm.HasSecondBuild);
        Assert.StartsWith("1 build ·", vm.FilterInfo);
        Assert.EndsWith("/ 25 rules", vm.FilterInfo);
        Assert.DoesNotContain(vm.UniquePurpleLines,
            line => line.StartsWith("Tyrael's Might", StringComparison.Ordinal));
        Assert.DoesNotContain(vm.TalismanSetOptions, o => o.Set.Class == "Sorcerer");
        Assert.Equal(originalSingleCode, vm.ImportCode);

        // A healthy first seat must not mask an empty second seat at the real copy-button boundary.
        vm.Ingest(Build("Healthy One", "Barbarian", "Banished Lord's Talisman",
            "Berserker's Crucible", "Strength"), "Test");
        vm.IngestSecond(Build("Empty Two", "Sorcerer", "Tyrael's Might",
            "Cain's Wild Lightning", "Also Not A Real Affix"));
        await vm.CopyCodeCommand.ExecuteAsync(null);
        Assert.Empty(vm.ImportCode);
        Assert.Contains("Not copied", vm.CopyConfirmation);
    }

    [Fact]
    public void Armory_drift_reports_only_the_favorite_build_and_leaves_when_it_is_dropped()
    {
        var path = Path.Combine(Path.GetTempPath(), $"medicksmight_armory_drift_{Guid.NewGuid():N}.json");
        try
        {
            const string secondUrl = "https://example.test/favorite-second";
            var oldSecond = Build("Pit Push", "Sorcerer", "Tyrael's Might",
                "Cain's Wild Lightning", "Dexterity");
            var freshSecond = Build("Pit Push", "Sorcerer", "Tyrael's Might",
                "Cain's Wild Lightning", "Intelligence");
            var store = new FavoritesStore(path);
            store.Toggle(new FavoriteEntry("second", secondUrl, "Test", null, null, "Pit Push", "Sorcerer",
                DateTime.UtcNow.AddDays(-10), DateTime.UtcNow.AddDays(-5),
                Snapshot: BuildSnapshot.Capture(oldSecond, DateTime.UtcNow.AddDays(-5))));
            var vm = new MainViewModel(startTierListFetches: false, favorites: store);
            vm.Ingest(Build("Speedfarm", "Barbarian", "Banished Lord's Talisman",
                "Berserker's Crucible", "Strength"), "Test");

            vm.IngestSecond(freshSecond, secondUrl);

            Assert.Contains("Pit Push changed", vm.BuildDriftNote);
            Assert.DoesNotContain("Speedfarm changed", vm.BuildDriftNote);
            vm.DropSecondBuildCommand.Execute(null);
            Assert.Empty(vm.BuildDriftNote);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Failed_armory_compile_does_not_advance_the_second_favorites_snapshot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"medicksmight_armory_failed_drift_{Guid.NewGuid():N}.json");
        try
        {
            const string secondUrl = "https://example.test/favorite-second-failed";
            var oldSecond = Build("Pit Push", "Sorcerer", "Tyrael's Might",
                "Cain's Wild Lightning", "Intelligence");
            var brokenSecond = Build("Pit Push", "Sorcerer", "Tyrael's Might",
                "Cain's Wild Lightning", "Not A Real Affix");
            var store = new FavoritesStore(path);
            store.Toggle(new FavoriteEntry("second-failed", secondUrl, "Test", null, null,
                "Pit Push", "Sorcerer", DateTime.UtcNow.AddDays(-10), DateTime.UtcNow.AddDays(-5),
                Snapshot: BuildSnapshot.Capture(oldSecond, DateTime.UtcNow.AddDays(-5))));
            var vm = new MainViewModel(startTierListFetches: false, favorites: store);
            vm.Ingest(Build("Speedfarm", "Barbarian", "Banished Lord's Talisman",
                "Berserker's Crucible", "Strength"), "Test");

            vm.IngestSecond(brokenSecond, secondUrl);

            var persisted = Assert.IsType<BuildSnapshot>(store.Find(secondUrl)!.Snapshot);
            Assert.False(BuildDrift.Compare(persisted, oldSecond)!.HasDrift);
            Assert.True(BuildDrift.Compare(persisted, brokenSecond)!.HasDrift);
        }
        finally { File.Delete(path); }
    }

    private static WitnessCardRequest CardRequest(FilterOutput output) => new(
        "Speedfarm", "Barbarian", "Maxroll", "Endgame", "S", output, 25,
        "https://discord.gg/test", VersionOverride: "MedicK's Might v1.2.3");

    private static ResolvedBuild Build(string name, string className, string unique,
        string talismanSet, params string[] affixes) => new(name, className,
        [new ResolvedVariant("Endgame", affixes, [unique], TalismanSets: [talismanSet])]);

    private static CompiledBuild SyntheticSlotted(string name, uint seed)
    {
        uint[] pool = [seed + 1, seed + 2, seed + 3];
        var names = pool.ToDictionary(id => id, id => $"Affix {id}");
        var slots = Enumerable.Range(0, 5)
            .Select(i => new SlotPool($"Slot {i}", [seed + 20u + (uint)i], pool))
            .ToList();
        return new CompiledBuild(name, "Barbarian", FilterColors.Red, FilterColors.Pink,
            pool, names, [], [], [], [], slots, [], []);
    }
}
