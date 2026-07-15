# SUPER BUILD — multi-build fusion (spec)
**Author:** Medick · **Written:** 2026-07-13 · **Status:** APPROVED, not yet built

## The problem, in Medick's words

> "I want to play multiple builds in a season but I'm gated to play another build starter, because what
> I really want to play is beyond being a starter build. I would like to see loot for two totally
> different builds — not just variants from a particular content creator."

Today the app filters for **one** build (or two, via Armory Mode). A season player respecs, rerolls, or
plays several builds — and their loot filter only serves one of them. Everything the other builds want
falls dark on the ground.

## What we're building

At the **launch screen**, the player ticks **up to 4 builds** from the tier lists and compiles them into a
single **Super Build** filter. Every selected build's chase loot lights up — in one shared color.

---

## THE LOAD-BEARING DECISION (judge-approved 2026-07-13)

**One shared color. One rule PER BUILD.** Not one merged affix pool.

| | |
|---|---|
| **Color means** | "one of your builds wants this" — NOT *which* one |
| **Each build gets** | its own rule, counting **only that build's** affixes |
| **An item lights up when** | it has 3+ affixes **from a single build** |
| **New colors needed** | **ZERO** |

**Why not merge the affix pools into one list?** Because it lies. An item with 2 Heartseeker affixes +
1 Death Trap affix would count as "3+ from the pool" and light up as chase loot — while being
**mediocre for both builds**. A per-build rule cannot produce that false positive. This is the whole
design and it is not negotiable without Medick's ruling.

**Bonus: this fixes a live bug.** Armory Mode today paints build B's chase loot `FilterColors.Gold`
(`MainViewModel.cs`, `Analyze(_secondResolved, FilterColors.Gold, FilterColors.Silver)`) — **the same
color as Cube Bases** (`FilterCompiler.cs:439`), and build B's keepers `Silver`, the same as the Leveling
tier (`:387`). Nobody caught it because nobody has used Armory yet. Super Build replaces that
per-build-color scheme entirely: **every build shares Red (chase) / Pink (keeper).**

---

## THE HARD CONSTRAINT — the 25-rule cap

Diablo 4 **rejects** any filter over 25 rules on import. A single endgame build with per-slot precision
already compiles to **25 / 25**. There is no headroom, so the budget is the feature.

- **~10 rules are global**: item-power tiers (×2), greater affixes, charms (×2), cube bases, codex,
  build uniques, hide-uniques, hide-the-rest.
- **~2 rules per build**: chase tier + keeper tier. (Identical scopes across builds already dedupe to one
  rule — `emittedTierScopes`, `FilterCompiler.cs:354-366`. Keep that.)

**Four builds will not fit at today's default options.** The app must therefore:

1. **Auto-fit, in a fixed and documented order.** Existing behavior drops per-slot precision first
   (`CompileWithinCap`). Extend the ladder; the order must be deterministic and stated in the code.
2. **NEVER silently truncate.** If something was turned off to fit, the UI says so in plain words:
   *"4 builds needs 31 rules; the 25-rule cap forced per-slot precision off and the keeper (Pink) tier
   off. Deselect a build to get them back."*
3. **Never drop a build silently.** If even the minimum for N builds cannot fit, **refuse to compile**
   and say which build to drop. Fail closed — the existing `CopySafety` gate already blocks unsafe
   output; this joins it.

A player who imports a filter believing it covers four builds, when it covers three, has been lied to.
That is worse than not shipping the feature.

---

## SCOPE — Phase 1 (build this now)

### 1. Multi-select on the launch screen
- Every build card in the Maxroll / D4Builds / Mobalytics tier lists gets a **checkbox**.
- Selecting **1** build → today's behavior, unchanged. This is the common case and must not regress.
- Selecting **2–4** → a **"Compile N builds"** action appears.
- **Hard cap: 4.** The 5th tick is refused with a reason, not silently ignored.
- Single-click on a card keeps working exactly as it does today (load one build). The checkbox is
  additive; **do not break the existing one-click flow.**

### 2. Fusion
- Each selected build is `Analyze`d with **the same color pair** (Red chase / Pink keeper).
- Each build emits its **own** tier rules over **its own** affix pool. Shared scopes still dedupe.
- Purple build-uniques: **union** across all builds (already the Armory behavior — keep it).
- Charm sets: offered for **every class** among the selected builds (already the Armory behavior).
- Cross-class fusion is **allowed** (you may be playing two characters), and cross-source is allowed.
  Both are **advised against** — see Advisories.

### 3. The rule budget, made visible
- Show **"N / 25 rules"** live, as it does today, and what it cost: *"3 builds · 21 / 25 rules."*
- When auto-fit fires, name **exactly what was turned off**, in the result-page warning band.
- When it cannot fit, block copy AND block the share card, and **name the build to drop**.

### 4. Advisories (warn, never block)
- **> 3 builds:** *"4 builds is a lot for a 25-rule filter — expect to lose per-slot precision. 2–3 is
  the sweet spot."*
- **Mixed sources:** *"These builds come from different sites. Maxroll and Mobalytics describe builds
  differently, so their affix lists may not be directly comparable — sticking to one source usually
  gives a tighter filter."*
- **Mixed classes:** *"You've selected builds from different classes. That's fine if you're playing both
  characters — otherwise you're filtering for loot you can't use."*

## SCOPE — Phase 2 (spec'd now, built after Medick plays Phase 1)

### 5. Loadouts — "save this build in this moment"
> "I want to take a snapshot of this build in this moment, and save it in this state, and put it in a
> saved area or favorites."

Save the **entire compile state** as a named Loadout in Favorites: the build(s), the **selected variants**
(the exact tick-state, e.g. *only* Spin 2 Win GoD), the filter options, the charm-set choices, the unique
selections. Re-opening a Loadout restores that state **exactly** — the same import code comes out.

