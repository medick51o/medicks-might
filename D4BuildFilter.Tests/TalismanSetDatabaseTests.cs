using System.Linq;
using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Tests;

/// <summary>S14 talisman-set catalog (generated from d4data 3.1.0.72592): the data behind the
/// per-class set checkboxes and the build-scoped Charms &amp; Seals rules.</summary>
public class TalismanSetDatabaseTests
{
    [Fact]
    public void Catalog_has_all_classes_and_generics()
    {
        Assert.Equal(45, TalismanSetDatabase.All.Count);
        foreach (var cls in new[] { "Barbarian", "Druid", "Necromancer", "Rogue", "Sorcerer", "Spiritborn", "Paladin", "Warlock" })
            Assert.Equal(5, TalismanSetDatabase.All.Count(s => s.Class == cls));
        Assert.Equal(5, TalismanSetDatabase.All.Count(s => s.Class == "Generic"));
    }

    [Fact]
    public void Sescherons_fury_matches_the_in_game_export()
    {
        // Cross-validated against the hand-built 3.1 rule exported from the game (2026-07-02):
        // set 0x22fb15 with exactly these five member charms.
        var s = TalismanSetDatabase.ById[0x22fb15u];
        Assert.Equal("Sescheron's Fury", s.Name);
        Assert.Equal("Barbarian", s.Class);
        Assert.Equal(new uint[] { 0x25069a, 0x2506a8, 0x2506b5, 0x2506b8, 0x2506cd },
            s.Items.Select(i => i.Id).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void ForClass_offers_class_sets_plus_generics()
    {
        var barb = TalismanSetDatabase.ForClass("Barbarian");
        Assert.Equal(10, barb.Count);                                  // the in-game picker's 10
        Assert.Contains(barb, s => s.Name == "Berserker's Crucible");
        Assert.Contains(barb, s => s.Name == "Mastery");               // generics ride along
        Assert.DoesNotContain(barb, s => s.Class == "Druid");
    }

    [Fact]
    public void Planner_token_resolves_to_the_set()
    {
        // Maxroll charm ids carry "…Set_Barb_05…" — token + number must land on Bul-Kathos' Pride.
        Assert.True(TalismanSetDatabase.TryGetByPlannerToken("Barb", 5, out var set));
        Assert.Equal("Bul-Kathos' Pride", set.Name);
        Assert.Equal(0x22fceeu, set.Id);
        Assert.False(TalismanSetDatabase.TryGetByPlannerToken("Wizard", 1, out _));
    }
}
