using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Tests;

/// <summary>Ground-truth for the favorites system. The store is a live-reference list (URL +
/// provenance), persisted to JSON. URL is identity — re-adding the same URL toggles it off.</summary>
public class FavoritesStoreTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(),
        $"medicksmight_favtest_{Guid.NewGuid():N}.json");

    private static FavoriteEntry Sample(string url = "https://maxroll.gg/d4/planner/abc123",
        string source = "Maxroll", string? tierKind = "Endgame", string? tier = "S",
        string name = "Whirlwind Barb", string className = "Barbarian") =>
        new(Guid.NewGuid().ToString("N"), url, source, tierKind, tier, name, className,
            DateTime.UtcNow, DateTime.UtcNow);

    [Fact]
    public void Toggle_Adds_When_Absent()
    {
        var p = TempPath();
        try
        {
            var store = new FavoritesStore(p);
            var added = store.Toggle(Sample());
            Assert.True(added);
            Assert.Single(store.All);
            Assert.True(store.Contains("https://maxroll.gg/d4/planner/abc123"));
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void Toggle_Removes_When_Present()
    {
        var p = TempPath();
        try
        {
            var store = new FavoritesStore(p);
            store.Toggle(Sample());
            var stillFavorited = store.Toggle(Sample());
            Assert.False(stillFavorited);
            Assert.Empty(store.All);
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void Persists_Across_Instances()
    {
        var p = TempPath();
        try
        {
            new FavoritesStore(p).Toggle(Sample(name: "Ball Lightning Sorc", className: "Sorcerer"));
            var reopened = new FavoritesStore(p);
            Assert.Single(reopened.All);
            Assert.Equal("Ball Lightning Sorc", reopened.All[0].Name);
            Assert.Equal("Sorcerer", reopened.All[0].ClassName);
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void Identity_Is_Url_Case_Insensitive()
    {
        var p = TempPath();
        try
        {
            var store = new FavoritesStore(p);
            store.Toggle(Sample(url: "https://maxroll.gg/d4/planner/AbC123"));
            Assert.True(store.Contains("https://maxroll.gg/d4/planner/abc123"));
            var stillFavorited = store.Toggle(Sample(url: "https://maxroll.gg/d4/planner/abc123"));
            Assert.False(stillFavorited);
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void StampOpened_Bumps_DateLastOpened()
    {
        var p = TempPath();
        try
        {
            var store = new FavoritesStore(p);
            var initial = DateTime.UtcNow.AddDays(-5);
            store.Toggle(Sample() with { DateAdded = initial, DateLastOpened = initial });
            var entry = store.All[0];
            var addedAt = entry.DateAdded;
            Thread.Sleep(5);
            store.StampOpened(entry.Url);
            var bumped = store.All[0];
            Assert.Equal(addedAt, bumped.DateAdded); // DateAdded preserved
            Assert.True(bumped.DateLastOpened > bumped.DateAdded);
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void Corrupt_File_Is_Treated_As_Empty_Not_Crash()
    {
        var p = TempPath();
        try
        {
            File.WriteAllText(p, "this is { not valid json");
            var store = new FavoritesStore(p);
            Assert.Empty(store.All);
            // Should still be able to add — Toggle's Save overwrites the corrupt file.
            store.Toggle(Sample());
            Assert.Single(store.All);
            Assert.Single(new FavoritesStore(p).All);
        }
        finally { File.Delete(p); }
    }
}
