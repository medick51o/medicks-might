# tools/

Helper scripts that sit beside the .NET app (not part of the build).

## d4builds_scraper.py
d4builds.gg can't be fetched over plain HTTP (Gatsby SPA → Firestore + App Check; see
`../BUILD_SCRAPING.md`). This headless-Chrome scraper renders a build page and prints its desired
affixes + unique item names, which you paste into the app's **Paste affixes** mode.

```bash
pip install -r requirements.txt      # selenium (auto-manages chromedriver); needs Chrome installed
python d4builds_scraper.py "https://d4builds.gg/builds/<id>/?var=1"
# paste the printed lines into the app
```

Scaffold status: the DOM selectors match d4builds as of 2026-05 but the script wasn't run in the
dev environment — if d4builds changed their markup, run with `--show` to watch it and adjust the
XPath selectors near the top of `scrape()`. maxroll and mobalytics don't need this — paste their
URL straight into the app.
