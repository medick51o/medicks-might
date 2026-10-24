namespace D4BuildFilter.Core;

/// <summary>The player's own ranking of saved builds. Community tier letters stay on
/// <see cref="FavoriteEntry.Tier"/>; this grouping is optional and old favorites files
/// that omit it still load.</summary>
public static class PersonalTierList
{
    private static readonly string[] Preferred = ["S", "A", "B", "C"];

    public sealed record TierGroup(string Name, IReadOnlyList<FavoriteEntry> Entries);

    public static IReadOnlyList<TierGroup> Group(IEnumerable<FavoriteEntry> entries)
    {
        var ranked = entries.Where(entry => !string.IsNullOrWhiteSpace(entry.PersonalGroup)).ToList();
        var names = ranked.Select(entry => entry.PersonalGroup!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(NameRank)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase);
        var groups = new List<TierGroup>();
        foreach (var name in names)
        {
            var members = ranked
                .Where(entry => entry.PersonalGroup!.Equals(name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.PersonalOrder)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            groups.Add(new TierGroup(name, members));
        }

        var unranked = entries.Where(entry => string.IsNullOrWhiteSpace(entry.PersonalGroup))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (unranked.Count > 0)
            groups.Add(new TierGroup("Unranked", unranked));
        return groups;
    }

    private static int NameRank(string name)
    {
        var index = Array.FindIndex(Preferred, preferred =>
            preferred.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? Preferred.Length : index;
    }
}
