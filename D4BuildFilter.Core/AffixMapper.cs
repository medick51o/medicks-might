using System.Text.RegularExpressions;

namespace D4BuildFilter.Core;

/// <summary>How a granular build-guide affix was resolved to a filterable category.</summary>
public enum MapStrategy
{
    /// <summary>Normalized name equals a known coarse category exactly.</summary>
    Exact,
    /// <summary>Hand-authored mapping (guide phrasing → coarse category).</summary>
    Alias,
    /// <summary>Inferred from a keyword inside a conditional affix
    /// (e.g. "Crit Damage with Core Skills" → "Critical Strike Damage Multiplier").
    /// REVIEW THESE: only correct if the coarse filter is a true parent of the variant.</summary>
    Keyword,
    /// <summary>Resolved to a skill-rank affix.</summary>
    Skill,
    /// <summary>No coarse equivalent — not filterable in-game (or missing from the DB).</summary>
    Dropped,
}

/// <summary>One granular affix and the coarse category it resolved to (if any).</summary>
public sealed class AffixMapping
{
    public required string Source { get; init; }   // original string from the build guide
    public string? CoarseName { get; init; }        // resolved category name, null when dropped
    public uint? CoarseId { get; init; }            // filter affix ID, null when dropped
    public MapStrategy Strategy { get; init; }
    public string? Note { get; init; }              // keyword that fired, or why it dropped
    public bool Mapped => CoarseId.HasValue;
}

/// <summary>Result of mapping a whole build's affix wishlist.</summary>
public sealed class BuildMapping
{
    public required IReadOnlyList<AffixMapping> All { get; init; }

    /// <summary>Distinct coarse categories that survived, in first-seen order — ready for the encoder.</summary>
    public IReadOnlyList<(string Name, uint Id)> Coarse =>
        All.Where(m => m.Mapped)
           .Select(m => (m.CoarseName!, m.CoarseId!.Value))
           .GroupBy(t => t.Item2).Select(g => g.First()).ToList();

    public IReadOnlyList<AffixMapping> Dropped => All.Where(m => !m.Mapped).ToList();
}

/// <summary>
/// THE CRUX (see project memory: project-d4-lootfilter-compiler).
/// Build guides list hundreds of granular affixes ("Crit Damage with Core Skills",
/// "Damage to Close Enemies"); the in-game filter only filters ~77 coarse categories.
/// This maps granular → coarse, dropping anything with no coarse equivalent.
///
/// Resolution order (first hit wins): Skill → Exact → Alias → Keyword → Dropped.
/// Keyword hits are flagged for review because mapping a conditional variant to its
/// coarse parent is only correct if the parent filter actually matches the variant.
/// </summary>
public static class AffixMapper
{
    /// <summary>Guide phrasing (normalized) → canonical <see cref="AffixDatabase.Affixes"/> key.</summary>
    private static readonly Dictionary<string, string> RawAliases = new()
    {
        ["critical strike damage"] = "Critical Strike Damage Multiplier",
        ["crit damage"] = "Critical Strike Damage Multiplier",
        ["crit chance"] = "Critical Strike Chance",
        ["vulnerable damage"] = "Vulnerable Damage Multiplier",
        ["damage over time"] = "Damage Over Time Multiplier",
        ["dot"] = "Damage Over Time Multiplier",
        ["all damage"] = "All Damage Multiplier",
        ["damage"] = "All Damage Multiplier",
        ["all resistance"] = "Resistance to All Elements",
        ["max life"] = "Maximum Life",
        ["max resource"] = "Maximum Resource",
        ["cold damage"] = "Cold Damage Multiplier",
        ["fire damage"] = "Fire Damage Multiplier",
        ["lightning damage"] = "Lightning Damage Multiplier",
        ["physical damage"] = "Physical Damage Multiplier",
        ["poison damage"] = "Poison Damage Multiplier",
        ["shadow damage"] = "Shadow Damage Multiplier",
        ["holy damage"] = "Holy Damage Multiplier",
        ["lucky hit: up to a % chance to restore primary resource"] = "Lucky Hit Restore Primary Resource",
        ["maximum evade charges"] = "Maximum Evade Charge",   // plural in some guides; singular in the DB
        ["all damage multipler"] = "All Damage Multiplier",   // Mobalytics ships this affix slug misspelled
        // Mobalytics names resource regen as "<resource> per second"; map to our "<resource> Regeneration".
        ["fury per second"] = "Fury Regeneration",
        ["mana per second"] = "Mana Regeneration",
        ["spirit per second"] = "Spirit Regeneration",
        ["essence per second"] = "Essence Regeneration",
        ["energy per second"] = "Energy Regeneration",
        ["bonus weapon damage"] = "Weapon Damage",
    };

