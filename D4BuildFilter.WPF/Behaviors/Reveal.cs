using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace D4BuildFilter.WPF.Behaviors;

/// <summary>
/// Attached behavior that gives a collapsible content element a smooth HEIGHT + OPACITY
/// expand/collapse driven by a single bool (<see cref="IsOpenProperty"/>). The existing *Expanded
/// VM bools stay the source of truth — the section header ToggleButton keeps writing them, and this
/// just binds to the same bool. Reusable across all four sections; no NuGet, no per-section
/// Storyboards, no code-behind wiring.
///
/// WHY HEIGHT (not ScaleY): we animate the element's OWN <see cref="FrameworkElement.Height"/> from
/// 0 to its measured natural height. A height reveal moves the bottom edge of the panel; the panel
/// clips its overflow (we set <c>ClipToBounds="True"</c> on the target in XAML) so the child content
/// is uncovered top-down at full size — text is never squished or scaled. Because a StackPanel's
/// rendered height equals its animated Height, everything BELOW it reflows smoothly and never overlaps.
///
/// THE ANIMATE-TO-AUTO PROBLEM: you can't animate to "Auto" (Height=NaN). So on OPEN we measure the
/// natural pixel height, animate 0 → that height, and on completion CLEAR the local Height value so
/// it returns to Auto. From then on the section resizes freely with live content (the summary label
/// updating, a list repopulating, a window resize re-wrapping the checkbox WrapPanel).
///
/// NO FLASH ON FIRST LOAD: <see cref="IsOpenProperty"/> is a <c>bool?</c> defaulting to null, so the
/// FIRST bound value (true OR false) always registers as a change (null → value) and PRIMES the
/// element to its settled state with NO animation — even for sections that start open (where a plain
/// bool default of true would swallow the initial change and leave the first collapse un-animated).
/// A section that starts collapsed appears already-closed on first paint; one that starts open
/// appears already-open. Only genuine user toggles after priming animate.
///
/// RE-ENTRANCY / FAST TOGGLING: every animation first stops the previous one
/// (<c>BeginAnimation(HeightProperty, null)</c>) and reads the live <see cref="FrameworkElement.
/// ActualHeight"/> as its start value, so a toggle mid-flight continues from wherever it is — no
/// snap, no stacking. Completion handlers re-check the live state so a close that lands after a newer
/// open (or vice-versa) doesn't stomp the newer state.
/// </summary>
public static class Reveal
{
    // ── Public knobs ──

    /// <summary>The state the section binds to, e.g. <c>b:Reveal.IsOpen="{Binding FilterOptionsExpanded}"</c>.
    /// bool? (null default) so the first assignment always primes-then-snaps; later flips animate.</summary>
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.RegisterAttached(
            "IsOpen", typeof(bool?), typeof(Reveal),
            new PropertyMetadata(null, OnIsOpenChanged));

    public static void SetIsOpen(DependencyObject o, bool? v) => o.SetValue(IsOpenProperty, v);
    public static bool? GetIsOpen(DependencyObject o) => (bool?)o.GetValue(IsOpenProperty);

    /// <summary>Animation length. Defaults to 180ms — inside the 160–200ms polished band.</summary>
    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.RegisterAttached(
            "Duration", typeof(Duration), typeof(Reveal),
            new PropertyMetadata(new Duration(TimeSpan.FromMilliseconds(180))));

    public static void SetDuration(DependencyObject o, Duration v) => o.SetValue(DurationProperty, v);
    public static Duration GetDuration(DependencyObject o) => (Duration)o.GetValue(DurationProperty);

    // ── Private bookkeeping ──

    /// <summary>False until the first state has been applied (snapped). Guards no-animate-on-first-paint.</summary>
    private static readonly DependencyProperty PrimedProperty =
        DependencyProperty.RegisterAttached(
            "Primed", typeof(bool), typeof(Reveal), new PropertyMetadata(false));

    private static bool IsOpenNow(FrameworkElement fe) => GetIsOpen(fe) == true;

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        if (e.NewValue is not bool open) return;   // null → nothing to do (pre-bind state)

