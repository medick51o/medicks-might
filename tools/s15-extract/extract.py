#!/usr/bin/env python3
"""
Medick's Might — Season 15 data-refresh extractor.

Pulls the unique/charm/set item catalog for Diablo 4 build 3.2.1.73552 out of the
DiabloTools/d4data GitHub repo, PINNED to a single commit for reproducibility, and
resolves display names via the enUS_Text StringList join.

Re-runnable: every fetched artifact (tree listings and raw json files) is cached to
disk under cache/. A second run reads the cache and does no network I/O beyond a
handful of tree listings it already has. A partial run resumes from whatever is
already cached.

Does NOT touch the D4BuildFilter repo. Everything is written under this script's
own directory.
"""

import json
import os
import sys
import time
import datetime
import concurrent.futures as cf

import requests

# ---------------------------------------------------------------------------
# PINNED SOURCE — never change these to master/main. Re-pin deliberately only.
# ---------------------------------------------------------------------------
REPO = "DiabloTools/d4data"
PIN_SHA = "961fe61288f8a03d16d5b08d03d5ece7e13184fb"
GAME_BUILD = "3.2.1.73552"

RAW_BASE = f"https://raw.githubusercontent.com/{REPO}/{PIN_SHA}/json"
API_TREES = f"https://api.github.com/repos/{REPO}/git/trees"

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
CACHE_DIR = os.path.join(SCRIPT_DIR, "cache")
TREES_DIR = os.path.join(CACHE_DIR, "trees")
ITEMS_DIR = os.path.join(CACHE_DIR, "items")          # json/base/meta/Item/*.itm.json
SETS_DIR = os.path.join(CACHE_DIR, "sets")             # json/base/meta/SetItemBonus/*.set.json
STL_DIR = os.path.join(CACHE_DIR, "stringlist")        # json/enUS_Text/meta/StringList/*.stl.json
OUT_DIR = SCRIPT_DIR

for d in (TREES_DIR, ITEMS_DIR, SETS_DIR, STL_DIR):
    os.makedirs(d, exist_ok=True)

MAX_WORKERS = 8
MAX_RETRIES = 4
BACKOFF_BASE = 1.6
TIMEOUT = 30

_session = requests.Session()
_session.headers.update({"User-Agent": "medicks-might-s15-extract/1.0"})

_stop_for_ratelimit = False


def log(msg):
    print(msg, flush=True)


# ---------------------------------------------------------------------------
# Networking helpers — cached, retried, polite.
# ---------------------------------------------------------------------------

def _get_with_retry(url, headers=None):
    global _stop_for_ratelimit
    last_err = None
    for attempt in range(1, MAX_RETRIES + 1):
        if _stop_for_ratelimit:
            return None
        try:
            r = _session.get(url, headers=headers, timeout=TIMEOUT)
        except requests.RequestException as e:
            last_err = e
            time.sleep(BACKOFF_BASE ** attempt)
            continue

        if r.status_code == 200:
            return r
        if r.status_code == 404:
            return r  # caller decides — a real "not found", not a transient failure
        if r.status_code == 403 and "rate limit" in r.text.lower():
            remaining = r.headers.get("X-RateLimit-Remaining")
            reset = r.headers.get("X-RateLimit-Reset")
            log(f"  !! GitHub API rate limit hit (remaining={remaining}, reset={reset}). "
                f"Stopping cleanly instead of thrashing.")
            _stop_for_ratelimit = True
            return r
        if r.status_code == 429 or r.status_code >= 500:
            last_err = f"HTTP {r.status_code}"
            time.sleep(BACKOFF_BASE ** attempt)
            continue
        # some other 4xx — not retryable
        return r
    log(f"  !! giving up after {MAX_RETRIES} attempts: {url} ({last_err})")
    return None


def fetch_tree(sha):
    """Fetch (or load cached) a git tree listing by sha. Never uses ?recursive."""
    cache_path = os.path.join(TREES_DIR, f"{sha}.json")
    if os.path.exists(cache_path):
        with open(cache_path, encoding="utf-8") as f:
            return json.load(f)
    if _stop_for_ratelimit:
        return None
    url = f"{API_TREES}/{sha}"
    r = _get_with_retry(url, headers={"Accept": "application/vnd.github+json"})
    if r is None or r.status_code != 200:
        return None
    data = r.json()
    if data.get("truncated"):
        log(f"  !! WARNING: tree {sha} reports truncated=true — data is incomplete.")
    tmp = cache_path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(data, f)
    os.replace(tmp, cache_path)
    return data


