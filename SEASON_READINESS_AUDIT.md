# Season-readiness audit — Medick's Might (2026-06-10)

Multi-agent audit (6 parallel auditors, every code finding adversarially verified, 0 refuted)
ahead of the Season 14 rollover. **S13 ends 2026-06-30; S14 (patch 3.1) launches between
2026-06-30 and ~2026-07-26** (PTR ran 06-02→06-09; no official date yet). Paladin & Warlock have
been live since Lord of Hatred (2026-04-28) and will be on every S14 list.

## ✅ Fixed in the 2026-06-10 live-sync pass

| # | Finding | Fix |
|---|---------|-----|
| 1 | **Mobalytics parser silently dropped 100% of Paladin + Warlock builds** (23/62 endgame, 9/31 leveling incl. half of S-tier, 17/37 pushing — measured live). Icon-anchored regex didn't match the new classes' icon paths. | Class now derives from the build slug (icon filename as fallback); any icon path accepted; unknown classes list with neutral color instead of vanishing. Live canary now asserts Paladin/Warlock presence. |
| 2 | Mobalytics **D-tier builds parsed then discarded** (MobaTiers whitelist lacked "D"). | "D" added. |
| 3 | **Favorites pinned their star-time tier forever** — the "Maxroll moved it S→A but we still show S" problem. Tier was never compared to any fresh fetch, across launches. | `TierReconciler` (Core, unit-tested) re-syncs every favorite's tier after **every** tier-list fetch: chips show "S→A" on movement, "was S · off the list" on delisting, tooltip shows when the tier was last verified. |
| 4 | **Tier caches lived for the whole session with zero invalidation**, no refresh affordance anywhere; re-clicking the active tab was a silent no-op. | 15-min TTL on cached tabs; **⟳ refresh-lists button + F5** force-refresh all three active tabs *and* every list a favorite references; re-clicking the active tab refreshes that source; per-source "updated HH:mm" stamps. |
| 5 | **HTTP 403/challenge pages parsed as a fake-empty list** ("No builds in this list yet"), cached all session — the likely season-day failure mode (curl had no `--fail`). | curl `--fail` added (errors now surface and fall through to HttpClient); 0-build parses are **never cached** (next activation retries); distinct status text for "couldn't reach" vs "nothing parsed"; failures logged to `%LOCALAPPDATA%\MedicKsMight\logs`. |
| 6 | **Tab-switch race**: `fetch(default)` + no active-tab guard meant a slow stale fetch could paint the wrong list under the active tab. | Per-source CancellationTokenSource + sequence guard; superseded fetches are cancelled (curl process killed) and never touch the UI; refresh-in-place keeps old rows visible while re-fetching. |
| 7 | **Provenance poisoning**: loading from a favorite chip or hand-pasted URL kept the *last clicked chip's* tier in `_currentTier`, so re-starring from the result page could persist a fabricated tier. Favorites-rail clicks never stamped `DateLastOpened`. | Favorites rail seeds provenance from the saved entry + stamps opened; hand-entered URL / sample loads clear tier provenance. |
| 8 | Paste favorites' tooltip claimed "click to re-fetch (live)" — the one favorite type that can never self-update. | Tooltip now says so honestly. |
| 9 | Test-health: the only full-page Mobalytics test **passed vacuously** when its fixture file was missing; one xUnit2031 warning. | `LocalFixtureFactAttribute` (reports Skipped, never false-Passed); warning fixed. Suite: **116 passed / 0 failed** (was 107), 3 live canaries strengthened + verified green against the real sites on 2026-06-10. |

## 🔴 Remaining — high priority before S14

1. ~~**ItemTypeDatabase lacks Flail / Quarterstaff / Glaive**~~ **RESOLVED 2026-06-10.**
   Flail = `0x234a98` (1H, Paladin/Warlock), Glaive = `0x165271` (2H), Quarterstaff = `0x16d22d`
   (2H, both Spiritborn). Every id corroborated by ≥2 independent sources (d4data ItemType
   `__snoID__` × fnuecke names.json; control rows Dagger/Polearm match the existing table), and
   Flail is filter-validated in two decoded real in-game exports (GameRant Universal + wudijo
   endgame, D4LootBench reference codes) — proving new types use the raw snoID in type-5
   conditions. `ResolveSlot` now takes bare + `1H`/`2H`-prefixed labels; handedness merge and
   per-slot rule emission covered by `NewWeaponTypeTests` incl. a weapon-rule-above-hide-rule
   regression test and a bit-exact protobuf round-trip. Suite: **133 passed / 0 failed**.
