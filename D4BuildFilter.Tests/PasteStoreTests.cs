using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Tests;

/// <summary>Sidecar store for community-paste favorites. Each paste's text is keyed by a hash
/// the VM computes, and survives across app launches so a starred community paste can be re-loaded.</summary>
public class PasteStoreTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(),
        $"medicksmight_pastetest_{Guid.NewGuid():N}");

    [Fact]
    public void Save_Then_Load_Round_Trips()
    {
        var d = TempDir();
        try
        {
            var store = new PasteStore(d);
            store.Save("abc123", "+30% Attack Speed\n+Critical Strike Damage");
            Assert.Equal("+30% Attack Speed\n+Critical Strike Damage", store.Load("abc123"));
        }
        finally { if (Directory.Exists(d)) Directory.Delete(d, true); }
    }

    [Fact]
    public void Load_Missing_Returns_Null_Not_Throws()
    {
        var d = TempDir();
        try
        {
            Assert.Null(new PasteStore(d).Load("nonexistent"));
        }
        finally { if (Directory.Exists(d)) Directory.Delete(d, true); }
    }

    [Fact]
    public void Remove_Deletes_The_Sidecar()
    {
        var d = TempDir();
        try
        {
            var store = new PasteStore(d);
            store.Save("xyz", "some text");
            Assert.NotNull(store.Load("xyz"));
            store.Remove("xyz");
            Assert.Null(store.Load("xyz"));
        }
        finally { if (Directory.Exists(d)) Directory.Delete(d, true); }
    }
}
