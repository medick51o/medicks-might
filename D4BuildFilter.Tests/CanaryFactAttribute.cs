using Xunit;

namespace D4BuildFilter.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that SKIPS (reported as skipped, never a false pass) unless the
/// environment variable <c>RUN_CANARY=1</c> is set. Used for live-network "canary" tests that hit
/// the real Maxroll / D4Builds / Mobalytics endpoints to detect when a site changes shape and our
/// scraper silently breaks. Excluded from the default <c>dotnet test</c> run so the offline suite
/// stays fast and deterministic.
///
/// Run the canaries on demand:
///   <c>$env:RUN_CANARY=1; dotnet test --filter Category=Canary</c>
/// </summary>
public sealed class CanaryFactAttribute : FactAttribute
{
    public CanaryFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_CANARY") != "1")
            Skip = "Live-network canary — set RUN_CANARY=1 to run (e.g. weekly, to catch scraper drift).";
    }
}
