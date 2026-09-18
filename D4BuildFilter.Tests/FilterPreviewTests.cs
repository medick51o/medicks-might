using D4BuildFilter.Core;
using D4BuildFilter.WPF.ViewModels;
using Xunit;

namespace D4BuildFilter.Tests;

public class FilterPreviewTests
{
    [Fact]
    public void Projection_uses_conditions_not_rule_names_and_preserves_emission_order()
    {
        var code = Code(
            FilterBuilder.MakeRule("Misleading 3+ Boots", Visibility.HideAll,
                [Conditions.Types([ItemTypeDatabase.ByName["Helm"]]), Conditions.RarityMask(Rarity.Legendary),
                 Conditions.Affixes([11u, 12u], 1), Conditions.ItemPower(1, 899)]),
            FilterBuilder.MakeRule("Second", Visibility.Show, []));

        var preview = FilterPreview.FromImportCode(code);

        Assert.Equal([1, 2], preview.Rules.Select(r => r.Order));
        Assert.Equal(["Hide", "Show"], preview.Rules.Select(r => r.Action));
        var hide = preview.Rules[0];
        Assert.Contains("Slots: Helm", hide.Scope);
        Assert.Contains("Rarities: Legendary", hide.Scope);
        Assert.Contains("minimum 1 of 2 encoded IDs", hide.Details);
        Assert.Contains("1–899", hide.Details);
        Assert.Contains("No earlier enabled Show", hide.ProtectionNote);
        Assert.Contains("below 900", hide.ProtectionNote);
        Assert.Contains("NOT PROVEN", FilterPreview.EvidenceNote);
    }

