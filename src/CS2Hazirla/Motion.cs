using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CS2Hazirla;

/// <summary>
/// The few moving things that XAML cannot do on its own, and the one gate that decides whether anything moves at all.
/// Rules: only Opacity and render transforms move (no layout, no effects); entrances run once; the only repeating animation is
/// <see cref="Breathing"/> (one small element, 20 fps, about 40 seconds, and only while the window is in front); every animation is
/// removed when it ends so no clock stays alive. Nothing here adds an asset: it is all brushes and transforms.
/// </summary>
public static class Motion
{
    /// <summary>DEBUG screenshot renders set this: the picture must show the finished state, not a frame of a transition.</summary>
    public static bool ForceStill { get; set; }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private const int SM_REMOTESESSION = 0x1000;

    /// <summary>
    /// Can this computer animate at all: Windows animations are on, the GPU renders (tier 2), and it is not a Remote Desktop session
    /// (frames would be sent over the link). Shared by everything that moves, including the dial.
    /// </summary>
    public static bool Available
    {
        get
        {
            if (!SystemParameters.ClientAreaAnimation) return false;
            try { return (RenderCapability.Tier >> 16) >= 2 && GetSystemMetrics(SM_REMOTESESSION) == 0; }
            catch { return false; }
        }
    }

    /// <summary><see cref="Available"/>, and this is not a still render. Re-read on every use (cheap).</summary>
    public static bool Allowed => !ForceStill && Available;

    // cubic-bezier(0.23, 1, 0.32, 1): fast start, long soft landing (entrances)
    private static readonly KeySpline OutSpline = MakeSpline();

    private static KeySpline MakeSpline()
    {
        var k = new KeySpline(0.23, 1, 0.32, 1);
        k.Freeze();
        return k;
    }

    private static DoubleAnimationUsingKeyFrames Spline(double from, double to, int ms, int delayMs)
    {
        var a = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(delayMs), FillBehavior = FillBehavior.HoldEnd };
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), OutSpline));
        return a;
    }

    /// <summary>
    /// Entrance: the element fades in and rises a few pixels. The start state is set at once (so nothing flashes before the delay ends);
    /// when the animation is over the final values are written and the animation is removed, so no clock stays alive.
    /// </summary>
    public static void Reveal(UIElement element, int delayMs = 0, double fromY = 10, int ms = 320)
    {
        if (!Allowed) { element.Opacity = 1; return; }

        TranslateTransform? move = null;
        if (fromY != 0 && (element.RenderTransform == null || element.RenderTransform == Transform.Identity))
        {
            move = new TranslateTransform(0, fromY);
            element.RenderTransform = move;
        }
        element.Opacity = 0;

        var fade = Spline(0, 1, ms, delayMs);
        fade.Completed += (_, _) =>
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            if (move != null) { move.BeginAnimation(TranslateTransform.YProperty, null); element.RenderTransform = Transform.Identity; }
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        move?.BeginAnimation(TranslateTransform.YProperty, Spline(fromY, 0, ms, delayMs));
    }
}

/// <summary>
/// The halo behind the big button breathes slowly while the app is ready and in front. One element, two properties, 20 frames a second,
/// about 40 seconds: then it settles at a calm glow, so a window left open in front for hours does not keep waking the computer.
/// It breathes again the next time it has to start (the window is activated, the home page opens, a run ends...).
/// <see cref="Update"/> is called whenever something that matters changes; it starts or stops the loop, never both at once.
/// </summary>
public sealed class Breathing
{
    private readonly UIElement _halo;
    private readonly ScaleTransform _scale;
    private bool? _state;       // null: nothing decided yet; true: the loop is alive; false: at rest

    /// <summary>true while the loop is alive (a test hook; nothing in the program depends on it).</summary>
    public bool IsRunning => _state == true;

    public Breathing(UIElement halo, ScaleTransform scale)
    {
        _halo = halo;
        _scale = scale;
    }

    public void Update(bool shouldRun)
    {
        shouldRun = shouldRun && Motion.Allowed;
        if (_state == shouldRun) return;
        _state = shouldRun;

        // the calm glow is the base value: it is what the halo shows when no animation is applied, and when the loop has run its course
        _halo.Opacity = 0.8;
        _scale.ScaleX = 1; _scale.ScaleY = 1;

        if (shouldRun)
        {
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            var cycles = new RepeatBehavior(8);                    // 8 x (breathe in + out) of 5.2 s
            var glow = new DoubleAnimation(0.55, 1.0, TimeSpan.FromMilliseconds(2600)) { AutoReverse = true, RepeatBehavior = cycles, EasingFunction = ease };
            var grow = new DoubleAnimation(1.0, 1.045, TimeSpan.FromMilliseconds(2600)) { AutoReverse = true, RepeatBehavior = cycles, EasingFunction = ease };
            Timeline.SetDesiredFrameRate(glow, 20);
            Timeline.SetDesiredFrameRate(grow, 20);
            glow.Completed += (_, _) => { if (_state == true) _state = false; };    // it ran its course: at rest until the next Update(true)
            _halo.BeginAnimation(UIElement.OpacityProperty, glow);
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }
        else
        {
            // no clock may stay behind
            _halo.BeginAnimation(UIElement.OpacityProperty, null);
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }
    }
}
