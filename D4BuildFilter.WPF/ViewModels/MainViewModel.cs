using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D4BuildFilter.Core;
using D4BuildFilter.WPF;
using D4BuildFilter.WPF.Services;

namespace D4BuildFilter.WPF.ViewModels;

internal delegate (FilterOutput Output, CompileFitReport Fit) CompileWithinCapDelegate(
    IReadOnlyList<CompiledBuild> builds, FilterOptions options, int maxRules, string label, string title);

public enum AppState
{
    Input,
    Loading,
    Result,
}

/// <summary>Top-nav destinations (the web-app shell). Browse = tier lists, Compile = the URL/paste
/// form, Favorites = saved builds, Artwork = gallery mode (chrome only — the warlord art at full
/// strength). The Result view is shown on top after a compile.</summary>
public enum InputTab { Browse, Compile, Favorites, Artwork, Coach }

public partial class MainViewModel : ObservableObject
{
    // ── App state ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInputState))]
    [NotifyPropertyChangedFor(nameof(IsLoadingState))]
    [NotifyPropertyChangedFor(nameof(IsResultState))]
    [NotifyPropertyChangedFor(nameof(IsNotInputState))]
    private AppState state = AppState.Input;

    public bool IsInputState => State == AppState.Input;
    public bool IsLoadingState => State == AppState.Loading;
    public bool IsResultState => State == AppState.Result;
    /// <summary>Header swaps between a big "wtf Immortan Joe" splash (landing) and a slim banner
    /// (loading + result). Once business starts, the art shrinks down so the build screen drives.</summary>
    public bool IsNotInputState => State != AppState.Input;

    // ── Top-nav tab (web-app shell): which Input sub-view is showing ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowseTab))]
    [NotifyPropertyChangedFor(nameof(IsCompileTab))]
    [NotifyPropertyChangedFor(nameof(IsFavTab))]
    [NotifyPropertyChangedFor(nameof(IsArtworkTab))]
    [NotifyPropertyChangedFor(nameof(IsCoachTab))]
    private InputTab activeTab = InputTab.Browse;

    public bool IsBrowseTab => ActiveTab == InputTab.Browse;
    public bool IsCompileTab => ActiveTab == InputTab.Compile;
    public bool IsFavTab => ActiveTab == InputTab.Favorites;
    public bool IsArtworkTab => ActiveTab == InputTab.Artwork;
    public bool IsCoachTab => ActiveTab == InputTab.Coach;

    /// <summary>The Crafting Coach wing (v1.0.2) — see CRAFTING_COACH_SPEC.md.</summary>
    public CoachViewModel Coach { get; } = new();

    /// <summary>Switch the top-nav tab. Also returns to the Input state, so the nav works from the
    /// result page (clicking Browse/Compile/Favorites takes you back to that view).</summary>
    [RelayCommand]
    private void SelectTab(string tab)
    {
        ActiveTab = tab switch
        {
            "Compile"   => InputTab.Compile,
            "Favorites" => InputTab.Favorites,
            "Artwork"   => InputTab.Artwork,
            "Coach"     => InputTab.Coach,
            _           => InputTab.Browse,
        };
        State = AppState.Input;
    }

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

    // ── Landing page: live community tier lists (this season's top builds) ──
    // Each source has its own tab strip; per-tab results are cached in-memory so re-clicks are
    // instant. Default Endgame is fetched eagerly on app launch; other tabs lazy-fetch on first click.
    public ObservableCollection<TierGroupVM> MaxrollTiers { get; } = new();
    public ObservableCollection<TierGroupVM> D4BuildsTiers { get; } = new();
    public ObservableCollection<TierGroupVM> MobalyticsTiers { get; } = new();

    private sealed record SelectedTierBuild(string Name, string ClassName, string Url,
        string Source, string? TierKind, string? Tier, int Priority);
    private sealed record BuildColorCustomization(uint? Chase, uint? Keeper);
    private readonly List<SelectedTierBuild> _selectedTierBuilds = new();
    // Session-only result state. Loadout persistence is intentionally deferred to Phase 2.
    private readonly Dictionary<string, BuildColorCustomization> _buildColorCustomizations =
        new(StringComparer.OrdinalIgnoreCase);
    public int SelectedBuildCount => _selectedTierBuilds.Count;
    public bool CanCompileSuperBuild => SelectedBuildCount is >= 2 and <= 4;
    public string SuperBuildActionLabel => $"Compile {SelectedBuildCount} builds";
    [ObservableProperty] private string superBuildSelectionStatus = "";
    [ObservableProperty] private string superBuildAdvisory = "";
    private string _selectionAdvisory = "";

    [ObservableProperty] private string maxrollTierStatus = "Loading…";
    [ObservableProperty] private string d4BuildsTierStatus = "Loading…";
    [ObservableProperty] private string mobalyticsTierStatus = "Loading…";

