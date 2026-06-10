using System.Net;
using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Tests;

/// <summary>The season-update data path: DataFiles preferring a VALID local override over the
/// bundled copy (and never a broken one), the shape validator both lean on, and GameDataUpdater's
/// download → validate → atomic-install flow (offline, via a stub HttpMessageHandler — the live
/// GitHub fetch is exercised manually / by the canary, never in the default suite).</summary>
public class GameDataTests
{
    // ── Synthetic data files (the minimal shape NameLookup / UniqueLookup read) ──

    private static string MiniAffixJson(int n) =>
        "[" + string.Join(",", Enumerable.Range(1, n).Select(i =>
            $$"""{"IdSno":"{{i}}","IdSnoList":["{{i}}"],"DescriptionClean":"Affix {{i}}"}""")) + "]";

    private static string MiniUniqueJson(int n) =>
        "[" + string.Join(",", Enumerable.Range(1, n).Select(i =>
            $$"""{"IdSno":"{{i}}","IdName":"Item_Unique_Gen_{{i}}","IdNameItem":"Item_Unique_Gen_{{i}}","Name":"Unique {{i}}"}""")) + "]";

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"medicksmight_gamedata_{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    private static void Nuke(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    // ── DataFiles: local-override-first ordering ──

    [Fact]
    public void Find_prefers_valid_local_override_over_bundled()
    {
        var dir = TempDir();
        try
        {
            var local = Path.Combine(dir, GameDataStore.AffixesFile);
            File.WriteAllText(local, MiniAffixJson(5));
            Assert.Equal(local, DataFiles.Find(GameDataStore.AffixesFile, dir));
        }
        finally { Nuke(dir); }
    }

    [Fact]
    public void Find_uses_bundled_when_local_absent()
    {
        var dir = TempDir();
        try
        {
            var found = DataFiles.Find(GameDataStore.AffixesFile, dir);
            Assert.Equal(DataFiles.FindBundled(GameDataStore.AffixesFile), found);
            Assert.NotNull(found);   // bundled data ships in the test output via the Core csproj
        }
        finally { Nuke(dir); }
    }

