using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using D4BuildFilter.Core;

const int MaxRuleName = 24;
var fetchedAt = DateTimeOffset.UtcNow;
var rows = new List<CatalogRow>();
var fetches = new List<FetchReceipt>();

await Fetch("Maxroll", "Endgame", TierListFetcher.MaxrollUrlFor(MaxrollList.Endgame),
    () => TierListFetcher.FetchMaxrollAsync(MaxrollList.Endgame));
await Fetch("Maxroll", "Bossing", TierListFetcher.MaxrollUrlFor(MaxrollList.Bossing),
    () => TierListFetcher.FetchMaxrollAsync(MaxrollList.Bossing));
await Fetch("Maxroll", "Leveling", TierListFetcher.MaxrollUrlFor(MaxrollList.Leveling),
    () => TierListFetcher.FetchMaxrollAsync(MaxrollList.Leveling));
await Fetch("Maxroll", "Push", TierListFetcher.MaxrollUrlFor(MaxrollList.Push),
    () => TierListFetcher.FetchMaxrollAsync(MaxrollList.Push));
await Fetch("Maxroll", "Speedfarm", TierListFetcher.MaxrollUrlFor(MaxrollList.Speedfarm),
    () => TierListFetcher.FetchMaxrollAsync(MaxrollList.Speedfarm));

await Fetch("D4Builds", "Endgame", TierListFetcher.D4BuildsUrlFor(D4BuildsList.Endgame),
    () => TierListFetcher.FetchD4BuildsAsync(D4BuildsList.Endgame));
await Fetch("D4Builds", "Leveling", TierListFetcher.D4BuildsUrlFor(D4BuildsList.Leveling),
    () => TierListFetcher.FetchD4BuildsAsync(D4BuildsList.Leveling));
await Fetch("D4Builds", "Tower", TierListFetcher.D4BuildsUrlFor(D4BuildsList.Tower),
    () => TierListFetcher.FetchD4BuildsAsync(D4BuildsList.Tower));

await Fetch("Mobalytics", "Endgame", TierListFetcher.MobalyticsUrlFor(MobalyticsList.Endgame),
    () => TierListFetcher.FetchMobalyticsAsync(MobalyticsList.Endgame));
await Fetch("Mobalytics", "Leveling", TierListFetcher.MobalyticsUrlFor(MobalyticsList.Leveling),
    () => TierListFetcher.FetchMobalyticsAsync(MobalyticsList.Leveling));
await Fetch("Mobalytics", "Pushing", TierListFetcher.MobalyticsUrlFor(MobalyticsList.Pushing),
    () => TierListFetcher.FetchMobalyticsAsync(MobalyticsList.Pushing));

if (fetches.Any(f => f.Error is not null))
    throw new InvalidOperationException("At least one live tier-list fetch failed; refusing to print a partial receipt.");

var identities = rows
    .GroupBy(r => r.IdentityKey, StringComparer.Ordinal)
    .Select(g => g.First())
    .OrderBy(r => r.Source, StringComparer.Ordinal)
    .ThenBy(r => r.Name, StringComparer.Ordinal)
    .ThenBy(r => r.IdentityKey, StringComparer.Ordinal)
    .ToList();

var tagInputs = identities.Select(r => Tagger.Analyze(r.Name, r.ClassName, r.Source, r.Url)).ToList();
var shippingInputs = identities.Select(r => ShippingBuild(r)).ToList();
var resolved = BuildTagger.Resolve(shippingInputs);
var reversedResolved = BuildTagger.Resolve(shippingInputs.AsEnumerable().Reverse().ToList()).Reverse().ToList();
var orderStabilityMismatches = resolved.Zip(reversedResolved).Count(x => !string.Equals(x.First, x.Second, StringComparison.Ordinal));
if (orderStabilityMismatches > 0)
    throw new InvalidOperationException($"Resolver changed {orderStabilityMismatches} tag(s) when input order was reversed.");
