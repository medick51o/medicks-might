using D4BuildFilter.Core;
using D4BuildFilter.WPF.ViewModels;
using Xunit;

namespace D4BuildFilter.Tests;

public class Segment3CharmRecheckTests
{
    private const string BlurringBlade = "Way of the Blurring Blade";
    private const string AppliedAlchemy = "Applied Alchemy";

    private static ResolvedBuild Build(string name, string className,
        params (string Variant, string? Set)[] variants) =>
        new(name, className, variants.Select(v => new ResolvedVariant(
            v.Variant, [className == "Barbarian" ? "Strength" : "Dexterity"], [],
            TalismanSets: v.Set is null ? null : [v.Set])).ToList());

    private static TalismanSetOption Option(MainViewModel vm, string setName) =>
        vm.TalismanSetOptions.Single(o => o.Set.Name == setName);

    /// <summary>Reverting the command leaves the manually narrowed option in place instead of
    /// rebuilding defaults from the variants checked on screen.</summary>
    [Fact]
    public void Recheck_rederives_defaults_from_current_variant_ticks_with_fresh_options()
    {
        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(Build("Twisting Blades", "Rogue",
            ("Speed", BlurringBlade), ("Boss", AppliedAlchemy)), "Test");

        vm.Variants.Single(v => v.Variant.Name == "Boss").IsSelected = false;
        Option(vm, BlurringBlade).IsChecked = false;
        var manuallyNarrowed = Option(vm, BlurringBlade);

        vm.RecheckCharmSetsCommand.Execute(null);

        Assert.NotSame(manuallyNarrowed, Option(vm, BlurringBlade));
        Assert.True(Option(vm, BlurringBlade).IsChecked);
        Assert.False(Option(vm, AppliedAlchemy).IsChecked);
    }

    /// <summary>Reverting the command leaves both builds manually narrowed and can leave an
    /// undetected build's charms hidden instead of restoring its fail-open all-shown state.</summary>
    [Fact]
    public void Multi_build_recheck_rederives_each_group_and_keeps_undetected_build_fail_open()
    {
        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(Build("Rogue Build", "Rogue",
                ("Speed", BlurringBlade), ("Boss", AppliedAlchemy)), "Test",
            [Build("Barbarian Build", "Barbarian",
                ("Detected", "Berserker's Crucible"), ("Undetected", null))]);

        vm.VariantGroups.Single(g => g.BuildName == "Rogue Build")
            .Variants.Single(v => v.Variant.Name == "Speed").IsSelected = false;
        vm.VariantGroups.Single(g => g.BuildName == "Barbarian Build")
            .Variants.Single(v => v.Variant.Name == "Detected").IsSelected = false;
        Option(vm, AppliedAlchemy).IsChecked = false;
        var narrowedBarbarian = vm.TalismanSetOptions.First(o => o.Set.Class == "Barbarian");
        narrowedBarbarian.IsChecked = false;

        vm.RecheckCharmSetsCommand.Execute(null);

        Assert.False(Option(vm, BlurringBlade).IsChecked);
        Assert.True(Option(vm, AppliedAlchemy).IsChecked);
        Assert.True(vm.CharmSetsUndetected);
        var barbarianOptions = vm.TalismanSetOptions.Where(o => o.Set.Class == "Barbarian").ToList();
        Assert.NotEmpty(barbarianOptions);
        Assert.All(barbarianOptions, option => Assert.True(option.IsChecked));
    }

    /// <summary>Reverting the scoped command or routing it through reset logic disturbs variant,
    /// unique, title, or other option state while refreshing charm defaults.</summary>
    [Fact]
    public void Recheck_does_not_disturb_variants_uniques_title_or_other_user_state()
    {
        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(Build("Twisting Blades", "Rogue",
            ("Speed", BlurringBlade), ("Boss", AppliedAlchemy)), "Test");
        vm.Variants.Single(v => v.Variant.Name == "Boss").IsSelected = false;
        Option(vm, BlurringBlade).IsChecked = false;
        vm.UniqueItemOptions[0].IsChecked = false;
        vm.UniqueCharmOptions[0].IsChecked = false;
        vm.FilterTitle = "My Current Filter";
        vm.OptCubeBases = true;
        vm.OptPerSlot = false;
        vm.UniquesExpanded = false;

        var variants = vm.Variants.Select(v => (Option: v, v.IsSelected)).ToList();
        var unique = vm.UniqueItemOptions[0];
        var uniqueCharm = vm.UniqueCharmOptions[0];

        vm.RecheckCharmSetsCommand.Execute(null);

        Assert.All(variants, before =>
        {
            Assert.Contains(before.Option, vm.Variants);
            Assert.Equal(before.IsSelected, before.Option.IsSelected);
        });
        Assert.Same(unique, vm.UniqueItemOptions[0]);
        Assert.False(unique.IsChecked);
        Assert.Same(uniqueCharm, vm.UniqueCharmOptions[0]);
        Assert.False(uniqueCharm.IsChecked);
        Assert.Equal("My Current Filter", vm.FilterTitle);
        Assert.True(vm.OptCubeBases);
        Assert.False(vm.OptPerSlot);
        Assert.False(vm.UniquesExpanded);
    }
}
