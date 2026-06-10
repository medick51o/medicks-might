using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

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

    /// <summary>Artwork tab: size the art to COVER the viewport (the same dramatic crop as
    /// Stretch=UniformToFill) while keeping the cropped overflow reachable by scrolling. One axis
    /// fits the viewport exactly (minus the other axis's scrollbar lane so only ONE bar shows);
    /// the other overflows and starts centered, so the old hard-crop view is the starting frame
    /// and the edges — the cool sword — are a scroll away.</summary>
    private void ArtworkScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ArtworkImage.Source is not BitmapSource art || art.PixelWidth == 0) return;
        var vw = e.NewSize.Width;
        var vh = e.NewSize.Height;
        if (vw <= 0 || vh <= 0) return;
        var aspect = (double)art.PixelWidth / art.PixelHeight;

        double w, h;
        if (aspect > vw / vh)
        {
            // Art is wider than the viewport: fit height, overflow (and pan) sideways.
            h = Math.Max(1, vh - SystemParameters.HorizontalScrollBarHeight);
            w = Math.Max(vw, h * aspect);
        }
        else
        {
            // Art is taller than the viewport: fit width, overflow (and wheel) downward.
            w = Math.Max(1, vw - SystemParameters.VerticalScrollBarWidth);
            h = Math.Max(vh, w / aspect);
        }
        ArtworkImage.Width = w;
        ArtworkImage.Height = h;

        // Center after this layout pass settles (extent/viewport aren't updated yet here).
        Dispatcher.InvokeAsync(() =>
        {
            ArtworkScroll.ScrollToHorizontalOffset((ArtworkScroll.ExtentWidth - ArtworkScroll.ViewportWidth) / 2);
            ArtworkScroll.ScrollToVerticalOffset((ArtworkScroll.ExtentHeight - ArtworkScroll.ViewportHeight) / 2);
        }, DispatcherPriority.Background);
    }

    /// <summary>When the artwork's overflow is horizontal there is nothing vertical to scroll —
    /// let the mouse wheel pan sideways instead of dying on a maxed-out vertical axis.</summary>
    private void ArtworkScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ArtworkScroll.ScrollableWidth > 0 && ArtworkScroll.ScrollableHeight == 0)
        {
            ArtworkScroll.ScrollToHorizontalOffset(ArtworkScroll.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }
}
