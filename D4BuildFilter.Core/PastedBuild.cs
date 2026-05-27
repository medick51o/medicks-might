using System.Text.RegularExpressions;

namespace D4BuildFilter.Core;

/// <summary>
/// Universal paste-based build import: turn a chunk of text copied from ANY build guide
/// (maxroll, d4builds, mobalytics, Icy Veins, a Discord message…) into a <see cref="ResolvedBuild"/>
/// that feeds the same mapper → compiler pipeline as the maxroll fetcher.
///
/// The contract is deliberately loose: paste affix names (one per line, or comma/bullet separated)
/// and any unique item names. Each line is cleaned of bullets/priority-numbers, then:
///   • lines that exactly match a known unique (see <see cref="UniqueDatabase"/>) become build uniques;
///   • everything else is handed to <see cref="AffixMapper"/>, which resolves real affixes and
///     drops noise (headers, item names, flavour text) — drops are reported, not fatal.
/// Inspired by how d4lf / Diablo4Companion / D4LootBench accept pasted build guides instead of
/// locking the user to a single site's API.
/// </summary>
public static class PastedBuild
{
    private static readonly Regex LeadingBullet = new(@"^[\s\-\*•·●▪‣>]+", RegexOptions.Compiled);
    private static readonly Regex LeadingPriority = new(@"^\d+\s*[\.\):]\s*", RegexOptions.Compiled);
    private static readonly Regex SplitSeparators = new(@"[\r\n]+|(?:,\s)|(?:\s•\s)|(?: \| )", RegexOptions.Compiled);

    public static ResolvedBuild Parse(string text, string name = "Pasted Build")
    {
        var affixes = new List<string>();
        var uniques = new List<string>();
        var seenUnique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in SplitSeparators.Split(text))
        {
            var line = Clean(token);
            if (line.Length < 3) continue;                       // skip blanks / stray punctuation
            if (line.EndsWith(':')) continue;                    // section headers ("Helm:", "Weapon:")

            if (UniqueDatabase.TryGet(line, out _))
            {
                if (seenUnique.Add(line)) uniques.Add(line);     // exact unique-name match → build unique
                continue;
            }
            affixes.Add(line);                                   // mapper resolves it or reports it as a drop
        }

        return new ResolvedBuild(name, "Unknown",
            new[] { new ResolvedVariant("Pasted", affixes, uniques) });
    }

    private static string Clean(string raw)
    {
        var s = raw.Trim();
        s = LeadingBullet.Replace(s, "");
        s = LeadingPriority.Replace(s, "");
        return s.Trim();
    }
}
