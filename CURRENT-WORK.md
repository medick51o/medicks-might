# CURRENT WORK — Medick's Might

**Fingerprint:** branch `codex/900plus-tier-gate` · HEAD `f4ebefe` · gate **412 passed / 0 failed / 5 skipped**
(run 2026-09-15 19:02 by the orchestrator, not by a builder)
**Verify:** `dotnet test` from the repo root.
**Last updated:** 2026-09-15 20:06

## WHAT THIS IS
A Diablo 4 loot-filter compiler (WPF / .NET 10). It turns build definitions into a Base64
payload pasted into Diablo 4's in-game loot-filter UI. Correctness depends on generated
catalogs of item types, uniques, affixes, and Talisman/Charm/Seal sets.

## THE STANDING TRUTH, SAID PLAINLY
**No feature of this app has ever been validated in Diablo 4.** Gates pass. The wire format
has never been confirmed to import. Three feature legs are public (v1.3.2) on that basis.
Two in-game checklists were written (2026-07-28, 2026-08-02) and neither was ever run.

## BLOCKER 1 — the 900-only gate may be a total loot blackout
`f4ebefe` commits an opt-in "900 only" gate: a union hide-rule for item power **1-899**,
landing BEFORE the affix keeper rules (`FilterCompiler.cs:419-452`, hide emitted ~:456/:479/
:484/:487). Compiler constants already assume orange=900, cyan=850 (`:228-235`, `:609-612`).
A sourcing seat reports Season 15's ceiling is **750 normal / 800 Ancestral, and that item
power 900 does not exist**. If true, enabling this switch hides **every drop in the game**.
- Status: **REPORTED, not CONFIRMED.** The claim cites community sites (Maxroll, D4Gold);
  Blizzard's own page could not be fetched. The seat that reported it ran on a fast-tier
  brain. An independent refuter seat (Grok) was dispatched TWICE and failed both times; the
  second timed out at 3600s with nothing on disk. See `SIGNED-grok.md` in the council dir.
  A fresh Claude-lineage seat was dispatched on this question at 2026-09-15 20:05.
  **Council coverage is 2 of 3 vendors, so blind corroboration here is INCOMPLETE.**
- It defaults OFF (`:96`). That is the only reason this is not already a live hazard.
- **Settles it:** one dropped, unupgraded 900-power item observed in game. An editor slider
  that merely permits 900 does NOT count.
- **Do not ship this switch until that observation exists.**

## BLOCKER 2 — the whole data-refresh lane is externally blocked
Catalogs are pinned to game data `3.1.0.72592`: `TalismanSetDatabase.cs:11`,
`UniqueDatabase.cs:337`; `AffixDatabase.cs:5` to CoreTOC `3.0.3.72031`.
**No public Season 15 data dump exists yet** — `blizzhackers/d4data` last published
`3.1.0.72592` on 2026-07-02. New uniques, talisman IDs and affix catalogs cannot be
regenerated until upstream publishes. This is a dependency, not an effort problem.
Do not hand-enter IDs; a wrong ID silently hides loot.

## KNOWN TRAPS
- **Gear ID != charm ID.** "Banished Lord's Talisman" is gear `0x17d2dc` (`UniqueDatabase.cs:99`)
  and charm `0x276db6` (`UniqueCharmDatabase.cs:25`). Name-only merging targets the wrong form.
- **Affix thresholds are COUNTS, not values.** Numeric text is stripped (`AffixMapper.cs:133,139`);
  conditions emit affix IDs + a minimum count (`D4Filter.cs:110-112`). A resistance value
  retune therefore needs no threshold math.
- **A missing unique loses highlighting, not loot** (`UniqueDatabase.cs:376`) — UNLESS the 900
  gate is on, which converts it into real hiding.
- **`RoundTripOk` overpromises.** It counts top-level rule fields only (`:698`/`:717`): no
  nested conditions, no Base64 decode, no import check.
- **`GameDataUpdater` is not transactional.** `:29` promises a paired install; `:103-104`
  install separately, so the second can fail after the first lands.
- **This suite has twice pinned a bug as correct.** A suspicious test is a finding. Any new
  regression test must be proven RED against unfixed code first.
- **Codex cannot build this repo in-sandbox.** `dotnet build` dies on
  `Failed to read NuGet.Config due to unauthorized access` (`%APPDATA%` is outside the
  workspace). File writes DO work. So Codex edits; the orchestrator runs the gate.

## IN FLIGHT
- **QUEUED:** astra Part A cleanup (6 behaviour-preserving items). Ticket banked verbatim at
  `C:\Sync\Projects\d4-s15-council\QUEUED-astra-partA.md`. Blocked by an OpenAI usage limit
  at 19:15; reset **20:24**. Re-fire unchanged.
- **HELD for a ruling:** astra item 7 (MainViewModel extraction, MEDIUM) and all of astra
  PART B — 6 elevation proposals, its own top pick being B1, a preview that shows the
  filter's real encoded intent before Copy. Council notes in `..\d4-s15-council\`.

## REPO LAW
One seat, never a fleet. Never push, merge, or tag. The owner's real name, email, and user
path must never touch this repo. Never weaken a test to make a gate green.