    /// <summary>Ordered most-specific-first. A conditional affix containing one of these
    /// keywords maps to the coarse parent (flagged Keyword for review).</summary>
    private static readonly (string Keyword, string Canonical)[] RawKeywords =
    {
        ("critical strike damage", "Critical Strike Damage Multiplier"),
        ("critical strike chance", "Critical Strike Chance"),
        ("vulnerable damage", "Vulnerable Damage Multiplier"),
        ("damage over time", "Damage Over Time Multiplier"),
        ("attack speed", "Attack Speed"),
        ("movement speed", "Movement Speed"),
        ("lucky hit chance", "Lucky Hit Chance"),
        ("maximum life", "Maximum Life"),
        ("cold damage", "Cold Damage Multiplier"),
        ("fire damage", "Fire Damage Multiplier"),
        ("lightning damage", "Lightning Damage Multiplier"),
        ("physical damage", "Physical Damage Multiplier"),
        ("poison damage", "Poison Damage Multiplier"),
        ("shadow damage", "Shadow Damage Multiplier"),
        ("holy damage", "Holy Damage Multiplier"),
    };

    private static readonly Dictionary<string, (string Name, uint Id)> AffixByNorm =
        AffixDatabase.Affixes.ToDictionary(kv => Normalize(kv.Key), kv => (kv.Key, kv.Value));

    private static readonly Dictionary<string, (string Name, uint Id)> SkillByNorm =
        AffixDatabase.Skills.ToDictionary(kv => Normalize(kv.Key), kv => (kv.Key, kv.Value));

    private static readonly Dictionary<string, (string Name, uint Id)> Alias =
        RawAliases.ToDictionary(kv => Normalize(kv.Key), kv => (kv.Value, AffixDatabase.Affixes[kv.Value]));

    private static readonly (string Keyword, string Name, uint Id)[] Keywords =
        RawKeywords.Select(k => (Normalize(k.Keyword), k.Canonical, AffixDatabase.Affixes[k.Canonical])).ToArray();

    private static readonly Regex SkillRanksLead = new(@"^\s*ranks?\s+to\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Lowercase, strip numbers / placeholders / +%[]() / punctuation, collapse whitespace.</summary>
    public static string Normalize(string raw)
    {
        var s = raw.ToLowerInvariant();
        s = Regex.Replace(s, @"\[[^\]]*\]", " ");      // [X], [12.5]
        s = Regex.Replace(s, @"\([^)]*\)", " ");        // (...)
        s = Regex.Replace(s, @"\d+(\.\d+)?", " ");      // numbers / decimals / ranges
        s = Regex.Replace(s, @"\bx\b", " ");            // literal "X" used as a value placeholder
        s = Regex.Replace(s, @"[^a-z ]", " ");          // +, %, #, punctuation, apostrophes
        s = Regex.Replace(s, @"\s+", " ").Trim();
        s = Regex.Replace(s, @"^to ", "");              // leading "to" from "+X to Intelligence"
        return s;
    }

    public static AffixMapping Map(string source)
    {
        var key = Normalize(source);

        // 1. Skill (direct name, or after a leading "Ranks to")
        var skillKey = SkillRanksLead.IsMatch(key) ? SkillRanksLead.Replace(key, "") : key;
        if (SkillByNorm.TryGetValue(skillKey, out var sk))
            return new AffixMapping { Source = source, CoarseName = sk.Name, CoarseId = sk.Id, Strategy = MapStrategy.Skill };

        // 2. Exact coarse category
        if (AffixByNorm.TryGetValue(key, out var ex))
            return new AffixMapping { Source = source, CoarseName = ex.Name, CoarseId = ex.Id, Strategy = MapStrategy.Exact };

        // 3. Explicit alias
        if (Alias.TryGetValue(key, out var al))
            return new AffixMapping { Source = source, CoarseName = al.Name, CoarseId = al.Id, Strategy = MapStrategy.Alias };

        // 4. Keyword inference (conditional variants) — flagged for review
        foreach (var (kw, name, id) in Keywords)
            if (key.Contains(kw))
                return new AffixMapping { Source = source, CoarseName = name, CoarseId = id, Strategy = MapStrategy.Keyword, Note = $"matched \"{kw}\"" };

        // 5. No coarse equivalent
        return new AffixMapping { Source = source, Strategy = MapStrategy.Dropped, Note = "no coarse category (not filterable, or missing from AffixDatabase)" };
    }

    public static BuildMapping MapBuild(IEnumerable<string> affixes) =>
        new() { All = affixes.Select(Map).ToList() };
}
