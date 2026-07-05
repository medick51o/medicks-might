<p align="center">
  <img src="D4BuildFilter.WPF/Assets/mk_logo.png" width="120" alt="MK monogram — Medick's Madhouse" />
</p>

<h1 align="center">Medic<span style="color:#e22d24">K</span>'s Might</h1>
<p align="center"><i>Filters Made EZ</i></p>

<p align="center"><b>THE GOLD STANDARD IN LOOT FILTER STANDARDIZATION FOR DIABLO 4</b></p>

<p align="center">Turn any Diablo 4 build into a ready-to-import in-game loot filter — in seconds.</p>

---

## Download

Grab the latest **MedicKs-Might.zip** from [Releases](../../releases/latest), unzip the folder (keep all the files together), then double-click **MedicK's Might.exe**.

> 🛡 Windows may say *"Windows protected your PC"* (unknown publisher) — click **More info → Run anyway**. It's unsigned, not unsafe.

**No install needed** — the .NET runtime is bundled in (single-file self-contained).

## What it does

- **Browse this season's top builds** — live tier lists from **Maxroll**, **D4Builds**, and **Mobalytics** right on the landing page, with tabs per source (Endgame · Bossing · Leveling · Pushing · Speedfarming — every list the source publishes).
- **All 8 classes color-coded** — Barb · Druid · Necro · Rogue · Sorc · Spiritborn · Paladin · Warlock — with a class filter row to hide the classes you don't play.
- **★ Favorites** — star any build or community paste; landing page surfaces them up top with provenance ("Maxroll · Endgame · S · added 3d ago"). Favorites are live references, not snapshots — they re-fetch on click so the build self-updates as the meta shifts, and their **tier labels re-sync against the live lists** too: if Maxroll demotes your pick the chip shows "S→A" (or "was S · off the list" when it drops off entirely).
- **⟳ Live refresh** — a refresh button (or F5) re-pulls all three tier lists on demand and re-checks every saved build's tier; lists also auto-refresh after 15 minutes and per-source "updated HH:mm" stamps show how fresh each column is.
- **Paste anything** — a Maxroll/D4Builds/Mobalytics URL, OR a raw affix list copied from Icy Veins / Discord / a screenshot. Universal paste mode handles the rest.
- **Compiles a precise per-slot loot filter:**
  - **Red** (3+ affix legendaries) and **Pink** (3+ affix rares) per gear slot: the strict standard, with 2-affix clutter hidden
  - **Leveling** (opt-in): adds **Silver** for 2+ affix rares so you can gear up on the way to endgame
  - **Gold** (opt-in): cube bases, magic items with both affixes on-build **and** a Greater Affix (the blues worth cubing)
  - Weapons grouped by handedness (1H / 2H) so the Barb arsenal fits the 25-rule cap
  - **White** Codex upgrades ranked top (a permanent aspect unlock beats any single drop) · **Purple** build uniques · **Orange/Cyan** Item Power (900+/850+) · **Blue** Greater Affixes · **Green** charms & seals
  - **Pick what you see:** checkbox lists for every unique and unique charm (all on by default, uncheck to hide, with select-all/none) plus per-class talisman sets
  - Mythics never touched (they drop with their natural beam)
  - Everything else hidden
- **Talisman & set-bonus recognition** — decoded community filters spell out set names (e.g. *"Talisman: Barbarian Set 01"*) instead of hex blobs.
- **Round-trip verified import code** — paste straight into Diablo 4's Loot Filter → Import.

## Season 14 notes & known limits

- **25 rules max** is Blizzard's hard import cap, not ours — the app warns when a build would blow
  it and tells you what to turn off.
- **Brand-new S14 uniques** purple-target via the 3.1 datamine; an oddball may show as *pending*
  in the app until its filter id is captured — it still drops visibly (never hidden).
- **Known in-game bug (not the app):** filtering Seals by talisman set-bonus can misbehave —
  reported to Blizzard by the community; no filter-side workaround exists.
- **Pandemonium Fragments / Lair Keys** are currency-like pickups, not gear — no loot filter
  (anyone's) can target them.

## How to use

1. Download from [Releases](../../releases/latest) and unzip.
2. Run **MedicK's Might.exe**.
3. Click a build on the landing page, star a favorite, or paste a build URL / affix list.
4. Tweak the options if you want (live preview).
5. Copy the import code.
6. In Diablo 4: **Inventory → Loot Filter → Import**, paste.

## Build from source

```bash
git clone https://github.com/medick51o/medicks-might.git
cd medicks-might
dotnet build -c Release
dotnet run --project D4BuildFilter.WPF
```

Stack: **.NET 10 · WPF · CommunityToolkit.Mvvm**. Three projects — `Core` (protobuf encoder, fetchers, compiler, affix/unique/talisman DBs), `WPF` (UI), `Tester` (console). **171 xUnit tests** (plus opt-in live canaries: `$env:RUN_CANARY=1; dotnet test --filter Category=Canary`).

## Credits

- Tier-list rankings live-pulled from **[Maxroll.gg](https://maxroll.gg/d4/tierlists/endgame-tier-list)**, **[D4Builds.gg](https://d4builds.gg/tierlist/)**, and **[Mobalytics](https://mobalytics.gg/diablo-4)**, displayed with attribution.
- Affix / skill / unique / talisman-set data from [DiabloTools/d4data](https://github.com/DiabloTools/d4data) (build 3.0.3.72031) via [ThunderEagle/D4LootBench](https://github.com/ThunderEagle/D4LootBench) (MIT).
- In-app **Update game data** refreshes the affix/unique name files from [josdemmers/Diablo4Companion](https://github.com/josdemmers/Diablo4Companion) (MIT) into `%LOCALAPPDATA%\MedicKsMight\data\`, so new-season items resolve without waiting for an app release.
- Display-only affix label cross-reference from [d4lfteam/d4lf](https://github.com/d4lfteam/d4lf).
- Built by **Medick** · MK — *Medick's Madhouse*.
