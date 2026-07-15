namespace D4BuildFilter.Core;

/// <summary>One S14 talisman set: display name, internal name (bridges to Maxroll planner charm
/// ids), owning class ("Generic" for the small class-agnostic sets), the type-9 set-bonus snoID,
/// and the member charm items (snoID + display name) for the in-game editor's per-item refinement.</summary>
public sealed record TalismanSet(string Name, string InternalName, string Class, uint Id,
    IReadOnlyList<(uint Id, string Name)> Items);

/// <summary>
/// The complete S14 talisman-set catalog — 5 named sets per class × 8 classes + 5 generic small
/// sets = 45. GENERATED 2026-07-02 from d4data 3.1.0.72592 via ThunderEagle/D4LootBench
/// d4-data.json (formatVersion 4). Sescheron's Fury cross-validated byte-for-byte against a real
/// 3.1 in-game exported rule (set 0x22fb15 + its five member ids). Do not hand-edit the table —
/// regenerate from the next data drop instead. Set snoIDs are unchanged from the pre-S14 dump
/// (SetItemBonusDatabase); S14 only added the display names and per-item structure.
/// </summary>
public static class TalismanSetDatabase
{
    public static readonly IReadOnlyList<TalismanSet> All = new TalismanSet[]
    {
        new("Sescheron's Fury", "Talisman_Barb_01", "Barbarian", 0x22fb15u,
            new[] { (0x25069au, "Phoba of Sescheron's Fury"), (0x2506a8u, "Fer of Sescheron's Fury"), (0x2506b5u, "Mlor of Sescheron's Fury"), (0x2506b8u, "Linta of Sescheron's Fury"), (0x2506cdu, "Berú of Sescheron's Fury") }),
        new("Berserker's Crucible", "Talisman_Barb_02", "Barbarian", 0x22fb41u,
            new[] { (0x2506d4u, "Phoba of the Crucible"), (0x2506d7u, "Fer of the Crucible"), (0x2506dcu, "Mlor of the Crucible"), (0x2506dfu, "Linta of the Crucible"), (0x2506e2u, "Berú of the Crucible") }),
        new("Arms of Arreat", "Talisman_Barb_03", "Barbarian", 0x22fce9u,
            new[] { (0x2506e6u, "Phoba of Arreat"), (0x2506eau, "Fer of Arreat"), (0x2506eeu, "Mlor of Arreat"), (0x2506f1u, "Linta of Arreat"), (0x2506f9u, "Berú of Arreat") }),
        new("Bloodletter's Flow", "Talisman_Barb_04", "Barbarian", 0x22fcebu,
            new[] { (0x250701u, "Phoba of the Bloodletter"), (0x250704u, "Fer of the Bloodletter"), (0x25070au, "Mlor of the Bloodletter"), (0x25070du, "Linta of the Bloodletter"), (0x250711u, "Berú of the Bloodletter") }),
        new("Bul-Kathos' Pride", "Talisman_Barb_05", "Barbarian", 0x22fceeu,
            new[] { (0x250715u, "Phoba of Bul-Kathos' Pride"), (0x25071bu, "Fer of Bul-Kathos' Pride"), (0x25071fu, "Mlor of Bul-Kathos' Pride"), (0x250721u, "Linta of Bul-Kathos' Pride"), (0x250725u, "Berú of Bul-Kathos' Pride") }),
        new("Storm Shepherd's Call", "Talisman_Druid_01", "Druid", 0x230326u,
            new[] { (0x25072au, "Phoba of the Den Mother"), (0x25072du, "Fer of the Den Mother"), (0x250731u, "Mlor of the Den Mother"), (0x250734u, "Linta of the Den Mother"), (0x250737u, "Berú of the Den Mother") }),
        new("Song of the Old Mountain", "Talisman_Druid_02", "Druid", 0x230380u,
            new[] { (0x25073bu, "Phoba of the Red Wolf Moon"), (0x25073fu, "Fer of the Red Wolf Moon"), (0x250742u, "Mlor of the Red Wolf Moon"), (0x250749u, "Linta of the Red Wolf Moon"), (0x25074cu, "Berú of the Red Wolf Moon") }),
        new("Might of the Den Mother", "Talisman_Druid_03", "Druid", 0x230383u,
            new[] { (0x250750u, "Phoba of the Storm Shepherd"), (0x250755u, "Fer of the Storm Shepherd"), (0x250757u, "Mlor of the Storm Shepherd"), (0x25075bu, "Linta of the Storm Shepherd"), (0x250760u, "Berú of the Storm Shepherd") }),
        new("Rush of the Red Wolf Moon", "Talisman_Druid_04", "Druid", 0x230386u,
            new[] { (0x250763u, "Phoba of the Old Mountain"), (0x250773u, "Fer of the Old Mountain"), (0x250776u, "Mlor of the Old Mountain"), (0x25077bu, "Linta of the Old Mountain"), (0x25077eu, "Berú of the Old Mountain") }),
        new("Nafain's Bestiary", "Talisman_Druid_05", "Druid", 0x230389u,
            new[] { (0x250782u, "Phoba of Nafain's Bestiary"), (0x250785u, "Fer of Nafain's Bestiary"), (0x250788u, "Mlor of Nafain's Bestiary"), (0x25078bu, "Linta of Nafain's Bestiary"), (0x25078eu, "Berú of Nafain's Bestiary") }),
        new("Radament's Desecration", "Talisman_Necro_01", "Necromancer", 0x230c68u,
            new[] { (0x250791u, "Phoba of the Waking Touch"), (0x250793u, "Fer of the Waking Touch"), (0x250796u, "Mlor of the Waking Touch"), (0x250799u, "Linta of the Waking Touch"), (0x25079bu, "Berú of the Waking Touch") }),
        new("Art of the Bone Weaver", "Talisman_Necro_02", "Necromancer", 0x230d68u,
            new[] { (0x25079fu, "Phoba of the Bone Weaver"), (0x2507a1u, "Fer of the Bone Weaver"), (0x2507a5u, "Mlor of the Bone Weaver"), (0x2507a8u, "Linta of the Bone Weaver"), (0x2507acu, "Berú of the Bone Weaver") }),
        new("Word of the Blood Binder", "Talisman_Necro_03", "Necromancer", 0x230d6au,
            new[] { (0x2507b1u, "Phoba of the Blood Binder"), (0x2507b4u, "Fer of the Blood Binder"), (0x2507b9u, "Mlor of the Blood Binder"), (0x2507bcu, "Linta of the Blood Binder"), (0x2507c0u, "Berú of the Blood Binder") }),
        new("Peace of the Black Shroud", "Talisman_Necro_04", "Necromancer", 0x230d6cu,
            new[] { (0x2507c7u, "Phoba of the Black Shroud"), (0x2507c9u, "Fer of the Black Shroud"), (0x2507ceu, "Mlor of the Black Shroud"), (0x2507d3u, "Linta of the Black Shroud"), (0x2507d7u, "Berú of the Black Shroud") }),
        new("Rathma's Waking Touch", "Talisman_Necro_05", "Necromancer", 0x230d6eu,
            new[] { (0x2507dcu, "Phoba of Desecration"), (0x2507e0u, "Fer of Desecration"), (0x2507e3u, "Mlor of Desecration"), (0x2507e6u, "Linta of Desecration"), (0x2507ebu, "Berú of Desecration") }),
        new("Cathan's Righteous Will", "Talisman_Pala_01", "Paladin", 0x23b4cbu,
            new[] { (0x250e7eu, "Phoba of Righteous Will"), (0x250e80u, "Fer of Righteous Will"), (0x250e82u, "Mlor of Righteous Will"), (0x250e84u, "Linta of Righteous Will"), (0x250e86u, "Berú of Righteous Will") }),
        new("Cathan's Dauntless Faith", "Talisman_Pala_02", "Paladin", 0x23b56au,
            new[] { (0x250e88u, "Phoba of Dauntless Faith"), (0x250e8au, "Fer of Dauntless Faith"), (0x250e8cu, "Mlor of Dauntless Faith"), (0x250e8eu, "Linta of Dauntless Faith"), (0x250e90u, "Berú of Dauntless Faith") }),
        new("Heaven's Radiant Fire", "Talisman_Pala_03", "Paladin", 0x23b56cu,
            new[] { (0x250e92u, "Phoba of Radiant Fire"), (0x250e94u, "Fer of Radiant Fire"), (0x250e96u, "Mlor of Radiant Fire"), (0x250e98u, "Linta of Radiant Fire"), (0x250e9au, "Berú of Radiant Fire") }),
        new("Light's Epiphany", "Talisman_Pala_04", "Paladin", 0x23b56eu,
            new[] { (0x250e9cu, "Phoba of Light's Epiphany"), (0x250e9eu, "Fer of Light's Epiphany"), (0x250ea1u, "Mlor of Light's Epiphany"), (0x250ea3u, "Linta of Light's Epiphany"), (0x250ea5u, "Berú of Light's Epiphany") }),
        new("Cathan's Iron Conviction", "Talisman_Pala_05", "Paladin", 0x23b570u,
            new[] { (0x250ea7u, "Phoba of Iron Conviction"), (0x250ea9u, "Fer of Iron Conviction"), (0x250eabu, "Mlor of Iron Conviction"), (0x250eadu, "Linta of Iron Conviction"), (0x250eafu, "Berú of Iron Conviction") }),
        new("Nilfur's Narrow Eye", "Talisman_Rogue_01", "Rogue", 0x230a6au,
            new[] { (0x250eb1u, "Phoba of the Narrow Eye"), (0x250eb3u, "Fer of the Narrow Eye"), (0x250eb5u, "Mlor of the Narrow Eye"), (0x250eb7u, "Linta of the Narrow Eye"), (0x250ebau, "Berú of the Narrow Eye") }),
        new("Way of the Blurring Blade", "Talisman_Rogue_02", "Rogue", 0x230aa2u,
            new[] { (0x250ebdu, "Phoba of the Blurring Blade"), (0x250ebfu, "Fer of the Blurring Blade"), (0x250ec1u, "Mlor of the Blurring Blade"), (0x250ec3u, "Linta of the Blurring Blade"), (0x250ec5u, "Berú of the Blurring Blade") }),
        new("Applied Alchemy", "Talisman_Rogue_03", "Rogue", 0x230ac8u,
            new[] { (0x250ec7u, "Phoba of Applied Alchemy"), (0x250ec9u, "Fer of Applied Alchemy"), (0x250ecbu, "Mlor of Applied Alchemy"), (0x250ecdu, "Linta of Applied Alchemy"), (0x250ecfu, "Berú of Applied Alchemy") }),
        new("Spellbound Steel", "Talisman_Rogue_04", "Rogue", 0x230acau,
            new[] { (0x250ed1u, "Phoba of Spellbound Steel"), (0x250ed3u, "Fer of Spellbound Steel"), (0x250ed5u, "Mlor of Spellbound Steel"), (0x250ed7u, "Linta of Spellbound Steel"), (0x250ed9u, "Berú of Spellbound Steel") }),
        new("Legacy of the Sightless", "Talisman_Rogue_05", "Rogue", 0x230accu,
            new[] { (0x250edbu, "Phoba of the Sightless"), (0x250eddu, "Fer of the Sightless"), (0x250edfu, "Mlor of the Sightless"), (0x250ee1u, "Linta of the Sightless"), (0x250ee3u, "Berú of the Sightless") }),
        new("Slaughter", "Talisman_Small_Generic01", "Generic", 0x232f8au,
            new[] { (0x2328b0u, "Phoba of Slaughter"), (0x255b18u, "Fer of Slaughter"), (0x255b1au, "Mlor of Slaughter") }),
        new("Practiced Technique", "Talisman_Small_Generic02", "Generic", 0x2331e6u,
            new[] { (0x233a66u, "Phoba of Lethality"), (0x255b1eu, "Fer of Lethality"), (0x255b22u, "Mlor of Lethality") }),
        new("Survival", "Talisman_Small_Generic03", "Generic", 0x233c93u,
            new[] { (0x233c95u, "Phoba of Cruel Fate"), (0x255b24u, "Fer of Cruel Fate"), (0x255b28u, "Mlor of Cruel Fate") }),
        new("Dark Pact", "Talisman_Small_Generic06", "Generic", 0x235120u,
            new[] { (0x2353a6u, "Phoba of Dark Pact"), (0x255b32u, "Fer of Dark Pact"), (0x255b34u, "Mlor of Dark Pact") }),
        new("Mastery", "Talisman_Small_Generic09", "Generic", 0x235942u,
            new[] { (0x23598du, "Phoba of Mastery"), (0x255b3du, "Fer of Mastery") }),
        new("Habacalva's Cauldron", "Talisman_Sorc_01", "Sorcerer", 0x2243bfu,
            new[] { (0x250e4cu, "Phoba of the Cauldron"), (0x250e4eu, "Fer of the Cauldron"), (0x250e50u, "Mlor of the Cauldron"), (0x250e52u, "Linta of the Cauldron"), (0x250e54u, "Berú of the Cauldron") }),
        new("Breath of the Frozen Sea", "Talisman_Sorc_02", "Sorcerer", 0x2242e0u,
            new[] { (0x250e56u, "Phoba of the Frozen Sea"), (0x250e58u, "Fer of the Frozen Sea"), (0x250e5au, "Mlor of the Frozen Sea"), (0x250e5cu, "Linta of the Frozen Sea"), (0x250e5eu, "Berú of the Frozen Sea") }),
        new("Cain's Wild Lightning", "Talisman_Sorc_03", "Sorcerer", 0x225065u,
            new[] { (0x250e60u, "Phoba of Wild Lightning"), (0x250e62u, "Fer of Wild Lightning"), (0x250e64u, "Mlor of Wild Lightning"), (0x250e66u, "Linta of Wild Lightning"), (0x250e68u, "Berú of Wild Lightning") }),
        new("Tal Rasha's Threefold Way", "Talisman_Sorc_04", "Sorcerer", 0x2250e4u,
            new[] { (0x250e6au, "Phoba of the Threefold"), (0x250e6cu, "Fer of the Threefold"), (0x250e6eu, "Mlor of the Threefold"), (0x250e70u, "Linta of the Threefold"), (0x250e72u, "Berú of the Threefold") }),
        new("Tiraj's Uncanny Insight", "Talisman_Sorc_05", "Sorcerer", 0x225135u,
            new[] { (0x250e74u, "Phoba of Uncanny Insight"), (0x250e76u, "Fer of Uncanny Insight"), (0x250e78u, "Mlor of Uncanny Insight"), (0x250e7au, "Linta of Uncanny Insight"), (0x250e7cu, "Berú of Uncanny Insight") }),
        new("Balazan's Bite", "Talisman_Spirit_01", "Spiritborn", 0x231392u,
            new[] { (0x250ee5u, "Phoba of Balazan's Bite"), (0x250ee7u, "Fer of Balazan's Bite"), (0x250ee9u, "Mlor of Balazan's Bite"), (0x250eebu, "Linta of Balazan's Bite"), (0x250eedu, "Berú of Balazan's Bite") }),
        new("Wumba's Embrace", "Talisman_Spirit_02", "Spiritborn", 0x2314deu,
            new[] { (0x250eefu, "Phoba of Wumba's Embrace"), (0x250ef1u, "Fer of Wumba's Embrace"), (0x250ef3u, "Mlor of Wumba's Embrace"), (0x250ef5u, "Linta of Wumba's Embrace"), (0x250ef7u, "Berú of Wumba's Embrace") }),
        new("Rezoka's Rage", "Talisman_Spirit_03", "Spiritborn", 0x2314e0u,
            new[] { (0x250efau, "Phoba of Rezoka's Rage"), (0x250efcu, "Fer of Rezoka's Rage"), (0x250efeu, "Mlor of Rezoka's Rage"), (0x250f00u, "Linta of Rezoka's Rage"), (0x250f05u, "Berú of Rezoka's Rage") }),
        new("Kwatli's Grace", "Talisman_Spirit_04", "Spiritborn", 0x2314e3u,
            new[] { (0x250f0au, "Phoba of Kwatli's Grace"), (0x250f0cu, "Fer of Kwatli's Grace"), (0x250f0eu, "Mlor of Kwatli's Grace"), (0x250f11u, "Linta of Kwatli's Grace"), (0x250f13u, "Berú of Kwatli's Grace") }),
        new("Bliss of the Multitude", "Talisman_Spirit_05", "Spiritborn", 0x2314e7u,
            new[] { (0x250f15u, "Phoba of the Multitude"), (0x250f18u, "Fer of the Multitude"), (0x250f1au, "Mlor of the Multitude"), (0x250f1cu, "Linta of the Multitude"), (0x250f1eu, "Berú of the Multitude") }),
        new("Fulcrum of Mefis", "Talisman_Warlock_01", "Warlock", 0x23b610u,
            new[] { (0x250f20u, "Phoba of Mefis' Fulcrum"), (0x250f23u, "Fer of Mefis' Fulcrum"), (0x250f25u, "Mlor of Mefis' Fulcrum"), (0x250f27u, "Linta of Mefis' Fulcrum"), (0x250f29u, "Berú of Mefis' Fulcrum") }),
        new("Flesh of Abaddon", "Talisman_Warlock_02", "Warlock", 0x23b613u,
            new[] { (0x250f32u, "Phoba of Abaddon's Flesh"), (0x250f34u, "Fer of Abaddon's Flesh"), (0x250f36u, "Mlor of Abaddon's Flesh"), (0x250f38u, "Linta of Abaddon's Flesh"), (0x250f3bu, "Berú of Abaddon's Flesh") }),
        new("Shadow of Harash", "Talisman_Warlock_03", "Warlock", 0x23b616u,
            new[] { (0x250fb8u, "Phoba of Harash's Shadow"), (0x250fbbu, "Fer of Harash's Shadow"), (0x250fbeu, "Mlor of Harash's Shadow"), (0x250fcbu, "Linta of Harash's Shadow"), (0x250fcdu, "Berú of Harash's Shadow") }),
        new("Rite of the Nameless", "Talisman_Warlock_04", "Warlock", 0x23b618u,
            new[] { (0x250fd0u, "Phoba of the Nameless"), (0x250fd4u, "Fer of the Nameless"), (0x250fd6u, "Mlor of the Nameless"), (0x250fdbu, "Linta of the Nameless"), (0x250fddu, "Berú of the Nameless") }),
        new("Chains of Horazon", "Talisman_Warlock_05", "Warlock", 0x23b61au,
            new[] { (0x250fe0u, "Phoba of Horazon's Chains"), (0x250fe3u, "Fer of Horazon's Chains"), (0x250fe7u, "Mlor of Horazon's Chains"), (0x250feau, "Linta of Horazon's Chains"), (0x250fecu, "Berú of Horazon's Chains") }),
    };

