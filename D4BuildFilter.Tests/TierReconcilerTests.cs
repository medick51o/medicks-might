using System.IO;
using System.Linq;
using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Tests;

/// <summary>TierReconciler: favorite tier labels must follow the live lists — the
/// "Maxroll moved it S→A but the chip still says S" fix. Uses a real FavoritesStore on a temp
/// file (same pattern as FavoritesStoreTests) so persistence round-trips are covered too.</summary>
public class TierReconcilerTests
{
    private static readonly DateTime Now = new(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);

    private static FavoritesStore TempStore() =>
        new(Path.Combine(Path.GetTempPath(), $"mk_reconciler_{Guid.NewGuid():N}.json"));

    private static FavoriteEntry Fav(string url, string tier, string source = "Maxroll", string kind = "Endgame") =>
        new(Guid.NewGuid().ToString("N"), url, source, kind, tier,
            "Whirlwind Barb", "Barbarian", Now.AddDays(-20), Now.AddDays(-20));

    private static TierList List(string source, params (string Url, string Tier)[] builds) =>
        new(source, "https://example.test/list",
            builds.Select(b => new TierBuild("Whirlwind Barb", "Barbarian", b.Tier, b.Url)).ToList());

    [Fact]
    public void Reranked_build_updates_tier_and_keeps_previous()
    {
        var store = TempStore();
        store.Toggle(Fav("https://maxroll.gg/d4/build-guides/ww", "S"));

        var changed = TierReconciler.Reconcile(store,
            List("Maxroll", ("https://maxroll.gg/d4/build-guides/ww", "A")), "Maxroll", "Endgame", Now);

        Assert.Equal(1, changed);
        var f = Assert.Single(store.All);
        Assert.Equal("A", f.Tier);
        Assert.Equal("S", f.PrevTier);
        Assert.False(f.Delisted);
        Assert.Equal(Now, f.TierCheckedUtc);
    }

    [Fact]
    public void Unchanged_build_stamps_checked_once_without_counting_as_change()
    {
        var store = TempStore();
        store.Toggle(Fav("https://maxroll.gg/d4/build-guides/ww", "S"));

        var changed = TierReconciler.Reconcile(store,
            List("Maxroll", ("https://maxroll.gg/d4/build-guides/ww", "S")), "Maxroll", "Endgame", Now);

        Assert.Equal(0, changed);
        var f = Assert.Single(store.All);
        Assert.Equal("S", f.Tier);
        Assert.Null(f.PrevTier);
        Assert.Equal(Now, f.TierCheckedUtc);
    }

    [Fact]
    public void Build_missing_from_its_own_list_is_delisted_but_keeps_old_tier()
    {
        var store = TempStore();
        store.Toggle(Fav("https://maxroll.gg/d4/build-guides/ww", "S"));

        var changed = TierReconciler.Reconcile(store,
            List("Maxroll", ("https://maxroll.gg/d4/build-guides/other", "S")), "Maxroll", "Endgame", Now);

        Assert.Equal(1, changed);
        var f = Assert.Single(store.All);
        Assert.True(f.Delisted);
        Assert.Equal("S", f.Tier);   // "was S" — the chip needs the old label to say so
    }

    [Fact]
    public void Delisted_build_that_returns_is_relisted_at_its_new_tier()
    {
        var store = TempStore();
        store.Toggle(Fav("https://maxroll.gg/d4/build-guides/ww", "S"));
        TierReconciler.Reconcile(store, List("Maxroll"), "Maxroll", "Endgame", Now);   // delist
        Assert.True(store.All.Single().Delisted);

        var changed = TierReconciler.Reconcile(store,
            List("Maxroll", ("https://maxroll.gg/d4/build-guides/ww", "B")), "Maxroll", "Endgame", Now.AddHours(1));

        Assert.Equal(1, changed);
        var f = Assert.Single(store.All);
        Assert.False(f.Delisted);
        Assert.Equal("B", f.Tier);
        Assert.Equal("S", f.PrevTier);
    }

    [Fact]
    public void Other_kinds_and_sources_say_nothing_about_a_favorite()
    {
        var store = TempStore();
        store.Toggle(Fav("https://maxroll.gg/d4/build-guides/ww", "S", kind: "Bossing"));

        // Same source, different kind: absence from Endgame must NOT delist a Bossing favorite.
        Assert.Equal(0, TierReconciler.Reconcile(store, List("Maxroll"), "Maxroll", "Endgame", Now));
        // Different source entirely.
        Assert.Equal(0, TierReconciler.Reconcile(store, List("Mobalytics"), "Mobalytics", "Bossing", Now));

        var f = Assert.Single(store.All);
        Assert.False(f.Delisted);
        Assert.Equal("S", f.Tier);
        Assert.Null(f.TierCheckedUtc);
    }

    [Fact]
    public void Paste_favorites_are_never_touched()
    {
        var store = TempStore();
        store.Toggle(new FavoriteEntry(Guid.NewGuid().ToString("N"), "paste://abc123", "Community",
            null, null, "My Paste", "Druid", Now.AddDays(-5), Now.AddDays(-5)));

        Assert.Equal(0, TierReconciler.Reconcile(store, List("Community"), "Community", "Endgame", Now));
        var f = Assert.Single(store.All);
        Assert.Null(f.Tier);
        Assert.False(f.Delisted);
    }

    [Fact]
    public void Matching_is_case_insensitive_on_source_kind_and_url()
    {
        var store = TempStore();
        store.Toggle(Fav("https://Maxroll.gg/d4/build-guides/WW", "S"));

        var changed = TierReconciler.Reconcile(store,
            List("maxroll", ("https://maxroll.gg/d4/build-guides/ww", "A")), "MAXROLL", "endgame", Now);

        Assert.Equal(1, changed);
        Assert.Equal("A", store.All.Single().Tier);
    }

    [Fact]
    public void New_fields_round_trip_through_the_json_file_and_old_files_still_load()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mk_reconciler_{Guid.NewGuid():N}.json");
        var store = new FavoritesStore(path);
        store.Toggle(Fav("https://maxroll.gg/d4/build-guides/ww", "S"));
        TierReconciler.Reconcile(store,
            List("Maxroll", ("https://maxroll.gg/d4/build-guides/ww", "A")), "Maxroll", "Endgame", Now);

        var reloaded = new FavoritesStore(path).All.Single();
        Assert.Equal("A", reloaded.Tier);
        Assert.Equal("S", reloaded.PrevTier);
        Assert.Equal(Now, reloaded.TierCheckedUtc);

        // Pre-upgrade favorites.json (no PrevTier/TierCheckedUtc/Delisted) must load with defaults.
        var legacy = Path.Combine(Path.GetTempPath(), $"mk_reconciler_legacy_{Guid.NewGuid():N}.json");
        File.WriteAllText(legacy,
            @"[{""Id"":""1"",""Url"":""https://maxroll.gg/d4/build-guides/old"",""Source"":""Maxroll""," +
            @"""TierKind"":""Endgame"",""Tier"":""S"",""Name"":""Old"",""ClassName"":""Druid""," +
            @"""DateAdded"":""2026-05-01T00:00:00Z"",""DateLastOpened"":""2026-05-01T00:00:00Z""}]");
        var old = new FavoritesStore(legacy).All.Single();
        Assert.Null(old.PrevTier);
        Assert.Null(old.TierCheckedUtc);
        Assert.False(old.Delisted);
    }
}
