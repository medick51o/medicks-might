namespace D4BuildFilter.Core;

/// <summary>
/// Unions every selected build into one slot layout: the same rule names a single build uses,
/// with affixes and uniques combined. A generic "Weapons" pool contributes its affixes to both
/// the 1H and 2H buckets and keeps each weapon type on the hand it actually is.
/// </summary>
public static class BuildMerge
{
    public static readonly string[] CanonicalOrder =
    [
        "Helm", "Chest", "2H Weapons", "1H Weapons", "Gloves", "Pants", "Boots", "Ring", "Amulet",
    ];

    public static CompiledBuild Union(IReadOnlyList<CompiledBuild> builds)
    {
        if (builds.Count == 0)
            throw new ArgumentException("At least one build is required.", nameof(builds));

        var first = builds[0];
        var pool = new List<uint>();
        var poolSeen = new HashSet<uint>();
        var names = new Dictionary<uint, string>();
        foreach (var build in builds)
            foreach (var id in build.Pool)
                if (poolSeen.Add(id))
                {
                    pool.Add(id);
                    if (build.Names.TryGetValue(id, out var name)) names[id] = name;
                }
        foreach (var id in pool)
            names.TryAdd(id, "0x" + id.ToString("x"));

        var uniqueIds = new List<uint>();
        var uniqueSeen = new HashSet<uint>();
        var targeted = new List<string>();
        var pending = new List<string>();
        foreach (var build in builds)
        {
            foreach (var id in build.UniqueIds)
                if (uniqueSeen.Add(id)) uniqueIds.Add(id);
            foreach (var name in build.UniquesTargeted)
                if (!targeted.Contains(name, StringComparer.OrdinalIgnoreCase)) targeted.Add(name);
            foreach (var name in build.UniquesPending)
                if (!pending.Contains(name, StringComparer.OrdinalIgnoreCase)) pending.Add(name);
        }

        var buckets = new Dictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase);
        foreach (var build in builds)
            foreach (var slot in build.SlotPools)
                Absorb(buckets, slot);

        var splinters = builds.SelectMany(b => b.SoulSplinterIds).Distinct().ToList();
        return new CompiledBuild(
            first.Name, first.Class, first.Color, first.Dim,
            pool, names,
            builds.SelectMany(b => b.Dropped).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            uniqueIds, targeted, pending,
            Order(buckets),
            builds.SelectMany(b => b.UnmappedSlotPools).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            builds.SelectMany(b => b.TalismanSets).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
        {
            Source = first.Source,
            UnresolvedSlotPools = builds.SelectMany(b => b.UnresolvedSlotPools)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            UnknownTalismanSets = builds.SelectMany(b => b.UnknownTalismanSets)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ChaseColorOverride = first.ChaseColorOverride,
            KeeperColorOverride = first.KeeperColorOverride,
            ChaseColorUserChosen = first.ChaseColorUserChosen,
            KeeperColorUserChosen = first.KeeperColorUserChosen,
            SoulSplinterIds = splinters,
        };
    }

    private sealed class Bucket
    {
        public string Label = "";
        public readonly List<uint> Types = new();
        public readonly HashSet<uint> TypeSeen = new();
        public readonly List<uint> Affixes = new();
        public readonly HashSet<uint> AffixSeen = new();

        public void AddTypes(IEnumerable<uint> ids)
        {
            foreach (var id in ids)
                if (TypeSeen.Add(id)) Types.Add(id);
        }

        public void AddAffixes(IEnumerable<uint> ids)
        {
            foreach (var id in ids)
                if (AffixSeen.Add(id)) Affixes.Add(id);
        }
    }

    private static void Absorb(Dictionary<string, Bucket> buckets, SlotPool slot)
    {
        if (slot.Label.Equals("Other Gear", StringComparison.OrdinalIgnoreCase)
            || slot.ItemTypeIds.Count == 0)
            return;

        var types = slot.ItemTypeIds;
        bool weapon = ItemTypeDatabase.IsWeaponSlot(types);
        bool genericWeapons = slot.Label.Equals("Weapons", StringComparison.OrdinalIgnoreCase);
        if (weapon && (genericWeapons || ItemTypeDatabase.WeaponHandedness(types).Length == 0))
        {
            var one = types.Where(id => ItemTypeDatabase.OneHandedWeapons.Contains(id)).ToList();
            var two = types.Where(id => ItemTypeDatabase.TwoHandedWeapons.Contains(id)).ToList();
            if (genericWeapons || one.Count > 0)
                Add(buckets, "1H Weapons", one, slot.AffixIds);
            if (genericWeapons || two.Count > 0)
                Add(buckets, "2H Weapons", two, slot.AffixIds);
            return;
        }

        if (weapon && ItemTypeDatabase.WeaponHandedness(types) == "2h")
        {
            Add(buckets, "2H Weapons", types, slot.AffixIds);
            return;
        }
        if (weapon)
        {
            Add(buckets, "1H Weapons", types, slot.AffixIds);
            return;
        }

        Add(buckets, CanonicalLabel(slot.Label, types), types, slot.AffixIds);
    }

    private static void Add(Dictionary<string, Bucket> buckets, string label, IEnumerable<uint> types, IEnumerable<uint> affixes)
    {
        if (!buckets.TryGetValue(label, out var bucket))
        {
            bucket = new Bucket { Label = label };
            buckets[label] = bucket;
        }
        bucket.AddTypes(types);
        bucket.AddAffixes(affixes);
    }

    public static string CanonicalLabel(string label, IReadOnlyList<uint> types)
    {
        if (CanonicalOrder.Contains(label, StringComparer.OrdinalIgnoreCase))
            return CanonicalOrder.First(name => name.Equals(label, StringComparison.OrdinalIgnoreCase));

        var resolved = ItemTypeDatabase.ResolveSlot(label);
        var probe = resolved is { Count: > 0 } ? resolved : types;
        if (probe.Count == 1)
        {
            return probe[0] switch
            {
                0x0006d16e => "Helm",
                0x0006d16d => "Chest",
                0x0006d171 => "Gloves",
                0x0006d16f => "Pants",
                0x0006d170 => "Boots",
                0x0006d174 => "Ring",
                0x0006d175 => "Amulet",
                _ => label,
            };
        }
        return label;
    }

    private static List<SlotPool> Order(Dictionary<string, Bucket> buckets)
    {
        var ordered = new List<SlotPool>();
        foreach (var name in CanonicalOrder)
            if (buckets.Remove(name, out var bucket) && bucket.Affixes.Count > 0)
                ordered.Add(new SlotPool(bucket.Label, bucket.Types, bucket.Affixes));
        foreach (var bucket in buckets.Values.OrderBy(b => b.Label, StringComparer.OrdinalIgnoreCase))
            if (bucket.Affixes.Count > 0)
                ordered.Add(new SlotPool(bucket.Label, bucket.Types, bucket.Affixes));
        return ordered;
    }
}
