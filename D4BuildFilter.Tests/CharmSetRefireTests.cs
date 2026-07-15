using D4BuildFilter.Core;
using D4BuildFilter.WPF.ViewModels;
using Xunit;

namespace D4BuildFilter.Tests;

/// <summary>Charm-set checkboxes must follow the build. Variant checkboxes and charm checkboxes both
/// call back into Recompile, and the rule that keeps a user's manual narrowing alive across their own
/// click was also being applied when the BUILD changed — so picking a different variant left the
/// previous variant's charm sets frozen on screen. Medick hit this on a Rogue.
///
/// Recompile CLEARS and rebuilds TalismanSetOptions on every run, so a fresh option instance is proof
/// that a recompile actually happened. The guards below assert that instead of only asserting a value,
/// because a value alone survives even if the callback is deleted outright.</summary>
public class CharmSetRefireTests
{
    // Two real sets from ONE class, so switching variants genuinely changes which sets the build calls for.
    private const string BlurringBlade = "Way of the Blurring Blade";
    private const string AppliedAlchemy = "Applied Alchemy";

    private static ResolvedBuild Rogue(string name, params (string Variant, string Set)[] variants) =>
        new(name, "Rogue",
            variants.Select(v => new ResolvedVariant(
                v.Variant, ["Dexterity"], [], TalismanSets: [v.Set])).ToList());

    private static TalismanSetOption Option(MainViewModel vm, string setName) =>
        vm.TalismanSetOptions.Single(o => o.Set.Name == setName);

    private static bool Checked(MainViewModel vm, string setName) => Option(vm, setName).IsChecked;

    /// <summary>THE REPORTED BUG. Reverting the fix re-freezes the charm sets on a variant switch.</summary>
    [Fact]
    public void Unticking_a_variant_re_derives_the_charm_sets_from_the_variants_still_selected()
    {
        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(Rogue("Twisting Blades", ("Speed", BlurringBlade), ("Boss", AppliedAlchemy)), "Test");

        // Both variants load selected, so both of their sets are called for.
        Assert.True(Checked(vm, BlurringBlade));
        Assert.True(Checked(vm, AppliedAlchemy));

        // The player drops the Boss variant. Its charm set is no longer part of the build.
        vm.Variants.Single(v => v.Variant.Name == "Boss").IsSelected = false;

        Assert.True(Checked(vm, BlurringBlade));
        Assert.False(Checked(vm, AppliedAlchemy));
    }

    /// <summary>THE GUARD against "just delete the priorChecks merge": that would make a charm tick undo
    /// itself, because Recompile rebuilds the whole list on every callback. NotSame proves the rebuild
    /// really ran — asserting the value alone would still pass if the callback were deleted entirely.</summary>
    [Fact]
    public void Toggling_a_charm_set_survives_the_recompile_its_own_click_triggers()
    {
        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(Rogue("Twisting Blades", ("Speed", BlurringBlade), ("Boss", AppliedAlchemy)), "Test");

        var clicked = Option(vm, AppliedAlchemy);
        clicked.IsChecked = false;

        Assert.NotSame(clicked, Option(vm, AppliedAlchemy));   // the click really did recompile
        Assert.False(Checked(vm, AppliedAlchemy));             // and the choice survived it
        Assert.True(Checked(vm, BlurringBlade));
    }

    /// <summary>Only a BUILD change refires. Misclassifying an option callback as BuildContextChanged
    /// would silently discard the player's deliberate narrowing. NotSame proves each toggle really did
    /// recompile, so this cannot pass by the callback simply never firing.</summary>
    [Theory]
    [InlineData("unique")]
    [InlineData("tier")]
    [InlineData("title")]
    public void Toggling_an_unrelated_option_recompiles_but_does_not_reset_the_players_charm_choices(string kind)
    {
        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(Rogue("Twisting Blades", ("Speed", BlurringBlade), ("Boss", AppliedAlchemy)), "Test");
        Option(vm, AppliedAlchemy).IsChecked = false;

        var before = Option(vm, AppliedAlchemy);
        switch (kind)
        {
            case "unique": vm.UniqueItemOptions[0].IsChecked = false; break;
            case "tier": vm.OptGreaterAffixes = !vm.OptGreaterAffixes; break;
            case "title": vm.FilterTitle = "My Filter"; break;
        }

        Assert.NotSame(before, Option(vm, AppliedAlchemy));   // it did recompile...
        Assert.False(Checked(vm, AppliedAlchemy));            // ...without wiping the player's choice
    }

