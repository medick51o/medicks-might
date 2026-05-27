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
    public ObservableCollection<string> MythicLines { get; } = new();

    public bool HasDropped => DroppedLines.Count > 0;
    public bool HasPurple => UniquePurpleLines.Count > 0;
    public bool HasPending => UniquePendingLines.Count > 0;
    public bool HasMythics => MythicLines.Count > 0;

    // ── Result: the compiled filter + the user's option toggles ──
    [ObservableProperty] private string importCode = "";
    [ObservableProperty] private string filterInfo = "";

    /// <summary>The branded in-game filter title (D4 shows this as the filter name on import).
    /// Auto-seeded as "{TitlePrefix} -- {Source} {Build}"; editable, recompiles live.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitleLengthNote))]
    private string filterTitle = "";
    partial void OnFilterTitleChanged(string value) { if (!_suppressRecompile) Recompile(); }

    /// <summary>Default branding prefix put on every filter — your name rides along into the game.</summary>
    public const string TitlePrefix = "Medick's";
    /// <summary>D4 drops filter/rule names over 24 chars on import (verified in-game), so the title
    /// field is capped here and the encoder clamps too.</summary>
    public const int MaxTitleLength = 24;
    private bool _suppressRecompile;
    public string TitleLengthNote =>
        $"{FilterTitle.Length} / {MaxTitleLength} characters — D4 drops the name above this on import";

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n].TrimEnd();

    // Option toggles — each recompiles the filter live. Defaults = the full recommended filter.
    [ObservableProperty] private bool strictEndgame;
    [ObservableProperty] private bool optPerSlot = true;   // recommended default (falls back to combined w/o slot data)
    [ObservableProperty] private bool optBuildUniques = true;
    [ObservableProperty] private bool optSilverTier = true;
    [ObservableProperty] private bool optItemPowerTiers = true;
    [ObservableProperty] private bool optGreaterAffixes = true;
    [ObservableProperty] private bool optCharmsSeals = true;
    [ObservableProperty] private bool optCodex = true;
    [ObservableProperty] private bool optHideRest = true;

    partial void OnStrictEndgameChanged(bool value) => Recompile();
    partial void OnOptPerSlotChanged(bool value) => Recompile();
    partial void OnOptBuildUniquesChanged(bool value) => Recompile();
    partial void OnOptSilverTierChanged(bool value) => Recompile();
    partial void OnOptItemPowerTiersChanged(bool value) => Recompile();
    partial void OnOptGreaterAffixesChanged(bool value) => Recompile();
    partial void OnOptCharmsSealsChanged(bool value) => Recompile();
    partial void OnOptCodexChanged(bool value) => Recompile();
    partial void OnOptHideRestChanged(bool value) => Recompile();

    private FilterOptions CurrentOptions => new()
    {
        StrictEndgame = StrictEndgame,
        PerSlotRules = OptPerSlot,
        BuildUniques = OptBuildUniques,
        SilverTier = OptSilverTier,
        ItemPowerTiers = OptItemPowerTiers,
        GreaterAffixes = OptGreaterAffixes,
        CharmsSeals = OptCharmsSeals,
        Codex = OptCodex,
        HideRest = OptHideRest,
    };

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

        StatusMessage = "Fetching build…";
        State = AppState.Loading;
        try
        {
            var (resolved, srcLabel) = await Task.Run(async () =>
            {
                // Route by source: d4builds (Firestore) vs Mobalytics (__PRELOADED_STATE__) vs maxroll.
                string? raw = File.Exists(source) ? await File.ReadAllTextAsync(source) : null;
                bool isD4b = raw is not null
                    ? raw.Contains("\"newStats\"") || raw.Contains("databases/(default)")
                    : D4BuildsFetcher.IsD4BuildsUrl(source);
                bool isMoba = raw is not null
                    ? raw.Contains("__PRELOADED_STATE__")
                    : MobalyticsFetcher.IsMobalyticsUrl(source);
                if (isD4b)
                {
                    raw ??= await D4BuildsFetcher.FetchRawAsync(source);
                    return (D4BuildsFetcher.Parse(raw), "D4Builds");
                }
                if (isMoba)
                {
                    raw ??= await MobalyticsFetcher.FetchRawAsync(source);
                    return (MobalyticsFetcher.Parse(raw), "Mobalytics");
                }
                raw ??= await MaxrollFetcher.FetchRawAsync(source);
                return (MaxrollFetcher.Parse(raw, NameLookup.Default(), UniqueLookup.Default()), "Maxroll");
            });
            Ingest(resolved, srcLabel);
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
            Ingest(resolved, "Pasted");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            State = AppState.Input;
        }
    }

    /// <summary>Shared: take a resolved build, fill the variant checklist, compile, show results.</summary>
    private void Ingest(ResolvedBuild resolved, string source)
    {
        _resolved = resolved;
        BuildName = resolved.Build;

        // Auto-brand the in-game filter title (the embedded name D4 shows on import). D4 caps names
        // at 24 chars, so "Medick's {build}" truncated to fit. Editable below (field also capped at 24).
        _suppressRecompile = true;
        FilterTitle = Truncate($"{TitlePrefix} {resolved.Build}", MaxTitleLength);
        _suppressRecompile = false;

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
        var title = string.IsNullOrWhiteSpace(FilterTitle) ? "D4BuildFilter" : FilterTitle.Trim();
        var output = FilterCompiler.Compile(new[] { compiled }, CurrentOptions, "Filter", title);

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
        MythicLines.Clear();
        foreach (var m in compiled.Mythics) MythicLines.Add(m);
        OnPropertyChanged(nameof(HasDropped));
        OnPropertyChanged(nameof(HasPurple));
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(HasMythics));

        ImportCode = output.ImportCode;
        FilterInfo = $"{output.RuleCount} rules · {output.Bytes} bytes · round-trip {(output.RoundTripOk ? "OK ✓" : "FAILED ✗")}";

        StatusMessage = "";
    }

    [RelayCommand]
    private void CopyCode()
    {
        if (string.IsNullOrEmpty(ImportCode)) return;
        try
        {
            Clipboard.SetText(ImportCode);
            StatusMessage = $"Copied the import code ({ImportCode.Length} chars) — "
                + "paste it into D4's Loot Filter → Import.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Clipboard error: {ex.Message}";
        }
    }

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
