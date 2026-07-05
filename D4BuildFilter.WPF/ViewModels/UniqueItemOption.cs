using System;
using CommunityToolkit.Mvvm.ComponentModel;
using D4BuildFilter.Core;

namespace D4BuildFilter.WPF.ViewModels;

/// <summary>One unique-item checkbox (v1.0.2, Medick's list): every regular unique, ALL pre-checked
/// to show. Uniques already drop by default, so this is a HIDE-list (the mirror of the charm show-
/// list): unchecking a unique adds it to the "Hide Uniques" rule. Toggling recompiles live. The
/// ctor writes the field directly so building the list doesn't fire the recompile callback.</summary>
public sealed partial class UniqueItemOption : ObservableObject
{
    public UniqueItem Item { get; }
    public string Label => Item.Name;

    private readonly Action _onChanged;

    [ObservableProperty] private bool isChecked;

    public UniqueItemOption(UniqueItem item, bool isChecked, Action onChanged)
    {
        Item = item;
        _onChanged = onChanged;
        this.isChecked = isChecked;   // field write — must not fire the recompile callback
    }

    partial void OnIsCheckedChanged(bool value) => _onChanged();
}