var resolvedCollisions = resolved.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList();
// Global duplicates are evidence, not failure: shipping resolution intentionally sees only the
// selected 2-4 builds. The table below remains a whole-corpus maintenance stress receipt.
var tagsByIdentity = identities.Zip(resolved).ToDictionary(x => x.First.IdentityKey, x => x.Second,
    StringComparer.Ordinal);

var overlong = rows.Where(r => Sample(tagsByIdentity[r.IdentityKey]).Length > MaxRuleName).ToList();
var empty = rows.Where(r => tagsByIdentity[r.IdentityKey].Length == 0).ToList();

Console.WriteLine("DETERMINISTIC BUILD-NAME SHORTHAND — LIVE CATALOG RECEIPT");
Console.WriteLine("=========================================================");
Console.WriteLine($"Fetched at: {fetchedAt:yyyy-MM-dd HH:mm:ss 'UTC'}");
Console.WriteLine("Data mode: LIVE ONLY (no cache or fixtures)");
Console.WriteLine($"Catalog rows: {rows.Count}; unique build identities: {identities.Count}");
Console.WriteLine($"Failures: {fetches.Count(f => f.Error is not null)}; over 24 chars: {overlong.Count}; empty tags: {empty.Count}; resolved-tag collisions: {resolvedCollisions.Count}; order-stability mismatches: {orderStabilityMismatches}");
Console.WriteLine();

Console.WriteLine("1. ALGORITHM (PLAIN STEPS)");
Console.WriteLine("---------------------------");
Console.WriteLine("1) Input identity is build name + class + source + build URL. URL is only a stable tie-breaker; it is never shown in the tag.");
Console.WriteLine("2) Unicode-normalize (Form KD), remove combining marks, split camel-case, map punctuation to spaces, and keep ASCII letters/digits.");
Console.WriteLine("3) For the base only, prefer the text before a spaced dash; suffix text remains collision evidence. Remove product noise (Build, Guide, Endgame, Leveling, Tier, Diablo, season markers), all known class names, and articles. Keep connectors such as 'of' for readable acronyms.");
Console.WriteLine("4) If the cleaned distinctive name fits 10 characters, keep it verbatim. Only longer single words use a 7-character consonant skeleton; longer multi-word names use initials (Dance of Knives -> DoK).");
Console.WriteLine("5) Resolve only within the selected 2-4 builds. Collisions append class initial first, then Mx/D4/Mb only for the same build selected from two sites. Hash suffixes are forbidden.");
Console.WriteLine("6) Curated seasonal overrides run before the algorithm. BuildTagOverrides.json is reviewed with seasonal tier data. Tags never exceed 10 characters.");
Console.WriteLine("7) Tier labels are canonical short words (Leg, Rare, Anc). The worst sample is '{10-char tag} Rare (Silver)' = 24, so the color suffix cannot be clamped away.");
Console.WriteLine();

Console.WriteLine("2. LIVE FETCH RECEIPTS");
Console.WriteLine("-----------------------");
foreach (var fetch in fetches)
    Console.WriteLine($"{fetch.Source,-11} {fetch.ListKind,-10} {fetch.Count,3} rows  {fetch.Url}");
Console.WriteLine();

Console.WriteLine("3. FULL TABLE — EVERY LIVE TIER-LIST ROW");
Console.WriteLine("-----------------------------------------");
Console.WriteLine("Source | List | Tier | Class | Build name | Tag | Sample full rule | Chars | Flags");
foreach (var row in rows.OrderBy(r => r.Source, StringComparer.Ordinal)
                         .ThenBy(r => r.ListKind, StringComparer.Ordinal)
                         .ThenBy(r => TierRank(r.Tier))
                         .ThenBy(r => r.ClassName, StringComparer.Ordinal)
                         .ThenBy(r => r.Name, StringComparer.Ordinal))
{
    var tag = tagsByIdentity[row.IdentityKey];
    var sample = Sample(tag);
    var flags = sample.Length > MaxRuleName ? "OVER-24" : tag.Length == 0 ? "EMPTY-TAG" : "OK";
    Console.WriteLine($"{Cell(row.Source)} | {Cell(row.ListKind)} | {Cell(row.Tier)} | {Cell(row.ClassName)} | {Cell(row.Name)} | {tag} | {sample} | {sample.Length} | {flags}");
}
Console.WriteLine();

