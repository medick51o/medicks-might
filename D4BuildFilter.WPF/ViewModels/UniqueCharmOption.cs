using System;
using CommunityToolkit.Mvvm.ComponentModel;
using D4BuildFilter.Core;

namespace D4BuildFilter.WPF.ViewModels;

/// <summary>One unique-charm checkbox (v1.0.2, Medick's list): every S14 unique charm is offered,
/// ALL pre-checked to show. Checked = drops on the ground in its natural color (build ones still go
/// purple above) · unchecked = falls to the hide. Toggling recompiles live. The ctor writes the
/// field directly so building the list doesn't fire the recompile callback.</summary>
public sealed partial class UniqueCharmOption : ObservableObject
{
    public UniqueCharm Charm { get; }
    public string Label => Charm.Name;

    private readonly Action _onChanged;

    [ObservableProperty] private bool isChecked;

    public UniqueCharmOption(UniqueCharm charm, bool isChecked, Action onChanged)
    {
        Charm = charm;
        _onChanged = onChanged;
        this.isChecked = isChecked;   // field write — must not fire the recompile callback
    }

    partial void OnIsCheckedChanged(bool value) => _onChanged();
}
