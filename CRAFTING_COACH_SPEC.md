# Crafting Coach — spec (greenlit 2026-07-04)

**What.** A new wing of MedicK's Might: a follow-along, second-screen crafting coach for the
Horadric Cube. Not a reference compendium — a guided "do your first craft with me" experience.
Nav tab: **Crafting Coach**. Page headline carries the lore ("the Horadric Cube, decoded").

**Why.** The founder's own story is the user story: *"I have all these mats at the end of the
season and no clue how to throw them into the cube… I'd love a colorful diagram with a great
flow that a player can put on a second screen, follow along, and go do an actual craft."*
The app already knows the player's build (affix pool, class, filter keepers) — no other D4 tool
can say "grab any blue glowing GOLD from your filter; that's your practice piece." The filter
marks the homework; the coach walks you through it.

**Who.** Filter users who never opened the cube (the founder included) — casuals first,
second-screen form factor: big type, one thing per step, glanceable from a couch mid-alt-tab.

## v1 scope (this build)

1. **Goal-card landing** — "What do you want to do?" cards, verb-first:
   - 🟡 *Fill an empty slot* → Add Affix (the hero: uses the gold cube-bases the filter marks)
   - ⬆ *Turn a Rare into a Legendary* → Upgrade to Legendary
   - (v1.1 cards visible but marked "coming soon": Fix one bad affix · Melt junk into a reroll)
2. **Step-flow coach** per craft — 4-ish steps, each one screen: YOUR ITEM · YOUR MATS ·
   THE CUBE · WHAT YOU GET, with Next/Back, "Step N of M" dots, big second-screen type.
   Pure XAML vectors + emoji glyphs — zero image assets.
3. **Dust decoder panel** — every mat tier in one glance (Raw/Coarse/Refined/Pure/Enhanced/
   Volatile/Attuned Primordial Dust + Pandemonium Fragments + Infused Horadric Resin), one
   plain-English line each: what it is, which recipes want it.
4. **Build-aware hints** — when a build is loaded, the coach greets with it ("Coaching for your
   Barbarian — Whirlwind") and the Add Affix flow points at the gold-glowing blues.

**Content honesty rules (inherited from the research + panel):** outcomes shown as ranges /
what-is-kept vs what-rerolls; "rerolls unless you lock it at the Occultist first" wording; the
only percentages allowed are the two sourced ones (~15% Transfigure, ~4% charm), each flagged
"~ community-sourced"; a "Recipes verified: S14 patch 3.1.x" stamp on the landing.

**Out of scope, permanently (panel guardrails):** stash/inventory sync · authored
Keep/Cube/Vendor verdicts · DPS math · fake precision · live data fetch · animated frames.

**v1.1:** Fix-one-affix (Focused/Chaotic Reroll) + 3-to-1 junk-melter flows.
**v2:** advanced wing (Mythic crafting, Transfigure, charm recipes), dust-cost audit across
planned crafts, relevance-sort, hand-drawn icons. **Someday:** standalone app (founder's note).

**Done-when (v1):** tab renders with landing + both coached flows + decoder; step navigation
works; build-aware hint reflects the loaded build; content matches the verified recipe research
word-for-word on inputs/mats/outcomes; solution builds 0/0; suite green; capture proof; founder
follows the Add Affix flow on a second screen and performs a real craft.

## Cube Sandbox (greenlit 2026-07-04 — "Greenlight, build it now"; third surface of the wing)

**What.** *"Simulate putting in an item and it gives you options of what recipes are available…
all the mats are on screen and clumped together in possible recipe combinations to drop in for
outcomes."* — the founder's words, built literally. Entry: a full-width button on the coach
landing ("🧪 Open the Cube Sandbox — rehearse any craft on a pretend item").

**The pretend item = four switches** (the only properties recipes actually key on):
rarity (Common/Magic/Rare/Legendary/Unique/Mythic) · ⭐ GA count (0/1/2) · 850+ Item Power ·
open affix slot. Defaults: Magic, 0 GA, 850+, open slot — the filter's gold-glow scenario.

**The board.** All 11 gear recipes as cards, each with its mat bundle clumped on it
("1× Coarse + 5× Raw Primordial Dust"). Flip a switch and cards light/dim live; a dimmed card
says *why* in red ("needs an open affix slot", "Rare items only", "needs a Unique at 850+ Item
Power"). Multi-item recipes declare their count on the card ("needs 3 IDENTICAL Uniques").

**Drop in.** Click an eligible card's "drop into 🧪" → the OUTCOME panel: honest what-you-get
lines (GA-aware: "Your ⭐ stays untouched" on Add/Remove Affix vs "does NOT carry — nothing
does" on the upgrades), the mats consumed, and warnings (Transfigure's LOCKS-FOREVER). No fake
rolls, no invented odds — the honesty rules above apply verbatim. Any switch flip clears the
shown outcome (the pretend item changed).

**Engine.** `CubeSimulator.Evaluate(CubeSimItem)` in Core — pure, test-pinned
(CubeSimulatorTests: eligibility gates, GA truths, lock warning, requirement copy). Charm
recipes stay out of the v1 sandbox (they take charms, not gear).

**Done-when (sandbox):** switches drive live eligibility; every recipe card carries its mat
bundle; drop-in shows the honest outcome; engine gates test-pinned; builds 0/0; suite green;
capture proof of board + outcome; founder flips the switches on his real S13 mat pile and picks
a first craft from it.

## Game icons (added 2026-07-04 — founder ask: "can we show icons from the game")

**What.** Real in-game art replaces the emoji glyphs across the whole wing: goal cards, the dust
decoder, the sandbox recipe cards, and the outcome panel. Mat bundles render as the actual item-
card art clumped together (Coarse+Raw dust, the blue Tuning Prism, the red Volatile dust, etc.).

**Source.** One-time fetch, NOT a live dependency: the 7 Primordial Dust tiers + Tuning Prism +
Horadric Resin came from maxroll's d4-tools data (`data.min.json` → each mat's `image` texture id
→ `assets-ng.maxroll.gg/d4-tools/images/webp/{id}.webp`), converted webp→png via WIC; the 11
recipe icons came from maxroll's cube resource page (`wordpress/horadric_cube_*.png`). All 20 PNGs
are committed under `D4BuildFilter.WPF/Assets/Cube/` and bundled as WPF `<Resource>` — the app
ships them, it never calls the network.

**Wiring.** `RecipeIconConverter` (recipe name → icon; Focused/Chaotic share the reroll art, the
two 3-unique recipes share the unique-reroll art, matching the game's own grouping) and
`MatIconsConverter` (mats line → the icons it mentions). `EmptyStringToVisibilityConverter` drives
the emoji fallback.

**Graceful gap.** Pandemonium Fragments' icon isn't in the source bucket (404), so it falls back
to its 🟣 glyph and the text line carries the full truth — the intended degradation, not a bug.

**Attribution.** The art is © Blizzard Entertainment, used the same way every D4 fan tool uses it;
covered by the footer's non-affiliation disclaimer. Provenance is documented in the csproj comment
beside the `Assets\Cube\*.png` resource include.
