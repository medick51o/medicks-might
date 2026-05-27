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
    }
}
