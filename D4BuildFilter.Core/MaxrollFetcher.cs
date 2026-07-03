using System.Text.Json;
using System.Text.RegularExpressions;

namespace D4BuildFilter.Core;

/// <summary>One gear slot's desired affixes (e.g. "Boots" -> [...]). Enables per-slot rules that
/// avoid the combined-pool false positives. Optional — paste import / older data have no slots.</summary>
public sealed record ResolvedSlot(string Slot, IReadOnlyList<string> Affixes);

/// <summary>One build variant (a maxroll "profile") reduced to its desirable affix wishlist
/// plus the gear uniques it equips (display names). <paramref name="Slots"/> keeps the per-slot
/// breakdown when the source provides it (null = flat list only, e.g. pasted builds).
/// <paramref name="TalismanSets"/> = the charm SETS this variant equips (SetItemBonusDatabase
/// display names, e.g. "Talisman: Barbarian Set 05"), extracted from set-charm item ids — feeds
/// the build-scoped Charms &amp; Seals rules. Null when the source carries no talisman data.</summary>
public sealed record ResolvedVariant(string Name, IReadOnlyList<string> Affixes, IReadOnlyList<string> Uniques,
    IReadOnlyList<ResolvedSlot>? Slots = null, IReadOnlyList<string>? TalismanSets = null);