    /// <summary>Regression pin, NOT a test of this fix: Ingest already clears TalismanSetOptions for a
    /// new build key (the "every Barb ran Crucible" fix), so this passes before AND after the enum
    /// change. It exists so that clear can never be removed without a failure.</summary>
    [Fact]
    public void Loading_another_build_of_the_same_class_does_not_inherit_its_charm_checkboxes()
    {
        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(Rogue("Alchemy Build", ("Endgame", AppliedAlchemy)), "Test");
        Assert.True(Checked(vm, AppliedAlchemy));
        Assert.False(Checked(vm, BlurringBlade));

        vm.Ingest(Rogue("Blade Build", ("Endgame", BlurringBlade)), "Test");

        Assert.True(Checked(vm, BlurringBlade));
        Assert.False(Checked(vm, AppliedAlchemy));
    }

    /// <summary>Adding an Armory seat changes the build context. Reverting the fix leaves the first
    /// build's stale narrowing in place while a second build's sets are being offered.</summary>
    [Fact]
    public void Adding_an_armory_second_build_refires_the_charm_sets()
    {
        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(Rogue("Twisting Blades", ("Speed", BlurringBlade), ("Boss", AppliedAlchemy)), "Test");
        Option(vm, AppliedAlchemy).IsChecked = false;

        vm.IngestSecond(new ResolvedBuild("Speedfarm", "Barbarian",
            [new ResolvedVariant("Endgame", ["Strength"], [], TalismanSets: ["Berserker's Crucible"])]));

        Assert.True(Checked(vm, AppliedAlchemy));            // refired back to the build's own answer
        Assert.True(Checked(vm, "Berserker's Crucible"));    // and the second class is offered
    }

    /// <summary>THE SAFETY RULE, and the most dangerous branch to get wrong: a source that doesn't expose
    /// the build's charm sets must still default them ALL SHOWN, or 'Hide the rest' buries every charm the
    /// player owns. NotEmpty matters — Assert.All is vacuously true on an empty collection, so without it
    /// this test would "pass" while offering the player nothing at all.</summary>
    [Theory]
    [InlineData("Rogue")]      // known class, no sets detected (e.g. Mobalytics)
    [InlineData("Unknown")]    // class not identified at all — only the Generic sets are offered
    public void A_build_with_no_detected_charm_sets_still_defaults_every_offered_set_shown(string className)
    {
        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(new ResolvedBuild("Undetected Build", className,
            [new ResolvedVariant("Endgame", ["Dexterity"], [])]), "Test");

        Assert.True(vm.CharmSetsUndetected);
        Assert.NotEmpty(vm.TalismanSetOptions);
        Assert.All(vm.TalismanSetOptions, o => Assert.True(o.IsChecked));
    }

    /// <summary>Fail-open must survive the TRANSITIONS too, now that a build change re-derives from
    /// scratch: a healthy build followed by an undetected one must still open every set back up.</summary>
    [Fact]
    public void Switching_from_a_detected_build_to_an_undetected_one_re_opens_every_set()
    {
        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(Rogue("Alchemy Build", ("Endgame", AppliedAlchemy)), "Test");
        Assert.False(Checked(vm, BlurringBlade));   // narrowed to the detected set

        vm.Ingest(new ResolvedBuild("Mobalytics Rogue", "Rogue",
            [new ResolvedVariant("Endgame", ["Dexterity"], [])]), "Test");

        Assert.True(vm.CharmSetsUndetected);
        Assert.NotEmpty(vm.TalismanSetOptions);
        Assert.All(vm.TalismanSetOptions, o => Assert.True(o.IsChecked));
    }

