namespace D4BuildFilter.Core;

// APPEND-ONLY REGISTRY: never remove an entry or reuse its value/short label. A future picker may
// hide an obsolete entry, but saved filters and codes must always be able to resolve its name.

/// <summary>A named rule-highlight color shipped as part of the filter palette.</summary>
public sealed record FilterColorEntry(uint Value, string FullName, string ShortLabel, bool DarkGroundRisk)
{
    public string Hex => $"#{Value & 0x00FFFFFFu:X6}";
}

/// <summary>Explicit unknown-color policy used to prove both shipped paths in every build configuration.</summary>
internal enum UnregisteredColorBehavior
{
    Throw,
    Fallback,
}

/// <summary>Rule highlight palette. D4's color field is ARGB: the uint32 is 0xAARRGGBB,
/// which Wire writes little-endian so the on-wire bytes are B,G,R,A. (Verified in-game:
/// packing R and B the other way made gold render as cyan and red as blue.)</summary>
public static class FilterColors
{
    private const string CustomColorAdvisory =
        "A rule uses an unregistered color; its rule-name suffix was safely shown as (Custom).";

    public static uint Make(byte r, byte g, byte b, byte a = 255)
        => ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

    public static readonly uint Red = Make(220, 0, 0);        // S14: 3-affix "chase" tier + ancestral charms
    public static readonly uint Pink = Make(255, 105, 180);   // S14: 2-affix "keeper" tier (#FF69B4 hot pink)
    public static readonly uint Gold = Make(255, 215, 0);     // legacy tier color (pre-S14); kept but unused
    public static readonly uint Silver = Make(170, 170, 170); // legacy tier color (pre-S14); kept but unused
    public static readonly uint Orange = Make(255, 140, 0);
    public static readonly uint Cyan = Make(0, 255, 255);
    public static readonly uint Blue = Make(34, 81, 232);     // rootsxo's "Greater Affixes" blue (#2251E8)
    public static readonly uint Green = Make(0, 200, 0);
    public static readonly uint White = Make(255, 255, 255);
    public static readonly uint Purple = Make(160, 32, 240);
    public static readonly uint Turquoise = Make(64, 224, 208);
    public static readonly uint LightGreen = Make(144, 238, 144);
    public static readonly uint LightBlue = Make(173, 216, 230);
    public static readonly uint SkyBlue = Make(135, 206, 235);
    public static readonly uint NeonBlue = Make(31, 81, 255);
    public static readonly uint LightPurple = Make(200, 158, 240);
    public static readonly uint LightOrange = Make(255, 179, 71);
    public static readonly uint Black = Make(16, 16, 16);

    public const uint Default = 0xFFFF0000;

    private static readonly FilterColorEntry[] Entries =
    [
        new(Red, "Red", "Red", false),
        new(Pink, "Pink", "Pink", false),
        new(Gold, "Gold", "Gold", false),
        new(Silver, "Silver", "Silver", false),
        new(Orange, "Orange", "Orange", false),
        new(Cyan, "Cyan", "Cyan", false),
        new(Blue, "Blue", "Blue", false),
        new(Green, "Green", "Green", false),
        new(White, "White", "White", false),
        new(Purple, "Purple", "Purple", false),
        new(Turquoise, "Turquoise", "Turq", false),
        new(LightGreen, "Light Green", "LtGrn", false),
        new(LightBlue, "Light Blue", "LtBlu", false),
        new(SkyBlue, "Sky Blue", "SkyBlu", false),
        new(NeonBlue, "Neon Blue", "NeonBl", true),
        new(LightPurple, "Light Purple", "LtPurp", false),
        new(LightOrange, "Light Orange", "LtOrng", false),
        new(Black, "Black", "Black", true),
    ];

    private static readonly IReadOnlyDictionary<uint, FilterColorEntry> ByValue;

    /// <summary>Every registered palette entry, in stable append-only order.</summary>
    public static IReadOnlyList<FilterColorEntry> Registry { get; } = Array.AsReadOnly(Entries);

    public static FilterColorEntry EntryFor(uint color) => ByValue.TryGetValue(color, out var entry)
        ? entry
        : throw new ArgumentOutOfRangeException(nameof(color), color, "The color is not registered.");

    public static bool TryGetEntry(uint color, out FilterColorEntry? entry) =>
        ByValue.TryGetValue(color, out entry);

    static FilterColors()
    {
        if (Entries.Any(entry => entry.ShortLabel.Length is < 1 or > 6))
            throw new InvalidOperationException("Every palette short label must contain 1 to 6 characters.");
        if (Entries.Select(entry => entry.Value & 0x00FFFFFFu).Distinct().Count() != Entries.Length)
            throw new InvalidOperationException("Palette RGB values must be unique.");
        if (Entries.Select(entry => entry.ShortLabel).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Entries.Length)
            throw new InvalidOperationException("Palette short labels must be unique.");

        ByValue = Entries.ToDictionary(entry => entry.Value);
    }

    /// <summary>Short color label for a rule-name suffix. Debug builds reject unknown colors;
    /// release builds return "Custom" and add one non-blocking advisory to the supplied sink.</summary>
    public static string NameOf(uint color, ICollection<string>? advisories = null) =>
        NameOf(color, advisories, DefaultUnregisteredColorBehavior);

    internal static string NameOf(uint color, ICollection<string>? advisories,
        UnregisteredColorBehavior unregisteredColorBehavior)
    {
        if (ByValue.TryGetValue(color, out var entry))
            return entry.ShortLabel;

        if (unregisteredColorBehavior == UnregisteredColorBehavior.Throw)
            throw new ArgumentOutOfRangeException(nameof(color), color,
                "Rule colors must be registered before they can be named.");

        if (advisories is not null && !advisories.Contains(CustomColorAdvisory))
            advisories.Add(CustomColorAdvisory);
        return "Custom";
    }

    private static UnregisteredColorBehavior DefaultUnregisteredColorBehavior =>
#if DEBUG
        UnregisteredColorBehavior.Throw;
#else
        UnregisteredColorBehavior.Fallback;
#endif
}
