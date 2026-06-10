namespace D4BuildFilter.Core;

/// <summary>Locates a data file for the name/unique lookups, in trust order:
/// 1. a VALID downloaded override under <c>%LOCALAPPDATA%\MedicKsMight\data\</c> (installed by
///    "Update game data" — lets a new season's affixes resolve without shipping a new exe;
///    anything unparseable there is ignored, see <see cref="GameDataStore.TryGetValidLocal"/>),
/// 2. the copy bundled next to the app under <c>Data\</c>,
/// 3. the newest local Diablo4Companion install (legacy dev convenience).</summary>
public static class DataFiles
{
    /// <param name="localDataDir">Override-folder seam for tests; null = the real
    /// <see cref="GameDataStore.DefaultDir"/>.</param>
    public static string? Find(string fileName, string? localDataDir = null) =>
        GameDataStore.TryGetValidLocal(fileName, localDataDir) ?? FindBundled(fileName);

    /// <summary>The compiled-in copy (Data\ next to the exe), or a Diablo4Companion install's.
    /// Skips the local-override folder — <see cref="GameDataUpdater"/> uses this as the baseline
    /// for its sanity count, which must not be the very file it's about to replace.</summary>
    public static string? FindBundled(string fileName)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "Data", fileName);
        if (File.Exists(bundled)) return bundled;

        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads))
            foreach (var dir in Directory.EnumerateDirectories(downloads, "Diablo4Companion_v*")
                                         .OrderByDescending(d => d))
            {
                var p = Path.Combine(dir, "Data", fileName);
                if (File.Exists(p)) return p;
            }
        return null;
    }
}
