using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Tests;

public class CharmLeakV120Tests
{
    [Theory]
    [InlineData("1HDagger", "Dagger")]
    [InlineData("1HFocus", "Focus")]
    [InlineData("1HScythe", "Scythe")]
    [InlineData("1HShield", "Shield")]
    [InlineData("1HTotem", "Totem")]
    [InlineData("1HWand", "Wand")]
    [InlineData("2HBow", "Bow")]
    public void Live_maxroll_gear_prefixes_resolve_to_their_existing_item_family(string prefix, string family)
    {
        var ids = ItemTypeDatabase.ResolveSlot(prefix);
        Assert.NotNull(ids);
        Assert.Equal(new[] { ItemTypeDatabase.ByName[family] }, ids!);
    }

    [Fact]
    public void Maxroll_rogue_bow_and_dual_daggers_compile_per_slot_without_unresolved_warning()
    {
        var inner = """
        {"profiles":[{"name":"Main","items":{"0":"1","1":"2"}}],
         "items":{
           "1":{"id":"2HBow_Legendary_Generic_001","explicits":[{"nid":1083197},{"nid":1227911},{"nid":1439265}]},
           "2":{"id":"1HDagger_Unique_Rogue_003_x2","explicits":[{"nid":1193845},{"nid":1829554},{"nid":1439265}]}}}
        """;
        var raw = $$"""{"name":"Rogue Weapons","class":"Rogue","data":{{System.Text.Json.JsonSerializer.Serialize(inner)}}}""";
        var parsed = MaxrollFetcher.Parse(raw, NameLookup.Default(), UniqueLookup.Default());
        var build = FilterCompiler.Analyze(parsed, FilterColors.Red, FilterColors.Pink);

        Assert.Empty(build.UnresolvedSlotPools);
        Assert.Contains(build.SlotPools, slot => slot.Label == "2H Weapons"
            && slot.ItemTypeIds.SequenceEqual(ItemTypeDatabase.ResolveSlot("Bow")!));
        Assert.Contains(build.SlotPools, slot => slot.Label == "1H Weapons"
            && slot.ItemTypeIds.SequenceEqual(ItemTypeDatabase.ResolveSlot("Dagger")!));

        var output = FilterCompiler.Compile([build], new FilterOptions
        {
            PerSlotRules = true,
            ItemPowerTiers = false,
            GreaterAffixes = false,
        }, "rogue-weapons");
        Assert.DoesNotContain(output.Diagnostics, d => d.Contains("Combined rules", StringComparison.Ordinal));
        var decoded = FilterDecoder.Decode(output.ImportCode);
        Assert.Contains(decoded.Rules, r => r.Conditions.Any(c => c.Type == 5
            && c.Ids.Contains(ItemTypeDatabase.ByName["Bow"])));
        Assert.Contains(decoded.Rules, r => r.Conditions.Any(c => c.Type == 5
            && c.Ids.Contains(ItemTypeDatabase.ByName["Dagger"])));
    }

    [Fact]
    public void Maxroll_dual_source_stats_fold_across_selected_profiles()
    {
        // Reverting the build-wide fold leaves Crit Chance out of profile B's Boots pool, so a 2-native-plus-Crit item can be hidden.
        var inner = """
        {"profiles":[
           {"name":"Chest profile","items":{"0":"1"}},
           {"name":"Boots profile","items":{"1":"2","10":"3"}}
         ],
         "items":{
           "1":{"id":"Chest_Legendary_001","explicits":[{"nid":1193845}]},
           "2":{"id":"Boots_Legendary_001","explicits":[{"nid":1829554},{"nid":1439265}]},
           "3":{"id":"Talisman_Charm_Set_Rogue_01_01","explicits":[{"nid":1193845}]}}}
        """;
        var raw = $$"""{"name":"Cross Profile","class":"Rogue","data":{{System.Text.Json.JsonSerializer.Serialize(inner)}}}""";

        var resolved = MaxrollFetcher.Parse(raw, NameLookup.Default(), UniqueLookup.Default());
        var build = FilterCompiler.Analyze(resolved, FilterColors.Red, FilterColors.Pink);
        var boots = Assert.Single(build.SlotPools, slot => slot.Label == "Boots");
        var crit = AffixMapper.Map("Critical Strike Chance").CoarseId!.Value;

        Assert.Contains(crit, boots.AffixIds);
        Assert.Equal(3, boots.AffixIds.Count);
        var output = FilterCompiler.Compile([build], new FilterOptions
        {
            PerSlotRules = true,
            ItemPowerTiers = false,
            GreaterAffixes = false,
        }, "cross-profile");
        var bootsLegendary = FilterDecoder.Decode(output.ImportCode).Rules.Single(r =>
            r.Conditions.Any(c => c.Type == 5 && c.Ids.Intersect(boots.ItemTypeIds).Any())
            && r.Conditions.Any(c => c.Type == 1 && c.MaskOrCount == Rarity.Legendary));
        Assert.True(TierMatches(bootsLegendary, boots.AffixIds.ToHashSet()));
    }

