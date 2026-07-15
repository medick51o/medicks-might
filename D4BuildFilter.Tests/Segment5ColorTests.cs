using D4BuildFilter.Core;
using D4BuildFilter.WPF.ViewModels;
using Xunit;

namespace D4BuildFilter.Tests;

public class Segment5ColorTests
{
    /// <summary>Reverting S5 either hides the pickers or lets their initial values drift from the byte-pinned ruled scheme.</summary>
    [Fact]
    public void Two_build_pickers_show_the_ruled_defaults()
    {
        var vm = TwoBuildViewModel();

        Assert.All(vm.VariantGroups, group => Assert.True(group.HasColorPickers));
        Assert.All(vm.VariantGroups, group => Assert.Equal(18, group.Palette.Count));
        Assert.Equal(["Red", "Pink"], vm.VariantGroups.Select(group => group.ChaseColor.FullName));
        Assert.Equal(["Gold", "Silver"], vm.VariantGroups.Select(group => group.KeeperColor.FullName));

        var tiers = TierRules(vm.ImportCode);
        Assert.Equal([FilterColors.Red, FilterColors.Pink, FilterColors.Gold, FilterColors.Silver],
            tiers.Select(rule => rule.Color));
    }

    /// <summary>Reverting S5 leaves one or more public surfaces advertising a color different from the emitted rule.</summary>
    [Fact]
    public void Custom_chase_color_flows_through_rule_suffix_legend_and_witness_card()
    {
        var vm = TwoBuildViewModel();

        vm.VariantGroups[0].ChaseColor = Palette("Turquoise");

        var chase = Assert.Single(TierRules(vm.ImportCode), rule =>
            rule.Name.StartsWith("Alpha Leg", StringComparison.Ordinal));
        Assert.Equal(FilterColors.Turquoise, chase.Color);
        Assert.EndsWith("(Turq)", chase.Name);
        Assert.Contains("Turquoise", vm.BuildTierLegend);
        Assert.Contains("Alpha", vm.BuildTierLegend);

        var card = Assert.IsType<WitnessCardViewModel>(vm.CurrentWitnessCard);
        Assert.Contains("custom colors", card.ProvenanceChip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(card.LegendRows, row => row.ColorName == "Turquoise"
            && row.Label.Contains("Alpha", StringComparison.Ordinal));
    }

    /// <summary>Reverting S5 either misses a warning class, warns on defaults, or lets cosmetic advice block copy/share.</summary>
    [Fact]
    public void Color_warnings_are_deduplicated_advisories_and_defaults_never_warn()
    {
        var vm = TwoBuildViewModel();
        Assert.Empty(vm.SuperBuildAdvisory);
        Assert.Empty(vm.CapWarning);
        Assert.True(vm.CanShareWitnessCard);

        vm.VariantGroups[1].ChaseColor = Palette("Blue");
        Assert.Single(LinesContaining(vm.SuperBuildAdvisory, "Greater Affixes"));
        Assert.Contains("Beta chase tier", vm.SuperBuildAdvisory);
        Assert.Contains("Blue", vm.SuperBuildAdvisory);
        Assert.Empty(vm.CapWarning);
        Assert.True(vm.CanShareWitnessCard);

        vm.VariantGroups[0].ChaseColor = Palette("Turquoise");
        vm.VariantGroups[0].KeeperColor = Palette("Turquoise");
        Assert.Single(LinesContaining(vm.SuperBuildAdvisory, "Alpha chase tier", "Alpha keeper tier"));

        vm.VariantGroups[0].ChaseColor = Palette("Black");
        vm.VariantGroups[1].KeeperColor = Palette("Black");
        Assert.Single(LinesContaining(vm.SuperBuildAdvisory, "hard to see on Sanctuary's ground"));
        Assert.DoesNotContain("hard to see", vm.CapWarning, StringComparison.OrdinalIgnoreCase);
        Assert.True(vm.CanShareWitnessCard);
    }

    /// <summary>Reverting S5 keys colors by position, loses them at a 2-to-3 crossing, or leaks them after drop/restart.</summary>
    [Fact]
    public void Custom_color_follows_identity_across_priority_and_count_but_clears_on_drop_and_restart()
    {
        var alpha = Build("Alpha", "https://example.test/alpha");
        var beta = Build("Beta", "https://example.test/beta");
        var gamma = Build("Gamma", "https://example.test/gamma");

        var twins = new MainViewModel(startTierListFetches: false);
        twins.Ingest(Build("Mirror", "https://example.test/mirror-one"), "Test",
            [Build("Mirror", "https://example.test/mirror-two")]);
        twins.VariantGroups.Single(group => group.Build.SourceUrl.EndsWith("mirror-one", StringComparison.Ordinal))
            .ChaseColor = Palette("Light Blue");
        Assert.Equal("Pink", twins.VariantGroups.Single(group =>
            group.Build.SourceUrl.EndsWith("mirror-two", StringComparison.Ordinal)).ChaseColor.FullName);

        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(alpha, "Test", [beta]);
        vm.VariantGroups.Single(group => group.BuildName == "Alpha").ChaseColor = Palette("Turquoise");

        vm.VariantGroups.Single(group => group.BuildName == "Alpha").Priority = 2;
        Assert.Equal("Turquoise", vm.VariantGroups.Single(group => group.BuildName == "Alpha").ChaseColor.FullName);

        vm.Ingest(alpha, "Test", [beta, gamma]);
        Assert.Equal("Turquoise", vm.VariantGroups.Single(group => group.BuildName == "Alpha").ChaseColor.FullName);

        var armory = new MainViewModel(startTierListFetches: false);
        armory.Ingest(alpha, "Test");
        armory.IngestSecond(beta, beta.SourceUrl);
        var betaGroup = armory.VariantGroups.Single(group => group.BuildName == "Beta");
        betaGroup.ChaseColor = Palette("Sky Blue");
        betaGroup.Priority = 1;
        armory.DropSecondBuildCommand.Execute(null);
        Assert.Equal("Alpha", Assert.Single(armory.VariantGroups).BuildName);
        armory.IngestSecond(beta, beta.SourceUrl);
        Assert.Equal("Pink", armory.VariantGroups.Single(group => group.BuildName == "Beta").ChaseColor.FullName);

        vm.VariantGroups.Single(group => group.BuildName == "Alpha").ChaseColor = Palette("Light Green");
        vm.RestartCommand.Execute(null);
        vm.Ingest(alpha, "Test", [beta]);
        Assert.Equal("Red", vm.VariantGroups.Single(group => group.BuildName == "Alpha").ChaseColor.FullName);
    }

    /// <summary>Reverting this fix lets Armory replacement retain the dropped build's custom color for a later re-add.</summary>
    [Fact]
    public void Armory_replacement_cleans_the_replaced_builds_custom_colors()
    {
        var alpha = Build("Alpha", "https://example.test/alpha");
        var beta = Build("Beta", "https://example.test/beta");
        var gamma = Build("Gamma", "https://example.test/gamma");
        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(alpha, "Test");
        vm.IngestSecond(beta, beta.SourceUrl);
        vm.VariantGroups.Single(group => group.BuildName == "Beta").ChaseColor = Palette("Sky Blue");

        vm.IngestSecond(gamma, gamma.SourceUrl);
        vm.DropSecondBuildCommand.Execute(null);
        vm.IngestSecond(beta, beta.SourceUrl);

        Assert.Equal("Pink", vm.VariantGroups.Single(group => group.BuildName == "Beta").ChaseColor.FullName);
        Assert.Equal("Silver", vm.VariantGroups.Single(group => group.BuildName == "Beta").KeeperColor.FullName);
    }

    private static MainViewModel TwoBuildViewModel()
    {
        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(Build("Alpha", "https://example.test/alpha"), "Test",
            [Build("Beta", "https://example.test/beta")]);
        return vm;
    }

    private static ResolvedBuild Build(string name, string url) => new(name, "Rogue",
        [new ResolvedVariant("Endgame", ["Dexterity", "Critical Strike Chance", "Maximum Life"], [])])
        { Source = "Test", SourceUrl = url };

    private static FilterColorEntry Palette(string name) =>
        FilterColors.Registry.Single(entry => entry.FullName == name);

    private static List<DecodedRule> TierRules(string importCode) => FilterDecoder.Decode(importCode).Rules
        .Where(rule => rule.Conditions.Any(condition => condition.Type == 6)
            && rule.Conditions.Any(condition => condition.Type == 1))
        .ToList();

    private static IReadOnlyList<string> LinesContaining(string text, params string[] fragments) => text
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Where(line => fragments.All(fragment => line.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        .ToList();
}
