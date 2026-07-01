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
        Assert.Contains("Tyrael's Might", compiled.UniquesTargeted);   // S14: targeted like any unique
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

    // ── maxroll: unknown nid / unique-id surfacing (the new-season data-gap signal) ──
    // Pre-fix these were dropped with NO trace, so a season-day build compiled an incomplete
    // filter that looked complete. The counts drive the result page's amber "update game data" note.

    [Fact]
    public void Maxroll_counts_ids_the_local_game_data_cant_name()
    {
        // Planner shell: `data` is a JSON-ENCODED STRING (the endpoint's double-parse quirk).
        var inner = """
        {"profiles":[{"name":"Main","items":{"0":"1","1":"2","2":"3","3":"4"}}],
         "items":{
           "1":{"id":"Helm_Legendary_001","explicits":[{"nid":100},{"nid":999}]},
           "2":{"id":"Chest_Unique_Barb_100","explicits":[]},
           "3":{"id":"Ring_Unique_NewSeason_777","explicits":[]},
           "4":{"id":"Talisman_Charm_Unique_Foo_001","explicits":[]}}}
        """;
        var raw = $$"""{"name":"Gap Test","class":"Barbarian","data":{{System.Text.Json.JsonSerializer.Serialize(inner)}}}""";

        // Mini lookups: nid 100 + the Barb chest are known; 999 + the NewSeason ring are not.
        var dir = Path.Combine(Path.GetTempPath(), $"medicksmight_maxgap_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var affixPath = Path.Combine(dir, "affixes.json");
            File.WriteAllText(affixPath,
                """[{"IdSnoList":["100"],"DescriptionClean":"Maximum Life"}]""");
            var uniquePath = Path.Combine(dir, "uniques.json");
            File.WriteAllText(uniquePath,
                """[{"IdNameItem":"Chest_Unique_Barb_100","Name":"Shroud of Test"}]""");

            var rb = MaxrollFetcher.Parse(raw,
                NameLookup.FromFile(affixPath), UniqueLookup.FromFile(uniquePath));

            Assert.Contains("Maximum Life", rb.Variants[0].Affixes);
            Assert.Contains("Shroud of Test", rb.Variants[0].Uniques);
            Assert.Equal(new[] { "999" }, rb.UnknownAffixNids);
            // The unknown gear unique is flagged; the charm id is NOT (charms are intentionally
            // absent from Uniques.enUS.json — their own filter category, not a data gap).
            Assert.Equal(new[] { "Ring_Unique_NewSeason_777" }, rb.UnknownUniqueIds);
            Assert.Equal(2, rb.UnknownDataCount);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort temp cleanup */ }
        }
    }

    [Fact]
    public void Sources_that_carry_names_directly_report_no_data_gaps()
    {
        // Paste / d4builds / Mobalytics never resolve ids against the local data, so the
        // amber note must stay silent for them even when an affix later fails to MAP.
        Assert.Equal(0, PastedBuild.Parse("Maximum Life\nSome Future Affix", "p").UnknownDataCount);
        Assert.Equal(0, D4BuildsFetcher.Parse(D4bDoc).UnknownDataCount);
        Assert.Equal(0, MobalyticsFetcher.Parse(MobaHtml).UnknownDataCount);
    }
}
