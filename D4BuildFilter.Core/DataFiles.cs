namespace D4BuildFilter.Core;

/// <summary>Locates a bundled data file (copied next to the app under Data\), falling back
/// to the newest local Diablo4Companion install. Shared by the name/unique lookups.</summary>
internal static class DataFiles
{
    public static string? Find(string fileName)
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
