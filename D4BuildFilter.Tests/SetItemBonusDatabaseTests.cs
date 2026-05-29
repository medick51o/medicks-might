using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Tests;

/// <summary>Regression guard for the talisman / set-bonus snoIDs ingested 2026-05-28 from
/// d4data 3.0.3.72031's <c>json/base/meta/SetItemBonus/*.set.json</c>. These IDs are the type-9
/// "Talisman Set Bonus" condition's accepted values; corrupting them would silently break any
/// future filter that gates on a class talisman set.</summary>
public class SetItemBonusDatabaseTests
{
    [Theory]
    [InlineData("Talisman: Barbarian Set 01", 0x22fb15u)]
    [InlineData("Talisman: Druid Set 01",      0x230326u)]
    [InlineData("Talisman: Necromancer Set 01",0x230c68u)]
    [InlineData("Talisman: Rogue Set 01",      0x230a6au)]
    [InlineData("Talisman: Sorcerer Set 01",   0x2243bfu)]
    [InlineData("Talisman: Spiritborn Set 01", 0x231392u)]
    [InlineData("Talisman: Paladin Set 01",    0x23b4cbu)]
    [InlineData("Talisman: Warlock Set 01",    0x23b610u)]
    public void Class_set_tier_01_has_expected_snoID(string name, uint id) =>
        Assert.Equal(id, SetItemBonusDatabase.ByName[name]);

    [Fact] // Raxx's "blue talisman" rule had this ID; it was the gap that prompted the ingest
    public void Mystery_0x234a98_resolves_to_unknown_generic_label() =>
        Assert.True(SetItemBonusDatabase.TryGetName(0x234a98u, out var n) &&
                    n == "Unknown Generic Talisman Set");

    [Fact]
    public void All_eight_classes_have_five_tiers()
    {
        string[] classes = { "Barbarian", "Druid", "Necromancer", "Rogue",
                             "Sorcerer", "Spiritborn", "Paladin", "Warlock" };
        foreach (var c in classes)
            for (int tier = 1; tier <= 5; tier++)
            {
                var key = $"Talisman: {c} Set 0{tier}";
                Assert.True(SetItemBonusDatabase.ByName.ContainsKey(key), $"missing {key}");
            }
    }

    [Fact]
    public void Reverse_lookup_round_trips()
    {
        foreach (var (name, id) in SetItemBonusDatabase.ByName)
        {
            Assert.True(SetItemBonusDatabase.TryGetName(id, out var back));
            Assert.Equal(name, back);
        }
    }

    [Fact]
    public void Unknown_id_returns_fallback_label()
    {
        Assert.False(SetItemBonusDatabase.TryGetName(0xdeadbeefu, out var n));
        Assert.Contains("0xdeadbeef", n);
    }
}