var collisionGroups = tagInputs.GroupBy(x => x.BaseTag, StringComparer.OrdinalIgnoreCase)
    .Where(g => g.Count() > 1)
    .OrderByDescending(g => g.Count())
    .ThenBy(g => g.Key, StringComparer.Ordinal)
    .ToList();

Console.WriteLine("4. PRE-RESOLUTION COLLISIONS (GLOBAL-CATALOG STRESS TEST)");
Console.WriteLine("--------------------------------------------------------");
Console.WriteLine($"Base-tag collision clusters: {collisionGroups.Count}. The real resolver sees only the selected 2-4 builds; this run deliberately resolves all {identities.Count} identities together.");
foreach (var group in collisionGroups)
{
    Console.WriteLine();
    Console.WriteLine($"[{group.Key}] — {group.Count()} identities");
    foreach (var input in group.OrderBy(x => x.CanonicalIdentity, StringComparer.Ordinal))
    {
        var index = tagInputs.IndexOf(input);
        Console.WriteLine($"  {input.Source,-11} {input.OriginalName} -> {resolved[index]}");
    }
}
Console.WriteLine();

Console.WriteLine("5. TEN UGLIEST RESULTS (MECHANICALLY SELECTED, NOT CHERRY-PICKED)");
Console.WriteLine("----------------------------------------------------------------");
Console.WriteLine("Score penalizes hash fallback, source suffixes, collision expansion, very short tags, digits, and heavy name loss. Highest score is ugliest.");
var ugliest = tagInputs.Select((input, index) => Ugliness(input, resolved[index]))
    .OrderByDescending(x => x.Score)
    .ThenBy(x => x.Input.OriginalName, StringComparer.Ordinal)
    .ThenBy(x => x.Input.CanonicalIdentity, StringComparer.Ordinal)
    .Take(10)
    .ToList();
foreach (var ugly in ugliest)
    Console.WriteLine($"{ugly.Score,3}  {ugly.Input.Source,-11} {ugly.Input.OriginalName} -> {ugly.Tag}  [{string.Join("; ", ugly.Reasons)}]");
Console.WriteLine();

Console.WriteLine("6. SCALING-COLOR RECOMMENDATION");
Console.WriteLine("--------------------------------");
Console.WriteLine("For 3-4 build filters, switch to colors-by-tier: every chase rule red, every keeper/rare rule one shared second color, with the tag carrying build ownership. Per-build color pairs encode two facts in one scarce palette, become hard to remember, and run out before four builds; tier colors preserve the fastest visual decision on the ground, while names solve the slower in-menu toggle problem. Keep per-build pairs only as an explicit legacy/advanced option, not the scaling default.");
Console.WriteLine();
Console.WriteLine("LIMIT: This receipt proves deterministic string bounds against today's live catalogs. It cannot prove D4 import/display behavior; final in-game validation remains required.");

async Task Fetch(string source, string listKind, string url, Func<Task<TierList>> fetch)
{
    try
    {
        var list = await fetch();
        if (list.Builds.Count == 0 && listKind != "Tower")
            throw new InvalidOperationException("Parser returned zero builds.");
        foreach (var build in list.Builds)
            rows.Add(new CatalogRow(source, listKind, build.Tier, build.ClassName, build.Name, build.Url));
        fetches.Add(new FetchReceipt(source, listKind, url, list.Builds.Count, null));
    }
    catch (Exception ex)
    {
        fetches.Add(new FetchReceipt(source, listKind, url, 0, $"{ex.GetType().Name}: {ex.Message}"));
    }
}

static string Sample(string tag) => $"{tag} Rare (Silver)";
static string Cell(string value) => value.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');
static int TierRank(string tier) => tier switch { "God" => 0, "S" => 1, "A" => 2, "B" => 3, "C" => 4, "D" => 5, "Support" => 6, _ => 99 };

