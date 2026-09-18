using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace D4BuildFilter.Core;

/// <summary>One ranked build on a community tier list.</summary>
/// <param name="Name">Display name (maxroll includes the class, e.g. "Whirlwind Barb";
/// d4builds / mobalytics are usually the skill only, e.g. "Whirlwind").</param>
/// <param name="ClassName">Character class, Title-cased (e.g. "Barbarian").</param>
/// <param name="Tier">Tier label — single letter ("S".."D") or word ("God", "Support") for the
/// non-letter Mobalytics tiers. <see cref="TierListFetcher.TierOrder"/> defines the display order.</param>
/// <param name="Url">Link to the build's page on the source site.</param>
public sealed record TierBuild(string Name, string ClassName, string Tier, string Url);

/// <summary>A community tier list scraped from one site.</summary>
public sealed record TierList(string Source, string SourceUrl, IReadOnlyList<TierBuild> Builds);

/// <summary>
/// Scrapes the live endgame tier lists from maxroll.gg, d4builds.gg, and mobalytics.gg so the app's
/// landing page can show the season's top builds, grouped by tier and split by source. All three
/// pages are server-rendered, so one <see cref="BrowserFetch"/> GET each is enough — no JS needed.
/// Parsing is intentionally tolerant; if a site changes shape the UI falls back to a plain link.
/// </summary>
/// <summary>Maxroll's published tier-list categories. Each one is a separate page with the same
/// Remix JSON shape, so <see cref="TierListFetcher.ParseMaxroll"/> handles all of them.</summary>
public enum MaxrollList { Endgame, Bossing, Leveling, Push, Speedfarm }

/// <summary>D4Builds' published tier-list categories. Tower can be empty mid-season.</summary>
public enum D4BuildsList { Endgame, Leveling, Tower }

/// <summary>Mobalytics' published tier-list categories. Leveling skips God/Support; Pushing exposes
/// D-tier in addition to the usual sections.</summary>
public enum MobalyticsList { Endgame, Leveling, Pushing }

public static class TierListFetcher
{
    // Per-source URL lookup so the UI can also drive the "view full list ↗" link per active tab.
    public static string MaxrollUrlFor(MaxrollList k) => k switch
    {
        MaxrollList.Endgame   => "https://maxroll.gg/d4/tierlists/endgame-tier-list",
        MaxrollList.Bossing   => "https://maxroll.gg/d4/tierlists/bossing-builds-tier-list",
        MaxrollList.Leveling  => "https://maxroll.gg/d4/tierlists/leveling-tier-list",
        MaxrollList.Push      => "https://maxroll.gg/d4/tierlists/push-tier-list",
        MaxrollList.Speedfarm => "https://maxroll.gg/d4/tierlists/speedfarming-tier-list",
        _ => throw new ArgumentOutOfRangeException(nameof(k)),
    };
    public static string D4BuildsUrlFor(D4BuildsList k) => k switch
    {
        D4BuildsList.Endgame  => "https://d4builds.gg/tierlist/",
        D4BuildsList.Leveling => "https://d4builds.gg/tierlist/leveling/",
        D4BuildsList.Tower    => "https://d4builds.gg/tierlist/tower/",
        _ => throw new ArgumentOutOfRangeException(nameof(k)),
    };
    public static string MobalyticsUrlFor(MobalyticsList k) => k switch
    {
        MobalyticsList.Endgame  => "https://mobalytics.gg/diablo-4/tier-list",
        MobalyticsList.Leveling => "https://mobalytics.gg/diablo-4/tier-list/leveling",
        MobalyticsList.Pushing  => "https://mobalytics.gg/diablo-4/tier-list/pushing",
        _ => throw new ArgumentOutOfRangeException(nameof(k)),
    };

    /// <summary>Default URLs (the Endgame list per source) — kept as constants for existing consumers.</summary>
    public const string MaxrollUrl = "https://maxroll.gg/d4/tierlists/endgame-tier-list";
    public const string D4BuildsUrl = "https://d4builds.gg/tierlist/";
    public const string MobalyticsUrl = "https://mobalytics.gg/diablo-4/tier-list";

