using System.Globalization;
using System.Text.Json;

namespace D4BuildFilter.Core;

/// <summary>Outcome of an update attempt. <see cref="Success"/> = the check completed (downloaded
/// + validated); <see cref="Updated"/> = files actually changed on disk (callers should invalidate
/// the cached lookups only then). <see cref="Message"/> is user-facing as-is.</summary>
public sealed record GameDataUpdateResult(
    bool Success,
    bool Updated,
    string Message,
    int AffixEntries = 0,
    int UniqueEntries = 0,
    string? DataVersion = null);

/// <summary>
/// Downloads the latest <c>Affixes.enUS.json</c> / <c>Uniques.enUS.json</c>, validates them, and
/// atomically installs them into <see cref="GameDataStore.DefaultDir"/> where <see cref="DataFiles"/>
/// prefers them over the bundled (frozen-at-build-time) copies.
///
/// Source: the <c>josdemmers/Diablo4Companion</c> repo's <c>D4Companion/Data/</c> tree — the same
/// format and lineage our bundled copies came from, updated within days of every D4 patch
/// (e.g. "Updates for v3.0.1" landed 1 day after Lord of Hatred). Researched 2026-06-10:
/// DiabloTools/d4data's <c>json/</c> tree holds only raw game dumps (CoreTOC, enUS_Text STLs),
/// not these processed per-locale files, and ThunderEagle/D4LootBench releases ship an exe, not
/// data — so D4Companion master is the only raw-URL host of this exact shape.
///
/// Both files are validated (parse + shape + entry-count sanity vs the bundled copies) BEFORE
/// either is installed — a season bump updates the pair together, and a truncated download or
/// GitHub error page must never half-install or clobber a working override.
/// </summary>
public static class GameDataUpdater
{
    private const string RawBase =
        "https://raw.githubusercontent.com/josdemmers/Diablo4Companion/master/D4Companion/Data/";

