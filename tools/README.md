# tools/

Helper scripts that sit beside the .NET app (not part of the build).

## d4builds_scraper.py  (fallback — usually unnecessary)
**The app now fetches d4builds builds directly over HTTP** (Firestore get-by-id; see
`../BUILD_SCRAPING.md` and `D4BuildsFetcher`), so just paste a d4builds URL into the app. This
headless-Chrome scraper is only a fallback in case Firestore rules ever tighten — it renders a
build page and prints its affixes + unique names for the app's **Paste affixes** mode.

```bash
pip install -r requirements.txt      # selenium (auto-manages chromedriver); needs Chrome installed
python d4builds_scraper.py "https://d4builds.gg/builds/<id>/?var=1"
# paste the printed lines into the app
```

Scaffold status: the DOM selectors match d4builds as of 2026-05 but the script wasn't run in the
dev environment — if d4builds changed their markup, run with `--show` to watch it and adjust the
XPath selectors near the top of `scrape()`. maxroll and mobalytics don't need this — paste their
URL straight into the app.
