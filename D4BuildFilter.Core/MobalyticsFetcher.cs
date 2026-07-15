using System.Text.Json;
using System.Text.RegularExpressions;

namespace D4BuildFilter.Core;

/// <summary>
/// Fetches a Diablo 4 build from a Mobalytics build-guide page and resolves it to the same
/// <see cref="ResolvedBuild"/> shape as the maxroll fetcher. Unlike maxroll (numeric nids needing
/// a lookup), Mobalytics ships affix and unique NAMES directly, so this needs no external data.
///
/// Mobalytics server-renders the whole build into the page as a single
/// <c>window.__PRELOADED_STATE__ = { … };</c> script (confirmed: a plain browser-UA GET returns it,
/// no Cloudflare wall, no headless browser). We pull that JSON and walk it:
///   userGeneratedDocumentBySlug.data.data  →  buildVariants.values[]  →  genericBuilder.slots[]
/// Each slot's <c>gameEntity.modifiers.gearStats[].id</c> are kebab-case affix slugs
/// ("critical-strike-damage-multiplier", "ranks-to-war-cry"); unique items carry their display
/// name in <c>gameEntity.entity.title</c>. Schema mirrors d4lf's mobalytics importer.
/// </summary>
public static class MobalyticsFetcher
{
    private const string PreloadMarker = "window.__PRELOADED_STATE__";

    public static bool IsMobalyticsUrl(string url) =>
        url.Contains("mobalytics.gg/diablo-4/", StringComparison.OrdinalIgnoreCase)
        && !url.Contains("/profile", StringComparison.OrdinalIgnoreCase);   // user-profile builds aren't supported

    /// <summary>GET the build page. Mobalytics sits behind Cloudflare which JA3-blocks .NET's TLS,
    /// so <see cref="BrowserFetch"/> routes through the system curl.exe (with an HttpClient fallback).</summary>
    public static Task<string> FetchRawAsync(string url, CancellationToken ct = default) =>
        BrowserFetch.GetStringAsync(url, ct);

    /// <summary>Parse the build out of a fetched Mobalytics page (pure / offline).</summary>
    public static ResolvedBuild Parse(string html)
    {
        var json = ExtractPreloadedState(html);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var ugd = FindFirst(root, "userGeneratedDocumentBySlug")
            ?? throw new FormatException("Mobalytics page had no userGeneratedDocumentBySlug — page shape changed?");
        var docData = ugd.GetProperty("data");
        var build = docData.GetProperty("data");

        string name = build.TryGetProperty("name", out var n) ? n.GetString() ?? "Mobalytics Build" : "Mobalytics Build";
        string cls = TalismanSetDatabase.NormalizeClassName(ClassFromTags(docData));

        // variant id -> title, from childrenVariants anywhere in the document
        var variantTitles = new Dictionary<string, string>(StringComparer.Ordinal);
        if (FindFirst(root, "childrenVariants") is { } cv && cv.ValueKind == JsonValueKind.Array)
            foreach (var c in cv.EnumerateArray())
                if (c.TryGetProperty("id", out var cid) && c.TryGetProperty("title", out var ct) && cid.GetString() is { } id)
                    variantTitles[id] = ct.GetString() ?? "";

        var variants = new List<ResolvedVariant>();
        var bv = FindFirst(build, "buildVariants");
        if (bv is { } bvEl && bvEl.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array)
        {
            int idx = 0;
            foreach (var v in values.EnumerateArray())
            {
                idx++;
                string vid = v.TryGetProperty("id", out var vidEl) ? vidEl.GetString() ?? "" : "";
                string vName = variantTitles.TryGetValue(vid, out var t) && !string.IsNullOrWhiteSpace(t)
                    ? t : $"Variant {idx}";
                variants.Add(ParseVariant(v, vName));
            }
        }
        if (variants.Count == 0)
            throw new FormatException("Mobalytics build had no variants/slots.");

        return new ResolvedBuild(name, cls, variants);
    }

