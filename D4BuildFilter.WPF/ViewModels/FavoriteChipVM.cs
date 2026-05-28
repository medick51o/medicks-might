using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D4BuildFilter.Core;

namespace D4BuildFilter.WPF.ViewModels;

/// <summary>A starred build on the landing page's "Your Favorites" row. Wraps a persisted
/// <see cref="FavoriteEntry"/> and re-uses the BuildChip visual style. Provenance is rendered as a
/// secondary line ("Maxroll · Endgame · S · added 3d ago") so the user can tell at a glance where a
/// favorite came from and roughly how stale it is — the data itself is re-fetched live on click so
/// the build is always current.</summary>
public sealed partial class FavoriteChipVM : ObservableObject
{
    public FavoriteEntry Entry { get; }
    public string Name => Entry.Name;
    public string Url => Entry.Url;
    public string ClassName => Entry.ClassName;
    public Brush ClassColor { get; }

    /// <summary>"Maxroll · Endgame · S · added 3d ago" — what reads under the chip name.</summary>
    public string Provenance { get; }

    /// <summary>Hover text showing the full URL + dates so power users can audit.</summary>
    public string Tip { get; }

    private readonly Action<string> _load;
    private readonly Action<FavoriteChipVM> _remove;

    public FavoriteChipVM(FavoriteEntry entry, Action<string> load, Action<FavoriteChipVM> remove)
    {
        Entry = entry;
        _load = load;
        _remove = remove;
        ClassColor = TierBuildVM.ClassBrush(entry.ClassName);
        Provenance = BuildProvenance(entry);
        Tip = $"{entry.ClassName} · click to re-fetch (live) and load into the filter\n"
            + $"{entry.Url}\n"
            + $"Added {entry.DateAdded.ToLocalTime():yyyy-MM-dd}, last opened "
            + $"{entry.DateLastOpened.ToLocalTime():yyyy-MM-dd}";
    }

    [RelayCommand] private void Load() => _load(Url);
    [RelayCommand] private void Remove() => _remove(this);

    private static string BuildProvenance(FavoriteEntry e)
    {
        var parts = new List<string> { e.Source };
        if (!string.IsNullOrEmpty(e.TierKind)) parts.Add(e.TierKind!);
        if (!string.IsNullOrEmpty(e.Tier)) parts.Add(e.Tier!);
        parts.Add($"added {AgoLabel(e.DateAdded)}");
        return string.Join(" · ", parts);
    }

    /// <summary>Compact relative-time label ("3d ago", "2w ago"). Used so a user can spot a stale
    /// favorite at a glance — D4 metas shift every season.</summary>
    public static string AgoLabel(DateTime utc)
    {
        var diff = DateTime.UtcNow - utc;
        if (diff.TotalDays >= 365) return $"{(int)(diff.TotalDays / 365)}y ago";
        if (diff.TotalDays >= 30) return $"{(int)(diff.TotalDays / 30)}mo ago";
        if (diff.TotalDays >= 7) return $"{(int)(diff.TotalDays / 7)}w ago";
        if (diff.TotalDays >= 1) return $"{(int)diff.TotalDays}d ago";
        if (diff.TotalHours >= 1) return $"{(int)diff.TotalHours}h ago";
        return "just now";
    }
}
