using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace D4BuildFilter.WPF.Services;

/// <summary>
/// Swaps the active theme ResourceDictionary at runtime + persists the choice across launches.
///
/// The theming model: each Themes/{Name}.xaml file is a ResourceDictionary of brushes/doubles
/// keyed by semantic tokens (Surface.Page, Accent.Primary, Watermark.Opacity, etc.). The XAML
/// across MainWindow.xaml reads them via {DynamicResource Token.Name} so a swap re-paints live
/// without restart. Each theme dict declares a `ThemeId` string resource as a sentinel so Apply
/// can find + unmerge the OLD theme before merging the new one.
///
/// Persistence: %LOCALAPPDATA%\MedicKsMight\theme.json. Loaded once in App.OnStartup BEFORE the
/// main window shows (synchronous, ~30 bytes) so there's no flash of default theme.
/// </summary>
public static class ThemeManager
{
    /// <summary>The three shipping themes. Order = picker display order.</summary>
    public static readonly IReadOnlyList<string> Available = new[] { "Default", "Discord", "Dark" };

    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MedicKsMight");
    private static readonly string SettingsFile = Path.Combine(SettingsDir, "theme.json");

    /// <summary>The currently-applied theme id, or "Default" before any Apply call.</summary>
    public static string Current { get; private set; } = "Default";

    /// <summary>When true, Surface.Panel + Surface.Inset get a forced low opacity so the
    /// warlord watermark surfaces through cards/inputs. Theme-agnostic — applies on top of
    /// whichever palette is active. Re-applied automatically after every Apply() call so a
    /// theme swap doesn't lose the setting.</summary>
    public static bool TranslucentPanels { get; private set; }

    /// <summary>Translucent-mode panel opacity. 0.35 lets ~65% warlord bleed-through net of the
    /// 22% global watermark — visibly translucent without losing panel text readability.</summary>
    private const double TranslucentOpacity = 0.35;

    /// <summary>Keys whose brushes get the opacity override when TranslucentPanels is on.
    /// Surface.Card already has alpha baked into the theme dicts (it's the chip/favorite/tier-
    /// list card surface on the LANDING and was always meant to bleed) so don't touch it.</summary>
    private static readonly string[] TranslucentBrushKeys = { "Surface.Panel", "Surface.Inset" };

    /// <summary>Fired AFTER a theme swap completes so subscribers can refresh non-DynamicResource
    /// surfaces (e.g. converters that cache brushes at static-init time).</summary>
    public static event Action<string>? ThemeChanged;

    /// <summary>Reads theme.json (or defaults to "Default") and merges the appropriate dictionary
    /// into Application.Current.Resources. Call once at startup from App.OnStartup BEFORE
    /// MainWindow shows — avoids a single frame of default-theme flash.</summary>
    public static void LoadAndApply()
    {
        var saved = TryReadSaved();
        TranslucentPanels = saved.translucent;
        Apply(saved.theme, persist: false);
    }

    /// <summary>Toggle the universal translucent-panels effect on/off. Re-applies immediately
    /// to all DynamicResource consumers of Surface.Panel / Surface.Inset. Persists.</summary>
    public static void SetTranslucentPanels(bool on)
    {
        if (TranslucentPanels == on) return;
        TranslucentPanels = on;
        ApplyTranslucencyOverride();
        TrySave(Current, TranslucentPanels);
    }

    /// <summary>Writes (or clears) the Surface.Panel / Surface.Inset opacity override on
    /// Application.Resources. Override sits ABOVE the theme dict in the lookup chain so it
    /// wins for {DynamicResource} bindings; cleared by removing the local entry, falling back
    /// to the theme dict's opaque brush.</summary>
    /// <summary>Cached original brushes per theme — we mutate the live merged theme dict in
    /// place when translucent is on, and need the originals to restore when it's off.
    /// Key = brush resource key (Surface.Panel etc.), value = the opaque original.</summary>
    private static readonly Dictionary<string, SolidColorBrush> _originalPanelBrushes = new();

