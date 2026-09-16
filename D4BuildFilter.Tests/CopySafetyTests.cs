using D4BuildFilter.Core;
using D4BuildFilter.WPF.ViewModels;
using Xunit;

namespace D4BuildFilter.Tests;

public class CopySafetyTests
{
    [Fact]
    public void Over_cap_code_is_blocked_with_import_rejection_feedback()
    {
        var output = Output(ruleCount: 26, roundTripOk: true);

        var reason = CopySafety.BlockReason(output, maxRules: 25);

        Assert.Contains("Not copied", reason);
        Assert.Contains("rejects filters over 25", reason);
    }

    [Fact]
    public void Corrupted_code_is_blocked_with_regeneration_feedback()
    {
        var output = Output(ruleCount: 10, roundTripOk: false);

        var reason = CopySafety.BlockReason(output, maxRules: 25);

        Assert.Contains("Not copied", reason);
        Assert.Contains("corruption check", reason);
    }

    [Fact]
    public void Valid_under_cap_code_can_be_copied()
    {
        var output = Output(ruleCount: 25, roundTripOk: true);

        Assert.Null(CopySafety.BlockReason(output, maxRules: 25));
    }

    [Fact]
    public void Withheld_code_is_blocked_with_actionable_feedback()
    {
        var output = new FilterOutput("test", "", 8, 4, true, false, ["unsafe"]);

        var reason = CopySafety.BlockReason(output, maxRules: 25);

        Assert.Contains("Not copied", reason);
        Assert.Contains("Hide the rest", reason);
    }

    [Fact]
    public void Diagnostic_blocks_copy_and_share_with_the_same_reason()
    {
        var output = new FilterOutput("test", "code", 8, 4, true, true,
            ["Build 'Partial' has an uncovered slot."]);

        var copyBlock = Assert.IsType<string>(CopySafety.BlockReason(output, maxRules: 25));
        var share = WitnessCardComposer.Compose(new WitnessCardRequest(
            "Partial", "Rogue", "Test", null, null, output, 25, "https://example.test"));

        Assert.True(share.IsBlocked);
        Assert.Equal(copyBlock, share.BlockReason);
    }

    public static TheoryData<bool, string, int, bool, string[], string> RefusalPrecedenceCases => new()
    {
        {
            false, "code", 26, false,
            ["Earlier diagnostic.", "No filter code was produced because coverage is missing.",
                "No filter code was produced because the final coverage check failed.", "Later diagnostic."],
            "⚠ Not copied — No filter code was produced because the final coverage check failed."
        },
        {
            false, "code", 26, false, ["Earlier diagnostic.", "Later diagnostic."],
            "⚠ Not copied — no safe filter code was generated. Review the warning and keep 'Hide the rest' off until the build has mapped affixes."
        },
        {
            true, "", 26, false,
            ["Earlier diagnostic.", "No filter code was produced because coverage is missing."],
            "⚠ Not copied — No filter code was produced because coverage is missing."
        },
        {
            true, "", 26, false, ["Earlier diagnostic.", "no filter code was produced with a lowercase prefix."],
            "⚠ Not copied — no safe filter code was generated. Review the warning and keep 'Hide the rest' off until the build has mapped affixes."
        },
        {
            false, "", 26, false, ["Earlier diagnostic."],
            "⚠ Not copied — no safe filter code was generated. Review the warning and keep 'Hide the rest' off until the build has mapped affixes."
        },
        {
            true, "code", 26, false, ["Earlier diagnostic.", "No filter code was produced in a prior attempt."],
            "⚠ Not copied — this code has 26 rules; Diablo 4 rejects filters over 25 on import."
        },
        {
            true, "code", 25, false, ["Earlier diagnostic.", "No filter code was produced in a prior attempt."],
            "⚠ Not copied — this code failed its corruption check. Regenerate before importing."
        },
        {
            true, "code", 25, true, ["Earlier diagnostic.", "No filter code was produced in a prior attempt."],
            "⚠ Not copied — Earlier diagnostic."
        },
    };

    [Theory]
    [MemberData(nameof(RefusalPrecedenceCases))]
    public void Overlapping_failures_return_the_first_refusal(
        bool isCopyable, string importCode, int ruleCount, bool roundTripOk,
        string[] diagnostics, string expectedReason)
    {
        var output = new FilterOutput("test", importCode, ruleCount, 4, roundTripOk, isCopyable, diagnostics);

        Assert.Equal(expectedReason, CopySafety.BlockReason(output, maxRules: 25));
    }

    private static FilterOutput Output(int ruleCount, bool roundTripOk) =>
        new("test", "code", ruleCount, 4, roundTripOk, true, []);
}
