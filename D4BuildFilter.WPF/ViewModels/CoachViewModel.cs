using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D4BuildFilter.Core;

namespace D4BuildFilter.WPF.ViewModels;

/// <summary>One coached recipe: the goal card + the persistent recipe strip + follow-along steps.
/// Icon is the in-game recipe art (Assets/Cube); empty string falls back to the emoji glyph.</summary>
public sealed record CoachCraft(string Key, string Emoji, string GoalTitle, string GoalBlurb, bool ComingSoon,
    string StripItem, string StripMats, string StripResult, IReadOnlyList<CoachStep> Steps, string Icon = "")
{
    public bool Enabled => !ComingSoon;
}

/// <summary>One step of a coached craft — big type, one thing at a time (second-screen sized).</summary>
public sealed record CoachStep(string Title, IReadOnlyList<string> Lines, string Tip = "");

/// <summary>Icon is the in-game item card art (Assets/Cube); empty string falls back to Glyph
/// (Pandemonium Fragments — its icon isn't in the source bucket).</summary>
public sealed record DustTier(string Glyph, string Name, string Blurb, string Icon = "");

/// <summary>The Crafting Coach (v1 — spec: CRAFTING_COACH_SPEC.md): a follow-along, second-screen
/// Horadric Cube coach. Landing = goal cards + dust decoder; picking a craft enters the step flow.
/// Content mirrors the verified S14 recipe research word-for-word on inputs, mats, and outcomes —
/// outcome RANGES only, never invented percentages; "rerolls unless Occultist-locked" wording.</summary>
public sealed partial class CoachViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLanding))]
    [NotifyPropertyChangedFor(nameof(IsCoaching))]
    [NotifyPropertyChangedFor(nameof(CurrentStep))]
    [NotifyPropertyChangedFor(nameof(StepLabel))]
    private CoachCraft? selectedCraft;

    /// <summary>The Cube Sandbox (v1.0.2): third surface of the wing — rehearse any craft on a
    /// simulated item before risking real mats.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLanding))]
    private bool isSandbox;

    public SandboxViewModel Sandbox { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStep))]
    [NotifyPropertyChangedFor(nameof(StepLabel))]
    private int stepIndex;

    public bool IsLanding => SelectedCraft is null && !IsSandbox;
    public bool IsCoaching => SelectedCraft is not null;
    public CoachStep? CurrentStep =>
        SelectedCraft is { } c && StepIndex >= 0 && StepIndex < c.Steps.Count ? c.Steps[StepIndex] : null;
    public string StepLabel => SelectedCraft is { } c ? $"Step {StepIndex + 1} of {c.Steps.Count}" : "";

    /// <summary>Build-aware greeting — set by MainViewModel whenever a build (re)compiles.</summary>
    [ObservableProperty]
    private string buildLine = "Load a build on the Browse tab and the coach will point at YOUR items.";

    [RelayCommand]
    private void PickCraft(CoachCraft craft)
    {
        if (craft.ComingSoon) return;
        StepIndex = 0;
        SelectedCraft = craft;
    }

    [RelayCommand]
    private void NextStep()
    {
        if (SelectedCraft is { } c && StepIndex < c.Steps.Count - 1) StepIndex++;
    }

    [RelayCommand]
    private void PrevStep()
    {
        if (StepIndex > 0) StepIndex--;
    }

    [RelayCommand]
    private void BackToGoals()
    {
        SelectedCraft = null;
        IsSandbox = false;
    }

    [RelayCommand]
    private void OpenSandbox()
    {
        SelectedCraft = null;
        IsSandbox = true;
    }

    public IReadOnlyList<CoachCraft> Crafts { get; } = new[]
    {
        new CoachCraft("add-affix", "🟡", "Fill an empty slot",
            "Your gold-glowing blues have room for one more affix — put it there.", false,
            "🔵  a 2-affix blue (gold glow)", "1× Coarse  +  5× Raw Primordial Dust", "🔵✨  same item, +1 new affix",
            Icon: "/Assets/Cube/recipe-add-affix.png",
            Steps: new[]
            {
                new CoachStep("Pick your practice piece",
                    new[]
                    {
                        "Want the filter to scout for you? Arm the gold rule: in Filter options, check 'Cube bases (GA blues) → Gold' (off by default), copy the new code, re-import.",
                        "Gold then marks the blues actually worth a stop: BOTH affixes on your build AND at least one Greater Affix (the ⭐) — a premium canvas with an empty slot the cube can fill.",
                        "Gold glows are meant to be RARE — most blues aren't worth your time, and that's the point. For a low-stakes first practice craft, any blue whose two affixes you like will do.",
                    },
                    "Your filter does the homework — this craft is why the gold toggle exists."),
                new CoachStep("Check your dust",
                    new[]
                    {
                        "You need:  1× Coarse Primordial Dust  +  5× Raw Primordial Dust.",
                        "Raw = the water of crafting — salvaging almost any gear at the Blacksmith produces it. You very likely have hundreds.",
                        "Coarse = rarer salvage. If you're short, salvage a few unwanted rares.",
                    }),
                new CoachStep("At the cube",
                    new[]
                    {
                        "Open the Horadric Cube (in town, next to the other crafters).",
                        "Place your blue item into the cube.",
                        "Choose the recipe:  ADD AFFIX.",
                        "Confirm — the dust is consumed, the cube does its thing.",
                    },
                    "Follow along on this screen; click Next when you're ready for the result."),
                new CoachStep("What you get",
                    new[]
                    {
                        "Your item gains +1 regular affix, rolled from the affixes that can appear on that slot.",
                        "You can't choose which affix rolls in — if it's a dud, the 'Fix one bad affix' craft (coming soon here) rerolls it.",
                        "Greater Affixes can't be crafted this way — those only drop on high-tier gear.",
                    },
                    "Congratulations — you just cubed. The mats hoard finally did something."),
            }),
        new CoachCraft("rare-to-leg", "⬆", "Turn a Rare into a Legendary",
            "Gamble a good rare into a legendary — power up, fresh affixes.", false,
            "🟡  a Rare worth gambling", "1× Pure  +  10× Raw Primordial Dust", "🟠  Legendary — ALL affixes reroll",
            Icon: "/Assets/Cube/recipe-upgrade-to-legendary.png",
            Steps: new[]
            {
                new CoachStep("Pick a rare worth gambling",
                    new[]
                    {
                        "Choose a RARE item with high item power for a slot you want to upgrade.",
                        "Important: its affixes will NOT survive — everything rerolls. Item power and the right slot are what matter here.",
                        "Don't feed it your perfectly-rolled rare hoping to keep the stats — this is a dice roll, not a promotion.",
                    },
                    "Upgrade for POWER, not to keep stats."),
                new CoachStep("Check your dust",
                    new[]
                    {
                        "You need:  1× Pure Primordial Dust  +  10× Raw Primordial Dust.",
                        "Pure comes from salvaging rarer gear and from activity rewards — scarcer than Coarse, so spend it on slots that matter.",
                    }),
                new CoachStep("At the cube",
                    new[]
                    {
                        "Open the Horadric Cube.",
                        "Place your rare into the cube.",
                        "Choose the recipe:  UPGRADE TO LEGENDARY.",
                        "Confirm.",
                    }),
                new CoachStep("What you get",
                    new[]
                    {
                        "A LEGENDARY item for the same slot — fresh affixes rolled from scratch, plus a legendary aspect.",
                        "Nothing from the rare carries over (no affix survives, no Greater Affix transfers).",
                        "Like the roll? Wear it. Hate one affix? The Occultist can enchant one — or the reroll crafts (coming soon here) can help.",
                    },
                    "This is the bridge from 'found gear' to 'made gear.'"),
            }),
        new CoachCraft("fix-affix", "🎲", "Fix one bad affix",
            "Reroll the affix that doesn't fit — within or across categories.", true,
            "", "", "", new CoachStep[0], Icon: "/Assets/Cube/recipe-reroll-affix.png"),
        new CoachCraft("melt-junk", "♻", "Melt junk into a reroll",
            "Three same-type items become one fresh roll of that type.", true,
            "", "", "", new CoachStep[0], Icon: "/Assets/Cube/recipe-3-to-1.png"),
    };

    public IReadOnlyList<DustTier> DustTiers { get; } = new[]
    {
        new DustTier("▫", "Raw Primordial Dust", "The water of crafting — salvage almost anything to get it. Fuels every recipe.", "/Assets/Cube/raw-dust.png"),
        new DustTier("◽", "Coarse Primordial Dust", "Add Affix's partner (1× per craft). From salvaging better gear.", "/Assets/Cube/coarse-dust.png"),
        new DustTier("◻", "Refined Primordial Dust", "Fuels the affix rerolls and Remove Affix.", "/Assets/Cube/refined-dust.png"),
        new DustTier("⬜", "Pure Primordial Dust", "The Rare → Legendary upgrade fuel.", "/Assets/Cube/pure-dust.png"),
        new DustTier("💠", "Enhanced Primordial Dust", "Common → Unique upgrades and unique-charm crafting.", "/Assets/Cube/enhanced-dust.png"),
        new DustTier("🔶", "Volatile Primordial Dust", "Transfigure: adds a powerful affix, then LOCKS the item forever.", "/Assets/Cube/volatile-dust.png"),
        new DustTier("🔷", "Attuned Primordial Dust", "Rerolls a Unique's power values.", "/Assets/Cube/attuned-dust.png"),
        new DustTier("🟣", "Pandemonium Fragments", "Five of these + an 850+ Unique = one Mythic gamble (S14's headline recipe)."),
        new DustTier("🟩", "Infused Horadric Resin", "The charm/talisman recipes' special sauce.", "/Assets/Cube/horadric-resin.png"),
    };
}

