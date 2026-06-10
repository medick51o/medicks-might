namespace D4BuildFilter.Core;

/// <summary>Re-syncs persisted favorite tier labels against a freshly fetched tier list, so the
/// landing page never presents a star-time tier as current after a source re-ranks (the
/// "Maxroll moved it S→A but we still show S" problem). Pure store-in/store-out logic — no UI,
/// no clock, no network — so it's unit-testable and web-reusable.</summary>
public static class TierReconciler
{
    /// <summary>Apply one fresh (source, tierKind) list to the store. Only favorites whose Source
    /// AND TierKind match the fetched list are touched — a build absent from, say, the Bossing list
    /// may still be ranked on Endgame, so other kinds say nothing about it. Paste favorites
    /// (TierKind null) are never touched.
    /// Returns how many entries visibly changed (tier moved, delisted, or re-listed) — 0 lets the
    /// caller skip rebuilding favorite chips.</summary>
    public static int Reconcile(IFavoritesStore store, TierList fresh, string source, string tierKind, DateTime nowUtc)
    {
        var byUrl = new Dictionary<string, TierBuild>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in fresh.Builds) byUrl[b.Url] = b;

        var changed = 0;
        foreach (var f in store.All.ToList())
        {
            if (f.TierKind is null) continue;
            if (!string.Equals(f.Source, source, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(f.TierKind, tierKind, StringComparison.OrdinalIgnoreCase)) continue;

            if (byUrl.TryGetValue(f.Url, out var live))
            {
                var moved = !string.Equals(f.Tier, live.Tier, StringComparison.OrdinalIgnoreCase);
                if (moved || f.Delisted)
                {
                    store.Update(f with
                    {
                        Tier = live.Tier,
                        PrevTier = moved ? f.Tier : f.PrevTier,
                        Delisted = false,
                        TierCheckedUtc = nowUtc,
                    });
                    changed++;
                }
                else if (f.TierCheckedUtc is null)
                {
                    // First-ever confirmation: stamp quietly (tooltip freshness cue, no chip change).
                    store.Update(f with { TierCheckedUtc = nowUtc });
                }
            }
            else if (!f.Delisted)
            {
                // Dropped off its own list — the strongest "this pick rotted" signal a season
                // rollover produces. Keep the old Tier so the chip can say "was S".
                store.Update(f with { Delisted = true, TierCheckedUtc = nowUtc });
                changed++;
            }
        }
        return changed;
    }
}
