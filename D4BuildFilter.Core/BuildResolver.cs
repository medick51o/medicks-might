namespace D4BuildFilter.Core;

/// <summary>
/// Routes a build reference — a maxroll/d4builds/mobalytics URL, a raw guide blob, or a local
/// file path — to the correct fetcher + parser, returning the resolved build and a source label.
///
/// This dispatch previously lived inline in the WPF <c>MainViewModel.RunCompileAsync</c>, which
/// made it UI-bound and untested. Extracting it into Core keeps the logic UI-agnostic and unit-
/// testable, and lets a future web backend reuse the exact same routing (the server only needs to
/// call <see cref="ResolveAsync"/> — the fetchers it depends on must run server-side anyway).
/// </summary>
public static class BuildResolver
{
    /// <summary>
    /// Resolve <paramref name="urlOrText"/> to a <see cref="ResolvedBuild"/> plus a source label
    /// ("D4Builds" / "Mobalytics" / "Maxroll"). If the argument is an existing file path, its
    /// contents are read and sniffed to pick the source; otherwise it's treated as a URL/id and the
    /// matching fetcher pulls the raw payload.
    /// </summary>
    public static async Task<(ResolvedBuild build, string sourceLabel)> ResolveAsync(
        string urlOrText, NameLookup names, UniqueLookup uniques, CancellationToken ct = default)
    {
        // A local file path => read it and sniff the contents; otherwise treat as a URL/id.
        string? raw = File.Exists(urlOrText) ? await File.ReadAllTextAsync(urlOrText, ct) : null;

        bool isD4b = raw is not null
            ? raw.Contains("\"newStats\"") || raw.Contains("databases/(default)")
            : D4BuildsFetcher.IsD4BuildsUrl(urlOrText);
        bool isMoba = raw is not null
            ? raw.Contains("__PRELOADED_STATE__")
            : MobalyticsFetcher.IsMobalyticsUrl(urlOrText);

        if (isD4b)
        {
            raw ??= await D4BuildsFetcher.FetchRawAsync(urlOrText, ct);
            return (D4BuildsFetcher.Parse(raw), "D4Builds");
        }
        if (isMoba)
        {
            raw ??= await MobalyticsFetcher.FetchRawAsync(urlOrText, ct);
            return (MobalyticsFetcher.Parse(raw), "Mobalytics");
        }
        raw ??= await MaxrollFetcher.FetchRawAsync(urlOrText, ct: ct);
        return (MaxrollFetcher.Parse(raw, names, uniques), "Maxroll");
    }
}
