using System;
using System.IO;
using System.Text.Json;

namespace D4BuildFilter.WPF.Services;

/// <summary>
/// Small cross-launch UI state: which of the two big collapsible unique lists the user left open,
/// and the last build they loaded (so those lists reopen only on a DIFFERENT build, not when the
/// same build reloads). Mirrors <see cref="ThemeManager"/>'s persistence
/// (<c>%LOCALAPPDATA%\MedicKsMight\ui-state.json</c>).
///
/// "Filter options" and "Charm sets" are deliberately NOT stored here — they always reopen on app
/// launch so nobody collapses the core toggles and forgets them.
///
/// Best-effort: any read/write failure falls back to defaults (everything expanded). A corrupt or
/// missing prefs file must never block the app.
/// </summary>
public sealed class UiState
{
    public bool UniquesExpanded { get; set; } = true;
    public bool UniqueCharmsExpanded { get; set; } = true;
    public string LastBuildKey { get; set; } = "";
    public int Version { get; set; } = 1;
}

public static class UiStateStore
{
    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MedicKsMight");
    private static string FilePath => Path.Combine(Dir, "ui-state.json");

    public static UiState Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new UiState();
            return JsonSerializer.Deserialize<UiState>(File.ReadAllText(FilePath)) ?? new UiState();
        }
        catch { return new UiState(); }
    }

    public static void Save(UiState state)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(state));
        }
        catch { /* best-effort — UI prefs aren't worth surfacing an error over */ }
    }
}
