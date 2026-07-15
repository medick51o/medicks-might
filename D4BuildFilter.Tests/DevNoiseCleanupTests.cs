using D4BuildFilter.Core;
using D4BuildFilter.WPF;
using D4BuildFilter.WPF.ViewModels;
using Xunit;

namespace D4BuildFilter.Tests;

/// <summary>Dev-noise cleanup: user-visible surfaces must never carry a raw .NET exception message
/// (scraper internals like "page shape changed?" or a bare stack-trace string are meaningless to an
/// end user), and the full exception must still reach AppLog so troubleshooting loses nothing. Also
/// covers the removal of the dead "Not yet filterable (dev)" diagnostic panel.
///
/// Round 2: the exemption from the friendly wrap is keyed off a dedicated marker type,
/// UserMessageException (D4BuildFilter.WPF/UserMessageException.cs), not off InvalidOperationException.
/// A raw InvalidOperationException (e.g. a Core fetcher's "d4builds Firestore error: ...") is dev
/// noise like any other exception type and must still be wrapped — only UserMessageException, thrown
/// deliberately for authored, build-specific product copy, survives verbatim.</summary>
public class DevNoiseCleanupTests
{
    private static string TodayLogPath() =>
        Path.Combine(AppLog.Dir, $"app-{DateTime.UtcNow:yyyyMMdd}.log");

