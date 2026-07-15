using D4BuildFilter.Core;
using D4BuildFilter.WPF.ViewModels;
using Xunit;

namespace D4BuildFilter.Tests;

public class MainViewModelCopyTests
{
    [Fact]
    public async Task Clipboard_failure_replaces_prior_success_on_visible_result_surface()
    {
        var clipboardFails = false;
        var vm = new MainViewModel(startTierListFetches: false, setClipboardText: _ =>
        {
            if (clipboardFails) throw new InvalidOperationException("clipboard unavailable");
        });
        vm.Ingest(new ResolvedBuild("Test Build", "Barbarian",
        [
            new ResolvedVariant("Endgame", ["Strength"], [])
        ]), "Test");

        await vm.CopyCodeCommand.ExecuteAsync(null);
        Assert.StartsWith("✓ Copied", vm.CopyConfirmation);

        clipboardFails = true;
        await vm.CopyCodeCommand.ExecuteAsync(null);

        Assert.Equal("⚠ Couldn't copy to the clipboard — try again.", vm.CopyConfirmation);
        Assert.DoesNotContain("clipboard unavailable", vm.CopyConfirmation);
        Assert.Equal("📋 Copy", vm.CopyButtonText);
    }

    [Fact]
    public async Task Empty_variant_selection_retires_code_and_reselection_restores_it()
    {
        var vm = new MainViewModel(startTierListFetches: false);
        var build = new ResolvedBuild("Test Build", "Barbarian",
        [
            new ResolvedVariant("Endgame", ["Strength"], [])
        ]);
        vm.Ingest(build, "Test");
        var originalCode = vm.ImportCode;
        Assert.NotEmpty(originalCode);

        vm.CopyConfirmation = "stale confirmation";
        Assert.Single(vm.Variants).IsSelected = false;

        Assert.Empty(vm.ImportCode);
        Assert.Empty(vm.FilterInfo);
        Assert.Empty(vm.AutoFitNote);
        Assert.Empty(vm.CapWarning);
        Assert.Empty(vm.CopyConfirmation);

        await vm.CopyCodeCommand.ExecuteAsync(null);

        Assert.Equal("Select at least one variant to include in the filter.", vm.CopyConfirmation);
        Assert.Empty(vm.ImportCode);

        Assert.Single(vm.Variants).IsSelected = true;

        Assert.Equal(originalCode, vm.ImportCode);
        Assert.NotEqual("Select at least one variant to include in the filter.", vm.FilterInfo);
        Assert.Empty(vm.CopyConfirmation);
    }
}