`BuildSnapshot` already captures a build's filter-relevant content (and, since 2026-07-13, its name and
class). A Loadout is that plus the **user's choices**. Reuse `FavoritesStore`; do not invent a second store.

### 6. Fuse from Favorites
Select 2–4 saved Loadouts → compile them as a Super Build. Same fusion rules as Phase 1.

---

## OUT OF SCOPE — do not build
- New colors, or reassigning any existing color's meaning. The palette is full and every color's meaning
  is load-bearing.
- Merging affix pools (see THE LOAD-BEARING DECISION).
- Any change to the wire format / import-code encoding. It is in-game-validated and must not move.
- Raising `MaxRules`. 25 is Diablo 4's limit, not ours.

## DONE WHEN
- `dotnet test D4BuildFilter.slnx` green; baseline **272 passed / 0 failed / 5 skipped**, and it does not
  go down.
- A test proves the **false-positive** case: an item carrying affixes spread across two builds, none of
  which alone reaches the threshold, **does not** light up.
- A test proves the **rule budget is honest**: an over-cap selection either auto-fits with a stated
  reason, or refuses to compile and names a build to drop — never silently truncates.
- Single-build compile is **byte-identical** to today's output. Pinned by a test.
- **Medick imports a Super Build into Diablo 4 and sees loot for all his builds light up.** No test can
  prove this. It is the only gate that counts.

---

## v1.2.0 layout — 2026-07-13 (supersedes the Phase 1 color/rule-budget layout above)

### Honest gear pools

Maxroll explicit stats from slotless items (charms, talismans, seals, and slotless uniques) do not enter
the gear affix pool. Charm loot remains covered by its dedicated rules. If a stat appears on both a
slotless item and genuine gear, it remains wanted and is folded into every resolved gear slot pool.
Unresolved source slot labels still force the wholesale combined fallback and a plain warning; per-slot
rules must never claim complete coverage when a source label cannot be resolved.

### Fixed Super Build rule layout

Super Builds are combined-pool, endgame-only tiers: exactly one 3+ legendary rule and one separate 3+
rare rule per selected build. Rarity masks are never merged. Item Power 900+ and 850+ are on by default
for multi-build compiles. Build uniques remain one shared purple union rule.

A normal two-build compile with build uniques has 13 rules, in this order:

1. Build Uniques (Purple)
2. Codex Upgrades (White)
3. Build A legendary 3+ (Red)
4. Build B legendary 3+ (Pink)
5. Build A rare 3+ (Gold)
6. Build B rare 3+ (Silver)
7. Item Power 900+ (Orange)
8. Item Power 850+ (Cyan)
9. Greater Affixes (Blue)
10. Charms & Seals ancestral (Red)
11. Charms & Seals (Green)
12. Unique Charms
13. Hide the rest

For three or four builds, colors switch to **colors by tier**: every legendary build rule is Red and
every rare build rule is Gold. Each extra build costs two rules, so the normal shapes are 15 and 17.
The hard selection cap remains four; a fifth build is refused. The result legend and Witness Card state
whether **colors by build** (two builds) or **colors by tier** (three or four) is active.

Cube Bases warns when Gold is already a two-build build color. Leveling warns when Silver is already a
two-build build color; Super Builds remain 3+ endgame filters and do not add the 2+ Leveling tier.

### Named multi-build tier rules

Every multi-build tier rule is `{Tag} Leg ({Color})` or `{Tag} Rare ({Color})`, with the existing
24-character clamp. Tags have a 10-character budget. Build/class/source identity is normalized by
removing product noise, punctuation, and class names. A cleaned distinctive name that fits is kept
verbatim. Only longer names abbreviate: a long single word uses a consonant skeleton capped at seven
(`Heartseeker` → `HrtSkr`), while a long multi-word name uses initials with readable connectors
(`Dance of Knives` → `DoK`).

Collision resolution sees only the selected two to four builds: append class initial first, then a
source tag (`Mx`, `D4`, or `Mb`) only when the same build from two sites is genuinely selected. Hash
suffixes are forbidden. The small embedded curated override map wins before the algorithm and is
reviewed whenever seasonal tier-list data is updated. The corpus receipt runner calls the shipping
tagger so future maintenance checks cannot drift into a second naming implementation.

### v1.2.3 correction — 2026-07-13

The result-page variant picker is grouped in selected-build order. Every group is headed by its rule
tag and build name, and every variant controls only that build's affix pool and emitted rules. The
`VariantOption` default applies independently to every group: names containing `Leveling` begin
unchecked. Clearing the last variant in any group retires the import code and names that build in the
fail-closed message; a zero-variant build is never silently compiled.

The same selected-build order drives variant groups, prefixed affix rows, and rule emission. Variant
narrowing uses temporary build copies. A favorite's `BuildSnapshot` remains the full freshly resolved
source baseline and advances only after a copyable compile, never from a failed or narrowed payload.

An active tier-card selection changes card-click semantics: clicking another card adds or removes it
from that selection, and only the explicit `Compile N builds` action may compile while any card remains
ticked. Selection identity is URL-based and survives tier-tab projections and forced list refreshes.
Every successful result status begins with its explicit count (`1 build · …` or `N builds · …`).

Single-build compilation is outside this layout and remains byte-identical to the pinned pre-Super-
Build output. The Diablo wire format, palette, 25-rule limit, and final in-game validation gate do not
change.

## v1.3 customization

2026-07-14: Segment 1's append-only color palette registry is specified in
[SUPERBUILD_V13_SPEC.md](SUPERBUILD_V13_SPEC.md). Later v1.3 segments remain separately gated.
