using System.Text.Json;
using System.Text.RegularExpressions;

namespace D4BuildFilter.Core;

/// <summary>One build variant (a maxroll "profile") reduced to its desirable affix wishlist
/// plus the gear uniques it equips (display names; mythics/charms excluded).</summary>
public sealed record ResolvedVariant(string Name, IReadOnlyList<string> Affixes, IReadOnlyList<string> Uniques);

/// <summary>A whole maxroll build resolved to affix names, ready for the AffixMapper/encoder.
/// Serializes (camelCase) to the same shape the Tester reads from build_resolved.json.</summary>
public sealed record ResolvedBuild(string Build, string Class, IReadOnlyList<ResolvedVariant> Variants);

/// <summary>
/// Fetches a Diablo 4 build from maxroll's public planner endpoint and resolves each item's
/// affixes to names. The endpoint is a plain GET (browser UA, no auth, no Cloudflare wall):
///   <c>https://planners.maxroll.gg/profiles/d4/&lt;plannerId&gt;</c>
/// The top JSON carries <c>name</c>/<c>class</c> and a JSON-ENCODED-STRING <c>data</c> that
/// holds <c>profiles[]</c> + an <c>items</c> dict. Each profile's <c>items</c> maps a slot
/// index to a key into that dict; each item lists <c>explicits</c> referencing affixes by nid.
/// </summary>
public static class MaxrollFetcher
{
    private static readonly Regex IdInUrl =
        new(@"d4/planner/([A-Za-z0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Accepts a full maxroll planner URL or a bare planner id; returns the id.</summary>
    public static string ExtractPlannerId(string urlOrId)
    {
        var m = IdInUrl.Match(urlOrId);
        if (m.Success) return m.Groups[1].Value;

        // Bare id, possibly with a trailing "#profileIndex" / query — trim it.
        var bare = urlOrId.Trim();
        var cut = bare.IndexOfAny(new[] { '#', '?', '/' });
        return cut >= 0 ? bare[..cut] : bare;
    }

    public static string PlannerUrl(string plannerId) =>
        $"https://planners.maxroll.gg/profiles/d4/{plannerId}";

    /// <summary>GET the raw planner JSON. A browser User-Agent is required to dodge the bot wall.</summary>
    public static async Task<string> FetchRawAsync(string urlOrId, HttpClient? http = null, CancellationToken ct = default)
    {
        var id = ExtractPlannerId(urlOrId);
        var owned = http is null;
        http ??= new HttpClient();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, PlannerUrl(id));
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) D4BuildFilter/0.1");
            req.Headers.Accept.ParseAdd("application/json");
            using var resp = await http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }
        finally { if (owned) http.Dispose(); }
    }

    /// <summary>Parse a raw planner response and resolve every active profile's affixes (and
    /// equipped gear uniques) to names. Pure / offline — feed it <see cref="FetchRawAsync"/>
    /// output or a saved sample response.</summary>
    public static ResolvedBuild Parse(string rawJson, NameLookup names, UniqueLookup uniques)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        string build = root.TryGetProperty("name", out var n) ? n.GetString() ?? "Unnamed Build" : "Unnamed Build";
        string cls = root.TryGetProperty("class", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString()! : "Unknown";

        // `data` is itself a JSON-encoded string — parse it a second time.
        if (!root.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.String)
            throw new FormatException("planner JSON has no string `data` field — endpoint shape changed?");
        using var inner = JsonDocument.Parse(dataEl.GetString()!);
        var data = inner.RootElement;

        var items = data.GetProperty("items");   // dict: itemKey(string) -> { explicits: [ { nid, values } ] }

        var variants = new List<ResolvedVariant>();
        foreach (var profile in data.GetProperty("profiles").EnumerateArray())
        {
            string vName = profile.TryGetProperty("name", out var pn)
                ? pn.GetString() ?? $"Variant {variants.Count + 1}"
                : $"Variant {variants.Count + 1}";

            // Skip variants the author marked dead. maxroll has no reliable "disabled" flag
            // (its `hidden` just means UI-collapsed), so match the convention authors use in
            // the name. This mirrors the hand-built build_resolved.json (6 of 7 profiles).
            if (IsDisabled(vName)) continue;

            var affixes = new List<string>();
            var uniqueNames = new List<string>();
            var seenUnique = new HashSet<string>(StringComparer.Ordinal);
            if (profile.TryGetProperty("items", out var slotMap) && slotMap.ValueKind == JsonValueKind.Object)
            {
                foreach (var slot in slotMap.EnumerateObject())
                {
                    var itemKey = slot.Value.ValueKind == JsonValueKind.Number
                        ? slot.Value.GetRawText()        // top-level items dict is keyed by the number as a string
                        : slot.Value.GetString();
                    if (itemKey is null || !items.TryGetProperty(itemKey, out var item)) continue;

                    // Gear unique? Resolve its display name (mythics/charms don't resolve, so
                    // they're skipped — mythics stay untouched, charms are a separate type).
                    if (item.TryGetProperty("id", out var idEl) && idEl.GetString() is { } itemId
                        && !itemId.Contains("Mythic", StringComparison.OrdinalIgnoreCase)
                        && uniques.Resolve(itemId) is { } un && seenUnique.Add(un))
                        uniqueNames.Add(un);

                    if (!item.TryGetProperty("explicits", out var ex) || ex.ValueKind != JsonValueKind.Array) continue;
                    foreach (var e in ex.EnumerateArray())
                    {
                        if (!e.TryGetProperty("nid", out var nidEl)) continue;
                        var name = names.Resolve(nidEl.GetInt64());
                        if (name is not null) affixes.Add(name);   // keep duplicates: the mapper dedupes by coarse id
                    }
                }
            }
            variants.Add(new ResolvedVariant(vName, affixes, uniqueNames));
        }
        return new ResolvedBuild(build, cls, variants);
    }

    private static bool IsDisabled(string name) =>
        name.Contains("disabled", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("do not use", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("deprecated", StringComparison.OrdinalIgnoreCase);
}
