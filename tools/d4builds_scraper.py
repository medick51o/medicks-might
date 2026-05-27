#!/usr/bin/env python3
"""
d4builds.gg -> affix list for the D4 Build Filter's "Paste affixes" mode.

WHY THIS EXISTS: d4builds is a Gatsby SPA whose build data loads client-side from Firestore
behind Firebase App Check, so there is no plain-HTTP way to fetch a build (see ../BUILD_SCRAPING.md).
The only reliable automated path is to render the page in a real browser and scrape the gear DOM.
This headless-Chrome helper does that and prints the build's desired affixes + unique item names,
which you paste into the app (Paste affixes mode) to generate the filter.

STATUS: scaffold. The DOM class names below are d4builds' own (factual) selectors, matching the
current site as of 2026-05. It was NOT run in the dev environment (no Chrome there) — calibrate the
selectors/waits if d4builds changes their markup. The approach is standard headless scraping; it is
not derived from any GPL project's code.

SETUP:
    pip install selenium                 # Selenium 4 auto-manages chromedriver
    (and have Google Chrome installed)

USAGE:
    python d4builds_scraper.py "https://d4builds.gg/builds/<id>/?var=1"
    python d4builds_scraper.py "<url>" --out build.txt --show     # save + run visible (debug)
then paste the output into the app's "Paste affixes" box.
"""
from __future__ import annotations

import argparse
import sys
import time


def scrape(url: str, headless: bool = True, settle: float = 5.0) -> tuple[list[str], list[str]]:
    from selenium import webdriver
    from selenium.webdriver.common.by import By
    from selenium.webdriver.support import expected_conditions as EC
    from selenium.webdriver.support.wait import WebDriverWait

    opts = webdriver.ChromeOptions()
    if headless:
        opts.add_argument("--headless=new")
    opts.add_argument("--log-level=3")
    opts.add_argument("--window-size=1920,1080")
    driver = webdriver.Chrome(options=opts)
    try:
        driver.get(url)
        wait = WebDriverWait(driver, 20)
        # wait for the stat list + gear paperdoll to exist, then let the SPA finish populating
        wait.until(EC.presence_of_element_located((By.XPATH, "//*[contains(@class,'builder__stats__list')]")))
        wait.until(EC.presence_of_element_located((By.XPATH, "//*[contains(@class,'builder__gear__items')]")))
        time.sleep(settle)

        affixes: list[str] = []
        wrappers = driver.find_elements(
            By.XPATH, "//*[contains(@class,'builder__stats__list')]//*[contains(@class,'dropdown__button__wrapper')]")
        for w in wrappers:
            # only "filled" slots carry a real affix (the filled class sits on a grandparent)
            try:
                anc = w.find_element(By.XPATH, "./../..")
                if "filled" not in (anc.get_attribute("class") or ""):
                    continue
            except Exception:
                pass
            # skip tempered / sanctified rows — those aren't searchable base affixes
            if w.find_elements(By.XPATH, ".//img[contains(@src,'tempering') or contains(@src,'sanctified')]"):
                continue
            txt = " ".join((w.text or "").split()).strip()
            if txt:
                affixes.append(txt)

        uniques: list[str] = []
        for u in driver.find_elements(
                By.XPATH, "//*[contains(@class,'builder__gear__items')]//*[contains(@class,'builder__gear__name--')]"):
            t = " ".join((u.text or "").split()).strip()
            if t and t not in uniques:
                uniques.append(t)

        return affixes, uniques
    finally:
        driver.quit()


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="Scrape a d4builds.gg build into a paste-ready affix list.")
    ap.add_argument("url")
    ap.add_argument("--out", help="also write the list to this file")
    ap.add_argument("--show", action="store_true", help="run a visible (non-headless) browser for debugging")
    ap.add_argument("--settle", type=float, default=5.0, help="seconds to wait after load for the SPA to populate")
    args = ap.parse_args(argv)

    if "d4builds.gg/builds" not in args.url:
        print("Please pass a d4builds.gg build URL (…/builds/<id>).", file=sys.stderr)
        return 2
    try:
        affixes, uniques = scrape(args.url, headless=not args.show, settle=args.settle)
    except ImportError:
        print("selenium is required:  pip install selenium  (and have Chrome installed)", file=sys.stderr)
        return 2

    lines = affixes + uniques
    text = "\n".join(lines)
    print(text)
    print(f"\n# {len(affixes)} affixes, {len(uniques)} uniques — paste the lines above into the app's "
          f"'Paste affixes' box.", file=sys.stderr)
    if args.out:
        from pathlib import Path
        Path(args.out).write_text(text + "\n", encoding="utf-8")
        print(f"# wrote {args.out}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