    public IReadOnlyList<TierTabVM> MaxrollTabs { get; }
    public IReadOnlyList<TierTabVM> D4BuildsTabs { get; }
    public IReadOnlyList<TierTabVM> MobalyticsTabs { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaxrollTierUrl))]
    private MaxrollList activeMaxrollList = MaxrollList.Endgame;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(D4BuildsTierUrl))]
    private D4BuildsList activeD4BuildsList = D4BuildsList.Endgame;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MobalyticsTierUrl))]
    private MobalyticsList activeMobalyticsList = MobalyticsList.Endgame;

    public string MaxrollTierUrl    => TierListFetcher.MaxrollUrlFor(ActiveMaxrollList);
    public string D4BuildsTierUrl   => TierListFetcher.D4BuildsUrlFor(ActiveD4BuildsList);
    public string MobalyticsTierUrl => TierListFetcher.MobalyticsUrlFor(ActiveMobalyticsList);

    /// <summary>One cached tab: the parsed groups plus when they were fetched. Entries older than
    /// <see cref="TierListTtl"/> re-fetch on activation, so a long-running app stops presenting
    /// harvest-time tiers as current ("This season's top builds" must actually mean this season).</summary>
    private sealed record CachedTab(List<TierGroupVM> Groups, DateTime FetchedUtc);

    /// <summary>How long a fetched tier list is served from cache before activation re-fetches.</summary>
    private static readonly TimeSpan TierListTtl = TimeSpan.FromMinutes(15);

    private readonly Dictionary<MaxrollList, CachedTab> _maxrollCache = new();
    private readonly Dictionary<D4BuildsList, CachedTab> _d4buildsCache = new();
    private readonly Dictionary<MobalyticsList, CachedTab> _mobalyticsCache = new();
    private readonly List<WeakReference<TierBuildVM>> _tierBuildProjections = new();

    /// <summary>Per-source fetch sequencing: each new activation bumps <see cref="Seq"/> and cancels
    /// the previous in-flight fetch, so a slow stale fetch can neither overwrite the rows of the tab
    /// the user has since switched to nor leak a 30s curl in the background.</summary>
    private sealed class SourceRuntime
    {
        public CancellationTokenSource? Cts;
        public int Seq;
    }

    private readonly SourceRuntime _maxrollRt = new();
    private readonly SourceRuntime _d4buildsRt = new();
    private readonly SourceRuntime _mobalyticsRt = new();

    // "updated 14:32" captions next to each source's "view full list ↗" link.
    [ObservableProperty] private string maxrollUpdatedLabel = "";
    [ObservableProperty] private string d4BuildsUpdatedLabel = "";
    [ObservableProperty] private string mobalyticsUpdatedLabel = "";

    private static string UpdatedLabel(DateTime utc) => $"updated {utc.ToLocalTime():HH:mm}";

    // ── Landing-page filters & toggles (live on the input page) ──
    // Class filter: 8 colored checkboxes (one per class), all on by default. Unchecking a class
    // hides every chip of that class across all 3 tier sources. Uses the class palette as legend.
    public IReadOnlyList<ClassFilterVM> ClassFilters { get; }
    // Show the "(Sorcerer)" subtitle under each chip's build name. Off = chip just shows the build
    // name (the class color already conveys class for trained eyes).
    [ObservableProperty] private bool showClassNames = true;
    // Show the ★/☆ star on each chip. Off = chips read cleaner; you can still star/unstar from
    // the result page after compiling.
    [ObservableProperty] private bool showFavoriteStars = true;

    // No on-change handlers for the show-* toggles: the chip XAML binds Visibility directly to
    // these properties via RelativeSource, so toggling them re-evaluates the bindings — no
    // collection rebuild needed (avoids a flicker on every checkbox click).

    // ── Favorites (persisted to %LOCALAPPDATA%\MedicKsMight\favorites.json) ──
    // The chips on the landing page's "Your Favorites" row + the star indicator on every tier chip
    // are both driven from this store. Favorites are LIVE REFERENCES: when the user re-opens one we
    // re-fetch from the source, so meta drift over the season is reflected automatically.
    // Typed as the interfaces so a future web build can inject DB-backed stores without touching the VM.
    private readonly IFavoritesStore _favorites;
    private readonly IPasteStore _pasteStore;
    private readonly Func<string, Task<(ResolvedBuild build, string sourceLabel)>> _resolveBuild;
    private readonly CompileWithinCapDelegate _compileWithinCap;
    public ObservableCollection<FavoriteChipVM> Favorites { get; } = new();
    public bool HasFavorites => Favorites.Count > 0;
    /// <summary>Inverse of <see cref="HasFavorites"/>, for XAML visibility (BooleanToVisibilityConverter
    /// doesn't support inversion). Drives the "Star any build below…" empty-state hint on the landing.</summary>
    public bool HasNoFavorites => Favorites.Count == 0;
    /// <summary>"3 saved" / "1 saved" / empty string — shown right of the section header so users
    /// see the running count without scanning the chip rail.</summary>
    public string FavoritesCountLabel => Favorites.Count switch
    {
        0 => "",
        1 => "1 saved",
        var n => $"{n} saved",
    };

    // ── Theme picker ──
    /// <summary>The 3 themes the gear-icon popup exposes. ThemeManager keeps this list authoritative.</summary>
    public IReadOnlyList<string> AvailableThemes => ThemeManager.Available;

    [ObservableProperty] private string selectedTheme = ThemeManager.Current;
    partial void OnSelectedThemeChanged(string value) => ThemeManager.Apply(value);

    /// <summary>Universal "drop panel opacity so the warlord shows through" toggle. Theme-
    /// agnostic — applies on top of whichever palette is active. Persists with theme choice.
    /// Discord doesn't support it (see <see cref="IsTranslucentSupported"/>); picker greys
    /// out the checkbox there.</summary>
    [ObservableProperty] private bool translucentPanels = ThemeManager.TranslucentPanels;
    partial void OnTranslucentPanelsChanged(bool value) => ThemeManager.SetTranslucentPanels(value);

    /// <summary>Mirrors <see cref="ThemeManager.IsTranslucentDiscouraged"/> — drives the
    /// "may look unusual" warning text in the picker. Re-notified after every theme swap via
    /// the TranslucentSupportChanged event so the warning appears/disappears live.</summary>
    public bool IsTranslucentDiscouraged => ThemeManager.IsTranslucentDiscouraged;

    [ObservableProperty] private bool isThemePickerOpen;
    [RelayCommand] private void ToggleThemePicker() => IsThemePickerOpen = !IsThemePickerOpen;

    // ── Game data (the affix/unique name files compiles resolve against) ──
    // The bundled files freeze at build time, so a new D4 season adds affixes/uniques this exe
    // has never heard of and Maxroll builds silently compile incomplete. "Update game data"
    // pulls the current pair from the Diablo4Companion repo into %LOCALAPPDATA%\MedicKsMight\data\,
    // which DataFiles prefers from then on. Reachable from the theme-gear popup and the result
    // page's missing-data note.

    /// <summary>Provenance caption in the gear popup: "Using bundled (game build …)" /
    /// "Using downloaded 2026-06-10".</summary>
    [ObservableProperty] private string gameDataLabel = $"Using {GameDataStore.DescribeActive()}";

    /// <summary>Last update-attempt outcome, shown under the popup link (empty until first use).</summary>
    [ObservableProperty] private string gameDataStatus = "";

    [RelayCommand]
    private async Task UpdateGameDataAsync()
    {
        GameDataStatus = "Checking for newer game data…";
        StatusMessage = "Checking for newer game data…";
        try
        {
            // Network + two ~1 MB JSON validations — keep it off the UI thread.
            var result = await Task.Run(() => GameDataUpdater.UpdateAsync());
            GameDataStatus = result.Message;
            StatusMessage = result.Message;
            if (result.Updated)
            {
                NameLookup.Invalidate();
                UniqueLookup.Invalidate();
                GameDataLabel = $"Using {GameDataStore.DescribeActive()}";
                // A result on screen was resolved against the OLD data — re-fetch the same source
                // so newly-known affixes actually appear (and the missing-data note recounts).
                if (State == AppState.Result && !string.IsNullOrEmpty(_currentSourceUrl))
                    await RunCompileAsync(_currentSourceUrl);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("gamedata", $"update check failed: {ex.ToString()}");
            GameDataStatus = "Couldn't check for updated game data — check your connection and try again.";
            StatusMessage = GameDataStatus;
        }
    }

    /// <summary>Provenance of the currently-loaded build — set when a build is loaded so the result
    /// page's ★ Favorite button knows what to persist. <see cref="_currentTierKind"/>/<see cref="_currentTier"/>
    /// are only known when the user clicked a tier chip (not when they pasted a URL).</summary>
    private string _currentSource = "";
    private string _currentSourceUrl = "";
    private string? _currentTierKind;
    private string? _currentTier;

    [ObservableProperty] private bool isCurrentFavorited;
    public bool CanFavoriteCurrent => !string.IsNullOrEmpty(CurrentResultBuild()?.SourceUrl);

    private ResolvedBuild? CurrentResultBuild() => VariantGroups.FirstOrDefault()?.Build ?? _resolved;

    /// <summary>Source label rendered as a small brand-colored pill next to the result-page H1
    /// build name — Maxroll / D4Builds / Mobalytics / Community. Empty for pre-load / landing.
    /// See <c>SetCurrentSource()</c> for the writers that keep <see cref="HasCurrentSource"/>
    /// + <see cref="CurrentSourceLabel"/> in sync with the underlying field.</summary>
    public string CurrentSource => _currentSource;
    public string CurrentSourceLabel => _currentSource.ToUpperInvariant();
    public bool HasCurrentSource => !string.IsNullOrEmpty(_currentSource);

    /// <summary>Write the build's source-of-origin + URL together and notify every UI binding
    /// that depends on either (the source pill on the H1 + the ★ Favorite button visibility).
    /// Centralizes what used to be 4 scattered `_currentSource = ...; _currentSourceUrl = ...`
    /// pairs across the load paths.</summary>
    private void SetCurrentSource(string source, string url)
    {
        _currentSource = source;
        _currentSourceUrl = url;
        OnPropertyChanged(nameof(CurrentSource));
        OnPropertyChanged(nameof(CurrentSourceLabel));
        OnPropertyChanged(nameof(HasCurrentSource));
        OnPropertyChanged(nameof(CanFavoriteCurrent));
    }

    partial void OnActiveMaxrollListChanged(MaxrollList value) { SyncTabs(MaxrollTabs, value.ToString()); _ = ActivateMaxrollAsync(value); }
    partial void OnActiveD4BuildsListChanged(D4BuildsList value) { SyncTabs(D4BuildsTabs, value.ToString()); _ = ActivateD4BuildsAsync(value); }
    partial void OnActiveMobalyticsListChanged(MobalyticsList value) { SyncTabs(MobalyticsTabs, value.ToString()); _ = ActivateMobalyticsAsync(value); }

    private static void SyncTabs(IReadOnlyList<TierTabVM> tabs, string activeKey)
    {
        foreach (var t in tabs) t.IsActive = t.Key == activeKey;
    }

    // Re-clicking the already-active tab used to be a silent no-op (the [ObservableProperty]
    // setter suppresses same-value callbacks) — now it force-refreshes that list, which doubles
    // as the discoverable "retry" after a failed fetch.
    [RelayCommand]
    private void SelectMaxrollTab(string key)
    {
        if (!Enum.TryParse<MaxrollList>(key, out var k)) return;
        if (k == ActiveMaxrollList) _ = ActivateMaxrollAsync(k, force: true);
        else ActiveMaxrollList = k;
    }
    [RelayCommand]
    private void SelectD4BuildsTab(string key)
    {
        if (!Enum.TryParse<D4BuildsList>(key, out var k)) return;
        if (k == ActiveD4BuildsList) _ = ActivateD4BuildsAsync(k, force: true);
        else ActiveD4BuildsList = k;
    }
    [RelayCommand]
    private void SelectMobalyticsTab(string key)
    {
        if (!Enum.TryParse<MobalyticsList>(key, out var k)) return;
        if (k == ActiveMobalyticsList) _ = ActivateMobalyticsAsync(k, force: true);
        else ActiveMobalyticsList = k;
    }

    [RelayCommand] private void OpenTierUrl(string url) => OpenUrl(url);

    /// <summary>Clicked a tier-list build chip: drop its URL into the URL box and compile it.
    /// The fetchers resolve a maxroll guide page or a d4builds build URL to the real planner/build,
    /// so a casual one-click "load the meta build" works without the user hunting for the planner.</summary>
    private void LoadBuildFromUrl(string url)
    {
        PasteMode = false;       // make sure we're in URL mode so the box shows the link
        MaxrollUrl = url;
        _ = RunCompileAsync(url);
    }

    /// <summary>Star/unstar a tier-chip build. Toggle is by URL; the new entry captures the chip's
    /// source/tier/tier-kind so the favorite knows where it came from. After toggling, sync IsFavorited
    /// across every cached chip (the same build might appear under multiple tabs).</summary>
    private void ToggleFavorite(TierBuildVM vm)
    {
        var snapshot = string.Equals(vm.Url, _currentSourceUrl, StringComparison.OrdinalIgnoreCase)
            && _resolved is not null && !string.IsNullOrEmpty(ImportCode)
            ? BuildSnapshot.Capture(_resolved)
            : null;
        var candidate = new FavoriteEntry(
            Id: Guid.NewGuid().ToString("N"),
            Url: vm.Url,
            Source: vm.Source,
            TierKind: vm.TierKind,
            Tier: vm.Tier,
            Name: vm.Name,
            ClassName: vm.ClassName,
            DateAdded: DateTime.UtcNow,
            DateLastOpened: DateTime.UtcNow,
            Snapshot: snapshot);
        _favorites.Toggle(candidate);
        RefreshFavoritesUi();
    }

    /// <summary>Star/unstar the currently-loaded build (result-page ★ button). Used when the build
    /// came from a paste or a raw URL paste — i.e. no tier chip involved — so TierKind/Tier are null.</summary>
    [RelayCommand]
    private void ToggleFavoriteCurrent()
    {
        var current = CurrentResultBuild();
        var url = current?.SourceUrl ?? "";
        if (current is null || string.IsNullOrEmpty(url)) return;
        var snapshot = !url.StartsWith("paste://", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(ImportCode)
            ? BuildSnapshot.Capture(current)
            : null;
        bool retainsLoadedTier = string.Equals(url, _currentSourceUrl, StringComparison.OrdinalIgnoreCase);
        var candidate = new FavoriteEntry(
            Id: Guid.NewGuid().ToString("N"),
            Url: url,
            Source: string.IsNullOrWhiteSpace(current.Source) ? _currentSource : current.Source,
            TierKind: retainsLoadedTier ? _currentTierKind : null,
            Tier: retainsLoadedTier ? _currentTier : null,
            Name: string.IsNullOrEmpty(current.Build) ? "(unnamed build)" : current.Build,
            ClassName: current.Class,
            DateAdded: DateTime.UtcNow,
            DateLastOpened: DateTime.UtcNow,
            Snapshot: snapshot);
        _favorites.Toggle(candidate);
        RefreshFavoritesUi();
    }

    private void RemoveFavorite(FavoriteChipVM chip)
    {
        _favorites.Remove(chip.Url);
        // Community pastes: also drop the sidecar text file (favorites file alone wouldn't be enough
        // to restore the build, and a dangling sidecar would leak disk space across re-favorites).
        if (chip.Url.StartsWith("paste://", StringComparison.OrdinalIgnoreCase))
            _pasteStore.Remove(chip.Url["paste://".Length..]);
        RefreshFavoritesUi();
    }

    /// <summary>Reconcile the Favorites collection + every cached tier chip's IsFavorited flag with
    /// the current store contents. Cheap: tier-list caches are at most ~50 chips per source.</summary>
    private void RefreshFavoritesUi()
    {
        Favorites.Clear();
        foreach (var f in _favorites.All.OrderByDescending(f => f.DateAdded))
        {
            var entry = f;   // capture per iteration for the chip's load closure
            Favorites.Add(new FavoriteChipVM(entry, ignoredUrl =>
            {
                _ = LoadBuildFromFavoriteAsync(entry);
            }, RemoveFavorite));
        }
        OnPropertyChanged(nameof(HasFavorites));
        OnPropertyChanged(nameof(HasNoFavorites));
        OnPropertyChanged(nameof(FavoritesCountLabel));

        var favUrls = new HashSet<string>(_favorites.All.Select(f => f.Url),
            StringComparer.OrdinalIgnoreCase);
        foreach (var b in AllCachedChips()) b.IsFavorited = favUrls.Contains(b.Url);
        var currentUrl = CurrentResultBuild()?.SourceUrl;
        IsCurrentFavorited = !string.IsNullOrEmpty(currentUrl)
            && favUrls.Contains(currentUrl);
    }

    private IEnumerable<TierBuildVM> AllCachedChips()
    {
        foreach (var g in _maxrollCache.Values.SelectMany(v => v.Groups))
            foreach (var b in g.Builds) yield return b;
        foreach (var g in _d4buildsCache.Values.SelectMany(v => v.Groups))
            foreach (var b in g.Builds) yield return b;
        foreach (var g in _mobalyticsCache.Values.SelectMany(v => v.Groups))
            foreach (var b in g.Builds) yield return b;
    }

    public MainViewModel() : this(startTierListFetches: true) { }

    // The regression suite exercises result-state wiring without launching three unrelated live
    // tier-list requests, and can replace the OS clipboard at the same command boundary production
    // uses. Production always enters through the parameterless constructor above.
    private readonly Action<string> _setClipboardText;
    internal MainViewModel(bool startTierListFetches, Action<string>? setClipboardText = null,
        IFavoritesStore? favorites = null, IPasteStore? pasteStore = null,
        Func<string, Task<(ResolvedBuild build, string sourceLabel)>>? resolveBuild = null,
        CompileWithinCapDelegate? compileWithinCap = null)
    {
        _setClipboardText = setClipboardText ?? Clipboard.SetText;
        _favorites = favorites ?? new FavoritesStore();
        _pasteStore = pasteStore ?? new PasteStore();
        _resolveBuild = resolveBuild ?? (source => BuildResolver.ResolveAsync(
            source, NameLookup.Default(), UniqueLookup.Default()));
        _compileWithinCap = compileWithinCap ?? ((builds, options, maxRules, label, title) =>
        {
            var output = FilterCompiler.CompileWithinCap(builds, options, maxRules,
                out CompileFitReport fit, label, title);
            return (output, fit);
        });

        // Re-notify TranslucentPanels after a theme swap so the picker checkbox reflects
        // whatever ThemeManager.TranslucentPanels is now (no longer gets force-cleared since
        // Medick wanted Discord to also be clickable).
        ThemeManager.TranslucentSupportChanged += () =>
        {
            SetProperty(ref translucentPanels, ThemeManager.TranslucentPanels, nameof(TranslucentPanels));
            OnPropertyChanged(nameof(IsTranslucentDiscouraged));
        };

        // Class filter strip: 8 D4 classes, all enabled by default. Unchecking a class hides every
        // chip of that class across all 3 tier sources. Order matches the in-game class select roughly.
        ClassFilters = new[]
        {
            "Barbarian", "Druid", "Necromancer", "Rogue",
            "Sorcerer", "Spiritborn", "Paladin", "Warlock",
        }.Select(c => new ClassFilterVM(c, RefreshAllTierViews)).ToList();

        // v1.0.2: the unique-charm show-list — every S14 unique charm, ALL pre-checked. Build-
        // independent (same catalog for every class), so it's populated once here; the ctor writes
        // the field directly so this doesn't fire Recompile. Unchecking a charm recompiles live.
        foreach (var c in UniqueCharmDatabase.All)
            UniqueCharmOptions.Add(new UniqueCharmOption(c, isChecked: true, Recompile));
        foreach (var u in UniqueItemDatabase.All)
            UniqueItemOptions.Add(new UniqueItemOption(u, isChecked: true, Recompile));

        // v1.0.3: restore the two big unique lists' remembered open/closed state (Filter options +
        // Charm sets always start open, so they aren't restored). Fields written directly so this
        // doesn't fire the persistence hooks during construction.
        uniqueCharmsExpanded = _uiState.UniqueCharmsExpanded;
        uniquesExpanded = _uiState.UniquesExpanded;

        MaxrollTabs = new[]
        {
            new TierTabVM("Endgame",    nameof(MaxrollList.Endgame),   true),
            new TierTabVM("Bossing",    nameof(MaxrollList.Bossing),   false),
            new TierTabVM("Leveling",   nameof(MaxrollList.Leveling),  false),
            new TierTabVM("Push",       nameof(MaxrollList.Push),      false),
            new TierTabVM("Speedfarm",  nameof(MaxrollList.Speedfarm), false),
        };
        D4BuildsTabs = new[]
        {
            new TierTabVM("Endgame",  nameof(D4BuildsList.Endgame),  true),
            new TierTabVM("Leveling", nameof(D4BuildsList.Leveling), false),
            new TierTabVM("Tower",    nameof(D4BuildsList.Tower),    false),
        };
        MobalyticsTabs = new[]
        {
            new TierTabVM("Endgame",  nameof(MobalyticsList.Endgame),  true),
            new TierTabVM("Leveling", nameof(MobalyticsList.Leveling), false),
            new TierTabVM("Pushing",  nameof(MobalyticsList.Pushing),  false),
        };
        // Hydrate the Favorites chip strip from the persisted store BEFORE the tier-list fetches —
        // favorites should be there on first paint, no network required.
        RefreshFavoritesUi();

        // Kick off the three default-tab fetches in parallel — independent, one failing doesn't
        // block the others. Awaits resume on the UI context so ObservableCollections are touched on
        // the dispatcher.
        if (startTierListFetches)
            _ = Task.WhenAll(
                ActivateMaxrollAsync(MaxrollList.Endgame),
                ActivateD4BuildsAsync(D4BuildsList.Endgame),
                ActivateMobalyticsAsync(MobalyticsList.Endgame));
    }

    private Task ActivateMaxrollAsync(MaxrollList kind, bool force = false) =>
        ActivateAsync(kind, _maxrollCache, MaxrollTiers, s => MaxrollTierStatus = s,
            ct => TierListFetcher.FetchMaxrollAsync(kind, ct),
            "Maxroll", kind.ToString(),
            _maxrollRt, () => ActiveMaxrollList, t => MaxrollUpdatedLabel = t, force);

    private Task ActivateD4BuildsAsync(D4BuildsList kind, bool force = false) =>
        ActivateAsync(kind, _d4buildsCache, D4BuildsTiers, s => D4BuildsTierStatus = s,
            ct => TierListFetcher.FetchD4BuildsAsync(kind, ct),
            "D4Builds", kind.ToString(),
            _d4buildsRt, () => ActiveD4BuildsList, t => D4BuildsUpdatedLabel = t, force);

    private Task ActivateMobalyticsAsync(MobalyticsList kind, bool force = false) =>
        ActivateAsync(kind, _mobalyticsCache, MobalyticsTiers, s => MobalyticsTierStatus = s,
            ct => TierListFetcher.FetchMobalyticsAsync(kind, ct),
            "Mobalytics", kind.ToString(),
            _mobalyticsRt, () => ActiveMobalyticsList, t => MobalyticsUpdatedLabel = t, force);

    /// <summary>Cache-aware tab activation: serve from cache when fresh (see <see cref="TierListTtl"/>),
    /// else fetch + populate + cache. Generic over the per-source enum + per-source fetcher so all
    /// three sources share one flow. <paramref name="source"/> + <paramref name="tierKind"/> stamp
    /// each chip with its provenance so a subsequent ★ favorite remembers where the build came from.
    /// <para>The cache holds the FULL group/build list (no class filter applied); the visible
    /// <paramref name="target"/> collection is a class-filtered projection built via
    /// <see cref="ProjectIntoTarget"/>. This way the class-filter toggles never need to re-fetch.</para>
    /// <para>Freshness rules: a new activation supersedes (cancels) the source's in-flight fetch;
    /// results only touch the visible collection if this kind is still the active tab; 0-build
    /// parses are NOT cached (could be a real empty list OR a site redesign — either way the next
    /// activation should retry, not pin "empty" for the session); every successful fetch re-syncs
    /// favorite tier labels via <see cref="TierReconciler"/>.</para></summary>
    private async Task ActivateAsync<TKind>(TKind kind,
        Dictionary<TKind, CachedTab> cache,
        ObservableCollection<TierGroupVM> target,
        Action<string> setStatus,
        Func<CancellationToken, Task<TierList>> fetch,
        string source, string tierKind,
        SourceRuntime rt, Func<TKind> activeKind, Action<string> setUpdated,
        bool force = false) where TKind : notnull
    {
        if (!force && cache.TryGetValue(kind, out var hit)
                   && DateTime.UtcNow - hit.FetchedUtc < TierListTtl)
        {
            ProjectIntoTarget(hit.Groups, target);
            setUpdated(UpdatedLabel(hit.FetchedUtc));
            setStatus(hit.Groups.Count == 0 ? "No builds in this list yet — open the full list ↗" : "");
            return;
        }

        rt.Cts?.Cancel();
        rt.Cts = new CancellationTokenSource();
        var ct = rt.Cts.Token;
        var seq = ++rt.Seq;
        bool IsCurrent() => seq == rt.Seq
            && EqualityComparer<TKind>.Default.Equals(activeKind(), kind);

        // Refresh-in-place keeps the old rows visible while the new fetch runs (no blank flash);
        // a tab SWITCH clears so the previous tab's rows never sit under the new tab's header.
        if (force) setStatus("Refreshing…");
        else { setStatus("Loading…"); target.Clear(); }

        try
        {
            var list = await fetch(ct);
            if (seq != rt.Seq) return;   // superseded — the newer fetch owns cache, favorites & UI

            var fetchedAt = DateTime.UtcNow;
            var groups = BuildGroups(list, source, tierKind);
            if (groups.Count > 0)
            {
                cache[kind] = new CachedTab(groups, fetchedAt);
            }
            else
            {
                // Ambiguous: genuinely empty (d4builds Tower mid-season) or markup drift. Don't cache.
                cache.Remove(kind);
                AppLog.Write("tierlist", $"{source}/{tierKind}: fetch OK but 0 builds parsed — empty list or format change");
            }

            // Tier labels on saved favorites re-sync against every fresh list (the S→A problem).
            var moved = TierReconciler.Reconcile(_favorites, list, source, tierKind, fetchedAt);
            if (moved > 0) RefreshFavoritesUi();

            if (IsCurrent())
            {
                ProjectIntoTarget(groups, target);
                setUpdated(UpdatedLabel(fetchedAt));
                setStatus(groups.Count == 0
                    ? "Nothing listed right now — re-click the tab to retry, or open the full list ↗"
                    : "");
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer activation (or shutdown) — the newer flow owns the UI.
        }
        catch (Exception ex)
        {
            AppLog.Write("tierlist", $"{source}/{tierKind}: fetch failed — {ex.GetType().Name}: {ex.Message}");
            if (IsCurrent())
                setStatus($"Couldn't reach {source} — re-click the tab to retry, or open the full list ↗");
        }
    }

    /// <summary>Project a fetched tier list into chip view-models (shared by tab activation and the
    /// background favorite-tier refresh).</summary>
    internal List<TierGroupVM> BuildGroups(TierList list, string source, string tierKind)
    {
        var favUrls = new HashSet<string>(_favorites.All.Select(f => f.Url),
            StringComparer.OrdinalIgnoreCase);
        var groups = list.Builds.GroupBy(b => b.Tier)
            .Select(g => new TierGroupVM(g.Key, g.Select(b =>
            {
                var selected = _selectedTierBuilds.FirstOrDefault(selection =>
                    selection.Url.Equals(b.Url, StringComparison.OrdinalIgnoreCase));
                return new TierBuildVM(b, source, tierKind, g.Key,
                    LoadBuildFromChip,
                    ToggleFavorite,
                    favUrls.Contains(b.Url),
                    openSource: OpenUrl,
                    toggleSelection: ToggleSuperBuildSelection,
                    isSelected: selected is not null,
                    changePriority: ChangeSelectedTierBuildPriority,
                    priority: selected?.Priority,
                    priorityCount: _selectedTierBuilds.Count);
            }).ToList()))
            .ToList();
        foreach (var chip in groups.SelectMany(group => group.Builds))
            _tierBuildProjections.Add(new(chip));
        return groups;
    }

    private bool ToggleSuperBuildSelection(TierBuildVM vm, bool selected)
    {
        int existing = _selectedTierBuilds.FindIndex(s =>
            s.Url.Equals(vm.Url, StringComparison.OrdinalIgnoreCase));
        if (selected && existing < 0)
        {
            if (_selectedTierBuilds.Count >= 4)
            {
                SuperBuildSelectionStatus = $"You can select up to 4 builds. Deselect one before adding '{vm.Name}'.";
                return false;
            }
            _selectedTierBuilds.Add(new(vm.Name, vm.ClassName, vm.Url, vm.Source, vm.TierKind, vm.Tier,
                _selectedTierBuilds.Count + 1));
            SuperBuildSelectionStatus = "";
        }
        else if (!selected && existing >= 0)
        {
            var removed = _selectedTierBuilds[existing];
            _buildColorCustomizations.Remove(BuildColorKey(
                removed.Source, removed.Url, removed.Name, removed.ClassName));
            _selectedTierBuilds.RemoveAt(existing);
            var remaining = _selectedTierBuilds.OrderBy(build => build.Priority).ToList();
            _selectedTierBuilds.Clear();
            for (int i = 0; i < remaining.Count; i++)
                _selectedTierBuilds.Add(remaining[i] with { Priority = i + 1 });
            SuperBuildSelectionStatus = "";
        }

        SyncSelectedTierBuildUi(vm);
        RefreshSuperBuildSelectionUi();
        return true;
    }

    private bool ChangeSelectedTierBuildPriority(TierBuildVM vm, int requestedPriority)
    {
        var changedIndex = _selectedTierBuilds.FindIndex(build =>
            build.Url.Equals(vm.Url, StringComparison.OrdinalIgnoreCase));
        if (changedIndex < 0 || requestedPriority is < 1 or > 4
            || requestedPriority > _selectedTierBuilds.Count)
            return false;

        var previousPriority = _selectedTierBuilds[changedIndex].Priority;
        var occupiedIndex = _selectedTierBuilds.FindIndex(build => build.Priority == requestedPriority);
        if (occupiedIndex >= 0 && occupiedIndex != changedIndex)
            _selectedTierBuilds[occupiedIndex] = _selectedTierBuilds[occupiedIndex] with
            {
                Priority = previousPriority,
            };
        _selectedTierBuilds[changedIndex] = _selectedTierBuilds[changedIndex] with
        {
            Priority = requestedPriority,
        };
        SyncSelectedTierBuildUi(vm);
        return true;
    }

    private void SyncSelectedTierBuildUi(TierBuildVM initiatingChip)
    {
        foreach (var chip in LiveTierBuildProjections().Append(initiatingChip).Distinct())
        {
            var selected = _selectedTierBuilds.FirstOrDefault(build =>
                build.Url.Equals(chip.Url, StringComparison.OrdinalIgnoreCase));
            chip.SyncSelection(selected is not null);
            chip.SyncPriority(selected?.Priority, _selectedTierBuilds.Count);
        }
    }

    private IEnumerable<TierBuildVM> LiveTierBuildProjections()
    {
        for (int i = _tierBuildProjections.Count - 1; i >= 0; i--)
        {
            if (_tierBuildProjections[i].TryGetTarget(out var chip)) yield return chip;
            else _tierBuildProjections.RemoveAt(i);
        }
    }

    private void RefreshSuperBuildSelectionUi()
    {
        var advisories = new List<string>();
        if (_selectedTierBuilds.Count > 3)
            advisories.Add("4 builds uses colors by tier: Red marks every build's legendaries and Gold marks every build's rares; rule names identify the build.");
        if (_selectedTierBuilds.Select(b => b.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            advisories.Add("These builds come from different sites. Maxroll and Mobalytics describe builds differently, so their affix lists may not be directly comparable — sticking to one source usually gives a tighter filter.");
        if (_selectedTierBuilds.Select(b => b.ClassName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            advisories.Add("You've selected builds from different classes. That's fine if you're playing both characters — otherwise you're filtering for loot you can't use.");
        _selectionAdvisory = string.Join("\n", advisories);
        SuperBuildAdvisory = _selectionAdvisory;
        OnPropertyChanged(nameof(SelectedBuildCount));
        OnPropertyChanged(nameof(CanCompileSuperBuild));
        OnPropertyChanged(nameof(SuperBuildActionLabel));
        CompileSelectedBuildsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanCompileSuperBuild))]
    private async Task CompileSelectedBuildsAsync()
    {
        var selections = _selectedTierBuilds.OrderBy(selection => selection.Priority).ToList();
        if (selections.Count is < 2 or > 4) return;

        StatusMessage = $"Loading {selections.Count} builds…";
        State = AppState.Loading;
        var resolved = new List<ResolvedBuild>(selections.Count);
        try
        {
            // Resolve in priority order. Every downstream positional Build A/B decision consumes
            // this same order, and refusal therefore names the lowest-priority build last.
            foreach (var selection in selections)
            {
                var result = await Task.Run(() => _resolveBuild(selection.Url));
                if (!result.build.HasUsableVariants)
                    throw new UserMessageException($"{selection.Name} returned no usable variants.");
                resolved.Add(result.build with { Source = selection.Source, SourceUrl = selection.Url });
            }

            var first = selections[0];
            SetCurrentSource(first.Source, first.Url);
            _currentTierKind = first.TierKind;
            _currentTier = first.Tier;
            Ingest(resolved[0], first.Source, resolved.Skip(1).ToList());

            // The primary favorite is handled by Ingest (including its drift note). Extra selected
            // favorites earn the same full-source baseline only after the grouped compile produced
            // copyable code; temporary variant narrowing is never written into BuildSnapshot.
            if (!string.IsNullOrEmpty(ImportCode))
            {
                bool updated = false;
                for (int i = 1; i < selections.Count; i++)
                    if (_favorites.Find(selections[i].Url) is { } favorite)
                    {
                        _favorites.Update(favorite with { Snapshot = BuildSnapshot.Capture(resolved[i]) });
                        updated = true;
                    }
                if (updated) RefreshFavoritesUi();
            }
        }
        catch (UserMessageException ex)
        {
            // Authored product copy (e.g. "{build} returned no usable variants") — not dev noise,
            // tells the player which build failed. Surface it verbatim; still logged for triage.
            AppLog.Write("compile", $"multi-build compile failed: {ex.ToString()}");
            StatusMessage = ex.Message;
            State = AppState.Input;
        }
        catch (Exception ex)
        {
            // This try block also covers local compile and favorites persistence, not just the
            // network resolve — a "check your connection" message would be wrong for those stages
            // and there's no cheap way to tell them apart here, so keep the copy stage-neutral.
            AppLog.Write("compile", $"multi-build compile failed: {ex.ToString()}");
            StatusMessage = "Couldn't finish loading that build — details are in the app log.";
            State = AppState.Input;
        }
    }

    // ── Refresh: one button re-pulls everything the landing page claims is live ──

    [ObservableProperty] private bool isRefreshing;

    /// <summary>⟳ button + F5: force-refresh the three active tabs AND every other (source, kind)
    /// tier list that a saved favorite references, so favorite tier labels re-sync even when their
    /// tab isn't open. The tier-page URLs themselves are compile-time constants in
    /// <see cref="TierListFetcher"/> — what's fetched is always the source's live ranking.</summary>
    [RelayCommand]
    private async Task RefreshTierListsAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        try
        {
            var work = new List<Task>
            {
                ActivateMaxrollAsync(ActiveMaxrollList, force: true),
                ActivateD4BuildsAsync(ActiveD4BuildsList, force: true),
                ActivateMobalyticsAsync(ActiveMobalyticsList, force: true),
            };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                $"Maxroll|{ActiveMaxrollList}",
                $"D4Builds|{ActiveD4BuildsList}",
                $"Mobalytics|{ActiveMobalyticsList}",
            };
            foreach (var f in _favorites.All)
            {
                if (string.IsNullOrEmpty(f.TierKind)) continue;        // paste favorites have no list
                if (!seen.Add($"{f.Source}|{f.TierKind}")) continue;
                work.Add(FetchAndReconcileAsync(f.Source, f.TierKind!));
            }
            await Task.WhenAll(work);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>Background fetch of a non-active tier list purely to re-sync favorite tiers (and
    /// warm the cache for that tab). Never touches the visible collections or status text.</summary>
    private async Task FetchAndReconcileAsync(string source, string tierKind)
    {
        try
        {
            switch (source)
            {
                case "Maxroll" when Enum.TryParse<MaxrollList>(tierKind, true, out var mk):
                    CacheAndReconcile(await TierListFetcher.FetchMaxrollAsync(mk), _maxrollCache, mk, source, tierKind);
                    break;
                case "D4Builds" when Enum.TryParse<D4BuildsList>(tierKind, true, out var dk):
                    CacheAndReconcile(await TierListFetcher.FetchD4BuildsAsync(dk), _d4buildsCache, dk, source, tierKind);
                    break;
                case "Mobalytics" when Enum.TryParse<MobalyticsList>(tierKind, true, out var bk):
                    CacheAndReconcile(await TierListFetcher.FetchMobalyticsAsync(bk), _mobalyticsCache, bk, source, tierKind);
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("refresh", $"{source}/{tierKind}: favorite-tier refresh failed — {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void CacheAndReconcile<TKind>(TierList list,
        Dictionary<TKind, CachedTab> cache, TKind kind,
        string source, string tierKind) where TKind : notnull
    {
        var now = DateTime.UtcNow;
        var groups = BuildGroups(list, source, tierKind);
        if (groups.Count > 0) cache[kind] = new CachedTab(groups, now);
        if (TierReconciler.Reconcile(_favorites, list, source, tierKind, now) > 0)
            RefreshFavoritesUi();
    }

    /// <summary>Apply the current class filter to a cached group list and replace the visible
    /// target. A tier with all-hidden builds is dropped from the view entirely (avoids showing an
    /// empty "S" row when the user has hidden every S-tier class). Cheap on small collections.</summary>
    private void ProjectIntoTarget(IReadOnlyList<TierGroupVM> source, ObservableCollection<TierGroupVM> target)
    {
        var hidden = new HashSet<string>(
            ClassFilters.Where(f => !f.IsEnabled).Select(f => f.ClassName),
            StringComparer.OrdinalIgnoreCase);
        target.Clear();
        foreach (var g in source)
        {
            var visibleBuilds = g.Builds.Where(b => !hidden.Contains(b.ClassName)).ToList();
            if (visibleBuilds.Count == 0) continue; // drop empty tier rows
            target.Add(new TierGroupVM(g.Tier, visibleBuilds));
        }
    }

    /// <summary>Re-project every currently-active source's cached groups through the class filter.
    /// Called when a class checkbox toggles. Inactive tabs are untouched — they'll filter when the
    /// user clicks them (via ActivateAsync's cache-hit path, which also uses ProjectIntoTarget).</summary>
    private void RefreshAllTierViews()
    {
        if (_maxrollCache.TryGetValue(ActiveMaxrollList, out var mr))
            ProjectIntoTarget(mr.Groups, MaxrollTiers);
        if (_d4buildsCache.TryGetValue(ActiveD4BuildsList, out var db))
            ProjectIntoTarget(db.Groups, D4BuildsTiers);
        if (_mobalyticsCache.TryGetValue(ActiveMobalyticsList, out var mb))
            ProjectIntoTarget(mb.Groups, MobalyticsTiers);
    }

    /// <summary>A card click loads normally until a checkbox selection is active. Once active, the
    /// cards become selection toggles so a click can never discard ticked builds into a one-build
    /// compile; the explicit Compile N builds button is then the only compile path.</summary>
    private void LoadBuildFromChip(TierBuildVM vm)
    {
        if (_selectedTierBuilds.Count > 0)
        {
            var wasSelected = vm.IsSelected;
            vm.IsSelected = !vm.IsSelected;
            if (vm.IsSelected == wasSelected) return; // hard-cap refusal already supplied the reason
            SuperBuildSelectionStatus = _selectedTierBuilds.Count == 0
                ? ""
                : $"{_selectedTierBuilds.Count} {(_selectedTierBuilds.Count == 1 ? "build" : "builds")} selected — "
                    + (_selectedTierBuilds.Count == 1
                        ? "tick another build to compile, or clear the selection."
                        : $"use {SuperBuildActionLabel}, or clear the selection.");
            return;
        }

        SetCurrentSource(vm.Source, vm.Url);
        _currentTierKind = vm.TierKind;
        _currentTier = vm.Tier;
        if (_favorites.Contains(vm.Url)) _favorites.StampOpened(vm.Url);
        LoadBuildFromUrl(vm.Url);
    }

    /// <summary>Favorites-rail click handler: seed provenance from the SAVED entry (previously this
    /// path left whatever tier the last-clicked tier chip set, so re-starring from the result page
    /// could persist a tier the build never had) and advance DateLastOpened, which the chip tooltip
    /// surfaces as a staleness cue.</summary>
    internal async Task LoadBuildFromFavoriteAsync(FavoriteEntry f)
    {
        SetCurrentSource(f.Source, f.Url);
        _currentTierKind = f.TierKind;
        _currentTier = f.Tier;
        _favorites.StampOpened(f.Url);
        PasteMode = false;
        MaxrollUrl = f.Url;
        await RunCompileAsync(f.Url);
    }

    // ── Result: header ──
    [ObservableProperty]
    private string buildName = "";

    [ObservableProperty]
    private string buildSubtitle = "";

    [ObservableProperty]
    private string buildTierLegend = "● Red: 3+ affix legendaries · ancestral charms\n● Pink: 3+ affix rares\n● Gold: Cube Bases";

    // The fetched build, kept so variant toggles can recompile from a subset.
    private ResolvedBuild? _resolved;
    private ResolvedBuild? _secondResolved;
    private readonly List<ResolvedBuild> _superResolved = new();
    private string _singleBuildDriftNote = "";

    [ObservableProperty] private string armorySource = "";
    [ObservableProperty] private string armoryStatus = "";
    [ObservableProperty] private string secondBuildName = "";
    [ObservableProperty] private string armoryClassNote = "";
    public bool HasSecondBuild => _secondResolved is not null;
    public string ArmoryBuildNames => _secondResolved is null || _resolved is null
        ? "" : $"{_resolved.Build}  +  {_secondResolved.Build}";

    public ObservableCollection<VariantOption> Variants { get; } = new();
    public ObservableCollection<BuildVariantGroup> VariantGroups { get; } = new();
    public ObservableCollection<string> PoolLines { get; } = new();
    public ObservableCollection<string> DroppedLines { get; } = new();
    public ObservableCollection<string> UniquePurpleLines { get; } = new();
    public ObservableCollection<string> UniquePendingLines { get; } = new();

    public bool HasDropped => DroppedLines.Count > 0;
    public bool HasPurple => UniquePurpleLines.Count > 0;
    public bool HasPending => UniquePendingLines.Count > 0;
    /// <summary>Inverse of HasPurple || HasPending. Drives the empty-state hint in the Uniques tab
    /// (BooleanToVisibilityConverter doesn't support inversion or compound conditions).</summary>
    public bool HasNoUniques => !HasPurple && !HasPending;

    // ── v1.0.1: per-class talisman-set checkboxes (build's sets pre-checked, unchecked = hidden) ──
    public ObservableCollection<TalismanSetOption> TalismanSetOptions { get; } = new();
    public bool HasTalismanSetOptions => TalismanSetOptions.Count > 0;

    /// <summary>v1.0.4: true when the loaded build's source didn't provide charm-set data (e.g.
    /// Mobalytics, whose build data omits equipped charms), so we defaulted to showing ALL the class's
    /// sets instead of hiding every charm. Drives an explanatory note in the Charm sets section.</summary>
    [ObservableProperty] private bool charmSetsUndetected;
    [ObservableProperty] private bool charmSetSelectionEnabled = true;
    [ObservableProperty] private string charmSetSafetyNote =
        "This build didn't list its charm sets, so all of them are shown (nothing hidden). Uncheck any you don't want.";

    // ── v1.0.2: unique-charm checkboxes (Medick's list — every S14 unique charm, ALL pre-checked to
    // show; uncheck any to hide it). Build-independent, so populated once in the ctor. ──
    public ObservableCollection<UniqueCharmOption> UniqueCharmOptions { get; } = new();
    public bool HasUniqueCharmOptions => UniqueCharmOptions.Count > 0;

    // ── v1.0.2: unique ITEM checkboxes (Medick's list — every regular unique, ALL pre-checked). A
    // hide-list: uniques drop by default, so unchecking one adds it to the "Hide Uniques" rule. ──
    public ObservableCollection<UniqueItemOption> UniqueItemOptions { get; } = new();
    public bool HasUniqueItemOptions => UniqueItemOptions.Count > 0;

    // ── v1.0.2 QoL (Medick): Select all / Unselect all for the checkbox lists. Flip every box with
    // the recompile suppressed, then compile once (hundreds would otherwise recompile per flip). ──
    [RelayCommand] private void CheckAllUniqueCharms() => BulkApply(() => { foreach (var o in UniqueCharmOptions) o.IsChecked = true; });
    [RelayCommand] private void UncheckAllUniqueCharms() => BulkApply(() => { foreach (var o in UniqueCharmOptions) o.IsChecked = false; });
    [RelayCommand] private void CheckAllCharmSets() => BulkApply(() => { foreach (var o in TalismanSetOptions) o.IsChecked = true; });
    [RelayCommand] private void UncheckAllCharmSets() => BulkApply(() => { foreach (var o in TalismanSetOptions) o.IsChecked = false; });
    [RelayCommand] private void RecheckCharmSets() => Recompile(RecompileCause.BuildContextChanged);
    [RelayCommand] private void CheckAllUniques() => BulkApply(() => { foreach (var o in UniqueItemOptions) o.IsChecked = true; });
    [RelayCommand] private void UncheckAllUniques() => BulkApply(() => { foreach (var o in UniqueItemOptions) o.IsChecked = false; });

    private void BulkApply(Action flip)
    {
        _suppressRecompile = true;
        flip();
        _suppressRecompile = false;
        Recompile();
    }

    /// <summary>Footer build stamp: assembly version ("-dev" suffix on work-in-progress builds)
    /// plus the exe's build time — answers "am I running the latest build?" at a glance
    /// (Medick's ask, 2026-07-02, after a dev build and the v1.0.0 release looked identical).</summary>
    public string AppVersionLabel { get; } = ComputeVersionLabel();

    private static string ComputeVersionLabel()
    {
        var asm = System.Reflection.Assembly.GetEntryAssembly();
        var info = asm is null ? null
            : ((System.Reflection.AssemblyInformationalVersionAttribute?)System.Attribute
                .GetCustomAttribute(asm, typeof(System.Reflection.AssemblyInformationalVersionAttribute)))
                ?.InformationalVersion;
        var ver = string.IsNullOrWhiteSpace(info) ? "?" : info!;
        var plus = ver.IndexOf('+');                        // strip any build-metadata suffix
        if (plus > 0) ver = ver[..plus];
        string built = "";
        try
        {
            if (Environment.ProcessPath is { } exe)
                built = $"  •  built {System.IO.File.GetLastWriteTime(exe):MMM d, h:mm tt}";
        }
        catch { /* cosmetic only — never let the footer break startup */ }
        return $"MedicK's Might v{ver}{built}";
    }

    // ── Result: the compiled filter + the user's option toggles ──
    [ObservableProperty] private string importCode = "";
    [ObservableProperty] private string filterInfo = "";

    // The card is composed from the exact FilterOutput that produced ImportCode. Keeping the output
    // (rather than reconstructing safety from UI strings) gives Share the same fail-closed truth as Copy.
    private FilterOutput? _lastFilterOutput;
    public WitnessCardViewModel? CurrentWitnessCard { get; private set; }
    [ObservableProperty] private string witnessCardBlockReason = "Compile a safe filter to create a share card.";
    [ObservableProperty] private string witnessCardConfirmation = "";
    public bool CanShareWitnessCard => CurrentWitnessCard is not null;

    /// <summary>D4 rejects any filter with more than 25 rules on import. Set when the current
    /// toggles push past that so the UI can warn (both Gold + Silver tiers on a many-slot build
    /// is the usual culprit).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCapWarning))]
    private string capWarning = "";
    public bool HasCapWarning => !string.IsNullOrEmpty(CapWarning);
    partial void OnCapWarningChanged(string value) => RefreshWitnessCardState();

    /// <summary>Plain-words auto-fit warning shown whenever the compiler turns an option off to stay
    /// within the cap. Mutually exclusive with <see cref="CapWarning"/>, which is the refusal path.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAutoFitNote))]
    private string autoFitNote = "";
    public bool HasAutoFitNote => !string.IsNullOrEmpty(AutoFitNote);

    /// <summary>D4's hard cap on rules per filter.</summary>
    public const int MaxRules = 25;

    /// <summary>One-line amber note when the loaded build references affix nids / unique ids the
    /// LOCAL GAME DATA can't name — the new-season symptom (the filter still compiles, minus those).
    /// Distinct from the dev-only "not yet filterable" list: that's mapper/DB coverage we have to
    /// fix in code; this one the user can fix themselves via Update game data. Empty = hidden.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissingDataNote))]
    private string missingDataNote = "";
    public bool HasMissingDataNote => MissingDataNote.Length > 0;

    /// <summary>Amber favorite-only note comparing the freshly resolved guide with the last
    /// successful compile. Paste favorites never set it because they cannot re-resolve.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBuildDriftNote))]
    private string buildDriftNote = "";
    public bool HasBuildDriftNote => BuildDriftNote.Length > 0;
    partial void OnBuildDriftNoteChanged(string value) => RefreshWitnessCardState();

    private static string BuildMissingDataNote(ResolvedBuild b)
    {
        int a = b.UnknownAffixNids?.Count ?? 0, u = b.UnknownUniqueIds?.Count ?? 0;
        if (a + u == 0) return "";
        var parts = new List<string>(2);
        if (a > 0) parts.Add(a == 1 ? "1 affix" : $"{a} affixes");
        if (u > 0) parts.Add(u == 1 ? "1 unique" : $"{u} uniques");
        return $"{string.Join(" and ", parts)} from this build {(a + u == 1 ? "isn't" : "aren't")} "
            + "in the local game data (likely added in a newer patch), so the filter skips "
            + (a + u == 1 ? "it." : "them.");
    }

    private static string BuildMissingDataNotes(IEnumerable<ResolvedBuild> builds) =>
        string.Join(" ", builds.Select(BuildMissingDataNote)
            .Where(note => !string.IsNullOrEmpty(note)));

    /// <summary>The branded in-game filter title (D4 shows this as the filter name on import).
    /// Auto-seeded to the brand (+ build when it still fits 24 chars); editable, recompiles live.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitleLengthNote))]
    private string filterTitle = "";
    partial void OnFilterTitleChanged(string value)
    {
        if (_suppressRecompile) return;
        _filterTitleDirty = true;
        Recompile();
    }

    /// <summary>The brand stamped on every filter — "MedicK's Might" rides along into the game, so
    /// anyone who imports a shared code sees it (the NeverSink play). The capital K nods to the clan
    /// tag MK (Medick's Madhouse); in the app header the M and K glow red. Tagline: "Filters Made EZ".</summary>
    public const string BrandName = "MedicK's Might";
    /// <summary>D4 drops filter/rule names over 24 chars on import (verified in-game), so the title
    /// field is capped here and the encoder clamps too.</summary>
    public const int MaxTitleLength = 24;
    private bool _suppressRecompile;
    private bool _filterTitleDirty;
    private string? _titleSelectionKey;
    private bool _multiItemPowerDefaultApplied;
    private bool _multiTierDefaultsApplied;
    private bool _singleGoldTier = true;
    private bool _singleSilverTier = true;
    private bool _singleLeveling;
    public bool MultiBuildTierControlsEnabled => !_multiTierDefaultsApplied;
    public bool MultiBuildLevelingEnabled => !_multiTierDefaultsApplied;
    public bool MultiBuildTierLayoutFixed => _multiTierDefaultsApplied;
    public string TitleLengthNote =>
        $"{FilterTitle.Length} / {MaxTitleLength}";

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n].TrimEnd();

    // PasteHash moved to Core.PasteStore.Hash so the paste identity can be computed server-side too.

    // Option toggles — each recompiles the filter live. Defaults = the full recommended filter.
    [ObservableProperty] private bool optLeveling;
    [ObservableProperty] private bool optPerSlot = true;   // recommended default (falls back to combined w/o slot data)
    [ObservableProperty] private bool optGoldTier = true;  // legacy option name: Red 3+ chase tier
    [ObservableProperty] private bool optSilverTier = true; // legacy option name: Pink 3+ keeper tier
    [ObservableProperty] private bool optBuildUniques = true;
    // OFF by default: at endgame nearly every drop is max item power, so 900/850 highlights are pure
    // clutter and aren't build-scoped. We want OUR build's items (the Red/Pink match). Leaving it
    // off also frees 2 rules — a 10-slot Barb with both tiers fits 25 instead of 27. A gearing aid only.
    [ObservableProperty] private bool optItemPowerTiers;
    [ObservableProperty] private bool optGreaterAffixes = true;
    [ObservableProperty] private bool optCharmsSeals = true;
    [ObservableProperty] private bool optCharmsSealsAncestral = true;
    [ObservableProperty] private bool optCodex = true;
    [ObservableProperty] private bool optHideRest = true;

    /// <summary>v1.0.2 (Medick): LEVELING mode. Off by default — strict IS the standard now (Red 3+
    /// legendaries, Pink 3+ rares). On = adds the SILVER 2+ affix-rare tier and forces combined
    /// tiers so it fits the 25-rule cap. Coarse by design; best with a leveling build loaded.</summary>
    partial void OnOptLevelingChanged(bool value) => Recompile();
    partial void OnOptPerSlotChanged(bool value) => Recompile();
    partial void OnOptGoldTierChanged(bool value) => Recompile();
    partial void OnOptSilverTierChanged(bool value) => Recompile();
    partial void OnOptBuildUniquesChanged(bool value) => Recompile();
    partial void OnOptItemPowerTiersChanged(bool value)
    {
        if (!_suppressRecompile) _multiItemPowerDefaultApplied = false;
        Recompile();
    }
    partial void OnOptGreaterAffixesChanged(bool value) => Recompile();
    partial void OnOptCharmsSealsChanged(bool value) => Recompile();
    partial void OnOptCharmsSealsAncestralChanged(bool value) => Recompile();
    partial void OnOptCodexChanged(bool value) => Recompile();
    partial void OnOptHideRestChanged(bool value) => Recompile();

    // ── v1.0.2: per-tier rarity refinement. Defaults ARE the strict standard: Red = legendaries
    //    only, Pink = rares only (both 3+). These flip bits inside the existing red/pink rules; the
    //    rule count never changes, so a player can hand-tune (e.g. add rares back to Red) freely. ──
    [ObservableProperty] private bool optRedRares = false;        // strict standard: Red = legendaries only
    [ObservableProperty] private bool optRedLegendaries = true;
    [ObservableProperty] private bool optRedAncestralOnly = false;
    [ObservableProperty] private bool optPinkRares = true;
    [ObservableProperty] private bool optPinkLegendaries = false; // strict standard: Pink = rares only
    [ObservableProperty] private bool optPinkAncestralOnly = false;
    partial void OnOptRedRaresChanged(bool value) => Recompile();
    partial void OnOptRedLegendariesChanged(bool value) => Recompile();
    partial void OnOptRedAncestralOnlyChanged(bool value) => Recompile();
    partial void OnOptPinkRaresChanged(bool value) => Recompile();
    partial void OnOptPinkLegendariesChanged(bool value) => Recompile();
    partial void OnOptPinkAncestralOnlyChanged(bool value) => Recompile();

    // v1.0.2 cube-bases rule (Horadric research): 2-on-build-affix MAGIC items = craft fodder.
    // Opt-in by founder call — the Crafting Coach is the on-ramp that tells players to enable it.
    [ObservableProperty] private bool optCubeBases = false;
    partial void OnOptCubeBasesChanged(bool value) => Recompile();

    // ── v1.0.3/1.0.4: collapsible option sections ───────────────────────────────────────────────
    //  v1.0.4: "Filter options" now defaults COLLAPSED (payoff-first: the import code + Copy sit at
    //  the top of the result page, so a newcomer isn't met by a wall of toggles). "Charm sets" still
    //  defaults open. Neither is persisted, so both reset on every app launch. The two big unique
    //  lists DO remember their open/closed state across launches (UiStateStore) and reopen only when
    //  a DIFFERENT build loads — reloading the same build keeps them where the user left them
    //  (reset-on-different-build lives in Ingest()).
    [ObservableProperty] private bool filterOptionsExpanded = false;
    [ObservableProperty] private bool charmSetsExpanded = true;
    [ObservableProperty] private bool uniqueCharmsExpanded = true;
    [ObservableProperty] private bool uniquesExpanded = true;

    private readonly UiState _uiState = UiStateStore.Load();

    partial void OnUniqueCharmsExpandedChanged(bool value) { _uiState.UniqueCharmsExpanded = value; UiStateStore.Save(_uiState); }
    partial void OnUniquesExpandedChanged(bool value) { _uiState.UniquesExpanded = value; UiStateStore.Save(_uiState); }

    /// <summary>Collapsed-header state line ("· all shown" / "· N hidden") so a player reads the
    /// selection state without expanding the long checkbox lists. Refreshed from Recompile().</summary>
    public string UniquesSummary
    {
        get
        {
            if (UniqueItemOptions.Count == 0) return "";
            int hidden = UniqueItemOptions.Count(o => !o.IsChecked);
            return hidden > 0 ? $"· {hidden} hidden" : "· all shown";
        }
    }
    public string UniqueCharmsSummary
    {
        get
        {
            if (UniqueCharmOptions.Count == 0) return "";
            int hidden = UniqueCharmOptions.Count(o => !o.IsChecked);
            return hidden > 0 ? $"· {hidden} hidden" : "· all shown";
        }
    }

    private FilterOptions CurrentOptions => new()
    {
        PinkMinAffixes = 3,                       // strict standard: both tiers at 3+
        PerSlotRules = OptPerSlot,
        Leveling = OptLeveling && !_multiTierDefaultsApplied, // Super Builds are fixed 3+ endgame hunts

        // v1.0.1: null while no build offers sets (legacy catch-all); otherwise exactly the
        // checked boxes — empty means "user unchecked everything" (charm rules omitted → hidden).
        TalismanSets = TalismanSetOptions.Count > 0
            ? TalismanSetOptions.Where(o => o.IsChecked).Select(o => o.Set).ToList()
            : null,
        // v1.0.2: the checked unique charms (all by default). The compiler collapses a full set to
        // the compact type-based show-all rule; a subset becomes an id-list; empty = none shown.
        UniqueCharms = UniqueCharmOptions.Where(o => o.IsChecked).Select(o => o.Charm.Id).ToList(),
        // v1.0.2 hide-list: the UNCHECKED uniques get hidden (uniques show by default). All checked
        // (default) → empty → no hide rule, so nothing changes for the normal player.
        HideUniques = UniqueItemOptions.Where(o => !o.IsChecked).Select(o => o.Item.Id).ToList(),
        GoldTier = OptGoldTier,
        BuildUniques = OptBuildUniques,
        SilverTier = OptSilverTier,
        RedRares = OptRedRares,
        RedLegendaries = OptRedLegendaries,
        RedAncestralOnly = OptRedAncestralOnly,
        PinkRares = OptPinkRares,
        PinkLegendaries = OptPinkLegendaries,
        PinkAncestralOnly = OptPinkAncestralOnly,
        CubeBases = OptCubeBases,
        ItemPowerTiers = OptItemPowerTiers,
        GreaterAffixes = OptGreaterAffixes,
        CharmsSeals = OptCharmsSeals,
        Codex = OptCodex,
        HideRest = OptHideRest,
    };

    // ── Commands ──

    [RelayCommand]
    private async Task CompileAsync()
    {
        // Hand-entered URL: no tier-chip provenance. Clear leftovers from any earlier chip click
        // so a result-page ★ doesn't stamp this build with another build's tier.
        _currentTierKind = null;
        _currentTier = null;
        await RunCompileAsync(MaxrollUrl);
    }

    [RelayCommand]
    private async Task LoadSampleAsync()
    {
        var sample = FindSample();
        if (sample is null)
        {
            StatusMessage = "Bundled sample build not found next to the app.";
            return;
        }
        _currentTierKind = null;
        _currentTier = null;
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
            // Community paste favorites: synthetic "paste://<hash>" URL → re-load from sidecar text
            // + re-run the paste parser. Source label tagged "Community" so the chip stays
            // identified as a community paste and the result-page ★ re-favorites the same URL.
            if (source.StartsWith("paste://", StringComparison.OrdinalIgnoreCase))
            {
                var hash = source["paste://".Length..];
                var text = _pasteStore.Load(hash);
                if (text is null)
                {
                    StatusMessage = "This community paste's text was deleted — re-paste it to reload.";
                    State = AppState.Input;
                    return;
                }
                var pasted = await Task.Run(() => PastedBuild.Parse(text, "Pasted Build"));
                SetCurrentSource("Community", source);
                _currentTierKind = null;
                _currentTier = null;
                Ingest(pasted, "Community paste");
                return;
            }
            // Source-routing now lives in Core.BuildResolver (extracted from here so it's testable
            // and web-reusable). Task.Run keeps the parse/CPU work off the UI thread.
            var (resolved, srcLabel) = await Task.Run(() => _resolveBuild(source));
            // URL-loaded builds: capture provenance for the result-page ★ button + source pill.
            // If the build came from a tier chip, LoadBuildFromChip already pre-seeded
            // TierKind/Tier; here we refine Source to whatever the fetcher actually resolved.
            SetCurrentSource(srcLabel, source);
            Ingest(resolved, srcLabel);
        }
        catch (Exception ex)
        {
            // This try block also covers local compile and favorites persistence, not just the
            // network resolve — a "check the URL" message would be wrong for those stages and
            // there's no cheap way to tell them apart here, so keep the copy stage-neutral.
            AppLog.Write("compile", $"build load failed: {ex.ToString()}");
            StatusMessage = "Couldn't finish loading that build — details are in the app log.";
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
            var pasted = PastedText;
            var resolved = await Task.Run(() => PastedBuild.Parse(pasted, "Pasted Build"));
            // Community paste favorites: synthesize a stable "paste://<hash>" pseudo-URL so the
            // favorite is starrable + identifiable, AND drop the raw text into the PasteStore
            // sidecar so a future ★ favorite click on the landing page can re-load it.
            var hash = PasteStore.Hash(pasted);
            _pasteStore.Save(hash, pasted);
            SetCurrentSource("Community", $"paste://{hash}");
            _currentTierKind = null;
            _currentTier = null;
            Ingest(resolved, "Community paste");
        }
        catch (IOException ex)
        {
            // The pasted text itself parsed fine — this is the sidecar write (PasteStore.Save)
            // failing, a distinct stage from parsing. Don't tell the player to fix their paste.
            AppLog.Write("paste", $"pasted build storage failed: {ex.ToString()}");
            StatusMessage = "Couldn't save that paste locally — the build itself parsed fine.";
            State = AppState.Input;
        }
        catch (Exception ex)
        {
            AppLog.Write("paste", $"pasted build parse failed: {ex.ToString()}");
            StatusMessage = "Couldn't parse that pasted build — check the format and try again.";
            State = AppState.Input;
        }
    }

    /// <summary>Shared: take a resolved build, fill the variant checklist, compile, show results.</summary>
    internal void Ingest(ResolvedBuild resolved, string source,
        IReadOnlyList<ResolvedBuild>? additionalSuperBuilds = null)
    {
        if (string.IsNullOrWhiteSpace(resolved.Source)) resolved = resolved with { Source = source };
        if (string.IsNullOrWhiteSpace(resolved.SourceUrl) && !string.IsNullOrWhiteSpace(_currentSourceUrl))
            resolved = resolved with { SourceUrl = _currentSourceUrl };

        // A load attempt owns the result surface immediately. If parsing or validation stops below,
        // no copyable code or result-page feedback from the previous build may survive behind it.
        ImportCode = "";
        FilterInfo = "";
        _lastFilterOutput = null;
        CurrentWitnessCard = null;
        WitnessCardBlockReason = "Compile a safe filter to create a share card.";
        WitnessCardConfirmation = "";
        OnPropertyChanged(nameof(CanShareWitnessCard));
        AutoFitNote = "";
        CapWarning = "";
        SuperBuildAdvisory = "";
        BuildDriftNote = "";
        _singleBuildDriftNote = "";
        CopyConfirmation = "";
        _secondResolved = null;
        _superResolved.Clear();
        if (additionalSuperBuilds is not null) _superResolved.AddRange(additionalSuperBuilds);
        var loadedBuilds = new[] { resolved }.Concat(_superResolved).ToList();
        ResetTitleDirtyForDifferentSelection(loadedBuilds);
        if (_superResolved.Count > 0)
        {
            ApplyMultiItemPowerDefault();
            ApplyMultiTierDefaults();
        }
        else
        {
            RestoreSingleBuildItemPowerDefault();
            RestoreSingleTierDefaults();
        }
        SecondBuildName = "";
        ArmorySource = "";
        ArmoryStatus = "";
        ArmoryClassNote = "";
        OnPropertyChanged(nameof(HasSecondBuild));
        OnPropertyChanged(nameof(ArmoryBuildNames));

        // A zero-variant parse is normally scraper drift. Recompile cannot compile it, so never pair
        // the new build's identity with the previous build's code or pretend a result was produced.
        if (!resolved.HasUsableVariants)
        {
            _resolved = null;
            Variants.Clear();
            StatusMessage = $"{source} returned no usable variants — the site may have changed; try again or paste affixes";
            State = AppState.Input;
            return;
        }

        _resolved = resolved;
        BuildName = resolved.Build;

        // v1.0.3: force the two unique lists open when a DIFFERENT build loads (a new build likely
        // wants different choices); keep them as the user left them when the same build reloads (F5
        // refresh, or re-pulling the same Whirlwind while testing). State persists across launches.
        var buildKey = !string.IsNullOrEmpty(_currentSourceUrl) ? _currentSourceUrl : resolved.Build;
        if (!string.Equals(buildKey, _uiState.LastBuildKey, StringComparison.Ordinal))
        {
            _uiState.LastBuildKey = buildKey;
            UniqueCharmsExpanded = true;   // setters persist _uiState
            UniquesExpanded = true;
            UiStateStore.Save(_uiState);   // persist the new key even if both were already open
            // v1.0.4 FIX: drop the previous build's charm-set checkboxes so the NEW build's detected
            // sets pre-check correctly. Recompile rebuilds TalismanSetOptions from ForClass(); clearing
            // here makes its priorChecks snapshot empty, so it falls through to the new build's detected
            // sets instead of freezing on the prior build's (the "every Barb ran Crucible" bug).
            TalismanSetOptions.Clear();
        }

        // Surface (and log — season-day triage gold) any ids the local game data couldn't name.
        MissingDataNote = BuildMissingDataNotes(new[] { resolved }.Concat(_superResolved));
        if (resolved.UnknownDataCount > 0)
            AppLog.Write("gamedata", $"build '{resolved.Build}': unknown affix nids "
                + $"[{string.Join(",", resolved.UnknownAffixNids ?? [])}], unknown unique ids "
                + $"[{string.Join(",", resolved.UnknownUniqueIds ?? [])}]");

        // Auto-brand the in-game filter title (the name D4 shows on import). Lead with the brand so it
        // rides every shared code; tack on the build only when the whole thing still fits D4's 24-char
        // cap, else just the brand. Editable below (field also capped at 24).
        // Single-build title seeding stays byte-for-byte as it was before Super Build. Multi-build
        // seeding happens below, once BuildTagger has resolved the selected builds' display tags.
        if (_superResolved.Count == 0)
            SeedSingleBuildTitle(resolved);

        // Populate one ordered checklist per selected build. VariantOption applies the Leveling-off
        // default independently in every group; the primary collection remains public for the
        // established single-build bindings/tests.
        ResetVariantGroups(new[] { resolved }.Concat(_superResolved).ToList());

        Recompile(RecompileCause.BuildContextChanged);
        State = AppState.Result;

        // A snapshot is earned only by a real, copyable compile. Compare before replacing the
        // baseline, then keep the note visible while the persisted favorite moves forward.
        if (!string.IsNullOrEmpty(ImportCode)
            && !string.IsNullOrEmpty(_currentSourceUrl)
            && !_currentSourceUrl.StartsWith("paste://", StringComparison.OrdinalIgnoreCase)
            && _favorites.Find(_currentSourceUrl) is { } favorite)
        {
            var diff = BuildDrift.Compare(favorite.Snapshot, resolved);
            if (diff is { HasDrift: true })
            {
                var days = Math.Max(0, (int)(DateTime.UtcNow - diff.BaselineCapturedUtc).TotalDays);
                BuildDriftNote = $"What changed since your last compile ({days} "
                    + $"{(days == 1 ? "day" : "days")} ago): {diff.Summary}";
            }
            _favorites.Update(favorite with { Snapshot = BuildSnapshot.Capture(resolved) });
            RefreshFavoritesUi();
        }
        // Result-page ★ button: refresh state + visibility for whichever URL we just loaded (paste
        // mode clears _currentSourceUrl so the button hides).
        var currentUrl = CurrentResultBuild()?.SourceUrl;
        IsCurrentFavorited = !string.IsNullOrEmpty(currentUrl) && _favorites.Contains(currentUrl);
        OnPropertyChanged(nameof(CanFavoriteCurrent));
    }

    /// <summary>Armory Mode's second seat. URL input uses the same injected resolver as the normal
    /// load path; free-form text uses the same community-paste parser. Two is the hard cap here.</summary>
    [RelayCommand]
    private async Task AddSecondBuildAsync()
    {
        if (_resolved is null)
        {
            ArmoryStatus = "Load the first build before adding an Armory build.";
            return;
        }
        if (RefuseArmoryForLoadedSuperBuild())
            return;
        if (string.IsNullOrWhiteSpace(ArmorySource))
        {
            ArmoryStatus = "Paste a second build URL or affix list first.";
            return;
        }

        ArmoryStatus = "Loading second build…";
        try
        {
            var input = ArmorySource.Trim();
            bool isUrl = Uri.TryCreate(input, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
            var resolved = isUrl
                ? (await Task.Run(() => _resolveBuild(input))).build
                : await Task.Run(() => PastedBuild.Parse(input, "Pasted Build"));
            IngestSecond(resolved, isUrl ? input : null);
        }
        catch (UserMessageException ex)
        {
            // Authored product copy (e.g. "Load the first build before adding an Armory build.") —
            // not dev noise. Surface it verbatim; still logged for triage.
            AppLog.Write("armory", $"second build load failed: {ex.ToString()}");
            ArmoryStatus = ex.Message;
        }
        catch (Exception ex)
        {
            AppLog.Write("armory", $"second build load failed: {ex.ToString()}");
            ArmoryStatus = "Couldn't load the second build — check the URL or pasted text and try again.";
        }
    }

    internal void IngestSecond(ResolvedBuild resolved, string? sourceUrl = null)
    {
        if (_resolved is null)
            throw new UserMessageException("Load the first build before adding an Armory build.");
        if (RefuseArmoryForLoadedSuperBuild())
            return;
        if (!resolved.HasUsableVariants)
        {
            ArmoryStatus = "Second build returned no usable variants — the site may have changed.";
            return;
        }
        if (!string.IsNullOrWhiteSpace(sourceUrl))
            resolved = resolved with
            {
                Source = string.IsNullOrWhiteSpace(resolved.Source) ? _currentSource : resolved.Source,
                SourceUrl = sourceUrl,
            };

        if (_secondResolved is null) _singleBuildDriftNote = BuildDriftNote;
        else
        {
            _buildColorCustomizations.Remove(BuildColorKey(_secondResolved));
            BuildDriftNote = _singleBuildDriftNote;
        }
        _superResolved.Clear();
        while (VariantGroups.Count > 1) VariantGroups.RemoveAt(VariantGroups.Count - 1);
        _secondResolved = resolved;
        SecondBuildName = resolved.Build;
        ArmoryStatus = "";
        MissingDataNote = BuildMissingDataNotes([_resolved, resolved]);
        ResetTitleDirtyForDifferentSelection([_resolved, resolved]);

        OnPropertyChanged(nameof(HasSecondBuild));
        OnPropertyChanged(nameof(ArmoryBuildNames));
        AddVariantGroup(resolved);
        ApplyMultiItemPowerDefault();
        ApplyMultiTierDefaults();
        Recompile(RecompileCause.BuildContextChanged);

        // Match the primary seat's earned-baseline rule: compile first, then compare and advance
        // only when the attempted Armory payload produced a real import code.
        if (!string.IsNullOrEmpty(ImportCode)
            && !string.IsNullOrEmpty(sourceUrl)
            && _favorites.Find(sourceUrl) is { Snapshot: not null } favorite)
        {
            var diff = BuildDrift.Compare(favorite.Snapshot, resolved);
            if (diff is { HasDrift: true })
            {
                var days = Math.Max(0, (int)(DateTime.UtcNow - diff.BaselineCapturedUtc).TotalDays);
                var note = $"{resolved.Build} changed since your last compile ({days} "
                    + $"{(days == 1 ? "day" : "days")} ago): {diff.Summary}";
                BuildDriftNote = string.IsNullOrEmpty(BuildDriftNote) ? note : BuildDriftNote + "\n" + note;
            }
            _favorites.Update(favorite with { Snapshot = BuildSnapshot.Capture(resolved) });
            RefreshFavoritesUi();
        }
    }

    private bool RefuseArmoryForLoadedSuperBuild()
    {
        if (_superResolved.Count == 0) return false;
        var buildCount = 1 + _superResolved.Count;
        ArmoryStatus = $"A {buildCount}-build Super Build is already loaded. Armory Mode won't replace or discard "
            + "any of its selections; start a new build selection instead.";
        return true;
    }

    [RelayCommand]
    private void DropSecondBuild()
    {
        var second = _secondResolved;
        var droppedGroups = second is null
            ? VariantGroups.Skip(1).ToList()
            : VariantGroups.Where(group =>
                BuildColorKey(group.Build).Equals(BuildColorKey(second), StringComparison.OrdinalIgnoreCase))
                .ToList();
        foreach (var group in droppedGroups)
        {
            _buildColorCustomizations.Remove(BuildColorKey(group.Build));
            VariantGroups.Remove(group);
        }
        _secondResolved = null;
        _superResolved.Clear();
        while (VariantGroups.Count > 1) VariantGroups.RemoveAt(VariantGroups.Count - 1);
        if (VariantGroups.Count == 1) VariantGroups[0].SetPriority(1);
        RefreshPriorityChoices();
        RefreshBuildColors();
        RefreshVariantGroupHeaders();
        SecondBuildName = "";
        ArmorySource = "";
        ArmoryStatus = "";
        ArmoryClassNote = "";
        BuildDriftNote = _singleBuildDriftNote;
        MissingDataNote = _resolved is null ? "" : BuildMissingDataNotes([_resolved]);
        OnPropertyChanged(nameof(HasSecondBuild));
        OnPropertyChanged(nameof(ArmoryBuildNames));
        RestoreSingleBuildItemPowerDefault();
        RestoreSingleTierDefaults();
        if (_resolved is not null)
        {
            ResetTitleDirtyForDifferentSelection([_resolved]);
            SeedSingleBuildTitle(_resolved);
        }
        Recompile(RecompileCause.BuildContextChanged);
    }

    private void ResetVariantGroups(IReadOnlyList<ResolvedBuild> builds)
    {
        var activeKeys = builds.Select(BuildColorKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in _buildColorCustomizations.Keys.Where(key => !activeKeys.Contains(key)).ToList())
            _buildColorCustomizations.Remove(stale);
        Variants.Clear();
        VariantGroups.Clear();
        for (int i = 0; i < builds.Count; i++)
        {
            var options = i == 0 ? Variants : new ObservableCollection<VariantOption>();
            foreach (var variant in builds[i].Variants)
                options.Add(new VariantOption(variant,
                    () => Recompile(RecompileCause.BuildContextChanged)));
            VariantGroups.Add(new BuildVariantGroup(builds[i], options, builds[i].Build,
                i + 1, ChangeBuildPriority, ChangeBuildColor));
        }
        RefreshPriorityChoices();
        RefreshBuildColors();
        RefreshVariantGroupHeaders();
    }

    private void AddVariantGroup(ResolvedBuild build)
    {
        var options = new ObservableCollection<VariantOption>();
        foreach (var variant in build.Variants)
            options.Add(new VariantOption(variant,
                () => Recompile(RecompileCause.BuildContextChanged)));
        VariantGroups.Add(new BuildVariantGroup(build, options, build.Build,
            VariantGroups.Count + 1, ChangeBuildPriority, ChangeBuildColor));
        RefreshPriorityChoices();
        RefreshBuildColors();
        RefreshVariantGroupHeaders();
    }

    private void ChangeBuildPriority(BuildVariantGroup changed, int requestedPriority)
    {
        if (!VariantGroups.Contains(changed)
            || requestedPriority is < 1 or > 4
            || requestedPriority > VariantGroups.Count)
            return;

        var previousPriority = changed.Priority;
        var occupied = VariantGroups.FirstOrDefault(group =>
            !ReferenceEquals(group, changed) && group.Priority == requestedPriority);
        occupied?.SetPriority(previousPriority);
        changed.SetPriority(requestedPriority);

        var ordered = VariantGroups.OrderBy(group => group.Priority).ToList();
        for (int target = 0; target < ordered.Count; target++)
        {
            var current = VariantGroups.IndexOf(ordered[target]);
            if (current != target) VariantGroups.Move(current, target);
        }

        BuildName = VariantGroups[0].BuildName;
        OnPropertyChanged(nameof(CanFavoriteCurrent));
        var currentUrl = CurrentResultBuild()?.SourceUrl;
        IsCurrentFavorited = !string.IsNullOrEmpty(currentUrl) && _favorites.Contains(currentUrl);
        RefreshBuildColors();
        RefreshVariantGroupHeaders();
        Recompile(RecompileCause.BuildContextChanged);
    }

    private void RefreshPriorityChoices()
    {
        foreach (var group in VariantGroups)
            group.SetPriorityCount(VariantGroups.Count);
    }

    private void ChangeBuildColor(BuildVariantGroup group, bool chase, FilterColorEntry color)
    {
        if (!VariantGroups.Contains(group) || VariantGroups.Count < 2) return;
        int index = VariantGroups.IndexOf(group);
        var key = BuildColorKey(group.Build);
        _buildColorCustomizations.TryGetValue(key, out var current);
        uint? chaseChoice = current?.Chase;
        uint? keeperChoice = current?.Keeper;
        if (chase)
            chaseChoice = color.Value == RuledChaseColor(VariantGroups.Count, index) ? null : color.Value;
        else
            keeperChoice = color.Value == RuledKeeperColor(VariantGroups.Count, index) ? null : color.Value;

        if (chaseChoice is null && keeperChoice is null)
            _buildColorCustomizations.Remove(key);
        else
            _buildColorCustomizations[key] = new(chaseChoice, keeperChoice);
        Recompile();
    }

    private void RefreshBuildColors()
    {
        for (int i = 0; i < VariantGroups.Count; i++)
        {
            var group = VariantGroups[i];
            _buildColorCustomizations.TryGetValue(BuildColorKey(group.Build), out var custom);
            var chase = FilterColors.EntryFor(custom?.Chase ?? RuledChaseColor(VariantGroups.Count, i));
            var keeper = FilterColors.EntryFor(custom?.Keeper ?? RuledKeeperColor(VariantGroups.Count, i));
            group.SetColors(chase, keeper, VariantGroups.Count > 1);
        }
    }

    private static uint RuledChaseColor(int buildCount, int index) =>
        buildCount == 2 && index == 1 ? FilterColors.Pink : FilterColors.Red;

    private static uint RuledKeeperColor(int buildCount, int index) =>
        buildCount == 2 && index == 1 ? FilterColors.Silver : FilterColors.Gold;

    private static string BuildColorKey(ResolvedBuild build) =>
        BuildColorKey(build.Source, build.SourceUrl, build.Build, build.Class);

    private static string BuildColorKey(string source, string url, string buildName, string className) =>
        !string.IsNullOrWhiteSpace(url)
            ? $"{source.Trim()}\u001f{url.Trim()}"
            : $"{source.Trim()}\u001fresolved://{className.Trim()}/{buildName.Trim()}";

    private void RefreshVariantGroupHeaders()
    {
        if (VariantGroups.Count == 0) return;
        if (VariantGroups.Count == 1)
        {
            VariantGroups[0].Header = VariantGroups[0].BuildName;
            return;
        }

        var tags = BuildTagger.Resolve(VariantGroups
            .Select(group => FilterCompiler.Analyze(group.Build, FilterColors.Red, FilterColors.Pink))
            .ToList());
        for (int i = 0; i < VariantGroups.Count; i++)
            VariantGroups[i].Header = tags[i].Equals(VariantGroups[i].BuildName,
                StringComparison.OrdinalIgnoreCase)
                ? VariantGroups[i].BuildName
                : $"{tags[i]} · {VariantGroups[i].BuildName}";
    }

    private void ApplyMultiItemPowerDefault()
    {
        if (OptItemPowerTiers) return;
        _suppressRecompile = true;
        OptItemPowerTiers = true;
        _suppressRecompile = false;
        _multiItemPowerDefaultApplied = true;
    }

    private void RestoreSingleBuildItemPowerDefault()
    {
        if (!_multiItemPowerDefaultApplied) return;
        _suppressRecompile = true;
        OptItemPowerTiers = false;
        _suppressRecompile = false;
        _multiItemPowerDefaultApplied = false;
    }

    private void ApplyMultiTierDefaults()
    {
        if (!_multiTierDefaultsApplied)
        {
            _singleGoldTier = OptGoldTier;
            _singleSilverTier = OptSilverTier;
            _singleLeveling = OptLeveling;
            _multiTierDefaultsApplied = true;
        }
        _suppressRecompile = true;
        OptGoldTier = true;
        OptSilverTier = true;
        OptLeveling = false;
        _suppressRecompile = false;
        OnPropertyChanged(nameof(MultiBuildTierControlsEnabled));
        OnPropertyChanged(nameof(MultiBuildLevelingEnabled));
        OnPropertyChanged(nameof(MultiBuildTierLayoutFixed));
    }

    private void RestoreSingleTierDefaults()
    {
        if (!_multiTierDefaultsApplied) return;
        _multiTierDefaultsApplied = false;
        _suppressRecompile = true;
        OptGoldTier = _singleGoldTier;
        OptSilverTier = _singleSilverTier;
        OptLeveling = _singleLeveling;
        _suppressRecompile = false;
        OnPropertyChanged(nameof(MultiBuildTierControlsEnabled));
        OnPropertyChanged(nameof(MultiBuildLevelingEnabled));
        OnPropertyChanged(nameof(MultiBuildTierLayoutFixed));
    }

    /// <summary>Analyze + compile from the currently-selected variants. Re-runs whenever the
    /// user toggles a variant checkbox. Pure/synchronous (no network) so it's fine on the UI thread.</summary>
    /// <summary>Live plain-words scope lines under the tier toggles (v1.0.2 UX): the UI narrates
    /// what each tier will highlight, so no toggle's effect hides in a tooltip. Both tiers are 3+
    /// under the strict standard; the looser 2+ rares live in the opt-in Leveling silver tier.</summary>
    public string RedTierSummary => "Red " + FilterCompiler.DescribeTierScope(
        OptGoldTier, FilterCompiler.Strict,
        OptRedRares, OptRedLegendaries, OptRedAncestralOnly)
        + (OptGoldTier && !OptRedRares
            ? "  (keep 'Show rares' off. it's off by design: turning it on paints your 3+ rares Red instead of Pink, and Pink stops working)"
            : "");
    public string PinkTierSummary => "Pink " + FilterCompiler.DescribeTierScope(
        OptSilverTier, FilterCompiler.Strict,
        OptPinkRares, OptPinkLegendaries, OptPinkAncestralOnly)
        + (OptSilverTier && !OptPinkLegendaries
            ? "  (keep 'Show legendaries' off. it's off by design: Red already covers legendaries, so it does nothing here)"
            : "");

    /// <summary>Why a recompile is running. Both variant checkboxes and charm-set checkboxes call back
    /// into Recompile, so without this it cannot tell them apart — and the rule that protects a user's
    /// charm choices (below) was silently freezing the charm sets when the BUILD changed instead.</summary>
    private enum RecompileCause
    {
        /// <summary>An option the user set by hand: a charm box, a unique, a tier, the title. Their
        /// charm-set choices must survive the recompile their own click triggered.</summary>
        OptionToggled,

        /// <summary>The build itself changed: a variant picked, a build loaded, an Armory seat added or
        /// dropped. The charm sets must be re-derived from the build now on screen.</summary>
        BuildContextChanged,
    }

    private void Recompile() => Recompile(RecompileCause.OptionToggled);

    private void Recompile(RecompileCause cause)
    {
        // Bulk toggles (Select all / Unselect all) flip many checkboxes at once; each would call
        // back in here, so they raise _suppressRecompile and recompile ONCE at the end.
        if (_suppressRecompile) return;

        // Every toggle that can change a tier's scope funnels through here — refresh the
        // narration lines first so the UI always tells the truth about the code below it.
        OnPropertyChanged(nameof(RedTierSummary));
        OnPropertyChanged(nameof(PinkTierSummary));
        OnPropertyChanged(nameof(UniquesSummary));
        OnPropertyChanged(nameof(UniqueCharmsSummary));
        CopyConfirmation = "";   // a fresh code makes any prior "Copied" confirmation stale

        if (_resolved is null) return;

        var selectedBuilds = new List<ResolvedBuild>(VariantGroups.Count);
        foreach (var group in VariantGroups)
        {
            var selectedVariants = group.Variants.Where(v => v.IsSelected)
                .Select(v => v.Variant).ToList();
            if (selectedVariants.Count == 0)
            {
                // The result page is still visible while its last checkbox is cleared. Retire every
                // piece of the previous compile here so neither the button nor the surrounding status
                // can offer yesterday's code as if it represented an empty selection.
                _copyBlockReason = VariantGroups.Count == 1
                    ? "Select at least one variant to include in the filter."
                    : $"Select at least one variant for '{group.BuildName}' to include in the filter.";
                _lastFilterOutput = null;
                ImportCode = "";
                FilterInfo = "";
                AutoFitNote = "";
                CapWarning = "";
                RefreshWitnessCardState();
                StatusMessage = _copyBlockReason;
                return;
            }
            selectedBuilds.Add(group.Build with { Variants = selectedVariants });
        }

        // Legacy callers always reach Ingest first, but keep the established primary collection as
        // a defensive fallback if a future host constructs result state without grouped variants.
        if (selectedBuilds.Count == 0)
            selectedBuilds.Add(_resolved with
            {
                Variants = Variants.Where(v => v.IsSelected).Select(v => v.Variant).ToList()
            });

        var primaryGroup = VariantGroups.FirstOrDefault();
        var build = selectedBuilds[0];
        var selected = build.Variants;
        var compiledBuilds = new List<CompiledBuild>();
        for (int i = 0; i < selectedBuilds.Count; i++)
        {
            var analyzed = FilterCompiler.Analyze(selectedBuilds[i], FilterColors.Red, FilterColors.Pink);
            if (selectedBuilds.Count > 1)
            {
                _buildColorCustomizations.TryGetValue(BuildColorKey(selectedBuilds[i]), out var custom);
                analyzed = analyzed with
                {
                    ChaseColorOverride = custom?.Chase,
                    KeeperColorOverride = custom?.Keeper,
                    ChaseColorUserChosen = custom?.Chase is not null,
                    KeeperColorUserChosen = custom?.Keeper is not null,
                };
            }
            compiledBuilds.Add(analyzed);
        }
        var compiled = compiledBuilds[0];
        var allSelectedVariants = selectedBuilds.SelectMany(b => b.Variants).ToList();
        bool isMultiBuild = compiledBuilds.Count > 1;
        var displayBuilds = DisplayBuildsInOrder(compiledBuilds);
        var displayTags = isMultiBuild ? BuildTagger.Resolve(displayBuilds) : [];
        if (isMultiBuild && !_filterTitleDirty)
            SetFilterTitleWithoutDirtying(DefaultMultiBuildTitle(displayTags));
        var unknownTalismanSets = allSelectedVariants
            .SelectMany(v => v.UnknownTalismanSets ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Crafting Coach greets with the loaded build (the coach's whole hook is build context).
        Coach.BuildLine = $"Coaching for your {build.Class} — {build.Build}. " +
            "Gold-glowing blues from your filter are perfect practice pieces.";

        // v1.0.1: refresh the per-class set checkboxes BEFORE compiling (CurrentOptions reads them).
        // The build's sets come pre-checked; a user's manual choices survive recompiles (merged by
        // set id). Toggling a box calls back into this method — the ctor writes the field directly
        // and same-value setter writes don't re-fire, so there is no loop.
        // The merge below keeps a user's manual narrowing alive across the recompile their own click
        // triggers — the list is cleared and rebuilt here, so without it their tick would undo itself.
        // But a BUILD change is not a tick: carrying the old checkboxes forward there left the previous
        // variant's charm sets frozen on screen (defaultSetIds was computed correctly, then discarded).
        var priorChecks = cause == RecompileCause.BuildContextChanged
            ? new Dictionary<uint, bool>()
            : TalismanSetOptions.ToDictionary(o => o.Set.Id, o => o.IsChecked);
        // Some sources (especially Mobalytics) do not expose a build's equipped charm sets. Detection
        // is evaluated PER BUILD: a healthy build must never lend its detected set names to a broken
        // neighbor and suppress that neighbor's fail-open. This also matters when the healthy build
        // exposes a Generic set, because Generic sets appear in every class picker.
        var classes = compiledBuilds.Select(b => b.Class).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var classSets = classes.SelectMany(TalismanSetDatabase.ForClass).DistinctBy(s => s.Id).ToList();
        bool classUnknown = classes.Any(c => !TalismanSetDatabase.IsKnownClass(c));
        var defaultSetIds = new HashSet<uint>();
        bool noneDetected = false;
        foreach (var compiledBuild in compiledBuilds)
        {
            var defaults = TalismanSetDatabase.DefaultSelectionForClass(
                compiledBuild.Class, compiledBuild.TalismanSets, out bool thisBuildNone);
            noneDetected |= thisBuildNone;
            defaultSetIds.UnionWith(defaults.Select(s => s.Id));
        }
        bool unknownSetsDetected = unknownTalismanSets.Count > 0;
        // A source can name one set we know and one the current catalog does not. Narrowing to the
        // known set would quietly feed the renamed set's charms to Hide the rest, so drift anywhere
        // in the detected list must default the whole class open just like no detection at all.
        if (unknownSetsDetected)
            defaultSetIds = classSets.Select(s => s.Id).ToHashSet();
        bool enteringFailOpen = (noneDetected || classUnknown || unknownSetsDetected)
            && !CharmSetsUndetected;
        TalismanSetOptions.Clear();
        foreach (var s in classSets)
            TalismanSetOptions.Add(new TalismanSetOption(s,
                // Crossing into the safety fallback must make its narration true immediately.
                // Once open, later recompiles preserve any narrowing the player chooses.
                !enteringFailOpen && priorChecks.TryGetValue(s.Id, out var was)
                    ? was
                    : defaultSetIds.Contains(s.Id),
                Recompile));
        CharmSetsUndetected = noneDetected || classUnknown || unknownSetsDetected;
        CharmSetSelectionEnabled = !classUnknown;
        ArmoryClassNote = classes.Count > 1
            ? $"Different classes loaded ({string.Join(" + ", classes)}) — charm sets for both classes are offered."
            : "";
        CharmSetSafetyNote = classUnknown
            ? "Class unknown — showing all charm sets. Load a class-identified guide to narrow them."
            : unknownSetsDetected
                ? TalismanSetDatabase.UnknownSetWarning(unknownTalismanSets)
                : "At least one selected build didn't list its charm sets, so all sets for that build's class are shown (nothing hidden). Uncheck any you don't want.";
        OnPropertyChanged(nameof(HasTalismanSetOptions));

        var title = string.IsNullOrWhiteSpace(FilterTitle) ? BrandName : FilterTitle.Trim();
        // Auto-fit (v1.0.3): if per-slot precision would blow the 25-rule cap (a big endgame build
        // sits right at it, and curating uniques adds a hide rule), compile combined instead so the
        // code is always importable. The per-slot checkbox stays as the user set it; a calm note
        // below explains the swap. See FilterCompiler.CompileWithinCap.
        var (output, fitReport) = _compileWithinCap(
            compiledBuilds, CurrentOptions, MaxRules, "Filter", title);
        _copyBlockReason = CopySafety.BlockReason(output, MaxRules);

        BuildSubtitle = !isMultiBuild
            ? $"{build.Class}   •   {selected.Count} of {primaryGroup?.Build.Variants.Count ?? build.Variants.Count} variants   •   {compiled.Pool.Count} filterable affixes in the pool"
            : $"{compiledBuilds.Count} builds   •   {compiledBuilds.Sum(b => b.Pool.Count)} filterable affixes across independent build rules";
        for (int i = 0; i < compiledBuilds.Count; i++)
            VariantGroups[i].SetTierEmission(compiledBuilds[i].Pool.Count > 0,
                "Tier colors inactive — not emitted because no filterable affixes remain in the selected variants.");
        BuildTierLegend = BuildTierLegendFor(compiledBuilds, displayTags, output.HasCustomBuildColors);

        PoolLines.Clear();
        foreach (var b in compiledBuilds)
            foreach (var name in b.PoolNames) PoolLines.Add(!isMultiBuild ? name : $"{b.Name}: {name}");
        DroppedLines.Clear();
        foreach (var b in compiledBuilds)
            foreach (var d in b.Dropped) DroppedLines.Add(!isMultiBuild ? d : $"{b.Name}: {d}");
        UniquePurpleLines.Clear();
        PopulateOwnedUniqueLines(UniquePurpleLines, displayBuilds, displayTags, isMultiBuild,
            build => build.UniquesTargeted);
        UniquePendingLines.Clear();
        PopulateOwnedUniqueLines(UniquePendingLines, displayBuilds, displayTags, isMultiBuild,
            build => build.UniquesPending);
        OnPropertyChanged(nameof(HasDropped));
        OnPropertyChanged(nameof(HasPurple));
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(HasNoUniques));

        // The compiler withholds code when empty affix mapping plus Hide the rest could produce a
        // filter that hides everything. Keeping ImportCode empty also makes CopyCodeAsync refuse it.
        ImportCode = output.IsCopyable ? output.ImportCode : "";
        // User-facing metadata: just the rule count (D4 caps at 25 — CapWarning below kicks in
        // over the limit). Byte count + round-trip status were dev-validation noise per Medick.
        // If round-trip ever fails, surface it loud — but the encoder's been stable for sessions.
        var buildCountLabel = compiledBuilds.Count == 1 ? "1 build" : $"{compiledBuilds.Count} builds";
        FilterInfo = !output.IsCopyable
            ? $"{buildCountLabel} · ⚠ Filter not generated — see the safety warning below"
            : output.RoundTripOk
            ? $"{buildCountLabel} · {output.RuleCount} / {MaxRules} rules"
            : $"{buildCountLabel} · ⚠ {output.RuleCount} rules — filter is corrupted, regenerate before importing";
        AutoFitNote = fitReport.Fits && fitReport.WasAdjusted
            ? fitReport.Describe(compiledBuilds.Count, MaxRules)
            : "";
        var warnings = output.Diagnostics
            .Where(d => !d.StartsWith("No filter code was produced because ", StringComparison.Ordinal))
            .ToList();
        if (unknownSetsDetected)
            warnings.Add(TalismanSetDatabase.UnknownSetWarning(unknownTalismanSets));
        if (!fitReport.Fits)
            warnings.Add(fitReport.Describe(compiledBuilds.Count, MaxRules));
        CapWarning = warnings.Count > 0 ? "⚠ " + string.Join("\n", warnings) : "";
        RouteCompilerAdvisories(output.Advisories);

        _lastFilterOutput = output;
        RefreshWitnessCardState();

        StatusMessage = "";
    }

    /// <summary>Recompose on every compile/warning transition. A favorite drift alarm is also a
    /// warning band: sharing waits until the player has reviewed and re-established the build.</summary>
    private void RefreshWitnessCardState()
    {
        if (_lastFilterOutput is null || _resolved is null)
        {
            CurrentWitnessCard = null;
            WitnessCardBlockReason = _copyBlockReason
                ?? "Compile a safe filter to create a share card.";
        }
        else
        {
            var warning = !string.IsNullOrWhiteSpace(CapWarning) ? CapWarning
                : !string.IsNullOrWhiteSpace(BuildDriftNote) ? BuildDriftNote
                : null;
            var loadedBuilds = VariantGroups.Count > 0
                ? VariantGroups.Select(group => group.Build).ToList()
                : [_resolved];
            var primaryBuild = loadedBuilds[0];
            var composition = WitnessCardComposer.Compose(new WitnessCardRequest(
                primaryBuild.Build, primaryBuild.Class, _currentSource, _currentTierKind, _currentTier,
                _lastFilterOutput, MaxRules, DiscordUrl, warning,
                BuildIdentities: loadedBuilds
                    .Select(b => WitnessCardComposer.Identity(b.Build, b.Class)).ToList()));
            CurrentWitnessCard = composition.Card;
            WitnessCardBlockReason = composition.BlockReason ?? "";
        }
        WitnessCardConfirmation = "";
        OnPropertyChanged(nameof(CurrentWitnessCard));
        OnPropertyChanged(nameof(CanShareWitnessCard));
    }

    internal void ReportWitnessCardSuccess(string message)
    {
        WitnessCardConfirmation = message;
        StatusMessage = message;
    }

    internal void ReportWitnessCardFailure(string message)
    {
        WitnessCardConfirmation = $"⚠ {message}";
        StatusMessage = message;
    }

    /// <summary>Copy-button label. Flips to "✓ Copied" on a successful copy, then resets — gives the
    /// result page a positive success beat instead of the page's only feedback being warning bands.</summary>
    [ObservableProperty]
    private string copyButtonText = "📋 Copy";

    /// <summary>v1.0.4: persistent copy confirmation shown on the result page. The intended "Copied"
    /// message was set on StatusMessage but never rendered in the result state, so a first-timer only
    /// saw the button flip for 1.6s. Cleared on every recompile — a new code makes the old copy stale.</summary>
    [ObservableProperty]
    private string copyConfirmation = "";

    // Set from the same compiler output that produced ImportCode. Keeping this separate from the
    // display strings means the copy guard cannot be fooled by future wording changes in the UI.
    private string? _copyBlockReason;

    [RelayCommand]
    private async Task CopyCodeAsync()
    {
        if (_copyBlockReason is not null)
        {
            StatusMessage = _copyBlockReason;
            CopyConfirmation = _copyBlockReason;
            return;
        }
        if (string.IsNullOrEmpty(ImportCode)) return;
        try
        {
            _setClipboardText(ImportCode);
            StatusMessage = "Copied — paste into D4's Loot Filter → Import.";
            CopyConfirmation = "✓ Copied. Paste into Diablo 4: Loot Filter → Import.";
            CopyButtonText = "✓ Copied";
            await Task.Delay(1600);
            CopyButtonText = "📋 Copy";
        }
        catch (Exception ex)
        {
            // Result state does not render StatusMessage. Replace any prior green success line at
            // its visible source, and retire the transient success label even if failure interrupts it.
            AppLog.Write("clipboard", $"copy failed: {ex.ToString()}");
            StatusMessage = "Couldn't copy to the clipboard — try again.";
            CopyConfirmation = "⚠ Couldn't copy to the clipboard — try again.";
            CopyButtonText = "📋 Copy";
        }
    }

    // ── Community + support links ──
    // Ko-fi is live (Medick's real page).
    public const string KofiUrl    = "https://ko-fi.com/medick94265";
    // Permanent invite (never expires) deliberately capped at 25 uses — anti-bot hygiene: an
    //   uncapped public invite invites a 100-bot cleanup job. When it fills, Medick mints a fresh
    //   one and bumps this constant (Discord → Server Settings → Invites).
    public const string DiscordUrl = "https://discord.gg/jnTk6Ha2ue";

    [RelayCommand] private void OpenKofi()    => OpenUrl(KofiUrl);
    [RelayCommand] private void OpenDiscord() => OpenUrl(DiscordUrl);

    /// <summary>v1.0.4: open the current build's ORIGINAL guide page on its source site
    /// (Maxroll / D4Builds / Mobalytics) in the browser — attribution plus lets the player read the
    /// full guide. No-op for pasted builds (they have no source URL).</summary>
    [RelayCommand]
    private void OpenBuildGuide()
    {
        if (!string.IsNullOrEmpty(_currentSourceUrl)) OpenUrl(_currentSourceUrl);
    }

    [RelayCommand]
    private void OpenMaxroll() => OpenUrl("https://maxroll.gg/d4/build-guides");

    [RelayCommand]
    private void Restart()
    {
        State = AppState.Input;
        StatusMessage = "";
        _resolved = null;
        _secondResolved = null;
        _superResolved.Clear();
        _buildColorCustomizations.Clear();
        _filterTitleDirty = false;
        _titleSelectionKey = null;
        RestoreSingleTierDefaults();
        _singleBuildDriftNote = "";
        SecondBuildName = "";
        ArmorySource = "";
        ArmoryStatus = "";
        ArmoryClassNote = "";
        OnPropertyChanged(nameof(HasSecondBuild));
        OnPropertyChanged(nameof(ArmoryBuildNames));
        MissingDataNote = "";
        Variants.Clear();
        PoolLines.Clear();
        DroppedLines.Clear();
        UniquePurpleLines.Clear();
        UniquePendingLines.Clear();
    }

    private void SeedSingleBuildTitle(ResolvedBuild resolved)
    {
        var titled = $"{BrandName} · {resolved.Build}";
        // v1.0.4: when "Brand · Build" is too long for D4's 24-char cap, fall back to the BUILD name
        // (trimmed) rather than the generic brand, so multiple imported filters stay distinguishable.
        SetFilterTitleWithoutDirtying(titled.Length <= MaxTitleLength
            ? titled
            : Truncate(resolved.Build, MaxTitleLength));
    }

    private void SetFilterTitleWithoutDirtying(string title)
    {
        _suppressRecompile = true;
        FilterTitle = title;
        _suppressRecompile = false;
    }

    internal void CommitFilterTitle()
    {
        var committed = FilterTitle.Trim();
        if (committed.Length == 0) committed = BrandName;
        if (!string.Equals(FilterTitle, committed, StringComparison.Ordinal))
            FilterTitle = committed;
    }

    private void ResetTitleDirtyForDifferentSelection(IReadOnlyList<ResolvedBuild> builds)
    {
        // Membership, not ordering, defines a selection. S4 priority swaps must not discard a
        // custom title; only adding, removing, or replacing a selected build resets dirty state.
        var key = string.Join('\u001f', builds
            .Select(build => !string.IsNullOrWhiteSpace(build.SourceUrl)
                ? build.SourceUrl.Trim()
                : $"resolved://{build.Source}/{build.Class}/{build.Build}")
            .OrderBy(identity => identity, StringComparer.OrdinalIgnoreCase));
        if (!string.Equals(_titleSelectionKey, key, StringComparison.OrdinalIgnoreCase))
            _filterTitleDirty = false;
        _titleSelectionKey = key;
    }

    private void RouteCompilerAdvisories(IReadOnlyList<string> compilerAdvisories)
    {
        var advisories = new List<string>();
        if (_superResolved.Count > 0 && !string.IsNullOrWhiteSpace(_selectionAdvisory))
            advisories.Add(_selectionAdvisory);
        advisories.AddRange(compilerAdvisories.Where(advisory => !string.IsNullOrWhiteSpace(advisory)));
        SuperBuildAdvisory = string.Join("\n", advisories.Distinct(StringComparer.Ordinal));
    }

    private static string BuildTierLegendFor(IReadOnlyList<CompiledBuild> builds,
        IReadOnlyList<string> tags, bool hasCustomColors)
    {
        if (builds.Count == 1)
            return "● Red: 3+ affix legendaries · ancestral charms\n● Pink: 3+ affix rares\n● Gold: Cube Bases";

        var lines = new List<string>
        {
            hasCustomColors
                ? "Active scheme — custom colors"
                : builds.Count == 2 ? "Active scheme — colors by build" : "Active scheme — colors by tier",
        };
        for (int i = 0; i < builds.Count; i++)
        {
            if (builds[i].Pool.Count == 0)
            {
                lines.Add($"○ {tags[i]}: tiers not emitted — no filterable affixes remain in the selected variants");
                continue;
            }
            var chase = builds[i].ChaseColorOverride ?? RuledChaseColor(builds.Count, i);
            var keeper = builds[i].KeeperColorOverride ?? RuledKeeperColor(builds.Count, i);
            lines.Add($"● {FilterColors.EntryFor(chase).FullName}: {tags[i]} chase · legendaries (3+)");
            lines.Add($"● {FilterColors.EntryFor(keeper).FullName}: {tags[i]} keeper · rares (3+)");
        }
        lines.Add("● Red also marks ancestral charms");
        return string.Join('\n', lines);
    }

    /// <summary>Display ownership follows the same priority-ordered list sent to the compiler.</summary>
    private static IReadOnlyList<CompiledBuild> DisplayBuildsInOrder(
        IReadOnlyList<CompiledBuild> priorityOrderedBuilds) => priorityOrderedBuilds;

    private static string DefaultMultiBuildTitle(IReadOnlyList<string> tags)
    {
        if (tags.Count == 0) return "";

        var title = tags[0]; // Priority 1 is never truncated.
        for (int i = 1; i < tags.Count; i++)
        {
            var withNextTag = $"{title} + {tags[i]}";
            int stillOmitted = tags.Count - i - 1;
            var completeCandidate = stillOmitted == 0
                ? withNextTag
                : $"{withNextTag} +{stillOmitted}";
            if (completeCandidate.Length <= MaxTitleLength)
            {
                title = withNextTag;
                continue;
            }

            return $"{title} +{tags.Count - i}";
        }
        return title;
    }

    private static void PopulateOwnedUniqueLines(ObservableCollection<string> target,
        IReadOnlyList<CompiledBuild> displayBuilds, IReadOnlyList<string> displayTags,
        bool isMultiBuild, Func<CompiledBuild, IReadOnlyList<string>> selectUniques)
    {
        if (!isMultiBuild)
        {
            foreach (var unique in selectUniques(displayBuilds[0])
                         .Distinct(StringComparer.OrdinalIgnoreCase))
                target.Add(unique);
            return;
        }

        var entries = new List<(string Name, List<string> Owners)>();
        var entryByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int buildIndex = 0; buildIndex < displayBuilds.Count; buildIndex++)
        {
            foreach (var unique in selectUniques(displayBuilds[buildIndex])
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!entryByName.TryGetValue(unique, out int entryIndex))
                {
                    entryIndex = entries.Count;
                    entryByName.Add(unique, entryIndex);
                    entries.Add((unique, []));
                }
                entries[entryIndex].Owners.Add(displayTags[buildIndex]);
            }
        }

        foreach (var entry in entries)
            target.Add($"{entry.Name} ({string.Join(", ", entry.Owners)})");
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
            AppLog.Write("browser", $"could not open link: {ex.ToString()}");
            StatusMessage = "Couldn't open your browser for that link.";
        }
    }

    /// <summary>Locate the bundled offline demo planner shipped next to the exe. baseDirectory is
    /// test-only (defaults to AppContext.BaseDirectory in production) — lets a test point this at a
    /// temp folder without touching the real exe's output directory.</summary>
    internal static string? FindSample(string? baseDirectory = null)
    {
        var p = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "Assets", "sample_barb.json");
        return File.Exists(p) ? p : null;
    }
}
