namespace D4BuildFilter.Core;

/// <summary>Sidecar store for pasted-text builds the user has favorited. Persists each paste's raw
/// text alongside <see cref="FavoritesStore"/> at <c>%LOCALAPPDATA%\MedicKsMight\pastes\&lt;hash&gt;.txt</c>
/// so a "community paste" favorite (synthetic <c>paste://&lt;hash&gt;</c> URL) can be re-loaded later.
/// Identity is the same hash <see cref="MainViewModel"/> computes from the paste text.</summary>
public sealed class PasteStore
{
    private readonly string _dir;

    public PasteStore(string? dir = null)
    {
        _dir = dir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MedicKsMight", "pastes");
    }

    public void Save(string hash, string text)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, $"{hash}.txt"), text);
    }

    public string? Load(string hash)
    {
        var p = Path.Combine(_dir, $"{hash}.txt");
        return File.Exists(p) ? File.ReadAllText(p) : null;
    }

    public void Remove(string hash)
    {
        var p = Path.Combine(_dir, $"{hash}.txt");
        if (File.Exists(p)) File.Delete(p);
    }
}
