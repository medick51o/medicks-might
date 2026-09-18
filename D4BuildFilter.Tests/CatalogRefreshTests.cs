using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Tests;

public class CatalogRefreshTests
{
    // Each row fails when the seal repair is reverted: the broad Talisman_Seal_ exclusion
    // previously omitted all three. Exact key spelling and ID catch remapping/transcription.
    [Theory]
    [InlineData("Seal of the Severed Finger", 0x280339u)]
    [InlineData("Seal of the Golden Epiphany", 0x28033bu)]
    [InlineData("Seal of the Diamond Mind", 0x28033du)]
    public void Mythic_seals_resolve_to_their_exact_names_and_ids(string name, uint id)
    {
        Assert.True(UniqueDatabase.TryGet(name, out var actual));
        Assert.Equal(id, actual);
        Assert.Contains(UniqueDatabase.ByName, entry => entry.Key == name && entry.Value == id);
    }

    // Baseline preservation guard: a full additive-refresh revert still passes. A rebuild
    // from tier 2 alone loses the tier-4 entries; renamed/remapped entries also fail.
    // Shroud of Khanduras is tier 2 now but remains part of this historical roster.
    [Theory]
    [InlineData("Doombringer", 0x035f59u)]
    [InlineData("The Grandfather", 0x036827u)]
    [InlineData("Andariel's Visage", 0x03b10au)]
    [InlineData("Ahavarion, Spear of Lycander", 0x057afdu)]
    [InlineData("Harlequin Crest", 0x094e1cu)]
    [InlineData("Melted Heart of Selig", 0x13781fu)]
    [InlineData("Ring of Starless Skies", 0x13eee2u)]
    [InlineData("Tyrael's Might", 0x1d03acu)]
    [InlineData("Shroud of Khanduras", 0x1d8465u)]
    [InlineData("Doombringer (Crucible)", 0x27b52fu)]
    [InlineData("The Grandfather (Crucible)", 0x27b547u)]
    public void Historical_mythic_roster_keeps_its_exact_names_and_ids(string name, uint id)
    {
        Assert.True(UniqueDatabase.TryGet(name, out var actual));
        Assert.Equal(id, actual);
        Assert.Contains(UniqueDatabase.ByName, entry => entry.Key == name && entry.Value == id);
    }

    // Each row fails when the gear refresh is reverted: these names were absent.
    // Exact IDs also catch copying a charm ID or selecting the wrong item form.
    [Theory]
    [InlineData("Leoric's Crown", 0x28646bu)]
    [InlineData("Stone of Jordan", 0x28647eu)]
    [InlineData("In-Geom", 0x286476u)]
    [InlineData("Nemesis Bracers", 0x286472u)]
    [InlineData("Squirt's Blouse", 0x28646eu)]
    [InlineData("The Furnace", 0x0368e9u)]
    [InlineData("Messerschmidt's Reaver", 0x286474u)]
    [InlineData("Arioc's Needle", 0x286478u)]
    [InlineData("Henri's Perquisition", 0x28647au)]
    public void Season_15_gear_resolves_to_the_verified_id(string name, uint id)
    {
        Assert.True(UniqueDatabase.TryGet(name, out var actual));
        Assert.Equal(id, actual);
    }

    // Baseline preservation guard; passes a full revert, fails a merge by display name
    // that assigns either form's ID to both catalogs or drops one form.
    [Fact]
    public void Banished_lords_talisman_keeps_distinct_gear_and_charm_forms()
    {
        Assert.True(UniqueDatabase.TryGet("Banished Lord's Talisman", out var gearId));
        Assert.Equal(0x17d2dcu, gearId);
        Assert.True(UniqueCharmDatabase.TryGetByName("Banished Lord's Talisman", out var charm));
        Assert.Equal(0x276db6u, charm.Id);
        Assert.Equal("Banished Lord's Talisman", charm.Name);
        Assert.NotEqual(gearId, charm.Id);
    }

    // Charm presence fails on revert; gear absence catches misclassification, but passes
    // on revert because neither catalog previously contained Annihilus.
    [Fact]
    public void Annihilus_is_a_standalone_charm_only()
    {
        Assert.True(UniqueCharmDatabase.TryGetByName("Annihilus", out var charm));
        Assert.Equal(0x28f5c2u, charm.Id);
        Assert.Equal("Annihilus", UniqueCharmDatabase.ById[0x28f5c2u].Name);
        Assert.DoesNotContain(0x28f5c2u, UniqueDatabase.ByName.Values);
        Assert.False(UniqueDatabase.ByName.ContainsKey("Annihilus"));
    }

