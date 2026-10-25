using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CS2Hazirla;

/// <summary>
/// Antilag's one memorable element: a dial of 64 ticks (CS2's tick rate). At rest the ticks are dim, every eighth one is longer.
/// While steps finish the ticks light up in order (<see cref="Progress"/> = finished steps / planned steps: a real number, never an estimate).
/// When the amount of work is not known (restore) <see cref="Sweeping"/> moves a short lit arc around the dial.
/// It only draws; it animates only while something runs (and not at all when Windows has animations switched off).
/// </summary>
public sealed class TickRing : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(TickRing), new PropertyMetadata(0.0, OnProgressChanged));

    public static readonly DependencyProperty SweepingProperty = DependencyProperty.Register(
        nameof(Sweeping), typeof(bool), typeof(TickRing), new PropertyMetadata(false, OnSweepingChanged));

    public static readonly DependencyProperty TickCountProperty = DependencyProperty.Register(
        nameof(TickCount), typeof(int), typeof(TickRing), new FrameworkPropertyMetadata(64, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LitBrushProperty = DependencyProperty.Register(
        nameof(LitBrush), typeof(Brush), typeof(TickRing), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DimBrushProperty = DependencyProperty.Register(
        nameof(DimBrush), typeof(Brush), typeof(TickRing), new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MajorBrushProperty = DependencyProperty.Register(
        nameof(MajorBrush), typeof(Brush), typeof(TickRing), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TickLengthProperty = DependencyProperty.Register(
        nameof(TickLength), typeof(double), typeof(TickRing), new FrameworkPropertyMetadata(0.1, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TickThicknessProperty = DependencyProperty.Register(
        nameof(TickThickness), typeof(double), typeof(TickRing), new FrameworkPropertyMetadata(0.021, FrameworkPropertyMetadataOptions.AffectsRender));

    // what is drawn: the animated copy of Progress, and the position of the sweeping arc (in ticks)
    private static readonly DependencyProperty ShownProperty = DependencyProperty.Register(
        nameof(Shown), typeof(double), typeof(TickRing), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty HeadProperty = DependencyProperty.Register(
        nameof(Head), typeof(double), typeof(TickRing), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>0 … 1: the share of the planned steps that are finished.</summary>
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public bool Sweeping { get => (bool)GetValue(SweepingProperty); set => SetValue(SweepingProperty, value); }
    public int TickCount { get => (int)GetValue(TickCountProperty); set => SetValue(TickCountProperty, value); }
    public Brush LitBrush { get => (Brush)GetValue(LitBrushProperty); set => SetValue(LitBrushProperty, value); }
    public Brush DimBrush { get => (Brush)GetValue(DimBrushProperty); set => SetValue(DimBrushProperty, value); }
    /// <summary>Colour of the longer ticks that are not lit (every eighth). Null: they look like the others.</summary>
    public Brush? MajorBrush { get => (Brush?)GetValue(MajorBrushProperty); set => SetValue(MajorBrushProperty, value); }
    /// <summary>Tick length as a share of the radius.</summary>
    public double TickLength { get => (double)GetValue(TickLengthProperty); set => SetValue(TickLengthProperty, value); }
    /// <summary>Tick thickness as a share of the radius.</summary>
    public double TickThickness { get => (double)GetValue(TickThicknessProperty); set => SetValue(TickThicknessProperty, value); }

    private double Shown { get => (double)GetValue(ShownProperty); set => SetValue(ShownProperty, value); }
    private double Head { get => (double)GetValue(HeadProperty); set => SetValue(HeadProperty, value); }

    // the same gate as every other animation (Windows animations on, GPU rendering, not a Remote Desktop session); a still render does not turn it off
    private static bool Animated => Motion.Available;

    private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ring = (TickRing)d;
        var to = Math.Clamp((double)e.NewValue, 0, 1);
        if (!Animated || !ring.IsLoaded || to < ring.Shown)
        {
            // a new run starts from nothing, and "no animations" means none: jump
            ring.BeginAnimation(ShownProperty, null);
            ring.Shown = to;
            return;
        }
        ring.BeginAnimation(ShownProperty, new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(167)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private static void OnSweepingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ring = (TickRing)d;
        if ((bool)e.NewValue && Animated)
        {
            var sweep = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromSeconds(1.6)))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            Timeline.SetDesiredFrameRate(sweep, 30);        // a travelling arc needs no more than 30 frames a second
            ring.BeginAnimation(HeadProperty, sweep);
        }
        else
        {
            ring.BeginAnimation(HeadProperty, null);
            ring.Head = 0;
        }
        ring.InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        var n = Math.Max(8, TickCount);
        var radius = Math.Min(w, h) / 2;
        if (radius < 4) return;

        var center = new Point(w / 2, h / 2);
        var length = Math.Max(2, radius * TickLength);
        var thickness = Math.Max(1, radius * TickThickness);
        var majorEvery = Math.Max(1, n / 8);

        var lit = MakePen(LitBrush, thickness);
        var dim = MakePen(DimBrush, thickness);
        var major = MakePen(MajorBrush ?? DimBrush, thickness);
        // the lit ticks shade from a deeper tone at the start to white at the head (the lit colour is a plain colour; anything else stays flat)
        var litColor = (LitBrush as SolidColorBrush)?.Color;
        var glow = litColor is { } gc ? MakePen(FrozenBrush(gc), thickness * 3.4) : null;
        var tonePens = new Pen?[10];

        var sweeping = Sweeping;
        // the arc that travels while the amount of work is unknown: its head position, and how many ticks trail behind it
        var head = sweeping && Animated ? Head * n : -1;
        var tail = Math.Max(4, n / 5);
        var litCount = (int)Math.Round(Math.Clamp(Shown, 0, 1) * n);
        if (sweeping && !Animated) litCount = n / 4;      // without animations: a still quarter of the dial

        for (var i = 0; i < n; i++)
        {
            var angle = (-90.0 + i * 360.0 / n) * Math.PI / 180.0;
            var isMajor = i % majorEvery == 0;
            var isHead = head < 0 && litCount > 0 && i == litCount - 1 && litCount < n;      // the newest lit tick: longer and white
            var len = isMajor ? length * 1.45 : length;
            if (isHead) len = Math.Max(len, length * 1.35);
            var dir = new Vector(Math.Cos(angle), Math.Sin(angle));
            var outer = center + dir * (radius - thickness);
            var inner = center + dir * (radius - thickness - len);

            Pen pen;
            var alpha = 1.0;
            var isLit = false;
            if (head >= 0)
            {
                var behind = (head - i + n) % n;           // 0 = at the head
                if (behind <= tail) { pen = lit; alpha = 1.0 - behind / (double)(tail + 1) * 0.85; isLit = true; }
                else pen = isMajor ? major : dim;
            }
            else if (i < litCount) { pen = lit; isLit = true; }
            else pen = isMajor ? major : dim;

            if (isLit && litColor is { } c)
            {
                // soft light under the lit tick (a wider, faint line) and a tone that brightens towards the head;
                // the tone is rounded to 9 steps (+ white for the head), so one frame needs at most 10 pens, not one per tick
                var t = head >= 0 ? 1.0 : litCount <= 1 ? 1.0 : i / (double)(litCount - 1);
                var step = isHead || (head >= 0 && alpha > 0.95) ? 9 : (int)Math.Round(Math.Min(1, t * 1.4) * 8);
                pen = tonePens[step] ??= MakePen(FrozenBrush(step == 9 ? Blend(c, Colors.White, 0.7) : Blend(Darken(c, 0.78), c, step / 8.0)), thickness);
                if (glow != null)
                {
                    dc.PushOpacity(0.14 * alpha);
                    dc.DrawLine(glow, inner, outer);
                    dc.Pop();
                }
            }

            if (alpha < 1) dc.PushOpacity(alpha);
            dc.DrawLine(pen, inner, outer);
            if (alpha < 1) dc.Pop();
        }
    }

    private static SolidColorBrush FrozenBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Color Blend(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }

    private static Color Darken(Color c, double keep) =>
        Color.FromRgb((byte)(c.R * keep), (byte)(c.G * keep), (byte)(c.B * keep));

    private static Pen MakePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }
}
