using D4BuildFilter.Core;
using D4BuildFilter.WPF.ViewModels;
using Xunit;

namespace D4BuildFilter.Tests;

public class MainViewModelAdvisoryTests
{
    private const string Advisory =
        "A rule uses an unregistered color; its rule-name suffix was safely shown as (Custom).";

    /// <summary>Reverting advisory routing hides compiler notes or moves cosmetic advice into a blocking warning.</summary>
    [Fact]
    public void Compiler_advisories_reach_the_result_advisory_band_without_blocking_share()
    {
        var vm = new MainViewModel(startTierListFetches: false, compileWithinCap: CompileWithAdvisory);

        vm.Ingest(new ResolvedBuild("Test Build", "Rogue",
        [
            new ResolvedVariant("Endgame",
                ["Dexterity", "Critical Strike Chance", "Maximum Life"], [])
        ]), "Test");

        Assert.Contains(Advisory, vm.SuperBuildAdvisory);
        Assert.DoesNotContain(Advisory, vm.CapWarning);
        Assert.True(vm.CanShareWitnessCard);
    }

    private static (FilterOutput Output, CompileFitReport Fit) CompileWithAdvisory(
        IReadOnlyList<CompiledBuild> builds, FilterOptions options, int maxRules,
        string label, string title)
    {
        var output = FilterCompiler.CompileWithinCap(builds, options, maxRules,
            out CompileFitReport fit, label, title);
        return (output with { Advisories = [Advisory] }, fit);
    }
}
