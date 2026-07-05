using System.Linq;

namespace D4BuildFilter.Core;

/// <summary>A simulated item for the Cube Sandbox — just the properties recipes actually key on.
/// Rarity: Common | Magic | Rare | Legendary | Unique | Mythic.</summary>
public sealed record CubeSimItem(string Rarity, bool Power850Plus, int GreaterAffixes, bool OpenSlot);

/// <summary>One material line in a recipe's REQUIRED MATERIALS panel — structured so the UI can
/// render the game's layout (icon + name + ×quantity per row). <see cref="Key"/> is the kebab icon
/// filename (e.g. "coarse-dust"); the UI maps it to the bundled art.</summary>
public sealed record CubeMat(string Key, string Name, int Quantity, bool Optional = false);

/// <summary>One recipe evaluated against a simulated item, shaped like the in-game recipe screen:
/// a category (Gear Modification / Item Transmutation), structured required materials, whether the
/// simulated item is eligible (with the reason if not), honest outcome lines, and any warning.</summary>
public sealed record CubeSimResult(
    string Name,
    string Category,
    IReadOnlyList<CubeMat> Materials,
    bool Eligible,
    string Requirement,
    IReadOnlyList<string> Outcome,
    string Warning,
    string MultiNote)
{
    /// <summary>The materials as one plain line ("1× Coarse + 5× Raw Primordial Dust"), for the
    /// "Consumed:" caption and anywhere a single string reads better than rows.</summary>
    public string MatsLine => Materials.Count == 0
        ? "no dust, items only"
        : string.Join(" + ", Materials.Select(m => $"{m.Quantity}× {m.Name}{(m.Optional ? " (optional)" : "")}"));
}

/// <summary>
/// The Cube Sandbox's rules engine (v1.0.2 — CRAFTING_COACH_SPEC.md). Evaluates every S14 Horadric
/// Cube gear recipe against a simulated item, in the game's own order and grouping. Content mirrors
/// the verified recipe research: deterministic transforms with honest ranges, "does NOT carry"
/// truths, the Transfigure lock, and community-sourced percentages flagged as such. Charm recipes
/// are out of the v1 sandbox (they take charms, not gear).
/// </summary>
public static class CubeSimulator
{
    public const string GearModification = "Gear Modification";
    public const string ItemTransmutation = "Item Transmutation";

    // Material shorthand — Key matches the bundled icon filename (Assets/Cube/{key}.png).
    private static CubeMat Raw(int n) => new("raw-dust", "Raw Primordial Dust", n);
    private static CubeMat Coarse(int n) => new("coarse-dust", "Coarse Primordial Dust", n);
    private static CubeMat Refined(int n) => new("refined-dust", "Refined Primordial Dust", n);
    private static CubeMat Pure(int n) => new("pure-dust", "Pure Primordial Dust", n);
    private static CubeMat Enhanced(int n) => new("enhanced-dust", "Enhanced Primordial Dust", n);
    private static CubeMat Volatile(int n) => new("volatile-dust", "Volatile Primordial Dust", n);
    private static CubeMat Attuned(int n) => new("attuned-dust", "Attuned Primordial Dust", n);
    private static CubeMat Prism(bool optional = false) => new("tuning-prism", "Tuning Prism", 1, optional);
    private static CubeMat Pandemonium(int n) => new("pandemonium-fragment", "Pandemonium Fragments", n);

