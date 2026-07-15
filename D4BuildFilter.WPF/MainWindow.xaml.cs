using System.Windows;
using System.Windows.Controls;
using D4BuildFilter.Core;
using D4BuildFilter.WPF.Services;
using D4BuildFilter.WPF.ViewModels;
using Microsoft.Win32;

namespace D4BuildFilter.WPF;

/// <summary>Interaction logic for MainWindow.xaml</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Pin the landing column to the ScrollViewer's viewport width. A Disabled-horizontal
        // ScrollViewer still sizes its content to the widest child (the tier WrapPanels), which
        // throws off centering and clips a column; forcing a finite width makes them wrap and center.
        InputScroll.SizeChanged += (_, _) => SyncInputColumnWidth();
        InputScroll.ScrollChanged += (_, _) => SyncInputColumnWidth();
    }

    private void SyncInputColumnWidth()
    {
        var w = InputScroll.ViewportWidth;
        if (w > 0 && InputColumn.Width != w) InputColumn.Width = w;
    }

    private void FilterTitle_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.CommitFilterTitle();
    }

    private void ShareWitnessCard_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CurrentWitnessCard: { } card } vm) return;
        try
        {
            var bitmap = WitnessCardRenderer.Render(card);
            Clipboard.SetDataObject(WitnessCardClipboard.Create(bitmap, card.ImportCode), true);
            vm.ReportWitnessCardSuccess("✓ Share card copied with its import code.");
        }
        catch (Exception ex)
        {
            AppLog.Write("witness-card", $"share card copy failed: {ex.ToString()}");
            vm.ReportWitnessCardFailure("Couldn't copy the share card — try again.");
        }
    }

    private void SaveWitnessCard_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CurrentWitnessCard: { } card } vm) return;
        var dialog = new SaveFileDialog
        {
            Title = "Save Witness Card as PNG",
            Filter = "PNG image (*.png)|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            FileName = "Medicks-Might-Witness-Card.png",
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            WitnessCardRenderer.SavePng(WitnessCardRenderer.Render(card), dialog.FileName);
            vm.ReportWitnessCardSuccess("✓ Share card saved as PNG.");
        }
        catch (Exception ex)
        {
            // This try block covers both rendering the card and writing the PNG — a "try a
            // different location" message would be wrong if rendering itself failed.
            AppLog.Write("witness-card", $"share card save failed: {ex.ToString()}");
            vm.ReportWitnessCardFailure("Couldn't create or save the share-card image.");
        }
    }
}