def walk_tree(path_parts):
    """Walk from the pinned commit's root tree down through path_parts (a list of
    directory names), fetching/caching each intermediate tree. Returns the final
    tree's entry list, or None on failure."""
    sha = PIN_SHA
    tree = fetch_tree(sha)
    if tree is None:
        return None
    for part in path_parts:
        entries = tree.get("tree", [])
        match = next((e for e in entries if e["path"] == part and e["type"] == "tree"), None)
        if match is None:
            log(f"  !! path segment '{part}' not found while walking {'/'.join(path_parts)}")
            return None
        tree = fetch_tree(match["sha"])
        if tree is None:
            return None
    return tree.get("tree", [])


def fetch_raw_json(rel_path, cache_path):
    """Fetch json/<rel_path> from raw.githubusercontent (uncounted against the API
    rate limit), cache the parsed JSON to cache_path. Idempotent: skips if cached."""
    if os.path.exists(cache_path):
        try:
            with open(cache_path, encoding="utf-8") as f:
                return json.load(f)
        except (json.JSONDecodeError, UnicodeDecodeError):
            pass  # corrupt cache entry — refetch
    if _stop_for_ratelimit:
        return None
    url = f"{RAW_BASE}/{rel_path}"
    r = _get_with_retry(url)
    if r is None:
        return None
    if r.status_code == 404:
        return None
    if r.status_code != 200:
        log(f"  !! unexpected status {r.status_code} for {rel_path}")
        return None
    try:
        data = r.json()
    except ValueError:
        log(f"  !! non-JSON response for {rel_path}")
        return None
    tmp = cache_path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(data, f)
    os.replace(tmp, cache_path)
    return data


def fetch_many(jobs, desc):
    """jobs: list of (rel_path, cache_path, key). Returns {key: data or None}."""
    results = {}
    total = len(jobs)
    done = 0
    to_fetch = [(rp, cp, k) for (rp, cp, k) in jobs if not os.path.exists(cp)]
    already_cached = total - len(to_fetch)
    if already_cached:
        log(f"[{desc}] {already_cached}/{total} already cached, fetching remaining {len(to_fetch)}")
    else:
        log(f"[{desc}] fetching {total} files")

    # load the already-cached ones straight from disk
    for rp, cp, k in jobs:
        if os.path.exists(cp):
            try:
                with open(cp, encoding="utf-8") as f:
                    results[k] = json.load(f)
                done += 1
            except (json.JSONDecodeError, UnicodeDecodeError):
                pass

    if not to_fetch:
        log(f"[{desc}] done ({done}/{total}, all from cache)")
        return results

    with cf.ThreadPoolExecutor(max_workers=MAX_WORKERS) as ex:
        future_to_key = {
            ex.submit(fetch_raw_json, rp, cp): k for rp, cp, k in to_fetch
        }
        for fut in cf.as_completed(future_to_key):
            if _stop_for_ratelimit:
                break
            k = future_to_key[fut]
            data = fut.result()
            results[k] = data
            done += 1
            if done % 500 == 0 or done == total:
                log(f"[{desc}] {done}/{total}")

    return results


def basename_no_ext(file_name_field, suffix):
    # file_name_field looks like "base/meta/Item/Foo.itm" -> "Foo"
    b = file_name_field.rsplit("/", 1)[-1]
    if b.endswith(suffix):
        b = b[: -len(suffix)]
    return b


def derive_gear_codename(charm_codename):
    """Only the Talisman system has a real paired gear item whose codename is
    embedded verbatim after the fixed prefix. Season 15's standalone charms
    (Annihilus, Hellfire Torch — codenames like S15_Charm_Unique_Annihilus) have no
    separate gear form (verified: no matching .itm.json exists), so we do not
    fabricate a gear codename for them — null, not a guess."""
    prefix = "Talisman_Charm_Unique_"
    if charm_codename.startswith(prefix):
        return charm_codename[len(prefix):]
    return None


# ---------------------------------------------------------------------------
# Main extraction
# ---------------------------------------------------------------------------

