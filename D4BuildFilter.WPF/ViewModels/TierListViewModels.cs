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
/// now that d4builds and mobalytics chips often show only the skill name without the class suffix.
/// <para>Knows its own provenance (Source / TierKind / Tier) so that when the user stars the chip
/// the favorite captures where it came from. <see cref="IsFavorited"/> mirrors the store so the
/// star indicator stays in sync if the same build is favorited/unfavorited elsewhere in the UI.</para></summary>
public sealed partial class TierBuildVM : ObservableObject
{
    public string Name { get; }
    public string ClassName { get; }
    public string Url { get; }
    public string Source { get; }        // "Maxroll" | "D4Builds" | "Mobalytics"
    public string? TierKind { get; }     // "Endgame" | "Bossing" | ... — null for non-tier chips
    public string? Tier { get; }         // "S" | "A" | ... | "God" | "Support" — null for non-tier chips
    public string Tip => $"{ClassName} · click to load or adjust the active selection · right-click to open on {Source}";
    public Brush ClassColor { get; }
    private readonly Action<TierBuildVM> _load;
    private readonly Action<TierBuildVM>? _toggleFav;
    private readonly Action<string>? _openSource;
    private readonly Func<TierBuildVM, bool, bool>? _toggleSelection;
    private readonly Func<TierBuildVM, int, bool>? _changePriority;
    private int? _priority;
    private IReadOnlyList<int> _priorityChoices = [];

    [ObservableProperty] private bool _isFavorited;
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            var oldValue = _isSelected;
            _isSelected = value;
            OnPropertyChanged();
            if (_toggleSelection?.Invoke(this, value) is false)
            {
                _isSelected = oldValue;
                OnPropertyChanged();
            }
            OnPropertyChanged(nameof(HasPriority));
        }
    }

    public int? Priority
    {
        get => _priority;
        set
        {
            if (value == _priority || value is null || !_priorityChoices.Contains(value.Value)) return;
            _changePriority?.Invoke(this, value.Value);
        }
    }

    public IReadOnlyList<int> PriorityChoices => _priorityChoices;
    public bool HasPriority => IsSelected && Priority is not null;

    public TierBuildVM(TierBuild b, string source, string? tierKind, string? tier,
        Action<TierBuildVM> load, Action<TierBuildVM>? toggleFav = null, bool isFavorited = false,
        Action<string>? openSource = null,
        Func<TierBuildVM, bool, bool>? toggleSelection = null, bool isSelected = false,
        Func<TierBuildVM, int, bool>? changePriority = null, int? priority = null,
        int priorityCount = 0)
    {
        Name = b.Name;
        ClassName = b.ClassName;
        Url = b.Url;
        Source = source;
        TierKind = tierKind;
        Tier = tier;
        _load = load;
        _toggleFav = toggleFav;
        _openSource = openSource;
        _toggleSelection = toggleSelection;
        _changePriority = changePriority;
        IsFavorited = isFavorited;
        _isSelected = isSelected;
        _priority = priority;
        _priorityChoices = Enumerable.Range(1, priorityCount).ToArray();
        ClassColor = ColorFor(b.ClassName);
    }

    internal void SyncSelection(bool value)
    {
        if (_isSelected == value) return;
        _isSelected = value;
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(HasPriority));
    }

    internal void SyncPriority(int? value, int selectedBuildCount)
    {
        _priority = value;
        _priorityChoices = Enumerable.Range(1, selectedBuildCount).ToArray();
        OnPropertyChanged(nameof(Priority));
        OnPropertyChanged(nameof(PriorityChoices));
        OnPropertyChanged(nameof(HasPriority));
    }

    [RelayCommand] private void Load() => _load(this);
    [RelayCommand] private void ToggleFavorite() => _toggleFav?.Invoke(this);
    /// <summary>Open this build on its source site (Maxroll/D4Builds/Mobalytics) in the browser —
    /// the "link-out + attribution" model: we drive traffic back to the source rather than presenting
    /// their rankings as our own redistributed data.</summary>
    [RelayCommand] private void OpenSource() { if (!string.IsNullOrWhiteSpace(Url)) _openSource?.Invoke(Url); }

    /// <summary>Public so FavoriteChipVM can render with the same class palette.</summary>
    public static Brush ClassBrush(string cls) => ColorFor(cls);

    /// <summary>Recognizable per-class color, tuned for legibility on the dimmed warlord background.
    /// Roughly tracks each class's in-game identity. Paladin is the one exception — pink, lifted
    /// from WoW's iconic Paladin color (Medick's call).</summary>
    private static Brush ColorFor(string cls) => cls switch
    {
        "Barbarian"   => Frozen(0xE0, 0x84, 0x5A),   // rust / amber
        "Druid"       => Frozen(0xC8, 0x96, 0x55),   // earth tan
        "Necromancer" => Frozen(0x9A, 0xCF, 0x7B),   // pale toxic green
        "Rogue"       => Frozen(0xE4, 0xCC, 0x4A),   // bright yellow
        "Sorcerer"    => Frozen(0x7C, 0xB6, 0xE6),   // ice blue
        "Spiritborn"  => Frozen(0x6F, 0xC9, 0xB8),   // teal / jade
        "Paladin"     => Frozen(0xF4, 0x8C, 0xBA),   // WoW Paladin pink (Medick's pick)
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

/// <summary>One tab in a source's tab strip (e.g., "Endgame", "Bossing", "Leveling" under Maxroll).
/// <see cref="Key"/> is the enum name (e.g. "Endgame") used as the command parameter; the active
/// tab visually highlights via the IsActive trigger on the TierTab style.</summary>
public sealed partial class TierTabVM : ObservableObject
{
    public string Label { get; }
    public string Key { get; }
    [ObservableProperty] private bool _isActive;
    public TierTabVM(string label, string key, bool isActive)
    {
        Label = label; Key = key; IsActive = isActive;
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

    // v1.0.5: the classic tier-list heat ramp (S red-hot → D cool blue) — the convention every
    // TierMaker/Maxroll-literate player already reads at a glance. The old scheme (S gold, C orange)
    // ranked hotter colors BELOW cooler ones and misread. God/Support sit outside the ramp and keep
    // their out-of-band flair colors.
    private static Brush BadgeFor(string tier) => tier switch
    {
        "God"     => Frozen(0xE0, 0x40, 0x8A),   // magenta — above-S flair
        "S"       => Frozen(0xE8, 0x4A, 0x4A),   // red (hottest)
        "A"       => Frozen(0xF0, 0x90, 0x2E),   // orange
        "B"       => Frozen(0xE8, 0xC8, 0x4A),   // yellow
        "C"       => Frozen(0x5F, 0xB8, 0x5F),   // green
        "D"       => Frozen(0x5A, 0x9B, 0xD8),   // blue (coolest)
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
