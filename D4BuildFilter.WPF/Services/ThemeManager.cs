using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

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

    /// <summary>Fired AFTER a theme swap completes so subscribers can refresh non-DynamicResource
    /// surfaces (e.g. converters that cache brushes at static-init time).</summary>
    public static event Action<string>? ThemeChanged;

    /// <summary>Reads theme.json (or defaults to "Default") and merges the appropriate dictionary
    /// into Application.Current.Resources. Call once at startup from App.OnStartup BEFORE
    /// MainWindow shows — avoids a single frame of default-theme flash.</summary>
    public static void LoadAndApply()
    {
        var saved = TryReadSavedTheme() ?? "Default";
        Apply(saved, persist: false);
    }

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
        if (persist) TrySaveTheme(themeId);
        ThemeChanged?.Invoke(themeId);
    }

    private record SavedSettings(string theme, int version);

    private static string? TryReadSavedTheme()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return null;
            var json = File.ReadAllText(SettingsFile);
            var parsed = JsonSerializer.Deserialize<SavedSettings>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return parsed?.theme is string t && Available.Contains(t) ? t : null;
        }
        catch
        {
            return null; // corrupt file = fall back to Default silently
        }
    }

    private static void TrySaveTheme(string themeId)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(new SavedSettings(themeId, 1));
            File.WriteAllText(SettingsFile, json);
        }
        catch
        {
            // Persistence is best-effort; if it fails (locked file, disk full) just lose the
            // user's choice on next launch. Not worth blocking the UI swap.
        }
    }
}
