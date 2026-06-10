# Build-source scraping — findings & status (2026-05-27, updated 2026-06-10)

> **2026-06-10 — Mobalytics icon-path drift (fixed).** Paladin/Warlock builds stopped using
> `classes-icons/<Class>.png` (now `uploads/images/diablo-4/Paladin.png?v1`, `…/Warlock-icon.png`),
> so the icon-anchored MobaItem regex silently dropped **100% of both new classes** (measured live:
> 23/62 endgame, 9/31 leveling, 17/37 pushing — half of leveling's S-tier). Parser now derives the
> class from the build slug first and tolerates any icon path; unknown classes list with the neutral
> chip color instead of vanishing. Also fixed: D-tier sections were parsed then discarded (MobaTiers
> whitelist lacked "D"), and `BrowserFetch` now passes `--fail` to curl so 403/challenge pages error
> instead of parsing to a fake-empty list. Live canaries (`RUN_CANARY=1`) now assert Paladin/Warlock
> presence and ≥30 builds on the Mobalytics endgame list.

How each build site delivers its data, and what that means for importing builds into the filter.

## Summary

| Source | How data is delivered | Scrape path | Status |
|---|---|---|---|
| **maxroll.gg** | public JSON endpoint `planners.maxroll.gg/profiles/d4/<id>` | plain HTTP GET → JSON | ✅ working (MaxrollFetcher) |
| **mobalytics.gg** | server-rendered `window.__PRELOADED_STATE__` in the page HTML | HTTP GET (via curl, see below) → extract+parse JSON | ✅ working (MobalyticsFetcher) |
| **d4builds.gg** | Cloud Firestore (Gatsby SPA); build doc world-readable **by id** | Firestore REST get-by-id (slug→`seoId`→GET) | ✅ working (D4BuildsFetcher) |
| any guide | — | copy affix list → **Paste mode** | ✅ working (PastedBuild) |

## maxroll (done earlier)
`https://planners.maxroll.gg/profiles/d4/<id>` returns JSON; top-level `data` is a JSON-encoded
string with `profiles[]` + `items{}`. Affixes referenced by numeric `nid` → resolved via the
D4Companion-format `Affixes.enUS.json` (a validated download under `%LOCALAPPDATA%\MedicKsMight\data\`
when present — see "Update game data" — else the bundled copy). Nids/unique-ids the data can't name
are counted on `ResolvedBuild` (amber note in the app), not silently dropped. Plain .NET
`HttpClient` works (maxroll isn't JA3-strict).

## mobalytics (done 2026-05-27)
The whole build is server-rendered into the page as `window.__PRELOADED_STATE__ = {…};`.
`MobalyticsFetcher` extracts that JSON and walks
`userGeneratedDocumentBySlug.data.data → buildVariants.values[] → genericBuilder.slots[]`,
reading each slot's `gameEntity.modifiers.gearStats[].id` (kebab-case affix slugs) and unique
`gameEntity.entity.title`. Variant names come from `childrenVariants`. No name lookup needed —
Mobalytics ships names directly.
- **Cloudflare/JA3 gotcha:** mobalytics 403s a bare .NET `HttpClient` request (Cloudflare fingerprints
  the TLS ClientHello) but allows the system **curl.exe**. So `BrowserFetch` runs the GET through
  `C:\Windows\System32\curl.exe` (ships with Win10/11) with an HttpClient fallback. Confirmed live:
  curl 200 ×3; HttpClient 403 even with full browser headers + HTTP/2.
- Slug quirks handled by mapper aliases: `all-damage-multipler` (their typo), `<resource> per second`
  → `<resource> Regeneration`, `bonus weapon damage` → `Weapon Damage`.

## d4builds.gg (researched 2026-05-27 — this was the priority; it's the hard one)
Gatsby SPA. The page HTML and `page-data.json` contain only the planner's **generic reference data**
(skill-tree structure, the full lists of possible affixes/gems/stats per slot/class) — NOT the
specific build's selected gear. The actual build is fetched client-side from **Cloud Firestore**
(project `d4builds-a3254`) via the Firebase JS SDK, and the project enforces **Firebase App Check**:
- `firestore.googleapis.com/v1/projects/d4builds-a3254/databases/(default)/documents/<col>` →
  **403 PERMISSION_DENIED** for anonymous REST (App Check blocks tokenless access).
- `page-data/builds/<slug>/page-data.json` → 200 but only reference catalogs (`gearStats:0`, `gear:0`).
- The bundle carries the Firebase web config (apiKey `AIzaSy…NmjDc`, projectId `d4builds-a3254`,
  authDomain `auth.d4builds.gg`) but App Check + security rules gate the data.

**=> UPDATE 2026-05-27: there IS a clean plain-HTTP path after all.** Firebase App Check only gates
`list`; a **get-by-id** on a known build doc is world-readable. So `D4BuildsFetcher` does:
1. **Resolve the build id.** `/builds/<uuid>` → the UUID directly. `/builds/<slug>` (named/featured
   builds like Rob's) → GET `page-data/builds/<slug>/page-data.json` → `result.pageContext.seoId`.
2. **GET the doc:** `firestore.googleapis.com/v1/projects/d4builds-a3254/databases/(default)/documents/builds/<id>?key=<webKey>`
   (apiKey/projectId are the site's public web config from its JS bundle).
3. **Decode** the Firestore typed JSON and read each `variants[].newStats` (per-slot affix-name
   arrays) + `gear` (uniques = gear names that aren't "Aspect …"). `greaterAffixes` carries parallel
   1/null GA flags (available for a future GA-required rule).
Live-validated on Rob's "Singer Ancient Barb" (3 variants) and "Whirlwind Spin2Win Barb" (8 variants).

**Fallbacks (no longer needed for normal use):** the headless-Chrome `tools/d4builds_scraper.py`
(if Firestore rules ever tighten) and Paste mode (any guide).

## "Rona's d4 builds site"
Medick named "Rona's d4 builds site" as the top priority but didn't have the URL. Couldn't pin a
distinct site by that name — strongest guess is this **is** d4builds.gg (the major non-maxroll/
non-mobalytics build site). **Confirm with Medick.** If it's a different site, point me at the URL.

## Recommendation
- maxroll + mobalytics: fetch by URL (done).
- d4builds + everything else: **Paste mode** now; revisit a headless d4builds scraper together if
  the paste flow proves too manual.
