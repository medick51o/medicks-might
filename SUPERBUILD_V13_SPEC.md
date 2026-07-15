# SUPER BUILD v1.3 — customization leg (spec)
**Author:** Medick · **Written:** 2026-07-14 · **Status:** DRAFT — pending adversarial
spec review (Fable seat), then built in FIVE SEGMENTS, each gated separately. Medick: "work on this in
small segments not to slop it up."

Baseline: v1.2.3 (branch codex/safety-hardening, suite 322/0/5). Everything here is ADDITIVE to the
ruled v1.2.x layout; single-build behavior stays byte-identical (SHA pin remains law).

## SEGMENT 1 — The palette (Core only; ships first because everything else leans on it)

Colors are raw RGB (`FilterColors.Make(r,g,b)`, D4Filter.cs:39) — arbitrary colors are expressible and
Pink (#FF69B4) is precedent: invented by the crew, validated in-game. Add NAMED palette entries:

| Full name | Short label (rule names) | Suggested RGB |
|---|---|---|
| Turquoise | Turq | #40E0D0 |
| Light Green | LtGrn | #90EE90 |
| Light Blue | LtBlu | #ADD8E6 |
| Sky Blue | SkyBlu | #87CEEB |
| Neon Blue | NeonBl | #1F51FF |
| Light Purple | LtPurp | #C89EF0 |
| Light Orange | LtOrng | #FFB347 |
| Black | Black | #101010 (NOT #000000 — pure black vanishes on dark ground; see legibility) |

HARD CONSTRAINTS:
- Every palette entry has BOTH a full name (legend, dropdowns) and a SHORT label ≤6 chars (rule-name
  suffix). The 24-char rule name budget assumes suffix ≤ 8 incl parens — "(SkyBlu)" ok, "(Turquoise)"
  NOT. `NameOf` (D4Filter.cs:57) currently returns "" for unknown colors, which would silently emit
  rules named "Tag Leg ()" — replace with a palette registry lookup; an UNREGISTERED color must fail
  loudly at compile time (exception in debug / diagnostic in release), never emit a nameless rule.
- LEGIBILITY: each entry carries a `DarkGroundRisk` flag (Black = true, dark blues borderline). Choosing
  a flagged color warns: "this color is hard to see on Sanctuary's ground — you may miss drops." Warn,
  never block. (An invisible beam is self-inflicted loot-hiding; the app must say so once.)
- The palette is data (like BuildTagOverrides.json): a shipped, versioned list — seasonal additions ride
  the normal data update. Distinct RGB values only; no two entries may share a value or a short label.
- The registry covers ALL colors — the 10 existing (Red, Pink, Gold, Silver, Orange, Cyan, Blue, Green,
  White, Purple) plus the 8 new. Palette entries are APPEND-ONLY: an entry may be hidden from the
  pickers in a future season but its registration never leaves, so old Loadouts/codes always resolve a
  name (Fable M-finding: never weaponize the loud-failure rule against saved data).
- Release-mode behavior for an unregistered color (should be unreachable): emit the rule with the
  nearest registered color's name is WRONG — instead fall back to `(Custom)` as the suffix and surface
  one advisory; debug builds throw. Never a nameless "()" rule, never a blocked compile over a name.
- Tests: NameOf/registry round-trip for every entry (all 18); the unregistered-color fallback; short-label
  length bound pinned (≤6, suffix ≤8 incl parens); no duplicate values/labels.

## SEGMENT 2 — Title prepopulation + unique attribution (display only; no rule changes)

- Multi-build filter title defaults to the build TAGS joined " + " in PRIORITY order (Segment 4; until
  S4 lands, selection order): "Death Trap + HrtSkr". Clamp to MaxTitleLength (24): the priority-1 tag is
  never truncated; if the next whole tag + " +N" does not fit, DROP the tag (never truncate a tag
  mid-word) and fold it into the "+N" count — worst case "P1sTag +3". User edits still win (title
  remains editable; prepopulation only when the user has not typed a custom title — track dirty state;
  dirty state RESETS on Restart / "Make another filter" / loading a different selection).
- Build Uniques panel: every line shows owner tags — "Scoundrel's Kiss (HrtSkr)". An item in multiple
  builds shows ONCE with all owners in priority order: "Scoundrel's Kiss (Death Trap, HrtSkr)". Scales
  to 4. The purple RULE is untouched (still one union rule); this is display only. Uniques hide-list
  interactions unchanged.
- Tests: title default (2/3/4 builds), clamp behavior, dirty-title preservation; attribution for
  single-owner, multi-owner, and 4-build cases.

## SEGMENT 3 — Charm re-check button (small; UX confidence)

- A button in the Charm sets header: label "Re-check from current variants". Tooltip/inline note, per
  Medick verbatim: it "pulls from the currently highlighted variants that are open in the menu on
  screen at the moment" — i.e., re-derives the charm-set defaults from the variants currently CHECKED
  in the Variants section, discarding manual charm narrowing (the same derivation a build-context
  change performs; the auto-refire stays — this is an explicit manual redo).
- Tests: manual narrowing + button ⇒ defaults re-derived from current variant selection (RED-first by
  asserting on option instances, the established NotSame discipline); button respects fail-open rules
  (undetected sets ⇒ all shown).

## SEGMENT 4 — Priority 1-4 (ordering backbone; ships before colors because colors display in its order)

- Each selected build carries a priority 1..4: auto-assigned in tick order, EDITABLE via dropdown on the
  selected card (and/or the result page group header — implementer's call, stated). No duplicates; a
  swap re-numbers deterministically.
- Priority drives: rule emission order, title tag order (S2), variant group order, unique attribution
  order, Witness Card build order, and refusal naming (BuildToDrop = lowest priority, replacing "last
  selected"). **The degradation clause is DELETED (Fable B3): multi-build has no auto-fit ladder — the
  ruled layout is fixed, tiers are compiler-pinned, and builds are never silently removed. Over-cap
  multi-build goes straight to honest refusal, which now names the LOWEST-PRIORITY build.** Stripping a
  low-priority build's tiers while its picker/legend still showed them would be masking instance #8.
- Priority 1 is the protected build: it is never the one named to drop while any lower-priority build
  remains.
- Reordering priorities on a LIVE result page recompiles as a build-context change; variant tick state
  is PRESERVED across the reorder (carried by build identity, re-applied by variant name), charm-set
  defaults re-derive per the established rule. Armory's add-a-build flows enter this same path: an
  Armory-added build takes the next free priority.
- Editing semantics: assigning an occupied priority SWAPS the two builds (deterministic, no duplicates).
- Tests: ordering in emitted rules; refusal names lowest priority (RED-first against current
  last-selected behavior); swap semantics + no-duplicate invariant; variant ticks survive reorder;
  determinism across recompiles.

## SEGMENT 5 — Per-build color pickers (the feature; last because it stands on S1+S4)

- In the result page legend area: per build, TWO dropdowns (Chase color / Keeper color) listing the full
  named palette with swatches.
- DEFAULTS = the ruled scheme EXACTLY AS PINNED (corrected 2026-07-14 after Fable spec review B1 — the
  draft's first wording accidentally described the dead Armory pairing): **build 1 = Red chase + Gold
  keeper; build 2 = Pink chase + Silver keeper** (warm = legendaries, metallic = rares, per Medick's
  line-swap ruling; SUPERBUILD_SPEC.md v1.2.0 section, pinned by V120LayoutTests). Builds 3-4 default to
  colors-by-tier as ruled (all chase Red / all keeper Gold) with tags carrying identity — until the
  player customizes. Defaults produce BYTE-IDENTICAL output to v1.2.3; the pinned layout tests must not
  change for the default path.
- ANTI-NOISE (Medick: "not have the app freak out into a noisey mess") — amended per Fable B2/M1:
  - Collision detection generalizes the Gold/CubeBases guard, **but ONLY for user-chosen crossings.
    Default-scheme sharing NEVER warns** — at 3-4 builds the ruled default shares Red/Gold across builds
    BY DESIGN, and warning on the app's own defaults is a warning wall with zero user action (B2).
    A user-chosen color that collides with any active meaning warns plainly once: "X and Y now share
    <color> — you won't be able to tell them apart." Warn, never block.
  - Same-build chase == keeper: warn (one build's tiers indistinguishable).
  - DarkGroundRisk warning from S1: once per color per compile session.
  - **WARNING BAND PLACEMENT (M1, load-bearing):** all S5 color warnings and the S1 legibility warning
    are ADVISORY — they live in the SuperBuildAdvisory band, NEVER in CapWarning and NEVER as compiler
    diagnostics. Rationale: any active warning/diagnostic blocks the Witness Card and occupies the
    card's single warning slot, so a cosmetic color note could mask the drift alarm or block sharing —
    masking instance #8. Cosmetic warnings must be structurally incapable of blocking copy/share or
    displacing safety warnings. Pin with a test.
  - The stale hardcoded 2-build Gold/CubeBases + Silver/Leveling diagnostics are SUBSUMED by the
    generalized collision system (state this in the diff; remove the old special case; keep its tests
    green by making them express the new mechanism).
  - CUSTOM-COLOR IDENTITY: choices key on build identity (source + URL), not on position — they survive
    priority swaps, and are discarded when that build is deselected/dropped or on Restart / "Make
    another filter". Crossing 2↔3 builds keeps per-build custom choices; only DEFAULTS differ by count.
    (Loadout persistence lands with Phase 2; out of scope here, stated.)
  - The Witness Card and legend must render CUSTOM colors truthfully: the card's legend rows and
    provenance/scheme line reflect the actual chosen colors and say "custom colors" when any build
    deviates from the ruled scheme (the hardcoded two scheme names gain a third state). Card build
    attribution must survive custom colors (tier legend rows keep their build tags).
- Legend, Witness Card, and rule-name suffixes all reflect the chosen colors live (short labels in rule
  names; full names in legend/card). The card states the active scheme as it does today.
- Tests: defaults pinned (first two builds keep ruled scheme RED-first); custom choice flows to rules +
  legend + card + suffix; every collision warning fires; suffix stays ≤8 incl parens for every palette
  entry.

## OUT OF SCOPE (this leg)
- Free RGB wheel input (palette presets only — "customizable without slop"; the palette itself is the
  extension point and grows as data).
- Reassigning GLOBAL rule colors (charms/GA/IP/uniques/codex stay as they are this leg).
- Wire-format changes, MaxRules changes, single-build changes of any kind.

## PROCESS
Segments build in order, one dispatch each, gates + red-first teeth verified per segment before the
next starts. Cold review after S1+S2+S3 (small batch) and again after S4+S5 (the behavioral batch).
Version bump to v1.3.0 by the referee when the leg completes. SUPERBUILD_SPEC.md gains a dated pointer
to this file when S1 lands.