    private static long CurrentLogLength()
    {
        var path = TodayLogPath();
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    private static string ReadLogSince(long offset)
    {
        using var stream = new FileStream(TodayLogPath(), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // ── CompileSelectedBuildsAsync (multi-build) ──

    /// <summary>Reverting this fix makes a failed multi-build compile show a raw scraper/JSON
    /// exception on StatusMessage instead of truthful, stage-neutral copy. (The authored "returned
    /// no usable variants" case is covered separately below and must NOT be wrapped.)</summary>
    [Fact]
    public async Task Multi_build_compile_raw_resolver_failure_shows_friendly_status_not_raw_exception_text()
    {
        var vm = new MainViewModel(startTierListFetches: false,
            resolveBuild: _ => throw new FormatException("d4builds doc has no fields"));
        var cards = vm.BuildGroups(new TierList("Maxroll", "https://example.test/list",
        [
            new TierBuild("Build A", "Rogue", "S", "https://example.test/a"),
            new TierBuild("Build B", "Rogue", "S", "https://example.test/b"),
        ]), "Maxroll", "Endgame").Single().Builds;
        foreach (var card in cards) card.IsSelected = true;

        await vm.CompileSelectedBuildsCommand.ExecuteAsync(null);

        Assert.Equal("Couldn't finish loading that build — details are in the app log.", vm.StatusMessage);
        Assert.DoesNotContain("d4builds doc has no fields", vm.StatusMessage);
    }

    /// <summary>Reverting the marker-type dispatch back to "any InvalidOperationException survives
    /// verbatim" leaks a raw Core-fetcher message (this is modeled on D4BuildsFetcher's real
    /// "d4builds Firestore error: ..." throw) straight to the player — the exact loophole round 2
    /// closes. Only UserMessageException may bypass the friendly wrap now.</summary>
    [Fact]
    public async Task Multi_build_compile_raw_invalid_operation_exception_gets_friendly_copy_not_verbatim()
    {
        var vm = new MainViewModel(startTierListFetches: false,
            resolveBuild: _ => throw new InvalidOperationException("d4builds Firestore error: PERMISSION_DENIED"));
        var cards = vm.BuildGroups(new TierList("Maxroll", "https://example.test/list",
        [
            new TierBuild("Build A", "Rogue", "S", "https://example.test/a"),
            new TierBuild("Build B", "Rogue", "S", "https://example.test/b"),
        ]), "Maxroll", "Endgame").Single().Builds;
        foreach (var card in cards) card.IsSelected = true;

        await vm.CompileSelectedBuildsCommand.ExecuteAsync(null);

        Assert.Equal("Couldn't finish loading that build — details are in the app log.", vm.StatusMessage);
        Assert.DoesNotContain("Firestore", vm.StatusMessage);
    }

    /// <summary>Reverting the UserMessageException special-case wraps this authored, build-specific
    /// message in generic friendly copy — the player would no longer be told WHICH of their selected
    /// builds failed. Exercises the real production throw site (HasUsableVariants), not just an
    /// injected exception, so it also proves the site actually throws UserMessageException now.</summary>
    [Fact]
    public async Task Multi_build_compile_authored_no_usable_variants_message_survives_verbatim()
    {
        var resolved = new Dictionary<string, ResolvedBuild>
        {
            ["https://example.test/a"] = new ResolvedBuild("Build A", "Rogue",
                [new ResolvedVariant("Endgame", ["Strength"], [])]),
            ["https://example.test/b"] = new ResolvedBuild("Build B", "Rogue", Array.Empty<ResolvedVariant>()),
        };
        var vm = new MainViewModel(startTierListFetches: false,
            resolveBuild: url => Task.FromResult((resolved[url], "Test")));
        var cards = vm.BuildGroups(new TierList("Maxroll", "https://example.test/list",
        [
            new TierBuild("Build A", "Rogue", "S", "https://example.test/a"),
            new TierBuild("Build B", "Rogue", "S", "https://example.test/b"),
        ]), "Maxroll", "Endgame").Single().Builds;
        foreach (var card in cards) card.IsSelected = true;

        await vm.CompileSelectedBuildsCommand.ExecuteAsync(null);

        Assert.Equal("Build B returned no usable variants.", vm.StatusMessage);
    }

    // ── RunCompileAsync (single build) ──

    /// <summary>Reverting this fix makes a failed single-build load (bad URL, scraper error, page
    /// shape change, or even a downstream favorites-write failure) show either the raw exception or
    /// a "check the URL" message that's wrong for non-network stages. Delta-based: only inspects the
    /// log bytes THIS call appends, so it can't pass by coincidentally matching unrelated log lines.</summary>
    [Fact]
    public async Task Single_build_load_failure_shows_truthful_status_and_still_logs_full_exception()
    {
        var vm = new MainViewModel(startTierListFetches: false,
            resolveBuild: _ => throw new FormatException("Mobalytics page had no userGeneratedDocumentBySlug — page shape changed?"));
        vm.MaxrollUrl = "https://example.test/some-build";
        var before = CurrentLogLength();

        await vm.CompileCommand.ExecuteAsync(null);

        Assert.Equal("Couldn't finish loading that build — details are in the app log.", vm.StatusMessage);
        Assert.DoesNotContain("page shape changed", vm.StatusMessage);

        var appended = ReadLogSince(before);
        Assert.Contains("page shape changed", appended);
        Assert.Contains("FormatException", appended);
    }

    // ── CompilePasteAsync ──

    /// <summary>Reverting the storage/parse split shows the pasted-format message even though the
    /// paste itself parsed fine and only the local sidecar write failed — telling the player to fix
    /// their paste when nothing was wrong with it.</summary>
    [Fact]
    public async Task Pasted_build_storage_failure_gets_storage_specific_copy_not_the_parse_message()
    {
        var vm = new MainViewModel(startTierListFetches: false, pasteStore: new ThrowingPasteStore());
        vm.PastedText = "+30% Attack Speed";

        await vm.CompilePasteCommand.ExecuteAsync(null);

        Assert.Equal("Couldn't save that paste locally — the build itself parsed fine.", vm.StatusMessage);
        Assert.DoesNotContain("sidecar write failed", vm.StatusMessage);
    }

    /// <summary>Reverting the storage/parse split (e.g. broadening the storage catch beyond
    /// IOException) would also swallow this non-storage failure into the storage copy — proves
    /// anything other than IOException still falls through to the original parse/format message.</summary>
    [Fact]
    public async Task Pasted_build_non_storage_failure_keeps_the_parse_format_copy()
    {
        var vm = new MainViewModel(startTierListFetches: false, pasteStore: new NonIoThrowingPasteStore());
        vm.PastedText = "+30% Attack Speed";

        await vm.CompilePasteCommand.ExecuteAsync(null);

        Assert.Equal("Couldn't parse that pasted build — check the format and try again.", vm.StatusMessage);
        Assert.DoesNotContain("unexpected paste-store bug", vm.StatusMessage);
    }

    // ── AddSecondBuildAsync (Armory) ──

    /// <summary>Reverting this fix makes a failed Armory second-build load show a raw scraper/JSON
    /// exception on ArmoryStatus instead of the existing action-specific message.</summary>
    [Fact]
    public async Task Armory_second_build_raw_resolver_failure_shows_friendly_status_not_raw_exception_text()
    {
        var vm = new MainViewModel(startTierListFetches: false,
            resolveBuild: _ => throw new FormatException("d4builds doc has no fields"));
        vm.Ingest(new ResolvedBuild("Test Build", "Barbarian",
        [
            new ResolvedVariant("Endgame", ["Strength"], [])
        ]), "Test");
        vm.ArmorySource = "https://example.test/second";

        await vm.AddSecondBuildCommand.ExecuteAsync(null);

        Assert.Equal("Couldn't load the second build — check the URL or pasted text and try again.",
            vm.ArmoryStatus);
        Assert.DoesNotContain("d4builds doc has no fields", vm.ArmoryStatus);
    }

    /// <summary>Flipped from round 1: a raw InvalidOperationException (modeled on a real Core-fetcher
    /// throw, e.g. D4BuildsFetcher's "d4builds Firestore error: ...") must now get the FRIENDLY copy,
    /// not survive verbatim. Reverting to the old "any IOE survives" special-case leaks this straight
    /// to the player.</summary>
    [Fact]
    public async Task Armory_second_build_raw_invalid_operation_exception_gets_friendly_copy_not_verbatim()
    {
        var vm = new MainViewModel(startTierListFetches: false,
            resolveBuild: _ => throw new InvalidOperationException("d4builds Firestore error: PERMISSION_DENIED"));
        vm.Ingest(new ResolvedBuild("Test Build", "Barbarian",
        [
            new ResolvedVariant("Endgame", ["Strength"], [])
        ]), "Test");
        vm.ArmorySource = "https://example.test/second";

        await vm.AddSecondBuildCommand.ExecuteAsync(null);

        Assert.Equal("Couldn't load the second build — check the URL or pasted text and try again.",
            vm.ArmoryStatus);
        Assert.DoesNotContain("Firestore", vm.ArmoryStatus);
    }

    /// <summary>Reverting the UserMessageException special-case wraps an authored message reaching
    /// this catch in generic friendly copy instead of surfacing it verbatim. No production caller
    /// currently throws UserMessageException through this exact resolveBuild path (the existing
    /// guards in IngestSecond either return before the try or set ArmoryStatus directly instead of
    /// throwing), so this exercises the catch's type-based dispatch directly — the same mechanism
    /// IngestSecond's own "Load the first build..." guard now uses.</summary>
    [Fact]
    public async Task Armory_second_build_authored_user_message_survives_verbatim()
    {
        var vm = new MainViewModel(startTierListFetches: false,
            resolveBuild: _ => throw new UserMessageException(
                "Second build could not be resolved: unsupported build type."));
        vm.Ingest(new ResolvedBuild("Test Build", "Barbarian",
        [
            new ResolvedVariant("Endgame", ["Strength"], [])
        ]), "Test");
        vm.ArmorySource = "https://example.test/second";

        await vm.AddSecondBuildCommand.ExecuteAsync(null);

        Assert.Equal("Second build could not be resolved: unsupported build type.", vm.ArmoryStatus);
    }

    // ── Dead panel removal ──

    /// <summary>Reverting the dead-panel removal reintroduces the ShowPendingAffixes VM property —
    /// this asserts it (and the panel it drove) is gone for good, not merely hidden.</summary>
    [Fact]
    public void ShowPendingAffixes_dev_toggle_no_longer_exists_on_the_view_model()
    {
        Assert.Null(typeof(MainViewModel).GetProperty("ShowPendingAffixes"));
    }

    /// <summary>Reverting the dead-panel removal reintroduces the orphaned multi-binding converter
    /// that only ever gated that panel's visibility.</summary>
    [Fact]
    public void Orphaned_AllTrueToVisibilityConverter_no_longer_exists()
    {
        Assert.Null(Type.GetType("D4BuildFilter.WPF.AllTrueToVisibilityConverter, D4BuildFilter.WPF"));
    }

    // ── FindSample dev-path removal ──

    /// <summary>Strengthened in round 2: FindSample now takes an optional baseDirectory so the test
    /// can point it at an isolated temp folder instead of only being able to observe the real exe's
    /// output directory. Proves two things a revert would break: (1) a file sitting directly at the
    /// base dir root — the SHAPE of the old hardcoded fallback, some lookup outside Assets/ — is never
    /// resolved, and (2) only {baseDirectory}\Assets\sample_barb.json ever is.</summary>
    [Fact]
    public void FindSample_only_resolves_the_Assets_subfolder_never_a_root_or_hardcoded_fallback()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"medicksmight_findsample_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "sample_barb.json"), "{}");
            Assert.Null(MainViewModel.FindSample(dir));

            Directory.CreateDirectory(Path.Combine(dir, "Assets"));
            File.WriteAllText(Path.Combine(dir, "Assets", "sample_barb.json"), "{}");
            Assert.Equal(Path.Combine(dir, "Assets", "sample_barb.json"), MainViewModel.FindSample(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private sealed class ThrowingPasteStore : IPasteStore
    {
        public void Save(string hash, string text) => throw new IOException("sidecar write failed");
        public string? Load(string hash) => null;
        public void Remove(string hash) { }
    }

    private sealed class NonIoThrowingPasteStore : IPasteStore
    {
        public void Save(string hash, string text) => throw new InvalidOperationException("unexpected paste-store bug");
        public string? Load(string hash) => null;
        public void Remove(string hash) { }
    }
}
