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
    }
}
