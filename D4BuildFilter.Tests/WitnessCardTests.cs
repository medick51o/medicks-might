using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using D4BuildFilter.Core;
using D4BuildFilter.WPF.Services;
using D4BuildFilter.WPF.ViewModels;
using Xunit;

namespace D4BuildFilter.Tests;

public class WitnessCardTests
{
    [Fact]
    public void Legend_contains_exactly_the_color_rules_that_the_filter_emitted()
    {
        var output = FilterCompiler.Compile([Build()], new FilterOptions
        {
            GoldTier = false,
            SilverTier = false,
            BuildUniques = false,
            ItemPowerTiers = true,
            GreaterAffixes = false,
            CharmsSeals = false,
            CharmsSealsAncestral = false,
            Codex = true,
            CubeBases = false,
            ShowUniqueCharms = false,
            HideRest = false,
        }, "Filter");

        var result = WitnessCardComposer.Compose(Request(output));

        Assert.False(result.IsBlocked);
        Assert.Equal(["White", "Orange", "Cyan"], result.Card!.LegendRows.Select(r => r.ColorName));
        Assert.Equal(["Codex Upgrades", "Item Power 900+", "Item Power 850+"],
            result.Card.LegendRows.Select(r => r.Label));
    }

    [Fact]
    public void Tier_chip_is_absent_without_complete_tier_provenance_and_version_is_stamped()
    {
        var output = SafeOutput();
        var withoutTier = WitnessCardComposer.Compose(Request(output) with { TierKind = null, Tier = null });
        var withTier = WitnessCardComposer.Compose(Request(output) with { VersionOverride = null });

        Assert.Null(withoutTier.Card!.ProvenanceChip);
        Assert.Equal("Maxroll · Endgame · S", withTier.Card!.ProvenanceChip);
        Assert.StartsWith("MedicK's Might v", withTier.Card.VersionStamp);
        Assert.DoesNotContain("built", withTier.Card.VersionStamp, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(BlockedOutputs))]
    public void Unsafe_compile_reasons_block_card_composition(FilterOutput output, string expected)
    {
        var result = WitnessCardComposer.Compose(Request(output));

        Assert.True(result.IsBlocked);
        Assert.Null(result.Card);
        Assert.Contains(expected, result.BlockReason, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<FilterOutput, string> BlockedOutputs => new()
    {
        { new("test", "", 0, 0, true, false, ["degenerate pool"]), "no safe filter code" },
        { new("test", "code", 26, 4, true, true, []), "over 25" },
        { new("test", "code", 10, 4, false, true, []), "corruption check" },
    };

    [Fact]
    public void Active_warning_band_blocks_an_otherwise_safe_card()
    {
        var result = WitnessCardComposer.Compose(Request(SafeOutput()) with
        {
            ActiveWarning = "⚠ This compile has an active warning.",
        });

        Assert.True(result.IsBlocked);
        Assert.Contains("active warning", result.BlockReason);
    }

    [Fact]
    public void Clipboard_payload_carries_png_bitmap_and_import_code_text()
    {
        var pixels = new byte[] { 0, 0, 0, 255 };
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
        bitmap.Freeze();

        var data = WitnessCardClipboard.Create(bitmap, "IMPORT-CODE");

        Assert.True(data.GetDataPresent(DataFormats.Bitmap, false));
        Assert.True(data.GetDataPresent(WitnessCardClipboard.PngFormat, false));
        Assert.True(data.GetDataPresent(DataFormats.UnicodeText, false));
        Assert.Equal("IMPORT-CODE", data.GetData(DataFormats.UnicodeText, false));
    }

    private static WitnessCardRequest Request(FilterOutput output) => new(
        "Whirlwind", "Barbarian", "Maxroll", "Endgame", "S", output, 25,
        "https://discord.gg/test", VersionOverride: "MedicK's Might v1.2.3");

    private static FilterOutput SafeOutput() =>
        FilterCompiler.Compile([Build()], new FilterOptions { HideRest = false }, "Filter");

    private static CompiledBuild Build() => FilterCompiler.Analyze(new ResolvedBuild(
        "Whirlwind", "Barbarian",
        [new ResolvedVariant("Endgame", ["Strength", "Maximum Life", "Critical Strike Chance"], [])]),
        FilterColors.Red, FilterColors.Pink);
}
