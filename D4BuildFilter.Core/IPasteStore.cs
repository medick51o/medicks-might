namespace D4BuildFilter.Core;

/// <summary>Sidecar store for the raw text of favorited "community paste" builds, keyed by
/// <see cref="PasteStore.Hash"/>. The desktop app uses the file-backed <see cref="PasteStore"/>;
/// a web backend can swap in a blob/DB implementation. (Hash() is a static identity helper on
/// the concrete type and intentionally not part of this instance interface.)</summary>
public interface IPasteStore
{
    void Save(string hash, string text);
    string? Load(string hash);
    void Remove(string hash);
}
