# Season 15 data refresh — reconnaissance
Recorded 2026-09-17. Everything marked CONFIRMED was established by fetching and reading the
actual game-data files, not by reading about them. Do not re-derive this.

## THE SOURCE
`github.com/DiabloTools/d4data` — the live successor to `blizzhackers/d4data`, which was
**archived 2024-07-04** ("Development halted; use the linked repo instead"). Anything claiming
no S15 dump exists is reading the archive.

- Build **`3.2.1.73552`**, published **2026-09-15** (the day Season 15 launched).
- Pin this commit: **`961fe61288f8a03d16d5b08d03d5ece7e13184fb`**.
- **Repo is ~9.7 GB (10,170,603 KB). Do NOT clone it.** CONFIRMED.
- Individual files fetch cleanly, no clone needed, and don't count against the API rate limit:
  `https://raw.githubusercontent.com/DiabloTools/d4data/961fe61288f8a03d16d5b08d03d5ece7e13184fb/json/<path>`
- Directory discovery via the Trees API:
  `curl https://api.github.com/repos/DiabloTools/d4data/git/trees/<sha>`
  **CONFIRMED GOTCHA: do not pass `?recursive=0`.** GitHub treats the value as truthy, recurses
  the whole tree, and truncates at ~60k entries. Omit the parameter entirely, then drill down
  tree by tree. Unauthenticated API limit is 60/hr; raw fetches are free.

## WHERE EACH CATALOG'S SOURCE DATA LIVES (all CONFIRMED by direct fetch)
| Catalog | Source path | Shape |
|---|---|---|
| `UniqueDatabase.cs`, `UniqueCharmDatabase.cs` | `json/base/meta/Item/*.itm.json` — **12,117 files**, one per item | `__snoID__` (int; this IS the filter type-8 id) and `__fileName__` (internal codename). **No display-name field.** |
| `TalismanSetDatabase.cs` | `json/base/meta/SetItemBonus/*.set.json` — **46 files (45 real + 1 "Axe Bad Data" junk)** | `__snoID__` = set id; `ptTiers[].nRequired`; `snoAffix.__raw__`/`name` pointing into `base/meta/Affix/Talisman_SetPower_*.aff.json`. **Member charms are NOT listed here** — they are separate `Talisman_Charm_Set_<Class>_<Tier>_0N.itm.json` files. |
| `AffixDatabase.cs` | `json/base/meta/Affix/*.aff.json` | referenced from the set and item files |

**The gear-vs-charm split is structural and CONFIRMED:** `Amulet_Unique_Barb_102_x2.itm.json`
(gear) versus `Talisman_Charm_Unique_Amulet_Unique_Barb_102_x2.itm.json` (charm). The charm's
filename literally embeds the gear filename, and each carries its own `__snoID__`. This is the
same pattern as "Banished Lord's Talisman" (gear `0x17d2dc`, charm `0x276db6`). **Never merge by
name — you will target the wrong form.**

## ✅ THE BIG DE-RISK: talisman set IDs did NOT change
**CONFIRMED by exact hex match against the pinned catalog.** `Talisman_Barb_01.set.json`
`__snoID__` = 2292501 = **`0x22fb15`** — identical to the value pinned from `3.1.0.72592`. All five
members (`Talisman_Charm_Set_Barb_01_01..05`) resolve to `0x25069a / 0x2506a8 / 0x2506b5 /
0x2506b8 / 0x2506cd` — **every one an exact match.** The filename set is the same 45 sets.

**So set IDs and set membership are UNCHANGED in `3.2.1.73552`. Only the `.aff.json` bonus VALUES
could differ.** The "stale talisman IDs are silently hiding loot" risk — previously ranked the most
dangerous silent failure in this codebase — **is not present.** This is corroborated for one set
exhaustively and consistent with the identical filename set across the other 44; a full sweep is
extractor work.

## ⛔ ITEM POWER IS NOT DERIVABLE FROM GAME DATA
The 800 / 900 / 925 dispute **cannot be settled from the dump.** Files checked and read:
`PowerFormulaTables.gam.json` (DmgTier1-7 curves), `DifficultyTiers.gam.json`,
`MonsterLevelCurves.gam.json`, `QualityClasses.gam.json`, `ItemFamilies.gam.json`,
`ItemSalvageLevels.gam.json`, `LootDistribution.gam.json`. **No `ItemPowerMax`, no stored ceiling
constant.** Item power is computed at drop time from those curves; many fields are hashed
(`unk_xxxxx`) in a decompiled dump.

**Four independent attempts have now failed to settle this:** two vendor seats (which contradicted
each other while citing the same site), one independent research seat (found a three-way split and
no official Blizzard text), and the game data itself. **It is settleable ONLY by looking at an item
in game.** Stop spending seats on it.

## THE SEASON 15 UNIQUE DELTA — partially open
- `Season 15.sea.json` (86 KB) was fully enumerated: **no array resembling a legacy-unique
  restoration list.** The season file does not advertise them.
- 4 of the 12 disputed names are ALREADY in `UniqueDatabase.cs`: Gospel of the Devotee `0x2410af`,
  Ae'grom's Schism `0x26e5d3`, Eye of Baal `0x26e5f0`, and **"Mace of King Leoric" `0x25aff6`** —
  note that is a different item from the reported "Leoric's Crown". **Treat the community name
  list as unreliable**; resolve against data, not articles.
- Items are keyed by internal codename (`Unique_Barb_102`), so name -> ID needs a **StringList
  join**: either 61,330 individual files under `enUS_Text/meta/StringList/`, or one ~55 MB
  `CoreTOC_flat.json`.

## EFFORT
**M.** The ID extraction is mechanical once scripted. **The hardest part is the SNO -> display-name
join**, not the extraction. Build it as a repeatable extractor, not a one-off — this recurs every
season.
