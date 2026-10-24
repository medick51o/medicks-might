using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Core.Tests;

public class CatalogAndTierTests
{
    [Fact]
    public void Rogue_shared_aspects_are_generic_and_not_filter_item_ids()
    {
        Assert.Equal(5, AspectCatalog.Genericized.Count);
        Assert.All(AspectCatalog.Genericized, aspect => Assert.True(AspectCatalog.IsGeneric(aspect)));
        Assert.Contains(AspectCatalog.Genericized, aspect => aspect.Name == "Earthstriker's");
        Assert.Contains(AspectCatalog.Genericized, aspect => aspect.Name == "Duelist's");
        Assert.Contains(AspectCatalog.Genericized, aspect => aspect.Name == "Neurotoxic");
        Assert.Contains(AspectCatalog.Genericized, aspect => aspect.Name == "Snowveiled");
        Assert.Contains(AspectCatalog.Genericized, aspect => aspect.Name == "Crushing");
    }

    [Fact]
    public void Grief_infinity_and_abyssal_splinters_resolve_to_real_item_ids()
    {
        Assert.Contains(0x28d6cbu, RunewordCatalog.IdsForName("Grief"));
        Assert.Contains(0x28fe54u, RunewordCatalog.IdsForName("Grief"));
        Assert.Contains(0x28fba5u, RunewordCatalog.IdsForName("Infinity"));
        Assert.True(UniqueDatabase.TryGet("Enigma", out var enigma));
        Assert.Equal(0x28d642u, enigma);
        Assert.True(SoulSplinterCatalog.TryGet("Abyssal Splinter of Hellfire", out var hellfire));
        Assert.Equal(0x28a12eu, hellfire);
        Assert.True(SoulSplinterCatalog.TryGet("Abyssal Splinter of Pain", out _));
        Assert.True(SoulSplinterCatalog.TryGet("Abyssal Splinter of the Black Soulstone", out _));
    }

    [Fact]
    public void Gear_cowl_and_charm_cowl_stay_different_ids()
    {
        Assert.True(UniqueDatabase.TryGet("Cowl of the Nameless", out var gear));
        Assert.Equal(0x078d8du, gear);
        Assert.True(UniqueCharmDatabase.TryGetByName("Cowl of the Nameless", out var charm));
        Assert.Equal(0x2783f8u, charm.Id);
        Assert.NotEqual(gear, charm.Id);
    }

    [Fact]
    public void Greater_affix_heuristic_rejects_unique_and_tempered_ids()
    {
        Assert.True(AffixGreater.GreaterAffixPossible("Affix_Strength"));
        Assert.False(AffixGreater.GreaterAffixPossible("Helm_Unique_Generic_003"));
        Assert.False(AffixGreater.GreaterAffixPossible("Tempered_Armor"));
        Assert.False(AffixGreater.GreaterAffixPossible(null));
    }

    [Fact]
    public void Personal_groups_sort_s_a_b_c_then_custom_and_leave_community_tiers_alone()
    {
        FavoriteEntry Entry(string name, string? group, int order) => new(
            name, "https://example.test/" + name, "Maxroll", "Endgame", "S",
            name, "Rogue", DateTime.UtcNow, DateTime.UtcNow,
            PersonalGroup: group, PersonalOrder: order);

        var groups = PersonalTierList.Group(
        [
            Entry("Zed", null, 0),
            Entry("Custom", "Farm", 0),
            Entry("Bee", "B", 1),
            Entry("Aye", "A", 0),
            Entry("Cee", "C", 0),
            Entry("Ess", "S", 2),
            Entry("Ess2", "s", 1),
        ]);

        Assert.Equal(["S", "A", "B", "C", "Farm", "Unranked"], groups.Select(group => group.Name));
        Assert.Equal(["Ess2", "Ess"], groups[0].Entries.Select(entry => entry.Name));
        Assert.Equal("S", groups[0].Entries[0].Tier); // community tier is untouched
        Assert.Equal(["Zed"], groups[^1].Entries.Select(entry => entry.Name));
    }

    [Fact]
    public void Old_favorite_json_without_personal_fields_still_loads()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fav-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """
            [
              {
                "Id": "1",
                "Url": "https://example.test/old",
                "Source": "Maxroll",
                "TierKind": "Endgame",
                "Tier": "A",
                "Name": "Old",
                "ClassName": "Rogue",
                "DateAdded": "2026-01-01T00:00:00Z",
                "DateLastOpened": "2026-01-02T00:00:00Z"
              }
            ]
            """);
            var loaded = Assert.Single(new FavoritesStore(path).All);
            Assert.Null(loaded.PersonalGroup);
            Assert.Equal(0, loaded.PersonalOrder);
            Assert.Equal("A", loaded.Tier);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
