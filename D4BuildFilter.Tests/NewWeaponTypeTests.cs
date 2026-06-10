using System;
using System.Linq;
using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Tests;

/// <summary>Newer-class weapon types: Flail (Paladin/Warlock 1H), Glaive and Quarterstaff
/// (Spiritborn 2H). Before these ids existed, per-slot mode emitted NO rule for those weapon
/// slots and the trailing "Hide the rest" rule then hid the classes' weapon drops in-game
/// (June 2026 season-readiness audit). Ids corroborated from d4data ItemType __snoID__ ×
/// fnuecke names.json; Flail (0x234a98) additionally decoded from two real in-game exports
/// (GameRant Universal + wudijo endgame, D4LootBench reference codes).</summary>
public class NewWeaponTypeTests
{
    private const uint FlailId = 0x00234a98;
    private const uint GlaiveId = 0x00165271;
    private const uint QuarterstaffId = 0x0016d22d;

    [Theory]
    [InlineData("Flail", FlailId)]
    [InlineData("1HFlail", FlailId)]          // maxroll item-id prefix (game id: 1HFlail_Unique_Paladin_003)
    [InlineData("Glaive", GlaiveId)]          // game id: Glaive_Unique_Spiritborn_010_x1
    [InlineData("2HGlaive", GlaiveId)]
    [InlineData("Quarterstaff", QuarterstaffId)]
    [InlineData("2HQuarterstaff", QuarterstaffId)]
    [InlineData("quarterstaff", QuarterstaffId)] // case-insensitive
    [InlineData("Flail 1", FlailId)]          // trailing index stripped
    public void ResolveSlot_maps_new_class_weapons(string label, uint id)
    {
        var ids = ItemTypeDatabase.ResolveSlot(label);
        Assert.NotNull(ids);
        Assert.Equal(new[] { id }, ids!);
    }

    [Theory]
    [InlineData("Flail", FlailId)]
    [InlineData("Glaive", GlaiveId)]
    [InlineData("Quarterstaff", QuarterstaffId)]
    public void ByName_contains_new_weapon_types(string name, uint id) =>
        Assert.Equal(id, ItemTypeDatabase.ByName[name]);

    [Fact]
    public void New_types_count_as_weapon_slots()
    {
        // Weapon-slot merging (1H/2H pooling) must treat them as weapons, not as off-hands.
        Assert.True(ItemTypeDatabase.IsWeaponSlot(new[] { FlailId }));
        Assert.True(ItemTypeDatabase.IsWeaponSlot(new[] { GlaiveId }));
        Assert.True(ItemTypeDatabase.IsWeaponSlot(new[] { QuarterstaffId }));
    }

    [Fact]
    public void Handedness_flail_1h_glaive_quarterstaff_2h()
    {
        Assert.Equal("1h", ItemTypeDatabase.WeaponHandedness(new[] { FlailId }));
        Assert.Equal("2h", ItemTypeDatabase.WeaponHandedness(new[] { GlaiveId, QuarterstaffId }));
        Assert.Equal("", ItemTypeDatabase.WeaponHandedness(new[] { FlailId, GlaiveId })); // mixed spans both
    }

    [Fact]
    public void Spiritborn_glaive_and_quarterstaff_merge_into_one_2h_pool()
    {
        var rb = new ResolvedBuild("t", "Spiritborn", new[]
        {
            new ResolvedVariant("v", new[] { "Maximum Life" }, Array.Empty<string>(), new[]
            {
                new ResolvedSlot("Quarterstaff", new[] { "Maximum Life", "Critical Strike Damage Multiplier", "Vulnerable Damage Multiplier" }),
                new ResolvedSlot("Glaive",       new[] { "Maximum Life", "Critical Strike Damage Multiplier", "Weapon Damage" }),
                new ResolvedSlot("Boots",        new[] { "Maximum Life", "Armor", "Movement Speed" }),
            }),
        });
        var b = FilterCompiler.Analyze(rb, FilterColors.Gold, FilterColors.Silver);

        var twoH = b.SlotPools.Single(sp => sp.Label == "2H Weapons");
        Assert.Equal(2, twoH.ItemTypeIds.Count);                 // Quarterstaff + Glaive merged
        Assert.Contains(QuarterstaffId, twoH.ItemTypeIds);
        Assert.Contains(GlaiveId, twoH.ItemTypeIds);
        Assert.DoesNotContain(b.SlotPools, sp => sp.Label is "Quarterstaff" or "Glaive");
    }

