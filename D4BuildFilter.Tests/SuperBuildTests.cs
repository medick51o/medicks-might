using System.Security.Cryptography;
using System.Text;
using D4BuildFilter.Core;
using D4BuildFilter.WPF.ViewModels;
using Xunit;

namespace D4BuildFilter.Tests;

public class SuperBuildTests
{
    /// <summary>Reverting this fix makes an enabled Leveling control promise a 2+ rescue that the multi-build code never emits.</summary>
    [Fact]
    public void Multi_build_disables_leveling_and_two_affix_rares_fall_to_the_advertised_hide_rule()
    {
        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(Build("Heartseeker", "Rogue", "Dexterity", "Critical Strike Chance", "Vulnerable Damage Multiplier"), "Test");
        vm.OptLeveling = true;

        vm.IngestSecond(Build("Death Trap", "Rogue", "Maximum Life", "Cooldown Reduction", "Movement Speed"));

        Assert.False(vm.MultiBuildLevelingEnabled);
        Assert.False(vm.OptLeveling);
        var rules = FilterDecoder.Decode(vm.ImportCode).Rules;
        Assert.DoesNotContain(rules, rule => rule.Name.Contains("Leveling", StringComparison.Ordinal));
        Assert.DoesNotContain(rules.Where(rule => rule.Visibility == (int)Visibility.Recolor), rule =>
            rule.Conditions.Any(condition => condition.Type == 1 && condition.MaskOrCount == Rarity.Rare)
            && rule.Conditions.Any(condition => condition.Type == 6 && condition.MaskOrCount == 2));
        Assert.Contains(rules, rule => rule.Visibility == (int)Visibility.HideAll);
    }

    [Fact]
    public void Split_affixes_never_cross_build_threshold_and_two_builds_use_the_four_ruled_colors()
    {
        var firstResolved = Build("Heartseeker", "Rogue", "Dexterity",
            "Critical Strike Chance", "Vulnerable Damage Multiplier");
        var secondResolved = Build("Death Trap", "Rogue", "Maximum Life",
            "Cooldown Reduction", "Movement Speed");
        var first = FilterCompiler.Analyze(firstResolved, FilterColors.Red, FilterColors.Pink);
        var second = FilterCompiler.Analyze(secondResolved, FilterColors.Red, FilterColors.Pink);
        var splitItem = first.Pool.Take(2).Concat(second.Pool.Take(1)).ToHashSet();

        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(firstResolved, "Test");
        vm.IngestSecond(secondResolved);
        var tierRules = FilterDecoder.Decode(vm.ImportCode).Rules
            .Where(rule => rule.Conditions.Any(c => c.Type == 6)
                && rule.Conditions.Any(c => c.Type == 1))
            .ToList();

        Assert.Equal(4, tierRules.Count);
        Assert.Equal([FilterColors.Red, FilterColors.Pink, FilterColors.Gold, FilterColors.Silver],
            tierRules.Select(rule => rule.Color));
        Assert.DoesNotContain(tierRules, rule => AffixTierMatches(rule, splitItem));
    }

    [Fact]
    public void Four_build_fixed_layout_fits_without_trade_down_and_tight_cap_refuses_honestly()
    {
        var builds = Enumerable.Range(0, 4)
            .Select(i => SyntheticSlotted($"Build {i + 1}", (uint)(100 * (i + 1))))
            .ToList();
        var options = new FilterOptions { PerSlotRules = true };

        var fitted = FilterCompiler.CompileWithinCap(builds, options, 25,
            out CompileFitReport fit, "super");

        Assert.True(fit.Fits);
        Assert.Equal(16, fit.RequestedRuleCount); // synthetic builds have no purple unique union rule
        Assert.Equal(16, fitted.RuleCount);
        Assert.Empty(fit.DisabledFeatures);
        Assert.Equal("", fit.Describe(4, 25));

        var refused = FilterCompiler.CompileWithinCap(builds, options, 10,
            out CompileFitReport refusal, "super");

        Assert.False(refusal.Fits);
        Assert.Equal("Build 4", refusal.BuildToDrop);
        Assert.Empty(refusal.DisabledFeatures); // fixed Super Build tiers are never traded away
        Assert.Empty(refused.ImportCode);
        Assert.False(refused.IsCopyable);
        Assert.Contains("Build 4", Assert.IsType<string>(CopySafety.BlockReason(refused, 10)));
        var share = WitnessCardComposer.Compose(new WitnessCardRequest(
            "Build 1", "Rogue", "Test", null, null, refused, 10, "https://example.test"));
        Assert.True(share.IsBlocked);
        Assert.Contains("Build 4", share.BlockReason);
    }