        // FIRST application → snap to state, no animation. Defer to Loaded if the element isn't in
        // the tree yet (so its width is known for a correct measure and the tree is settled).
        if (!(bool)fe.GetValue(PrimedProperty))
        {
            fe.SetValue(PrimedProperty, true);
            if (fe.IsLoaded)
            {
                Snap(fe, open);
            }
            else
            {
                void OnLoaded(object? s, RoutedEventArgs a)
                {
                    fe.Loaded -= OnLoaded;
                    Snap(fe, IsOpenNow(fe)); // re-read in case it changed before load
                }
                fe.Loaded += OnLoaded;
            }
            return;
        }

        if (open) Open(fe);
        else Close(fe);
    }

    /// <summary>Snap to a state with no animation (initial load / not-yet-primed).</summary>
    private static void Snap(FrameworkElement fe, bool open)
    {
        fe.BeginAnimation(FrameworkElement.HeightProperty, null); // release any hold
        fe.BeginAnimation(UIElement.OpacityProperty, null);

        if (open)
        {
            fe.ClearValue(FrameworkElement.HeightProperty); // Auto — sizes to content
            fe.Opacity = 1;
            fe.Visibility = Visibility.Visible;
        }
        else
        {
            fe.Visibility = Visibility.Collapsed; // fully out of layout — reserves no space
            fe.Opacity = 0;
            fe.Height = 0;
        }
    }

    private static void Open(FrameworkElement fe)
    {
        var dur = GetDuration(fe);

        fe.Visibility = Visibility.Visible;
        fe.BeginAnimation(FrameworkElement.HeightProperty, null); // stop any in-flight anim FIRST
        double from = fe.ActualHeight;                            // live start (0 if was collapsed, or partial on interrupt)
        double target = MeasureNaturalHeight(fe);                 // natural height at the REAL layout width
        fe.Height = from;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var heightAnim = new DoubleAnimation
        {
            From = from,
            To = target,
            Duration = dur,
            EasingFunction = ease,
        };
        // ANIMATE-TO-AUTO FIX: once open completes, drop the fixed Height so the section resizes
        // freely with live content. Guard against a fast open→close having superseded us.
        heightAnim.Completed += (_, _) =>
        {
            if (IsOpenNow(fe))
            {
                fe.BeginAnimation(FrameworkElement.HeightProperty, null);
                fe.ClearValue(FrameworkElement.HeightProperty); // back to Auto
            }
        };

        var opacityAnim = new DoubleAnimation
        {
            From = fe.Opacity,
            To = 1,
            Duration = dur,
            EasingFunction = ease,
        };

        fe.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
        fe.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
    }

    private static void Close(FrameworkElement fe)
    {
        var dur = GetDuration(fe);

        fe.BeginAnimation(FrameworkElement.HeightProperty, null); // stop any in-flight anim (e.g. open)
        double from = fe.ActualHeight;                            // live start
        fe.Height = from;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var heightAnim = new DoubleAnimation
        {
            From = from,
            To = 0,
            Duration = dur,
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd, // hold at 0 until we collapse Visibility
        };
        heightAnim.Completed += (_, _) =>
        {
            if (!IsOpenNow(fe))
                fe.Visibility = Visibility.Collapsed; // leave layout so nothing reserves the space
        };

        var opacityAnim = new DoubleAnimation
        {
            From = fe.Opacity,
            To = 0,
            Duration = dur,
            EasingFunction = ease,
        };

        fe.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
        fe.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
    }

    /// <summary>Measure the element's natural height at the width it will really occupy, so the
    /// checkbox WrapPanels report their true wrapped height and the MaxHeight=220 ScrollViewers
    /// report min(content, 220). A just-shown (previously Collapsed) element has ActualWidth 0 and is
    /// not yet arranged, so fall back to the arranged parent's width — otherwise a WrapPanel would
    /// measure as a single row and the reveal would clip the content short.</summary>
    private static double MeasureNaturalHeight(FrameworkElement fe)
    {
        double width = fe.ActualWidth;
        if (width <= 0 && VisualTreeHelper.GetParent(fe) is FrameworkElement vp && vp.ActualWidth > 0)
            width = vp.ActualWidth;
        if (width <= 0 && fe.Parent is FrameworkElement lp && lp.ActualWidth > 0)
            width = lp.ActualWidth;
        if (width <= 0) width = double.PositiveInfinity;

        fe.ClearValue(FrameworkElement.HeightProperty);
        fe.Measure(new Size(width, double.PositiveInfinity));
        double h = fe.DesiredSize.Height; // includes the element's own Margin; honors MaxHeight
        return h > 0 ? h : 0;
    }
}
