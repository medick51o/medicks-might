using System;
using System.Linq;
using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Tests;

public class PerSlotTests
{
    [Theory]
    [InlineData("Boots", 0x0006d170u)]
    [InlineData("boots", 0x0006d170u)]
    [InlineData("Ring 2", 0x0006d174u)]      // trailing index stripped
    [InlineData("chest-armor", 0x0006d16du)] // mobalytics kebab
    [InlineData("Chest", 0x0006d16du)]       // maxroll item-id prefix
    [InlineData("1HMace", 0x0006d13au)]      // maxroll exact weapon type
    [InlineData("2HSword", 0x0006d14fu)]
    [InlineData("Helm", 0x0006d16eu)]
    public void ResolveSlot_maps_known_slots(string label, uint id)
    {
        var ids = ItemTypeDatabase.ResolveSlot(label);
        Assert.NotNull(ids);
        Assert.Contains(id, ids!);
    }

    [Fact]
    public void ResolveSlot_returns_null_for_unknown() =>
        Assert.Null(ItemTypeDatabase.ResolveSlot("Mercenary Slot"));

    [Fact]
    public void Weapon_slots_merge_by_handedness_armor_stays_separate()
    {
        // Barb arsenal: 1 one-hander + 2 two-handers + 2 armor slots. Weapons merge BY HANDEDNESS —
        // one "1H Weapons" pool and one "2H Weapons" pool (not per weapon type); armor stays separate.
        var rb = new ResolvedBuild("t", "Barbarian", new[]
        {
            new ResolvedVariant("v", new[] { "Strength" }, Array.Empty<string>(), new[]
            {
                new ResolvedSlot("1HMace",  new[] { "Strength", "Critical Strike Damage Multiplier", "Vulnerable Damage Multiplier" }),
                new ResolvedSlot("2HSword", new[] { "Strength", "Critical Strike Damage Multiplier", "Weapon Damage" }),
                new ResolvedSlot("2HAxe",   new[] { "Strength", "Vulnerable Damage Multiplier", "Weapon Damage" }),
                new ResolvedSlot("Boots",   new[] { "Strength", "Maximum Life", "Armor", "Movement Speed" }),
                new ResolvedSlot("Helm",    new[] { "Strength", "Maximum Life", "Cooldown Reduction" }),
            }),
        });
        var b = FilterCompiler.Analyze(rb, FilterColors.Gold, FilterColors.Silver);

        var oneH = b.SlotPools.Single(sp => sp.Label == "1H Weapons");
        var twoH = b.SlotPools.Single(sp => sp.Label == "2H Weapons");
        Assert.Single(oneH.ItemTypeIds);                         // 1HMace only
        Assert.Equal(2, twoH.ItemTypeIds.Count);                 // 2HSword + 2HAxe merged
        Assert.Equal(2, b.SlotPools.Count(sp => !sp.Label.EndsWith("Weapons")));  // Boots + Helm
        Assert.DoesNotContain(b.SlotPools, sp => sp.Label is "1HMace" or "2HSword" or "2HAxe");
    }

    [Fact]
    public void Per_slot_rules_scope_each_rule_to_its_item_type()
    {
        var rb = new ResolvedBuild("t", "Barbarian", new[]
        {
            new ResolvedVariant("v",
                new[] { "Strength", "Maximum Life" },
                Array.Empty<string>(),
                new[]
                {
                    new ResolvedSlot("Boots", new[] { "Strength", "Maximum Life", "Armor", "Movement Speed" }),
                    new ResolvedSlot("Helm", new[] { "Strength", "Maximum Life", "Cooldown Reduction" }),
                }),
        });
        var b = FilterCompiler.Analyze(rb, FilterColors.Gold, FilterColors.Silver);
        Assert.True(b.SlotPools.Count >= 2);

        var o = FilterCompiler.Compile(new[] { b }, new FilterOptions { PerSlotRules = true }, "t");
        Assert.True(o.RoundTripOk);

        var dec = FilterDecoder.Decode(o.ImportCode);
        uint bootsType = ItemTypeDatabase.ResolveSlot("Boots")![0];
        var bootsRule = dec.Rules.FirstOrDefault(r => r.Conditions.Any(c => c.Type == 5 && c.Ids.Contains(bootsType)));
        Assert.NotNull(bootsRule);                                       // a rule scoped to Boots exists
        Assert.Contains(bootsRule!.Conditions, c => c.Type == 6);        // ANDed with an affix condition
    }

    [Fact]
    public void Per_slot_emits_gold_3plus_and_silver_2plus_tiers_for_a_rich_slot()
    {
        // A slot with >=3 ideal affixes produces two distinct rules: Gold [3+] and Silver [2+].
        var rb = new ResolvedBuild("t", "Barbarian", new[]
        {
            new ResolvedVariant("v", new[] { "Strength" }, Array.Empty<string>(),
                new[] { new ResolvedSlot("Boots", new[] { "Strength", "Maximum Life", "Armor", "Movement Speed" }) }),
        });
        var b = FilterCompiler.Analyze(rb, FilterColors.Gold, FilterColors.Silver);

        var both = FilterCompiler.Compile(new[] { b }, new FilterOptions { PerSlotRules = true, GoldTier = true, SilverTier = true }, "t");
        var goldOnly = FilterCompiler.Compile(new[] { b }, new FilterOptions { PerSlotRules = true, GoldTier = true, SilverTier = false }, "t");

        Assert.Equal(goldOnly.RuleCount + 1, both.RuleCount);   // Silver adds exactly one rule for this one slot

        var mins = FilterDecoder.Decode(both.ImportCode).Rules
            .SelectMany(r => r.Conditions).Where(c => c.Type == 6).Select(c => (uint)c.MaskOrCount!.Value).ToList();
        Assert.Contains(3u, mins);   // Gold tier
        Assert.Contains(2u, mins);   // Silver tier
    }

    [Fact]
    public void Per_slot_silver_is_skipped_when_it_would_duplicate_gold()
    {
        // A slot with only 2 ideal affixes: Gold [2+] and Silver [2+] would be identical, so only one fires.
        var rb = new ResolvedBuild("t", "Barbarian", new[]
        {
            new ResolvedVariant("v", new[] { "Strength" }, Array.Empty<string>(),
                new[] { new ResolvedSlot("Boots", new[] { "Strength", "Maximum Life" }) }),
        });
        var b = FilterCompiler.Analyze(rb, FilterColors.Gold, FilterColors.Silver);

        var both = FilterCompiler.Compile(new[] { b }, new FilterOptions { PerSlotRules = true, GoldTier = true, SilverTier = true }, "t");
        var goldOnly = FilterCompiler.Compile(new[] { b }, new FilterOptions { PerSlotRules = true, GoldTier = true, SilverTier = false }, "t");

        Assert.Equal(goldOnly.RuleCount, both.RuleCount);   // no duplicate Silver rule added
    }

    [Fact]
    public void Per_slot_falls_back_to_combined_when_no_slot_data()
    {
        // Pasted builds carry no slot breakdown; PerSlotRules must still produce a valid filter.
        var rb = PastedBuild.Parse("Strength\nMaximum Life\nCritical Strike Chance", "p");
        var b = FilterCompiler.Analyze(rb, FilterColors.Gold, FilterColors.Silver);
        Assert.Empty(b.SlotPools);
        var o = FilterCompiler.Compile(new[] { b }, new FilterOptions { PerSlotRules = true }, "t");
        Assert.True(o.RoundTripOk);
    }
}
