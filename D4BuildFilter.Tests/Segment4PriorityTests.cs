using D4BuildFilter.Core;
using D4BuildFilter.WPF.ViewModels;
using Xunit;

namespace D4BuildFilter.Tests;

public class Segment4PriorityTests
{
    private const string BlurringBlade = "Way of the Blurring Blade";
    private const string AppliedAlchemy = "Applied Alchemy";

    /// <summary>Reverting S4 emits, labels, attributes, and shares builds in tick order after the player changes their priorities.</summary>
    [Fact]
    public async Task Emitted_and_display_order_follow_priority_not_selection_order()
    {
        var alpha = Build("Alpha", "Harlequin Crest", ("Endgame", BlurringBlade));
        var beta = Build("Beta", "Harlequin Crest", ("Endgame", AppliedAlchemy));
        var builds = new Dictionary<string, ResolvedBuild>
        {
            ["https://example.test/alpha"] = alpha,
            ["https://example.test/beta"] = beta,
        };
        var vm = new MainViewModel(startTierListFetches: false,
            resolveBuild: url => Task.FromResult((builds[url], "Test")));
        var cards = vm.BuildGroups(new TierList("Test", "https://example.test/list",
        [
            new TierBuild("Alpha", "Rogue", "S", "https://example.test/alpha"),
            new TierBuild("Beta", "Rogue", "S", "https://example.test/beta"),
        ]), "Test", "Endgame").Single().Builds;
        cards[0].IsSelected = true;
        cards[1].IsSelected = true;

        cards[1].Priority = 1;
        await vm.CompileSelectedBuildsCommand.ExecuteAsync(null);

        Assert.Equal(1, cards[1].Priority);
        Assert.Equal(2, cards[0].Priority);
        Assert.Equal(["Beta", "Alpha"], vm.VariantGroups.Select(group => group.BuildName));
        Assert.Equal([1, 2], vm.VariantGroups.Select(group => group.Priority));
        var tierNames = FilterDecoder.Decode(vm.ImportCode).Rules
            .Where(IsTier).Select(rule => rule.Name).ToList();
        Assert.Equal([
            "Beta Leg (Red)", "Alpha Leg (Pink)",
            "Beta Rare (Gold)", "Alpha Rare (Silver)"
        ], tierNames);
        Assert.Equal("Beta + Alpha", vm.FilterTitle);
        Assert.Equal(["Harlequin Crest (Beta, Alpha)"], vm.UniquePurpleLines);
        Assert.Equal(["Beta", "Alpha"],
            Assert.IsType<WitnessCardViewModel>(vm.CurrentWitnessCard).Builds.Select(build => build.BuildName));
    }

    /// <summary>Reverting S4 makes an over-cap refusal name the last-ticked build even when it is priority 1.</summary>
    [Fact]
    public void Over_cap_refusal_names_the_lowest_priority_build_not_the_last_selected()
    {
        var vm = new MainViewModel(startTierListFetches: false,
            compileWithinCap: (builds, options, _, label, title) =>
            {
                var output = FilterCompiler.CompileWithinCap(builds, options, 10,
                    out CompileFitReport fit, label, title);
                return (output, fit);
            });
        vm.Ingest(Build("Alpha", "Harlequin Crest", ("Endgame", BlurringBlade)), "Test",
        [
            Build("Beta", "Tyrael's Might", ("Endgame", AppliedAlchemy)),
            Build("Gamma", "Heir of Perdition", ("Endgame", BlurringBlade)),
        ]);

        vm.VariantGroups.Single(group => group.BuildName == "Alpha").Priority = 3;

        Assert.Empty(vm.ImportCode);
        Assert.Contains("Deselect build 'Alpha'", vm.CapWarning);
        Assert.DoesNotContain("Deselect build 'Gamma'", vm.CapWarning);
    }

    /// <summary>Reverting S4 permits duplicate/unstable priorities or changes output after an identical swap and recompile sequence.</summary>
    [Fact]
    public void Occupied_priority_swaps_without_duplicates_and_recompiles_deterministically()
    {
        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(Build("Alpha", "Harlequin Crest", ("Endgame", BlurringBlade)), "Test",
        [
            Build("Beta", "Tyrael's Might", ("Endgame", AppliedAlchemy)),
            Build("Gamma", "Heir of Perdition", ("Endgame", BlurringBlade)),
        ]);
        var original = vm.ImportCode;
        var alpha = vm.VariantGroups.Single(group => group.BuildName == "Alpha");
        var gamma = vm.VariantGroups.Single(group => group.BuildName == "Gamma");

        gamma.Priority = 1;
        var swapped = vm.ImportCode;

        Assert.Equal(["Gamma", "Beta", "Alpha"], vm.VariantGroups.Select(group => group.BuildName));
        Assert.Equal([1, 2, 3], vm.VariantGroups.Select(group => group.Priority));
        Assert.Equal(3, vm.VariantGroups.Select(group => group.Priority).Distinct().Count());

        alpha.Priority = 1;
        Assert.Equal(original, vm.ImportCode);
        gamma.Priority = 1;
        Assert.Equal(swapped, vm.ImportCode);
        vm.OptGreaterAffixes = false;
        vm.OptGreaterAffixes = true;
        Assert.Equal(swapped, vm.ImportCode);
    }

