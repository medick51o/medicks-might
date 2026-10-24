namespace D4BuildFilter.Core;

/// <summary>
/// Aspects a Rogue build can use that companion data still tags to a subset of classes.
/// Class order matches D4Companion AffixInfo: Sorcerer, Druid, Barbarian, Rogue, Necromancer,
/// Spiritborn, Paladin, Warlock. These five are generic (every class allowed).
/// <see cref="Aspect.IdSno"/> is the companion aspect definition id, not a loot-filter item id,
/// and it is never written into a filter.
/// </summary>
public static class AspectCatalog
{
    public sealed record Aspect(string Name, string IdName, string IdSno, bool[] AllowedForPlayerClass);

    private static bool[] Everyone() => [true, true, true, true, true, true, true, true];

    public static readonly IReadOnlyList<Aspect> Genericized =
    [
        new("Earthstriker's", "legendary_barb_014_x2", "2582877", Everyone()),
        new("Duelist's", "legendary_barb_005_x2", "2557499", Everyone()),
        new("Neurotoxic", "Legendary_Druid_116_x2", "2541747", Everyone()),
        new("Snowveiled", "legendary_sorc_108_x2", "2575214", Everyone()),
        new("Crushing", "legendary_druid_049_x2", "2542658", Everyone()),
    ];

    public static bool IsGeneric(Aspect aspect) =>
        aspect.AllowedForPlayerClass.Length == 8 && aspect.AllowedForPlayerClass.All(allowed => allowed);
}
