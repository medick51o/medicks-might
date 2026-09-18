# Game-data extractor (d4data -> catalog JSON)

Regenerates the raw data behind `UniqueDatabase.cs`, `UniqueCharmDatabase.cs` and
`TalismanSetDatabase.cs`. Built 2026-09-17 for game build `3.2.1.73552`. **Re-run this each
season instead of hand-editing a catalog.**

## Run it
```
python extract.py      # writes uniques.json, unique_charms.json, mythics.json,
                       # mythic_charms.json, talisman_sets.json, MANIFEST.json
python verify.py       # 22 assertions against independently-known-correct ids
```
`verify.py` must print **22/22 passed** before anything downstream trusts the output. It asserts
ids that were established independently of this script — the Banished Lord's gear/charm pair,
`Talisman_Barb_01` plus all five members, four known uniques, the set count, and all 11 mythic ids
present in the shipped catalog. **If an assertion fails, that is a finding. Never edit an expected
value to make it pass.**

## How it works, and why it is built this way
- **Pinned** to commit `961fe61288f8a03d16d5b08d03d5ece7e13184fb` of `DiabloTools/d4data`
  (the live successor to the long-archived `blizzhackers/d4data`). Never fetch a moving ref —
  reproducibility is the point. Bump the constant deliberately when moving to a new build.
- **Never clones.** That repo is ~9.7 GB. It fetches individual files over
  `raw.githubusercontent.com`, which does not count against the API rate limit.
- **Fully cached and idempotent.** A re-run costs ~3s and zero network. A partial run resumes.
- **Classifies on the game's own `eMagicType`** (2 = Unique, 3 = Set-charm, 4 = Mythic), never on
  filename patterns. That found 118 more true uniques than pattern-matching would have.
- **Tiers stay in separate files on purpose.** The consumer decides the union explicitly, because
  `UniqueDatabase.cs` mixes tier 2 and tier 4 and a silent merge would hide that.
- **Never invents an id.** An unresolvable display name is emitted as `null` and counted in
  `MANIFEST.json`.

## Traps this script already survived — do not re-learn them
- GitHub Trees API: **never pass `?recursive=0`.** The value is truthy, it recurses everything and
  truncates at ~60k entries. Omit the parameter and drill down tree by tree.
- Items carry `__snoID__` (which IS the loot-filter type-8 id) but **no display name**. Names come
  from a StringList join — the single hardest part of this job.
- **Gear and charm are separate items with separate ids.** Never merge by name.
- Charm codenames are NOT all `Talisman_Charm_Unique_*`. Season 15 added `S15_Charm_Unique_*`
  (Annihilus, Hellfire Torch) in the `HoradricSeal` slot. The match is the substring
  `_Charm_Unique_`, verified against all 12,117 codenames.
- Annihilus and Hellfire Torch are **standalone charms with no paired gear item** — their
  `gearCodename` is `null`, which is correct, not missing data.
- `SetItemBonus` contains one junk entry, `Axe Bad Data`. 45 real sets, 46 files.
- QA/test placeholders are deliberately NOT filtered from the output. The script stays literal to
  `eMagicType`, so no real id is ever dropped by a heuristic. Filter downstream, visibly.
