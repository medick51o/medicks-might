using System.Linq;
using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Tests;

/// <summary>Locks our decoder against a REAL in-game crit filter (a community export). A rule
/// literally named "Crit Chance" encodes 0x1beace — this is the ground truth that proved our
/// earlier IDs were mislabeled. If the decoder or IDs regress, these break.</summary>
public class DecoderTests
{
    // Real in-game "GR Crit Filter" (rules trimmed to the crit ones).
    private const string CritFilter =
        "CiMKDkFuY2VzdHJhbCBHZWFyEAAdAAD//yIICAAghAcohAcoAQorChBDRG1nIGFuZCBDQ2hhbmNlEAId6CLY" +
        "/yIOCAYVzuobABXS6hsAIAIoAQohCgtDcml0IERhbWFnZRACHeg6Iv8iCQgGFdLqGwAgASgACiEKC0NyaXQg" +
        "Q2hhbmNlEAIdAAD//yIJCAYVzuobACABKAEKUAoXTm8gQ3JpdCBETUcgTXVsdGlwbGllcnMQAh3U6CL/Iiw" +
        "IBhWA/BsAFdAKJwAV+QonABX1CicAFfsKJwAV9wonABX/CicAFf0KJwAgASgA";

    [Fact]
    public void Crit_Chance_rule_uses_0x1beace()
    {
        var f = FilterDecoder.Decode(CritFilter);
        var rule = f.Rules.Single(r => r.Name == "Crit Chance");
        var cond = rule.Conditions.Single(c => c.Type == 6);
        Assert.Contains(0x001beaceu, cond.Ids);
    }

    [Fact]
    public void Crit_Damage_rule_uses_0x1bead2()
    {
        var f = FilterDecoder.Decode(CritFilter);
        var rule = f.Rules.Single(r => r.Name == "Crit Damage");
        Assert.Contains(0x001bead2u, rule.Conditions.Single(c => c.Type == 6).Ids);
    }

    [Fact]
    public void Item_power_range_decodes_min_and_max()
    {
        // The "Ancestral Gear" rule is an Item Power Range (type 0) min 900 / max 900.
        var f = FilterDecoder.Decode(CritFilter);
        var rule = f.Rules.Single(r => r.Name == "Ancestral Gear");
        var cond = rule.Conditions.Single(c => c.Type == 0);
        Assert.Equal(900u, (uint)cond.MaskOrCount!.Value);   // field4 = min
        Assert.Equal(900u, (uint)cond.Max!.Value);            // field5 = max
    }

    [Fact] // Type-9 talisman set bonus: encoder + decoder round-trip
    public void TalismanSetBonus_round_trips_through_filter()
    {
        var ids = new[] { 0x22fb15u, 0x230326u };  // Barb Set 01, Druid Set 01
        var rule = FilterBuilder.MakeRule("My Talismans", Visibility.Recolor,
            new[] { Conditions.RarityMask(Rarity.Talisman), Conditions.TalismanSetBonus(ids) },
            FilterColors.Blue);
        var bytes = FilterBuilder.MakeFilter("TalismanTest", new[] { rule });
        var f = FilterDecoder.Decode(bytes);
        var cond = f.Rules.Single().Conditions.Single(c => c.Type == 9);
        Assert.Equal(ids, cond.Ids.ToArray());
    }

    [Fact] // S14 (3.1): type-9 with per-item refinement — pinned BYTE-FOR-BYTE to Medick's in-game export
    public void TalismanSetBonus_with_items_matches_the_in_game_export_bytes()
    {
        // Ground truth: the "Charms & Seals (Green)" rule hand-built in the 3.1 editor and exported
        // (2026-07-02): Sescheron's Fury (= "Talisman: Barbarian Set 01", 0x22fb15) + its five
        // member charms. field2 carries the set; field3 is a sub-message {1: set, 2: item ×N}.
        var expected = new byte[]
        {
            0x22, 0x27,                        // rule-level condition wrapper (field 4, 39 bytes)
            0x08, 0x09,                        // type = 9
            0x15, 0x15, 0xfb, 0x22, 0x00,      // field2 fixed32: set 0x22fb15
            0x1a, 0x1e,                        // field3 (30 bytes): { set, items… }
            0x0d, 0x15, 0xfb, 0x22, 0x00,      //   f1: set 0x22fb15
            0x15, 0xb5, 0x06, 0x25, 0x00,      //   f2: item 0x2506b5
            0x15, 0x9a, 0x06, 0x25, 0x00,      //   f2: item 0x25069a
            0x15, 0xb8, 0x06, 0x25, 0x00,      //   f2: item 0x2506b8
            0x15, 0xcd, 0x06, 0x25, 0x00,      //   f2: item 0x2506cd
            0x15, 0xa8, 0x06, 0x25, 0x00,      //   f2: item 0x2506a8
        };
        var items = new uint[] { 0x2506b5, 0x25069a, 0x2506b8, 0x2506cd, 0x2506a8 };
        var actual = Conditions.TalismanSetBonus(new[] { (0x22fb15u, (IReadOnlyList<uint>)items) });
        Assert.Equal(expected, actual);
    }

    [Fact] // S14: decoder surfaces the per-item refinement instead of mis-filing it as a GA marker
    public void Decoder_surfaces_talisman_set_items()
    {
        var items = new uint[] { 0x2506b5, 0x25069a, 0x2506b8, 0x2506cd, 0x2506a8 };
        var rule = FilterBuilder.MakeRule("Green", Visibility.Recolor,
            new[] { Conditions.TalismanSetBonus(new[] { (0x22fb15u, (IReadOnlyList<uint>)items) }) },
            FilterColors.Green);
        var f = FilterDecoder.Decode(FilterBuilder.MakeFilter("X", new[] { rule }));
        var cond = f.Rules.Single().Conditions.Single(c => c.Type == 9);
        Assert.Equal(new[] { 0x22fb15u }, cond.Ids.ToArray());          // field2 set list
        var (setId, memberItems) = Assert.Single(cond.SetItems);        // field3 refinement
        Assert.Equal(0x22fb15u, setId);
        Assert.Equal(items, memberItems.ToArray());
        Assert.Empty(cond.GreaterAffixOf);                              // no more mislabeling
    }

    [Fact] // Decoder labels type-9 ids with friendly SetItemBonusDatabase names
    public void Describe_emits_friendly_talisman_set_names()
    {
        var rule = FilterBuilder.MakeRule("Barb Talisman", Visibility.Recolor,
            new[] { Conditions.TalismanSetBonus(new[] { 0x22fb15u }) });
        var bytes = FilterBuilder.MakeFilter("X", new[] { rule });
        var desc = FilterDecoder.Describe(FilterDecoder.Decode(bytes));
        Assert.Contains("Talisman: Barbarian Set 01", desc);
    }
}
