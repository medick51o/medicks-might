namespace D4BuildFilter.Core;

/// <summary>
/// The user-updatable game-data folder: <c>%LOCALAPPDATA%\MedicKsMight\data\</c>. Files here
/// (installed by <see cref="GameDataUpdater"/>) take precedence over the copies bundled next to
/// the exe, so a new season's affixes/uniques are picked up without shipping a new build. A local
/// file only wins when it validates (<see cref="GameDataValidator"/>) — anything broken is logged,
/// ignored, and the bundled copy is used instead.
/// </summary>
public static class GameDataStore
{
    public const string AffixesFile = "Affixes.enUS.json";
    public const string UniquesFile = "Uniques.enUS.json";

    /// <summary>Game build of the data files compiled into the exe (d4data via D4LootBench,
    /// ingested 2026-05-28 — see README). Shown in the UI until a download replaces them.</summary>
    public const string BundledDataBuild = "3.0.3.72031";

    /// <summary>Production data folder. Tests pass an explicit dir instead of touching this.</summary>
    public static string DefaultDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MedicKsMight", "data");

    /// <summary>Path of a VALID local override for <paramref name="fileName"/>, or null when the
    /// file is absent, unreadable, or fails validation (rejects are logged — a half-written or
    /// hand-mangled file silently falling back is otherwise undiagnosable in the field).</summary>
    public static string? TryGetValidLocal(string fileName, string? dir = null)
    {
        var path = Path.Combine(dir ?? DefaultDir, fileName);
        try
        {
            if (!File.Exists(path)) return null;
            var (ok, _, error) = GameDataValidator.Validate(fileName, File.ReadAllText(path));
            if (ok) return path;
            AppLog.Write("gamedata", $"ignoring invalid local {fileName}: {error}");
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Write("gamedata", $"ignoring unreadable local {fileName}: {ex.Message}");
            return null;
        }
    }

    /// <summary>One-line provenance for the UI: which data the lookups will load —
    /// "downloaded 2026-06-10" when a valid override is installed, else the bundled build.</summary>
    public static string DescribeActive(string? dir = null)
    {
        var local = TryGetValidLocal(AffixesFile, dir);
        if (local is null) return $"bundled (game build {BundledDataBuild})";
        try { return $"downloaded {File.GetLastWriteTime(local):yyyy-MM-dd}"; }
        catch { return "downloaded"; }
    }
}
