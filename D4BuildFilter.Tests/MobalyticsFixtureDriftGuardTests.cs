using System.IO;
using System.Linq;
using D4BuildFilter.Core;
using Xunit;
using Xunit.Abstractions;

namespace D4BuildFilter.Tests;

/// <summary>
/// OFFLINE guard for the 2026-09-18 Season 15 drift: Mobalytics added a per-build "tags" array
/// whose entries are themselves JSON arrays/objects, so they contain "]"/"}" characters *inside*
/// an ugDataItems element. The old scraper isolated each section's item block with a non-nesting
/// regex (`"ugDataItems":\[(?<items>[^\]]*)\]`), which stopped at the FIRST "]" — the closing
/// bracket of the first item's own "tags" array — truncating every section to ~1 build (measured
/// live: 73 real endgame builds parsed down to 4).
///
/// Unlike <see cref="ScraperCanaryTests"/> (network-gated, skipped unless RUN_CANARY=1, so it never
/// runs in CI), this test parses real fixture HTML captured from a live fetch and checked into the
/// repo at <c>Fixtures/Mobalytics/*.html</c> — it runs on every `dotnet test` and would have caught
/// the drift without ever touching the network.
/// </summary>
public class MobalyticsFixtureDriftGuardTests
{
    private readonly ITestOutputHelper _out;
    public MobalyticsFixtureDriftGuardTests(ITestOutputHelper o) => _out = o;

    private static string FixturePath(string name)
    {
        // Resolve from the test assembly's location, not the runner's working directory — same
        // pattern EmittedBytesCharacterizationTests uses for Goldens/, so no csproj copy-to-output
        // entry is required.
        for (DirectoryInfo? dir = new DirectoryInfo(
                 Path.GetDirectoryName(typeof(MobalyticsFixtureDriftGuardTests).Assembly.Location)!);
             dir is not null; dir = dir.Parent)
        {
            var projectDir = Path.Combine(dir.FullName, "D4BuildFilter.Tests");
            if (File.Exists(Path.Combine(projectDir, "D4BuildFilter.Tests.csproj")))
                return Path.Combine(projectDir, "Fixtures", "Mobalytics", name);
        }
        throw new DirectoryNotFoundException(
            "Cannot locate D4BuildFilter.Tests above the test assembly. Run this test from a checkout's build output.");
    }

    private static readonly string[] AllKnownClasses =
        { "Barbarian", "Druid", "Necromancer", "Rogue", "Sorcerer", "Spiritborn", "Paladin", "Warlock" };

    [Fact]
    public void Endgame_fixture_parses_realistic_count_and_every_class_present()
    {
        var html = File.ReadAllText(FixturePath("endgame.html"));
        var tl = TierListFetcher.ParseMobalytics(html);

        _out.WriteLine($"endgame: {tl.Builds.Count} builds");
        foreach (var g in tl.Builds.GroupBy(b => b.ClassName).OrderBy(g => g.Key))
            _out.WriteLine($"  {(g.Key.Length == 0 ? "(unattributed)" : g.Key)}: {g.Count()}");

        // Live endgame has been 60+ since Lord of Hatred — same floor the live canary uses. A count
        // this low is exactly the symptom of the nested-bracket truncation bug.
        Assert.True(tl.Builds.Count >= 30, $"Only {tl.Builds.Count} builds parsed from the endgame fixture.");

        // Regression guard proper: EVERY known class must be attributed to at least one build. The
        // 2026-06-10 incident dropped Paladin/Warlock entirely with no error; this generalizes that
        // guard to all 8 classes instead of just the two that broke last time.
        var seenClasses = tl.Builds.Select(b => b.ClassName).ToHashSet();
        var missing = AllKnownClasses.Where(c => !seenClasses.Contains(c)).ToList();
        Assert.True(missing.Count == 0, $"Classes missing entirely from the endgame parse: {string.Join(", ", missing)}");

        // Structural spot-checks: exact title -> class -> tier, cross-checked against the raw fixture
        // text (not just re-deriving from the same parser).
        Assert.Contains(tl.Builds, b => b.Name == "Blazing Scream Warlock Endgame Build Guide" && b.ClassName == "Warlock" && b.Tier == "S");
        Assert.Contains(tl.Builds, b => b.Name == "Penetrating Shot Rogue Endgame Build Guide" && b.ClassName == "Rogue" && b.Tier == "S");
        Assert.Contains(tl.Builds, b => b.Name == "Swarm - Swarmborn" && b.ClassName == "Spiritborn" && b.Tier == "S");
    }

    [Fact]
    public void Leveling_fixture_parses_realistic_count_with_correct_class_via_fallback()
    {
        // The leveling list's items mostly ship with an EMPTY "tags":[] array (measured live), so
        // this exercises the ClassFromSlugOrIcon fallback path, not the tags-based primary path.
        var html = File.ReadAllText(FixturePath("leveling.html"));
        var tl = TierListFetcher.ParseMobalytics(html);

        _out.WriteLine($"leveling: {tl.Builds.Count} builds");
        foreach (var g in tl.Builds.GroupBy(b => b.ClassName).OrderBy(g => g.Key))
            _out.WriteLine($"  {(g.Key.Length == 0 ? "(unattributed)" : g.Key)}: {g.Count()}");

        Assert.True(tl.Builds.Count >= 15, $"Only {tl.Builds.Count} builds parsed from the leveling fixture.");
        Assert.DoesNotContain(tl.Builds, b => b.ClassName.Length == 0);

        Assert.Contains(tl.Builds, b => b.Name == "Dance of Knives Leveling" && b.ClassName == "Rogue");
        Assert.Contains(tl.Builds, b => b.Name == "Wing Strikes Leveling" && b.ClassName == "Paladin");
    }

    [Fact]
    public void Pushing_fixture_parses_realistic_count_and_exposes_D_tier()
    {
        var html = File.ReadAllText(FixturePath("pushing.html"));
        var tl = TierListFetcher.ParseMobalytics(html);

        _out.WriteLine($"pushing: {tl.Builds.Count} builds");
        foreach (var g in tl.Builds.GroupBy(b => b.ClassName).OrderBy(g => g.Key))
            _out.WriteLine($"  {(g.Key.Length == 0 ? "(unattributed)" : g.Key)}: {g.Count()}");

        Assert.True(tl.Builds.Count >= 25, $"Only {tl.Builds.Count} builds parsed from the pushing fixture.");
        Assert.Contains(tl.Builds, b => b.Tier == "D");
    }
}
