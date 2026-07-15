using D4BuildFilter.Core;
using Xunit;

namespace D4BuildFilter.Tests;

public class BuildDriftTests
{
    private static ResolvedBuild Build(
        IReadOnlyList<string>? affixes = null,
        IReadOnlyList<string>? uniques = null,
        IReadOnlyList<string>? sets = null,
        IReadOnlyList<ResolvedSlot>? slots = null) =>
        new("Test", "Barbarian",
        [
            new ResolvedVariant("Endgame", affixes ?? [], uniques ?? [], slots, sets)
        ]);

    [Fact]
    public void Reports_added_and_removed_affixes_uniques_and_sets()
    {
        var baseline = BuildSnapshot.Capture(Build(
            affixes: ["Movement Speed"],
            uniques: ["Ramaladni's Magnum Opus"],
            sets: ["Old Set"]), new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
        var fresh = Build(
            affixes: ["Fury Cost Reduction"],
            uniques: ["The Grandfather"],
            sets: ["New Set"]);

        var diff = BuildDrift.Compare(baseline, fresh)!;

        Assert.True(diff.HasDrift);
        Assert.Contains(diff.Changes, c => c is { Added: true, Kind: BuildDriftKind.Affix, Name: "Fury Cost Reduction" });
        Assert.Contains(diff.Changes, c => c is { Added: false, Kind: BuildDriftKind.Affix, Name: "Movement Speed" });
        Assert.Contains(diff.Changes, c => c is { Added: true, Kind: BuildDriftKind.Unique, Name: "The Grandfather" });
        Assert.Contains(diff.Changes, c => c is { Added: false, Kind: BuildDriftKind.Unique, Name: "Ramaladni's Magnum Opus" });
        Assert.Contains(diff.Changes, c => c is { Added: true, Kind: BuildDriftKind.TalismanSet, Name: "New Set" });
        Assert.Contains(diff.Changes, c => c is { Added: false, Kind: BuildDriftKind.TalismanSet, Name: "Old Set" });
    }

    [Fact]
    public void Ignores_order_case_and_whitespace_only_edits()
    {
        var baseline = BuildSnapshot.Capture(new ResolvedBuild("Test", "Barbarian",
        [
            new ResolvedVariant(" Endgame ", ["Movement   Speed", "Strength"],
                ["The Grandfather", "Harlequin Crest"], null, ["Earth Set", "Fire Set"])
        ]));
        var fresh = new ResolvedBuild("Test", "Barbarian",
        [
            new ResolvedVariant("endgame", [" strength ", "movement speed"],
                ["harlequin crest", "THE GRANDFATHER"], null, [" fire set ", "EARTH SET"])
        ]);

        var diff = BuildDrift.Compare(baseline, fresh)!;

        Assert.False(diff.HasDrift);
        Assert.Empty(diff.Changes);
    }

    [Fact]
    public void Preserves_slot_on_affix_change()
    {
        var baseline = BuildSnapshot.Capture(Build(slots: [new ResolvedSlot("Boots", ["Movement Speed"])]));
        var diff = BuildDrift.Compare(baseline,
            Build(slots: [new ResolvedSlot(" boots ", ["Fury Cost Reduction"])]))!;

        Assert.Contains(diff.Changes, c => c.PlainText == "+ Fury Cost Reduction (boots)");
        Assert.Contains(diff.Changes, c => c.PlainText == "− Movement Speed (boots)");
    }

    [Fact]
    public void Missing_snapshot_has_no_drift_opinion()
    {
        Assert.Null(BuildDrift.Compare(null, Build(affixes: ["Strength"])));
    }

    [Fact]
    public void Reports_build_name_and_class_changes_even_when_variant_content_is_identical()
    {
        var baseline = BuildSnapshot.Capture(new ResolvedBuild("Speedfarm", "Barbarian",
        [
            new ResolvedVariant("Endgame", ["Strength"], ["The Grandfather"])
        ]));
        var fresh = new ResolvedBuild("Pit Push", "Sorcerer",
        [
            new ResolvedVariant("Endgame", ["Strength"], ["The Grandfather"])
        ]);

        var diff = BuildDrift.Compare(baseline, fresh)!;

        Assert.True(diff.HasDrift);
        Assert.Contains(diff.Changes, c => c.PlainText == "− build Speedfarm");
        Assert.Contains(diff.Changes, c => c.PlainText == "+ build Pit Push");
        Assert.Contains(diff.Changes, c => c.PlainText == "− class Barbarian");
        Assert.Contains(diff.Changes, c => c.PlainText == "+ class Sorcerer");
    }
}
