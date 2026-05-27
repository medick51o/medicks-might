using System;
using System.Collections.Generic;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D4BuildFilter.Core;

namespace D4BuildFilter.WPF.ViewModels;

/// <summary>One build on a tier list. Clicking opens its page on the source site.</summary>
public sealed partial class TierBuildVM : ObservableObject
{
    public string Name { get; }
    public string ClassName { get; }
    public string Url { get; }
    public string Tip => $"{ClassName} · click to open on the site";
    private readonly Action<string> _open;

    public TierBuildVM(TierBuild b, Action<string> open)
    {
        Name = b.Name;
        ClassName = b.ClassName;
        Url = b.Url;
        _open = open;
    }

    [RelayCommand] private void Open() => _open(Url);
}

/// <summary>A tier row (S / A / B) with its colored badge and the builds in it.</summary>
public sealed class TierGroupVM
{
    public string Tier { get; }
    public Brush Badge { get; }
    public IReadOnlyList<TierBuildVM> Builds { get; }

    public TierGroupVM(string tier, IReadOnlyList<TierBuildVM> builds)
    {
        Tier = tier;
        Builds = builds;
        Badge = BadgeFor(tier);
    }

    private static Brush BadgeFor(string tier) => tier switch
    {
        "S" => Frozen(0xE6, 0xB8, 0x00),   // gold
        "A" => Frozen(0x7B, 0xBF, 0x6A),   // green
        "B" => Frozen(0x5B, 0x8F, 0xD6),   // blue
        _   => Frozen(0x8a, 0x7e, 0x74),   // gray
    };

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }
}
