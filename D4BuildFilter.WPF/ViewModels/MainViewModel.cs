using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D4BuildFilter.Core;

namespace D4BuildFilter.WPF.ViewModels;

public enum AppState
{
    Input,
    Loading,
    Result,
}

public partial class MainViewModel : ObservableObject
{
    // ── App state ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInputState))]
    [NotifyPropertyChangedFor(nameof(IsLoadingState))]
    [NotifyPropertyChangedFor(nameof(IsResultState))]
    private AppState state = AppState.Input;

    public bool IsInputState => State == AppState.Input;
    public bool IsLoadingState => State == AppState.Loading;
    public bool IsResultState => State == AppState.Result;

    // ── Input ──
    [ObservableProperty]
    private string maxrollUrl = "";

    [ObservableProperty]
    private string statusMessage = "";

    // Input mode: maxroll URL vs. pasted affix list (universal import).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUrlMode))]
    [NotifyPropertyChangedFor(nameof(IsPasteMode))]
    private bool pasteMode;

    [ObservableProperty]
    private string pastedText = "";

    public bool IsUrlMode => !PasteMode;
    public bool IsPasteMode => PasteMode;

    [RelayCommand] private void UseUrlMode() => PasteMode = false;
    [RelayCommand] private void UsePasteMode() => PasteMode = true;

    // ── Result: header ──
    [ObservableProperty]
    private string buildName = "";

    [ObservableProperty]
    private string buildSubtitle = "";

    // The fetched build, kept so variant toggles can recompile from a subset.
    private ResolvedBuild? _resolved;

    public ObservableCollection<VariantOption> Variants { get; } = new();
    public ObservableCollection<string> PoolLines { get; } = new();
    public ObservableCollection<string> DroppedLines { get; } = new();
    public ObservableCollection<string> UniquePurpleLines { get; } = new();
    public ObservableCollection<string> UniquePendingLines { get; } = new();

    public bool HasDropped => DroppedLines.Count > 0;
    public bool HasPurple => UniquePurpleLines.Count > 0;
    public bool HasPending => UniquePendingLines.Count > 0;

    // ── Result: the two compiled codes + the Normal/Strict toggle ──
    private string normalCode = "";
    private string strictCode = "";

    [ObservableProperty]
    private string normalInfo = "";

