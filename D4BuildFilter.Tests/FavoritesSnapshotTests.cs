using D4BuildFilter.Core;
using System.Text.Json;
using Xunit;

namespace D4BuildFilter.Tests;

public class FavoritesSnapshotTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(),
        $"medicksmight_snapshottest_{Guid.NewGuid():N}.json");

    private static FavoriteEntry Sample(BuildSnapshot? snapshot = null) => new(
        Guid.NewGuid().ToString("N"), "https://maxroll.gg/d4/planner/snapshot", "Maxroll",
        "Endgame", "S", "Test Build", "Barbarian", DateTime.UtcNow, DateTime.UtcNow,
        Snapshot: snapshot);

    [Fact]
    public void Snapshot_round_trips_through_save_and_load()
    {
        var p = TempPath();
        try
        {
            var snapshot = BuildSnapshot.Capture(new ResolvedBuild("Test", "Barbarian",
            [
                new ResolvedVariant("Endgame", ["Strength"], ["The Grandfather"],
                    [new ResolvedSlot("Boots", ["Movement Speed"])], ["Earth Set"])
            ]), new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
            new FavoritesStore(p).Toggle(Sample(snapshot));

            var loaded = Assert.Single(new FavoritesStore(p).All).Snapshot;

            Assert.NotNull(loaded);
            Assert.Equal(snapshot.CapturedUtc, loaded.CapturedUtc);
            var variant = Assert.Single(loaded.Variants);
            Assert.Equal("The Grandfather", Assert.Single(variant.Uniques));
            Assert.Equal("Earth Set", Assert.Single(variant.TalismanSets));
            Assert.Equal("Boots", Assert.Single(variant.Affixes).Slot);
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void Legacy_entry_without_snapshot_loads_with_no_baseline()
    {
        var p = TempPath();
        try
        {
            File.WriteAllText(p, """
            [{
              "Id":"legacy", "Url":"https://example.test/build", "Source":"Maxroll",
              "Name":"Legacy", "ClassName":"Barbarian",
              "DateAdded":"2026-01-01T00:00:00Z", "DateLastOpened":"2026-01-02T00:00:00Z"
            }]
            """);

            var entry = Assert.Single(new FavoritesStore(p).All);

            Assert.Equal("Legacy", entry.Name);
            Assert.Null(entry.Snapshot);
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void New_snapshot_persists_identity_while_legacy_snapshot_identity_remains_unknown()
    {
        var p = TempPath();
        try
        {
            var snapshot = BuildSnapshot.Capture(new ResolvedBuild("Identity Build", "Druid",
            [
                new ResolvedVariant("Endgame", ["Strength"], [])
            ]));
            new FavoritesStore(p).Toggle(Sample(snapshot));

            using (var json = JsonDocument.Parse(File.ReadAllText(p)))
            {
                var saved = json.RootElement[0].GetProperty("Snapshot");
                Assert.Equal("Identity Build", saved.GetProperty("BuildName").GetString());
                Assert.Equal("Druid", saved.GetProperty("ClassName").GetString());
            }

            File.WriteAllText(p, """
            [{
              "Id":"legacy-snapshot", "Url":"https://example.test/legacy-snapshot", "Source":"Maxroll",
              "Name":"Legacy", "ClassName":"Barbarian",
              "DateAdded":"2026-01-01T00:00:00Z", "DateLastOpened":"2026-01-02T00:00:00Z",
              "Snapshot":{
                "CapturedUtc":"2026-01-01T00:00:00Z",
                "Variants":[{
                  "Name":"Endgame",
                  "Affixes":[{"Name":"Strength","Slot":null}],
                  "Uniques":[], "TalismanSets":[]
                }]
              }
            }]
            """);
            var legacy = Assert.IsType<BuildSnapshot>(Assert.Single(new FavoritesStore(p).All).Snapshot);
            var renamed = new ResolvedBuild("Different Build", "Sorcerer",
            [
                new ResolvedVariant("Endgame", ["Strength"], [])
            ]);

            Assert.False(BuildDrift.Compare(legacy, renamed)!.HasDrift);
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void Atomic_write_keeps_old_file_if_interrupted_before_replace()
    {
        var p = TempPath();
        try
        {
            File.WriteAllText(p, "old-complete-file");

            Assert.Throws<IOException>(() => FavoritesStore.AtomicWrite(p, "new-complete-file", temp =>
            {
                Assert.Equal("old-complete-file", File.ReadAllText(p));
                Assert.Equal("new-complete-file", File.ReadAllText(temp));
                throw new IOException("simulated interruption");
            }));

            Assert.Equal("old-complete-file", File.ReadAllText(p));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(p)!, Path.GetFileName(p) + ".*.tmp"));
        }
        finally { File.Delete(p); }
    }
}