def main():
    started = datetime.datetime.now(datetime.timezone.utc)
    log(f"=== Medick's Might S15 extractor ===")
    log(f"pinned commit: {PIN_SHA}")
    log(f"game build:    {GAME_BUILD}")
    log(f"cache dir:     {CACHE_DIR}")

    # --- 1. Tree listings we need -------------------------------------------------
    log("\n-- step 1: tree listings --")
    item_entries = walk_tree(["json", "base", "meta", "Item"])
    if item_entries is None:
        log("FATAL: could not obtain Item tree listing.")
        sys.exit(1)
    set_entries = walk_tree(["json", "base", "meta", "SetItemBonus"])
    if set_entries is None:
        log("FATAL: could not obtain SetItemBonus tree listing.")
        sys.exit(1)

    item_files = [e["path"] for e in item_entries if e["type"] == "blob" and e["path"].endswith(".itm.json")]
    set_files = [e["path"] for e in set_entries if e["type"] == "blob" and e["path"].endswith(".set.json")]
    log(f"Item tree: {len(item_files)} files")
    log(f"SetItemBonus tree: {len(set_files)} files")

    # --- 2. Fetch every item file (data-driven rarity classification, no filename
    #        guessing: every one of the 12k+ files is fetched and its real
    #        eMagicType field is read) --------------------------------------------
    log("\n-- step 2: fetching all item files --")
    item_jobs = [
        (f"base/meta/Item/{fn}", os.path.join(ITEMS_DIR, fn), fn)
        for fn in item_files
    ]
    item_data = fetch_many(item_jobs, "items")
    if _stop_for_ratelimit:
        log("Stopping: rate limited mid-run. Re-run this script later to resume from cache.")
        sys.exit(2)

    fetched_items = {k: v for k, v in item_data.items() if v is not None}
    missing_items = [k for k, v in item_data.items() if v is None]
    if missing_items:
        log(f"  !! {len(missing_items)} item files failed to fetch (404 or error); "
            f"see fetch_failures in MANIFEST.")

    # --- 3. Fetch all 46 set files --------------------------------------------------
    log("\n-- step 3: fetching set files --")
    set_jobs = [
        (f"base/meta/SetItemBonus/{fn}", os.path.join(SETS_DIR, fn), fn)
        for fn in set_files
    ]
    set_data = fetch_many(set_jobs, "sets")
    fetched_sets = {k: v for k, v in set_data.items() if v is not None}

    # --- 4. Classify items -----------------------------------------------------
    log("\n-- step 4: classifying items by eMagicType (data-driven) --")
    unique_gear = []       # eMagicType==2, not a Talisman_Charm_*
    unique_charms = []     # eMagicType==2, filename starts with Talisman_Charm_Unique_
    mythic_gear = []       # eMagicType==4, not a Talisman_Charm_*
    mythic_charms = []     # eMagicType==4, filename starts with Talisman_Charm_Unique_
    set_charms = []        # eMagicType==3, filename starts with Talisman_Charm_Set_
    other_magic_types = {}

    for fn, d in fetched_items.items():
        if d is None:
            continue
        magic = d.get("eMagicType")
        codename = basename_no_ext(d.get("__fileName__", fn), ".itm")
        sno = d.get("__snoID__")
        # Charm detection: originally this only matched the exact
        # "Talisman_Charm_Unique_" prefix (the paired gear<->charm Talisman system).
        # Season 15 introduced standalone charms ("Horadric Seals" / classic D2-style
        # small charms — Annihilus, Hellfire Torch) that do NOT share that prefix but
        # DO contain "_Charm_Unique_" in their codename (e.g. S15_Charm_Unique_
        # Annihilus) and carry the same eCharmRarity marker as real charms. Matching
        # on substring instead of prefix catches these without widening to anything
        # that merely mentions "Charm" elsewhere (verified against the full 12,117
        # codename list — only these two fall outside the Talisman_ prefix).
        is_unique_charm_name = "_Charm_Unique_" in codename
        is_set_charm_name = "_Charm_Set_" in codename
        if magic == 2:
            if is_unique_charm_name:
                unique_charms.append((codename, sno, d))
            else:
                unique_gear.append((codename, sno, d))
        elif magic == 4:
            # Mythic tier — a rarer tier than Unique on the game's own eMagicType
            # enum, but D4BuildFilter's existing UniqueDatabase.cs mixes fixed-roster
            # Mythics into the same table as Uniques, so we resolve them with the
            # identical gear/charm split, just into separate output files (the
            # consumer decides the union; see mythics.json / mythic_charms.json).
            if is_unique_charm_name:
                mythic_charms.append((codename, sno, d))
            else:
                mythic_gear.append((codename, sno, d))
        elif magic == 3 and is_set_charm_name:
            set_charms.append((codename, sno, d))
        else:
            other_magic_types[magic] = other_magic_types.get(magic, 0) + 1

    log(f"unique gear candidates: {len(unique_gear)}")
    log(f"unique charm candidates: {len(unique_charms)}")
    log(f"mythic gear candidates: {len(mythic_gear)}")
    log(f"mythic charm candidates: {len(mythic_charms)}")
    log(f"set charm candidates: {len(set_charms)}")

    # --- 5. Resolve display names via StringList join -------------------------
    log("\n-- step 5: resolving display names (StringList join) --")
    name_targets = [(codename, sno) for (codename, sno, d) in unique_gear] + \
                   [(codename, sno) for (codename, sno, d) in unique_charms] + \
                   [(codename, sno) for (codename, sno, d) in mythic_gear] + \
                   [(codename, sno) for (codename, sno, d) in mythic_charms]
    stl_jobs = [
        (f"enUS_Text/meta/StringList/Item_{codename}.stl.json",
         os.path.join(STL_DIR, f"Item_{codename}.stl.json"),
         codename)
        for codename, sno in name_targets
    ]
    stl_data = fetch_many(stl_jobs, "stringlist (display names)")

    def resolve_name(codename):
        d = stl_data.get(codename)
        if not d:
            return None
        for s in d.get("arStrings", []):
            if s.get("szLabel") == "Name":
                return s.get("szText")
        return None

    # --- 6. Build uniques.json ---------------------------------------------------
    uniques_out = []
    unresolved_gear = 0
    for codename, sno, d in sorted(unique_gear, key=lambda x: x[1] or 0):
        name = resolve_name(codename)
        if name is None:
            unresolved_gear += 1
        uniques_out.append({
            "snoID": sno,
            "snoID_hex": f"0x{sno:x}" if sno is not None else None,
            "codename": codename,
            "displayName": name,
        })

    # --- 7. Build unique_charms.json --------------------------------------------
    charms_out = []
    unresolved_charm = 0
    for codename, sno, d in sorted(unique_charms, key=lambda x: x[1] or 0):
        name = resolve_name(codename)
        if name is None:
            unresolved_charm += 1
        gear_codename = derive_gear_codename(codename)
        charms_out.append({
            "snoID": sno,
            "snoID_hex": f"0x{sno:x}" if sno is not None else None,
            "codename": codename,
            "displayName": name,
            "gearCodename": gear_codename,
        })

    # --- 7b. Build mythics.json (eMagicType==4 gear) -----------------------------
    mythics_out = []
    unresolved_mythic_gear = 0
    for codename, sno, d in sorted(mythic_gear, key=lambda x: x[1] or 0):
        name = resolve_name(codename)
        if name is None:
            unresolved_mythic_gear += 1
        mythics_out.append({
            "snoID": sno,
            "snoID_hex": f"0x{sno:x}" if sno is not None else None,
            "codename": codename,
            "displayName": name,
        })

    # --- 7c. Build mythic_charms.json (eMagicType==4 charms) ---------------------
    mythic_charms_out = []
    unresolved_mythic_charm = 0
    for codename, sno, d in sorted(mythic_charms, key=lambda x: x[1] or 0):
        name = resolve_name(codename)
        if name is None:
            unresolved_mythic_charm += 1
        gear_codename = derive_gear_codename(codename)
        mythic_charms_out.append({
            "snoID": sno,
            "snoID_hex": f"0x{sno:x}" if sno is not None else None,
            "codename": codename,
            "displayName": name,
            "gearCodename": gear_codename,
        })

    # --- 8. Build talisman_sets.json ---------------------------------------------
    # identify + skip the junk "Axe Bad Data" entry by its actual name field, not
    # by filename, since filename ("Axe Bad Data.set.json") already gives it away,
    # but we confirm via content too.
    all_item_basenames = set()
    for fn in item_files:
        all_item_basenames.add(fn[:-len(".itm.json")] if fn.endswith(".itm.json") else fn)

    sets_out = []
    junk_skipped = []
    for fn, d in sorted(fetched_sets.items()):
        if d is None:
            continue
        codename = basename_no_ext(d.get("__fileName__", fn), ".set")
        sno = d.get("__snoID__")
        if codename == "Axe Bad Data" or fn.strip() == "Axe Bad Data.set.json":
            junk_skipped.append({"filename": fn, "codename": codename, "snoID": sno})
            continue

        tiers = []
        for t in d.get("ptTiers", []):
            tiers.append({
                "nRequired": t.get("nRequired"),
                "affix": (t.get("snoAffix") or {}).get("name"),
            })

        # members: derive the charm-family prefix from the set codename
        # ("Talisman_Barb_01" -> member files "Talisman_Charm_Set_Barb_01_*"),
        # found by actual filename presence in the Item tree (data-driven, not
        # an assumed count).
        if not codename.startswith("Talisman_"):
            log(f"  !! set codename '{codename}' doesn't start with 'Talisman_' — "
                f"cannot derive member prefix, skipping member lookup")
            suffix = None
        else:
            suffix = codename[len("Talisman_"):]

        members = []
        if suffix is not None:
            member_prefix = f"Talisman_Charm_Set_{suffix}_"
            for mc, msno, mdata in set_charms:
                if mc.startswith(member_prefix):
                    members.append({
                        "codename": mc,
                        "snoID": msno,
                        "snoID_hex": f"0x{msno:x}" if msno is not None else None,
                    })
            members.sort(key=lambda m: m["codename"])

        sets_out.append({
            "snoID": sno,
            "snoID_hex": f"0x{sno:x}" if sno is not None else None,
            "codename": codename,
            "tiers": tiers,
            "members": members,
        })

    # --- 9. Write outputs ----------------------------------------------------------
    log("\n-- step 6: writing outputs --")

    def dump(name, obj):
        path = os.path.join(OUT_DIR, name)
        with open(path, "w", encoding="utf-8") as f:
            json.dump(obj, f, indent=2, ensure_ascii=False)
        log(f"  wrote {path}")
        return path

    dump("uniques.json", uniques_out)
    dump("unique_charms.json", charms_out)
    dump("mythics.json", mythics_out)
    dump("mythic_charms.json", mythic_charms_out)
    dump("talisman_sets.json", sets_out)

    manifest = {
        "pinnedCommit": PIN_SHA,
        "gameBuild": GAME_BUILD,
        "sourceRepo": REPO,
        "extractedAtUtc": started.isoformat(),
        "counts": {
            "uniqueGear": len(uniques_out),
            "uniqueCharms": len(charms_out),
            "mythicGear": len(mythics_out),
            "mythicCharms": len(mythic_charms_out),
            "talismanSetsRealCount": len(sets_out),
            "talismanSetsJunkSkipped": len(junk_skipped),
            "itemFilesTotalInTree": len(item_files),
            "itemFilesFetchedOk": len(fetched_items),
            "itemFilesFetchFailed": len(missing_items),
            "setFilesTotalInTree": len(set_files),
            "setFilesFetchedOk": len(fetched_sets),
        },
        "unresolvedNames": {
            "uniqueGear": unresolved_gear,
            "uniqueCharms": unresolved_charm,
            "mythicGear": unresolved_mythic_gear,
            "mythicCharms": unresolved_mythic_charm,
            "total": unresolved_gear + unresolved_charm + unresolved_mythic_gear + unresolved_mythic_charm,
        },
        "junkEntriesSkipped": junk_skipped,
        "fetchFailures": missing_items[:200],  # cap so manifest stays readable
        "eMagicTypeDistributionOfUnclassifiedItems": other_magic_types,
        "note": "uniques.json/unique_charms.json = eMagicType 2 only. "
                "mythics.json/mythic_charms.json = eMagicType 4 only, kept SEPARATE "
                "by design so a consumer decides the union explicitly (D4BuildFilter's "
                "existing UniqueDatabase.cs mixes both tiers into one table; this "
                "extractor does not silently replicate that merge).",
        "rateLimitedMidRun": _stop_for_ratelimit,
    }
    dump("MANIFEST.json", manifest)

    log("\n=== extraction complete ===")


if __name__ == "__main__":
    main()