    // ── maxroll: builds live in the Remix data blob as JSON objects
    //   {"name":"Whirlwind Barb","icon":"d4/barbarian","iconImageUrl":"…","link":"…","tier":"S"}
    private static readonly Regex MaxrollBuild = new(
        @"""name"":""(?<name>[^""]+)"",""icon"":""d4/(?<cls>[^""]+)""[^}]*?""link"":""(?<link>https://maxroll\.gg/d4/build-guides/[^""]+)"",""tier"":""(?<tier>[A-Z])""",
        RegexOptions.Compiled);

    // ── d4builds: server-rendered HTML. Categories are <div class="tier__list__category S">,
    //   each followed by <a class="tier__list__item" href="/builds/…"><img class="…__icon Sorcerer">Name</a>
    private static readonly Regex D4BuildsItem = new(
        @"<a class=""tier__list__item"" href=""(?<href>/builds/[^""]+)"">\s*<img class=""tier__list__item__icon (?<cls>[A-Za-z]+)""[^>]*>(?<name>[^<]+)</a>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // ── mobalytics: embedded JSON in `window.__PRELOADED_STATE__ = {...};` (a <script> global, not
    //   a <script type="application/json"> tag) has shape (path to it moves around inside the blob —
    //   it's buried under an Apollo GraphQL cache keyed by query hash, so we don't hardcode the path;
    //   see <see cref="FindArraysByPropertyName"/>):
    //   "tierLists":{"values":[{"id":"…","tierSections":[
    //     {"name":"God Tier","color":"tier-e","description":null,"ugDataItems":[
    //       {"id":"…","iconUrl":"…classes-icons/Sorcerer.png","linkUrl":"/diablo-4/builds/sorcerer-ball-lightning",
    //        "title":"Ball Lightning - Mekuna's Ballkuna","subTitle":"Mekuna",
    //        "tags":[{"groupSlug":"class","slug":"sorcerer"},{"groupSlug":"season","slug":"season-15"},…]},
    //       …
    //     ]},
    //     {"name":"S","color":"tier-s",…}, {"name":"A",…}, {"name":"B",…}, {"name":"C",…}, {"name":"Support",…}
    //   ]}]}
    // 2026-09-18 (S15 drift): Mobalytics added the per-item "tags" array. Its entries are themselves
    // JSON arrays/objects, so they contain "]" and "}" characters *inside* an ugDataItems element.
    // The old scraper found each section's item block with a non-nesting regex,
    // `"ugDataItems":\[(?<items>[^\]]*)\]` — greedy-but-"]"-excluding — which now stops at the FIRST
    // "]" it meets, i.e. the closing bracket of the first item's own "tags" array, silently truncating
    // every section to ~1 build (measured live: 73 endgame builds parsed as 4). Regex can't safely
    // balance nested brackets, and patching it to tolerate one level of nesting would just move the
    // same failure mode to the next field Mobalytics adds. So: parse the embedded blob as real JSON
    // (<see cref="ExtractPreloadedStateJson"/>, <see cref="ExtractMobaTierSections"/>) instead of
    // scraping it with a regex at all — a JSON parser cannot mis-balance brackets the way a regex can.
    //
    // Class attribution now prefers the same "tags" array (<see cref="ClassFromTags"/>) — an explicit
    // {"groupSlug":"class","slug":"warlock"} entry is unambiguous taxonomy data, not an incidental
    // asset path. It is NOT the only source, though: measured live, "tags" is present and populated on
    // the endgame list but comes back EMPTY (`"tags":[]`) for most leveling/pushing items, so
    // <see cref="ClassFromSlugOrIcon"/> (slug-prefix, then icon-filename) stays as the fallback for
    // those. That fallback is the fix for the ORIGINAL 2026-06-10 incident below and must keep working
    // on its own merits — it is not being anchored on again, just kept as a second signal.
    //
    // Loose twin of the old section regex: same anchors, ANY section name. Drift tripwire — the section
    // canary enumerates these and fails loud if the live page carries a section the whitelist
    // above would silently drop (a renamed "God Tier", a new "S+", …).
    private static readonly Regex MobaAnySection = new(
        @"""name"":""(?<name>[^""]{1,40})"",""color"":""tier-[a-z]"",.*?""ugDataItems"":\[",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>Every tier-section name on a Mobalytics tier-list page, WITHOUT the known-name
    /// whitelist <see cref="ParseMobalytics"/> applies. Canary fuel, not a parse path.</summary>
    public static IReadOnlyList<string> EnumerateMobaSectionNames(string html) =>
        MobaAnySection.Matches(html).Select(m => m.Groups["name"].Value).Distinct().ToList();

    /// <summary>Classes we can attribute a Mobalytics build to. Slugs lead with the class name
    /// ("paladin-blessed-hammer"); icon filenames are the fallback. A class missing here still
    /// LISTS (with the neutral chip color) — it just can't be class-filtered until added.</summary>
    private static readonly string[] KnownClasses =
    {
        "Barbarian", "Druid", "Necromancer", "Rogue",
        "Sorcerer", "Spiritborn", "Paladin", "Warlock",
    };

    /// <summary>Per-source list of tiers we surface, in display order. We show ALL meta tiers now
    /// (S→D) plus the Mobalytics-only top (God) and bottom (Support).</summary>
    private static readonly string[] LetterTiers = { "S", "A", "B", "C", "D" };
    private static readonly string[] MobaTiers   = { "God", "S", "A", "B", "C", "D", "Support" };

    /// <summary>Cross-source ordering used by <see cref="Order"/>. Lower index = displayed first.</summary>
    private static readonly Dictionary<string, int> TierOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["God"] = 0, ["S"] = 1, ["A"] = 2, ["B"] = 3, ["C"] = 4, ["D"] = 5, ["Support"] = 6,
    };

    public static Task<TierList> FetchMaxrollAsync(CancellationToken ct = default) =>
        FetchMaxrollAsync(MaxrollList.Endgame, ct);
    public static async Task<TierList> FetchMaxrollAsync(MaxrollList kind, CancellationToken ct = default) =>
        WithUrl(ParseMaxroll(await BrowserFetch.GetStringAsync(MaxrollUrlFor(kind), ct)), MaxrollUrlFor(kind));

    public static Task<TierList> FetchD4BuildsAsync(CancellationToken ct = default) =>
        FetchD4BuildsAsync(D4BuildsList.Endgame, ct);
    public static async Task<TierList> FetchD4BuildsAsync(D4BuildsList kind, CancellationToken ct = default) =>
        WithUrl(ParseD4Builds(await BrowserFetch.GetStringAsync(D4BuildsUrlFor(kind), ct)), D4BuildsUrlFor(kind));

    public static Task<TierList> FetchMobalyticsAsync(CancellationToken ct = default) =>
        FetchMobalyticsAsync(MobalyticsList.Endgame, ct);
    public static async Task<TierList> FetchMobalyticsAsync(MobalyticsList kind, CancellationToken ct = default) =>
        WithUrl(ParseMobalytics(await BrowserFetch.GetStringAsync(MobalyticsUrlFor(kind), ct)), MobalyticsUrlFor(kind));

    /// <summary>Override the SourceUrl on a parsed list so the "view full list ↗" link points at
    /// the specific tab's URL (not just the source's default endgame page).</summary>
    private static TierList WithUrl(TierList tl, string sourceUrl) =>
        new(tl.Source, sourceUrl, tl.Builds);

    public static TierList ParseMaxroll(string html)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var builds = new List<TierBuild>();
        foreach (Match m in MaxrollBuild.Matches(html))
        {
            var tier = m.Groups["tier"].Value;
            if (Array.IndexOf(LetterTiers, tier) < 0) continue;   // skip X / unranked
            var name = Decode(m.Groups["name"].Value);
            if (!seen.Add($"{name}|{tier}")) continue;            // dedupe SSR + hydration copies
            builds.Add(new TierBuild(name, TitleCase(m.Groups["cls"].Value), tier, m.Groups["link"].Value));
        }
        return new TierList("Maxroll", MaxrollUrl, Order(builds));
    }

    public static TierList ParseD4Builds(string html)
    {
        var builds = new List<TierBuild>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Split on the category marker; each chunk[i>0] starts with its tier letter.
        var chunks = html.Split("tier__list__category ");
        for (int i = 1; i < chunks.Length; i++)
        {
            var chunk = chunks[i];
            var tier = chunk.Length > 0 ? chunk[0].ToString().ToUpperInvariant() : "";
            if (Array.IndexOf(LetterTiers, tier) < 0) continue;
            foreach (Match m in D4BuildsItem.Matches(chunk))
            {
                var name = Decode(m.Groups["name"].Value).Trim();
                if (name.Length == 0 || !seen.Add($"{name}|{tier}")) continue;
                var url = "https://d4builds.gg" + m.Groups["href"].Value;
                builds.Add(new TierBuild(name, TitleCase(m.Groups["cls"].Value), tier, url));
            }
        }
        return new TierList("D4Builds", D4BuildsUrl, Order(builds));
    }

    private const string MobaBuildLinkPrefix = "/diablo-4/builds/";

    public static TierList ParseMobalytics(string html)
    {
        var builds = new List<TierBuild>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (tier, items) in ExtractMobaTierSections(html))
        {
            if (Array.IndexOf(MobaTiers, tier) < 0) continue;
            foreach (var item in items)
            {
                if (!item.TryGetProperty("linkUrl", out var linkProp) || linkProp.ValueKind != JsonValueKind.String)
                    continue;
                var link = linkProp.GetString() ?? "";
                var idx = link.IndexOf(MobaBuildLinkPrefix, StringComparison.Ordinal);
                if (idx < 0) continue;
                var slug = link[(idx + MobaBuildLinkPrefix.Length)..];

                var title = item.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String
                    ? Decode(titleProp.GetString() ?? "").Trim()
                    : "";
                if (title.Length == 0 || !seen.Add($"{title}|{tier}")) continue;

                var icon = item.TryGetProperty("iconUrl", out var iconProp) && iconProp.ValueKind == JsonValueKind.String
                    ? iconProp.GetString() ?? ""
                    : "";
                var cls = ClassFromTags(item) ?? ClassFromSlugOrIcon(slug, icon);
                builds.Add(new TierBuild(title, cls, tier, "https://mobalytics.gg" + MobaBuildLinkPrefix + slug));
            }
        }
        return new TierList("Mobalytics", MobalyticsUrl, Order(builds));
    }

    /// <summary>Pulls every Mobalytics tier section (name + its raw ugDataItems JSON elements) out of
    /// a tier-list page's embedded <c>window.__PRELOADED_STATE__</c> blob. Returns nothing (not a
    /// throw) if the marker is missing or the blob doesn't parse as JSON — callers see an empty list,
    /// same "degrade to a plain link" contract the regex-era parser had.</summary>
    private static List<(string Tier, List<JsonElement> Items)> ExtractMobaTierSections(string html)
    {
        var result = new List<(string, List<JsonElement>)>();
        var json = ExtractPreloadedStateJson(html);
        if (json is null) return result;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return result; }

        using (doc)
        {
            // The tierSections array is nested several levels deep inside an Apollo GraphQL query
            // cache keyed by a query hash/index that isn't stable across pages or deploys, so we
            // search for it by property name instead of hardcoding a path.
            foreach (var sectionsArray in FindArraysByPropertyName(doc.RootElement, "tierSections"))
            {
                foreach (var section in sectionsArray.EnumerateArray())
                {
                    if (!section.TryGetProperty("name", out var nameProp) || nameProp.ValueKind != JsonValueKind.String)
                        continue;
                    var rawTier = nameProp.GetString() ?? "";
                    var tier = rawTier == "God Tier" ? "God" : rawTier;

                    var items = new List<JsonElement>();
                    if (section.TryGetProperty("ugDataItems", out var itemsProp) && itemsProp.ValueKind == JsonValueKind.Array)
                        foreach (var item in itemsProp.EnumerateArray())
                            items.Add(item.Clone()); // detach from `doc` — it's disposed before we return

                    result.Add((tier, items));
                }
            }
        }
        return result;
    }

    /// <summary>Finds every JSON array under a property named <paramref name="propertyName"/>,
    /// anywhere in the tree. Used instead of a fixed path because Mobalytics' preloaded-state blob
    /// nests <c>tierSections</c> under a GraphQL query-cache key that shifts between pages/deploys.</summary>
    private static IEnumerable<JsonElement> FindArraysByPropertyName(JsonElement element, string propertyName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.NameEquals(propertyName) && prop.Value.ValueKind == JsonValueKind.Array)
                        yield return prop.Value;
                    foreach (var found in FindArraysByPropertyName(prop.Value, propertyName))
                        yield return found;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    foreach (var found in FindArraysByPropertyName(item, propertyName))
                        yield return found;
                break;
        }
    }

    /// <summary>Extracts the JSON object assigned to <c>window.__PRELOADED_STATE__ = {...};</c> using
    /// string-aware brace counting rather than a lazy regex, so a "}" or ";" that happens to appear
    /// inside a JSON string value (a build title, a description) can't truncate the match early.</summary>
    private static string? ExtractPreloadedStateJson(string html)
    {
        const string marker = "__PRELOADED_STATE__=";
        var markerAt = html.IndexOf(marker, StringComparison.Ordinal);
        int start;
        if (markerAt >= 0)
        {
            var pos = markerAt + marker.Length;
            while (pos < html.Length && html[pos] != '{') pos++;
            if (pos >= html.Length) return null;
            start = pos;
        }
        else
        {
            // No live-page wrapper found. On a real fetch this means the marker itself vanished —
            // genuine drift, and the brace-scan below will find no balanced object (or the wrong
            // one) and ExtractMobaTierSections degrades to empty, same as it always has. This branch
            // also lets a test fixture hand in bare JSON (no <script> wrapper) directly.
            start = html.IndexOf('{');
            if (start < 0) return null;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var pos = start; pos < html.Length; pos++)
        {
            var c = html[pos];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') { inString = true; continue; }
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return html[start..(pos + 1)];
            }
        }
        return null; // unbalanced — page truncated or shape changed; ExtractMobaTierSections degrades gracefully
    }

    /// <summary>Class from a Mobalytics build item's explicit <c>"tags":[{"groupSlug":"class",
    /// "slug":"warlock"},…]</c> entry — structural taxonomy data, not an incidental asset path.
    /// Returns null (not "") when absent so <see cref="ParseMobalytics"/> can fall back to
    /// <see cref="ClassFromSlugOrIcon"/>; measured live, "tags" is reliably populated on the endgame
    /// list but comes back as <c>"tags":[]</c> for most leveling/pushing items.</summary>
    private static string? ClassFromTags(JsonElement item)
    {
        if (!item.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var tag in tags.EnumerateArray())
        {
            if (tag.ValueKind != JsonValueKind.Object) continue;
            if (!tag.TryGetProperty("groupSlug", out var gs) || gs.ValueKind != JsonValueKind.String
                || gs.GetString() != "class")
                continue;
            if (!tag.TryGetProperty("slug", out var slugProp) || slugProp.ValueKind != JsonValueKind.String)
                continue;
            var slug = slugProp.GetString();
            if (string.IsNullOrEmpty(slug)) continue;
            foreach (var c in KnownClasses)
                if (slug.Equals(c, StringComparison.OrdinalIgnoreCase)) return c;
            return TitleCase(slug); // a class tag we don't recognize yet — still attribute it, don't drop it
        }
        return null;
    }

    /// <summary>Sort builds by tier (God → S → A → B → C → D → Support), keeping source order
    /// within each tier.</summary>
    private static IReadOnlyList<TierBuild> Order(IEnumerable<TierBuild> builds) =>
        builds.OrderBy(b => TierOrder.TryGetValue(b.Tier, out var v) ? v : 99).ToList();

    private static string TitleCase(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

    /// <summary>Resolve a Mobalytics build's class: slug prefix first ("paladin-blessed-hammer"),
    /// then the icon filename ("…/Warlock-icon.png?v1" → Warlock). Unknown → "" so the build still
    /// lists with the neutral chip color instead of being dropped.</summary>
    private static string ClassFromSlugOrIcon(string slug, string icon)
    {
        var first = slug.Split('-', 2)[0];
        foreach (var c in KnownClasses)
            if (first.Equals(c, StringComparison.OrdinalIgnoreCase)) return c;

        var file = icon.Replace("\\u002F", "/");
        var query = file.IndexOf('?');
        if (query >= 0) file = file[..query];
        var slash = file.LastIndexOf('/');
        if (slash >= 0) file = file[(slash + 1)..];
        var dot = file.LastIndexOf('.');
        if (dot >= 0) file = file[..dot];
        if (file.EndsWith("-icon", StringComparison.OrdinalIgnoreCase)) file = file[..^5];
        foreach (var c in KnownClasses)
            if (file.Equals(c, StringComparison.OrdinalIgnoreCase)) return c;
        return "";
    }

    private static string Decode(string s) => WebUtility.HtmlDecode(s.Replace("\\u0026", "&"));
}
