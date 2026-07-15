using System;
using System.Linq;
using D4BuildFilter.Core;
using D4BuildFilter.WPF.ViewModels;
using Xunit;

namespace D4BuildFilter.Tests;

/// <summary>
/// v1.0.3 auto-fit. A big endgame build sits right at D4's 25-rule cap in per-slot mode (about two
/// rules per gear slot), so curating uniques (which adds a hide rule) or turning on an extra option
/// can tip it to 26+, which the game rejects on import. <see cref="FilterCompiler.CompileWithinCap"/>
/// retries in combined mode so the code stays importable; the UI now reports that trade-down plainly.
/// These lock that contract, plus the root-cause fact that hiding a unique costs exactly one rule.
/// </summary>
public class AutoFitTests
{
    private const int Cap = 25;

    // Nine distinct gear slots, each rich enough that per-slot emits BOTH tiers (Red + Pink). That
    // blows the cap in per-slot mode while combined (one pooled Red + Pink) fits comfortably.
    private static CompiledBuild BigPerSlotBarb() => FilterCompiler.Analyze(
        new ResolvedBuild("t", "Barbarian", new[]
        {
            new ResolvedVariant("v", new[] { "Strength" }, Array.Empty<string>(), new[]
            {
                new ResolvedSlot("Helm",    new[] { "Strength", "Maximum Life", "Cooldown Reduction" }),
                new ResolvedSlot("Chest",   new[] { "Strength", "Maximum Life", "Armor" }),
                new ResolvedSlot("Gloves",  new[] { "Strength", "Critical Strike Damage Multiplier", "Vulnerable Damage Multiplier" }),
                new ResolvedSlot("Pants",   new[] { "Strength", "Maximum Life", "Armor" }),
                new ResolvedSlot("Boots",   new[] { "Strength", "Maximum Life", "Movement Speed" }),
                new ResolvedSlot("Amulet",  new[] { "Strength", "Cooldown Reduction", "Movement Speed" }),
                new ResolvedSlot("Ring 1",  new[] { "Critical Strike Damage Multiplier", "Vulnerable Damage Multiplier", "Maximum Life" }),
                new ResolvedSlot("1HMace",  new[] { "Strength", "Critical Strike Damage Multiplier", "Weapon Damage" }),
                new ResolvedSlot("2HSword", new[] { "Strength", "Vulnerable Damage Multiplier", "Weapon Damage" }),
            }),
        }),
        FilterColors.Red, FilterColors.Pink);

    [Fact]
    public void Per_slot_over_cap_auto_fits_to_combined()
    {
        var b = BigPerSlotBarb();
        var opts = new FilterOptions { PerSlotRules = true };

        var perSlot = FilterCompiler.Compile(new[] { b }, opts, "t");
        Assert.True(perSlot.RuleCount > Cap, $"expected per-slot to exceed the cap, got {perSlot.RuleCount}");

        var fitted = FilterCompiler.CompileWithinCap(new[] { b }, opts, Cap, out bool autoFit, "t");
        Assert.True(autoFit);                       // it fell back AND the fallback fits
        Assert.True(fitted.RuleCount <= Cap);       // importable
        Assert.True(fitted.RoundTripOk);

        // The chosen output is exactly the combined compile (not some half-measure).
        var combined = FilterCompiler.Compile(new[] { b }, opts with { PerSlotRules = false }, "t");
        Assert.Equal(combined.RuleCount, fitted.RuleCount);
        Assert.Equal(combined.ImportCode, fitted.ImportCode);
    }

    [Fact]
    public void Under_cap_build_keeps_full_per_slot_precision()
    {
        // A small two-slot build stays well under the cap, so auto-fit must NOT touch it.
        var b = FilterCompiler.Analyze(
            new ResolvedBuild("t", "Barbarian", new[]
            {
                new ResolvedVariant("v", new[] { "Strength" }, Array.Empty<string>(), new[]
                {
                    new ResolvedSlot("Boots", new[] { "Strength", "Maximum Life", "Armor" }),
                    new ResolvedSlot("Helm",  new[] { "Strength", "Maximum Life", "Cooldown Reduction" }),
                }),
            }),
            FilterColors.Red, FilterColors.Pink);
        var opts = new FilterOptions { PerSlotRules = true };

        var perSlot = FilterCompiler.Compile(new[] { b }, opts, "t");
        Assert.True(perSlot.RuleCount <= Cap);

        var fitted = FilterCompiler.CompileWithinCap(new[] { b }, opts, Cap, out bool autoFit, "t");
        Assert.False(autoFit);                                 // nothing to fit
        Assert.Equal(perSlot.ImportCode, fitted.ImportCode);   // per-slot precision preserved verbatim
    }

    [Fact]
    public void Hiding_a_unique_adds_exactly_one_rule()
    {
        // Root cause: uniques show by default, so hiding any adds one "Hide Uniques" rule. On a build
        // already at 25 that is the +1 that tips it to 26. (Count in combined mode for a stable base.)
        var b = BigPerSlotBarb();
        var baseOpts = new FilterOptions { PerSlotRules = false };
        var withHide = baseOpts with { HideUniques = new[] { UniqueItemDatabase.All[0].Id } };

        var basic = FilterCompiler.Compile(new[] { b }, baseOpts, "t");
        var hidden = FilterCompiler.Compile(new[] { b }, withHide, "t");
        Assert.Equal(basic.RuleCount + 1, hidden.RuleCount);
    }

    [Fact]
    public void Unmappable_slot_warning_survives_combined_auto_fit_and_copy_share_refuse_identically()
    {
        var b = BigPerSlotBarb() with { UnmappedSlotPools = ["Amulet"] };
        var opts = new FilterOptions { PerSlotRules = true };

        var fitted = FilterCompiler.CompileWithinCap([b], opts, Cap,
            out CompileFitReport fit, "t");

        Assert.Contains("per-slot precision", fit.DisabledFeatures);
        Assert.Contains(fitted.Diagnostics, message =>
            message.Contains("Amulet") && message.Contains("no rule"));
        Assert.False(fitted.IsCopyable);
        var copyBlock = Assert.IsType<string>(CopySafety.BlockReason(fitted, Cap));
        var share = WitnessCardComposer.Compose(new WitnessCardRequest(
            b.Name, b.Class, "Test", null, null, fitted, Cap, "https://example.test"));
        Assert.True(share.IsBlocked);
        Assert.Equal(copyBlock, share.BlockReason);
    }
}
