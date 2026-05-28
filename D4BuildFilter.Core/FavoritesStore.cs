using System.Text.Json;
using System.Text.Json.Serialization;

namespace D4BuildFilter.Core;

/// <summary>One starred build. Stores a LIVE REFERENCE (url + provenance), not a snapshot — opening
/// a favorite re-fetches the build from its source so the user gets the current meta, not whatever
/// was there the day they starred it. <see cref="DateAdded"/> is shown so they can tell if a pick
/// has gone stale; <see cref="DateLastOpened"/> updates whenever they re-open it.</summary>
public sealed record FavoriteEntry(
    string Id,
    string Url,
    string Source,         // "Maxroll" | "D4Builds" | "Mobalytics" | "Paste"
    string? TierKind,      // "Endgame" | "Bossing" | "Leveling" | "Push" | "Speedfarm" | "Tower" | "Pushing" | null
    string? Tier,          // "S" | "A" | "B" | "C" | "D" | "God" | "Support" | null
    string Name,
    string ClassName,
    DateTime DateAdded,
    DateTime DateLastOpened);

/// <summary>Persisted list of starred builds. Defaults to
/// <c>%LOCALAPPDATA%\MedicKsMight\favorites.json</c> so it survives app re-installs (the publish zip
/// blows away the install dir, but AppData stays). Identity is by URL — toggle removes the existing
/// entry if any, else adds a fresh one.</summary>
public sealed class FavoritesStore
{
    private readonly string _path;
    private readonly List<FavoriteEntry> _entries = new();

    public FavoritesStore(string? path = null)
    {
        _path = path ?? DefaultPath();
        Load();
    }

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MedicKsMight", "favorites.json");

    public IReadOnlyList<FavoriteEntry> All => _entries;

    public bool Contains(string url) =>
        _entries.Any(e => string.Equals(e.Url, url, StringComparison.OrdinalIgnoreCase));

    public FavoriteEntry? Find(string url) =>
        _entries.FirstOrDefault(e => string.Equals(e.Url, url, StringComparison.OrdinalIgnoreCase));

    /// <summary>Toggle by URL. If a favorite with that URL exists it's removed; otherwise the supplied
    /// entry is added (with <see cref="FavoriteEntry.DateAdded"/> and <see cref="FavoriteEntry.DateLastOpened"/>
    /// stamped to now). Returns the resulting favorited-state (true = added, false = removed).</summary>
    public bool Toggle(FavoriteEntry candidate)
    {
        var existing = Find(candidate.Url);
        if (existing != null)
        {
            _entries.Remove(existing);
            Save();
            return false;
        }
        var now = DateTime.UtcNow;
        _entries.Add(candidate with { DateAdded = now, DateLastOpened = now });
        Save();
        return true;
    }

    public void Remove(string url)
    {
        var existing = Find(url);
        if (existing == null) return;
        _entries.Remove(existing);
        Save();
    }

    /// <summary>Stamp <see cref="FavoriteEntry.DateLastOpened"/> = now for this URL (no-op if not
    /// favorited). Call this when the user clicks a favorite chip — gives them a sense of which
    /// favorites they actually still use.</summary>
    public void StampOpened(string url)
    {
        var existing = Find(url);
        if (existing == null) return;
        _entries.Remove(existing);
        _entries.Add(existing with { DateLastOpened = DateTime.UtcNow });
        Save();
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<List<FavoriteEntry>>(json, JsonOpts);
            if (loaded != null) _entries.AddRange(loaded);
        }
        catch
        {
            // Corrupt file: better to start fresh than crash. The next Save() overwrites it.
        }
    }

    private void Save()
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(_entries, JsonOpts));
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