    [Fact]
    public void Nine_hundred_toggle_changes_actual_hide_scope_and_not_the_later_keeper_threshold()
    {
        var options = Quiet() with
        {
            NineHundredOnlyLegendaries = true, NineHundredOnlyHelm = true,
        };
        var off = Preview(options);
        var on = Preview(options with { NineHundredOnly = true });

        Assert.DoesNotContain(off.Rules, r => r.Action == "Hide");
        var hide = Assert.Single(on.Rules, r => r.Action == "Hide");
        Assert.Equal(1, hide.Order);
        Assert.Equal("Slots: Helm; Rarities: Legendary (mask 0x8)", hide.Scope);
        Assert.Equal(899ul, Assert.Single(hide.Conditions, c => c.Type == 0).Max);
        Assert.Contains("Mythic is excluded", hide.ProtectionNote);
        Assert.Contains("900 only", on.HideSummary);
        Assert.Contains("1–899", on.HideSummary);
        Assert.All(on.Rules.Where(r => r.IsShow), r =>
            Assert.Equal(3ul, Assert.Single(r.Conditions, c => c.Type == 6).MaskOrCount));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Floor_protections_only_name_emitted_build_unique_and_codex_rules(bool uniques, bool codex)
    {
        var build = Build() with { UniqueIds = [0x17d2dcu] };
        var options = Quiet() with
        {
            NineHundredOnly = true, NineHundredOnlyUniques = true, NineHundredOnlyHelm = true,
            BuildUniques = uniques, Codex = codex,
        };
        var output = FilterCompiler.Compile([build], options, "Preview");
        var hide = Assert.Single(FilterPreview.FromImportCode(output.ImportCode).Rules, r => r.Action == "Hide");

        Assert.Equal(uniques, hide.ProtectionNote.Contains("Build Uniques", StringComparison.Ordinal));
        Assert.Equal(codex, hide.ProtectionNote.Contains("Codex Upgrades", StringComparison.Ordinal));
        if (uniques || codex) Assert.Contains("must match its own conditions", hide.ProtectionNote);
        else Assert.Contains("No earlier enabled Show", hide.ProtectionNote);
    }

    [Fact]
    public void Requested_four_affixes_are_shown_as_the_encoded_two_after_clamping()
    {
        var output = FilterCompiler.Compile([Build(poolCount: 2)], Quiet() with
        {
            RedMinAffixes = 4, PinkMinAffixes = 4,
        }, "Preview");
        var preview = FilterPreview.FromImportCode(output.ImportCode);

        Assert.Equal(2, preview.Rules.Count);
        Assert.All(preview.Rules, rule =>
        {
            var affixes = Assert.Single(rule.Conditions, c => c.Type == 6);
            Assert.Equal(2ul, affixes.MaskOrCount);
            Assert.Equal(2, affixes.Ids.Count);
            Assert.Contains("minimum 2 of 2", rule.Details);
            Assert.DoesNotContain("minimum 4", rule.Details);
        });
    }

    [Fact]
    public void Disabled_tier_is_not_advertised_by_its_color()
    {
        var on = Preview(Quiet());
        var off = Preview(Quiet() with { GoldTier = false });

        Assert.Contains(on.Rules, r => r.Color == FilterColors.Red);
        Assert.DoesNotContain(off.Rules, r => r.Color == FilterColors.Red);
        Assert.Equal("Pink", Assert.Single(off.Rules).ColorName);
    }

    [Fact]
    public void Auto_fit_preview_shows_combined_scope_from_final_payload()
    {
        var build = Build(slots: true);
        var options = Quiet() with { PerSlotRules = true };
        var requested = FilterCompiler.Compile([build], options, "Preview");
        var fitted = FilterCompiler.CompileWithinCap([build], options, 2, out CompileFitReport report, "Preview");
        var before = FilterPreview.FromImportCode(requested.ImportCode);
        var after = FilterPreview.FromImportCode(fitted.ImportCode);

        Assert.True(report.Fits);
        Assert.Contains("per-slot precision", report.DisabledFeatures);
        Assert.Equal(6, before.Rules.Count);
        Assert.All(before.Rules, r => Assert.Contains(r.Conditions, c => c.Type == 5));
        Assert.Equal(2, after.Rules.Count);
        Assert.All(after.Rules, r =>
        {
            Assert.DoesNotContain(r.Conditions, c => c.Type == 5);
            Assert.Contains("Slots: unrestricted", r.Scope);
        });
        AssertPayloadMatches(fitted.ImportCode, after);
    }

    [Fact]
    public void Auto_fit_removal_of_keeper_is_visible_even_when_option_remains_on()
    {
        var options = Quiet();
        var fitted = FilterCompiler.CompileWithinCap([Build()], options, 1, out CompileFitReport fit, "Preview");

        Assert.True(options.SilverTier);
        Assert.Contains("the keeper (Pink) tier", fit.DisabledFeatures);
        var rule = Assert.Single(FilterPreview.FromImportCode(fitted.ImportCode).Rules);
        Assert.Equal("Red", rule.ColorName);
        Assert.Contains("Legendary", rule.Scope);
        AssertPayloadMatches(fitted.ImportCode, FilterPreview.FromImportCode(fitted.ImportCode));
    }

    [Fact]
    public void Hide_rest_is_a_bounded_rarity_scope_not_everything_else()
    {
        var preview = Preview(Quiet() with { HideRest = true });
        var hide = Assert.Single(preview.Rules, r => r.Action == "Hide");

        Assert.Contains("Common, Magic, Rare, Legendary, Talisman", hide.Scope);
        Assert.DoesNotContain("Unique", hide.Scope);
        Assert.Contains("1–900", hide.Details);
        Assert.DoesNotContain("everything else", preview.HideSummary);
        Assert.Contains("No enabled Hide", Preview(Quiet()).HideSummary);
    }

    [Fact]
    public void Unknowns_duplicate_affix_ids_and_greater_affix_markers_are_not_silently_normalized()
    {
        var optional = Wire.Efb(4, Wire.Concat(Wire.Efv(1, 7), Wire.Ef32(2, 11), Wire.Ef32(2, 11),
            Wire.Efv(4, 2), Wire.Efb(3, Wire.Concat(Wire.Ef32(1, 11), Wire.Ef32(2, 11))), Wire.Efv(15, 7)));
        var unfamiliar = Wire.Efb(4, Wire.Concat(Wire.Efv(1, 42), Wire.Ef32(2, 23),
            Wire.Efv(4, 8), Wire.Efv(5, 9), Wire.Efv(6, 10)));
        var code = Code(FilterBuilder.MakeRule("Unknown", (Visibility)99,
            [optional, unfamiliar, Conditions.RarityMask(0x80)], color: 0x12345678));
        var rule = Assert.Single(FilterPreview.FromImportCode(code).Rules);

        Assert.Equal("Unknown action (99)", rule.Action);
        Assert.Equal("Custom (#12345678)", rule.ColorName);
        Assert.False(rule.IsShow);
        Assert.Contains("minimum 2 of 2 encoded IDs (1 distinct)", rule.Details);
        Assert.Contains("must be Greater Affixes (1): 0x0000000B", rule.Details);
        Assert.Contains("field15/wt0=7", rule.Details);
        Assert.Contains("Unknown condition type 42", rule.Details);
        Assert.Contains("field 4: 8; field 6: 10; field 5: 9", rule.Details);
        Assert.Contains("unknown bits 0x80", rule.Scope);
        AssertPayloadMatches(code, FilterPreview.FromImportCode(code));
    }

    [Fact]
    public void Charm_names_set_members_and_unspecified_power_bounds_follow_decoded_fields()
    {
        var code = Code(FilterBuilder.MakeRule("Charm", Visibility.Show,
            [Conditions.Types([ItemTypeDatabase.ByName["Charm"]]), Conditions.ItemPowerAny(),
             Conditions.TalismanSetBonus(new (uint SetId, IReadOnlyList<uint> Items)[] { (0x22fb15, [41u, 42u]) })]));
        var preview = FilterPreview.FromImportCode(code);
        var rule = Assert.Single(preview.Rules);

        Assert.Contains("Slots: Charm", rule.Scope);
        Assert.DoesNotContain("Horadric Seal", rule.Scope);
        Assert.Contains("unspecified–unspecified", rule.Details);
        Assert.Contains("members (2): 0x00000029, 0x0000002A", rule.Details);
        Assert.Null(Assert.Single(rule.Conditions, c => c.Type == 0).MaskOrCount);
        AssertPayloadMatches(code, preview);
    }

    [Fact]
    public void Disabled_rules_remain_in_order_but_supply_no_protection()
    {
        var code = Code(
            FilterBuilder.MakeRule("Codex", Visibility.Recolor, [Conditions.Codex()], FilterColors.White, enabled: false),
            FilterBuilder.MakeRule("Hide", Visibility.HideAll, [Conditions.ItemPower(1, 899)]));
        var preview = FilterPreview.FromImportCode(code);

        Assert.Equal(2, preview.Rules.Count);
        Assert.Contains("[disabled]", preview.Rules[0].Heading);
        Assert.Contains("supplies no protection", preview.Rules[0].ProtectionNote);
        Assert.Contains("No earlier enabled Show", preview.Rules[1].ProtectionNote);
        Assert.Equal("2 encoded rules · 1 enabled", preview.Summary);
    }

    [Fact]
    public void Projection_does_not_change_output_code_or_recompile_bytes()
    {
        var build = Build(slots: true);
        var options = Quiet() with { PerSlotRules = true, Codex = true, HideRest = true };
        var output = FilterCompiler.Compile([build], options, "Preview");
        var originalCode = output.ImportCode;
        var originalBytes = Convert.FromBase64String(originalCode);

        var preview = FilterPreview.FromImportCode(output.ImportCode);

        AssertPayloadMatches(originalCode, preview);
        Assert.Equal(originalCode, output.ImportCode);
        Assert.Equal(originalBytes, Convert.FromBase64String(output.ImportCode));
        Assert.Equal(originalCode, FilterCompiler.Compile([build], options, "Preview").ImportCode);
    }

    [Fact]
    public void View_model_preview_uses_injected_final_auto_fit_output_instead_of_live_options()
    {
        var final = FilterCompiler.CompileWithinCap([Build(slots: true)], Quiet() with { PerSlotRules = true },
            1, out CompileFitReport fit, "Preview");
        var vm = new MainViewModel(startTierListFetches: false,
            compileWithinCap: (_, _, _, _, _) => (final, fit));

        vm.Ingest(Resolved(), "Test");

        Assert.True(vm.OptSilverTier);
        Assert.Equal(final.ImportCode, vm.ImportCode);
        var preview = Assert.IsType<FilterPreview>(vm.EncodedPreview);
        Assert.Equal("Red", Assert.Single(preview.Rules).ColorName);
        Assert.Contains("Slots: unrestricted", vm.BuildTierLegend);
        Assert.DoesNotContain("Pink", vm.BuildTierLegend);
        AssertPayloadMatches(vm.ImportCode, preview);
    }

    [Fact]
    public void View_model_toggle_refreshes_preview_and_empty_selection_retires_it()
    {
        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(Resolved(), "Test");
        var previous = vm.EncodedPreview;

        vm.OptCodex = false;

        Assert.NotSame(previous, vm.EncodedPreview);
        var preview = Assert.IsType<FilterPreview>(vm.EncodedPreview);
        Assert.DoesNotContain(preview.Rules, r => r.Conditions.Any(c => c.Type == 3));
        AssertPayloadMatches(vm.ImportCode, preview);

        vm.VariantGroups.Single().Variants.Single().IsSelected = false;

        Assert.Empty(vm.ImportCode);
        Assert.Null(vm.EncodedPreview);
        Assert.Equal("No import code to preview.", vm.PreviewStatus);
        Assert.Equal(vm.PreviewStatus, vm.BuildTierLegend);
    }

    [Fact]
    public void Invalid_code_retires_preview_instead_of_showing_previous_intent()
    {
        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(Resolved(), "Test");
        Assert.NotNull(vm.EncodedPreview);

        vm.ImportCode = "not base64!";

        Assert.Null(vm.EncodedPreview);
        Assert.Contains("could not be decoded", vm.PreviewStatus);
        Assert.Equal(vm.PreviewStatus, vm.BuildTierLegend);
    }

    [Fact]
    public void Witness_model_keeps_show_hide_and_duplicate_rules_in_payload_order()
    {
        var show = FilterBuilder.MakeRule("Same", Visibility.Show, [Conditions.RarityMask(Rarity.Unique)]);
        var code = Code(show, show, FilterBuilder.MakeRule("Floor", Visibility.HideAll,
            [Conditions.Types([ItemTypeDatabase.ByName["Helm"]]), Conditions.ItemPower(1, 899)]));
        var output = new FilterOutput("Preview", code, 3, Convert.FromBase64String(code).Length, true, true, []);
        var composition = WitnessCardComposer.Compose(new("Preview", "Barbarian", "Test", null, null,
            output, 25, "https://example.test"));
        var card = Assert.IsType<WitnessCardViewModel>(composition.Card);

        Assert.Equal(3, card.LegendRows.Count);
        Assert.Equal(["Show · Same", "Show · Same", "Hide · Floor"], card.LegendRows.Select(r => r.Label));
        Assert.Contains("1–899", card.LegendRows[2].DisplayLabel);
        Assert.Contains("Slots: Helm", card.LegendRows[2].DisplayLabel);
        Assert.Contains("Candidate protections: #1 Same, #2 Same", card.LegendRows[2].DisplayLabel);
        AssertPayloadMatches(code, Assert.IsType<FilterPreview>(card.EncodedPreview));
    }

    private static FilterPreview Preview(FilterOptions options) =>
        FilterPreview.FromImportCode(FilterCompiler.Compile([Build()], options, "Preview").ImportCode);

    private static string Code(params byte[][] rules) =>
        FilterBuilder.ToImportCode(FilterBuilder.MakeFilter("Preview", rules));

    private static FilterOptions Quiet() => new()
    {
        BuildUniques = false, ItemPowerTiers = false, GreaterAffixes = false,
        CharmsSeals = false, CharmsSealsAncestral = false, Codex = false,
        ShowUniqueCharms = false, HideRest = false,
    };

    private static CompiledBuild Build(int poolCount = 3, bool slots = false)
    {
        uint[] pool = new[] { 0x1beac2u, 0x1bead8u, 0x1beaceu }.Take(poolCount).ToArray();
        return new("Preview", "Barbarian", FilterColors.Red, FilterColors.Pink, pool,
            pool.ToDictionary(id => id, id => $"Affix {id}"), [], [], [], [],
            slots ? new[] { "Helm", "Boots", "Ring" }.Select(name =>
                new SlotPool(name, [ItemTypeDatabase.ByName[name]], pool)).ToArray() : [], [], []);
    }

    private static ResolvedBuild Resolved() => new("Preview", "Barbarian",
        [new ResolvedVariant("Endgame", ["Strength", "Maximum Life", "Critical Strike Chance"], [])]);

    private static void AssertPayloadMatches(string code, FilterPreview preview)
    {
        var decoded = FilterDecoder.Decode(code);
        Assert.Equal(decoded.Name, preview.Name);
        Assert.Equal(decoded.Version, preview.Version);
        Assert.Equal(decoded.Rules.Count, preview.Rules.Count);
        for (int i = 0; i < decoded.Rules.Count; i++)
        {
            var source = decoded.Rules[i];
            var shown = preview.Rules[i];
            Assert.Equal(i + 1, shown.Order);
            Assert.Equal(source.Name, shown.Name);
            Assert.Equal(source.Visibility, shown.Visibility);
            Assert.Equal(source.Color, shown.Color);
            Assert.Equal(source.Enabled, shown.Enabled);
            Assert.Equal(source.Conditions.Count, shown.Conditions.Count);
            for (int j = 0; j < source.Conditions.Count; j++)
            {
                var a = source.Conditions[j];
                var b = shown.Conditions[j];
                Assert.Equal(a.Type, b.Type);
                Assert.Equal(a.Ids, b.Ids);
                Assert.Equal(a.MaskOrCount, b.MaskOrCount);
                Assert.Equal(a.Count, b.Count);
                Assert.Equal(a.Max, b.Max);
                Assert.Equal(a.GreaterAffixOf, b.GreaterAffixOf);
                Assert.Equal(a.Unknown, b.Unknown);
                Assert.Equal(a.SetItems.Count, b.SetItems.Count);
                for (int k = 0; k < a.SetItems.Count; k++)
                {
                    Assert.Equal(a.SetItems[k].SetId, b.SetItems[k].SetId);
                    Assert.Equal(a.SetItems[k].Items, b.SetItems[k].Items);
                }
            }
        }
    }
}