    /// <summary>Set-bonus snoID → set.</summary>
    public static readonly IReadOnlyDictionary<uint, TalismanSet> ById =
        All.ToDictionary(s => s.Id);

    /// <summary>Display name → set. Source-provided names must cross this boundary before they
    /// count as detection; a season rename must never silently turn every charm checkbox off.</summary>
    public static readonly IReadOnlyDictionary<string, TalismanSet> ByName =
        All.ToDictionary(s => s.Name, (IEqualityComparer<string>)StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> KnownClasses = All
        .Where(s => s.Class != "Generic")
        .Select(s => s.Class)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToDictionary(c => c, c => c, (IEqualityComparer<string>)StringComparer.OrdinalIgnoreCase);

    /// <summary>Trim source class text and canonicalize catalog-known casing. An unrecognized
    /// nonblank name is preserved for display, while <see cref="IsKnownClass"/> still fails it open.</summary>
    public static string NormalizeClassName(string? className)
    {
        var trimmed = className?.Trim() ?? "";
        if (trimmed.Length == 0) return "Unknown";
        return KnownClasses.TryGetValue(trimmed, out var canonical) ? canonical : trimmed;
    }

    public static bool IsKnownClass(string? className) =>
        KnownClasses.ContainsKey(NormalizeClassName(className));

    public static bool TryGetByName(string name, out TalismanSet set)
    {
        if (name is not null && ByName.TryGetValue(name.Trim(), out var s))
        {
            set = s;
            return true;
        }
        set = null!;
        return false;
    }

    /// <summary>Internal name ("Talisman_Barb_01") → set. The bridge from planner charm ids.</summary>
    public static readonly IReadOnlyDictionary<string, TalismanSet> ByInternalName =
        All.ToDictionary(s => s.InternalName, (IEqualityComparer<string>)StringComparer.OrdinalIgnoreCase);

    /// <summary>Sets offered to a build of <paramref name="className"/>: its five class sets plus
    /// the five generics — the same ten the in-game picker shows.</summary>
    public static IReadOnlyList<TalismanSet> ForClass(string className)
    {
        var normalized = NormalizeClassName(className);
        return All.Where(s => s.Class.Equals(normalized, StringComparison.OrdinalIgnoreCase)
                    || s.Class == "Generic").ToList();
    }

    /// <summary>Choose the sets checked on first load. If none of the detected names belongs to
    /// this class's picker, fail open to every offered set so Hide the rest cannot eat build charms.</summary>
    public static IReadOnlyList<TalismanSet> DefaultSelectionForClass(string className,
        IEnumerable<string> detectedNames, out bool noneDetected)
    {
        var offered = ForClass(className);
        var detected = new HashSet<string>(detectedNames, StringComparer.OrdinalIgnoreCase);
        var matched = offered.Where(s => detected.Contains(s.Name)).ToList();
        noneDetected = matched.Count == 0;
        return noneDetected ? offered : matched;
    }

    public static string UnknownSetWarning(IEnumerable<string> names)
    {
        var seen = string.Join("', '", names);
        return $"Build lists charm set '{seen}' this app doesn't know — showing all sets; app data may be a season behind.";
    }

    /// <summary>Member-charm display name ("Phoba of the Crucible") → owning set. The bridge for
    /// sources that list equipped charms by ITEM name (Mobalytics slot titles). Built with TryAdd
    /// so a future data-drop collision degrades to first-wins instead of a type-init crash.</summary>
    public static readonly IReadOnlyDictionary<string, TalismanSet> ByItemName = BuildItemNameIndex();

    private static Dictionary<string, TalismanSet> BuildItemNameIndex()
    {
        var d = new Dictionary<string, TalismanSet>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in All)
            foreach (var (_, itemName) in s.Items)
                d.TryAdd(itemName, s);
        return d;
    }

    /// <summary>Resolve an equipped charm's ITEM display name to its set (v1.0.5 — feeds the
    /// Mobalytics charm-slot extraction).</summary>
    public static bool TryGetByItemName(string itemName, out TalismanSet set)
    {
        if (itemName is not null && ByItemName.TryGetValue(itemName.Trim(), out var s))
        {
            set = s;
            return true;
        }
        set = null!;
        return false;
    }

    /// <summary>Resolve a planner-style class token + set number ("Barb", 5) via the internal
    /// name ("Talisman_Barb_05").</summary>
    public static bool TryGetByPlannerToken(string classToken, int setNumber, out TalismanSet set)
    {
        if (ByInternalName.TryGetValue($"Talisman_{classToken}_{setNumber:D2}", out var s))
        {
            set = s;
            return true;
        }
        set = null!;
        return false;
    }
}
