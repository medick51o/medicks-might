using System.Text;
using D4BuildFilter.Core;
using D4BuildFilter.WPF.ViewModels;
using Xunit;

namespace D4BuildFilter.Tests;

public class Segment2DisplayTests
{
    /// <summary>Reverting S2 leaves every multi-build import named after only the first build.</summary>
    [Theory]
    [InlineData(new[] { "One", "Two" }, "One + Two")]
    [InlineData(new[] { "One", "Two", "Three" }, "One + Two + Three")]
    [InlineData(new[] { "One", "Two", "Three", "Four" }, "One + Two + Three + Four")]
    public void Multi_build_title_defaults_to_tags_in_selection_order(string[] names, string expected)
    {
        var vm = Ingest(names.Select(name => Build(name)).ToArray());

        Assert.Equal(Utf8(expected), Utf8(vm.FilterTitle));
    }

    /// <summary>Reverting S2 truncates or overflows crowded titles instead of keeping/folding whole tags.</summary>
    [Theory]
    [InlineData(new[] { "Alphaomega", "Betagammad" }, "Alphaomega + Betagammad")]
    [InlineData(new[] { "Alphaomega", "Betagammad", "Third" }, "Alphaomega +2")]
    [InlineData(new[] { "Alphaomega", "Betagammad", "Third", "Fourth" }, "Alphaomega +3")]
    public void Multi_build_title_keeps_whole_tags_and_folds_the_remainder(string[] names, string expected)
    {
        var vm = Ingest(names.Select(name => Build(name)).ToArray());

        Assert.Equal(Utf8(expected), Utf8(vm.FilterTitle));
        Assert.True(vm.FilterTitle.Length <= MainViewModel.MaxTitleLength);
    }

    /// <summary>Reverting S2 overwrites a typed title or lets dirty state leak through restart/selection changes.</summary>
    [Fact]
    public void Dirty_title_survives_recompile_and_resets_for_restart_and_a_different_selection()
    {
        var firstSelection = new[] { Build("One"), Build("Two") };
        var vm = Ingest(firstSelection);
        vm.FilterTitle = "My Custom Filter";

        vm.OptGreaterAffixes = !vm.OptGreaterAffixes;
        Assert.Equal("My Custom Filter", vm.FilterTitle);

        vm.Ingest(firstSelection[0], "Test", firstSelection.Skip(1).ToList());
        Assert.Equal("My Custom Filter", vm.FilterTitle);

        vm.RestartCommand.Execute(null);
        vm.Ingest(firstSelection[0], "Test", firstSelection.Skip(1).ToList());
        Assert.Equal("One + Two", vm.FilterTitle);

        vm.FilterTitle = "Another Custom Filter";
        vm.Ingest(Build("Three"), "Test", [Build("Four")]);
        Assert.Equal("Three + Four", vm.FilterTitle);
    }

    /// <summary>Reverting the URL identity key preserves a dirty title when a same-named guide URL changes.</summary>
    [Fact]
    public void Dirty_title_resets_when_an_identically_named_guide_url_changes()
    {
        var first = new[]
        {
            Build("One") with { SourceUrl = "https://example.test/one-v1" },
            Build("Two") with { SourceUrl = "https://example.test/two" },
        };
        var vm = Ingest(first);
        vm.FilterTitle = "My Custom Filter";

        vm.Ingest(first[0] with { SourceUrl = "https://example.test/one-v2" }, "Test", [first[1]]);

        Assert.Equal("One + Two", vm.FilterTitle);
    }

    /// <summary>Reverting input commit trimming makes the visible title disagree with the encoded title.</summary>
    [Fact]
    public void Committing_a_typed_title_trims_the_display_and_wire_to_the_same_value()
    {
        var vm = Ingest(Build("One"));
        vm.FilterTitle = " Raid Filter ";

        vm.CommitFilterTitle();

        Assert.Equal("Raid Filter", vm.FilterTitle);
        Assert.Equal("Raid Filter", FilterDecoder.Decode(vm.ImportCode).Name);
    }

