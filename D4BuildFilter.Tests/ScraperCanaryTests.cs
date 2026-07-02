using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Tests;

/// <summary>
/// Live-network canaries. These hit the REAL tier-list endpoints and assert the fetch+parse still
/// yields builds — the early-warning that a source changed its page shape and the scraper needs a
/// fix. They are SKIPPED by default (see <see cref="CanaryFactAttribute"/>); run on demand with
/// <c>$env:RUN_CANARY=1; dotnet test --filter Category=Canary</c>.
///
/// Why tier lists (not a single build URL): the tier-list fetchers exercise each source's full
/// fetch path (including the BrowserFetch/Cloudflare-evasion route for Mobalytics) and don't depend
/// on a specific build staying published, so they're the most stable drift signal.
/// </summary>
[Trait("Category", "Canary")]
public class ScraperCanaryTests
{
    [CanaryFact]
    public async Task Maxroll_tierlist_still_parses()
    {
        var list = await TierListFetcher.FetchMaxrollAsync();
        Assert.NotEmpty(list.Builds);
    }

    [CanaryFact]
    public async Task D4Builds_tierlist_still_parses()
    {
        var list = await TierListFetcher.FetchD4BuildsAsync();
        Assert.NotEmpty(list.Builds);
    }

    [CanaryFact]
    public async Task Mobalytics_tierlist_still_parses()
    {
        var list = await TierListFetcher.FetchMobalyticsAsync();
        Assert.NotEmpty(list.Builds);
        // Regression guard: Mobalytics moved Paladin/Warlock icons off the classes-icons/ path and
        // an icon-anchored regex silently dropped BOTH classes (2026-06-10: 23/62 endgame builds
        // lost). The two newest classes are heavily represented on every live list — if neither
        // parses, the class-attribution path has rotted again.
        Assert.Contains(list.Builds, b => b.ClassName is "Paladin" or "Warlock");
        // And no build may be dropped to the point the list visibly thins: live endgame has been
        // 60+ entries since Lord of Hatred; alert if we parse less than half of recent reality.
        Assert.True(list.Builds.Count >= 30,
            $"Only {list.Builds.Count} Mobalytics builds parsed — page shape likely drifted.");
    }

    [Fact]
    public void Moba_section_enumeration_surfaces_unknown_sections()
    {
        // Offline guard for the tripwire itself: a renamed/new section MUST be enumerated even
        // though ParseMobalytics' name whitelist would silently drop its builds.
        const string html =
            @"""name"":""God Tier"",""color"":""tier-e"",""description"":null,""ugDataItems"":[]," +
            @"""name"":""S+"",""color"":""tier-s"",""description"":null,""ugDataItems"":[]";
        var names = TierListFetcher.EnumerateMobaSectionNames(html);
        Assert.Contains("God Tier", names);
        Assert.Contains("S+", names);
    }

    [CanaryFact]
    public async Task Mobalytics_tier_sections_all_recognized()
    {
        // S14 recon's #1 hardening item: ParseMobalytics whitelists exact section names, so a
        // RENAMED or NEW section (say "God Tier" -> "S+") vanishes with zero error. Enumerate the
        // live page's sections loosely and fail LOUD on anything the whitelist doesn't know.
        var html = await BrowserFetch.GetStringAsync(TierListFetcher.MobalyticsUrlFor(MobalyticsList.Endgame));
        var names = TierListFetcher.EnumerateMobaSectionNames(html);
        Assert.True(names.Count >= 4, $"only {names.Count} tier sections enumerated — page shape drifted");
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "God Tier", "S", "A", "B", "C", "D", "Support" };
        var unknown = names.Where(n => !known.Contains(n)).ToList();
        Assert.True(unknown.Count == 0,
            $"Mobalytics carries tier section(s) the parser silently drops: {string.Join(", ", unknown)}");
        // And the core trio must EXIST — a vanished known section is drift too (God/Support/C/D are
        // content-dependent and may legitimately be absent; S/A/B never are).
        foreach (var core in new[] { "S", "A", "B" })
            Assert.Contains(names, n => n.Equals(core, StringComparison.OrdinalIgnoreCase));
    }
}
