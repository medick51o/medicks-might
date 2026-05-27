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
var build = FilterCompiler.Analyze(resolved, FilterColors.Gold, FilterColors.Silver);

Console.WriteLine($"Build: {build.Name}");
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
Console.WriteLine($"  GOLD = >={FilterCompiler.Strict} of pool   SECONDARY = >={FilterCompiler.Loose} of pool\n");

void Emit(bool strictEndgame, string label, string outPath)
{
    var output = FilterCompiler.Compile(new[] { build }, strictEndgame, label);
    Console.WriteLine($"=== {output.Label}: {output.RuleCount} rules (cap 25), {output.Bytes} bytes, "
        + $"round-trip {(output.RoundTripOk ? "OK" : "MISMATCH")} ===");
    Console.WriteLine(output.ImportCode + "\n");
    File.WriteAllText(outPath, output.ImportCode);
}

Emit(false, "NORMAL", @"C:\Sync\Projects\D4BuildFilter\last_code.txt");
Emit(true, "STRICT ENDGAME (Ancestral-only)", @"C:\Sync\Projects\D4BuildFilter\last_code_strict.txt");