    [Fact]
    public void Fifth_tier_card_selection_is_refused_with_a_reason()
    {
        var vm = new MainViewModel(startTierListFetches: false);
        var cards = vm.BuildGroups(new TierList("Maxroll", "https://example.test/list",
            Enumerable.Range(1, 5)
                .Select(i => new TierBuild($"Build {i}", "Rogue", "S", $"https://example.test/{i}"))
                .ToList()), "Maxroll", "Endgame").Single().Builds;

        foreach (var card in cards.Take(4)) card.IsSelected = true;
        cards[4].IsSelected = true;

        Assert.Equal(4, vm.SelectedBuildCount);
        Assert.False(cards[4].IsSelected);
        Assert.Contains("up to 4 builds", vm.SuperBuildSelectionStatus);
        Assert.Contains("Build 5", vm.SuperBuildSelectionStatus);
        Assert.Contains("4 builds uses colors by tier: Red marks every build's legendaries and Gold marks every build's rares; rule names identify the build.",
            vm.SuperBuildAdvisory);
    }

    [Fact]
    public async Task Three_selected_cards_compile_union_uniques_and_offer_every_class_charm_set()
    {
        var resolved = new Dictionary<string, ResolvedBuild>
        {
            ["https://example.test/rogue"] = BuildWithLoot("Heartseeker", "Rogue",
                "Harlequin Crest", "Nilfur's Narrow Eye", "Dexterity", "Critical Strike Chance", "Vulnerable Damage Multiplier"),
            ["https://example.test/sorc"] = BuildWithLoot("Lightning", "Sorcerer",
                "Tyrael's Might", "Cain's Wild Lightning", "Intelligence", "Cooldown Reduction", "Maximum Life"),
            ["https://example.test/barb"] = BuildWithLoot("Berserker", "Barbarian",
                "Banished Lord's Talisman", "Berserker's Crucible", "Strength", "Armor", "Maximum Life"),
        };
        var vm = new MainViewModel(startTierListFetches: false,
            resolveBuild: url => Task.FromResult((resolved[url], "Test")));
        TierBuildVM Card(string source, string url, string name, string className) =>
            vm.BuildGroups(new TierList(source, "https://example.test/list",
                [new TierBuild(name, className, "S", url)]), source, "Endgame").Single().Builds.Single();
        var cards = new[]
        {
            Card("Maxroll", "https://example.test/rogue", "Heartseeker", "Rogue"),
            Card("Mobalytics", "https://example.test/sorc", "Lightning", "Sorcerer"),
            Card("Maxroll", "https://example.test/barb", "Berserker", "Barbarian"),
        };
        foreach (var card in cards) card.IsSelected = true;

        await vm.CompileSelectedBuildsCommand.ExecuteAsync(null);

        Assert.Contains("3 builds ·", vm.FilterInfo);
        Assert.Equal([
            "Harlequin Crest (HrtSkr)",
            "Tyrael's Might (Lightning)",
            "Banished Lord's Talisman (Berserker)"
        ], vm.UniquePurpleLines);
        Assert.All(new[] { "Rogue", "Sorcerer", "Barbarian" },
            className => Assert.Contains(vm.TalismanSetOptions, o => o.Set.Class == className));
        Assert.Contains("different sites", vm.SuperBuildAdvisory);
        Assert.Contains("different classes", vm.SuperBuildAdvisory);
        var tierRules = FilterDecoder.Decode(vm.ImportCode).Rules
            .Where(rule => rule.Conditions.Any(c => c.Type == 6)
                && rule.Conditions.Any(c => c.Type == 1))
            .ToList();
        Assert.Equal(6, tierRules.Count);
        Assert.All(tierRules.Where(r => r.Conditions.Single(c => c.Type == 1).MaskOrCount == Rarity.Legendary),
            rule => Assert.Equal(FilterColors.Red, rule.Color));
        Assert.All(tierRules.Where(r => r.Conditions.Single(c => c.Type == 1).MaskOrCount == Rarity.Rare),
            rule => Assert.Equal(FilterColors.Gold, rule.Color));
        var witness = Assert.IsType<WitnessCardViewModel>(vm.CurrentWitnessCard);
        Assert.Equal(3, witness.Builds.Count);
        Assert.Equal("Super Build · 3 builds · colors by tier", witness.ProvenanceChip);
    }

