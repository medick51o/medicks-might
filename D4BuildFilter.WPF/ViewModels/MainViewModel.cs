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
    [NotifyPropertyChangedFor(nameof(IsNotInputState))]
    private AppState state = AppState.Input;

    public bool IsInputState => State == AppState.Input;
    public bool IsLoadingState => State == AppState.Loading;
    public bool IsResultState => State == AppState.Result;
    /// <summary>Header swaps between a big "wtf Immortan Joe" splash (landing) and a slim banner
    /// (loading + result). Once business starts, the art shrinks down so the build screen drives.</summary>
    public bool IsNotInputState => State != AppState.Input;

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

    private readonly Dictionary<MaxrollList, List<TierGroupVM>> _maxrollCache = new();
    private readonly Dictionary<D4BuildsList, List<TierGroupVM>> _d4buildsCache = new();
    private readonly Dictionary<MobalyticsList, List<TierGroupVM>> _mobalyticsCache = new();

    // ── Favorites (persisted to %LOCALAPPDATA%\MedicKsMight\favorites.json) ──
    // The chips on the landing page's "Your Favorites" row + the star indicator on every tier chip
    // are both driven from this store. Favorites are LIVE REFERENCES: when the user re-opens one we
    // re-fetch from the source, so meta drift over the season is reflected automatically.
    private readonly FavoritesStore _favorites = new();
    public ObservableCollection<FavoriteChipVM> Favorites { get; } = new();
    public bool HasFavorites => Favorites.Count > 0;

    /// <summary>Provenance of the currently-loaded build — set when a build is loaded so the result
    /// page's ★ Favorite button knows what to persist. <see cref="_currentTierKind"/>/<see cref="_currentTier"/>
    /// are only known when the user clicked a tier chip (not when they pasted a URL).</summary>
    private string _currentSource = "";
    private string _currentSourceUrl = "";
    private string? _currentTierKind;
    private string? _currentTier;

    [ObservableProperty] private bool isCurrentFavorited;
    public bool CanFavoriteCurrent => !string.IsNullOrEmpty(_currentSourceUrl);

    partial void OnActiveMaxrollListChanged(MaxrollList value) { SyncTabs(MaxrollTabs, value.ToString()); _ = ActivateMaxrollAsync(value); }
    partial void OnActiveD4BuildsListChanged(D4BuildsList value) { SyncTabs(D4BuildsTabs, value.ToString()); _ = ActivateD4BuildsAsync(value); }
    partial void OnActiveMobalyticsListChanged(MobalyticsList value) { SyncTabs(MobalyticsTabs, value.ToString()); _ = ActivateMobalyticsAsync(value); }

    private static void SyncTabs(IReadOnlyList<TierTabVM> tabs, string activeKey)
    {
        foreach (var t in tabs) t.IsActive = t.Key == activeKey;
    }

    [RelayCommand]
    private void SelectMaxrollTab(string key)
    {
        if (Enum.TryParse<MaxrollList>(key, out var k)) ActiveMaxrollList = k;
    }
    [RelayCommand]
    private void SelectD4BuildsTab(string key)
    {
        if (Enum.TryParse<D4BuildsList>(key, out var k)) ActiveD4BuildsList = k;
    }
    [RelayCommand]
    private void SelectMobalyticsTab(string key)
    {
        if (Enum.TryParse<MobalyticsList>(key, out var k)) ActiveMobalyticsList = k;
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
        RefreshFavoritesUi();
    }

    /// <summary>Reconcile the Favorites collection + every cached tier chip's IsFavorited flag with
    /// the current store contents. Cheap: tier-list caches are at most ~50 chips per source.</summary>
    private void RefreshFavoritesUi()
    {
        Favorites.Clear();
        foreach (var f in _favorites.All.OrderByDescending(f => f.DateAdded))
            Favorites.Add(new FavoriteChipVM(f, LoadBuildFromUrl, RemoveFavorite));
        OnPropertyChanged(nameof(HasFavorites));

        var favUrls = new HashSet<string>(_favorites.All.Select(f => f.Url),
            StringComparer.OrdinalIgnoreCase);
        foreach (var b in AllCachedChips()) b.IsFavorited = favUrls.Contains(b.Url);
        IsCurrentFavorited = !string.IsNullOrEmpty(_currentSourceUrl)
            && favUrls.Contains(_currentSourceUrl);
    }

    private IEnumerable<TierBuildVM> AllCachedChips()
    {
        foreach (var g in _maxrollCache.Values.SelectMany(v => v))
            foreach (var b in g.Builds) yield return b;
        foreach (var g in _d4buildsCache.Values.SelectMany(v => v))
            foreach (var b in g.Builds) yield return b;
        foreach (var g in _mobalyticsCache.Values.SelectMany(v => v))
            foreach (var b in g.Builds) yield return b;
    }

    public MainViewModel()
    {
        // Build the tab strips. Default Endgame is active; other tabs come up dim and fetch on click.
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

    private Task ActivateMaxrollAsync(MaxrollList kind) =>
        ActivateAsync(kind, _maxrollCache, MaxrollTiers, s => MaxrollTierStatus = s,
            ct => TierListFetcher.FetchMaxrollAsync(kind, ct),
            "Maxroll", kind.ToString());

    private Task ActivateD4BuildsAsync(D4BuildsList kind) =>
        ActivateAsync(kind, _d4buildsCache, D4BuildsTiers, s => D4BuildsTierStatus = s,
            ct => TierListFetcher.FetchD4BuildsAsync(kind, ct),
            "D4Builds", kind.ToString());

    private Task ActivateMobalyticsAsync(MobalyticsList kind) =>
        ActivateAsync(kind, _mobalyticsCache, MobalyticsTiers, s => MobalyticsTierStatus = s,
            ct => TierListFetcher.FetchMobalyticsAsync(kind, ct),
            "Mobalytics", kind.ToString());

    /// <summary>Cache-aware tab activation: serve from cache if hit, else fetch + populate + cache.
    /// Generic over the per-source enum + per-source fetcher so all three sources share one flow.
    /// <paramref name="source"/> + <paramref name="tierKind"/> stamp each chip with its provenance
    /// so a subsequent ★ favorite remembers where the build came from.</summary>
    private async Task ActivateAsync<TKind>(TKind kind,
        Dictionary<TKind, List<TierGroupVM>> cache,
        ObservableCollection<TierGroupVM> target,
        Action<string> setStatus,
        Func<CancellationToken, Task<TierList>> fetch,
        string source, string tierKind) where TKind : notnull
    {
        if (cache.TryGetValue(kind, out var hit))
        {
            target.Clear();
            foreach (var g in hit) target.Add(g);
            setStatus(hit.Count == 0 ? "No builds in this list yet — open the full list ↗" : "");
            return;
        }
        setStatus("Loading…");
        target.Clear();
        try
        {
            var list = await fetch(default);
            var favUrls = new HashSet<string>(_favorites.All.Select(f => f.Url),
                StringComparer.OrdinalIgnoreCase);
            var groups = list.Builds.GroupBy(b => b.Tier)
                .Select(g => new TierGroupVM(g.Key, g.Select(b =>
                    new TierBuildVM(b, source, tierKind, g.Key,
                        url => LoadBuildFromChipUrl(b, source, tierKind, g.Key),
                        ToggleFavorite,
                        favUrls.Contains(b.Url))).ToList()))
                .ToList();
            cache[kind] = groups;
            foreach (var g in groups) target.Add(g);
            setStatus(groups.Count == 0 ? "No builds in this list yet — open the full list ↗" : "");
        }
        catch
        {
            setStatus("Couldn't load right now — open the full list ↗");
        }
    }

    /// <summary>Tier chip click handler: capture chip provenance (so the result-page ★ knows where
    /// the build came from) and stamp the favorite as re-opened if it's one of the user's.</summary>
    private void LoadBuildFromChipUrl(TierBuild b, string source, string tierKind, string tier)
    {
        _currentSource = source;
        _currentSourceUrl = b.Url;
        _currentTierKind = tierKind;
        _currentTier = tier;
        if (_favorites.Contains(b.Url)) _favorites.StampOpened(b.Url);
        LoadBuildFromUrl(b.Url);
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
    public ObservableCollection<string> MythicLines { get; } = new();

    public bool HasDropped => DroppedLines.Count > 0;
    public bool HasPurple => UniquePurpleLines.Count > 0;
    public bool HasPending => UniquePendingLines.Count > 0;
    public bool HasMythics => MythicLines.Count > 0;

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
        $"{FilterTitle.Length} / {MaxTitleLength} characters — D4 drops the name above this on import";

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n].TrimEnd();

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
    [ObservableProperty] private bool optCodex = true;
    [ObservableProperty] private bool optHideRest = true;

    partial void OnStrictEndgameChanged(bool value) => Recompile();
    partial void OnOptPerSlotChanged(bool value) => Recompile();
    partial void OnOptGoldTierChanged(bool value) => Recompile();
    partial void OnOptSilverTierChanged(bool value) => Recompile();
    partial void OnOptBuildUniquesChanged(bool value) => Recompile();
    partial void OnOptItemPowerTiersChanged(bool value) => Recompile();
    partial void OnOptGreaterAffixesChanged(bool value) => Recompile();
    partial void OnOptCharmsSealsChanged(bool value) => Recompile();
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
            // URL-loaded builds: capture provenance for the result-page ★ button. If the build
            // came from a tier chip, LoadBuildFromChip already pre-seeded TierKind/Tier; here we
            // refine Source to whatever the fetcher actually resolved.
            _currentSource = srcLabel;
            _currentSourceUrl = source;
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
            // Pastes have no source URL — favoriting them isn't meaningful (nothing to re-fetch).
            // Clear provenance so the result-page ★ button hides itself via CanFavoriteCurrent.
            _currentSource = "Paste";
            _currentSourceUrl = "";
            _currentTierKind = null;
            _currentTier = null;
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
        var compiled = FilterCompiler.Analyze(build, FilterColors.Gold, FilterColors.Silver);
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
        MythicLines.Clear();
        foreach (var m in compiled.Mythics) MythicLines.Add(m);
        OnPropertyChanged(nameof(HasDropped));
        OnPropertyChanged(nameof(HasPurple));
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(HasMythics));

        ImportCode = output.ImportCode;
        FilterInfo = $"{output.RuleCount} rules · {output.Bytes} bytes · round-trip {(output.RoundTripOk ? "OK ✓" : "FAILED ✗")}";
        CapWarning = output.RuleCount > MaxRules
            ? $"⚠ {output.RuleCount} rules — Diablo 4 rejects filters over {MaxRules} on import. "
              + "Turn off a tier (Gold 3+ or Silver 2+), or deselect some variants, to get back under the limit."
            : "";

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