/// <summary>A whole maxroll build resolved to affix names, ready for the AffixMapper/encoder.
/// Serializes (camelCase) to the same shape the Tester reads from build_resolved.json.
/// <paramref name="UnknownAffixNids"/> / <paramref name="UnknownUniqueIds"/> are the ids the LOCAL
/// GAME DATA couldn't resolve (distinct, across all kept variants). Nonzero right after a season
/// patch means the data files predate the build — "Update game data" is the fix. Null for sources
/// that carry display names directly (Mobalytics / d4builds / paste), where no id resolution happens.</summary>
public sealed record ResolvedBuild(string Build, string Class, IReadOnlyList<ResolvedVariant> Variants,
    IReadOnlyList<string>? UnknownAffixNids = null, IReadOnlyList<string>? UnknownUniqueIds = null)
{
    /// <summary>How many distinct ids this build references that the local game data doesn't know.</summary>
    public int UnknownDataCount => (UnknownAffixNids?.Count ?? 0) + (UnknownUniqueIds?.Count ?? 0);
}

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

    // A build-GUIDE page embeds its planner as a `maxroll/planner-page` Gutenberg block whose
    // attributes carry the canonical planner URL: {"link":"https://maxroll.gg/d4/planner/<id>"}.
    // The link often has a "#<profileIndex>" suffix (e.g. .../w62gqj0v#4) — most guides do, Ball
    // Lightning happened not to — so we capture just the id and tolerate any trailing #/query.
    private static readonly Regex GuidePlannerLink =
        new(@"""link"":""(https://maxroll\.gg/d4/planner/[A-Za-z0-9]+)[^""]*""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>True for a maxroll build-GUIDE article URL (the wrapper a casual user lands on),
    /// as opposed to a /planner/ URL. Guide pages embed the real planner — see
    /// <see cref="ResolvePlannerUrlAsync"/>.</summary>
    public static bool IsBuildGuideUrl(string url) =>
        url.Contains("maxroll.gg/d4/build-guides/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Dig the real planner URL out of a build-guide page. The guide is just an article
    /// wrapping one embedded planner (its variants are that planner's profiles), so we pull the
    /// planner link out of the page and hand it back — letting a "guide" link resolve to the
    /// actual build the compiler needs.</summary>
    public static async Task<string> ResolvePlannerUrlAsync(string guideUrl, CancellationToken ct = default)
    {
        var html = await BrowserFetch.GetStringAsync(guideUrl, ct);
        return ParseGuidePlannerLink(html) ?? throw new FormatException(
            "Couldn't find a planner on that Maxroll guide page. Open the guide, click the planner, and paste its /d4/planner/ URL instead.");
    }

    /// <summary>Pull the embedded planner URL out of a build-guide page's HTML (the
    /// `maxroll/planner-page` block's link). Pure/offline — returns null if the page has none.</summary>
    public static string? ParseGuidePlannerLink(string html)
    {
        var m = GuidePlannerLink.Match(html);
        return m.Success ? m.Groups[1].Value : null;
    }

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
        // A casual user pastes (or clicks) a build-GUIDE link; resolve it to the embedded planner first.
        if (IsBuildGuideUrl(urlOrId))
            urlOrId = await ResolvePlannerUrlAsync(urlOrId, ct);
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
        // Ids the local game data can't name — silently dropping these is exactly how a new
        // season's builds compile incomplete with no signal, so count them for the UI.
        var unknownNids = new SortedSet<long>();
        var unknownUniqueIds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
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
            var resolvedSlots = new List<ResolvedSlot>();
            var seenUnique = new HashSet<string>(StringComparer.Ordinal);
            var talismanSets = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            if (profile.TryGetProperty("items", out var slotMap) && slotMap.ValueKind == JsonValueKind.Object)
            {
                foreach (var slot in slotMap.EnumerateObject())
                {
                    var itemKey = slot.Value.ValueKind == JsonValueKind.Number
                        ? slot.Value.GetRawText()        // top-level items dict is keyed by the number as a string
                        : slot.Value.GetString();
                    if (itemKey is null || !items.TryGetProperty(itemKey, out var item)) continue;

                    string itemId = item.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";

                    // Gear unique? Resolve its display name. (Charms don't resolve here — they're a
                    // separate type.) S14: mythics are just uniques now, handled uniformly downstream.
                    if (itemId.Length > 0)
                    {
                        // Set-charms name their set in the id ("Talisman_Charm_Set_Barb_05_03") —
                        // collect the SETS (S14 display names, e.g. "Bul-Kathos' Pride") so the
                        // Charms & Seals rules can scope to the build.
                        if (TalismanSetCharm.Match(itemId) is { Success: true } tm &&
                            TalismanSetDatabase.TryGetByPlannerToken(tm.Groups["cls"].Value,
                                int.Parse(tm.Groups["num"].Value), out var tset))
                            talismanSets.Add(tset.Name);
                        else if (uniques.Resolve(itemId) is { } un) { if (seenUnique.Add(un)) uniqueNames.Add(un); }
                        else if (LooksLikeGearUnique(itemId)) unknownUniqueIds.Add(itemId);
                    }

                    if (!item.TryGetProperty("explicits", out var ex) || ex.ValueKind != JsonValueKind.Array) continue;
                    var slotAffixes = new List<string>();
                    foreach (var e in ex.EnumerateArray())
                    {
                        if (!e.TryGetProperty("nid", out var nidEl)) continue;
                        var nid = nidEl.GetInt64();
                        var name = names.Resolve(nid);
                        if (name is not null) { affixes.Add(name); slotAffixes.Add(name); }   // mapper dedupes by coarse id
                        else unknownNids.Add(nid);
                    }
                    // Per-slot breakdown: derive a slot label from the item id (e.g. "1HMace_..." -> "1HMace",
                    // "S05_BSK_Helm_..." -> "Helm") — the first id segment that resolves to an item type.
                    if (slotAffixes.Count > 0 && SlotLabel(itemId) is { } label)
                        resolvedSlots.Add(new ResolvedSlot(label, slotAffixes));
                }
            }
            variants.Add(new ResolvedVariant(vName, affixes, uniqueNames, resolvedSlots,
                talismanSets.Count > 0 ? talismanSets.ToList() : null));
        }
        return new ResolvedBuild(build, cls, variants,
            unknownNids.Select(n => n.ToString()).ToList(), unknownUniqueIds.ToList());
    }

    /// <summary>A maxroll item id that SHOULD have resolved via Uniques.enUS.json: gear uniques
    /// carry "_Unique_" ("Chest_Unique_Barb_100"). Charm/seal/talisman ids can carry the token too
    /// but are intentionally absent from the lookup (their own filter category, not a data gap).</summary>
    // Set-charm ids carry their set: "Talisman_Charm_Set_Barb_05_03" → class token "Barb", set 05.
    private static readonly Regex TalismanSetCharm = new(
        @"^Talisman_Charm_Set_(?<cls>[A-Za-z]+)_(?<num>\d+)_", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static bool LooksLikeGearUnique(string itemId) =>
        itemId.Contains("_Unique_", StringComparison.OrdinalIgnoreCase)
        && !itemId.Contains("Charm", StringComparison.OrdinalIgnoreCase)
        && !itemId.Contains("Seal", StringComparison.OrdinalIgnoreCase)
        && !itemId.Contains("Talisman", StringComparison.OrdinalIgnoreCase);

    private static bool IsDisabled(string name) =>
        name.Contains("disabled", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("do not use", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("deprecated", StringComparison.OrdinalIgnoreCase);

    /// <summary>Slot label for a maxroll item id = the first underscore-segment that resolves to an
    /// item type ("1HMace_Legendary_001" -> "1HMace", "S05_BSK_Helm_Unique_001" -> "Helm",
    /// "Chest_Unique_Barb_100" -> "Chest"). null if none resolves (e.g. Talisman → no per-slot rule).</summary>
    private static string? SlotLabel(string itemId)
    {
        foreach (var seg in itemId.Split('_'))
            if (ItemTypeDatabase.ResolveSlot(seg) is not null) return seg;
        return null;
    }
}
