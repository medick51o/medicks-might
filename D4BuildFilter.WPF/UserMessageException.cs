namespace D4BuildFilter.WPF;

/// <summary>Marker exception for deliberately authored, player-facing product copy (e.g.
/// "{build} returned no usable variants") that must reach the result surface verbatim — never
/// wrapped in generic friendly copy. Every other exception type, INCLUDING Core's own
/// InvalidOperationException (fetchers throw it for raw scraper/API errors like "d4builds
/// Firestore error: ..."), is dev noise and must never reach the player raw.</summary>
public sealed class UserMessageException : Exception
{
    public UserMessageException(string message) : base(message) { }
}