    // Fails on revert or a tier-2-only charm refresh: this standalone charm is tier 4.
    [Fact]
    public void Hellfire_torch_survives_the_charm_tier_union()
    {
        Assert.True(UniqueCharmDatabase.TryGetByName("Hellfire Torch", out var charm));
        Assert.Equal(0x28f523u, charm.Id);
    }

    // Safety guard; passes a full revert, fails an unfiltered import of either cosmetic ID.
    [Theory]
    [InlineData(0x14d564u)]
    [InlineData(0x23b6dfu)]
    public void Unproven_cosmetics_are_absent_from_both_catalogs(uint id)
    {
        Assert.DoesNotContain(id, UniqueDatabase.ByName.Values);
        Assert.DoesNotContain(UniqueCharmDatabase.All, charm => charm.Id == id);
    }

    // Baseline preservation guard; passes a full revert, fails replacing the catalog
    // wholesale with the new dump, which lacks each of these five shipped IDs.
    [Theory]
    [InlineData("Cleansing Prayer", 0x1beb34u)]
    [InlineData("Iron Horn", 0x1c608bu)]
    [InlineData("Skatsimi Tome", 0x1c8956u)]
    [InlineData("Bucrani's Visage", 0x1f3434u)]
    [InlineData("Egg", 0x273c4bu)]
    public void Shipped_entries_absent_from_the_dump_are_retained(string name, uint id)
    {
        Assert.True(UniqueDatabase.TryGet(name, out var actual));
        Assert.Equal(id, actual);
    }

    // Baseline preservation guard; passes a full revert. Fails any additional duplicate
    // ID, an extra alias on an allowed ID, or removal/renaming of a deliberate alias pair.
    [Fact]
    public void Gear_duplicate_ids_are_exactly_the_six_existing_spelling_alias_pairs()
    {
        // Escapes preserve the exact shipped mojibake/NBSP spellings visibly.
        var permitted = new Dictionary<uint, string[]>
        {
            [0x1ec38bu] = new[] { "Mj\u00c3\u00b6lnic Ryng", "Mjölnic Ryng" },
            [0x1ee2b3u] = new[] { "Sunstained\u00c2\u00a0War-Crozier", "Sunstained War-Crozier" },
            [0x1fc162u] = new[] { "Bane of\u00c2\u00a0Ahjad-Den", "Bane of Ahjad-Den" },
            [0x2410e1u] = new[] { "Kilt of\u00c2\u00a0Blackwing", "Kilt of Blackwing" },
            [0x2410edu] = new[] { "Galvanic\u00c2\u00a0Azurite", "Galvanic Azurite" },
            [0x274088u] = new[] { "Coop de Gr\u00c3\u00a2ce", "Coop de Grâce" },
        };
        var duplicates = UniqueDatabase.ByName.GroupBy(entry => entry.Value)
            .Where(group => group.Count() > 1).ToArray();

        Assert.Equal(permitted.Count, duplicates.Length);
        foreach (var group in duplicates)
        {
            Assert.True(permitted.TryGetValue(group.Key, out var names),
                $"Unexpected duplicate gear ID 0x{group.Key:x6}.");
            Assert.Equal(names!.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
                group.Select(entry => entry.Key).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        }
    }

    // Structural guard; passes a full revert, fails a duplicated charm row/ID.
    [Fact]
    public void Charm_ids_are_unique()
    {
        Assert.Equal(UniqueCharmDatabase.All.Count,
            UniqueCharmDatabase.All.Select(charm => charm.Id).Distinct().Count());
    }

    // Structural guard; passes a full revert, fails copying a gear name/ID pair into
    // the charm catalog (or vice versa) instead of using that form's distinct ID.
    [Fact]
    public void No_name_and_id_pair_is_shared_between_gear_and_charms()
    {
        Assert.All(UniqueCharmDatabase.All, charm =>
            Assert.DoesNotContain(UniqueDatabase.ByName, gear =>
                gear.Value == charm.Id &&
                string.Equals(gear.Key, charm.Name, StringComparison.OrdinalIgnoreCase)));
    }
}
