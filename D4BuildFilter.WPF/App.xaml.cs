using System.Windows;
using D4BuildFilter.WPF.Services;

namespace D4BuildFilter.WPF;

/// <summary>Interaction logic for App.xaml</summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Load the persisted theme BEFORE the main window shows. Synchronous read of a tiny
        // file — no perceptible startup cost, and it avoids a single frame of default-theme
        // flash if the user is on Discord/Dark.
        ThemeManager.LoadAndApply();
        base.OnStartup(e);
    }
}
