using System.Linq;
using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Tests;

public class SourceParserTests
{
    // ── Paste mode ──
    [Fact]
    public void Paste_extracts_affixes_and_uniques_skips_headers()
    {
        var text = "Gear Priorities:\n- Maximum Life\n- Critical Strike Chance\n"
                 + "Recommended Uniques:\nTyrael's Might";
        var rb = PastedBuild.Parse(text, "p");
        var v = rb.Variants[0];
        Assert.Contains("Maximum Life", v.Affixes);
        Assert.Contains("Critical Strike Chance", v.Affixes);
        Assert.Contains("Tyrael's Might", v.Uniques);            // recognized as a known unique
        Assert.DoesNotContain(v.Affixes, a => a.EndsWith(":"));  // "Gear Priorities:" etc. dropped
    }

    // ── d4builds Firestore doc ──
    private const string D4bDoc = """
    {"fields":{
      "name":{"stringValue":"Test Build"},
      "class":{"stringValue":"Barbarian"},
      "variants":{"arrayValue":{"values":[
        {"mapValue":{"fields":{
          "variantName":{"stringValue":"V1"},
          "newStats":{"mapValue":{"fields":{
            "Helm":{"arrayValue":{"values":[{"stringValue":"Strength"},{"nullValue":null},{"stringValue":"Maximum Life"}]}}
          }}},
          "gear":{"mapValue":{"fields":{
            "Helm":{"stringValue":"Heir of Perdition"},
            "Ring 1":{"stringValue":"Aspect of Corruption"}
          }}}
        }}}
      ]}}
    }}
    """;

    [Fact]
    public void D4Builds_parses_variant_affixes_and_unique()
    {
        var rb = D4BuildsFetcher.Parse(D4bDoc);
        Assert.Equal("Test Build", rb.Build);
        Assert.Equal("Barbarian", rb.Class);
        var v = Assert.Single(rb.Variants);
        Assert.Equal("V1", v.Name);
        Assert.Contains("Strength", v.Affixes);
        Assert.Contains("Maximum Life", v.Affixes);
        Assert.Contains("Heir of Perdition", v.Uniques);            // non-aspect gear = unique
        Assert.DoesNotContain("Aspect of Corruption", v.Uniques);   // aspects excluded
    }

    // ── Mobalytics embedded state ──
    private const string MobaHtml =
        "<html><body><script>window.__PRELOADED_STATE__=" +
        """{"x":{"userGeneratedDocumentBySlug":{"data":{"data":{"name":"Moba Test","buildVariants":{"values":[{"id":"v1","genericBuilder":{"slots":[{"gameSlotSlug":"helm","gameEntity":{"type":"uniqueItems","entity":{"title":"Tyrael's Might","mythic":false},"modifiers":{"gearStats":[{"id":"strength"},{"id":"critical-strike-chance"}]}}}]}}]}},"tags":{"data":[{"groupSlug":"class","name":"Sorcerer"}]}}}}}""" +
        ";</script></body></html>";

    [Fact]
    public void Mobalytics_parses_embedded_state()
    {
        var rb = MobalyticsFetcher.Parse(MobaHtml);
        Assert.Equal("Moba Test", rb.Build);
        Assert.Equal("Sorcerer", rb.Class);
        var v = Assert.Single(rb.Variants);
        Assert.Contains("critical strike chance", v.Affixes);  // slug -> spaced; mapper normalizes
        Assert.Contains("Tyrael's Might", v.Uniques);
    }

    [Fact]
    public void End_to_end_paste_to_filter_roundtrips()
    {
        var rb = PastedBuild.Parse("Strength\nMaximum Life\nCritical Strike Chance\nTyrael's Might", "p");
        var compiled = FilterCompiler.Analyze(rb, FilterColors.Gold, FilterColors.Silver);
        var output = FilterCompiler.Compile(new[] { compiled }, new FilterOptions(), "t");
        Assert.True(output.RoundTripOk);
        Assert.Contains("Tyrael's Might", compiled.Mythics);   // a mythic → its own category, not purple
    }

    // ── maxroll guide → planner resolution (tier-list one-click load) ──
    [Theory]
    [InlineData("https://maxroll.gg/d4/build-guides/ball-lightning-sorcerer-guide", true)]
    [InlineData("https://maxroll.gg/d4/planner/5k34xn0u#3", false)]   // already a planner
    [InlineData("https://d4builds.gg/builds/whirlwind-barbarian-endgame/", false)]
    public void IsBuildGuideUrl_detects_guide_pages(string url, bool expected) =>
        Assert.Equal(expected, MaxrollFetcher.IsBuildGuideUrl(url));

    [Theory]
    // No suffix (Ball Lightning) AND the common "#<profileIndex>" suffix (Whirlwind etc.) — the
    // suffix used to break extraction, so only BL loaded. Both must yield the bare planner URL.
    [InlineData("https://maxroll.gg/d4/planner/5k34xn0u", "https://maxroll.gg/d4/planner/5k34xn0u")]
    [InlineData("https://maxroll.gg/d4/planner/w62gqj0v#4", "https://maxroll.gg/d4/planner/w62gqj0v")]
    public void Guide_page_planner_link_is_extracted(string linkInPage, string expected)
    {
        // Mirrors the maxroll/planner-page Gutenberg block embedded in a build-guide page.
        var guideHtml =
            @"…""category"":""Build Guides"",""gutenbergBlock"":[{""attributes"":{""link"":""" +
            linkInPage + @"""},""blockName"":""maxroll/planner-page"",""innerHTML"":""<html…";
        var link = MaxrollFetcher.ParseGuidePlannerLink(guideHtml);
        Assert.Equal(expected, link);
        Assert.Equal(expected.Split('/')[^1], MaxrollFetcher.ExtractPlannerId(link!));   // feeds the planner fetch
    }

    [Fact]
    public void Guide_page_with_no_planner_returns_null()
        => Assert.Null(MaxrollFetcher.ParseGuidePlannerLink("<html>just an article, no embed</html>"));
}