    [ObservableProperty]
    private string strictInfo = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentCode))]
    [NotifyPropertyChangedFor(nameof(CurrentInfo))]
    [NotifyPropertyChangedFor(nameof(ActiveModeLabel))]
    [NotifyPropertyChangedFor(nameof(NormalSelected))]
    [NotifyPropertyChangedFor(nameof(StrictSelected))]
    private bool showStrict;

    public string CurrentCode => ShowStrict ? strictCode : normalCode;
    public string CurrentInfo => ShowStrict ? StrictInfo : NormalInfo;
    public string ActiveModeLabel => ShowStrict
        ? "STRICT ENDGAME — only Ancestral (top item-power) gear highlights. Built for T6+ farming; hides everything while leveling."
        : "NORMAL — highlights build gear at any item power. Good all-round / leveling filter.";
    public bool NormalSelected => !ShowStrict;
    public bool StrictSelected => ShowStrict;

    // ── Commands ──

    [RelayCommand]
    private async Task CompileAsync() => await RunCompileAsync(MaxrollUrl);

    [RelayCommand]
    private async Task LoadSampleAsync()
    {
        var sample = FindSample();
        if (sample is null)
        {
            StatusMessage = "Bundled sample build not found next to the app.";
            return;
        }
        await RunCompileAsync(sample);
    }

    /// <summary>Fetch + parse the build (URL or local planner .json), then hand off to
    /// <see cref="Recompile"/>. Runs the network/parse off the UI thread.</summary>
    private async Task RunCompileAsync(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            StatusMessage = "Paste a maxroll planner URL first (or click \"Load sample build\").";
            return;
        }

        StatusMessage = "Fetching build from maxroll…";
        State = AppState.Loading;
        try
        {
            var resolved = await Task.Run(async () =>
            {
                string raw = File.Exists(source)
                    ? await File.ReadAllTextAsync(source)
                    : await MaxrollFetcher.FetchRawAsync(source);
                return MaxrollFetcher.Parse(raw, NameLookup.Default(), UniqueLookup.Default());
            });
            Ingest(resolved);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            State = AppState.Input;
        }
    }

    /// <summary>Compile from a pasted affix/unique list (universal import — any build guide).</summary>
    [RelayCommand]
    private async Task CompilePasteAsync()
    {
        if (string.IsNullOrWhiteSpace(PastedText))
        {
            StatusMessage = "Paste an affix list (and any unique names) first.";
            return;
        }
        StatusMessage = "Parsing pasted build…";
        State = AppState.Loading;
        try
        {
            var resolved = await Task.Run(() => PastedBuild.Parse(PastedText, "Pasted Build"));
            Ingest(resolved);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            State = AppState.Input;
        }
    }

    /// <summary>Shared: take a resolved build, fill the variant checklist, compile, show results.</summary>
    private void Ingest(ResolvedBuild resolved)
    {
        _resolved = resolved;
        BuildName = resolved.Build;

        // Populate the variant checklist (all on by default). The field initializer keeps
        // IsSelected=true without firing OnIsSelectedChanged, so Recompile runs just once below.
        Variants.Clear();
        foreach (var v in resolved.Variants)
            Variants.Add(new VariantOption(v, Recompile));

        Recompile();
        State = AppState.Result;
    }

    /// <summary>Analyze + compile from the currently-selected variants. Re-runs whenever the
    /// user toggles a variant checkbox. Pure/synchronous (no network) so it's fine on the UI thread.</summary>
    private void Recompile()
    {
        if (_resolved is null) return;

        var selected = Variants.Where(v => v.IsSelected).Select(v => v.Variant).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "Select at least one variant to include in the filter.";
            return;
        }

        var build = _resolved with { Variants = selected };
        var compiled = FilterCompiler.Analyze(build, FilterColors.Gold, FilterColors.Silver);
        var normal = FilterCompiler.Compile(new[] { compiled }, strictEndgame: false, "NORMAL");
        var strict = FilterCompiler.Compile(new[] { compiled }, strictEndgame: true, "STRICT ENDGAME");

        BuildSubtitle = $"{_resolved.Class}   •   {selected.Count} of {_resolved.Variants.Count} variants   •   "
            + $"{compiled.Pool.Count} filterable affixes in the pool";

        PoolLines.Clear();
        foreach (var name in compiled.PoolNames) PoolLines.Add(name);
        DroppedLines.Clear();
        foreach (var d in compiled.Dropped) DroppedLines.Add(d);
        UniquePurpleLines.Clear();
        foreach (var u in compiled.UniquesTargeted) UniquePurpleLines.Add(u);
        UniquePendingLines.Clear();
        foreach (var u in compiled.UniquesPending) UniquePendingLines.Add(u);
        OnPropertyChanged(nameof(HasDropped));
        OnPropertyChanged(nameof(HasPurple));
        OnPropertyChanged(nameof(HasPending));

        normalCode = normal.ImportCode;
        strictCode = strict.ImportCode;
        NormalInfo = $"{normal.RuleCount} rules · {normal.Bytes} bytes · round-trip {(normal.RoundTripOk ? "OK ✓" : "FAILED ✗")}";
        StrictInfo = $"{strict.RuleCount} rules · {strict.Bytes} bytes · round-trip {(strict.RoundTripOk ? "OK ✓" : "FAILED ✗")}";
        OnPropertyChanged(nameof(CurrentCode));
        OnPropertyChanged(nameof(CurrentInfo));

        StatusMessage = "";
    }

    [RelayCommand]
    private void CopyCode()
    {
        if (string.IsNullOrEmpty(CurrentCode)) return;
        try
        {
            Clipboard.SetText(CurrentCode);
            StatusMessage = $"Copied the {(ShowStrict ? "STRICT" : "NORMAL")} import code "
                + $"({CurrentCode.Length} chars) — paste it into D4's Loot Filter → Import.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Clipboard error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void UseNormal() => ShowStrict = false;

    [RelayCommand]
    private void UseStrict() => ShowStrict = true;

    [RelayCommand]
    private void OpenMaxroll() => OpenUrl("https://maxroll.gg/d4/build-guides");

    [RelayCommand]
    private void Restart()
    {
        State = AppState.Input;
        StatusMessage = "";
        _resolved = null;
        Variants.Clear();
        PoolLines.Clear();
        DroppedLines.Clear();
        UniquePurpleLines.Clear();
        UniquePendingLines.Clear();
    }

    // ── Helpers ──

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open browser: {ex.Message}";
        }
    }

    /// <summary>Locate the bundled offline demo planner — next to the exe first, then the repo.</summary>
    private static string? FindSample()
    {
        foreach (var p in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "Assets", "sample_barb.json"),
                     @"C:\Sync\Projects\D4BuildFilter\sample_barb.json",
                 })
            if (File.Exists(p)) return p;
        return null;
    }
}
