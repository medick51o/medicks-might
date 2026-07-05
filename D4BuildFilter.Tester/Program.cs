using System.Text.Json;
using D4BuildFilter.Core;

const string ResolvedPath = @"C:\Sync\Projects\D4BuildFilter\build_resolved.json";

var jsonOpts = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
};

// DECODE MODE: `dotnet run -- decode [<base64>]` prints the structure of any
// import code (real game export or ours). With no code, decodes our last output.
// Use this to harvest filter IDs from in-game exports and spot unknown conditions.
if (args.Length >= 1 && args[0] == "decode")
{
    string decodeInput = args.Length >= 2
        ? args[1]
        : File.ReadAllText(@"C:\Sync\Projects\D4BuildFilter\last_code.txt");
    Console.WriteLine(FilterDecoder.Describe(FilterDecoder.Decode(decodeInput)));
    return;
}

// SURVEY MODE: `dotnet run -- survey` — live tier-section drift survey across all 11 lists on
// the 3 sources. Prints each list's build count + section letters and WARNS on any section the
// Mobalytics whitelist would silently drop (the canary's interactive sibling; born 2026-07-01,
// ~30h into S14, for release-week drift watching).
if (args.Length >= 1 && args[0] == "survey")
{
    Console.WriteLine("=== TIER SECTION LIVE SURVEY ===\n");
    Console.WriteLine("MOBALYTICS:");
    await SurveyMobalytics(MobalyticsList.Endgame, "Endgame");
    await SurveyMobalytics(MobalyticsList.Leveling, "Leveling");
    await SurveyMobalytics(MobalyticsList.Pushing, "Pushing");
    Console.WriteLine("\nMAXROLL:");
    await SurveyMaxroll(MaxrollList.Endgame, "Endgame");
    await SurveyMaxroll(MaxrollList.Bossing, "Bossing");
    await SurveyMaxroll(MaxrollList.Leveling, "Leveling");
    await SurveyMaxroll(MaxrollList.Push, "Push");
    await SurveyMaxroll(MaxrollList.Speedfarm, "Speedfarm");
    Console.WriteLine("\nD4BUILDS:");
    await SurveyD4Builds(D4BuildsList.Endgame, "Endgame");
    await SurveyD4Builds(D4BuildsList.Leveling, "Leveling");
    await SurveyD4Builds(D4BuildsList.Tower, "Tower");
    return;
}

// PROBE MODE: `dotnet run -- probe-seals` — one-off in-game diagnostic for the type-9
// TalismanSetBonus condition (2026-07-02, the "every seal shows green" report). Import the code
// in D4 and read the colors: GREEN = class-set charms addressable · BLUE = seals via generic
// sets · PINK = not reachable by set-targeting (needs a fallback). Recolor-only: hides nothing.
if (args.Length >= 1 && args[0] == "probe-seals")
{
    var charmSeal = new[] { ItemTypes.Charm, ItemTypes.Seal };
    var barbSets = SetItemBonusDatabase.ByName
        .Where(kv => kv.Key.Contains("Barbarian", StringComparison.OrdinalIgnoreCase))
        .Select(kv => kv.Value).ToList();
    var genericSets = SetItemBonusDatabase.ByName
        .Where(kv => kv.Key.Contains("Generic", StringComparison.OrdinalIgnoreCase))
        .Select(kv => kv.Value).ToList();
    var probe = new List<byte[]>
    {
        FilterBuilder.MakeRule("BarbSets (Green)", Visibility.Recolor,
            new[] { Conditions.Types(charmSeal), Conditions.TalismanSetBonus(barbSets) }, FilterColors.Green),
        FilterBuilder.MakeRule("GenericSets (Blue)", Visibility.Recolor,
            new[] { Conditions.Types(charmSeal), Conditions.TalismanSetBonus(genericSets) }, FilterColors.Blue),
        FilterBuilder.MakeRule("OtherCharmSeal (Pink)", Visibility.Recolor,
            new[] { Conditions.Types(charmSeal) }, FilterColors.Pink),
    };
    var probeBytes = FilterBuilder.MakeFilter("MK SEAL PROBE", probe);
    var probeCode = FilterBuilder.ToImportCode(probeBytes);
    var rt = FilterDecoder.Decode(probeCode).Rules.Count == probe.Count;
    Console.WriteLine($"=== MK SEAL PROBE: {probe.Count} rules, round-trip {(rt ? "OK" : "MISMATCH")} ===");
    Console.WriteLine($"  barb sets: {barbSets.Count}  generic sets: {genericSets.Count}");
    Console.WriteLine(probeCode);
    return;
}