    [Fact]
    public void Each_super_build_exposes_variants_and_narrowing_build_b_changes_only_build_b_rules()
    {
        var first = BuildWithVariants("Heartseeker", "Rogue",
            ("Endgame", ["Dexterity", "Critical Strike Chance", "Vulnerable Damage Multiplier"]),
            ("Pit", ["Maximum Life", "Armor", "Movement Speed"]));
        var second = BuildWithVariants("Death Trap", "Rogue",
            ("Endgame", ["Cooldown Reduction", "Maximum Life", "Movement Speed"]),
            ("Boss", ["Dexterity", "Armor", "Resistance to All Elements"]));
        var vm = new MainViewModel(startTierListFetches: false);

        vm.Ingest(first, "Test", [second]);

        Assert.Equal(["Heartseeker", "Death Trap"], vm.VariantGroups.Select(g => g.BuildName));
        Assert.All(vm.VariantGroups, group => Assert.Contains(group.BuildName, group.Header));
        Assert.All(vm.VariantGroups, group => Assert.Equal(2, group.Variants.Count));
        var before = TierAffixIdsByColor(vm.ImportCode);

        vm.VariantGroups[1].Variants.Single(v => v.Variant.Name == "Boss").IsSelected = false;

        var after = TierAffixIdsByColor(vm.ImportCode);
        Assert.Equal(before[FilterColors.Red].ToArray(), after[FilterColors.Red].ToArray());
        Assert.Equal(before[FilterColors.Gold].ToArray(), after[FilterColors.Gold].ToArray());
        Assert.False(before[FilterColors.Pink].SequenceEqual(after[FilterColors.Pink]));
        Assert.False(before[FilterColors.Silver].SequenceEqual(after[FilterColors.Silver]));
    }

    [Fact]
    public void Leveling_variants_default_off_for_every_build_in_a_super_build()
    {
        var first = BuildWithVariants("Heartseeker", "Rogue",
            ("Endgame", ["Dexterity", "Critical Strike Chance", "Vulnerable Damage Multiplier"]),
            ("Leveling 1-70", ["Armor"]));
        var second = BuildWithVariants("Death Trap", "Rogue",
            ("Endgame", ["Cooldown Reduction", "Maximum Life", "Movement Speed"]),
            ("Leveling Starter", ["Strength"]));
        var vm = new MainViewModel(startTierListFetches: false);

        vm.Ingest(first, "Test", [second]);

        Assert.All(vm.VariantGroups,
            group => Assert.False(group.Variants.Single(v => v.Variant.Name.Contains("Leveling")).IsSelected));
        var emittedIds = TierAffixIdsByColor(vm.ImportCode).Values.SelectMany(ids => ids).ToHashSet();
        var levelingIds = new[] { first, second }
            .SelectMany(build => FilterCompiler.Analyze(build with
            {
                Variants = build.Variants.Where(v => v.Name.Contains("Leveling")).ToList()
            }, FilterColors.Red, FilterColors.Pink).Pool)
            .ToList();
        Assert.DoesNotContain(levelingIds, emittedIds.Contains);
    }

    [Fact]
    public void Unchecking_build_bs_last_variant_retires_code_and_names_build_b()
    {
        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(Build("Heartseeker", "Rogue", "Dexterity", "Critical Strike Chance", "Vulnerable Damage Multiplier"),
            "Test", [Build("Death Trap", "Rogue", "Maximum Life", "Cooldown Reduction", "Movement Speed")]);

        vm.VariantGroups[1].Variants.Single().IsSelected = false;

        Assert.Empty(vm.ImportCode);
        Assert.Empty(vm.FilterInfo);
        Assert.Null(vm.CurrentWitnessCard);
        Assert.Contains("at least one variant", vm.StatusMessage);
        Assert.Contains("Death Trap", vm.StatusMessage);
    }

