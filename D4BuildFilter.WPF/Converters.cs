using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace D4BuildFilter.WPF;

/// <summary>Visible when the bound string is non-empty (e.g. a loading/error note), else collapsed.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Options-panel color swatch: when the bound toggle is ON, paint it the rule's real
/// in-game highlight color (passed as the hex ConverterParameter, e.g. "#FFD700"); when OFF,
/// dim it to a muted gray so the marker reads as "this color won't be produced".</summary>
public sealed class ActiveColorConverter : IValueConverter
{
    private static readonly SolidColorBrush Off = new(Color.FromRgb(0x4a, 0x44, 0x3d));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool on = value is true;
        if (!on || parameter is not string hex) return Off;
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return Off; }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>★ when the build is favorited, ☆ when not — used as the chip's star indicator glyph.</summary>
public sealed class FavoriteStarConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "★" : "☆";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Star glyph color: gold when favorited, dim cream when not (still visible enough to
/// invite a click, but quiet enough that an un-starred chip doesn't shout for attention).</summary>
public sealed class FavoriteStarBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush On = new(Color.FromRgb(0xFF, 0xD7, 0x00)); // classic D4 gold
    private static readonly SolidColorBrush Off = new(Color.FromRgb(0x6b, 0x62, 0x5a));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? On : Off;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Two-way string equality for radio-button groups bound to a string source-of-truth.
/// Convert: value.ToString() == parameter.ToString() → bool. ConvertBack: if bool is true, returns
/// parameter (selects this radio's value); if false, Binding.DoNothing (leaves source unchanged
/// when a different radio in the group takes ownership). Used by the theme picker popup.</summary>
public sealed class StringEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? parameter : Binding.DoNothing;
}

/// <summary>Brand color for the result-page source-origin pill. Maps the build's source string
/// (set in MainViewModel.SetCurrentSource) to a muted brand-adjacent brush. Stays quiet on the
/// page (not a callout) but distinguishable at a glance — Maxroll teal, D4Builds rust,
/// Mobalytics violet, Community neutral gray, fallback dim warm.</summary>
public sealed class SourceToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Maxroll    = new(Color.FromRgb(0x2e, 0x7c, 0x7e)); // muted teal
    private static readonly SolidColorBrush D4Builds   = new(Color.FromRgb(0xa8, 0x5d, 0x2c)); // amber rust
    private static readonly SolidColorBrush Mobalytics = new(Color.FromRgb(0x6a, 0x4a, 0x99)); // violet
    private static readonly SolidColorBrush Community  = new(Color.FromRgb(0x5a, 0x4f, 0x46)); // neutral gray
    private static readonly SolidColorBrush Fallback   = new(Color.FromRgb(0x3a, 0x33, 0x2b));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value as string)?.ToLowerInvariant() switch
        {
            "maxroll"    => Maxroll,
            "d4builds"   => D4Builds,
            "mobalytics" => Mobalytics,
            "community"  => Community,
            _            => Fallback,
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Multi-binding visibility: visible only when EVERY bound boolean is true. Used to gate
/// the "Not yet filterable" diagnostic on (ShowPendingAffixes AND HasDropped) — both must be true
/// or we collapse. BooleanToVisibilityConverter only handles single inputs.</summary>
public sealed class AllTrueToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.All(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => Array.Empty<object>();
}

/// <summary>Options-panel label text: bright when the toggle is ON, grayed when OFF — so the
/// whole row (swatch + words) visibly goes inactive together. Reads Text.Primary / Text.Muted
/// from the active theme dictionary at convert time so theme swaps re-paint these labels too
/// (previously cached static brushes at init time → frozen on the first-loaded theme).</summary>
public sealed class ActiveTextConverter : IValueConverter
{
    // Fallback brushes used if Application.Current isn't ready (designer / unit-test contexts).
    private static readonly SolidColorBrush FallbackOn  = new(Color.FromRgb(0xe3, 0xd8, 0xcc));
    private static readonly SolidColorBrush FallbackOff = new(Color.FromRgb(0x6b, 0x62, 0x5a));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is true ? "Text.Primary" : "Text.Muted";
        return Application.Current?.TryFindResource(key) as Brush
               ?? (value is true ? FallbackOn : FallbackOff);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