// FETCH MODE: `dotnet run -- fetch <maxroll-url-or-id | path-to-planner.json>`
// pulls a build from maxroll (or reads a saved planner response), resolves each
// item's affixes to names, overwrites build_resolved.json, then falls through to
// build the filter — so you see the whole loop from a URL in one command.
ResolvedBuild resolved;
if (args.Length >= 1 && args[0] == "fetch")
{
    if (args.Length < 2)
    {
        Console.WriteLine("usage: fetch <maxroll-url-or-id | path-to-planner.json>");
        return;
    }
    string src = args[1];
    string? raw = File.Exists(src) ? File.ReadAllText(src) : null;
    // Route by source: d4builds (Firestore doc) vs Mobalytics (__PRELOADED_STATE__) vs maxroll (planner JSON).
    bool isD4b = raw is not null ? raw.Contains("\"newStats\"") || raw.Contains("databases/(default)") : D4BuildsFetcher.IsD4BuildsUrl(src);
    bool isMoba = raw is not null ? raw.Contains("__PRELOADED_STATE__") : MobalyticsFetcher.IsMobalyticsUrl(src);
    string source;
    if (isD4b)
    {
        raw ??= D4BuildsFetcher.FetchRawAsync(src).GetAwaiter().GetResult();
        resolved = D4BuildsFetcher.Parse(raw);
        source = "d4builds";
    }
    else if (isMoba)
    {
        raw ??= MobalyticsFetcher.FetchRawAsync(src).GetAwaiter().GetResult();
        resolved = MobalyticsFetcher.Parse(raw);
        source = "mobalytics";
    }
    else
    {
        raw ??= MaxrollFetcher.FetchRawAsync(src).GetAwaiter().GetResult();
        resolved = MaxrollFetcher.Parse(raw, NameLookup.Default(), UniqueLookup.Default());
        source = "maxroll";
    }
    File.WriteAllText(ResolvedPath, JsonSerializer.Serialize(resolved, jsonOpts));

    Console.WriteLine($"Fetched \"{resolved.Build}\" ({resolved.Class}) — {resolved.Variants.Count} variants [{source}]");
    foreach (var v in resolved.Variants)
        Console.WriteLine($"  {v.Name}: {v.Affixes.Count} affixes, {v.Uniques.Count} gear uniques");
    Console.WriteLine($"  -> wrote {ResolvedPath}\n");
}
else if (args.Length >= 1 && args[0] == "paste")
{
    // PASTE MODE: `dotnet run -- paste <file>` — universal import. Reads affix names (and any
    // unique item names) copied from ANY build guide, maps them, and builds the filter. The same
    // back half as fetch, just sourced from free text instead of a maxroll URL.
    if (args.Length < 2) { Console.WriteLine("usage: paste <path-to-text-file>"); return; }
    var text = File.ReadAllText(args[1]);
    var buildName = Path.GetFileNameWithoutExtension(args[1]);
    resolved = PastedBuild.Parse(text, buildName);
    File.WriteAllText(ResolvedPath, JsonSerializer.Serialize(resolved, jsonOpts));
    var v0 = resolved.Variants[0];
    Console.WriteLine($"Pasted \"{resolved.Build}\" — {v0.Affixes.Count} affix lines, {v0.Uniques.Count} unique names recognized\n");
}
else
{
    // Default: build from the previously-resolved build on disk.
    resolved = JsonSerializer.Deserialize<ResolvedBuild>(File.ReadAllText(ResolvedPath), jsonOpts)
        ?? throw new InvalidOperationException($"could not read {ResolvedPath}");
}

// === Compile ===============================================================
// Analyze the build into its affix pool / unique targeting (the shared Core service
// the WPF app uses too), then emit both the NORMAL and STRICT ENDGAME filters.
var build = FilterCompiler.Analyze(resolved, FilterColors.Red, FilterColors.Pink);

// v1.0.1: mirror the app — scope the Charms & Seals rules to the build's own sets when known
// (the app's checkbox panel does the same with the build's sets pre-checked).
var talismanScope = build.TalismanSets.Count > 0
    ? TalismanSetDatabase.All.Where(s => build.TalismanSets.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).ToList()
    : null;

Console.WriteLine($"Build: {build.Name}");
if (build.TalismanSets.Count > 0)
    Console.WriteLine($"  charm sets -> scoped ({build.TalismanSets.Count}): {string.Join(", ", build.TalismanSets)}");