    /// <summary>Reverting S4 recreates or resets variant ticks, freezes manual charm choices, or reorders a dirty title during a live swap.</summary>
    [Fact]
    public void Live_reorder_preserves_variant_ticks_rederives_charms_and_only_reorders_a_clean_title()
    {
        var alpha = Build("Alpha", "Harlequin Crest",
            ("Speed", BlurringBlade), ("Boss", AppliedAlchemy));
        var beta = Build("Beta", "Tyrael's Might", ("Endgame", "Nilfur's Narrow Eye"));
        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(alpha, "Test", [beta]);
        var alphaGroup = vm.VariantGroups.Single(group => group.BuildName == "Alpha");
        var boss = alphaGroup.Variants.Single(option => option.Variant.Name == "Boss");
        boss.IsSelected = false;
        var manuallyNarrowed = Option(vm, BlurringBlade);
        manuallyNarrowed.IsChecked = false;

        vm.VariantGroups.Single(group => group.BuildName == "Beta").Priority = 1;

        Assert.Same(alphaGroup, vm.VariantGroups.Single(group => group.BuildName == "Alpha"));
        Assert.Same(boss, alphaGroup.Variants.Single(option => option.Variant.Name == "Boss"));
        Assert.False(boss.IsSelected);
        Assert.NotSame(manuallyNarrowed, Option(vm, BlurringBlade));
        Assert.True(Option(vm, BlurringBlade).IsChecked);
        Assert.False(Option(vm, AppliedAlchemy).IsChecked);
        Assert.True(Option(vm, "Nilfur's Narrow Eye").IsChecked);
        Assert.Equal("Beta + Alpha", vm.FilterTitle);

        vm.FilterTitle = "My Custom Order";
        alphaGroup.Priority = 1;
        Assert.Equal("My Custom Order", vm.FilterTitle);
    }

    /// <summary>Reverting S4 leaves an Armory-added build without the next free priority or outside the shared reorder path.</summary>
    [Fact]
    public void Armory_added_build_takes_the_next_free_priority()
    {
        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(Build("Alpha", "Harlequin Crest", ("Endgame", BlurringBlade)), "Test");

        vm.IngestSecond(Build("Beta", "Tyrael's Might", ("Endgame", AppliedAlchemy)));

        Assert.Equal([1, 2], vm.VariantGroups.Select(group => group.Priority));
        vm.VariantGroups.Single(group => group.BuildName == "Beta").Priority = 1;
        Assert.Equal(["Beta", "Alpha"], vm.VariantGroups.Select(group => group.BuildName));
    }

    /// <summary>Reverting this fix stores Alpha's URL and snapshot under Beta's result-page name after a priority swap.</summary>
    [Fact]
    public void Result_page_favorite_after_priority_swap_saves_one_coherent_build_identity()
    {
        var path = Path.Combine(Path.GetTempPath(), $"favorite-priority-{Guid.NewGuid():N}.json");
        try
        {
            var store = new FavoritesStore(path);
            var alpha = Build("Alpha", "Harlequin Crest", ("Endgame", BlurringBlade)) with
            {
                Source = "Test",
                SourceUrl = "https://example.test/alpha",
            };
            var beta = Build("Beta", "Tyrael's Might", ("Endgame", AppliedAlchemy)) with
            {
                Source = "Test",
                SourceUrl = "https://example.test/beta",
            };
            var vm = new MainViewModel(startTierListFetches: false, favorites: store);
            vm.Ingest(alpha, "Test", [beta]);

            vm.VariantGroups.Single(group => group.BuildName == "Beta").Priority = 1;
            vm.ToggleFavoriteCurrentCommand.Execute(null);

            var saved = Assert.Single(store.All);
            Assert.Equal("https://example.test/beta", saved.Url);
            Assert.Equal("Beta", saved.Name);
            Assert.Equal("Rogue", saved.ClassName);
            Assert.Equal("Beta", saved.Snapshot?.BuildName);
            Assert.Equal("Rogue", saved.Snapshot?.ClassName);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static TalismanSetOption Option(MainViewModel vm, string setName) =>
        vm.TalismanSetOptions.Single(option => option.Set.Name == setName);

    private static ResolvedBuild Build(string name, string unique,
        params (string Variant, string Set)[] variants) => new(name, "Rogue",
            variants.Select(variant => new ResolvedVariant(
                variant.Variant,
                ["Dexterity", "Critical Strike Chance", "Maximum Life"],
                [unique],
                TalismanSets: [variant.Set])).ToList());

    private static bool IsTier(DecodedRule rule) => rule.Conditions.Any(condition => condition.Type == 6)
        && rule.Conditions.Any(condition => condition.Type == 1);
}