    public static IReadOnlyList<CubeSimResult> Evaluate(CubeSimItem it)
    {
        bool Is(params string[] rarities)
        {
            foreach (var r in rarities)
                if (it.Rarity.Equals(r, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        string gaKept = it.GreaterAffixes > 0 ? "Your ⭐ Greater Affix stays untouched." : "";
        string gaLost = it.GreaterAffixes > 0 ? "Your ⭐ Greater Affix does NOT carry: nothing does." : "Nothing carries over.";

        var results = new List<CubeSimResult>();
        void Add(string name, string category, IReadOnlyList<CubeMat> mats, bool eligible,
            string requirement, List<string> outcome, string warning = "", string multi = "")
        {
            // Add Affix only fills an empty slot, so an existing ⭐ is safe. (Remove Affix is the
            // opposite — it targets a RANDOM affix and can hit the ⭐, warned separately below.)
            if (eligible && !string.IsNullOrEmpty(gaKept) && name == "Add Affix")
                outcome.Add(gaKept);
            results.Add(new CubeSimResult(name, category, mats, eligible, eligible ? "" : requirement,
                outcome, warning, multi));
        }

        // ── Gear Modification (game order) ──
        Add("Add Affix", GearModification, new[] { Coarse(1), Raw(5), Prism(optional: true) },
            Is("Common", "Magic", "Rare", "Legendary") && it.OpenSlot,
            !Is("Common", "Magic", "Rare", "Legendary") ? "Common/Magic/Rare/Legendary only"
                : "needs an open affix slot (this item is full: Remove Affix can open one on Magic/Rare)",
            new List<string>
            {
                "Same item, +1 regular affix rolled from the slot's possible pool.",
                "You can't pick which affix: a dud can be rerolled afterwards.",
                "Cannot create a ⭐ Greater Affix.",
                "A Tuning Prism (optional) narrows the affix category.",
            });

        Add("Chaotic Reroll", GearModification, new[] { Refined(1), Raw(15), Prism(optional: true) },
            Is("Magic", "Rare", "Legendary"),
            "Magic/Rare/Legendary only",
            new List<string>
            {
                "Changes a RANDOM affix into one of another category.",
                "A Tuning Prism (optional) sets which category it rolls into.",
                "Higher variance than Focused; the rest of the item is untouched.",
            });

        Add("Focused Reroll", GearModification, new[] { Refined(1), Raw(15), Prism() },
            Is("Magic", "Rare", "Legendary"),
            "Magic/Rare/Legendary only (Uniques use their own power reroll)",
            new List<string>
            {
                "Changes an affix into a different one of the SAME category.",
                "Requires a Tuning Prism to set the category.",
                "The rest of the item is untouched.",
            });

        Add("Remove Affix", GearModification, new[] { Refined(1), Raw(15), Prism(optional: true) },
            Is("Magic", "Rare"),
            "Magic and Rare only",
            new List<string>
            {
                "Removes a RANDOM affix (a Tuning Prism, optional, narrows which one).",
                "Opens a slot for Add Affix: the remove-then-add loop is how crafters sculpt a base.",
            },
            warning: it.GreaterAffixes > 0
                ? "Removal targets a RANDOM affix: it can hit your ⭐ Greater Affix. A Tuning Prism (optional) narrows which affix is chosen."
                : "");

        Add("Transfigure", GearModification, new[] { Volatile(1), Prism(optional: true) },
            Is("Legendary", "Unique", "Mythic"),
            "Legendary/Unique/Mythic only",
            new List<string>
            {
                "Grants a random powerful modification (an extra affix, or a stat pushed toward a ⭐).",
                "A Tuning Prism (optional) narrows the outcomes.",
            },
            "Very high chance the item becomes UNMODIFIABLE afterwards: no more crafting or enchanting. Treat it as near-permanent.");

        // ── Item Transmutation (game order) ──
        Add("Unique Power Reroll", ItemTransmutation, new[] { Attuned(1), Raw(100) },
            Is("Unique", "Mythic"),
            "Uniques and Mythics only",
            new List<string>
            {
                "Rerolls the unique power's VALUES: the power itself stays the same.",
                "S14 widened this: non-Ancestral uniques qualify too.",
            });

        Add("3-to-1 Transmutation", ItemTransmutation, System.Array.Empty<CubeMat>(),
            !Is("Mythic"),
            "not for Mythics",
            new List<string>
            {
                "Three same-type items melt into ONE random item of that type.",
                "Rarity tier is preserved (all-Ancestral in → Ancestral out); affixes are fresh rolls.",
            },
            multi: "needs 3 items of the same type");

        Add("Recycle Uniques", ItemTransmutation, System.Array.Empty<CubeMat>(),
            Is("Unique"),
            "Uniques only",
            new List<string>
            {
                "Three copies of the same Unique become one fresh copy, rerolled.",
                "The classic 'my three bad Tibault's become one hopefully-good one.'",
            },
            multi: "needs 3 IDENTICAL Uniques");

        Add("Upgrade to Unique", ItemTransmutation, new[] { Enhanced(1), Raw(10) },
            Is("Common"),
            "Common (white) items only",
            new List<string>
            {
                "A random UNIQUE of the same item type, freshly rolled.",
            });

        Add("Upgrade to Legendary", ItemTransmutation, new[] { Pure(1), Raw(10) },
            Is("Rare"),
            "Rare items only",
            new List<string>
            {
                "A new LEGENDARY for the same slot: ALL affixes reroll, plus a legendary aspect.",
                gaLost,
                "Upgrade for item POWER and the slot, not to keep stats.",
            });

        Add("Upgrade to Mythic", ItemTransmutation, new[] { Pandemonium(5) },
            Is("Unique") && it.Power850Plus,
            Is("Unique") ? "needs 850+ Item Power" : "needs a Unique at 850+ Item Power",
            new List<string>
            {
                "A random MYTHIC for the same slot: always Ancestral, affixes max-rolled, +30% unique power.",
                gaLost,
                "S14's headline gamble: any qualifying unique is fodder.",
            });

        return results;
    }
}
