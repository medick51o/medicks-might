# Build-source scraping — findings & status (2026-05-27)

How each build site delivers its data, and what that means for importing builds into the filter.

## Summary

| Source | How data is delivered | Scrape path | Status |
|---|---|---|---|
| **maxroll.gg** | public JSON endpoint `planners.maxroll.gg/profiles/d4/<id>` | plain HTTP GET → JSON | ✅ working (MaxrollFetcher) |
| **mobalytics.gg** | server-rendered `window.__PRELOADED_STATE__` in the page HTML | HTTP GET (via curl, see below) → extract+parse JSON | ✅ working (MobalyticsFetcher) |
| **d4builds.gg** | client-side **Firestore + App Check** (Gatsby SPA) | ❌ no plain-HTTP path; needs headless browser **or** paste | ⚠️ paste bridge today |
| any guide | — | copy affix list → **Paste mode** | ✅ working (PastedBuild) |

## maxroll (done earlier)
`https://planners.maxroll.gg/profiles/d4/<id>` returns JSON; top-level `data` is a JSON-encoded
string with `profiles[]` + `items{}`. Affixes referenced by numeric `nid` → resolved via the
bundled D4Companion `Affixes.enUS.json`. Plain .NET `HttpClient` works (maxroll isn't JA3-strict).

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

**=> No clean plain-HTTP path.** Realistic options:
1. **Paste mode (works today):** open the d4builds build, copy the gear/affix priority text, paste it
   into the app's "Paste affixes" input. Zero new code; pragmatic.
2. **Headless browser (future, with the user):** drive headless Chrome (Playwright/.NET or a small
   Python+Selenium helper), wait for the SPA to render, scrape the gear DOM (d4lf does exactly this:
   classes `builder__stats__list`, `builder__gear__items`, `greater__affix__button--filled`, etc.).
   Heavy dependency (bundled/installed browser) and brittle (breaks on CSS changes) — a deliberate
   choice to make with the user, not added silently. d4lf's `src/gui/importer/d4builds.py` is the
   reference implementation (GPL — reimplement clean-room from the factual DOM selectors).

## "Rona's d4 builds site"
Medick named "Rona's d4 builds site" as the top priority but didn't have the URL. Couldn't pin a
distinct site by that name — strongest guess is this **is** d4builds.gg (the major non-maxroll/
non-mobalytics build site). **Confirm with Medick.** If it's a different site, point me at the URL.

## Recommendation
- maxroll + mobalytics: fetch by URL (done).
- d4builds + everything else: **Paste mode** now; revisit a headless d4builds scraper together if
  the paste flow proves too manual.
