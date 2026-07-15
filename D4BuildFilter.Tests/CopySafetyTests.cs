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

    private static FilterOutput Output(int ruleCount, bool roundTripOk) =>
        new("test", "code", ruleCount, 4, roundTripOk, true, []);
}
