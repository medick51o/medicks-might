using System.Net;
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

    // ── mobalytics: embedded JSON in __PRELOADED_STATE__ has shape:
    //   "tierLists":{"values":[{"id":"…","tierSections":[
    //     {"name":"God Tier","color":"tier-e","description":null,"ugDataItems":[
    //       {"id":"…","iconUrl":"…classes-icons/Sorcerer.png","linkUrl":"/diablo-4/builds/sorcerer-ball-lightning","title":"Ball Lightning - Mekuna's Ballkuna","subTitle":"Mekuna"},
    //       …
    //     ]},
    //     {"name":"S","color":"tier-s",…},
    //     {"name":"A",…}, {"name":"B",…}, {"name":"C",…}, {"name":"Support",…}
    //   ]}]}
    // Slashes are JSON-escaped as / throughout.
    // Tier sections look like {"name":"X","color":"tier-y","description":<value>,"ugDataItems":[...]}.
    // <value> is usually null or a plain string, but the Support section uses a structured object
    // ({"root":{"children":[…]}}) — so we just skip lazily until ugDataItems rather than enumerating
    // all the description shapes.
    private static readonly Regex MobaTierSection = new(
        @"""name"":""(?<name>God Tier|S|A|B|C|D|Support)"",""color"":""tier-[a-z]"",.*?""ugDataItems"":\[(?<items>[^\]]*)\]",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Loose twin of MobaTierSection: same anchors, ANY section name. Drift tripwire — the section
    // canary enumerates these and fails loud if the live page carries a section the whitelist
    // above would silently drop (a renamed "God Tier", a new "S+", …).
    private static readonly Regex MobaAnySection = new(
        @"""name"":""(?<name>[^""]{1,40})"",""color"":""tier-[a-z]"",.*?""ugDataItems"":\[",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>Every tier-section name on a Mobalytics tier-list page, WITHOUT the known-name
    /// whitelist <see cref="ParseMobalytics"/> applies. Canary fuel, not a parse path.</summary>
    public static IReadOnlyList<string> EnumerateMobaSectionNames(string html) =>
        MobaAnySection.Matches(html).Select(m => m.Groups["name"].Value).Distinct().ToList();
    // Slashes appear as `/` in the raw HTML embed and as plain `/` in already-decoded JSON
    // (or in test fixtures). The (?:\\u002F|/) alternation tolerates both.
    // The icon is captured loosely and the CLASS is derived from the build slug (preferred) or the
    // icon filename: Paladin/Warlock builds stopped using the classes-icons/<Class>.png convention
    // ("uploads/images/diablo-4/Paladin.png?v1", "…/Warlock-icon.png"), and an icon-path-anchored
    // regex was silently dropping 100% of both classes (measured live 2026-06-10: 23/62 endgame,
    // 9/31 leveling, 17/37 pushing builds lost). `[^{}]*?` tolerates new fields between iconUrl and
    // linkUrl without ever crossing into the next item object.
    private static readonly Regex MobaItem = new(
        @"""iconUrl"":""(?<icon>[^""]*)""[^{}]*?""linkUrl"":""(?:\\u002F|/)diablo-4(?:\\u002F|/)builds(?:\\u002F|/)(?<slug>[^""]+)"",""title"":""(?<title>[^""]+)""",
        RegexOptions.Compiled);

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

    public static TierList ParseMobalytics(string html)
    {
        var builds = new List<TierBuild>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match section in MobaTierSection.Matches(html))
        {
            var rawTier = section.Groups["name"].Value;
            var tier = rawTier == "God Tier" ? "God" : rawTier;
            if (Array.IndexOf(MobaTiers, tier) < 0) continue;
            var itemsChunk = section.Groups["items"].Value;
            foreach (Match m in MobaItem.Matches(itemsChunk))
            {
                var slug = m.Groups["slug"].Value;
                var title = Decode(m.Groups["title"].Value).Trim();
                var cls = ClassFromSlugOrIcon(slug, m.Groups["icon"].Value);
                if (title.Length == 0 || !seen.Add($"{title}|{tier}")) continue;
                builds.Add(new TierBuild(title, cls, tier, "https://mobalytics.gg/diablo-4/builds/" + slug));
            }
        }
        return new TierList("Mobalytics", MobalyticsUrl, Order(builds));
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
