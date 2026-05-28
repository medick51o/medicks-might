using System;
using System.Collections.Generic;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D4BuildFilter.Core;

namespace D4BuildFilter.WPF.ViewModels;

/// <summary>One build on a tier list. Clicking LOADS it into the compiler (the source URL — a
/// maxroll guide page, d4builds build, or mobalytics build — is resolved by the fetchers).
/// The chip is rendered in the build's CLASS COLOR so the class is visible at a glance — important
/// now that d4builds and mobalytics chips often show only the skill name without the class suffix.</summary>
public sealed partial class TierBuildVM : ObservableObject
{
    public string Name { get; }
    public string ClassName { get; }
    public string Url { get; }
    public string Tip => $"{ClassName} · click to load this build into the filter";
    public Brush ClassColor { get; }
    private readonly Action<string> _load;

    public TierBuildVM(TierBuild b, Action<string> load)
    {
        Name = b.Name;
        ClassName = b.ClassName;
        Url = b.Url;
        _load = load;
        ClassColor = ColorFor(b.ClassName);
    }

    [RelayCommand] private void Load() => _load(Url);

    /// <summary>Recognizable per-class color, tuned for legibility on the dimmed warlord background.
    /// Roughly tracks each class's in-game identity.</summary>
    private static Brush ColorFor(string cls) => cls switch
    {
        "Barbarian"   => Frozen(0xE0, 0x84, 0x5A),   // rust / amber
        "Druid"       => Frozen(0xC8, 0x96, 0x55),   // earth tan
        "Necromancer" => Frozen(0x9A, 0xCF, 0x7B),   // pale toxic green
        "Rogue"       => Frozen(0xE4, 0xCC, 0x4A),   // bright yellow
        "Sorcerer"    => Frozen(0x7C, 0xB6, 0xE6),   // ice blue
        "Spiritborn"  => Frozen(0x6F, 0xC9, 0xB8),   // teal / jade
        "Paladin"     => Frozen(0xF2, 0xD4, 0x6A),   // bright gold
        "Warlock"     => Frozen(0xB5, 0x8A, 0xE6),   // purple
        _             => Frozen(0xE3, 0xD8, 0xCC),   // cream fallback
    };

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }
}

/// <summary>A tier row (God / S / A / B / C / D / Support) with its colored badge and the builds
/// in it.</summary>
public sealed class TierGroupVM
{
    public string Tier { get; }
    /// <summary>What the badge actually renders. Single letters for S/A/B/C/D, three-letter
    /// abbreviations for the word tiers (GOD / SUP) so the badge stays a compact pill.</summary>
    public string BadgeText { get; }
    public Brush Badge { get; }
    public IReadOnlyList<TierBuildVM> Builds { get; }

    public TierGroupVM(string tier, IReadOnlyList<TierBuildVM> builds)
    {
        Tier = tier;
        Builds = builds;
        Badge = BadgeFor(tier);
        BadgeText = tier switch { "God" => "GOD", "Support" => "SUP", _ => tier };
    }

    private static Brush BadgeFor(string tier) => tier switch
    {
        "God"     => Frozen(0xE0, 0x40, 0x8A),   // magenta — top tier flair
        "S"       => Frozen(0xE6, 0xB8, 0x00),   // gold
        "A"       => Frozen(0x7B, 0xBF, 0x6A),   // green
        "B"       => Frozen(0x5B, 0x8F, 0xD6),   // blue
        "C"       => Frozen(0xE0, 0x7A, 0x4D),   // orange
        "D"       => Frozen(0xB2, 0x51, 0x51),   // red-brown
        "Support" => Frozen(0x4D, 0xB3, 0xA8),   // teal — utility / multiplayer
        _         => Frozen(0x8a, 0x7e, 0x74),   // gray fallback
    };

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }
}