    /// <summary>Reverting S2 removes the owning build tag from a unique used by only one selected build.</summary>
    [Fact]
    public void Multi_build_unique_displays_its_single_owner_tag()
    {
        var vm = Ingest(
            Build("Heartseeker", "Scoundrel's Kiss", "Future Unique"),
            Build("Death Trap", "Tyrael's Might"));

        Assert.Contains("Scoundrel's Kiss (HrtSkr)", vm.UniquePurpleLines);
        Assert.Contains("Future Unique (HrtSkr)", vm.UniquePendingLines);
    }

    /// <summary>Reverting S2 duplicates a shared unique or hides which selected builds own it.</summary>
    [Fact]
    public void Shared_unique_displays_once_with_all_owner_tags_in_selection_order()
    {
        var vm = Ingest(
            Build("Death Trap", "Scoundrel's Kiss"),
            Build("Heartseeker", "Scoundrel's Kiss"));

        Assert.Equal(["Scoundrel's Kiss (DeathTrap, HrtSkr)"],
            vm.UniquePurpleLines.Where(line => line.StartsWith("Scoundrel's Kiss", StringComparison.Ordinal)));
    }

    /// <summary>Reverting S2 loses deterministic owner ordering when attribution scales to four builds.</summary>
    [Fact]
    public void Shared_unique_lists_all_four_owner_tags_in_selection_order()
    {
        var vm = Ingest(
            Build("One", "Scoundrel's Kiss"),
            Build("Two", "Scoundrel's Kiss"),
            Build("Three", "Scoundrel's Kiss"),
            Build("Four", "Scoundrel's Kiss"));

        Assert.Equal(["Scoundrel's Kiss (One, Two, Three, Four)"],
            vm.UniquePurpleLines.Where(line => line.StartsWith("Scoundrel's Kiss", StringComparison.Ordinal)));
    }

    /// <summary>Reverting owner-tag attribution loses the collision suffix that identifies the owning build.</summary>
    [Fact]
    public void Unique_owner_attribution_keeps_a_collision_suffix()
    {
        var vm = Ingest(
            new ResolvedBuild("Minion Build", "Barbarian",
            [
                new ResolvedVariant("Endgame",
                    ["Strength", "Critical Strike Chance", "Maximum Life"], ["Scoundrel's Kiss"])
            ]) { Source = "Test" },
            new ResolvedBuild("Minion Guide", "Necromancer",
            [
                new ResolvedVariant("Endgame",
                    ["Intelligence", "Critical Strike Chance", "Maximum Life"], [])
            ]) { Source = "Test" });

        Assert.Contains("Scoundrel's Kiss (Minion B)", vm.UniquePurpleLines);
    }

    /// <summary>Reverting the single-build guard changes today's title or uniques display bytes.</summary>
    [Fact]
    public void Single_build_title_and_unique_display_bytes_remain_pinned()
    {
        var vm = Ingest(Build("Tiny", "Scoundrel's Kiss", "Future Unique"));

        Assert.Equal(Utf8("MedicK's Might · Tiny"), Utf8(vm.FilterTitle));
        Assert.Equal([Utf8("Scoundrel's Kiss")], vm.UniquePurpleLines.Select(Utf8));
        Assert.Equal([Utf8("Future Unique")], vm.UniquePendingLines.Select(Utf8));
    }

    private static MainViewModel Ingest(params ResolvedBuild[] builds)
    {
        var vm = new MainViewModel(startTierListFetches: false);
        vm.Ingest(builds[0], "Test", builds.Skip(1).ToList());
        return vm;
    }

    private static ResolvedBuild Build(string name, params string[] uniques) =>
        new(name, "Rogue",
        [
            new ResolvedVariant("Endgame",
                ["Dexterity", "Critical Strike Chance", "Vulnerable Damage Multiplier"], uniques)
        ]) { Source = "Test" };

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
}
