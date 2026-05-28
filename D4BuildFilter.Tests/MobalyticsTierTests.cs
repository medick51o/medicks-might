using System.IO;
using System.Linq;
using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Tests;

/// <summary>Mobalytics tier-list parser: God / S / A / B / C / Support sections + builds with
/// linkUrl, title, classes-icons/Class.png — validated against a saved live fixture.</summary>
public class MobalyticsTierTests
{
    // Mini synthetic fixture mimicking the embedded __PRELOADED_STATE__ shape.
    private const string SyntheticFixture =
        @"{""tierLists"":{""values"":[{""id"":""4"",""tierSections"":[" +
        // God Tier
        @"{""name"":""God Tier"",""color"":""tier-e"",""description"":null,""ugDataItems"":[" +
            @"{""id"":""x"",""iconUrl"":""https://cdn.mobalytics.gg/assets/diablo-4/images/classes-icons/Sorcerer.png"",""linkUrl"":""/diablo-4/builds/sorcerer-ball-lightning"",""title"":""Ball Lightning - Mekuna's Ballkuna"",""subTitle"":""Mekuna""}" +
        @"]}," +
        // S
        @"{""name"":""S"",""color"":""tier-s"",""description"":null,""ugDataItems"":[" +
            @"{""id"":""y"",""iconUrl"":""https://cdn.mobalytics.gg/assets/diablo-4/images/classes-icons/Barbarian.png"",""linkUrl"":""/diablo-4/builds/barbarian-whirl-wind-barb"",""title"":""Whirlwind"",""subTitle"":""x""}" +
        @"]}," +
        // Support
        @"{""name"":""Support"",""color"":""tier-i"",""description"":null,""ugDataItems"":[" +
            @"{""id"":""z"",""iconUrl"":""https://cdn.mobalytics.gg/assets/diablo-4/images/classes-icons/Druid.png"",""linkUrl"":""/diablo-4/builds/druid-companion"",""title"":""Companion"",""subTitle"":""x""}" +
        @"]}" +
        @"]}]}}";

    [Fact]
    public void Mobalytics_parses_synthetic_with_God_and_Support()
    {
        var tl = TierListFetcher.ParseMobalytics(SyntheticFixture);
        Assert.Equal("Mobalytics", tl.Source);
        Assert.Equal(3, tl.Builds.Count);

        var god = Assert.Single(tl.Builds.Where(b => b.Tier == "God"));
        Assert.Contains("Ball Lightning", god.Name);
        Assert.Equal("Sorcerer", god.ClassName);
        Assert.Equal("https://mobalytics.gg/diablo-4/builds/sorcerer-ball-lightning", god.Url);

        Assert.Contains(tl.Builds, b => b is { Tier: "S", Name: "Whirlwind", ClassName: "Barbarian" });
        Assert.Contains(tl.Builds, b => b is { Tier: "Support", Name: "Companion", ClassName: "Druid" });
    }

    [Fact]
    public void Mobalytics_orders_God_first_Support_last()
    {
        var tiers = TierListFetcher.ParseMobalytics(SyntheticFixture).Builds.Select(b => b.Tier).ToList();
        Assert.Equal(new[] { "God", "S", "Support" }, tiers);
    }

    private readonly Xunit.Abstractions.ITestOutputHelper _out;
    public MobalyticsTierTests(Xunit.Abstractions.ITestOutputHelper o) => _out = o;

    [Fact]
    public void Mobalytics_parses_real_live_fixture()
    {
        // Stashed from a live fetch — full page, every section populated.
        const string path = @"C:\Sync\Projects\D4BuildFilter\_tmpfix_moba.html";
        if (!File.Exists(path)) return;   // skip silently if stash isn't there (CI / fresh clone)

        var tl = TierListFetcher.ParseMobalytics(File.ReadAllText(path));
        var byTier = tl.Builds.GroupBy(b => b.Tier).ToDictionary(g => g.Key, g => g.Count());
        _out.WriteLine($"total: {tl.Builds.Count}");
        foreach (var kv in byTier) _out.WriteLine($"  [{kv.Key}] {kv.Value}");
        var tierSet = tl.Builds.Select(b => b.Tier).Distinct().OrderBy(t => t).ToList();
        Assert.Contains("God", tierSet);
        Assert.Contains("S", tierSet);
        Assert.Contains("A", tierSet);
        Assert.Contains("B", tierSet);
        Assert.Contains("C", tierSet);
        Assert.Contains("Support", tierSet);
    }

    [Fact]
    public void Maxroll_and_D4Builds_now_include_C_and_D_tiers()
    {
        // Letter-tier expansion check: synthetic Maxroll + D4Builds fixtures with C-tier entries
        // (we used to filter to S/A/B only).
        const string maxrollHtml =
            @"{""name"":""Test"",""icon"":""d4/barbarian"",""iconImageUrl"":""x"",""link"":""https://maxroll.gg/d4/build-guides/test-c-guide"",""tier"":""C""}";
        var mr = TierListFetcher.ParseMaxroll(maxrollHtml);
        Assert.Contains(mr.Builds, b => b.Tier == "C");

        const string d4bHtml =
            @"<div class=""tier__list__category C"">C</div><ul class=""tier__list__list"">" +
            @"<a class=""tier__list__item"" href=""/builds/test-c""><img class=""tier__list__item__icon Druid"" src=""x""/>Test</a></ul>";
        var d4 = TierListFetcher.ParseD4Builds(d4bHtml);
        Assert.Contains(d4.Builds, b => b.Tier == "C");
    }
}
