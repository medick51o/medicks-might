# Season 14 readiness spec — Medick's Might (2026-06-30)

**Context.** Diablo 4 **Season 14 "Death Awakening" (patch 3.1.0)** went live today
(2026-06-30, 1pm EDT). This spec closes the last open item from `SEASON_READINESS_AUDIT.md`
(2026-06-10) — the deferred **Mythic 3.0** change — plus a user-requested **tier recolor**,
a **season data refresh**, and a **season-day verification** pass.

Scope chosen with Medick: **Standard readiness** + mythic handling kept **tight (fix detection,
least clutter)**.

---

## Goal / why

Get the existing app **correct and ready for S14 launch** — not a feature build. Concretely:

1. The S14 itemization change (mythic is now a *quality on any unique*) broke an assumption in
   the filter compiler. Fix it.
2. Real user feedback: the **gold** keeper-tier blended into yellow rare items, and **silver**
   looked too dull. Recolor the two affix tiers.
3. Refresh season data so new S14 items resolve.
4. Prove all of it works on patch day.

## Out of scope (deliberate)

- **New S14 *systems*** — Pandemonium Ruptures, Solo Self-Found, War Boards, Tower/Pit reward
  changes, the Corrupted Reaper lair boss. These are activities, not loot-filter rules.
- **New weapons** — Flail / Quarterstaff / Glaive were already added in the 2026-06-10 pass.
- The audit's **🟡 polish items** (degenerate-filter guard, 11 name-drift uniques, MainViewModel
  split, repo hygiene) — parked unless promoted later.

---

## A. Mythic Uniques 3.0 — fix detection (the season-blocker)

**Problem.** S14 makes *mythic* a **quality any unique can have or be upgraded to** (via the
Horadric Cube), not a fixed set of "uber" items. All uniques now also drop with 2 guaranteed
affixes and are enchantable. The app currently identifies mythics from a **hardcoded list**
(`UniqueDatabase.Mythics` / `IsMythic`) and pulls them into their own category — that list is now
obsolete and unmaintainable.

**Change.**
- **Retire** `UniqueDatabase.Mythics` and `IsMythic()`.
- In `FilterCompiler.Analyze`, drop the mythic carve-out: **every build unique** resolves to a
  **purple type-8 targeting id** if known, else lands in **pending** (unchanged path). No separate
  mythic bucket; remove `CompiledBuild.Mythics`.
- **UI:** remove the result-screen **"Mythics" panel** (`MythicLines` / `HasMythics` /
  `HasNoMythics` in `MainViewModel` + the XAML panel). Its data source is the stale list, so in S14
  it would show wrong info. Build uniques now appear only in the Uniques list.
- Both fetchers already treat mythics as ordinary uniques (they only resolve names) — no fetcher
  change needed beyond deleting the now-wrong `IsMythic` comments.

**Safety guarantee (already true, keep it).** The final **"Hide the rest"** rule masks only
`Common | Magic | Rare | Legendary` — it **never hides Unique (0x10) or Mythic (0x20)**. So a mythic
drop is **never filtered away** (keeps its natural beam) regardless of how 3.1 tags it internally.
Keep `Rarity.Mythic = 0x20` defined (harmless, future-proof).

**Tests.**
- Replace `IsMythic`/`Mythics` assertions (e.g. `FilterCompilerTests` "Tyrael's Might in b.Mythics").
- Add: a former-uber unique present in a build now produces a **purple targeting id** (or pending),
  **not** a mythic carve-out.
- Add/keep a test asserting the hide-rest mask **excludes** the Unique and Mythic bits (the
  never-hide-a-mythic guarantee, pinned).

**Done when:** no `IsMythic`/`Mythics` references remain; build uniques handled uniformly; the
never-hide-a-mythic test is green.

---

## B. Tier recolor — red / pink (red = valuable)

**Principle (Medick).** **Red = high-value / chase**, and red is **reserved** so it stays a strong
signal. Sharing red across chase categories is intentional. Pink = solid secondary keeper.

**Final palette.**

| Color | Means | Rule(s) |
|---|---|---|
| **Red** | Chase / high-value | 3-affix rares & legendaries **+** Ancestral Charms & Seals (shared) |
| **Pink** | Solid keeper | 2-affix rares & legendaries |
| Purple | Build uniques | unchanged |
| Orange / Cyan | Item Power 900+ / 850+ | unchanged (off by default) |
| Blue | Greater Affixes (off-build) | unchanged |
| Green | Charms & Seals (basic) | unchanged |
| White | Codex upgrades | unchanged |
| — | Non-build uniques & mythics | untouched (natural beam) |

