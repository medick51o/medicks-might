<p align="center">
  <img src="D4BuildFilter.WPF/Assets/mk_logo.png" width="120" alt="MK monogram — Medick's Madhouse" />
</p>

<h1 align="center">Medic<span style="color:#e22d24">K</span>'s Might</h1>
<p align="center"><i>Filters Made EZ</i></p>

<p align="center">Turn any Diablo 4 build into a ready-to-import in-game loot filter — in seconds.</p>

---

## Download

Grab the latest **MedicK's Might.zip** from [Releases](../../releases/latest), unzip the folder (keep all the files together), then double-click **MedicK's Might.exe**.

> 🛡 Windows may say *"Windows protected your PC"* (unknown publisher) — click **More info → Run anyway**. It's unsigned, not unsafe.

**No install needed** — the .NET runtime is bundled in.

## What it does

- **Browse this season's top builds** — live S/A/B tier lists from Maxroll and D4Builds right on the landing page.
- **One click loads a build** — guide pages auto-resolve to their planner; D4Builds URLs load directly.
- **Or paste a build URL** — Maxroll, Mobalytics, or D4Builds.
- **Or paste any affix list** — works with any build guide (Icy Veins, Discord, screenshots, anywhere).
- **Compiles a precise per-slot loot filter:**
  - **Gold** (3+ build affixes) and **Silver** (2+) per gear slot — your build's keepers
  - Weapons grouped by handedness (1H / 2H) so the Barb arsenal fits the rule cap
  - **Purple** — build uniques · **Blue** — Greater Affixes · **Green** — charms & seals · **White** — Codex upgrades
  - Mythics never touched (they drop with their natural beam)
  - Everything else hidden
- **Round-trip verified import code** — paste straight into Diablo 4's Loot Filter → Import.

## How to use

1. Download from [Releases](../../releases/latest) and unzip.
2. Run **MedicK's Might.exe**.
3. Click a build on the landing page (or paste a build URL).
4. Tweak the options if you want (live preview).
5. Copy the import code.
6. In Diablo 4: **Inventory → Loot Filter → Import**, paste.

## Build from source

```bash
git clone <this repo>
cd D4BuildFilter
dotnet build -c Release
dotnet run --project D4BuildFilter.WPF
```

Stack: **.NET 10 · WPF · CommunityToolkit.Mvvm**. Three projects — `Core` (protobuf encoder, fetchers, compiler), `WPF` (UI), `Tester` (console). 67 xUnit tests.

## Credits

- Tier-list rankings live-pulled from **[Maxroll.gg](https://maxroll.gg/d4/tierlists/endgame-tier-list)** and **[D4Builds.gg](https://d4builds.gg/tierlist/)**, displayed with attribution.
- Item / affix / unique data from [DiabloTools/d4data](https://github.com/DiabloTools/d4data) via [ThunderEagle/D4LootBench](https://github.com/ThunderEagle/D4LootBench) (MIT).
- Built by **Medick** · MK — *Medick's Madhouse*.