/// <summary>The Cube Sandbox, shaped like the in-game Horadric Cube screen: a "pretend item" (four
/// switches: rarity · power band · GA count · open slot) drives a grouped recipe list (Gear
/// Modification / Item Transmutation). Eligible recipes read normally, ineligible ones dim. Selecting
/// a recipe fills a detail panel with its REQUIRED MATERIALS (game-style icon + name + ×qty), the
/// honest outcome, and any warning — all live via <see cref="CubeSimulator"/>. The item-first angle
/// (pick an item, see what it can become) is the thing the in-game recipe list can't do for you.</summary>
public sealed partial class SandboxViewModel : ObservableObject
{
    public IReadOnlyList<string> RarityOptions { get; } =
        new[] { "Common", "Magic", "Rare", "Legendary", "Unique", "Mythic" };
    public IReadOnlyList<int> GaOptions { get; } = new[] { 0, 1, 2 };

    [ObservableProperty] private string selectedRarity = "Magic";
    [ObservableProperty] private bool power850Plus = true;
    [ObservableProperty] private int greaterAffixes;
    [ObservableProperty] private bool openSlot = true;

    /// <summary>The recipe whose details fill the right-hand panel. Kept pointed at the same recipe
    /// (by name) as the switches change, so the panel updates live instead of clearing.</summary>
    [ObservableProperty] private CubeSimResult? selectedRecipe;