    /// <summary>Reverting this fix leaves active color pickers and legend rows visible for a build whose tiers were skipped.</summary>
    [Fact]
    public void Empty_narrowed_pool_marks_that_builds_legend_and_pickers_not_emitted()
    {
        var narrowed = BuildWithVariants("Narrowed", "Rogue",
            ("Mapped", ["Dexterity", "Critical Strike Chance", "Maximum Life"]),
            ("Unmapped", ["Affix that cannot be mapped anywhere"]));
        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(narrowed, "Test",
            [Build("Heartseeker", "Rogue", "Dexterity", "Critical Strike Chance", "Maximum Life")]);
        vm.VariantGroups.Single(item => item.BuildName == "Narrowed")
            .Variants.Single(option => option.Variant.Name == "Mapped").IsSelected = false;

        var group = vm.VariantGroups.Single(item => item.BuildName == "Narrowed");
        Assert.False(group.TierColorsEnabled);
        Assert.Contains("not emitted", group.TierEmissionNote, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Narrowed", vm.BuildTierLegend, StringComparison.Ordinal);
        Assert.Contains("not emitted", vm.BuildTierLegend, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Narrowed chase", vm.BuildTierLegend, StringComparison.Ordinal);
    }

    [Fact]
    public void Card_click_with_an_active_selection_toggles_selection_without_loading_one_build()
    {
        var resolveCalls = 0;
        var vm = new MainViewModel(startTierListFetches: false,
            resolveBuild: _ =>
            {
                resolveCalls++;
                throw new InvalidOperationException("A selected card click must not resolve a single build.");
            });
        var cards = vm.BuildGroups(new TierList("Maxroll", "https://example.test/list",
        [
            new TierBuild("Heartseeker", "Rogue", "S", "https://example.test/heartseeker"),
            new TierBuild("Death Trap", "Rogue", "S", "https://example.test/death-trap")
        ]), "Maxroll", "Endgame").Single().Builds;
        cards[0].IsSelected = true;

        cards[1].LoadCommand.Execute(null);

        Assert.Equal(0, resolveCalls);
        Assert.Equal(AppState.Input, vm.State);
        Assert.True(cards[1].IsSelected);
        Assert.Equal(2, vm.SelectedBuildCount);
        Assert.Contains("Compile 2 builds", vm.SuperBuildSelectionStatus);
    }

    [Fact]
    public void Selection_survives_tier_tab_projection_and_refresh_rebuild()
    {
        var vm = new MainViewModel(startTierListFetches: false);
        var endgame = new TierList("Maxroll", "https://example.test/endgame",
        [
            new TierBuild("Heartseeker", "Rogue", "S", "https://example.test/heartseeker"),
            new TierBuild("Death Trap", "Rogue", "S", "https://example.test/death-trap")
        ]);
        var selectedCards = vm.BuildGroups(endgame, "Maxroll", "Endgame").Single().Builds;
        foreach (var card in selectedCards) card.IsSelected = true;

        var otherTab = vm.BuildGroups(new TierList("Maxroll", "https://example.test/leveling",
            [new TierBuild("Starter", "Rogue", "A", "https://example.test/starter")]),
            "Maxroll", "Leveling");
        var refreshedCards = vm.BuildGroups(endgame, "Maxroll", "Endgame").Single().Builds;

        Assert.Single(otherTab);
        Assert.Equal(2, vm.SelectedBuildCount);
        Assert.All(refreshedCards, card => Assert.True(card.IsSelected));
    }

    [Fact]
    public void Single_build_compile_bytes_are_pinned_to_the_pre_super_build_output()
    {
        var build = FilterCompiler.Analyze(Build("Heartseeker", "Rogue", "Dexterity",
            "Critical Strike Chance", "Vulnerable Damage Multiplier"),
            FilterColors.Red, FilterColors.Pink);

        var output = FilterCompiler.Compile([build], new FilterOptions(), "Filter", "Pinned Single");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output.ImportCode)));

