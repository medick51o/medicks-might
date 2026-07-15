using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using D4BuildFilter.WPF.ViewModels;

namespace D4BuildFilter.WPF.Services;

public static class WitnessCardRenderer
{
    public const int PixelWidth = 1200;
    public const int PixelHeight = 675;

    public static BitmapSource Render(WitnessCardViewModel card)
    {
        var control = new WitnessCardControl { DataContext = card };
        control.Measure(new Size(PixelWidth, PixelHeight));
        control.Arrange(new Rect(0, 0, PixelWidth, PixelHeight));
        control.UpdateLayout();
        var bitmap = new RenderTargetBitmap(PixelWidth, PixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(control);
        bitmap.Freeze();
        return bitmap;
    }

    public static void SavePng(BitmapSource bitmap, string path)
    {
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }
}

/// <summary>Builds the dual-purpose clipboard payload once: Discord sees the bitmap/PNG while a
/// text-aware paste target receives the exact same import code shown on the card.</summary>
public static class WitnessCardClipboard
{
    public const string PngFormat = "PNG";

    public static DataObject Create(BitmapSource bitmap, string importCode)
    {
        var png = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(png);
        png.Position = 0;

        var data = new DataObject();
        data.SetData(DataFormats.Bitmap, bitmap);
        data.SetData(PngFormat, png);
        data.SetData(DataFormats.UnicodeText, importCode);
        data.SetData(DataFormats.Text, importCode);
        return data;
    }
}