static UglyResult Ugliness(TagInput input, string tag)
{
    var reasons = new List<string>();
    var score = 0;
    if (tag.StartsWith('B') && tag.Skip(1).All(char.IsDigit)) { score += 100; reasons.Add("non-ASCII/hash fallback"); }
    var hasSourceSuffix = tag.EndsWith("Mx", StringComparison.Ordinal) || tag.EndsWith("D4", StringComparison.Ordinal) || tag.EndsWith("Mb", StringComparison.Ordinal);
    if (tag.Any(char.IsDigit) && !tag.EndsWith("D4", StringComparison.Ordinal)) { score += 35; reasons.Add("hash/digit suffix"); }
    if (hasSourceSuffix) { score += 25; reasons.Add("source suffix"); }
    if (!tag.Equals(input.BaseTag, StringComparison.Ordinal)) { score += 15; reasons.Add("collision-expanded"); }
    if (tag.Length <= 3) { score += 12; reasons.Add("very short"); }
    var lettersInName = input.AsciiFull.Count(char.IsLetter);
    if (lettersInName > 0 && (double)tag.Count(char.IsLetter) / lettersInName < 0.22) { score += 8; reasons.Add("heavy abbreviation"); }
    if (input.UsedPrimaryFallback) { score += 20; reasons.Add("noise stripping exhausted primary words"); }
    if (reasons.Count == 0) reasons.Add("lowest readability score among remaining rows");
    return new UglyResult(input, tag, score, reasons);
}

static CompiledBuild ShippingBuild(CatalogRow row) => new(row.Name, row.ClassName,
    FilterColors.Red, FilterColors.Pink, [], new Dictionary<uint, string>(), [], [], [], [], [], [], [])
    { Source = row.Source };

sealed record CatalogRow(string Source, string ListKind, string Tier, string ClassName, string Name, string Url)
{
    public string IdentityKey => $"{Source.ToLowerInvariant()}|{Name.Trim().ToLowerInvariant()}|{ClassName.Trim().ToLowerInvariant()}|{Url.Trim().ToLowerInvariant()}";
}
sealed record FetchReceipt(string Source, string ListKind, string Url, int Count, string? Error);
sealed record UglyResult(TagInput Input, string Tag, int Score, IReadOnlyList<string> Reasons);

sealed record TagInput(
    string OriginalName,
    string ClassName,
    string Source,
    string Url,
    string CanonicalIdentity,
    string BaseTag,
    string SemanticKey,
    string DetailChars,
    string AsciiFull,
    bool UsedPrimaryFallback);

