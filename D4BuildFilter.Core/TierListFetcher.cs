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
public static class TierListFetcher
{
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
    // Slashes appear as `/` in the raw HTML embed and as plain `/` in already-decoded JSON
    // (or in test fixtures). The (?:\\u002F|/) alternation tolerates both.
    private static readonly Regex MobaItem = new(
        @"""iconUrl"":""[^""]*classes-icons(?:\\u002F|/)(?<cls>[A-Za-z]+)\.png"",""linkUrl"":""(?:\\u002F|/)diablo-4(?:\\u002F|/)builds(?:\\u002F|/)(?<slug>[^""]+)"",""title"":""(?<title>[^""]+)""",
        RegexOptions.Compiled);

    /// <summary>Per-source list of tiers we surface, in display order. We show ALL meta tiers now
    /// (S→D) plus the Mobalytics-only top (God) and bottom (Support).</summary>
    private static readonly string[] LetterTiers = { "S", "A", "B", "C", "D" };
    private static readonly string[] MobaTiers   = { "God", "S", "A", "B", "C", "Support" };

    /// <summary>Cross-source ordering used by <see cref="Order"/>. Lower index = displayed first.</summary>
    private static readonly Dictionary<string, int> TierOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["God"] = 0, ["S"] = 1, ["A"] = 2, ["B"] = 3, ["C"] = 4, ["D"] = 5, ["Support"] = 6,
    };

    public static async Task<TierList> FetchMaxrollAsync(CancellationToken ct = default) =>
        ParseMaxroll(await BrowserFetch.GetStringAsync(MaxrollUrl, ct));

    public static async Task<TierList> FetchD4BuildsAsync(CancellationToken ct = default) =>
        ParseD4Builds(await BrowserFetch.GetStringAsync(D4BuildsUrl, ct));

    public static async Task<TierList> FetchMobalyticsAsync(CancellationToken ct = default) =>
        ParseMobalytics(await BrowserFetch.GetStringAsync(MobalyticsUrl, ct));

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
                var cls = TitleCase(m.Groups["cls"].Value);
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

    private static string Decode(string s) => WebUtility.HtmlDecode(s.Replace("\\u0026", "&"));
}