        Assert.Equal("1B44D5072544C40E8FA97C1B6D6BC28A0E799A30A56D28FEA3D12B740950B935", hash);
    }

    [Fact]
    public void Missing_data_note_aggregates_all_four_selected_builds()
    {
        var builds = new[]
        {
            new ResolvedBuild("One", "Rogue", [new ResolvedVariant("v", ["Dexterity"], [])],
                UnknownAffixNids: ["a1"]),
            new ResolvedBuild("Two", "Rogue", [new ResolvedVariant("v", ["Dexterity"], [])],
                UnknownUniqueIds: ["u1", "u2"]),
            new ResolvedBuild("Three", "Rogue", [new ResolvedVariant("v", ["Dexterity"], [])],
                UnknownAffixNids: ["a1", "a2", "a3"]),
            new ResolvedBuild("Four", "Rogue", [new ResolvedVariant("v", ["Dexterity"], [])],
                UnknownAffixNids: ["a1", "a2", "a3", "a4"], UnknownUniqueIds: ["u1"]),
        };
        var vm = new MainViewModel(startTierListFetches: false);

        vm.Ingest(builds[0], "Test", builds.Skip(1).ToList());

        Assert.Contains("1 affix", vm.MissingDataNote);
        Assert.Contains("2 uniques", vm.MissingDataNote);
        Assert.Contains("3 affixes", vm.MissingDataNote);
        Assert.Contains("4 affixes and 1 unique", vm.MissingDataNote);
    }

    [Fact]
    public void Armory_add_refuses_without_dropping_a_loaded_super_build()
    {
        var builds = new[]
        {
            BuildWithLoot("A", "Rogue", "Harlequin Crest", "Applied Alchemy", "Dexterity"),
            BuildWithLoot("B", "Rogue", "Tyrael's Might", "Applied Alchemy", "Maximum Life"),
            BuildWithLoot("C", "Rogue", "Heir of Perdition", "Applied Alchemy", "Armor"),
            BuildWithLoot("D", "Rogue", "The Grandfather", "Applied Alchemy", "Movement Speed"),
        };
        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(builds[0], "Test", builds.Skip(1).ToList());
        var originalCode = vm.ImportCode;

        vm.IngestSecond(BuildWithLoot("E", "Rogue", "Banished Lord's Talisman",
            "Applied Alchemy", "Critical Strike Chance"));

        Assert.Contains("4-build Super Build", vm.ArmoryStatus);
        Assert.False(vm.HasSecondBuild);
        Assert.Contains("4 builds", vm.FilterInfo);
        Assert.Equal(originalCode, vm.ImportCode);
        Assert.Equal([
            "Harlequin Crest (Build)",
            "Tyrael's Might (B)",
            "Heir of Perdition (C)",
            "The Grandfather (D)"
        ], vm.UniquePurpleLines);
        Assert.DoesNotContain(vm.UniquePurpleLines,
            line => line.StartsWith("Banished Lord's Talisman", StringComparison.Ordinal));
    }

    private static ResolvedBuild Build(string name, string className, params string[] affixes) =>
        new(name, className, [new ResolvedVariant("Endgame", affixes, [])]);

    private static ResolvedBuild BuildWithVariants(string name, string className,
        params (string Name, string[] Affixes)[] variants) => new(name, className,
            variants.Select(v => new ResolvedVariant(v.Name, v.Affixes, [])).ToList());

    private static ResolvedBuild BuildWithLoot(string name, string className, string unique,
        string talismanSet, params string[] affixes) => new(name, className,
            [new ResolvedVariant("Endgame", affixes, [unique], TalismanSets: [talismanSet])]);

    private static CompiledBuild SyntheticSlotted(string name, uint seed)
    {
        uint[] pool = [seed + 1, seed + 2, seed + 3];
        var names = pool.ToDictionary(id => id, id => $"Affix {id}");
        var slots = Enumerable.Range(0, 5)
            .Select(i => new SlotPool($"Slot {i}", [seed + 20u + (uint)i], pool))
            .ToList();
        return new CompiledBuild(name, "Rogue", FilterColors.Red, FilterColors.Pink,
            pool, names, [], [], [], [], slots, [], []);
    }

    private static bool AffixTierMatches(DecodedRule rule, IReadOnlySet<uint> itemAffixes)
    {
        var condition = rule.Conditions.Single(c => c.Type == 6);
        return condition.Ids.Count(itemAffixes.Contains) >= (int)condition.MaskOrCount!.Value;
    }

    private static Dictionary<uint, IReadOnlyList<uint>> TierAffixIdsByColor(string importCode) =>
        FilterDecoder.Decode(importCode).Rules
            .Where(rule => rule.Conditions.Any(c => c.Type == 6)
                && rule.Conditions.Any(c => c.Type == 1))
            .ToDictionary(rule => rule.Color, rule => (IReadOnlyList<uint>)rule.Conditions.Single(c => c.Type == 6).Ids);
}