**Change.**
- Add `FilterColors.Pink` — default **#FF69B4** (hot pink); bump to **#FF1493** (deep pink) if it
  needs more punch in-game.
- Point the two affix tiers at the new colors: swap `Analyze(build, FilterColors.Gold,
  FilterColors.Silver)` → `(…, FilterColors.Red, FilterColors.Pink)` at all call sites
  (`MainViewModel`, `Tester/Program.cs`, and the tests).
- **Ancestral Charms & Seals** rule (6a) **stays `Red`** — now shared with the 3-affix tier, by
  design. Basic charms stay green (6b).
- Leave every other rule color unchanged (protects red's meaning).
- Update the **in-app Legend** swatches + labels ("Gold/Silver" → "Red/Pink") and the **README**
  color descriptions. `FilterColors.Gold`/`Silver` constants may be left defined (unused) or removed
  as cleanup.

**Shades.** Bake strong defaults (red ≈ existing **#DC0000**, pink ≈ **#FF69B4**); Medick verifies
the *feel* in-game and we tweak if any shade is off. (Note: red vs pink can read similar to some
colorblind players — the in-game check covers it.)

**Done when:** filter emits red for the 3-affix tier **and** ancestral charms, pink for the 2-affix
tier; in-app legend + README match; round-trip + 25-rule cap stay green; Medick confirms shades
in-game.

---

## C. S14 game-data refresh

**Change.**
- Refresh the **bundled** `Affixes.enUS.json` + `Uniques.enUS.json` from **josdemmers/Diablo4Companion**
  (shipped **v5.3.3.0, 2026-06-29**) so new-season names resolve out of the box for a fresh install;
  update the `_share/MedicK's Might (BETA)/Data` copy too.
- Verify the in-app **⚙ → Update game data** live path still works (`RUN_CANARY=1` GitHub flow).

**Known, graceful gap.** New S14 uniques' **filter type-8 ids** live in the hardcoded
`UniqueDatabase.ByName` (sourced from d4data build 3.0.2). Brand-new S14 uniques won't have ids until
d4data publishes its 3.1 dump — until then a build using one shows it as **pending** (still safe —
never hidden, surfaced as an amber note). Check whether 3.1 ids are available; if not, document and
rely on the existing pending-surface.

**Done when:** bundled data updated and spot-checked to contain S14 content (entry counts ≥ prior;
a couple of known new unique names resolve) **or** the gap is explicitly documented; updater canary
green.

---

## D. Season-day verification

- **Full xUnit suite green** (updated for A & B).
- **Live canaries** (`$env:RUN_CANARY=1; dotnet test --filter Category=Canary`): all three tier
  sources (Maxroll / D4Builds / Mobalytics) still parse on patch day; round-trip + cap green.
  Specifically re-check the **Mobalytics tier-name whitelist** in case S14 renamed any sections.
- **Final gate (Medick, in-hand):** import the generated filter in S14 and confirm — a real mythic
  drop behaves, and the red/pink shades feel right. That in-game export also settles the
  mythic-encoding unknown for good.

---

## Risks / notes

- **Mythic internal encoding** (does 3.1 export tag mythics `0x20`, or as Unique `0x10`?) is
  unconfirmed until an in-game export. The never-hide guarantee holds **either way**, so it does not
  block the work.
- **No auto-commit** — per house rules, commit only when Medick asks.
- Build/tests must be **green before any "done"** claim; Medick's in-game validation is the final gate.

---

## Implementation status — 2026-06-30 (built + verified; pending in-game sign-off)

- **A. Mythic 3.0** ✅ Retired `UniqueDatabase.Mythics`/`IsMythic` + `CompiledBuild.Mythics`; every build
  unique handled uniformly (purple targeting or pending); result-screen Mythics panel removed (now a
  2-col Uniques | Legend). Safety pinned by `Hide_rest_never_hides_unique_or_mythic_items`.
- **B. Recolor** ✅ Added `FilterColors.Pink` (#FF69B4). 3-affix → Red (#DC0000), 2-affix → Pink,
  Ancestral Charms & Seals share Red (red = valuable/chase, by design). In-app legend + option swatches
  + tooltip + README + Tester updated. Verified by decoding a real import code: `[3+]`=#DC0000,
  `[2+]`=#FF69B4, `(Anc)`=#DC0000, Build Uniques=purple, Hide=HideAll. Round-trips at 10 rules (<25 cap).
- **C. Data refresh** ✅ Bundled `Core/Data` + `_share` refreshed from josdemmers/Diablo4Companion
  (master, same source as the in-app updater): Affixes 891→893, Uniques 299 (content refreshed). Valid
  JSON, parsed counts confirmed.
- **D. Verification** ✅ Solution builds 0 warnings/0 errors; 161 unit tests green; live canaries
  (`RUN_CANARY=1`) green — all three tier sources parse on patch day + the data-updater flow works.
- **Remaining (human gate):** Medick imports the filter in S14 and confirms (1) red/pink shades feel
  right, (2) a real mythic drop is never hidden. New tests added: `Former_uber_uniques_are_targeted_
  like_any_build_unique`, `Hide_rest_never_hides_unique_or_mythic_items`, `Chase_tier_and_ancestral_
  are_red_keeper_tier_is_pink`, `Rule_names_carry_their_color_label_for_in_game_scanning`. Not committed (house rule).

---

## E. Rule-name color labels — 2026-06-30 (added per request; done + verified)

Each recolor rule's name now carries its color as a suffix so players can scroll the in-game filter
list and toggle by color (e.g. turn **Charms & Seals (Green)** off at a glance). Suffix format
(Medick's pick over color-first). Added `FilterColors.NameOf(uint)` + a `Recolor` helper in
`FilterCompiler.Compile`; ancestral squeezed to **Charms&Seals Anc (Red)** to fit D4's 24-char name
cap; the Hide rule stays uncolored. Verified by decoding the real output — all 10 names ≤24 chars,
color intact: `Build Uniques (Purple)` · `Rare/Leg [3+] (Red)` · `Rare/Leg [2+] (Pink)` ·
`Item Power 900+ (Orange)` · `Item Power 850+ (Cyan)` · `Greater Affixes (Blue)` ·
`Charms&Seals Anc (Red)` · `Charms & Seals (Green)` · `Codex Upgrades (White)` · `Hide the rest`.

---

## F. Bug fix — catch-all hide rule was leaking items — 2026-06-30

Medick (in-game): unwanted items still dropped through "Hide the rest"; setting Item Power ≥1 fixed
it. **Root cause:** D4 won't apply a rule that has ONLY a rarity condition (conditions are ANDed, so
the rarity was matching — the engine just ignores a single-rarity-condition rule). Every other rule
already pairs rarity with a concrete second condition; the hide rule didn't. **Fix:** added
`Conditions.ItemPower(1, ItemPowerCap)` to the hide rule. Pinned by
`Hide_rest_carries_an_item_power_condition_so_d4_actually_applies_it`; the never-hide-Unique/Mythic
guarantee + round-trip stay green. Pre-existing bug (not introduced by the S14 work). In-game
confirmation by Medick is the final gate.

---

## G. Day-2 (30h) sweep — 2026-07-01: data drops landed, uniques backfilled, tripwire armed

The S14 datamine wave landed overnight, faster than the ~3-week estimate:
- **d4data rebuilt for 3.1.0.72592** (2026-07-01 04:28Z) and **D4LootBench shipped Season-14
  d4-data.json** (formatVersion 4, 771 unique entries) — the validated snoId==type-8 lineage.
- **UniqueDatabase backfill (+28):** every missing display name merged additively — incl. clean-name
  aliases that close ALL SIX mojibake-drift entries from the June-10 audit, and 21 new **"(Crucible)"**
  weapon-variant items (S14 system; ids 0x27b52b–0x27b557). Excluded dev junk; Eaglehorn id conflict
  deliberately NOT applied (ours 0x15f732 kept until an in-game export decides).
- **Bundled game data re-refreshed** to Diablo4Companion's v3.1.0.72592 files (committed upstream
  2026-07-01 12:20Z; content changed, counts steady at 893/299). Core/Data + _share updated.
- **Mobalytics section tripwire (recon's #1 hardening item):** `TierListFetcher.EnumerateMobaSectionNames`
  (loose twin of the whitelist regex) + an offline unit test + a live canary that fails LOUD if the
  page ever carries a section the parser would silently drop.
- **Verification:** 170 tests / 0 failed WITH live canaries (new section canary green against the
  live page at ~30h into S14); solution builds 0 warnings / 0 errors. 30h recon fleet (wf `wc02osofj`)
  out for meta/hotfix/competitor drift — synthesis folds in on return.
- **Open questions for the fleet / in-game export:** what "(Crucible)" is mechanically (if it's the
  mythic-crafted variant, purple targeting may want name→[baseId, crucibleId] multi-mapping); the
  Eaglehorn id; official-notes confirmation of the wire format.

---

## H. Final gate checklist — Medick, one in-game pass (THE beta blocker)

One import session settles everything still open. In S14, with a filter generated from the current
build (Desktop shortcut runs it):

- [ ] **(a) Import accepted** by the 3.1.0 client — proves the wire format survived the patch.
- [ ] **(b) Red (3+) vs Pink (2+)** render distinctly and feel right in real loot (tweak shades if not).
- [ ] **(c) Junk actually hides** — the Item-Power≥1 hide fix working in the wild.
- [ ] **(d) A Unique/Mythic drop is never hidden** — natural beam intact.
- [ ] **(e) 25-rule cap behavior** — load a fat build (Barb), confirm the cap warning guides correctly.
- [ ] **(f) EXPORT one filter and paste it back** — decoding it settles: wire-format details, how a
      "(Crucible)" item is tagged (decides the multi-mapping question), and the Eaglehorn id if one
      drops. `dotnet run --project D4BuildFilter.Tester -- decode <code>` does the rest.

Post-gate (Medick's calls, locked 2026-07-01): **friends beta only** (public parked) · **merge
webapp-convergence + s14-readiness lineage → master** · version bump · rebuild the publish zip
(whole publish folder — native DLLs + Assets/Data) · Desktop zip + private GH release → crew.

30h fleet verdict (overseer unsatisfied after 3 rounds — over report hygiene, not the repo): all
repo-touching findings re-verified correct; hotfix 3.1.0a (XP/login) has zero filter impact; bundle
confirmed current vs v5.3.4.0-era master; stale God-tier fixture test fixed (fixture captured live,
assertion made structural); section canary now also guards the S/A/B core trio.

---

## I. v1.0.1 — Build-scoped Charms & Seals (the checkbox panel) — 2026-07-02

Medick's in-game report: the green Charms & Seals rule showed EVERY seal (unscoped type-only
catch-all; the fetcher deliberately discarded all talisman data). He then hand-built the correct
rule in the 3.1 in-game editor and exported it — that export is the pinned ground truth.

- **Wire:** type-9 TalismanSetBonus with per-item refinement (field3 `{1:set, 2:item×N}`) —
  encoder overload + decoder surface (`SetItems`), byte-pinned to the export; `ItemPowerAny()`
  filler mirrors the editor's shape.
- **Data:** `TalismanSetDatabase` — all 45 S14 sets (8 classes × 5 + 5 generics) with display
  names + member charms, GENERATED from d4data 3.1.0.72592 (D4LootBench, formatVersion 4);
  Sescheron's Fury cross-validated against the export. No scraping, no agents — data was on disk.
- **Fetcher:** Maxroll set-charm ids (`Talisman_Charm_Set_Barb_05_NN`) → per-variant
  `ResolvedVariant.TalismanSets` (display names).
- **Compiler:** `FilterOptions.TalismanSets` — null = legacy catch-all (paste builds), empty =
  rules omitted (all unchecked → hidden), non-empty = green + ancestral-red both scope to the
  selection in the export shape; one rule each (25-cap honored).
- **UI (Medick's wireframe):** "Charm sets" checkbox panel at the bottom of Filter options — the
  class's 5 sets + 5 generics, unchecked by default, the BUILD's sets pre-checked (his call:
  defer to the build data); user choices survive recompiles; unchecked = hidden. Footer now
  shows `v<version>[-dev] • built <timestamp>` (the version-visibility ask).
- **Verified:** 181/181 incl. live canaries, zero skips; the live WW Barb auto-scopes to
  Berserker's Crucible + Sescheron's Fury (reconciling the original Crucible-charm screenshot).
  Preliminary in-game glow sighting positive; the confirmed pass gates the v1.0.1 release.
