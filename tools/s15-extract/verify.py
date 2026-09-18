#!/usr/bin/env python3
"""
Acceptance check for extract.py's output, run against build 3.2.1.73552 data pulled
from the pinned commit. Asserts the extractor reproduces IDs already independently
known correct from the pinned 3.1.0.72592 catalogs (per the ticket). Prints PASS/FAIL
per line. Does not touch D4BuildFilter.
"""
import json
import os
import sys

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))

uniques = json.load(open(os.path.join(SCRIPT_DIR, "uniques.json"), encoding="utf-8"))
charms = json.load(open(os.path.join(SCRIPT_DIR, "unique_charms.json"), encoding="utf-8"))
mythics = json.load(open(os.path.join(SCRIPT_DIR, "mythics.json"), encoding="utf-8"))
mythic_charms = json.load(open(os.path.join(SCRIPT_DIR, "mythic_charms.json"), encoding="utf-8"))
sets_ = json.load(open(os.path.join(SCRIPT_DIR, "talisman_sets.json"), encoding="utf-8"))

by_hex_unique = {u["snoID_hex"]: u for u in uniques}
by_hex_charm = {c["snoID_hex"]: c for c in charms}
by_hex_mythic = {m["snoID_hex"]: m for m in mythics}
by_hex_mythic_charm = {m["snoID_hex"]: m for m in mythic_charms}
# NOTE: display names are not guaranteed unique (e.g. seasonal-quest transmog armor
# pieces reuse a unique's display string), so this must be a multimap, not a
# last-write-wins dict.
by_name_unique = {}
for u in uniques:
    if u["displayName"]:
        by_name_unique.setdefault(u["displayName"], []).append(u)
sets_by_codename = {s["codename"]: s for s in sets_}

results = []


def check(label, cond):
    results.append((label, bool(cond)))
    print(("PASS" if cond else "FAIL") + " — " + label)


# --- Banished Lord's Talisman: gear + charm, distinct, correctly paired ---
gear = by_hex_unique.get("0x17d2dc")
charm = by_hex_charm.get("0x276db6")
check(
    "Banished Lord's Talisman gear id 0x17d2dc present with that display name",
    gear is not None and gear["displayName"] == "Banished Lord's Talisman",
)
check(
    "Banished Lord's Talisman charm id 0x276db6 present with that display name",
    charm is not None and charm["displayName"] == "Banished Lord's Talisman",
)
check(
    "gear (0x17d2dc) and charm (0x276db6) are two distinct entries",
    gear is not None and charm is not None and gear["snoID_hex"] != charm["snoID_hex"],
)
check(
    "charm's gearCodename correctly points back at the gear item's codename",
    charm is not None and gear is not None and charm.get("gearCodename") == gear.get("codename"),
)

# --- Talisman_Barb_01 set id ---
barb01 = sets_by_codename.get("Talisman_Barb_01")
check(
    "Talisman_Barb_01 set snoID = 0x22fb15 (decimal 2292501)",
    barb01 is not None and barb01["snoID"] == 2292501 and barb01["snoID_hex"] == "0x22fb15",
)

# --- Talisman_Barb_01 five members ---
expected_members = {"0x25069a", "0x2506a8", "0x2506b5", "0x2506b8", "0x2506cd"}
actual_members = set(m["snoID_hex"] for m in barb01["members"]) if barb01 else set()
check(
    f"Talisman_Barb_01 has exactly its 5 known members {sorted(expected_members)}",
    actual_members == expected_members,
)

# --- Already-present uniques resolve to known ids ---
known = {
    "Gospel of the Devotee": "0x2410af",
    "Ae'grom's Schism": "0x26e5d3",
    "Eye of Baal": "0x26e5f0",
    "Mace of King Leoric": "0x25aff6",
}
for name, hexid in known.items():
    candidates = by_name_unique.get(name, [])
    check(
        f'"{name}" resolves to {hexid}',
        any(u["snoID_hex"] == hexid for u in candidates),
    )

# --- Exactly 45 real sets after excluding junk ---
check("exactly 45 real talisman sets (junk 'Axe Bad Data' excluded)", len(sets_) == 45)

# --- Mythic tier (eMagicType==4): the 11 ids already present in UniqueDatabase.cs's
# ByName dict, mixed in alongside Uniques. Per the coordinator: if any of these is
# absent from the pinned build's data entirely, that's a significant finding (a
# shipped id the current game no longer has) and must be reported loudly, not
# silently omitted. Since UniqueDatabase.cs mixes Mythic tier into the same table as
# Unique tier, "present in your output" means present in uniques.json+mythics.json
# combined — a hit in uniques.json instead of mythics.json is flagged as a tier
# note, not a failure, since the id and name both resolve correctly either way.
known_mythics = {
    "Doombringer": "0x35f59",
    "The Grandfather": "0x36827",
    "Andariel's Visage": "0x3b10a",
    "Ahavarion, Spear of Lycander": "0x57afd",
    "Harlequin Crest": "0x94e1c",
    "Melted Heart of Selig": "0x13781f",
    "Ring of Starless Skies": "0x13eee2",
    "Tyrael's Might": "0x1d03ac",
    "Shroud of Khanduras": "0x1d8465",
    "Doombringer (Crucible)": "0x27b52f",
    "The Grandfather (Crucible)": "0x27b547",
}
print()
print("-- Mythic-tier check (11 ids from UniqueDatabase.cs) --")
for name, hexid in known_mythics.items():
    in_mythics = by_hex_mythic.get(hexid)
    in_uniques = by_hex_unique.get(hexid)
    if in_mythics is not None:
        check(f'"{name}" ({hexid}) present in mythics.json with matching name',
              in_mythics["displayName"] == name)
    elif in_uniques is not None:
        check(f'"{name}" ({hexid}) present — NOTE: found in uniques.json '
              f'(eMagicType 2, Unique tier) not mythics.json (tier differs from '
              f'UniqueDatabase.cs grouping, id+name both correct)',
              in_uniques["displayName"] == name)
    else:
        check(f'"{name}" ({hexid}) present anywhere in output '
              f'(*** NOT FOUND IN 3.2.1.73552 DATA AT ALL ***)', False)

print()
n_pass = sum(1 for _, ok in results if ok)
n_fail = len(results) - n_pass
print(f"{n_pass}/{len(results)} checks passed, {n_fail} failed")

if n_fail:
    sys.exit(1)
