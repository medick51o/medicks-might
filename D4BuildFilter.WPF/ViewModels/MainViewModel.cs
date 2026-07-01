using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D4BuildFilter.Core;
using D4BuildFilter.WPF.Services;

namespace D4BuildFilter.WPF.ViewModels;

public enum AppState
{
    Input,
    Loading,
    Result,
}

/// <summary>Top-nav destinations (the web-app shell). Browse = tier lists, Compile = the URL/paste
/// form, Favorites = saved builds, Artwork = gallery mode (chrome only — the warlord art at full
/// strength). The Result view is shown on top after a compile.</summary>
public enum InputTab { Browse, Compile, Favorites, Artwork }

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
    private InputTab activeTab = InputTab.Browse;

    public bool IsBrowseTab => ActiveTab == InputTab.Browse;
    public bool IsCompileTab => ActiveTab == InputTab.Compile;
    public bool IsFavTab => ActiveTab == InputTab.Favorites;
    public bool IsArtworkTab => ActiveTab == InputTab.Artwork;

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
    private readonly IFavoritesStore _favorites = new FavoritesStore();
    private readonly IPasteStore _pasteStore = new PasteStore();
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
            GameDataStatus = $"Update failed: {ex.Message}";
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
    public bool CanFavoriteCurrent => !string.IsNullOrEmpty(_currentSourceUrl);

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
        var candidate = new FavoriteEntry(
            Id: Guid.NewGuid().ToString("N"),
            Url: vm.Url,
            Source: vm.Source,
            TierKind: vm.TierKind,
            Tier: vm.Tier,
            Name: vm.Name,
            ClassName: vm.ClassName,
            DateAdded: DateTime.UtcNow,
            DateLastOpened: DateTime.UtcNow);
        _favorites.Toggle(candidate);
        RefreshFavoritesUi();
    }

    /// <summary>Star/unstar the currently-loaded build (result-page ★ button). Used when the build
    /// came from a paste or a raw URL paste — i.e. no tier chip involved — so TierKind/Tier are null.</summary>
    [RelayCommand]
    private void ToggleFavoriteCurrent()
    {
        if (string.IsNullOrEmpty(_currentSourceUrl)) return;
        var candidate = new FavoriteEntry(
            Id: Guid.NewGuid().ToString("N"),
            Url: _currentSourceUrl,
            Source: _currentSource,
            TierKind: _currentTierKind,
            Tier: _currentTier,
            Name: string.IsNullOrEmpty(BuildName) ? "(unnamed build)" : BuildName,
            ClassName: _resolved?.Class ?? "",
            DateAdded: DateTime.UtcNow,
            DateLastOpened: DateTime.UtcNow);
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
            Favorites.Add(new FavoriteChipVM(entry, _ => LoadBuildFromFavorite(entry), RemoveFavorite));
        }
        OnPropertyChanged(nameof(HasFavorites));
        OnPropertyChanged(nameof(HasNoFavorites));
        OnPropertyChanged(nameof(FavoritesCountLabel));

        var favUrls = new HashSet<string>(_favorites.All.Select(f => f.Url),
            StringComparer.OrdinalIgnoreCase);
        foreach (var b in AllCachedChips()) b.IsFavorited = favUrls.Contains(b.Url);
        IsCurrentFavorited = !string.IsNullOrEmpty(_currentSourceUrl)
            && favUrls.Contains(_currentSourceUrl);
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

    public MainViewModel()
    {
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
    private List<TierGroupVM> BuildGroups(TierList list, string source, string tierKind)
    {
        var favUrls = new HashSet<string>(_favorites.All.Select(f => f.Url),
            StringComparer.OrdinalIgnoreCase);
        return list.Builds.GroupBy(b => b.Tier)
            .Select(g => new TierGroupVM(g.Key, g.Select(b =>
                new TierBuildVM(b, source, tierKind, g.Key,
                    url => LoadBuildFromChipUrl(b, source, tierKind, g.Key),
                    ToggleFavorite,
                    favUrls.Contains(b.Url),
                    openSource: OpenUrl)).ToList()))
            .ToList();
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

    /// <summary>Tier chip click handler: capture chip provenance (so the result-page ★ knows where
    /// the build came from) and stamp the favorite as re-opened if it's one of the user's.</summary>
    private void LoadBuildFromChipUrl(TierBuild b, string source, string tierKind, string tier)
    {
        SetCurrentSource(source, b.Url);
        _currentTierKind = tierKind;
        _currentTier = tier;
        if (_favorites.Contains(b.Url)) _favorites.StampOpened(b.Url);
        LoadBuildFromUrl(b.Url);
    }

    /// <summary>Favorites-rail click handler: seed provenance from the SAVED entry (previously this
    /// path left whatever tier the last-clicked tier chip set, so re-starring from the result page
    /// could persist a tier the build never had) and advance DateLastOpened, which the chip tooltip
    /// surfaces as a staleness cue.</summary>
    private void LoadBuildFromFavorite(FavoriteEntry f)
    {
        SetCurrentSource(f.Source, f.Url);
        _currentTierKind = f.TierKind;
        _currentTier = f.Tier;
        _favorites.StampOpened(f.Url);
        LoadBuildFromUrl(f.Url);
    }

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
    /// <summary>Inverse of HasPurple || HasPending. Drives the empty-state hint in the Uniques tab
    /// (BooleanToVisibilityConverter doesn't support inversion or compound conditions).</summary>
    public bool HasNoUniques => !HasPurple && !HasPending;

    // ── Result: the compiled filter + the user's option toggles ──
    [ObservableProperty] private string importCode = "";
    [ObservableProperty] private string filterInfo = "";

    /// <summary>D4 rejects any filter with more than 25 rules on import. Set when the current
    /// toggles push past that so the UI can warn (both Gold + Silver tiers on a many-slot build
    /// is the usual culprit).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCapWarning))]
    private string capWarning = "";
    public bool HasCapWarning => !string.IsNullOrEmpty(CapWarning);
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

    /// <summary>The branded in-game filter title (D4 shows this as the filter name on import).
    /// Auto-seeded to the brand (+ build when it still fits 24 chars); editable, recompiles live.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitleLengthNote))]
    private string filterTitle = "";
    partial void OnFilterTitleChanged(string value) { if (!_suppressRecompile) Recompile(); }

    /// <summary>The brand stamped on every filter — "MedicK's Might" rides along into the game, so
    /// anyone who imports a shared code sees it (the NeverSink play). The capital K nods to the clan
    /// tag MK (Medick's Madhouse); in the app header the M and K glow red. Tagline: "Filters Made EZ".</summary>
    public const string BrandName = "MedicK's Might";
    /// <summary>D4 drops filter/rule names over 24 chars on import (verified in-game), so the title
    /// field is capped here and the encoder clamps too.</summary>
    public const int MaxTitleLength = 24;
    private bool _suppressRecompile;
    public string TitleLengthNote =>
        $"{FilterTitle.Length} / {MaxTitleLength}";

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n].TrimEnd();

    // PasteHash moved to Core.PasteStore.Hash so the paste identity can be computed server-side too.

    // Option toggles — each recompiles the filter live. Defaults = the full recommended filter.
    [ObservableProperty] private bool strictEndgame;
    [ObservableProperty] private bool optPerSlot = true;   // recommended default (falls back to combined w/o slot data)
    [ObservableProperty] private bool optGoldTier = true;  // 3+ affixes per slot → gold ("best items")
    [ObservableProperty] private bool optSilverTier = true; // 2+ affixes per slot → silver ("one roll away")
    [ObservableProperty] private bool optBuildUniques = true;
    // OFF by default: at endgame nearly every drop is max item power, so 900/850 highlights are pure
    // clutter and aren't build-scoped. We want OUR build's items (the Gold/Silver match). Leaving it
    // off also frees 2 rules — a 10-slot Barb with both tiers fits 25 instead of 27. A gearing aid only.
    [ObservableProperty] private bool optItemPowerTiers;
    [ObservableProperty] private bool optGreaterAffixes = true;
    [ObservableProperty] private bool optCharmsSeals = true;
    [ObservableProperty] private bool optCharmsSealsAncestral = true;
    [ObservableProperty] private bool optCodex = true;
    [ObservableProperty] private bool optHideRest = true;

    // Dev-facing diagnostic toggle (off by default). When ON, the result page surfaces the
    // "Not yet filterable" list — affixes the mapper couldn't resolve to a filter ID. End users
    // can't act on this info; it's for us to track DB gaps. Medick explicitly flagged it as noise.
    [ObservableProperty] private bool showPendingAffixes;

    partial void OnStrictEndgameChanged(bool value) => Recompile();
    partial void OnOptPerSlotChanged(bool value) => Recompile();
    partial void OnOptGoldTierChanged(bool value) => Recompile();
    partial void OnOptSilverTierChanged(bool value) => Recompile();
    partial void OnOptBuildUniquesChanged(bool value) => Recompile();
    partial void OnOptItemPowerTiersChanged(bool value) => Recompile();
    partial void OnOptGreaterAffixesChanged(bool value) => Recompile();
    partial void OnOptCharmsSealsChanged(bool value) => Recompile();
    partial void OnOptCharmsSealsAncestralChanged(bool value) => Recompile();
    partial void OnOptCodexChanged(bool value) => Recompile();
    partial void OnOptHideRestChanged(bool value) => Recompile();

    private FilterOptions CurrentOptions => new()
    {
        StrictEndgame = StrictEndgame,
        PerSlotRules = OptPerSlot,
        GoldTier = OptGoldTier,
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
            var (resolved, srcLabel) = await Task.Run(
                () => BuildResolver.ResolveAsync(source, NameLookup.Default(), UniqueLookup.Default()));
            // URL-loaded builds: capture provenance for the result-page ★ button + source pill.
            // If the build came from a tier chip, LoadBuildFromChip already pre-seeded
            // TierKind/Tier; here we refine Source to whatever the fetcher actually resolved.
            SetCurrentSource(srcLabel, source);
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

        // Surface (and log — season-day triage gold) any ids the local game data couldn't name.
        MissingDataNote = BuildMissingDataNote(resolved);
        if (resolved.UnknownDataCount > 0)
            AppLog.Write("gamedata", $"build '{resolved.Build}': unknown affix nids "
                + $"[{string.Join(",", resolved.UnknownAffixNids ?? [])}], unknown unique ids "
                + $"[{string.Join(",", resolved.UnknownUniqueIds ?? [])}]");

        // Auto-brand the in-game filter title (the name D4 shows on import). Lead with the brand so it
        // rides every shared code; tack on the build only when the whole thing still fits D4's 24-char
        // cap, else just the brand. Editable below (field also capped at 24).
        _suppressRecompile = true;
        var titled = $"{BrandName} · {resolved.Build}";
        FilterTitle = titled.Length <= MaxTitleLength ? titled : BrandName;
        _suppressRecompile = false;

        // Populate the variant checklist (all on by default). The field initializer keeps
        // IsSelected=true without firing OnIsSelectedChanged, so Recompile runs just once below.
        Variants.Clear();
        foreach (var v in resolved.Variants)
            Variants.Add(new VariantOption(v, Recompile));

        Recompile();
        State = AppState.Result;
        // Result-page ★ button: refresh state + visibility for whichever URL we just loaded (paste
        // mode clears _currentSourceUrl so the button hides).
        IsCurrentFavorited = !string.IsNullOrEmpty(_currentSourceUrl) && _favorites.Contains(_currentSourceUrl);
        OnPropertyChanged(nameof(CanFavoriteCurrent));
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
        var compiled = FilterCompiler.Analyze(build, FilterColors.Red, FilterColors.Pink);
        var title = string.IsNullOrWhiteSpace(FilterTitle) ? BrandName : FilterTitle.Trim();
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
        OnPropertyChanged(nameof(HasDropped));
        OnPropertyChanged(nameof(HasPurple));
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(HasNoUniques));

        ImportCode = output.ImportCode;
        // User-facing metadata: just the rule count (D4 caps at 25 — CapWarning below kicks in
        // over the limit). Byte count + round-trip status were dev-validation noise per Medick.
        // If round-trip ever fails, surface it loud — but the encoder's been stable for sessions.
        FilterInfo = output.RoundTripOk
            ? $"{output.RuleCount} / {MaxRules} rules"
            : $"⚠ {output.RuleCount} rules — filter is corrupted, regenerate before importing";
        CapWarning = output.RuleCount > MaxRules
            ? $"⚠ {output.RuleCount} rules — Diablo 4 rejects filters over {MaxRules} on import. "
              + "Turn off a tier (Gold 3+ or Silver 2+), or deselect some variants, to get back under the limit."
            : "";

        StatusMessage = "";
    }

    /// <summary>Copy-button label. Flips to "✓ Copied" on a successful copy, then resets — gives the
    /// result page a positive success beat instead of the page's only feedback being warning bands.</summary>
    [ObservableProperty]
    private string copyButtonText = "📋 Copy";

    [RelayCommand]
    private async Task CopyCodeAsync()
    {
        if (string.IsNullOrEmpty(ImportCode)) return;
        try
        {
            Clipboard.SetText(ImportCode);
            StatusMessage = "Copied — paste into D4's Loot Filter → Import.";
            CopyButtonText = "✓ Copied";
            await Task.Delay(1600);
            CopyButtonText = "📋 Copy";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Clipboard error: {ex.Message}";
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

    [RelayCommand]
    private void OpenMaxroll() => OpenUrl("https://maxroll.gg/d4/build-guides");

    [RelayCommand]
    private void Restart()
    {
        State = AppState.Input;
        StatusMessage = "";
        _resolved = null;
        MissingDataNote = "";
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