Console.WriteLine($"  affix pool ({build.Pool.Count}): {string.Join(", ", build.PoolNames)}");
if (build.Dropped.Count > 0)
{
    // Some of these are genuinely not filterable in-game (conditional/granular affixes);
    // others are real coarse stats/skills simply missing from AffixDatabase. To add a
    // missing one, make a single-affix loot filter for it in D4, export, and run:
    //   dotnet run --project D4BuildFilter.Tester -- decode <code>
    // then paste the captured id into AffixDatabase.cs.
    Console.WriteLine($"  not yet filterable ({build.Dropped.Count}) — export single-affix filters in D4 to capture any of these IDs:");
    foreach (var d in build.Dropped) Console.WriteLine($"      - {d}");
}
if (build.UniquesTargeted.Count > 0)
    Console.WriteLine($"  build uniques -> PURPLE ({build.UniquesTargeted.Count}): {string.Join(", ", build.UniquesTargeted)}");
if (build.UniquesPending.Count > 0)
    Console.WriteLine($"  build uniques without an id yet ({build.UniquesPending.Count}) — export to capture: {string.Join(", ", build.UniquesPending)}");
Console.WriteLine($"  STANDARD (strict): RED = 3+ legendaries · PINK = 3+ rares   |   LEVELING adds SILVER 2+ rares\n");

void Emit(FilterOptions opts, string label, string outPath)
{
    var output = FilterCompiler.Compile(new[] { build }, opts, label);
    Console.WriteLine($"=== {output.Label}: {output.RuleCount} rules (cap 25), {output.Bytes} bytes, "
        + $"round-trip {(output.RoundTripOk ? "OK" : "MISMATCH")} ===");
    Console.WriteLine(output.ImportCode + "\n");
    File.WriteAllText(outPath, output.ImportCode);
}

Emit(new FilterOptions { TalismanSets = talismanScope }, "STANDARD (strict: Red 3+ leg / Pink 3+ rare)", @"C:\Sync\Projects\D4BuildFilter\last_code.txt");
Emit(new FilterOptions { Leveling = true, TalismanSets = talismanScope },
    "LEVELING (+ Silver 2+ rares, combined)", @"C:\Sync\Projects\D4BuildFilter\last_code_leveling.txt");
Emit(new FilterOptions { PerSlotRules = true, TalismanSets = talismanScope }, "PER-SLOT (precise, no cross-slot false positives)", @"C:\Sync\Projects\D4BuildFilter\last_code_perslot.txt");

// ── survey helpers ─────────────────────────────────────────────────────────
async Task SurveyMobalytics(MobalyticsList kind, string label)
{
    var html = await BrowserFetch.GetStringAsync(TierListFetcher.MobalyticsUrlFor(kind));
    var sections = TierListFetcher.EnumerateMobaSectionNames(html);
    var parsed = TierListFetcher.ParseMobalytics(html);
    var tiers = parsed.Builds.Select(b => b.Tier).Distinct().OrderBy(t => t).ToList();
    Console.WriteLine($"  {label}: {parsed.Builds.Count} builds | Sections: {string.Join(", ", tiers)}");
    // Warn on any section name the parser's whitelist would silently drop (compare against the
    // KNOWN set, not the parsed tiers — "God Tier" parses to "God", which is fine, not drift).
    var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "God Tier", "S", "A", "B", "C", "D", "Support" };
    var unknown = sections.Where(n => !known.Contains(n)).ToList();
    if (unknown.Count > 0)
        Console.WriteLine($"    WARNING: section(s) the parser silently drops: {string.Join(", ", unknown)}");
}

async Task SurveyMaxroll(MaxrollList kind, string label)
{
    var list = await TierListFetcher.FetchMaxrollAsync(kind);
    var sections = list.Builds.Select(b => b.Tier).Distinct().OrderBy(t => t).ToList();
    Console.WriteLine($"  {label}: {list.Builds.Count} builds | Sections: {string.Join(", ", sections)}");
}

async Task SurveyD4Builds(D4BuildsList kind, string label)
{
    var list = await TierListFetcher.FetchD4BuildsAsync(kind);
    var sections = list.Builds.Select(b => b.Tier).Distinct().OrderBy(t => t).ToList();
    Console.WriteLine(list.Builds.Count == 0
        ? $"  {label}: EMPTY"
        : $"  {label}: {list.Builds.Count} builds | Sections: {string.Join(", ", sections)}");
}