    [Fact]
    public void Detected_build_cannot_mask_same_class_build_with_no_charm_data()
    {
        var vm = new MainViewModel(startTierListFetches: false);
        var detected = Rogue("Alchemy Build", ("Endgame", AppliedAlchemy));
        var undetected = new ResolvedBuild("Mobalytics Rogue", "Rogue",
            [new ResolvedVariant("Endgame", ["Dexterity"], [])]);

        vm.Ingest(detected, "Test", [undetected]);

        var offered = TalismanSetDatabase.ForClass("Rogue");
        Assert.True(vm.CharmSetsUndetected);
        Assert.All(offered, set =>
            Assert.True(vm.TalismanSetOptions.Single(o => o.Set.Id == set.Id).IsChecked));
        var green = FilterDecoder.Decode(vm.ImportCode).Rules
            .Single(rule => rule.Name == "Charms & Seals (Green)");
        var shownSetIds = green.Conditions.Single(condition => condition.Type == 9).Ids;
        Assert.Equal(offered.Select(set => set.Id).OrderBy(id => id), shownSetIds.OrderBy(id => id));
    }

    [Fact]
    public void Generic_set_on_one_class_cannot_mask_another_class_with_no_charm_data()
    {
        var vm = new MainViewModel(startTierListFetches: false);
        var genericDetected = new ResolvedBuild("Generic Rogue", "Rogue",
            [new ResolvedVariant("Endgame", ["Dexterity"], [], TalismanSets: ["Mastery"])]);
        var undetectedBarbarian = new ResolvedBuild("Mobalytics Barbarian", "Barbarian",
            [new ResolvedVariant("Endgame", ["Strength"], [])]);

        vm.Ingest(genericDetected, "Test", [undetectedBarbarian]);

        var barbarianSets = TalismanSetDatabase.ForClass("Barbarian");
        Assert.True(vm.CharmSetsUndetected);
        Assert.All(barbarianSets, set =>
            Assert.True(vm.TalismanSetOptions.Single(o => o.Set.Id == set.Id).IsChecked));
    }

    /// <summary>Medick's exact use case, pinned: play Death Trap whole, collect for ONE Heartseeker
    /// variant. Narrowing a group's variants must re-derive THAT build's charm sets (auto-checking what
    /// the surviving variant calls for) without disturbing the other build's. Reverting the
    /// BuildContextChanged wiring on group VariantOptions re-freezes the charm sets he reported.</summary>
    [Fact]
    public void Narrowing_one_groups_variants_rederives_only_that_builds_charm_sets()
    {
        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(Rogue("Death Trap", ("Endgame", AppliedAlchemy)), "Test",
            [Rogue("Heartseeker", ("Endgame", BlurringBlade), ("Spin 2 Win GoD", "Nilfur's Narrow Eye"))]);

        // Both Heartseeker variants selected -> both its sets called for; Death Trap's set untouched.
        Assert.True(Checked(vm, AppliedAlchemy));
        Assert.True(Checked(vm, BlurringBlade));
        Assert.True(Checked(vm, "Nilfur's Narrow Eye"));

        // Narrow Heartseeker to Spin 2 Win GoD only (the second group's checklist).
        var heartseeker = vm.VariantGroups.Single(g => g.BuildName == "Heartseeker");
        heartseeker.Variants.Single(v => v.Variant.Name == "Endgame").IsSelected = false;

        Assert.True(Checked(vm, "Nilfur's Narrow Eye"));   // GoD's set stays auto-checked
        Assert.False(Checked(vm, BlurringBlade));           // the dropped variant's set refired OFF
        Assert.True(Checked(vm, AppliedAlchemy));           // Death Trap untouched
        Assert.Contains("2 builds ·", vm.FilterInfo);       // still a 2-build compile
    }
}