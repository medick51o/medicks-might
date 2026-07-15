using System.Text.RegularExpressions;

namespace D4BuildFilter.Core;

/// <summary>The filter-relevant part of a resolved guide at the moment a favorite compiled.
/// Lists are sets in practice: comparison normalizes case and whitespace and ignores ordering.</summary>
public sealed record BuildSnapshot(
    DateTime CapturedUtc,
    IReadOnlyList<BuildVariantSnapshot> Variants,
    string? BuildName = null,
    string? ClassName = null)
{
    public static BuildSnapshot Capture(ResolvedBuild build, DateTime? capturedUtc = null) => new(
        capturedUtc ?? DateTime.UtcNow,
        build.Variants.Select(BuildVariantSnapshot.Capture).ToList(),
        build.Build,
        build.Class);
}

public sealed record BuildVariantSnapshot(
    string Name,
    IReadOnlyList<BuildAffixSnapshot> Affixes,
    IReadOnlyList<string> Uniques,
    IReadOnlyList<string> TalismanSets)
{
    internal static BuildVariantSnapshot Capture(ResolvedVariant variant)
    {
        // Slot-aware sources already repeat their flat affix pool in Slots. Prefer the richer form
        // so the alarm can say "Fury Cost Reduction (boots)" without double-counting it.
        var affixes = variant.Slots is { Count: > 0 }
            ? variant.Slots.SelectMany(slot => slot.Affixes.Select(name => new BuildAffixSnapshot(name, slot.Slot)))
            : variant.Affixes.Select(name => new BuildAffixSnapshot(name, null));
        return new(variant.Name, affixes.ToList(), variant.Uniques.ToList(),
            (variant.TalismanSets ?? []).ToList());
    }
}

public sealed record BuildAffixSnapshot(string Name, string? Slot);

public enum BuildDriftKind { Affix, Unique, TalismanSet, BuildName, Class }

public sealed record BuildDriftChange(
    bool Added, BuildDriftKind Kind, string Name, string Variant, string? Slot = null)
{
    public string PlainText
    {
        get
        {
            var label = Kind switch
            {
                BuildDriftKind.BuildName => $"build {Name}",
                BuildDriftKind.Class => $"class {Name}",
                _ => Name,
            };
            return $"{(Added ? "+" : "−")} {label}"
                + (string.IsNullOrWhiteSpace(Slot) ? "" : $" ({Slot.Trim().ToLowerInvariant()})");
        }
    }
}

public sealed record BuildDriftDiff(DateTime BaselineCapturedUtc, IReadOnlyList<BuildDriftChange> Changes)
{
    public bool HasDrift => Changes.Count > 0;
    public string Summary => string.Join(" · ", Changes.Select(change => change.PlainText));
}

/// <summary>Pure comparison for favorite snapshots. Null means there was no baseline, while a
/// non-null empty result means the guide was checked and its filter-relevant content is unchanged.</summary>
public static class BuildDrift
{
    public static BuildDriftDiff? Compare(BuildSnapshot? baseline, ResolvedBuild fresh) =>
        baseline is null ? null : Compare(baseline, BuildSnapshot.Capture(fresh));

    public static BuildDriftDiff Compare(BuildSnapshot baseline, BuildSnapshot fresh)
    {
        var oldItems = Flatten(baseline);
        var newItems = Flatten(fresh);
        var changes = new List<BuildDriftChange>();

        AddIdentityChanges(changes, BuildDriftKind.BuildName, baseline.BuildName, fresh.BuildName);
        AddIdentityChanges(changes, BuildDriftKind.Class, baseline.ClassName, fresh.ClassName);

        foreach (var key in newItems.Keys.Except(oldItems.Keys).OrderBy(k => k, StringComparer.Ordinal))
            changes.Add(newItems[key] with { Added = true });
        foreach (var key in oldItems.Keys.Except(newItems.Keys).OrderBy(k => k, StringComparer.Ordinal))
            changes.Add(oldItems[key] with { Added = false });

        return new BuildDriftDiff(baseline.CapturedUtc, changes);
    }

    private static void AddIdentityChanges(List<BuildDriftChange> changes, BuildDriftKind kind,
        string? baseline, string? fresh)
    {
        // Snapshots written before identity was added deserialize these fields as null. Unknown is
        // not evidence of change; a successful compile will replace the legacy snapshot afterward.
        if (string.IsNullOrWhiteSpace(baseline) || string.IsNullOrWhiteSpace(fresh)
            || Normalize(baseline) == Normalize(fresh))
            return;

        changes.Add(new(false, kind, Clean(baseline), ""));
        changes.Add(new(true, kind, Clean(fresh), ""));
    }

    private static Dictionary<string, BuildDriftChange> Flatten(BuildSnapshot snapshot)
    {
        var items = new Dictionary<string, BuildDriftChange>(StringComparer.Ordinal);
        foreach (var variant in snapshot.Variants)
        {
            var variantKey = Normalize(variant.Name);
            foreach (var affix in variant.Affixes)
                Add(items, BuildDriftKind.Affix, affix.Name, variant.Name, variantKey, affix.Slot);
            foreach (var unique in variant.Uniques)
                Add(items, BuildDriftKind.Unique, unique, variant.Name, variantKey, null);
            foreach (var set in variant.TalismanSets)
                Add(items, BuildDriftKind.TalismanSet, set, variant.Name, variantKey, null);
        }
        return items;
    }

    private static void Add(Dictionary<string, BuildDriftChange> items, BuildDriftKind kind,
        string name, string variant, string variantKey, string? slot)
    {
        var cleanName = Clean(name);
        var cleanSlot = string.IsNullOrWhiteSpace(slot) ? null : Clean(slot);
        var key = $"{variantKey}\u001f{kind}\u001f{Normalize(cleanSlot)}\u001f{Normalize(cleanName)}";
        items.TryAdd(key, new BuildDriftChange(true, kind, cleanName, Clean(variant), cleanSlot));
    }

    private static string Clean(string value) => Regex.Replace(value.Trim(), @"\s+", " ");
    private static string Normalize(string? value) => value is null ? "" : Clean(value).ToUpperInvariant();
}
