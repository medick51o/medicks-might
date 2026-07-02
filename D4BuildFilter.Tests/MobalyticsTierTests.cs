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

        var god = Assert.Single(tl.Builds, b => b.Tier == "God");
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

    /// <summary>New-class builds (Lord of Hatred) stopped using the classes-icons/&lt;Class&gt;.png
    /// convention, which silently dropped 100% of Paladin/Warlock chips (measured live 2026-06-10:
    /// 23/62 endgame builds lost). The parser now derives class from the slug first and tolerates
    /// any icon path. These fixtures mirror the three real shapes seen live.</summary>
    [Fact]
    public void Mobalytics_parses_new_class_icon_formats_and_D_tier()
    {
        const string fixture =
            @"{""tierLists"":{""values"":[{""id"":""9"",""tierSections"":[" +
            @"{""name"":""S"",""color"":""tier-s"",""description"":null,""ugDataItems"":[" +
                // New-style icon: not under classes-icons, query suffix after .png
                @"{""id"":""p"",""iconUrl"":""https://cdn.mobalytics.gg/assets/common/uploads/images/diablo-4/Paladin.png?v1"",""linkUrl"":""/diablo-4/builds/paladin-blessed-hammer-hammerdin"",""title"":""Blessed Hammer - Hammerdin"",""subTitle"":""x""}," +
                // New-style icon: -icon suffix in the filename
                @"{""id"":""w"",""iconUrl"":""https://cdn.mobalytics.gg/assets/common/uploads/images/diablo-4/Warlock-icon.png"",""linkUrl"":""/diablo-4/builds/warlock-dread-claws"",""title"":""Dread Claws"",""subTitle"":""x""}," +
                // Custom build art (no class in the icon at all) — class comes from the slug
                @"{""id"":""c"",""iconUrl"":""https://cdn.mobalytics.gg/assets/common/uploads/images/diablo-4/Cataclysm.png"",""linkUrl"":""/diablo-4/builds/druid-cataclysm"",""title"":""Cataclysm"",""subTitle"":""x""}" +
            @"]}," +
            // D-tier section: Pushing exposes D; MobaTiers used to omit it (parsed then discarded)
            @"{""name"":""D"",""color"":""tier-d"",""description"":null,""ugDataItems"":[" +
                @"{""id"":""d"",""iconUrl"":""https://cdn.mobalytics.gg/assets/diablo-4/images/classes-icons/Rogue.png"",""linkUrl"":""/diablo-4/builds/rogue-flurry"",""title"":""Flurry"",""subTitle"":""x""}" +
            @"]}" +
            @"]}]}}";

        var tl = TierListFetcher.ParseMobalytics(fixture);
        Assert.Equal(4, tl.Builds.Count);
        Assert.Contains(tl.Builds, b => b is { Tier: "S", ClassName: "Paladin", Name: "Blessed Hammer - Hammerdin" });
        Assert.Contains(tl.Builds, b => b is { Tier: "S", ClassName: "Warlock", Name: "Dread Claws" });
        Assert.Contains(tl.Builds, b => b is { Tier: "S", ClassName: "Druid", Name: "Cataclysm" });
        Assert.Contains(tl.Builds, b => b is { Tier: "D", ClassName: "Rogue", Name: "Flurry" });
    }

    [Fact]
    public void Mobalytics_unknown_class_still_lists_with_empty_class()
    {
        // A hypothetical 9th class: must LIST (neutral chip) rather than vanish like Paladin/Warlock did.
        const string fixture =
            @"{""tierLists"":{""values"":[{""id"":""9"",""tierSections"":[" +
            @"{""name"":""A"",""color"":""tier-a"",""description"":null,""ugDataItems"":[" +
                @"{""id"":""n"",""iconUrl"":""https://cdn.mobalytics.gg/assets/common/uploads/images/diablo-4/Templar.png"",""linkUrl"":""/diablo-4/builds/templar-holy-bolt"",""title"":""Holy Bolt"",""subTitle"":""x""}" +
            @"]}]}]}}";

        var b = Assert.Single(TierListFetcher.ParseMobalytics(fixture).Builds);
        Assert.Equal("Holy Bolt", b.Name);
        Assert.Equal("A", b.Tier);
        Assert.Equal("", b.ClassName);
    }

    [LocalFixtureFact(@"C:\Sync\Projects\D4BuildFilter\_tmpfix_moba.html")]
    public void Mobalytics_parses_real_live_fixture()
    {
        // Stashed from a live fetch — full page, every section populated.
        const string path = @"C:\Sync\Projects\D4BuildFilter\_tmpfix_moba.html";

        var tl = TierListFetcher.ParseMobalytics(File.ReadAllText(path));
        var byTier = tl.Builds.GroupBy(b => b.Tier).ToDictionary(g => g.Key, g => g.Count());
        _out.WriteLine($"total: {tl.Builds.Count}");
        foreach (var kv in byTier) _out.WriteLine($"  [{kv.Key}] {kv.Value}");
        // Assert STRUCTURE, not tier content: hard-pinning "God" went stale the day S14 reset the
        // meta (no God-ranked builds ~30h in — captured fixture proved it). The core letter trio is
        // always populated; everything parsed must be a tier we know; and the list must not thin.
        var tierSet = tl.Builds.Select(b => b.Tier).Distinct().OrderBy(t => t).ToList();
        Assert.Contains("S", tierSet);
        Assert.Contains("A", tierSet);
        Assert.Contains("B", tierSet);
        var known = new[] { "God", "S", "A", "B", "C", "D", "Support" };
        Assert.All(tierSet, t => Assert.Contains(t, known));
        Assert.True(tl.Builds.Count >= 30, $"only {tl.Builds.Count} builds parsed from the fixture");
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
