using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Tests;

/// <summary>Regression guard for the filter-space affix IDs — especially the 5 in the
/// 0x1beab4–0x1bead4 cluster that were mislabeled and corrected on 2026-05-26 (proven against a
/// real in-game crit filter). If any of these drift, the filter highlights the WRONG stat in-game.</summary>
public class AffixDatabaseTests
{
    [Theory]
    [InlineData("Attack Speed", 0x001beab4u)]                       // was wrongly 0x1beace
    [InlineData("Willpower", 0x001beac6u)]                          // was wrongly 0x1beab4
    [InlineData("Critical Strike Chance", 0x001beaceu)]             // was wrongly 0x1bead2
    [InlineData("Critical Strike Damage Multiplier", 0x001bead2u)] // was wrongly 0x1bead4
    [InlineData("All Damage Multiplier", 0x001bead4u)]             // was wrongly 0x1beac6
    [InlineData("Strength", 0x001beac2u)]                          // stable control
    [InlineData("Weapon Damage", 0x0027fc93u)]                     // stable control
    [InlineData("Cooldown Reduction", 0x001beab8u)]                // added 2026-05-26
    [InlineData("All Stats", 0x001beacau)]                         // added 2026-05-26
    public void Affix_has_filter_validated_id(string name, uint id) =>
        Assert.Equal(id, AffixDatabase.Affixes[name]);

    [Fact]
    public void All_Skills_id_is_corrected() =>
        Assert.Equal(0x00273c0au, AffixDatabase.Skills["All Skills"]);

    [Fact]
    public void Barb_skill_ranks_present() // closed the d4builds/maxroll Barb drops
    {
        Assert.Equal(0x001c6949u, AffixDatabase.Skills["War Cry"]);
        Assert.Equal(0x001c6943u, AffixDatabase.Skills["Rallying Cry"]);
    }

    [Fact]
    public void Tables_are_substantial() // full d4data adoption
    {
        Assert.True(AffixDatabase.Affixes.Count >= 70, $"affixes={AffixDatabase.Affixes.Count}");
        Assert.True(AffixDatabase.Skills.Count >= 200, $"skills={AffixDatabase.Skills.Count}");
    }

    [Fact] // 3.0.3 ingest: the one new "to X Skills" label that has a real filter ID
    public void Mobility_Skills_present_in_skills_table() =>
        Assert.Equal(0x20f6f8u, AffixDatabase.Skills["Mobility Skills"]);

    [Theory] // engine-unfilterable labels — d4 displays them on items but the filter has no ID
    [InlineData("bonus kill experience")]
    [InlineData("martial skills")]
    [InlineData("combat skills")]
    [InlineData("ultimate skills")]
    [InlineData("sigil skills")]
    public void Unfilterable_labels_recognized(string label) =>
        Assert.Contains(label, AffixDatabase.UnfilterableLabels);
}