    [Fact]
    public void Maxroll_unknown_gear_forces_combined_fallback_but_charm_only_does_not()
    {
        // Reverting the unknown-item guard makes the future Boots stat look charm-only, erasing both the fallback and its warning.
        var inner = """
        {"profiles":[{"name":"Main","items":{"0":"1","1":"2","10":"3"}}],
         "items":{
           "1":{"id":"Chest_Legendary_001","explicits":[{"nid":1083197},{"nid":1227911},{"nid":1439265}]},
           "2":{"id":"S15_Rogue_NewBootType_001","explicits":[{"nid":1193845}]},
           "3":{"id":"Talisman_Charm_Set_Rogue_01_01","explicits":[{"nid":1193845}]}}}
        """;
        var raw = $$"""{"name":"Future Gear","class":"Rogue","data":{{System.Text.Json.JsonSerializer.Serialize(inner)}}}""";
        var parsed = MaxrollFetcher.Parse(raw, NameLookup.Default(), UniqueLookup.Default());
        var build = FilterCompiler.Analyze(parsed, FilterColors.Red, FilterColors.Pink);

        Assert.Contains("S15_Rogue_NewBootType_001", build.UnresolvedSlotPools);
        Assert.Contains(AffixMapper.Map("Critical Strike Chance").CoarseId!.Value, build.Pool);
        var output = FilterCompiler.Compile([build], new FilterOptions
        {
            PerSlotRules = true,
            ItemPowerTiers = false,
            GreaterAffixes = false,
        }, "unknown-slot");
        Assert.Contains(output.Diagnostics, d => d.Contains("Combined rules", StringComparison.Ordinal));
        Assert.DoesNotContain(FilterDecoder.Decode(output.ImportCode).Rules.Where(r => r.Conditions.Any(c => c.Type == 6)),
            r => r.Conditions.Any(c => c.Type == 5));

        var charmOnlyInner = """
        {"profiles":[{"name":"Main","items":{"10":"1"}}],
         "items":{"1":{"id":"Talisman_Charm_Set_Rogue_01_01","explicits":[{"nid":1193845}]}}}
        """;
        var charmOnlyRaw = $$"""{"name":"Charm Only","class":"Rogue","data":{{System.Text.Json.JsonSerializer.Serialize(charmOnlyInner)}}}""";
        var charmOnly = FilterCompiler.Analyze(
            MaxrollFetcher.Parse(charmOnlyRaw, NameLookup.Default(), UniqueLookup.Default()),
            FilterColors.Red, FilterColors.Pink);
        Assert.Empty(charmOnly.UnresolvedSlotPools);
    }

    [Theory]
    [InlineData("Talisman_Charm_Set_Rogue_01_01")]
    [InlineData("Talisman_Seal_Legendary")]
    [InlineData("Generic_Charm_Flippy")]
    public void Maxroll_charms_and_seals_remain_intentionally_slotless(string itemId)
    {
        var inner = """
        {"profiles":[{"name":"Main","items":{"10":"1"}}],
         "items":{"1":{"id":"__ITEM_ID__","explicits":[{"nid":1193845}]}}}
        """.Replace("__ITEM_ID__", itemId, StringComparison.Ordinal);
        var raw = $$"""{"name":"Slotless","class":"Rogue","data":{{System.Text.Json.JsonSerializer.Serialize(inner)}}}""";
        var parsed = MaxrollFetcher.Parse(raw, NameLookup.Default(), UniqueLookup.Default());
        var variant = Assert.Single(parsed.Variants);
        var build = FilterCompiler.Analyze(parsed, FilterColors.Red, FilterColors.Pink);

        Assert.Empty(variant.Affixes);
        Assert.Empty(variant.Slots!);
        Assert.Empty(build.UnresolvedSlotPools);
        Assert.Empty(build.Pool);
    }

