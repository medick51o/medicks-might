using System.Text.Json;
using D4BuildFilter.Core;

const string ResolvedPath = @"C:\Sync\Projects\D4BuildFilter\build_resolved.json";

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
if (args.Length >= 1 && args[0] == "fetch")
{
    if (args.Length < 2)
    {
        Console.WriteLine("usage: fetch <maxroll-url-or-id | path-to-planner.json>");
        return;
    }
    string src = args[1];
    string raw = File.Exists(src)
        ? File.ReadAllText(src)
        : MaxrollFetcher.FetchRawAsync(src).GetAwaiter().GetResult();

    var lookup = NameLookup.Default();
    var uniqueLookup = UniqueLookup.Default();
    var resolved = MaxrollFetcher.Parse(raw, lookup, uniqueLookup);
    File.WriteAllText(ResolvedPath, JsonSerializer.Serialize(resolved,
        new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

    Console.WriteLine($"Fetched \"{resolved.Build}\" ({resolved.Class}) — {resolved.Variants.Count} variants "
        + $"(DB: {lookup.Count} affix SNOs, {uniqueLookup.Count} unique items)");
    foreach (var v in resolved.Variants)
        Console.WriteLine($"  {v.Name}: {v.Affixes.Count} affixes, {v.Uniques.Count} gear uniques");
    Console.WriteLine($"  -> wrote {ResolvedPath}\n");
    // fall through to build the filter from what we just wrote
}

// === Model =================================================================
// A build = a name, a color, and a pool of desirable coarse affixes.
// Each build emits two rules: a STRICT rule (item has >=3 of the pool -> bright
// color) and a LOOSE rule (>=2 -> dim color, toggleable). Items are matched by
// affix COUNT, not "any one match" — that's what makes the filter selective.
// Multiple builds each get their own color; the app/user will assign colors.

const int Strict = 3;   // gold: >=3 build affixes ("3 of 3 / 3 of 4, reroll the dud")
const int Loose = 2;    // secondary: >=2 ("2 of 3, one reroll from great")

static BuildFilter LoadBuild(string path, uint color, uint dim)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(path));
    var root = doc.RootElement;
    string name = root.GetProperty("build").GetString()!;
    var pool = new List<uint>();
    var names = new Dictionary<uint, string>();
    var seen = new HashSet<uint>();
    var dropped = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var v in root.GetProperty("variants").EnumerateArray())
        foreach (var a in v.GetProperty("affixes").EnumerateArray())
        {
            var src = a.GetString()!;
            var m = AffixMapper.Map(src);
            if (m.Mapped)
            {
                if (seen.Add(m.CoarseId!.Value))
                {
                    pool.Add(m.CoarseId.Value);
                    names[m.CoarseId.Value] = m.CoarseName!;
                }
            }
            else dropped.Add(src.Trim());
        }
    // Build-specific unique targeting (type-8): map each equipped gear unique's name to its
    // filter id via UniqueDatabase. Names with a known id -> purple rule; the rest are
    // reported so we know which to capture next (UniqueDatabase grows from in-game exports).
    var buildUniques = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var v in root.GetProperty("variants").EnumerateArray())
        if (v.TryGetProperty("uniques", out var us))
            foreach (var u in us.EnumerateArray()) buildUniques.Add(u.GetString()!);

    var uniqueIds = new List<uint>();
    var uniquesTargeted = new List<string>();
    var uniquesPending = new List<string>();
    foreach (var un in buildUniques)
        if (UniqueDatabase.TryGet(un, out var uid)) { uniqueIds.Add(uid); uniquesTargeted.Add(un); }
        else uniquesPending.Add(un);

    return new BuildFilter(name, color, dim, pool, names, dropped.ToList(), uniqueIds, uniquesTargeted, uniquesPending);
}

// === Build the filter ======================================================
// Today: one build. Drop in more BuildFilters (each a different color) and the
// rest just works, up to the 25-rule cap (2 rules per build).
var builds = new List<BuildFilter>
{
    LoadBuild(ResolvedPath, FilterColors.Gold, FilterColors.Silver),
};

foreach (var b in builds)
{
    Console.WriteLine($"Build: {b.Name}");
    Console.WriteLine($"  affix pool ({b.Pool.Count}): {string.Join(", ", b.Pool.Select(id => b.Names[id]))}");
    if (b.Dropped.Count > 0)
    {
        // Some of these are genuinely not filterable in-game (conditional/granular affixes);
        // others are real coarse stats/skills simply missing from AffixDatabase. To add a
        // missing one, make a single-affix loot filter for it in D4, export, and run:
        //   dotnet run --project D4BuildFilter.Tester -- decode <code>
        // then paste the captured id into AffixDatabase.cs.
        Console.WriteLine($"  not yet filterable ({b.Dropped.Count}) — export single-affix filters in D4 to capture any of these IDs:");
        foreach (var d in b.Dropped) Console.WriteLine($"      - {d}");
    }
    if (b.UniquesTargeted.Count > 0)
        Console.WriteLine($"  build uniques -> PURPLE ({b.UniquesTargeted.Count}): {string.Join(", ", b.UniquesTargeted)}");
    if (b.UniquesPending.Count > 0)
        Console.WriteLine($"  build uniques without an id yet ({b.UniquesPending.Count}) — export to capture: {string.Join(", ", b.UniquesPending)}");
    Console.WriteLine($"  GOLD = >={Strict} of pool   SECONDARY = >={Loose} of pool\n");
}

