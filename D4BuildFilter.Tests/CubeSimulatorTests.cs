using System.Linq;
using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Tests;

/// <summary>The Cube Sandbox's eligibility engine (v1.0.2): a simulated item is just the handful
/// of properties recipes actually key on — rarity, item power band, GA count, open affix slot.
/// Every gate, category, material, and outcome line mirrors the verified S14 recipe research and
/// the in-game recipe screen's own grouping.</summary>
public class CubeSimulatorTests
{
    private static CubeSimItem Item(string rarity, bool p850 = false, int ga = 0, bool open = true)
        => new(rarity, p850, ga, open);

    private static CubeSimResult Recipe(CubeSimItem it, string name)
        => CubeSimulator.Evaluate(it).Single(x => x.Name == name);

    [Fact]
    public void Add_affix_needs_an_open_slot_and_lists_its_materials_structured()
    {
        var add = Recipe(Item("Magic", ga: 1), "Add Affix");
        Assert.True(add.Eligible);
        // structured materials, not a loose string: Coarse ×1 + Raw ×5 (+ optional Tuning Prism)
        Assert.Contains(add.Materials, m => m.Name.Contains("Coarse") && m.Quantity == 1);
        Assert.Contains(add.Materials, m => m.Name.Contains("Raw") && m.Quantity == 5);
        Assert.Contains(add.Materials, m => m.Name.Contains("Tuning Prism") && m.Optional);
        Assert.Contains(add.Outcome, l => l.Contains("Greater Affix stays"));
    }

    [Fact]
    public void Full_item_cannot_add_affix_and_says_why()
    {
        var add = Recipe(Item("Magic", open: false), "Add Affix");
        Assert.False(add.Eligible);
        Assert.Contains("open", add.Requirement, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rare_upgrades_to_legendary_with_full_reroll_warning()
    {
        var up = Recipe(Item("Rare", ga: 1), "Upgrade to Legendary");
        Assert.True(up.Eligible);
        Assert.Contains(up.Outcome, l => l.Contains("ALL affixes reroll"));
        Assert.Contains(up.Outcome, l => l.Contains("does NOT carry"));   // the GA truth
        Assert.Contains(up.Materials, m => m.Name.Contains("Pure") && m.Quantity == 1);
        Assert.False(Recipe(Item("Legendary"), "Upgrade to Legendary").Eligible);
    }

    [Fact]
    public void Mythic_gamble_needs_an_850_plus_unique()
    {
        var low = Recipe(Item("Unique", p850: false), "Upgrade to Mythic");
        Assert.False(low.Eligible);
        Assert.Contains("850", low.Requirement);
        var high = Recipe(Item("Unique", p850: true), "Upgrade to Mythic");
        Assert.True(high.Eligible);
        Assert.Contains(high.Materials, m => m.Name.Contains("Pandemonium") && m.Quantity == 5);
    }

    [Fact]
    public void Transfigure_warns_it_likely_becomes_unmodifiable()
    {
        var t = Recipe(Item("Legendary"), "Transfigure");
        Assert.True(t.Eligible);
        // matches the in-game wording: "very high chance to become Unmodifiable"
        Assert.Contains("Unmodifiable", t.Warning, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains(t.Materials, m => m.Name.Contains("Tuning Prism") && m.Optional);
        Assert.False(Recipe(Item("Rare"), "Transfigure").Eligible);
    }

    [Fact]
    public void Reroll_and_remove_recipes_carry_the_games_tuning_prism_rules()
    {
        // Focused Reroll REQUIRES a Tuning Prism; Chaotic and Remove take it as OPTIONAL.
        Assert.Contains(Recipe(Item("Rare"), "Focused Reroll").Materials, m => m.Name.Contains("Tuning Prism") && !m.Optional);
        Assert.Contains(Recipe(Item("Rare"), "Chaotic Reroll").Materials, m => m.Name.Contains("Tuning Prism") && m.Optional);
        Assert.Contains(Recipe(Item("Rare"), "Remove Affix").Materials, m => m.Name.Contains("Tuning Prism") && m.Optional);
    }

    [Fact]
    public void Remove_affix_warns_it_can_hit_your_greater_affix()
    {
        // Remove Affix targets a RANDOM affix, so with a GA on the item it's a real risk (honest edge).
        var withGa = Recipe(Item("Rare", ga: 1), "Remove Affix");
        Assert.Contains("Greater Affix", withGa.Warning);
        // no GA on the item → no scary warning
        Assert.Equal("", Recipe(Item("Rare", ga: 0), "Remove Affix").Warning);
    }

    [Fact]
    public void Remove_affix_is_magic_and_rare_only_and_multis_declare_their_count()
    {
        Assert.True(Recipe(Item("Rare"), "Remove Affix").Eligible);
        Assert.False(Recipe(Item("Legendary"), "Remove Affix").Eligible);
        Assert.Contains("3", Recipe(Item("Unique"), "Recycle Uniques").MultiNote);   // 3 identical
    }

    [Fact]
    public void Recipes_are_grouped_into_the_two_in_game_categories()
    {
        var all = CubeSimulator.Evaluate(Item("Magic"));
        Assert.All(all, r => Assert.Contains(r.Category,
            new[] { CubeSimulator.GearModification, CubeSimulator.ItemTransmutation }));
        // Add Affix is Gear Modification; the upgrades are Item Transmutation (matches the game).
        Assert.Equal(CubeSimulator.GearModification, Recipe(Item("Magic"), "Add Affix").Category);
        Assert.Equal(CubeSimulator.ItemTransmutation, Recipe(Item("Rare"), "Upgrade to Legendary").Category);
    }

    [Fact]
    public void Mats_line_renders_from_structured_materials()
    {
        var add = Recipe(Item("Magic"), "Add Affix");
        Assert.Equal("1× Coarse Primordial Dust + 5× Raw Primordial Dust + 1× Tuning Prism (optional)", add.MatsLine);
        // recipes with no dust say so rather than showing an empty bundle
        Assert.Equal("no dust — items only", Recipe(Item("Rare"), "3-to-1 Transmutation").MatsLine);
    }
}
