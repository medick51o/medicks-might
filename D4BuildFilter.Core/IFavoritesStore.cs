namespace D4BuildFilter.Core;

/// <summary>Persistence abstraction for starred builds. The desktop app uses the file-backed
/// <see cref="FavoritesStore"/>; a future web backend can supply a per-account database
/// implementation without any consumer (ViewModel / API) needing to change.</summary>
public interface IFavoritesStore
{
    IReadOnlyList<FavoriteEntry> All { get; }
    bool Contains(string url);
    FavoriteEntry? Find(string url);
    /// <summary>Add the entry if its URL isn't present, else remove it. Returns true = now favorited.</summary>
    bool Toggle(FavoriteEntry candidate);
    void Remove(string url);
    /// <summary>Stamp DateLastOpened = now for this URL (no-op if not favorited).</summary>
    void StampOpened(string url);
}