    [Fact]
    public void Unknown_slot_fallback_warning_is_single_build_only()
    {
        static CompiledBuild Build(string name) => FilterCompiler.Analyze(new ResolvedBuild(name, "Rogue",
        [
            new ResolvedVariant("Main", ["Maximum Life"], [],
            [
                new ResolvedSlot("S15_NewBootType", ["Maximum Life"]),
            ]),
        ]), FilterColors.Red, FilterColors.Pink);

        var unknown = Build("Unknown Slot");
        var healthy = FilterCompiler.Analyze(new ResolvedBuild("Healthy", "Rogue",
        [
            new ResolvedVariant("Main", ["Maximum Life"], [],
            [
                new ResolvedSlot("Boots", ["Maximum Life"]),
            ]),
        ]), FilterColors.Red, FilterColors.Pink);
        var options = new FilterOptions { PerSlotRules = true, ItemPowerTiers = false, GreaterAffixes = false };

        var single = FilterCompiler.Compile([unknown], options, "single");
        Assert.Contains(single.Diagnostics, d => d.Contains("Combined rules", StringComparison.Ordinal));

        var multi = FilterCompiler.Compile([unknown, healthy], options, "multi");
        Assert.DoesNotContain(multi.Diagnostics, d => d.Contains("Combined rules", StringComparison.Ordinal));
    }


    [Fact]
    public void Maxroll_slotless_stats_do_not_enter_gear_but_dual_source_stats_cover_every_slot()
    {
        var inner = """
        {"profiles":[{"name":"Main","items":{"0":"1","1":"2","10":"3"}}],
         "items":{
           "1":{"id":"Chest_Legendary_001","explicits":[{"nid":1083197},{"nid":1227911},{"nid":1193845}]},
           "2":{"id":"Boots_Legendary_001","explicits":[{"nid":1829554},{"nid":1439265}]},
           "3":{"id":"Talisman_Charm_Set_Rogue_01_01","explicits":[{"nid":1639556},{"nid":1193845}]}}}
        """;
        var raw = $$"""{"name":"Charm Leak","class":"Rogue","data":{{System.Text.Json.JsonSerializer.Serialize(inner)}}}""";

        var resolved = MaxrollFetcher.Parse(raw, NameLookup.Default(), UniqueLookup.Default());
        var variant = Assert.Single(resolved.Variants);
        Assert.DoesNotContain("Poison Resistance", variant.Affixes);
        Assert.All(variant.Slots!, slot => Assert.Contains("Critical Strike Chance", slot.Affixes));

        var build = FilterCompiler.Analyze(resolved, FilterColors.Red, FilterColors.Pink);
        var poison = AffixMapper.Map("Poison Resistance").CoarseId!.Value;
        var crit = AffixMapper.Map("Critical Strike Chance").CoarseId!.Value;
        Assert.DoesNotContain(poison, build.Pool);
        Assert.All(build.SlotPools, slot => Assert.Contains(crit, slot.AffixIds));

        var output = FilterCompiler.Compile([build], new FilterOptions
        {
            PerSlotRules = true,
            ItemPowerTiers = false,
            GreaterAffixes = false,
        }, "slotless");
        Assert.DoesNotContain(output.Diagnostics, d => d.Contains("Per-slot precision was disabled", StringComparison.Ordinal));
        var decoded = FilterDecoder.Decode(output.ImportCode);
        Assert.Contains(decoded.Rules, r => r.Conditions.Any(c => c.Type == 5));

        var twoGearPlusCharm = new HashSet<uint>
        {
            AffixMapper.Map("Strength").CoarseId!.Value,
            AffixMapper.Map("Maximum Life").CoarseId!.Value,
            poison,
        };
        Assert.DoesNotContain(decoded.Rules.Where(r => r.Conditions.Any(c => c.Type == 6)),
            rule => TierMatches(rule, twoGearPlusCharm));
    }

    private static bool TierMatches(DecodedRule rule, IReadOnlySet<uint> itemAffixes)
    {
        var condition = rule.Conditions.Single(c => c.Type == 6);
        return condition.Ids.Count(itemAffixes.Contains) >= (int)condition.MaskOrCount!.Value;
    }
}