static class Tagger
{
    private const int MaxBaseTag = 7;

    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "build", "builds", "guide", "guides", "endgame", "leveling", "levelling", "tier", "list",
        "diablo", "d4", "season", "seasonal", "meta", "setup", "version", "variant",
        "barbarian", "barb", "druid", "necromancer", "necro", "rogue", "sorcerer", "sorc",
        "spiritborn", "paladin", "warlock"
    };

    private static readonly HashSet<string> Articles = new(StringComparer.OrdinalIgnoreCase)
        { "a", "an", "the" };

    private static readonly HashSet<string> Connectors = new(StringComparer.OrdinalIgnoreCase)
        { "of", "and", "to", "for", "with", "n" };

    public static TagInput Analyze(string name, string className, string source, string url)
    {
        var asciiFull = ToAsciiWords(name);
        var classWords = ToAsciiWords(className).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var spacedDash = Regex.Match(name, @"\s[\-–—]\s");
        var primaryRaw = spacedDash.Success ? name[..spacedDash.Index] : Regex.Replace(name, @"\s*[\(\[].*$", "");
        var primaryTokens = Tokens(ToAsciiWords(primaryRaw));
        var fullTokens = Tokens(asciiFull);

        bool IsNoise(string token) => Noise.Contains(token) || Articles.Contains(token) || classWords.Contains(token) || Regex.IsMatch(token, @"^s\d+$", RegexOptions.IgnoreCase);
        var meaningfulPrimary = primaryTokens.Where(t => !IsNoise(t)).ToList();
        var usedFallback = false;
        if (meaningfulPrimary.Count == 0)
        {
            meaningfulPrimary = primaryTokens.Where(t => !Articles.Contains(t)).ToList();
            usedFallback = true;
        }

        string baseTag;
        if (meaningfulPrimary.Count == 0)
        {
            baseTag = "B" + StableBase36(name, 6);
            usedFallback = true;
        }
        else
        {
            var significant = meaningfulPrimary.Where(t => !Connectors.Contains(t)).ToList();
            if (significant.Count <= 1)
                baseTag = OneWord(significant.FirstOrDefault() ?? meaningfulPrimary[0], MaxBaseTag);
            else
                baseTag = InitialTag(meaningfulPrimary, MaxBaseTag);
        }
        if (baseTag.Length == 0) baseTag = "B" + StableBase36(name, 6);

        var semanticTokens = fullTokens.Where(t => !IsNoise(t)).ToList();
        if (semanticTokens.Count == 0) semanticTokens = fullTokens;
        var semanticKey = string.Join("", semanticTokens).ToLowerInvariant() + "|" + ToAsciiWords(className).Replace(" ", "").ToLowerInvariant();

        var primaryCount = Math.Min(primaryTokens.Count, fullTokens.Count);
        var suffixTokens = fullTokens.Skip(primaryCount).Where(t => !IsNoise(t)).ToList();
        var detail = new StringBuilder();
        foreach (var token in suffixTokens) if (token.Length > 0) detail.Append(char.ToUpperInvariant(token[0]));
        foreach (var token in semanticTokens.Where(t => !Connectors.Contains(t)))
        {
            foreach (var c in token.Skip(1).Where(IsConsonant)) detail.Append(char.ToLowerInvariant(c));
            foreach (var c in token.Skip(1).Where(c => !IsConsonant(c) && char.IsLetterOrDigit(c))) detail.Append(char.ToLowerInvariant(c));
        }

        var canonical = $"{asciiFull.ToLowerInvariant()}|{ToAsciiWords(className).ToLowerInvariant()}|{source.ToLowerInvariant()}|{url.Trim().ToLowerInvariant()}";
        return new TagInput(name, className, source, url, canonical, baseTag, semanticKey,
            DeduplicateChars(detail.ToString()), asciiFull, usedFallback);
    }

    public static IReadOnlyList<string> Resolve(IReadOnlyList<TagInput> inputs, int maxLength)
    {
        var result = inputs.Select(x => x.BaseTag).ToArray();
        foreach (var baseGroup in inputs.Select((input, index) => (input, index))
                     .GroupBy(x => x.input.BaseTag, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            var semanticGroups = baseGroup.GroupBy(x => x.input.SemanticKey, StringComparer.Ordinal).ToList();
            var semanticLabels = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var semanticGroup in semanticGroups)
            {
                var representative = semanticGroup.First().input;
                var label = representative.BaseTag;
                if (semanticGroups.Count > 1)
                {
                    var room = maxLength - label.Length;
                    var found = false;
                    for (var take = 1; take <= room; take++)
                    {
                        var candidate = label + PrefixOrHash(representative.DetailChars, representative.SemanticKey, take);
                        var unique = semanticGroups.Count(other =>
                        {
                            var o = other.First().input;
                            return string.Equals(candidate, o.BaseTag + PrefixOrHash(o.DetailChars, o.SemanticKey, take), StringComparison.OrdinalIgnoreCase);
                        }) == 1;
                        if (unique) { label = candidate; found = true; break; }
                    }
                    if (!found) label = Fit(label, StableBase36(representative.SemanticKey, Math.Min(3, room)), maxLength);
                }
                semanticLabels[semanticGroup.Key] = label;
            }

            foreach (var semanticGroup in semanticGroups)
            {
                var members = semanticGroup.OrderBy(x => x.input.CanonicalIdentity, StringComparer.Ordinal).ToList();
                var label = semanticLabels[semanticGroup.Key];
                if (members.Count == 1)
                {
                    result[members[0].index] = label;
                    continue;
                }

                foreach (var sourceGroup in members.GroupBy(x => SourceCode(x.input.Source), StringComparer.Ordinal))
                {
                    var sourceMembers = sourceGroup.ToList();
                    if (sourceMembers.Count == 1)
                    {
                        var member = sourceMembers[0];
                        result[member.index] = Fit(label, sourceGroup.Key, maxLength);
                    }
                    else
                    {
                        foreach (var member in sourceMembers)
                        {
                            var suffix = sourceGroup.Key + StableBase36(member.input.CanonicalIdentity, 2);
                            result[member.index] = Fit(label, suffix, maxLength);
                        }
                    }
                }
            }
        }

        // Final deterministic safety net for rare cross-branch collisions.
        foreach (var duplicate in result.Select((tag, index) => (tag, index))
                     .GroupBy(x => x.tag, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            foreach (var member in duplicate)
                result[member.index] = Fit(inputs[member.index].BaseTag, StableBase36(inputs[member.index].CanonicalIdentity, 4), maxLength);
        }
        return result;
    }

    private static List<string> Tokens(string ascii) => ascii.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

    private static string ToAsciiWords(string value)
    {
        var camel = Regex.Replace(value, @"(?<=[a-z0-9])(?=[A-Z])", " ");
        var normalized = camel.Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c <= 127 && char.IsLetterOrDigit(c) ? c : ' ');
        }
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    private static string OneWord(string word, int max)
    {
        if (word.Length == 0) return "";
        var chars = new List<char> { char.ToUpperInvariant(word[0]) };
        chars.AddRange(word.Skip(1).Where(IsConsonant).Select(char.ToLowerInvariant));
        chars.AddRange(word.Skip(1).Where(c => !IsConsonant(c) && char.IsLetterOrDigit(c)).Select(char.ToLowerInvariant));
        return new string(chars.Take(max).ToArray());
    }

    private static string InitialTag(IReadOnlyList<string> words, int max)
    {
        var initials = new string(words.Where(w => w.Length > 0)
            .Select(w => Connectors.Contains(w) ? char.ToLowerInvariant(w[0]) : char.ToUpperInvariant(w[0]))
            .Take(max).ToArray());
        if (initials.Length >= 3) return initials;

        var significant = words.Where(w => !Connectors.Contains(w)).ToList();
        var expanded = new StringBuilder();
        for (var depth = 0; expanded.Length < Math.Min(4, max); depth++)
            foreach (var word in significant)
                if (depth < word.Length && expanded.Length < Math.Min(4, max))
                    expanded.Append(depth == 0 ? char.ToUpperInvariant(word[depth]) : char.ToLowerInvariant(word[depth]));
        return expanded.ToString();
    }

    private static bool IsConsonant(char c) => char.IsLetter(c) && !"aeiouAEIOU".Contains(c);

    private static string DeduplicateChars(string value)
    {
        var seen = new HashSet<char>();
        return new string(value.Where(c => seen.Add(char.ToLowerInvariant(c))).ToArray());
    }

    private static string PrefixOrHash(string detail, string key, int count)
    {
        var combined = detail + StableBase36(key, count);
        return combined[..Math.Min(count, combined.Length)];
    }

    private static string Fit(string prefix, string suffix, int max)
    {
        var keep = Math.Max(1, max - suffix.Length);
        return prefix[..Math.Min(prefix.Length, keep)] + suffix[..Math.Min(suffix.Length, max - Math.Min(prefix.Length, keep))];
    }

    private static string SourceCode(string source) => source switch
    {
        "Maxroll" => "Mx", "D4Builds" => "D4", "Mobalytics" => "Mb", _ => "X" + StableBase36(source, 1)
    };

    private static string StableBase36(string value, int length)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        const string alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
        var sb = new StringBuilder(length);
        ulong state = BitConverter.ToUInt64(bytes, 0);
        for (var i = 0; i < length; i++)
        {
            if (state == 0) state = BitConverter.ToUInt64(bytes, 8 + (i % 2) * 8);
            sb.Append(alphabet[(int)(state % 36)]);
            state /= 36;
        }
        return sb.ToString();
    }
}