    /// <summary>Latest commit touching the data folder — its message usually carries the game
    /// build ("Updated data for v3.0.2.71886"), which is the closest thing to a data version
    /// the source publishes. One unauthenticated call per update click (limit: 60/hour).</summary>
    private const string CommitsApiUrl =
        "https://api.github.com/repos/josdemmers/Diablo4Companion/commits?path=D4Companion/Data&per_page=1";

    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) D4BuildFilter/0.1";

    // Absolute entry floors, used when no bundled copy is around to compare against (the bundled
    // files carry 891 / 299 usable entries today — real data can only realistically grow).
    private const int MinAffixEntries = 400;
    private const int MinUniqueEntries = 150;

    public static string RawUrlFor(string fileName) => RawBase + fileName;

    /// <summary>Download, validate, and install the data pair. Never throws for the expected
    /// failure modes (offline, GitHub down, bad payload) — those come back as a failed result
    /// with a user-facing message.</summary>
    /// <param name="http">Injectable for tests (fake handler); owned + disposed when null.</param>
    /// <param name="targetDir">Install-folder seam for tests; null = the real data folder.</param>
    public static async Task<GameDataUpdateResult> UpdateAsync(
        HttpClient? http = null, string? targetDir = null, CancellationToken ct = default)
    {
        var owned = http is null;
        http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        try
        {
            string affixJson, uniqueJson;
            try
            {
                affixJson = await GetStringAsync(http, RawUrlFor(GameDataStore.AffixesFile), ct);
                uniqueJson = await GetStringAsync(http, RawUrlFor(GameDataStore.UniquesFile), ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                return Fail($"Couldn't reach GitHub — {Brief(ex)}. Check your connection and try again.");
            }

            var (affixOk, affixCount, affixErr) = GameDataValidator.Validate(GameDataStore.AffixesFile, affixJson);
            if (!affixOk) return Fail($"Downloaded affix data failed validation ({affixErr}) — keeping the current data.");
            var (uniqueOk, uniqueCount, uniqueErr) = GameDataValidator.Validate(GameDataStore.UniquesFile, uniqueJson);
            if (!uniqueOk) return Fail($"Downloaded unique data failed validation ({uniqueErr}) — keeping the current data.");

            // Sanity count vs the bundled copies: a new season ADDS entries, so a big shrink means
            // a truncated body or an error page, not a real data release.
            var affixFloor = SanityFloor(GameDataStore.AffixesFile, MinAffixEntries);
            if (affixCount < affixFloor)
                return Fail($"Downloaded affix data has only {affixCount} entries (expected ≥ {affixFloor}) — keeping the current data.");
            var uniqueFloor = SanityFloor(GameDataStore.UniquesFile, MinUniqueEntries);
            if (uniqueCount < uniqueFloor)
                return Fail($"Downloaded unique data has only {uniqueCount} entries (expected ≥ {uniqueFloor}) — keeping the current data.");

            var dir = targetDir ?? GameDataStore.DefaultDir;
            var version = await TryFetchDataVersionAsync(http, ct);
            var suffix = version is null ? "" : $" · {version}";

            if (SameAsActive(GameDataStore.AffixesFile, affixJson, dir)
                && SameAsActive(GameDataStore.UniquesFile, uniqueJson, dir))
                return new(true, false,
                    $"Already up to date — {affixCount} affixes, {uniqueCount} uniques{suffix}.",
                    affixCount, uniqueCount, version);

            try
            {
                Directory.CreateDirectory(dir);
                InstallAtomic(Path.Combine(dir, GameDataStore.AffixesFile), affixJson);
                InstallAtomic(Path.Combine(dir, GameDataStore.UniquesFile), uniqueJson);
            }
            catch (Exception ex)
            {
                return Fail($"Couldn't write the data folder — {Brief(ex)}.");
            }

            AppLog.Write("gamedata",
                $"installed update: {affixCount} affixes, {uniqueCount} uniques, source: {version ?? "unknown"}");
            return new(true, true,
                $"Game data updated — {affixCount} affixes, {uniqueCount} uniques{suffix}.",
                affixCount, uniqueCount, version);
        }
        finally { if (owned) http.Dispose(); }
    }

    /// <summary>"&lt;commit message first line&gt;, yyyy-MM-dd" from a GitHub commits-API response,
    /// or null on any surprise (the version line is garnish, never a failure).</summary>
    public static string? ParseLatestDataVersion(string commitsApiJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(commitsApiJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return null;
            var commit = doc.RootElement[0].GetProperty("commit");
            var firstLine = (commit.GetProperty("message").GetString() ?? "").Split('\n')[0].Trim();
            string? date = null;
            if (commit.TryGetProperty("committer", out var c) && c.TryGetProperty("date", out var d)
                && DateTimeOffset.TryParse(d.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal, out var dto))
                date = dto.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (firstLine.Length == 0) return date;
            return date is null ? firstLine : $"{firstLine}, {date}";
        }
        catch { return null; }
    }

    private static GameDataUpdateResult Fail(string message)
    {
        AppLog.Write("gamedata", $"update rejected: {message}");
        return new(false, false, message);
    }

    /// <summary>Minimum acceptable entry count: 80% of the bundled copy's (when present and valid),
    /// never below the absolute floor.</summary>
    private static int SanityFloor(string fileName, int absoluteMin)
    {
        try
        {
            var bundled = DataFiles.FindBundled(fileName);
            if (bundled is null) return absoluteMin;
            var (ok, entries, _) = GameDataValidator.Validate(fileName, File.ReadAllText(bundled));
            return ok ? Math.Max(absoluteMin, entries * 4 / 5) : absoluteMin;
        }
        catch { return absoluteMin; }
    }

    /// <summary>True when the downloaded payload is byte-identical to whatever the lookups would
    /// load today (local override if valid, else bundled) — then installing is a no-op and the
    /// caller can report "already up to date" without touching disk.</summary>
    private static bool SameAsActive(string fileName, string downloaded, string dir)
    {
        try
        {
            var active = GameDataStore.TryGetValidLocal(fileName, dir) ?? DataFiles.FindBundled(fileName);
            return active is not null && string.Equals(File.ReadAllText(active), downloaded, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    /// <summary>Write-to-temp + atomic rename, so a crash/power-cut mid-write can never leave a
    /// half-file where the next launch's validator would (correctly, but confusingly) reject it.</summary>
    private static void InstallAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }

    private static async Task<string?> TryFetchDataVersionAsync(HttpClient http, CancellationToken ct)
    {
        try { return ParseLatestDataVersion(await GetStringAsync(http, CommitsApiUrl, ct)); }
        catch { return null; }   // rate-limited / offline mid-flight — version is garnish
    }

    private static async Task<string> GetStringAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd(UserAgent);   // GitHub's API 403s UA-less requests
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    private static string Brief(Exception ex) => ex.InnerException?.Message ?? ex.Message;
}