2. ~~**Bundled game data is frozen in the exe**~~ **RESOLVED 2026-06-10.**
   `DataFiles` now prefers a *validated* `Affixes/Uniques.enUS.json` pair under
   `%LOCALAPPDATA%\MedicKsMight\data\` (any parse/shape failure logs + falls back to bundled);
   **⚙ → Update game data** downloads the current pair from josdemmers/Diablo4Companion
   `D4Companion/Data/` raw URLs (researched: DiabloTools/d4data's `json/` tree carries only raw
   dumps, not these processed per-locale files; D4LootBench releases ship an exe, not data),
   validates parse + entry-count sanity vs bundled (≥80%), installs atomically, reports the data
   version from the repo's last data commit, and invalidates the cached lookups. Maxroll
   nids/unique-ids the local data can't name are no longer silently dropped — counted on
   `ResolvedBuild`, logged, and surfaced as an amber result-page note with an inline Update link
   (which re-fetches the build after a successful update). Offline-stubbed tests cover fallback
   ordering, the validator, and the updater's no-clobber guarantees; a new `RUN_CANARY=1` canary
   exercises the live GitHub flow (green 2026-06-10). Suite: **159 passed / 0 failed**.
3. ~~**Discord invite dead-links ~2026-06-29**~~ **RESOLVED 2026-06-10.** Swapped to a permanent
   invite (`discord.gg/jnTk6Ha2ue`), deliberately capped at 25 uses as anti-bot hygiene — Medick
   mints a fresh one when it fills (cheaper than cleaning 100 bots out of the server).
4. **S14 rule change: any Unique can roll Mythic quality** — invalidates the curated-mythic-list
   assumption in `UniqueDatabase` (mythics currently get a hands-off pass by name). **Last open
   season-blocking item** — needs S14 patch notes/PTR data to act on; revisit at season launch.

## 🟡 Worth doing (verified, not season-blocking)

- **Degenerate filter guard**: a build whose affixes all fail to map emits a `Rare/Leg [0+]` rule
  that gold-flags *every* rare/legendary — no warning. Guard + warn.
- **11 uniques can never get a purple rule**: JSON names vs hand-coded C# dict drift (6 from
  mojibake/NBSP baked into `UniqueDatabase.cs`).
- **MainViewModel split**: the tier pipeline is triplicated per source (~150 lines + three
  near-identical 73-line XAML columns). Extract a `TierSourceSectionVM<TKind>` + one DataTemplate;
  inject `IFavoritesStore`/fetcher for testability. (The refresh feature landed in the triplicated
  shape — fine at current scale, refactor before the next per-source feature.)
- **Class-filter strip renders on Compile/Favorites tabs** where it controls nothing (and doesn't
  filter favorites); gate to Browse or make it filter favorites too. The 8-class list is also
  hardcoded — an unknown class renders chips but can't be filtered.
- **Mobalytics tier-name whitelist** ("God Tier|S|…|Support") silently skips any renamed/new
  section next season; longer-term parse the `__PRELOADED_STATE__` JSON instead of regexing HTML.
- d4builds fetch rests on three rot-points (hardcoded Firebase apiKey/projectId, Gatsby page-data
  shape, App Check get-by-id loophole) — fallbacks exist (headless scraper, paste mode); keep the
  canaries running weekly (`$env:RUN_CANARY=1; dotnet test --filter Category=Canary`).
- **No CI**: canaries never run automatically; a weekly scheduled run would catch site drift.
- Repo hygiene: committed debris (15-byte `maxroll_d4_data.json` containing the string
  "unauthenticated", duplicate 396 KB `sample_barb.json` at root, `last_code*.txt`, pitch PDF);
  branch `webapp-convergence-20260530` is **unpushed** (no remote counterpart); `.git` is 127 MB
  from binary history; ~141 MB gitignored screenshots/publish staging on disk.

## ✅ Confirmed healthy (don't churn)

Threading discipline (no async-void, UI-context awaits, CPU work off-thread); data-driven tier
rows (unknown tiers/classes render gracefully); cache-unfiltered/project-filtered class-filter
design; frozen brushes; favorites hydrate from disk before network; the 25-rule cap warning and
round-trip corruption check are real user protection. Maxroll (all 5 lists) and d4builds (endgame +
leveling; Tower genuinely empty until S14) parse perfectly live; curl path confirmed working.