    private static void ApplyTranslucencyOverride()
    {
        var app = Application.Current;
        if (app is null) return;

        // Find the live theme dict (carries the ThemeId sentinel). We modify ITS entries so
        // DynamicResource consumers see the change — writing to app.Resources at top level
        // doesn't fire the notification path some templates rely on.
        var themeDict = app.Resources.MergedDictionaries.FirstOrDefault(d => d.Contains("ThemeId"));
        if (themeDict is null) return;

        foreach (var key in TranslucentBrushKeys)
        {
            if (TranslucentPanels)
            {
                if (themeDict[key] is not SolidColorBrush current) continue;
                // Stash the original (only on first toggle for this theme — guard against
                // re-stashing the translucent variant on a second toggle).
                if (!_originalPanelBrushes.ContainsKey(key))
                    _originalPanelBrushes[key] = current;
                var src = _originalPanelBrushes[key];
                var c = src.Color;
                // Bake alpha into Color.A — Color.A is the most reliable signal for templates
                // that don't pick up Brush.Opacity changes (e.g. ListBox internal panels).
                var translucent = new SolidColorBrush(Color.FromArgb((byte)(255 * TranslucentOpacity), c.R, c.G, c.B));
                translucent.Freeze();
                themeDict[key] = translucent;
            }
            else
            {
                // Restore the original from cache, if we have one.
                if (_originalPanelBrushes.TryGetValue(key, out var original))
                    themeDict[key] = original;
            }
        }
    }

    /// <summary>Called from Apply() after a theme swap so the cache invalidates — the new theme
    /// has different originals than the old one.</summary>
    private static void ResetTranslucencyCache() => _originalPanelBrushes.Clear();

    /// <summary>Swap to the named theme. Idempotent — if already on this theme, no-ops.
    /// Persists the choice to disk unless <paramref name="persist"/> is false (e.g. startup load).</summary>
    public static void Apply(string themeId, bool persist = true)
    {
        if (!Available.Contains(themeId))
            throw new ArgumentException($"Unknown theme '{themeId}'. Available: {string.Join(", ", Available)}");

        if (Current == themeId && Application.Current?.Resources.Contains("ThemeId") == true)
            return;

        var app = Application.Current
            ?? throw new InvalidOperationException("ThemeManager.Apply called before Application.Current is set");
        var dicts = app.Resources.MergedDictionaries;

        // Remove the previous theme dict (identified by the ThemeId sentinel resource).
        for (int i = dicts.Count - 1; i >= 0; i--)
            if (dicts[i].Contains("ThemeId"))
                dicts.RemoveAt(i);

        // Merge the new theme.
        dicts.Add(new ResourceDictionary
        {
            Source = new Uri($"/Themes/{themeId}.xaml", UriKind.Relative)
        });

        Current = themeId;
        // Theme swap brings fresh originals — wipe the cache and re-apply translucency on top.
        ResetTranslucencyCache();
        ApplyTranslucencyOverride();
        if (persist) TrySave(themeId, TranslucentPanels);
        ThemeChanged?.Invoke(themeId);
    }

    private record SavedSettings(string theme, bool translucent, int version);

    private static (string theme, bool translucent) TryReadSaved()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return ("Default", false);
            var json = File.ReadAllText(SettingsFile);
            var parsed = JsonSerializer.Deserialize<SavedSettings>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var t = parsed?.theme is string s && Available.Contains(s) ? s : "Default";
            return (t, parsed?.translucent ?? false);
        }
        catch
        {
            return ("Default", false);
        }
    }

    private static void TrySave(string themeId, bool translucent)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(new SavedSettings(themeId, translucent, 1));
            File.WriteAllText(SettingsFile, json);
        }
        catch
        {
            // Persistence is best-effort; if it fails (locked file, disk full) just lose the
            // user's choice on next launch. Not worth blocking the UI swap.
        }
    }
}
