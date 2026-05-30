# web/ — MedicK's Might landing page

A single static page (`index.html` + `style.css`), brand-matched to the app's Default theme.
No build step, no dependencies — hostable free on Cloudflare Pages or GitHub Pages.

## Before going live — fill in the placeholders
Search the HTML for `data-link="..."` and `TODO`:
- `data-link="download"` → the GitHub Releases latest-zip URL
- `data-link="discord"`  → the Discord invite URL
- `data-link="kofi"`     → the Ko-fi URL
- `og:url` / `og:image`  → set once the domain + a share image exist

(The `data-link` attributes are there so you can wire them all from one small `<script>` later, or
just replace each `href="#"` directly.)

## Deploy (GitHub Pages, simplest)
1. Push this repo to GitHub.
2. Settings → Pages → Source = `main`, folder = `/web` (or move these files to a `gh-pages` branch root).
3. Add the custom domain `medicksmight.com` and the CNAME.

## Deploy (Cloudflare Pages)
1. Connect the repo, set the build output directory to `web/`, no build command.
2. Add the custom domain in the Pages project.

## Next
Add SEO content pages (e.g. `how-to-import-a-loot-filter.html`, `best-filter-<class>.html`) — these
are what rank on Google and unlock the ad-revenue stage. See `../MONETIZATION_PLAN.md`.
