# CURRENT WORK — Medick's Might

**Fingerprint:** branch `codex/900plus-tier-gate` · HEAD `11afe9d` · gate **438 passed / 0 failed / 5 skipped**
(run 2026-09-15 23:01 by the orchestrator, not by a builder)
**Verify:** `dotnet test` from the repo root.
**Last updated:** 2026-09-17 22:20

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
- Status: **DISPUTED — two vendors, opposite answers, neither CONFIRMED.** An independent
  Claude-lineage seat (2026-09-15 20:08) says the OPPOSITE: that 900 IS the Ancestral drop
  ceiling at Torment, unchanged by S15, citing Maxroll's equipment page (updated 2026-07-25)
  and a Blizzard forum thread with a Blue reply. Both seats cite Maxroll and disagree, so one
  read a stale page. Blizzard's own text was unreachable to both (HTTP 500/404).
  **Do NOT strip this feature on the current evidence.** See `..\d4-s15-council\DECISION-QUEUE.md` D1.
- Earlier status line, kept for the record: **REPORTED, not CONFIRMED.** The claim cites community sites (Maxroll, D4Gold);
  Blizzard's own page could not be fetched. The seat that reported it ran on a fast-tier
  brain. An independent refuter seat (Grok) was dispatched TWICE and failed both times; the
  second timed out at 3600s with nothing on disk. See `SIGNED-grok.md` in the council dir.
  A fresh Claude-lineage seat was dispatched on this question at 2026-09-15 20:05.
  **Council coverage is 2 of 3 vendors, so blind corroboration here is INCOMPLETE.**
- It defaults OFF (`:96`). That is the only reason this is not already a live hazard.
- **Settles it:** one dropped, unupgraded 900-power item observed in game. An editor slider
  that merely permits 900 does NOT count.
- **Do not ship this switch until that observation exists.**

## ~~BLOCKER 2~~ — RESOLVED: the data-refresh lane is OPEN
Catalogs are pinned to game data `3.1.0.72592`: `TalismanSetDatabase.cs:11`,
`UniqueDatabase.cs:337`; `AffixDatabase.cs:5` to CoreTOC `3.0.3.72031`.
**RESOLVED 2026-09-17 — the S15 dump EXISTS and this lane is OPEN.**
`DiabloTools/d4data` published **build 3.2.1.73552 on 2026-09-15**, the day S15 launched
(commits "Rebuilt JSON 3.2.1.73552" / "Updated definitions to 3.2.1.73552").
**A prior note here claimed no dump existed, citing `blizzhackers/d4data`. That was WRONG**
and cost two days: `blizzhackers/d4data` was archived 2024-07-04 with "Development halted;
use the linked repo instead." This codebase always cited the correct successor
(`AffixDatabase.cs:5`, `UniqueDatabase.cs:7`, `SetItemBonusDatabase.cs:5`).

**The refresh path:** the compiled catalogs come from that repo's RAW dumps (CoreTOC,
enUS_Text STLs) — see `GameDataUpdater.cs:25-26`, which documents that the processed
per-locale files are NOT there and come from `josdemmers/Diablo4Companion` instead
(`GameDataUpdater.cs:37`). That runtime updater is alive and unaffected.
**There is no automated catalog generator in this repo** — `tools/` holds scrapers and
capture scripts only. Prior refreshes were ad-hoc. Building a repeatable extractor is
the real work, and it is now unblocked.
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
- **DONE 2026-09-15 21:45 — astra Part A cleanup, committed `ca0da57`.** Six behaviour-preserving
  items: tier docs corrected, `RoundTripOk` scope documented, `GameDataUpdater`'s false
  paired-install promise corrected, the wrong season label stripped, the duplicated colour helpers
  centralized in `FilterColors.cs` (now PUBLIC where they were private), and 8 CopySafety precedence
  cases added. Gate rose 412 -> 420. Fence clean: delta was exactly the 6 permitted files.
  Review: Gemini returned no defects, BUT it reported a compilation check it was told not to run,
  so treat it as corroboration, not proof. The pure-move claim was verified independently against
  the diff. **Emitted-byte identity is reasoned, not measured** - no test pins the payload bytes.
  astra could not build at all (its sandbox cannot read the roaming NuGet.Config outside its
  workspace), and its writes to `AffixDatabase.cs` were denied although the file is not read-only;
  the orchestrator applied that one item.
- **DONE 2026-09-15 23:01 — emitted-byte identity MEASURED, committed `1cb9127`.** 18 characterization
  cases pin the SHA-256 of the emitted `ImportCode` across different emission paths. Goldens were
  generated by running the same test against the PRE-cleanup commit `f4ebefe`, then asserted against
  the cleaned-up code: **all 18 matched**, so the cleanup moved no bytes. Proven able to FAIL
  (corrupting one golden gave exactly one red case naming expected vs actual; restoring returned
  green). Goldens are never written by test code; promotion is manual. Gate 420 -> 438.
  Scope limit: 18 option combinations, not the whole option space, and it says NOTHING about whether
  Diablo 4 accepts any payload.
- **IN FLIGHT 2026-09-17 22:15 — astra building PART B item B1**, the payload-derived preview.
  It must decode the emitted `ImportCode` and show each rule's action, real scope, and encoded
  counts, so what the UI claims can no longer diverge from what shipped. Write set:
  `FilterPreview.cs` (new), `MainViewModel.cs`, `MainWindow.xaml`, `WitnessCardViewModel.cs`,
  `FilterPreviewTests.cs` (new). The 18 payload-hash cases from `1cb9127` are the fence — if a
  single emitted byte moves they go red and the ticket fails. astra cannot build in its sandbox,
  so the orchestrator runs the gate. NOT yet reviewed; reviewer must be non-OpenAI (D3 open).
- **HELD for a ruling:** astra item 7 (MainViewModel extraction, MEDIUM) and all of astra
  PART B — 6 elevation proposals, its own top pick being B1, a preview that shows the
  filter's real encoded intent before Copy. Council notes in `..\d4-s15-council\`.

## REPO LAW
One seat, never a fleet. Never push, merge, or tag. The owner's real name, email, and user
path must never touch this repo. Never weaken a test to make a gate green.

## HOUSEKEEPING
Two stale review worktrees from July remain registered under `C:\Sync\Projects\`:
`D4BF-review-wt` (77c3a86) and `D4BF-review-wt2` (31d0878). Both verified CLEAN — no
uncommitted work. Safe to `git worktree remove` whenever the owner wants the space back.