    /// <summary>All recipes for the current pretend item, in the game's order.</summary>
    public ObservableCollection<CubeSimResult> Recipes { get; } = new();

    /// <summary>The recipe list grouped into the two in-game sections (Gear Modification / Item
    /// Transmutation) for the section headers, matching the game's RECIPES panel.</summary>
    public System.Windows.Data.ListCollectionView RecipesView { get; }

    public SandboxViewModel()
    {
        RecipesView = new System.Windows.Data.ListCollectionView(Recipes);
        RecipesView.GroupDescriptions.Add(
            new System.Windows.Data.PropertyGroupDescription(nameof(CubeSimResult.Category)));
        Reevaluate();
    }

    partial void OnSelectedRarityChanged(string value) => Reevaluate();
    partial void OnPower850PlusChanged(bool value) => Reevaluate();
    partial void OnGreaterAffixesChanged(int value) => Reevaluate();
    partial void OnOpenSlotChanged(bool value) => Reevaluate();

    private void Reevaluate()
    {
        var all = CubeSimulator.Evaluate(new CubeSimItem(SelectedRarity, Power850Plus, GreaterAffixes, OpenSlot));
        Recipes.Clear();
        foreach (var r in all) Recipes.Add(r);
        // Preserve the selection across a switch flip so the detail panel tracks the same recipe;
        // fall back to the first eligible recipe, then just the first.
        var keepName = SelectedRecipe?.Name;
        SelectedRecipe = all.FirstOrDefault(r => r.Name == keepName)
                         ?? all.FirstOrDefault(r => r.Eligible)
                         ?? all.FirstOrDefault();
    }

    [RelayCommand]
    private void SelectRecipe(CubeSimResult recipe) => SelectedRecipe = recipe;
}
