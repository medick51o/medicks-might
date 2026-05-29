using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Tests;

public class AffixMapperTests
{
    [Fact]
    public void Exact_affix_maps_to_corrected_id()
    {
        var m = AffixMapper.Map("Critical Strike Chance");
        Assert.True(m.Mapped);
        Assert.Equal(0x001beaceu, m.CoarseId);
        Assert.Equal(MapStrategy.Exact, m.Strategy);
    }

    [Fact]
    public void Skill_rank_maps_via_skill_tier()
    {
        var m = AffixMapper.Map("to War Cry");
        Assert.True(m.Mapped);
        Assert.Equal(MapStrategy.Skill, m.Strategy);
        Assert.Equal("War Cry", m.CoarseName);
    }

    [Theory]
    [InlineData("all damage multipler", "All Damage Multiplier")]      // Mobalytics typo
    [InlineData("Fury per Second", "Fury Regeneration")]              // Mobalytics regen naming
    [InlineData("bonus weapon damage", "Weapon Damage")]
    public void Source_quirk_aliases_resolve(string raw, string expected)
    {
        var m = AffixMapper.Map(raw);
        Assert.True(m.Mapped);
        Assert.Equal(expected, m.CoarseName);
    }

    [Fact]
    public void Weapon_prefixed_overpower_maps_via_keyword()
    {
        var m = AffixMapper.Map("2h Mace: 392.7% Overpower Damage");
        Assert.True(m.Mapped);
        Assert.Equal("Overpower Damage", m.CoarseName);
    }

    [Fact]
    public void Plus_strength_normalizes_to_strength()
    {
        var m = AffixMapper.Map("% Strength");
        Assert.True(m.Mapped);
        Assert.Equal("Strength", m.CoarseName);
    }

    [Fact]
    public void Non_filterable_affix_drops()
    {
        var m = AffixMapper.Map("Gem Strength in this Item");
        Assert.False(m.Mapped);
        Assert.Equal(MapStrategy.Dropped, m.Strategy);
    }

    [Theory] // 3.0.3: labels d4 shows on items but the engine has no filter ID for —
    [InlineData("to Martial Skills")]    // Medick's UI screenshot pending entry
    [InlineData("Bonus Kill Experience")] // also from the screenshot
    [InlineData("to Ultimate Skills")]
    [InlineData("to Combat Skills")]
    public void Engine_unfilterable_label_returns_Unfilterable_not_Dropped(string label)
    {
        var m = AffixMapper.Map(label);
        Assert.False(m.Mapped);
        Assert.Equal(MapStrategy.Unfilterable, m.Strategy);
    }

    [Fact] // 3.0.3 ingest: the one genuinely new skill-category that we added
    public void Mobility_Skills_resolves_via_skill_tier()
    {
        var m = AffixMapper.Map("to Mobility Skills");
        Assert.True(m.Mapped);
        Assert.Equal(MapStrategy.Skill, m.Strategy);
        Assert.Equal(0x20f6f8u, m.CoarseId);
    }
}