    [Fact]
    public void Find_ignores_corrupt_local_and_falls_back_to_bundled()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, GameDataStore.AffixesFile), "{ definitely not an affix arr");
            var found = DataFiles.Find(GameDataStore.AffixesFile, dir);
            Assert.Equal(DataFiles.FindBundled(GameDataStore.AffixesFile), found);
        }
        finally { Nuke(dir); }
    }

    [Fact]
    public void Find_ignores_wrong_shape_local_even_when_it_parses()
    {
        var dir = TempDir();
        try
        {
            // Valid JSON, zero usable entries (no DescriptionClean/IdSnoList) — e.g. the wrong
            // file saved under the right name. Must NOT win over bundled.
            File.WriteAllText(Path.Combine(dir, GameDataStore.AffixesFile),
                """[{"foo":1},{"bar":2}]""");
            Assert.Equal(DataFiles.FindBundled(GameDataStore.AffixesFile),
                DataFiles.Find(GameDataStore.AffixesFile, dir));
        }
        finally { Nuke(dir); }
    }

    [Fact]
    public void DescribeActive_reports_bundled_vs_downloaded()
    {
        var dir = TempDir();
        try
        {
            Assert.Contains(GameDataStore.BundledDataBuild, GameDataStore.DescribeActive(dir));
            File.WriteAllText(Path.Combine(dir, GameDataStore.AffixesFile), MiniAffixJson(3));
            Assert.StartsWith("downloaded", GameDataStore.DescribeActive(dir));
        }
        finally { Nuke(dir); }
    }

    // ── Validator ──

    [Fact]
    public void Validator_accepts_the_bundled_files()
    {
        foreach (var (file, floor) in new[] { (GameDataStore.AffixesFile, 400), (GameDataStore.UniquesFile, 150) })
        {
            var bundled = DataFiles.FindBundled(file);
            Assert.NotNull(bundled);
            var (ok, entries, error) = GameDataValidator.Validate(file, File.ReadAllText(bundled!));
            Assert.True(ok, $"{file}: {error}");
            Assert.True(entries >= floor, $"{file}: only {entries} usable entries");
        }
    }

    [Theory]
    [InlineData("""{"not":"an array"}""")]
    [InlineData("[]")]
    [InlineData("""[{"DescriptionClean":"x"}]""")]                    // missing IdSnoList
    [InlineData("""[{"IdSnoList":["1"]}]""")]                         // missing DescriptionClean
    [InlineData("""[{"IdSnoList":[],"DescriptionClean":"x"}]""")]     // empty sno list keys nothing
    public void Validator_rejects_unusable_affix_payloads(string json)
    {
        var (ok, _, error) = GameDataValidator.Validate(GameDataStore.AffixesFile, json);
        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void Validator_rejects_truncated_json()
    {
        var truncated = MiniAffixJson(10)[..40];   // mid-download cut
        var (ok, _, error) = GameDataValidator.Validate(GameDataStore.AffixesFile, truncated);
        Assert.False(ok);
        Assert.Contains("not valid JSON", error);
    }

    [Fact]
    public void Validator_counts_only_usable_entries()
    {
        var json = """[{"IdSnoList":["1"],"DescriptionClean":"A"},{"junk":true},{"IdSnoList":["2"],"DescriptionClean":"B"}]""";
        var (ok, entries, _) = GameDataValidator.Validate(GameDataStore.AffixesFile, json);
        Assert.True(ok);
        Assert.Equal(2, entries);
    }

    [Fact]
    public void Validator_uniques_accepts_IdName_only_and_requires_Name()
    {
        var idNameOnly = """[{"IdName":"Helm_Unique_Generic_002","Name":"Harlequin Crest"}]""";
        Assert.True(GameDataValidator.Validate(GameDataStore.UniquesFile, idNameOnly).Ok);

        var nameless = """[{"IdNameItem":"Helm_Unique_Generic_002"}]""";
        Assert.False(GameDataValidator.Validate(GameDataStore.UniquesFile, nameless).Ok);
    }

    // ── Updater (offline: stub handler plays GitHub) ──

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly List<(string UrlContains, HttpStatusCode Code, string Body)> _routes = new();

        public StubHandler Map(string urlContains, string body, HttpStatusCode code = HttpStatusCode.OK)
        {
            _routes.Add((urlContains, code, body));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            foreach (var (k, code, body) in _routes)
                if (url.Contains(k, StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("no route") });
        }
    }

    private const string CommitsFixture =
        """[{"sha":"abc123","commit":{"message":"Updated data for v3.1.0.99999\n\nSeason 14 PTR ingest","committer":{"name":"jos","date":"2026-06-20T07:00:00Z"}}}]""";

    /// <summary>Entry counts that always clear the updater's 80%-of-bundled sanity floor, however
    /// the bundled snapshot grows over time.</summary>
    private static (int Affixes, int Uniques) SafeCounts()
    {
        static int CountOf(string file, int fallback)
        {
            var bundled = DataFiles.FindBundled(file);
            if (bundled is null) return fallback;
            var (ok, entries, _) = GameDataValidator.Validate(file, File.ReadAllText(bundled));
            return ok ? entries + 9 : fallback;
        }
        return (CountOf(GameDataStore.AffixesFile, 800), CountOf(GameDataStore.UniquesFile, 300));
    }

    [Fact]
    public async Task Updater_installs_valid_download_and_reports_version()
    {
        var dir = TempDir();
        var (na, nu) = SafeCounts();
        var http = new HttpClient(new StubHandler()
            .Map("Affixes.enUS.json", MiniAffixJson(na))
            .Map("Uniques.enUS.json", MiniUniqueJson(nu))
            .Map("api.github.com", CommitsFixture));
        try
        {
            var r = await GameDataUpdater.UpdateAsync(http, dir);
            Assert.True(r.Success, r.Message);
            Assert.True(r.Updated);
            Assert.Equal(na, r.AffixEntries);
            Assert.Equal(nu, r.UniqueEntries);
            Assert.Equal("Updated data for v3.1.0.99999, 2026-06-20", r.DataVersion);
            // Installed files are picked up as the valid local override (the DataFiles win path).
            Assert.NotNull(GameDataStore.TryGetValidLocal(GameDataStore.AffixesFile, dir));
            Assert.NotNull(GameDataStore.TryGetValidLocal(GameDataStore.UniquesFile, dir));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));   // atomic install leaves no debris
        }
        finally { http.Dispose(); Nuke(dir); }
    }

    [Fact]
    public async Task Updater_rejects_truncated_download_and_keeps_existing_override()
    {
        var dir = TempDir();
        var (na, nu) = SafeCounts();
        var existing = MiniAffixJson(na);
        File.WriteAllText(Path.Combine(dir, GameDataStore.AffixesFile), existing);
        var http = new HttpClient(new StubHandler()
            .Map("Affixes.enUS.json", existing[..60])          // cut mid-body
            .Map("Uniques.enUS.json", MiniUniqueJson(nu))
            .Map("api.github.com", CommitsFixture));
        try
        {
            var r = await GameDataUpdater.UpdateAsync(http, dir);
            Assert.False(r.Success);
            Assert.False(r.Updated);
            Assert.Contains("validation", r.Message);
            // The working override must be untouched — a bad download never clobbers good data.
            Assert.Equal(existing, File.ReadAllText(Path.Combine(dir, GameDataStore.AffixesFile)));
        }
        finally { http.Dispose(); Nuke(dir); }
    }

    [Fact]
    public async Task Updater_rejects_suspiciously_small_payload()
    {
        var dir = TempDir();
        var http = new HttpClient(new StubHandler()
            .Map("Affixes.enUS.json", MiniAffixJson(50))       // parses fine; way below the floor
            .Map("Uniques.enUS.json", MiniUniqueJson(300))
            .Map("api.github.com", CommitsFixture));
        try
        {
            var r = await GameDataUpdater.UpdateAsync(http, dir);
            Assert.False(r.Success);
            Assert.Contains("only 50 entries", r.Message);
            Assert.Empty(Directory.GetFiles(dir));             // nothing installed
        }
        finally { http.Dispose(); Nuke(dir); }
    }

    [Fact]
    public async Task Updater_reports_already_up_to_date_without_rewriting()
    {
        var dir = TempDir();
        var (na, nu) = SafeCounts();
        var affixes = MiniAffixJson(na);
        var uniques = MiniUniqueJson(nu);
        File.WriteAllText(Path.Combine(dir, GameDataStore.AffixesFile), affixes);
        File.WriteAllText(Path.Combine(dir, GameDataStore.UniquesFile), uniques);
        var http = new HttpClient(new StubHandler()
            .Map("Affixes.enUS.json", affixes)
            .Map("Uniques.enUS.json", uniques)
            .Map("api.github.com", CommitsFixture));
        try
        {
            var r = await GameDataUpdater.UpdateAsync(http, dir);
            Assert.True(r.Success, r.Message);
            Assert.False(r.Updated);
            Assert.StartsWith("Already up to date", r.Message);
        }
        finally { http.Dispose(); Nuke(dir); }
    }

    [Fact]
    public async Task Updater_still_updates_when_version_api_is_down()
    {
        var dir = TempDir();
        var (na, nu) = SafeCounts();
        var http = new HttpClient(new StubHandler()
            .Map("Affixes.enUS.json", MiniAffixJson(na))
            .Map("Uniques.enUS.json", MiniUniqueJson(nu))
            .Map("api.github.com", "rate limited", HttpStatusCode.Forbidden));
        try
        {
            var r = await GameDataUpdater.UpdateAsync(http, dir);
            Assert.True(r.Updated, r.Message);   // the version line is garnish, never a blocker
            Assert.Null(r.DataVersion);
        }
        finally { http.Dispose(); Nuke(dir); }
    }

    [Fact]
    public async Task Updater_fails_cleanly_when_offline()
    {
        var dir = TempDir();
        var http = new HttpClient(new ThrowingHandler());
        try
        {
            var r = await GameDataUpdater.UpdateAsync(http, dir);
            Assert.False(r.Success);
            Assert.Contains("Couldn't reach GitHub", r.Message);
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally { http.Dispose(); Nuke(dir); }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("name resolution failed");
    }

    // ── Version-line parsing (the commits-API shape, pinned by fixture) ──

    [Fact]
    public void ParseLatestDataVersion_extracts_first_line_and_date() =>
        Assert.Equal("Updated data for v3.1.0.99999, 2026-06-20",
            GameDataUpdater.ParseLatestDataVersion(CommitsFixture));

    [Theory]
    [InlineData("[]")]
    [InlineData("not json at all")]
    [InlineData("""{"message":"API rate limit exceeded"}""")]   // GitHub's own error shape
    public void ParseLatestDataVersion_returns_null_on_surprises(string json) =>
        Assert.Null(GameDataUpdater.ParseLatestDataVersion(json));

    // ── Live canary (RUN_CANARY=1 only): the real D4Companion raw URLs + commits API ──
    // Catches upstream drift before season day: file moved/renamed, format change the validator
    // rejects, or a data shrink that trips the sanity floor.

    [CanaryFact]
    [Trait("Category", "Canary")]
    public async Task GameData_update_flow_works_live()
    {
        var dir = TempDir();
        try
        {
            var r = await GameDataUpdater.UpdateAsync(targetDir: dir);
            Assert.True(r.Success, r.Message);   // "already up to date" and "updated" both pass
            Assert.True(r.AffixEntries >= 400, $"affix entries: {r.AffixEntries}");
            Assert.True(r.UniqueEntries >= 150, $"unique entries: {r.UniqueEntries}");
        }
        finally { Nuke(dir); }
    }
}
