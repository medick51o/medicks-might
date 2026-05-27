using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace D4BuildFilter.Core;

/// <summary>
/// Fetches a Diablo 4 build from d4builds.gg (where Rob2628's builds live) into the same
/// <see cref="ResolvedBuild"/> shape as the other fetchers.
///
/// d4builds is a Gatsby SPA backed by Cloud Firestore. There's no server-rendered build in the
/// page — BUT the build doc is world-readable by id through the Firestore REST API (App Check is
/// only enforced for list, not get-by-id):
///   GET firestore.googleapis.com/v1/projects/d4builds-a3254/databases/(default)/documents/builds/&lt;id&gt;?key=&lt;webKey&gt;
/// (apiKey/projectId are the site's public web config, lifted from its JS bundle.)
///
/// The build id is the UUID in a `/builds/&lt;uuid&gt;` URL, or — for a named/slug build like
/// `/builds/ancients-barbarian-endgame` — the `seoId` in that page's Gatsby
/// `page-data/builds/&lt;slug&gt;/page-data.json`.
///
/// In the doc: `variants[]` each carry `newStats` (per-slot affix-name arrays), `greaterAffixes`
/// (parallel 1/null GA flags), and `gear` (per-slot aspect/unique NAME). Uniques are gear names
/// that aren't aspects.
/// </summary>
public static class D4BuildsFetcher
{
    private const string ApiKey = "AIzaSyDiFjyn-CH9a80pzfcwMd_AH-zSstNmjDc";   // d4builds public web config
    private const string Project = "d4builds-a3254";

    private static readonly Regex BuildSeg = new(@"d4builds\.gg/builds/([^/?#]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Uuid = new(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsD4BuildsUrl(string url) =>
        url.Contains("d4builds.gg/builds/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Fetch the build's Firestore document JSON (resolving a slug to its id first).</summary>
    public static async Task<string> FetchRawAsync(string url, CancellationToken ct = default)
    {
        var id = await ResolveBuildIdAsync(url, ct);
        var docUrl = $"https://firestore.googleapis.com/v1/projects/{Project}/databases/(default)/documents/builds/{id}?key={ApiKey}";
        return await BrowserFetch.GetStringAsync(docUrl, ct);
    }

    /// <summary>URL build segment is either the UUID id, or a slug we resolve via the page's seoId.</summary>
    public static async Task<string> ResolveBuildIdAsync(string url, CancellationToken ct = default)
    {
        var m = BuildSeg.Match(url);
        if (!m.Success) throw new FormatException("not a d4builds build URL (…/builds/<id>)");
        var seg = m.Groups[1].Value;
        if (Uuid.IsMatch(seg)) return seg;

        var pageData = await BrowserFetch.GetStringAsync(
            $"https://d4builds.gg/page-data/builds/{seg}/page-data.json", ct);
        using var doc = JsonDocument.Parse(pageData);
        if (doc.RootElement.TryGetProperty("result", out var r)
            && r.TryGetProperty("pageContext", out var pc)
            && pc.TryGetProperty("seoId", out var seo) && seo.GetString() is { } id)
            return id;
        throw new FormatException($"could not resolve d4builds slug '{seg}' to a build id (no seoId)");
    }

    /// <summary>Parse the Firestore build document into a build (pure / offline).</summary>
    public static ResolvedBuild Parse(string firestoreJson)
    {
        using var docJson = JsonDocument.Parse(firestoreJson);
        var root = docJson.RootElement;
        if (root.TryGetProperty("error", out var err))
            throw new InvalidOperationException($"d4builds Firestore error: {err.GetProperty("status").GetString()}");
        if (!root.TryGetProperty("fields", out var fieldsEl))
            throw new FormatException("d4builds doc has no fields");

        var doc = DecodeFields(fieldsEl)!.AsObject();
        string name = (string?)doc["name"] ?? "d4builds Build";
        string cls = (string?)doc["class"] ?? "Unknown";

        var variants = new List<ResolvedVariant>();
        if (doc["variants"] is JsonArray vs && vs.Count > 0)
        {
            int i = 0;
            foreach (var v in vs)
            {
                i++;
                if (v is JsonObject vo) variants.Add(ParseVariant(vo, i));
            }
        }
        else
        {
            variants.Add(ParseVariant(doc, 1));   // single-variant build: use the top-level fields
        }

        return new ResolvedBuild(name, cls, variants);
    }

    private static ResolvedVariant ParseVariant(JsonObject v, int idx)
    {
        string vName = (string?)v["variantName"] ?? "";
        if (string.IsNullOrWhiteSpace(vName)) vName = $"Variant {idx}";

        var affixes = new List<string>();
        if (v["newStats"] is JsonObject ns)
            foreach (var slot in ns)
                if (slot.Value is JsonArray arr)
                    foreach (var a in arr)
                        if (a is JsonValue jv && jv.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s))
                            affixes.Add(s);

        var uniques = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (v["gear"] is JsonObject gear)
            foreach (var slot in gear)
                if (slot.Value is JsonValue gv && gv.TryGetValue(out string? itemName)
                    && !string.IsNullOrWhiteSpace(itemName)
                    && !itemName.Contains("aspect", StringComparison.OrdinalIgnoreCase)   // aspects aren't uniques
                    && seen.Add(itemName))
                    uniques.Add(itemName);

        return new ResolvedVariant(vName, affixes, uniques);
    }

    // ── Firestore typed-value JSON → plain JsonNode ──
    private static JsonNode? DecodeFields(JsonElement fields)
    {
        var obj = new JsonObject();
        foreach (var f in fields.EnumerateObject())
            obj[f.Name] = DecodeValue(f.Value);
        return obj;
    }

    private static JsonNode? DecodeValue(JsonElement v)
    {
        if (v.TryGetProperty("stringValue", out var s)) return JsonValue.Create(s.GetString());
        if (v.TryGetProperty("integerValue", out var i))
            return JsonValue.Create(long.TryParse(i.GetString(), out var l) ? l : 0L);
        if (v.TryGetProperty("doubleValue", out var d)) return JsonValue.Create(d.GetDouble());
        if (v.TryGetProperty("booleanValue", out var b)) return JsonValue.Create(b.GetBoolean());
        if (v.TryGetProperty("nullValue", out _)) return null;
        if (v.TryGetProperty("timestampValue", out var t)) return JsonValue.Create(t.GetString());
        if (v.TryGetProperty("arrayValue", out var a))
        {
            var arr = new JsonArray();
            if (a.TryGetProperty("values", out var vals))
                foreach (var x in vals.EnumerateArray()) arr.Add(DecodeValue(x));
            return arr;
        }
        if (v.TryGetProperty("mapValue", out var mv))
            return mv.TryGetProperty("fields", out var mf) ? DecodeFields(mf) : new JsonObject();
        return null;
    }
}
