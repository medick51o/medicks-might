namespace D4BuildFilter.Core;

/// <summary>Sidecar store for pasted-text builds the user has favorited. Persists each paste's raw
/// text alongside <see cref="FavoritesStore"/> at <c>%LOCALAPPDATA%\MedicKsMight\pastes\&lt;hash&gt;.txt</c>
/// so a "community paste" favorite (synthetic <c>paste://&lt;hash&gt;</c> URL) can be re-loaded later.
/// Identity is the same hash <see cref="MainViewModel"/> computes from the paste text.</summary>
public sealed class PasteStore : IPasteStore
{
    /// <summary>Stable 12-char hash of pasted text — the synthetic-URL identity (<c>paste://&lt;hash&gt;</c>)
    /// for favoriting pastes that have no real source URL. Same text =&gt; same favorite entry.
    /// Lives here (next to the store) so the identity and its persistence stay together; moved out of
    /// the WPF ViewModel so a web backend can compute the same id.</summary>
    public static string Hash(string text)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text ?? ""));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }

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
