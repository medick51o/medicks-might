using System;
using CommunityToolkit.Mvvm.ComponentModel;
using D4BuildFilter.Core;

namespace D4BuildFilter.WPF.ViewModels;

/// <summary>One selectable build variant (a maxroll profile). Toggling <see cref="IsSelected"/>
/// re-runs the compile so the pool/codes update live.</summary>
public sealed partial class VariantOption : ObservableObject
{
    public ResolvedVariant Variant { get; }
    public string Label { get; }

    private readonly Action _onChanged;

    [ObservableProperty]
    private bool isSelected = true;

    partial void OnIsSelectedChanged(bool value) => _onChanged();

    public VariantOption(ResolvedVariant variant, Action onChanged)
    {
        Variant = variant;
        _onChanged = onChanged;
        Label = $"{variant.Name}  ·  {variant.Affixes.Count} affixes, {variant.Uniques.Count} uniques";
        // Leveling variants default OFF — Maxroll/D4Builds/Mobalytics all publish "Leveling 1-70" /
        // "Leveling Skill Tree" / "Leveling" variants alongside endgame ones. Most users loading a
        // build are doing it for endgame play; leveling stats add noise (extra +XP / +Movement Speed
        // affixes) that pollute the filter pool and can quietly leave players matching leveling gear
        // mid-T6. They can still tick it on if they're actually leveling.
        if (variant.Name?.IndexOf("Leveling", StringComparison.OrdinalIgnoreCase) >= 0)
            isSelected = false;
    }
}
