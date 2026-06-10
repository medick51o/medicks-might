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
}