    [Fact]
    public void Paladin_flail_pools_as_1h_weapons_shield_stays_separate()
    {
        var rb = new ResolvedBuild("t", "Paladin", new[]
        {
            new ResolvedVariant("v", new[] { "Strength" }, Array.Empty<string>(), new[]
            {
                new ResolvedSlot("1HFlail", new[] { "Strength", "Critical Strike Damage Multiplier", "Weapon Damage" }),
                new ResolvedSlot("Shield",  new[] { "Strength", "Maximum Life", "Armor" }),
            }),
        });
        var b = FilterCompiler.Analyze(rb, FilterColors.Gold, FilterColors.Silver);

        var oneH = b.SlotPools.Single(sp => sp.Label == "1H Weapons");
        Assert.Equal(new[] { FlailId }, oneH.ItemTypeIds);
        var shield = b.SlotPools.Single(sp => sp.Label == "Shield");   // off-hand keeps its own rule
        Assert.Equal(ItemTypeDatabase.ResolveSlot("Shield")!, shield.ItemTypeIds);
    }

    [Fact]
    public void Per_slot_filter_emits_weapon_rule_above_hide_rest_and_roundtrips()
    {
        // THE audit bug: a Spiritborn weapon slot used to emit no rule, so "Hide the rest"
        // hid every quarterstaff/glaive drop. The compiled filter must now carry a type-5
        // rule with the weapon's id ABOVE the hide rule, and survive the protobuf round-trip.
        var rb = new ResolvedBuild("t", "Spiritborn", new[]
        {
            new ResolvedVariant("v", new[] { "Maximum Life" }, Array.Empty<string>(), new[]
            {
                new ResolvedSlot("Quarterstaff", new[] { "Maximum Life", "Critical Strike Damage Multiplier", "Vulnerable Damage Multiplier" }),
            }),
        });
        var b = FilterCompiler.Analyze(rb, FilterColors.Gold, FilterColors.Silver);
        var o = FilterCompiler.Compile(new[] { b }, new FilterOptions { PerSlotRules = true }, "t");
        Assert.True(o.RoundTripOk);

        var dec = FilterDecoder.Decode(o.ImportCode);
        int weaponRule = dec.Rules.FindIndex(r => r.Conditions.Any(c => c.Type == 5 && c.Ids.Contains(QuarterstaffId)));
        int hideRule = dec.Rules.FindIndex(r => r.Name == "Hide the rest");
        Assert.True(weaponRule >= 0, "no rule scoped to Quarterstaff");
        Assert.True(hideRule >= 0, "hide rule missing");
        Assert.True(weaponRule < hideRule, "weapon rule must outrank the hide rule (first match wins)");
        Assert.Contains(dec.Rules[weaponRule].Conditions, c => c.Type == 6);   // ANDed with affixes

        // The fixed32 id must round-trip bit-exactly through the encoder.
        var ids = dec.Rules[weaponRule].Conditions.Single(c => c.Type == 5).Ids;
        Assert.Equal(new[] { QuarterstaffId }, ids);
    }

    [Fact]
    public void Flail_id_decodes_from_a_real_export_shaped_type5_condition()
    {
        // Mirrors the wudijo/GameRant exports: 0x234a98 sits in a type-5 list alongside the
        // legacy 0x6d1xx ids. Encode the same shape and make sure our decoder reads it back.
        var rule = FilterBuilder.MakeRule("Everything Else", Visibility.HideAll,
            new[] { Conditions.Types(new[] { 0x0006d15du, FlailId, 0x0006d16du }) });
        var dec = FilterDecoder.Decode(FilterBuilder.MakeFilter("X", new[] { rule }));
        var cond = dec.Rules.Single().Conditions.Single(c => c.Type == 5);
        Assert.Equal(new[] { 0x0006d15du, FlailId, 0x0006d16du }, cond.Ids);
    }
}
