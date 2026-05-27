using System.Net;
using System.Text.RegularExpressions;

namespace D4BuildFilter.Core;

/// <summary>One ranked build on a community tier list.</summary>
/// <param name="Name">Display name (maxroll includes the class, e.g. "Whirlwind Barb";
/// d4builds is the skill only, e.g. "Whirlwind").</param>
/// <param name="ClassName">Character class, Title-cased (e.g. "Barbarian").</param>
/// <param name="Tier">Single-letter tier: "S", "A", "B", …</param>
/// <param name="Url">Link to the build's page on the source site.</param>
public sealed record TierBuild(string Name, string ClassName, string Tier, string Url);

/// <summary>A community tier list scraped from one site.</summary>
public sealed record TierList(string Source, string SourceUrl, IReadOnlyList<TierBuild> Builds);

/// <summary>
/// Scrapes the live endgame tier lists from maxroll.gg and d4builds.gg so the app's landing page
/// can show the season's top builds, grouped by tier and split by source. Both pages are
/// server-rendered, so one <see cref="BrowserFetch"/> GET is enough — no JS execution needed.
/// Parsing is intentionally tolerant; if a site changes shape the UI falls back to a plain link.
/// </summary>
public static class TierListFetcher
{
    public const string MaxrollUrl = "https://maxroll.gg/d4/tierlists/endgame-tier-list";
    public const string D4BuildsUrl = "https://d4builds.gg/tierlist/";

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

    /// <summary>Tiers we surface (top of the list). The sites also publish C/D/X; we skip those —
    /// people running this tool play the meta.</summary>
    private static readonly string[] ShownTiers = { "S", "A", "B" };

    public static async Task<TierList> FetchMaxrollAsync(CancellationToken ct = default) =>
        ParseMaxroll(await BrowserFetch.GetStringAsync(MaxrollUrl, ct));

    public static async Task<TierList> FetchD4BuildsAsync(CancellationToken ct = default) =>
        ParseD4Builds(await BrowserFetch.GetStringAsync(D4BuildsUrl, ct));

    public static TierList ParseMaxroll(string html)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var builds = new List<TierBuild>();
        foreach (Match m in MaxrollBuild.Matches(html))
        {
            var tier = m.Groups["tier"].Value;
            if (Array.IndexOf(ShownTiers, tier) < 0) continue;
            var name = Decode(m.Groups["name"].Value);
            if (!seen.Add($"{name}|{tier}")) continue;   // dedupe SSR + hydration copies
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
            if (Array.IndexOf(ShownTiers, tier) < 0) continue;
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

    // Keep S→A→B order, builds within a tier in source order.
    private static IReadOnlyList<TierBuild> Order(IEnumerable<TierBuild> builds) =>
        builds.OrderBy(b => Array.IndexOf(ShownTiers, b.Tier)).ToList();

    private static string TitleCase(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

    private static string Decode(string s) => WebUtility.HtmlDecode(s.Replace("\\u0026", "&"));
}
