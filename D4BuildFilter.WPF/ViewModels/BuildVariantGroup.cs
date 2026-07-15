using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using D4BuildFilter.Core;

namespace D4BuildFilter.WPF.ViewModels;

/// <summary>One selected build and the variants that feed only that build's Super Build rules.</summary>
public sealed partial class BuildVariantGroup : ObservableObject
{
    public string BuildName => Build.Build;
    public ResolvedBuild Build { get; }
    public ObservableCollection<VariantOption> Variants { get; }
    private readonly Action<BuildVariantGroup, int> changePriority;
    private readonly Action<BuildVariantGroup, bool, FilterColorEntry> changeColor;
    private int priority;
    private IReadOnlyList<int> priorityChoices = [1];
    private FilterColorEntry chaseColor = FilterColors.EntryFor(FilterColors.Red);
    private FilterColorEntry keeperColor = FilterColors.EntryFor(FilterColors.Gold);
    private bool hasColorPickers;
    private bool tierColorsEnabled = true;
    private string tierEmissionNote = "";

    public int Priority
    {
        get => priority;
        set
        {
            if (value == priority || !PriorityChoices.Contains(value)) return;
            changePriority(this, value);
        }
    }

    public IReadOnlyList<int> PriorityChoices => priorityChoices;
    public IReadOnlyList<FilterColorEntry> Palette => FilterColors.Registry;

    public FilterColorEntry ChaseColor
    {
        get => chaseColor;
        set
        {
            if (value is null || value == chaseColor) return;
            chaseColor = value;
            OnPropertyChanged();
            changeColor(this, true, value);
        }
    }

    public FilterColorEntry KeeperColor
    {
        get => keeperColor;
        set
        {
            if (value is null || value == keeperColor) return;
            keeperColor = value;
            OnPropertyChanged();
            changeColor(this, false, value);
        }
    }

    public bool HasColorPickers => hasColorPickers;
    public bool TierColorsEnabled => tierColorsEnabled;
    public string TierEmissionNote => tierEmissionNote;

    [ObservableProperty] private string header;

    public BuildVariantGroup(ResolvedBuild build, ObservableCollection<VariantOption> variants,
        string header, int priority, Action<BuildVariantGroup, int> changePriority,
        Action<BuildVariantGroup, bool, FilterColorEntry> changeColor)
    {
        Build = build;
        Variants = variants;
        this.header = header;
        this.priority = priority;
        this.changePriority = changePriority;
        this.changeColor = changeColor;
    }

    internal void SetPriority(int value)
    {
        if (priority == value) return;
        priority = value;
        OnPropertyChanged(nameof(Priority));
    }

    internal void SetPriorityCount(int count)
    {
        priorityChoices = Enumerable.Range(1, count).ToArray();
        OnPropertyChanged(nameof(PriorityChoices));
    }

    internal void SetColors(FilterColorEntry chase, FilterColorEntry keeper, bool showPickers)
    {
        if (chaseColor != chase)
        {
            chaseColor = chase;
            OnPropertyChanged(nameof(ChaseColor));
        }
        if (keeperColor != keeper)
        {
            keeperColor = keeper;
            OnPropertyChanged(nameof(KeeperColor));
        }
        if (hasColorPickers == showPickers) return;
        hasColorPickers = showPickers;
        OnPropertyChanged(nameof(HasColorPickers));
    }

    internal void SetTierEmission(bool emitted, string reason)
    {
        if (tierColorsEnabled != emitted)
        {
            tierColorsEnabled = emitted;
            OnPropertyChanged(nameof(TierColorsEnabled));
        }
        var note = emitted ? "" : reason;
        if (tierEmissionNote == note) return;
        tierEmissionNote = note;
        OnPropertyChanged(nameof(TierEmissionNote));
    }
}
