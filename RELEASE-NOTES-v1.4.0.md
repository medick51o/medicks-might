# MedicK's Might v1.4.0 — Season 15 "Hell's Legacy"

Built against Diablo 4 **Season 15**, game build `3.2.1.73552`.

## ⚠ READ THIS FIRST — what is NOT proven
**No feature of this app has ever been validated inside Diablo 4.** Gates pass; the emitted
payload has never been confirmed to import. That has been true since v1.0.0 and it is still true.

**Do not enable the "900 only" switch yet.** It emits a hide-rule for item power 1-899, ahead of
the keeper rules. Whether item power 900 is attainable in Season 15 is genuinely unresolved —
community sources give 800, 900 and 925, Blizzard's own pages state no number, and the game data
does not store a ceiling (it is computed at drop time). **If 900 is not reachable, that switch
hides every drop in the game.** It ships OFF. One in-game look at any Ancestral item's power
settles it.

## Season 15 data — catalogs refreshed to `3.2.1.73552`
The returning legacy uniques are now targetable: **Stone of Jordan, The Furnace, In-Geom,
Nemesis Bracers, Squirt's Blouse, Arioc's Needle, Henri's Perquisition, Messerschmidt's Reaver,
Leoric's Crown**, plus The Cow King's Crown, Godly Plate of the Whale and Bell of the Bovine.

**Charms:** 37 added, including **Annihilus** and **Hellfire Torch** — standalone charms in the
`HoradricSeal` slot with no paired gear item.

**The Mythic Horadric Seals are in:** Seal of the Severed Finger, Seal of the Golden Epiphany,
Seal of the Diamond Mind. These carry the game's own `Talisman_Charm_Slot_Count_Base = 6`
attribute — they are the items behind the 6-charm-slot capacity.

Refresh was **purely additive**: no catalog entry was removed, so no filter you already saved can
break. Five ids that no longer exist in current game data were deliberately KEPT for that reason.

## Your build sites work again
Both Mobalytics scrapers were broken and nobody noticed, because the live canaries are gated
behind `RUN_CANARY=1` and had gone unrun.
- **403 across the whole mobalytics.gg domain** — the site now requires Chromium `sec-ch-ua`
  client-hint headers. Fixed on both fetch paths.
- **73 endgame builds were parsing as 4.** A non-nesting regex isolated tier entries with
  `"ugDataItems":\[[^\]]*\]`; Season 15 added a nested `tags` array to each entry, so the pattern
  stopped at the first `]` inside item 1. Replaced with real JSON parsing of the page's embedded
  state, which cannot mis-balance brackets.
- Now parsing **68 endgame / 24 leveling / 47 pushing builds, all 8 classes on every list.**
Maxroll and D4Builds were unaffected throughout.

## See what your filter actually does, before you paste it
The result panel used to show a fixed legend and "Hidden: everything else" no matter what
compiled. It now **decodes the payload you are about to copy** and shows each rule in emission
order — Show or Hide, its real scope, encoded affix counts and thresholds, and any conditional
protections. If a setting would hide more than you meant, the app says so on screen.

## Under the hood
- A **byte-identity harness**: 18 characterization cases pin the SHA-256 of emitted payloads, so a
  refactor that changes output is caught immediately. Every change in this release was verified
  against it — adding 65 catalog entries moved zero bytes.
- An **offline drift guard** for the tier-list parsers, so site changes are caught by the normal
  test run instead of only by a network canary nobody remembers to enable.
- A **repeatable data extractor** (`tools/s15-extract/`), pinned and cached, with 22 self-checks
  against independently known ids. Previous season refreshes were ad-hoc.
- Documentation corrected where it lied: tier defaults, `RoundTripOk`'s real (narrow) scope, and
  `GameDataUpdater`'s false promise of a paired install.

**Gate: 499 passed / 0 failed / 5 skipped. Live scraper canaries: 6/6.**
Gates passing is not the same as it working in game.
