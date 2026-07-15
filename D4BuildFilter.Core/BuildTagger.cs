using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace D4BuildFilter.Core;

/// <summary>Creates readable, deterministic rule-name tags for only the builds selected into one
/// Super Build. Curated seasonal names win; already-short distinctive names stay untouched.</summary>
public static partial class BuildTagger
{
    public const int MaxTagLength = 10;

    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "build", "builds", "guide", "guides", "endgame", "leveling", "levelling", "tier", "list",
        "diablo", "d4", "season", "seasonal", "meta", "setup", "version", "variant",
        "barbarian", "barb", "druid", "necromancer", "necro", "rogue", "sorcerer", "sorc",
        "spiritborn", "paladin", "warlock",
    };

    private static readonly HashSet<string> Articles = new(StringComparer.OrdinalIgnoreCase) { "a", "an", "the" };
    private static readonly HashSet<string> Connectors = new(StringComparer.OrdinalIgnoreCase) { "of", "and", "to", "for", "with", "n" };
    private static readonly string[] CompoundSuffixes = ["seeker", "strike", "storm", "shot", "trap", "spear", "wave", "ball"];
    private static readonly Lazy<IReadOnlyList<OverrideRow>> Overrides = new(LoadOverrides);

    public static IReadOnlyList<string> Resolve(IReadOnlyList<CompiledBuild> builds) =>
        Resolve(builds, Overrides.Value);

    internal static IReadOnlyList<string> ResolveWithOverrides(IReadOnlyList<CompiledBuild> builds,
        string overridesJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(overridesJson);
            return Resolve(builds, ParseOverrides(doc));
        }
        catch (Exception ex)
        {
            AppLog.Write("build-tags", $"override document ignored — {ex.GetType().Name}: {ex.Message}");
            return Resolve(builds, []);
        }
    }

    private static IReadOnlyList<string> Resolve(IReadOnlyList<CompiledBuild> builds,
        IReadOnlyList<OverrideRow> overrides)
    {
        var cleaned = builds.Select(b => Clean(b.Name, b.Class)).ToArray();
        var tags = builds.Select((b, i) => Curated(b, overrides) ?? MakeBase(cleaned[i])).ToArray();

        foreach (var group in tags.Select((tag, index) => (tag, index))
                     .GroupBy(x => x.tag, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            var members = group.Select(x => x.index).ToList();
            if (members.Select(i => builds[i].Class).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                AddShortestClassSuffix(builds, tags, members);

            ResolveSourceCollisions(builds, cleaned, tags, members);
            ResolveReadableCollisions(cleaned, tags, members);
        }
        EnsureGloballyUnique(builds, cleaned, tags);
        return tags;
    }

    private static void EnsureGloballyUnique(IReadOnlyList<CompiledBuild> builds, string[] cleaned, string[] tags)
    {
        // Earlier collision repair can mint a tag already owned outside its original group. Re-scan
        // the whole selected set until every tag is unique, preserving the first deterministic owner.
        for (int pass = 0; pass < Math.Max(1, builds.Count * 2); pass++)
        {
            var collision = tags.Select((tag, index) => (tag, index))
                .GroupBy(x => x.tag, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (collision is null) return;

            var members = collision.Select(x => x.index)
                .OrderBy(i => $"{cleaned[i]}|{builds[i].Class}|{builds[i].Source}", StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i)
                .ToList();
            var memberSet = members.ToHashSet();
            var used = tags.Where((_, i) => !memberSet.Contains(i))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var i in members)
            {
                var chosen = UniqueCandidates(builds[i], cleaned[i], tags[i], builds.Count)
                    .First(candidate => used.Add(candidate));
                tags[i] = chosen;
            }
        }

        throw new InvalidOperationException("Build tags could not be made unique.");
    }

    private static IEnumerable<string> UniqueCandidates(CompiledBuild build, string cleaned,
        string current, int buildCount)
    {
        yield return current;
        for (int take = 1; take <= 4; take++)
            yield return Fit(current, $" {Title(Prefix(string.Concat(Words(build.Class)), take))}");
        if (!string.IsNullOrWhiteSpace(build.Source))
            yield return Fit(current, $" {SourceTag(build.Source)}");
        var compact = cleaned.Replace(" ", "");
        for (int take = 1; take <= MaxTagLength; take++)
            yield return Prefix(compact, take);
        for (int n = 0; n < Math.Max(4, buildCount * 2); n++)
            yield return Fit(current, $" {(char)('A' + n)}");
    }

    private static void AddShortestClassSuffix(IReadOnlyList<CompiledBuild> builds, string[] tags,
        IReadOnlyList<int> members)
    {
        for (int take = 1; take <= 4; take++)
        {
            var suffixes = members.Select(i => Prefix(string.Concat(Words(builds[i].Class)), take)).ToList();
            if (suffixes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != members.Count) continue;
            for (int n = 0; n < members.Count; n++)
                tags[members[n]] = Fit(tags[members[n]], $" {Title(suffixes[n])}");
            return;
        }
        foreach (var i in members) tags[i] = Fit(tags[i], $" {ClassInitial(builds[i].Class)}");
    }

    /// <summary>Used by the maintenance receipt so corpus reruns exercise the shipping algorithm.</summary>
    public static string Preview(string name, string className) => MakeBase(Clean(name, className));

    private static void ResolveSourceCollisions(IReadOnlyList<CompiledBuild> builds, string[] cleaned,
        string[] tags, IReadOnlyList<int> members)
    {
        foreach (var collision in members.GroupBy(i => tags[i], StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            var indices = collision.ToList();
            bool sameBuild = indices.Select(i => cleaned[i]).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
                && indices.Select(i => builds[i].Class).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
            bool differentKnownSources = indices.All(i => !string.IsNullOrWhiteSpace(builds[i].Source))
                && indices.Select(i => SourceTag(builds[i].Source)).Distinct(StringComparer.OrdinalIgnoreCase).Count() == indices.Count;
            if (sameBuild && differentKnownSources)
                foreach (var i in indices) tags[i] = Fit(tags[i], SourceTag(builds[i].Source));
        }
    }

    private static void ResolveReadableCollisions(string[] cleaned, string[] tags, IReadOnlyList<int> members)
    {
        foreach (var collision in members.GroupBy(i => tags[i], StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            var indices = collision.OrderBy(i => cleaned[i], StringComparer.OrdinalIgnoreCase).ToList();
            for (int take = 1; take <= MaxTagLength; take++)
            {
                var candidates = indices.Select(i => Prefix(cleaned[i].Replace(" ", ""), take)).ToList();
                if (candidates.Distinct(StringComparer.OrdinalIgnoreCase).Count() != indices.Count) continue;
                for (int n = 0; n < indices.Count; n++) tags[indices[n]] = candidates[n];
                break;
            }
        }
    }

    private static string? Curated(CompiledBuild build, IReadOnlyList<OverrideRow> overrides)
    {
        var exact = overrides.FirstOrDefault(x => Same(x.Name, build.Name) && Same(x.Class, build.Class)
            && Same(x.Source, build.Source));
        if (exact is not null) return exact.Tag;
        var byName = overrides.Where(x => Same(x.Name, build.Name) && Same(x.Class, build.Class)).ToList();
        return byName.Count == 1 ? byName[0].Tag : null;
    }

    private static string MakeBase(string cleaned)
    {
        if (cleaned.Length == 0) return "Build";
        if (cleaned.Length <= MaxTagLength) return cleaned;
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 1)
        {
            var initials = string.Concat(words.Select(w => Connectors.Contains(w)
                ? char.ToLowerInvariant(w[0]).ToString()
                : char.ToUpperInvariant(w[0]).ToString()));
            return initials[..Math.Min(initials.Length, 7)];
        }
        return SingleWord(words[0]);
    }

    private static string SingleWord(string word)
    {
        foreach (var suffix in CompoundSuffixes)
            if (word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && word.Length > suffix.Length + 1)
                return Skeleton(word[..^suffix.Length], 3) + Skeleton(word[^suffix.Length..], 3);
        return Skeleton(word, 7);
    }

    private static string Skeleton(string word, int max)
    {
        var sb = new StringBuilder();
        if (word.Length > 0) sb.Append(char.ToUpperInvariant(word[0]));
        foreach (var c in word.Skip(1))
            if (char.IsLetterOrDigit(c) && !"aeiou".Contains(char.ToLowerInvariant(c))) sb.Append(char.ToLowerInvariant(c));
        foreach (var c in word.Skip(1))
            if (char.IsLetterOrDigit(c) && "aeiou".Contains(char.ToLowerInvariant(c))) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString()[..Math.Min(max, sb.Length)];
    }

    private static string Clean(string name, string className)
    {
        var classTokens = Words(className).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tokens = Words(name).Where(t => !Noise.Contains(t) && !Articles.Contains(t)
            && !classTokens.Contains(t) && !SeasonToken().IsMatch(t)).ToList();
        if (tokens.Count == 0) tokens = Words(name).Where(t => !Articles.Contains(t)).ToList();
        return string.Join(" ", tokens.Select(Title));
    }

    private static IEnumerable<string> Words(string value)
    {
        value = CamelBoundary().Replace(value.Normalize(NormalizationForm.FormKD), "$1 $2");
        var ascii = new StringBuilder();
        foreach (var c in value)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            ascii.Append(c <= 127 && char.IsLetterOrDigit(c) ? c : ' ');
        }
        return ascii.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string Title(string word) => word.Length == 0 ? word
        : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
    private static char ClassInitial(string className)
    {
        var first = Words(className).FirstOrDefault();
        return string.IsNullOrEmpty(first) ? 'X' : char.ToUpperInvariant(first[0]);
    }
    private static string SourceTag(string source) => source.Contains("maxroll", StringComparison.OrdinalIgnoreCase) ? "Mx"
        : source.Contains("d4build", StringComparison.OrdinalIgnoreCase) ? "D4"
        : source.Contains("mobalytics", StringComparison.OrdinalIgnoreCase) ? "Mb"
        : Prefix(string.Concat(Words(source)), 2);
    private static string Fit(string tag, string suffix) => tag[..Math.Min(tag.Length, MaxTagLength - suffix.Length)] + suffix;
    private static string Prefix(string value, int length) => value[..Math.Min(value.Length, length)];
    private static bool Same(string left, string right) => string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<OverrideRow> LoadOverrides()
    {
        try
        {
            var assembly = typeof(BuildTagger).Assembly;
            using var stream = assembly.GetManifestResourceStream("D4BuildFilter.Core.Data.BuildTagOverrides.json")
                ?? throw new InvalidOperationException("Embedded build-tag overrides are missing.");
            using var doc = JsonDocument.Parse(stream);
            return ParseOverrides(doc);
        }
        catch (Exception ex)
        {
            // Lazy must never become permanently faulted: the deterministic algorithm is a safe
            // fallback when packaging or the document itself is broken.
            AppLog.Write("build-tags", $"embedded overrides ignored — {ex.GetType().Name}: {ex.Message}");
            return [];
        }
    }

    private static IReadOnlyList<OverrideRow> ParseOverrides(JsonDocument doc)
    {
        if (!doc.RootElement.TryGetProperty("overrides", out var rows)
            || rows.ValueKind != JsonValueKind.Array)
            throw new FormatException("override document has no overrides array");

        var accepted = new List<OverrideRow>();
        var seenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.EnumerateArray())
        {
            try
            {
                string source = RequiredString(row, "source");
                string className = RequiredString(row, "class");
                string name = RequiredString(row, "name");
                string tag = RequiredString(row, "tag");
                string identity = $"{source.Trim()}|{className.Trim()}|{name.Trim()}";
                if (string.IsNullOrWhiteSpace(tag) || tag.Length > MaxTagLength
                    || tag.Contains('(') || tag.Contains(')'))
                    throw new FormatException($"tag must be 1-{MaxTagLength} characters and contain no parentheses");
                if (seenTags.Contains(tag)) throw new FormatException($"duplicate tag '{tag}'");
                if (seenIdentities.Contains(identity)) throw new FormatException("duplicate source/class/name identity");
                seenTags.Add(tag);
                seenIdentities.Add(identity);
                accepted.Add(new OverrideRow(source, className, name, tag));
            }
            catch (Exception ex)
            {
                AppLog.Write("build-tags", $"override row ignored — {ex.Message}");
            }
        }
        return accepted;
    }

    private static string RequiredString(JsonElement row, string property)
    {
        if (!row.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
            throw new FormatException($"missing {property}");
        return value.GetString()!;
    }

    private sealed record OverrideRow(string Source, string Class, string Name, string Tag);

    [GeneratedRegex(@"([a-z0-9])([A-Z])")]
    private static partial Regex CamelBoundary();
    [GeneratedRegex(@"^s\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonToken();
}
