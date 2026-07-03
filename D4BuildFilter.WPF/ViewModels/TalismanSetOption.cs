using CommunityToolkit.Mvvm.ComponentModel;
using D4BuildFilter.Core;

namespace D4BuildFilter.WPF.ViewModels;

/// <summary>One per-class talisman-set checkbox (v1.0.1, Medick's wireframe): all ten of the build
/// class's sets are offered; the sets the BUILD calls for come pre-checked; toggling recompiles
/// live. Checked = shown (the green / ancestral-red rules scope to it) · unchecked = hidden.</summary>
public sealed partial class TalismanSetOption : ObservableObject
{
    public TalismanSet Set { get; }
    public string Label { get; }

    private readonly Action _onChanged;

    [ObservableProperty] private bool isChecked;

    public TalismanSetOption(TalismanSet set, bool isChecked, Action onChanged)
    {
        Set = set;
        Label = set.Class == "Generic" ? $"{set.Name} (generic)" : set.Name;
        _onChanged = onChanged;
        this.isChecked = isChecked;   // field write — must not fire the recompile callback
    }

    partial void OnIsCheckedChanged(bool value) => _onChanged();
}
