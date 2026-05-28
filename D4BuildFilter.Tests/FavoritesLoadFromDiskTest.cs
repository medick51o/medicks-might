using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Tests;

/// <summary>Smoke test: ensure the JSON shape we wrote to disk by hand actually deserializes back
/// into FavoriteEntry records. Catches mismatches between hand-authored seed files and the record's
/// constructor parameter casing.</summary>
public class FavoritesLoadFromDiskTest
{
    [Fact]
    public void Loads_The_Same_Json_Shape_We_Seed_Live()
    {
        var p = Path.Combine(Path.GetTempPath(), $"medicksmight_loadtest_{Guid.NewGuid():N}.json");
        try
        {
            // Same shape as the live seed we wrote to %LOCALAPPDATA%\MedicKsMight\favorites.json.
            File.WriteAllText(p, """
[
  {
    "Id": "test1",
    "Url": "https://maxroll.gg/d4/build-guides/ball-lightning-sorcerer-guide",
    "Source": "Maxroll",
    "TierKind": "Endgame",
    "Tier": "S",
    "Name": "Ball Lightning Sorc",
    "ClassName": "Sorcerer",
    "DateAdded": "2026-05-25T10:30:00Z",
    "DateLastOpened": "2026-05-25T10:30:00Z"
  }
]
""");
            var store = new FavoritesStore(p);
            Assert.Single(store.All);
            Assert.Equal("Ball Lightning Sorc", store.All[0].Name);
        }
        finally { File.Delete(p); }
    }
}
