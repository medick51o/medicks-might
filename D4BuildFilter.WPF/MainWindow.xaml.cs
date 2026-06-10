using System.Windows;
using System.Windows.Controls;

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
}
