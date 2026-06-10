namespace D4BuildFilter.Core;

/// <summary>Dead-simple daily-rolling diagnostics log under %LOCALAPPDATA%\MedicKsMight\logs.
/// The app ships without a console; when a scraper breaks on season day this file is the only
/// triage signal a user can send us. Never throws — logging must never take the app down.</summary>
public static class AppLog
{
    private const int KeepDays = 7;
    private static readonly object Gate = new();

    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MedicKsMight", "logs");

    public static void Write(string area, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                var now = DateTime.UtcNow;
                File.AppendAllText(
                    Path.Combine(Dir, $"app-{now:yyyyMMdd}.log"),
                    $"{now:HH:mm:ss}Z [{area}] {message}{Environment.NewLine}");
                Prune();
            }
        }
        catch
        {
            // Disk full / AV lock / permissions — diagnostics are best-effort only.
        }
    }

    private static void Prune()
    {
        var files = Directory.GetFiles(Dir, "app-*.log");
        if (files.Length <= KeepDays) return;
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);   // name order == date order
        for (var i = 0; i < files.Length - KeepDays; i++)
            File.Delete(files[i]);
    }
}
