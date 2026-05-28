using System.Linq;
using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Tests;

/// <summary>Tier-list parsers (maxroll Remix JSON + d4builds server HTML). Fixtures mirror the real
/// page shapes; we surface every letter tier S/A/B/C/D (only the maxroll "X" / un-ranked is dropped).</summary>
public class TierListTests
{
    // Mirrors maxroll's embedded build objects (extra fields between icon and link are tolerated).
    private const string MaxrollFixture =
        @"...{""isBoss"":false,""name"":""Whirlwind Barb"",""icon"":""d4/barbarian"",""iconImageUrl"":""a.webp"",""link"":""https://maxroll.gg/d4/build-guides/whirlwind-barbarian-guide"",""tier"":""S""}," +
        @"{""name"":""Ball Lightning Sorc"",""icon"":""d4/sorcerer"",""iconImageUrl"":""b.webp"",""link"":""https://maxroll.gg/d4/build-guides/ball-lightning-sorcerer-guide"",""tier"":""A""}," +
        @"{""name"":""Meteor Sorc"",""icon"":""d4/sorcerer"",""iconImageUrl"":""c.webp"",""link"":""https://maxroll.gg/d4/build-guides/meteor-sorcerer-guide"",""tier"":""D""}," +
        @"{""name"":""Mystery"",""icon"":""d4/rogue"",""iconImageUrl"":""d.webp"",""link"":""https://maxroll.gg/d4/build-guides/mystery-rogue-guide"",""tier"":""X""}...";

    [Fact]
    public void Maxroll_parses_all_letter_tiers_and_drops_X()
    {
        var tl = TierListFetcher.ParseMaxroll(MaxrollFixture);
        Assert.Equal("Maxroll", tl.Source);
        Assert.Equal(3, tl.Builds.Count);                               // S + A + D included, X dropped
        var ww = tl.Builds.First();
        Assert.Equal("Whirlwind Barb", ww.Name);
        Assert.Equal("Barbarian", ww.ClassName);
        Assert.Equal("S", ww.Tier);
        Assert.Equal("https://maxroll.gg/d4/build-guides/whirlwind-barbarian-guide", ww.Url);
        Assert.Contains(tl.Builds, b => b.Tier == "D");                 // D-tier now included
        Assert.DoesNotContain(tl.Builds, b => b.Tier == "X");           // X / un-ranked still dropped
    }

    [Fact]
    public void Maxroll_orders_S_then_A_then_D()
    {
        var tiers = TierListFetcher.ParseMaxroll(MaxrollFixture).Builds.Select(b => b.Tier).ToList();
        Assert.Equal(new[] { "S", "A", "D" }, tiers);
    }

    private const string D4BuildsFixture =
        @"<div class=""tier__list__category S"">S</div><ul class=""tier__list__list"">" +
        @"<a class=""tier__list__item"" href=""/builds/ball-lightning-sorcerer-endgame/?var=0""><img class=""tier__list__item__icon Sorcerer"" src=""x.png"" alt=""Dropdown Arrow""/>Ball Lightning</a>" +
        @"<div class=""tier__list__item empty""></div></ul>" +
        @"<div class=""tier__list__category A"">A</div><ul class=""tier__list__list"">" +
        @"<a class=""tier__list__item"" href=""/builds/hydra-sorcerer-endgame/""><img class=""tier__list__item__icon Sorcerer"" src=""y.png""/>Hydra</a></ul>" +
        @"<div class=""tier__list__category C"">C</div><ul class=""tier__list__list"">" +
        @"<a class=""tier__list__item"" href=""/builds/shred-druid-endgame/""><img class=""tier__list__item__icon Druid"" src=""z.png""/>Shred</a></ul>";

    [Fact]
    public void D4Builds_parses_items_assigns_tier_and_absolutizes_url()
    {
        var tl = TierListFetcher.ParseD4Builds(D4BuildsFixture);
        Assert.Equal("D4Builds", tl.Source);
        Assert.Equal(3, tl.Builds.Count);                               // S + A + C all included now; empty slot ignored

        var bl = tl.Builds.First();
        Assert.Equal("Ball Lightning", bl.Name);
        Assert.Equal("Sorcerer", bl.ClassName);
        Assert.Equal("S", bl.Tier);
        Assert.Equal("https://d4builds.gg/builds/ball-lightning-sorcerer-endgame/?var=0", bl.Url);

        Assert.Contains(tl.Builds, b => b is { Name: "Hydra", Tier: "A" });
        Assert.Contains(tl.Builds, b => b is { Name: "Shred", Tier: "C" });    // C-tier now surfaced
    }

    [Fact]
    public void Parsers_return_empty_not_throw_on_garbage()
    {
        Assert.Empty(TierListFetcher.ParseMaxroll("<html>no data</html>").Builds);
        Assert.Empty(TierListFetcher.ParseD4Builds("<html>no data</html>").Builds);
    }
}
