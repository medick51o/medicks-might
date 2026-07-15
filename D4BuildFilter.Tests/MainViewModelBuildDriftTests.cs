using D4BuildFilter.Core;
using D4BuildFilter.WPF.ViewModels;
using Xunit;

namespace D4BuildFilter.Tests;

public class MainViewModelBuildDriftTests
{
    [Fact]
    public async Task Loading_stale_favorite_surfaces_note_and_refreshes_snapshot_after_compile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"medicksmight_vm_drift_{Guid.NewGuid():N}.json");
        try
        {
            const string url = "https://maxroll.gg/d4/planner/drifttest";
            var oldBuild = new ResolvedBuild("Test Build", "Barbarian",
            [
                new ResolvedVariant("Endgame", ["Dexterity"], [])
            ]);
            var freshBuild = new ResolvedBuild("Test Build", "Barbarian",
            [
                new ResolvedVariant("Endgame", ["Strength"], [])
            ]);
            var oldCapture = DateTime.UtcNow.AddDays(-11);
            var store = new FavoritesStore(path);
            store.Toggle(new FavoriteEntry(
                "favorite", url, "Maxroll", "Endgame", "S", "Test Build", "Barbarian",
                DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(-12),
                Snapshot: BuildSnapshot.Capture(oldBuild, oldCapture)));
            var vm = new MainViewModel(startTierListFetches: false, favorites: store,
                resolveBuild: _ => Task.FromResult((freshBuild, "Maxroll")));

            await vm.LoadBuildFromFavoriteAsync(Assert.Single(store.All));

            Assert.Equal(AppState.Result, vm.State);
            Assert.True(vm.HasBuildDriftNote);
            Assert.Contains("What changed since your last compile", vm.BuildDriftNote);
            Assert.Contains("+ Strength", vm.BuildDriftNote);
            Assert.Contains("− Dexterity", vm.BuildDriftNote);
            var refreshed = Assert.Single(store.All).Snapshot;
            Assert.NotNull(refreshed);
            Assert.True(refreshed.CapturedUtc > oldCapture);
            Assert.False(BuildDrift.Compare(refreshed, freshBuild)!.HasDrift);
        }
        finally { File.Delete(path); }
    }
}