    private static ResolvedVariant ParseVariant(JsonElement variant, string name)
    {
        var affixes = new List<string>();
        var uniques = new List<string>();
        var resolvedSlots = new List<ResolvedSlot>();
        var talismanSets = new List<string>();
        var unknownTalismanSets = new List<string>();
        var seenUnique = new HashSet<string>(StringComparer.Ordinal);
        var seenSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenUnknownSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var gb = FindFirst(variant, "genericBuilder");
        if (gb is { } gbEl && gbEl.TryGetProperty("slots", out var slots) && slots.ValueKind == JsonValueKind.Array)
        {
            foreach (var slot in slots.EnumerateArray())
            {
                if (!slot.TryGetProperty("gameEntity", out var ge) || ge.ValueKind != JsonValueKind.Object) continue;
                string entityType = ge.TryGetProperty("type", out var et) ? et.GetString() ?? "" : "";
                if (entityType is not ("aspects" or "uniqueItems" or "charms")) continue;

                // v1.0.5: equipped charm slots (gameSlotSlug "season-*-charm-N") carry the charm's
                // ITEM name directly on gameEntity.title ("Phoba of the Crucible") — resolve it to
                // its SET via the catalog. Cataloged unique charms ("Endurant Faith") are governed
                // by unique-charm checkboxes; every other unmatched title is recorded as unsafe drift.
                if (entityType == "charms")
                {
                    string cn = ge.TryGetProperty("title", out var charmTitle)
                        ? charmTitle.GetString()?.Trim() ?? ""
                        : "";
                    if (cn.Length > 0 && TalismanSetDatabase.TryGetByItemName(cn, out var set))
                    {
                        if (seenSet.Add(set.Name)) talismanSets.Add(set.Name);
                    }
                    else if (cn.Length == 0 || !UniqueCharmDatabase.TryGetByName(cn, out _))
                    {
                        string unknown = cn.Length > 0 ? cn : "(unnamed charm)";
                        if (seenUnknownSet.Add(unknown)) unknownTalismanSets.Add(unknown);
                    }
                    continue;   // charm slots carry charmStats, not gearStats — nothing else to read
                }

                var entity = ge.TryGetProperty("entity", out var en) ? en : default;

                // unique gear → its display name. S14: mythics are just uniques now, handled
                // uniformly downstream (the per-item `mythic` flag in the source is ignored).
                if (entityType == "uniqueItems" && entity.ValueKind == JsonValueKind.Object
                    && entity.TryGetProperty("title", out var ti) && ti.GetString() is { } un && seenUnique.Add(un))
                    uniques.Add(un);

                // desired affixes for this slot (kebab-case slugs → spaced names; the mapper normalizes)
                string slotName = slot.TryGetProperty("gameSlotSlug", out var ss) ? ss.GetString() ?? "" : "";
                var slotAffixes = new List<string>();
                if (ge.TryGetProperty("modifiers", out var mods) && mods.ValueKind == JsonValueKind.Object
                    && mods.TryGetProperty("gearStats", out var gear) && gear.ValueKind == JsonValueKind.Array)
                {
                    foreach (var stat in gear.EnumerateArray())
                        if (stat.ValueKind == JsonValueKind.Object && stat.TryGetProperty("id", out var sid)
                            && sid.GetString() is { } slug && slug.Length > 0)
                        {
                            var a = SlugToName(slug);
                            affixes.Add(a);
                            slotAffixes.Add(a);
                        }
                }
                if (slotAffixes.Count > 0 && slotName.Length > 0)
                    resolvedSlots.Add(new ResolvedSlot(slotName, slotAffixes));
            }
        }
        return new ResolvedVariant(name, affixes, uniques, resolvedSlots,
            talismanSets.Count > 0 ? talismanSets : null,
            unknownTalismanSets.Count > 0 ? unknownTalismanSets : null);
    }

    private static string SlugToName(string slug) => slug.Replace('-', ' ').Trim();

    private static string ClassFromTags(JsonElement docData)
    {
        if (docData.TryGetProperty("tags", out var tags) && tags.TryGetProperty("data", out var arr)
            && arr.ValueKind == JsonValueKind.Array)
            foreach (var tag in arr.EnumerateArray())
                if (tag.TryGetProperty("groupSlug", out var gs) && gs.GetString() == "class"
                    && tag.TryGetProperty("name", out var nm))
                    return nm.GetString() ?? "Unknown";
        return "Unknown";
    }

    private static string ExtractPreloadedState(string html)
    {
        int i = html.IndexOf(PreloadMarker, StringComparison.Ordinal);
        if (i < 0) throw new FormatException($"no {PreloadMarker} in the Mobalytics page (not a build guide URL?)");
        int eq = html.IndexOf('=', i);
        int end = html.IndexOf("</script>", eq, StringComparison.Ordinal);
        if (eq < 0 || end < 0) throw new FormatException("malformed __PRELOADED_STATE__ script block");
        return html[(eq + 1)..end].Trim().TrimEnd(';').Trim();
    }

    /// <summary>Depth-first search for the first property named <paramref name="prop"/>.</summary>
    private static JsonElement? FindFirst(JsonElement el, string prop)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject())
                {
                    if (p.NameEquals(prop)) return p.Value;
                    if (FindFirst(p.Value, prop) is { } hit) return hit;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                    if (FindFirst(item, prop) is { } hit) return hit;
                break;
        }
        return null;
    }
}