// Affix-count rules apply to RARE + LEGENDARY only (the items whose affixes are
// the variable you're hunting). Uniques/mythics are handled separately.
const uint RareLeg = Rarity.Rare | Rarity.Legendary;

// Assemble the filter. D4 applies rules TOP-DOWN, FIRST match wins (verified: in-game
// guidance + the rootsxo reference, whose only HideAll rule sits last as a catch-all).
// So emit MOST-SPECIFIC first, broad hide-all last. List order == in-game top-to-bottom.
// strictEndgame: gate the build-affix + greater-affix rules on Ancestral tier so ONLY
// top item-power gear highlights (great for T6+ farming, hides everything while leveling).
void Emit(bool strictEndgame, string label, string outPath)
{
    byte[][] Tier(params byte[][] conds) =>
        strictEndgame ? conds.Append(Conditions.Ancestral()).ToArray() : conds;

    var rules = new List<byte[]>();
    // 1. The build's OWN uniques -> purple (per-unique type-8). Dormant until we have ids.
    foreach (var b in builds)
        if (b.UniqueIds.Count > 0)
            rules.Add(FilterBuilder.MakeRule($"{b.Name} build uniques", Visibility.Recolor,
                new[] { Conditions.RarityMask(Rarity.Unique), Conditions.Uniques(b.UniqueIds) }, FilterColors.Purple));
    // 2. Rare/leg with >=3 build affixes -> gold (keepers).
    foreach (var b in builds)
        rules.Add(FilterBuilder.MakeRule($"{b.Name} rare/leg [{Strict}+]", Visibility.Recolor,
            Tier(Conditions.RarityMask(RareLeg), Conditions.Affixes(b.Pool, Strict)), b.Color));
    // 3. Rare/leg with >=2 build affixes -> silver (one reroll from great).
    foreach (var b in builds)
        rules.Add(FilterBuilder.MakeRule($"{b.Name} rare/leg [{Loose}+]", Visibility.Recolor,
            Tier(Conditions.RarityMask(RareLeg), Conditions.Affixes(b.Pool, Loose)), b.Dim));
    // 4. Item-power tier -> orange. INTERIM: "Ancestral" (type 2 = 4) is the only item-power
    //    signal we can encode today — it's exactly what rootsxo uses. Stands in for the future
    //    900 / 850 split, which needs D4's numeric "Item Power Range" condition (not yet captured).
    rules.Add(FilterBuilder.MakeRule("Ancestral (high item power)", Visibility.Recolor,
        new[] { Conditions.RarityMask(RareLeg), Conditions.Ancestral() }, FilterColors.Orange));
    // 5. Greater Affixes -> blue: any rare/leg with >=1 GA not already matched above.
    rules.Add(FilterBuilder.MakeRule("Greater Affixes", Visibility.Recolor,
        Tier(Conditions.RarityMask(RareLeg), Conditions.GreaterAffix(1)), FilterColors.Blue));
    // 5. Charms & Seals -> green (rootsxo idea): surface them by item type, any rarity,
    //    so the hide rule doesn't eat them. Not tier-gated.
    rules.Add(FilterBuilder.MakeRule("Charms & Seals", Visibility.Recolor,
        new[] { Conditions.Types(new[] { ItemTypes.Charm, ItemTypes.Seal }) }, FilterColors.Green));
    // 6. Any remaining Codex-of-Power upgrade -> white (pick up for the aspect, then salvage).
    rules.Add(FilterBuilder.MakeRule("Codex Upgrades", Visibility.Recolor,
        new[] { Conditions.Codex() }, FilterColors.White));
    // 7. Hide the clutter: Common / Magic / Rare / Legendary that nothing above matched.
    //    NOT Unique (build ones purple above; rest fall through to default) and NOT Mythic —
    //    mythics drop untouched with their natural beam + "tink".
    rules.Add(FilterBuilder.MakeRule("Hide the rest", Visibility.HideAll,
        new[] { Conditions.RarityMask(Rarity.Common | Rarity.Magic | Rarity.Rare | Rarity.Legendary) }));

    var filterBytes = FilterBuilder.MakeFilter("D4BuildFilter", rules);
    var code = FilterBuilder.ToImportCode(filterBytes);
    var ruleCount = ProtobufReader.Read(filterBytes).Count(f => f is { Field: 1, WireType: 2 });
    Console.WriteLine($"=== {label}: {rules.Count} rules (cap 25), {filterBytes.Length} bytes, round-trip {(ruleCount == rules.Count ? "OK" : "MISMATCH")} ===");
    Console.WriteLine(code + "\n");
    File.WriteAllText(outPath, code);
}

Emit(false, "NORMAL", @"C:\Sync\Projects\D4BuildFilter\last_code.txt");
Emit(true, "STRICT ENDGAME (Ancestral-only)", @"C:\Sync\Projects\D4BuildFilter\last_code_strict.txt");

record BuildFilter(string Name, uint Color, uint Dim, List<uint> Pool, Dictionary<uint, string> Names,
    List<string> Dropped, List<uint> UniqueIds, List<string> UniquesTargeted, List<string> UniquesPending);
