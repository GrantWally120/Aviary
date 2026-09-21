using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Aviary;

/// <summary>
/// How Aviary moves (Alpha Build 5 M4.5 "Smooth Flight", 19 Sep 2026). Each look has its own
/// way of moving, the way it has its own colours — Grant's naming:
/// <list type="bullet">
/// <item><b>Smooth Flight</b> — Classic's. Crisp, quick and calm, like Safari 15–18 on the Mac:
/// ease-out curves, 120–240 ms, no overshoot; things slide and fade in straight lines.</item>
/// <item><b>Fluid</b> — Liquid Glass's. Springy and gel-like, like Safari 26: real spring
/// physics with a slight overshoot, shapes that stretch toward where they are going.</item>
/// </list>
/// Settings → Appearance → Animation style picks "Match the look" (the default), either one for
/// both looks; Accessibility → Reduce motion turns every animation here off.
///
/// Everything runs as a Composition animation — on the compositor's thread, not the UI thread —
/// so a busy page or a slow handler cannot make it stutter, and it cannot make the UI stall
/// (the diagnostics watchdog's number to watch).
/// </summary>
internal static class Motion
{
    public enum Style
    {
        Off,
        SmoothFlight,
        Fluid,
    }

    public const string SettingKey = "animationStyle";
    public const string SpeedKey = "animationSpeed";
    public const string MatchTheLook = "Match the look";

    /// <summary>
    /// How fast the whole browser moves (M4.6): Calm stretches every duration by half, Quick cuts
    /// it by a third. One multiplier, read where the durations are made, so it reaches every
    /// animation in the window AND - through <see cref="PageSpeed"/> - Settings and the Start Page.
    /// Springs are not scaled by it: a spring's feel is its damping and period, not its length.
    /// </summary>
    public static double Speed => AppSettings.Get(SpeedKey) switch
    {
        "Calm" => 1.5,
        "Quick" => 0.68,
        _ => 1.0,
    };

    /// <summary>"calm", "standard" or "quick" - what Aviary's own pages key their durations off.</summary>
    public static string PageSpeed => AppSettings.Get(SpeedKey) switch
    {
        "Calm" => "calm",
        "Quick" => "quick",
        _ => "standard",
    };

    /// <summary>The style in force now, from the three settings that decide it.</summary>
    public static Style Current
    {
        get
        {
            if (AppSettings.GetBool("reduceMotion"))
            {
                return Style.Off;
            }

            string chosen = AppSettings.Get(SettingKey);
            if (chosen.Equals("Smooth Flight", StringComparison.OrdinalIgnoreCase))
            {
                return Style.SmoothFlight;
            }

            if (chosen.Equals("Fluid", StringComparison.OrdinalIgnoreCase))
            {
                return Style.Fluid;
            }

            return AppSettings.Get("look").Equals("Liquid Glass", StringComparison.OrdinalIgnoreCase)
                ? Style.Fluid
                : Style.SmoothFlight;
        }
    }

    /// <summary>For Aviary's own pages: "flight", "fluid" or "off" — their CSS keys off it.</summary>
    public static string PageToken => Current switch
    {
        Style.Fluid => "fluid",
        Style.SmoothFlight => "flight",
        _ => "off",
    };

    public static bool On => Current != Style.Off;

    // ── The shared timing ──────────────────────────────────────────────────────
    // The Settings page's own curve, cubic-bezier(.32,.72,0,1): a fast start that settles.

    public static TimeSpan Quick => TimeSpan.FromMilliseconds(120 * Speed);
    public static TimeSpan Standard => TimeSpan.FromMilliseconds(200 * Speed);
    public static TimeSpan Slow => TimeSpan.FromMilliseconds(320 * Speed);

    public static CompositionEasingFunction Ease(Compositor c) =>
        c.CreateCubicBezierEasingFunction(new Vector2(0.32f, 0.72f), new Vector2(0f, 1f));

    public static Visual VisualOf(UIElement element) => ElementCompositionPreview.GetElementVisual(element);

    /// <summary>A spring for positions and sizes: settles in about a quarter second, overshoots a touch.</summary>
    public static SpringVector3NaturalMotionAnimation Spring3(Compositor c, float damping = 0.72f, int periodMs = 50)
    {
        SpringVector3NaturalMotionAnimation s = c.CreateSpringVector3Animation();
        s.DampingRatio = damping;
        s.Period = TimeSpan.FromMilliseconds(periodMs);
        return s;
    }

    public static SpringScalarNaturalMotionAnimation Spring1(Compositor c, float damping = 0.72f, int periodMs = 50)
    {
        SpringScalarNaturalMotionAnimation s = c.CreateSpringScalarAnimation();
        s.DampingRatio = damping;
        s.Period = TimeSpan.FromMilliseconds(periodMs);
        return s;
    }

    // ── Enter and leave ────────────────────────────────────────────────────────

    /// <summary>
    /// Something arriving: fades in and grows from 92% (Smooth Flight) or springs up from 80%
    /// (Fluid). <paramref name="origin"/> is where it grows from, as a fraction of its size.
    /// </summary>
    public static void Enter(UIElement element, Vector2? origin = null)
    {
        Style style = Current;
        if (style == Style.Off)
        {
            return;
        }

        try
        {
            Visual v = VisualOf(element);
            Compositor c = v.Compositor;
            Vector2 o = origin ?? new Vector2(0.5f, 0.5f);
            v.CenterPoint = new Vector3(v.Size.X * o.X, v.Size.Y * o.Y, 0);
            if (element is FrameworkElement fe && v.Size == Vector2.Zero)
            {
                v.CenterPoint = new Vector3((float)fe.ActualWidth * o.X, (float)fe.ActualHeight * o.Y, 0);
            }

            ScalarKeyFrameAnimation fade = c.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0f, 0f);
            fade.InsertKeyFrame(1f, 1f, Ease(c));
            fade.Duration = style == Style.Fluid ? Standard : Quick + TimeSpan.FromMilliseconds(40);
            v.StartAnimation("Opacity", fade);

            if (style == Style.Fluid)
            {
                v.Scale = new Vector3(0.8f, 0.8f, 1f);
                SpringVector3NaturalMotionAnimation grow = Spring3(c, 0.6f, 45);
                grow.FinalValue = Vector3.One;
                v.StartAnimation("Scale", grow);
            }
            else
            {
                Vector3KeyFrameAnimation grow = c.CreateVector3KeyFrameAnimation();
                grow.InsertKeyFrame(0f, new Vector3(0.92f, 0.92f, 1f));
                grow.InsertKeyFrame(1f, Vector3.One, Ease(c));
                grow.Duration = Standard;
                v.StartAnimation("Scale", grow);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "motion: enter");
        }
    }

    /// <summary>
    /// Something leaving: fades and shrinks, THEN <paramref name="done"/> runs (to remove it).
    /// With motion off, <paramref name="done"/> runs at once.
    /// </summary>
    public static void Leave(UIElement element, Action done)
    {
        Style style = Current;
        if (style == Style.Off)
        {
            done();
            return;
        }

        try
        {
            element.IsHitTestVisible = false;
            Visual v = VisualOf(element);
            Compositor c = v.Compositor;
            if (element is FrameworkElement fe)
            {
                v.CenterPoint = new Vector3((float)fe.ActualWidth / 2, (float)fe.ActualHeight / 2, 0);
            }

            CompositionScopedBatch batch = c.CreateScopedBatch(CompositionBatchTypes.Animation);
            ScalarKeyFrameAnimation fade = c.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1f, 0f, Ease(c));
            fade.Duration = Quick + TimeSpan.FromMilliseconds(30);
            v.StartAnimation("Opacity", fade);

            Vector3KeyFrameAnimation shrink = c.CreateVector3KeyFrameAnimation();
            shrink.InsertKeyFrame(1f, style == Style.Fluid ? new Vector3(0.78f, 0.78f, 1f) : new Vector3(0.9f, 0.9f, 1f), Ease(c));
            shrink.Duration = Quick + TimeSpan.FromMilliseconds(30);
            v.StartAnimation("Scale", shrink);
            batch.End();
            batch.Completed += (_, _) =>
            {
                try
                {
                    done();
                }
                catch (Exception ex)
                {
                    Diagnostics.Swallowed(ex, "motion: after leave");
                }
            };
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "motion: leave");
            done();
        }
    }

    /// <summary>
    /// Makes the next layout moves of these elements glide instead of jump: an implicit
    /// animation on the Offset XAML layout writes. Armed only around a change Aviary makes (a
    /// tab opened, closed or dragged) and disarmed after it, so resizing the window never drags
    /// the tabs along behind the mouse.
    /// </summary>
    public static void GlideNextLayout(IEnumerable<UIElement> elements, TimeSpan? armedFor = null)
    {
        Style style = Current;
        if (style == Style.Off)
        {
            return;
        }

        List<Visual> armed = new();
        try
        {
            foreach (UIElement element in elements)
            {
                Visual v = VisualOf(element);
                Compositor c = v.Compositor;
                ImplicitAnimationCollection implicitSet = c.CreateImplicitAnimationCollection();

                if (style == Style.Fluid)
                {
                    SpringVector3NaturalMotionAnimation spring = Spring3(c, 0.68f, 48);
                    spring.Target = "Offset";
                    implicitSet["Offset"] = spring;
                }
                else
                {
                    Vector3KeyFrameAnimation glide = c.CreateVector3KeyFrameAnimation();
                    glide.Target = "Offset";
                    glide.InsertExpressionKeyFrame(1f, "this.FinalValue", Ease(c));
                    glide.Duration = Standard;
                    implicitSet["Offset"] = glide;
                }

                v.ImplicitAnimations = implicitSet;
                armed.Add(v);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "motion: arm glide");
        }

        if (armed.Count == 0)
        {
            return;
        }

        Microsoft.UI.Dispatching.DispatcherQueue? queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Microsoft.UI.Dispatching.DispatcherQueueTimer? timer = queue?.CreateTimer();
        if (timer is null)
        {
            return;
        }

        timer.Interval = armedFor ?? TimeSpan.FromMilliseconds(450);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            foreach (Visual v in armed)
            {
                try
                {
                    v.ImplicitAnimations = null;
                }
                catch (Exception)
                {
                    // Its element has gone; nothing to disarm.
                }
            }
        };
        timer.Start();
    }

    /// <summary>A small bump — the shield when it blocks something, a count that changed.</summary>
    public static void Pop(UIElement element, float to = 1.14f)
    {
        Style style = Current;
        if (style == Style.Off)
        {
            return;
        }

        try
        {
            Visual v = VisualOf(element);
            Compositor c = v.Compositor;
            if (element is FrameworkElement fe)
            {
                v.CenterPoint = new Vector3((float)fe.ActualWidth / 2, (float)fe.ActualHeight / 2, 0);
            }

            if (style == Style.Fluid)
            {
                v.Scale = new Vector3(to, to, 1f);
                SpringVector3NaturalMotionAnimation back = Spring3(c, 0.45f, 40);
                back.FinalValue = Vector3.One;
                v.StartAnimation("Scale", back);
            }
            else
            {
                Vector3KeyFrameAnimation bump = c.CreateVector3KeyFrameAnimation();
                bump.InsertKeyFrame(0.35f, new Vector3(to, to, 1f), Ease(c));
                bump.InsertKeyFrame(1f, Vector3.One, Ease(c));
                bump.Duration = Standard;
                v.StartAnimation("Scale", bump);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "motion: pop");
        }
    }

    /// <summary>A panel arriving from a little above (or below): slides and fades in.</summary>
    public static void SlideIn(UIElement element, float fromY = -10f)
    {
        Style style = Current;
        if (style == Style.Off)
        {
            return;
        }

        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            Visual v = VisualOf(element);
            Compositor c = v.Compositor;

            ScalarKeyFrameAnimation fade = c.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0f, 0f);
            fade.InsertKeyFrame(1f, 1f, Ease(c));
            fade.Duration = Standard;
            v.StartAnimation("Opacity", fade);

            if (style == Style.Fluid)
            {
                v.Properties.InsertVector3("Translation", new Vector3(0, fromY * 1.6f, 0));
                SpringVector3NaturalMotionAnimation settle = Spring3(c, 0.62f, 50);
                settle.FinalValue = Vector3.Zero;
                v.StartAnimation("Translation", settle);
            }
            else
            {
                Vector3KeyFrameAnimation slide = c.CreateVector3KeyFrameAnimation();
                slide.InsertKeyFrame(0f, new Vector3(0, fromY, 0));
                slide.InsertKeyFrame(1f, Vector3.Zero, Ease(c));
                slide.Duration = Standard;
                v.StartAnimation("Translation", slide);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "motion: slide in");
        }
    }

    private static readonly Dictionary<UIElement, int> RevealTokens = new();

    /// <summary>
    /// Shows or hides an element whose XAML Opacity means something (the address field's focus
    /// ring, a panel): XAML's value is set to where it ends up, and the Composition animation
    /// carries the visual there. Hiding sets XAML's 0 only once the fade has finished — and only
    /// if nothing showed it again meanwhile (a later call wins).
    /// </summary>
    public static void Reveal(UIElement element, bool show, TimeSpan? duration = null)
    {
        int token = RevealTokens.TryGetValue(element, out int t) ? t + 1 : 1;
        RevealTokens[element] = token;

        if (Current == Style.Off)
        {
            try { VisualOf(element).StopAnimation("Opacity"); } catch (Exception) { }
            element.Opacity = show ? 1 : 0;
            return;
        }

        try
        {
            Visual v = VisualOf(element);
            Compositor c = v.Compositor;
            float from = (float)element.Opacity;
            ScalarKeyFrameAnimation fade = c.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0f, from);
            fade.InsertKeyFrame(1f, show ? 1f : 0f, Ease(c));
            fade.Duration = duration ?? Quick + TimeSpan.FromMilliseconds(40);

            if (show)
            {
                element.Opacity = 1;
                v.StartAnimation("Opacity", fade);
                return;
            }

            CompositionScopedBatch batch = c.CreateScopedBatch(CompositionBatchTypes.Animation);
            v.StartAnimation("Opacity", fade);
            batch.End();
            batch.Completed += (_, _) =>
            {
                if (RevealTokens.TryGetValue(element, out int now) && now == token)
                {
                    element.Opacity = 0;
                }
            };
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "motion: reveal");
            element.Opacity = show ? 1 : 0;
        }
    }

    // == FULL SCREEN (M4.6) =====================================================
    // Full screen was the one thing left in Aviary that still snapped: the presenter changed and
    // every row vanished in the same frame. These two move the chrome instead, and they are built
    // around the one hard fact about the switch - the Windows App SDK's own SetPresenter holds the
    // UI thread for 85-120 ms (M4.5 measured it; Grant's own 20 Sep log shows 92 ms with 62 ms of
    // real work). So the motion goes where the thread is FREE: away first and then switch, or
    // switch and then back.

    /// <summary>
    /// The chrome leaving for full screen: each row fades and slides up under the window's edge,
    /// then <paramref name="done"/> runs - which is where the presenter actually changes. Every
    /// row is put back exactly as it was first, so a cancelled or repeated toggle cannot leave one
    /// half-faded. With motion off, <paramref name="done"/> runs at once.
    /// </summary>
    public static void LiftAway(IReadOnlyList<FrameworkElement> rows, Action done)
    {
        if (Current == Style.Off || rows.Count == 0)
        {
            done();
            return;
        }

        try
        {
            Visual first = VisualOf(rows[0]);
            Compositor c = first.Compositor;
            CompositionScopedBatch batch = c.CreateScopedBatch(CompositionBatchTypes.Animation);

            foreach (FrameworkElement row in rows)
            {
                if (row.Visibility != Visibility.Visible)
                {
                    continue;
                }

                ElementCompositionPreview.SetIsTranslationEnabled(row, true);
                Visual v = VisualOf(row);
                float up = -(float)Math.Max(row.ActualHeight, 1) - 8;

                ScalarKeyFrameAnimation fade = c.CreateScalarKeyFrameAnimation();
                fade.InsertKeyFrame(1f, 0f, Ease(c));
                fade.Duration = Quick + TimeSpan.FromMilliseconds(20);
                v.StartAnimation("Opacity", fade);

                Vector3KeyFrameAnimation lift = c.CreateVector3KeyFrameAnimation();
                lift.InsertKeyFrame(1f, new Vector3(0, up, 0), Ease(c));
                lift.Duration = Quick + TimeSpan.FromMilliseconds(20);
                v.StartAnimation("Translation", lift);
            }

            batch.End();
            batch.Completed += (_, _) =>
            {
                try
                {
                    SettleNow(rows);
                }
                catch (Exception ex)
                {
                    Diagnostics.Swallowed(ex, "motion: reset after lift away");
                }

                try
                {
                    done();
                }
                catch (Exception ex)
                {
                    Diagnostics.Swallowed(ex, "motion: after lift away");
                }
            };
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "motion: lift away");
            done();
        }
    }

    /// <summary>
    /// The chrome coming back from full screen: the rows start above where they belong, faded, and
    /// settle - on a spring in Fluid, on the shared curve in Smooth Flight. Call it after the rows
    /// are visible again and the layout has settled.
    /// </summary>
    public static void SettleBack(IReadOnlyList<FrameworkElement> rows)
    {
        Style style = Current;
        if (style == Style.Off)
        {
            return;
        }

        try
        {
            foreach (FrameworkElement row in rows)
            {
                if (row.Visibility != Visibility.Visible)
                {
                    continue;
                }

                ElementCompositionPreview.SetIsTranslationEnabled(row, true);
                Visual v = VisualOf(row);
                Compositor c = v.Compositor;
                float up = -(float)Math.Max(row.ActualHeight, 1) - 8;

                v.Opacity = 0;
                v.Properties.InsertVector3("Translation", new Vector3(0, up, 0));

                ScalarKeyFrameAnimation fade = c.CreateScalarKeyFrameAnimation();
                fade.InsertKeyFrame(1f, 1f, Ease(c));
                fade.Duration = Standard;
                v.StartAnimation("Opacity", fade);

                if (style == Style.Fluid)
                {
                    SpringVector3NaturalMotionAnimation settle = Spring3(c, 0.78f, 48);
                    settle.FinalValue = Vector3.Zero;
                    v.StartAnimation("Translation", settle);
                }
                else
                {
                    Vector3KeyFrameAnimation slide = c.CreateVector3KeyFrameAnimation();
                    slide.InsertKeyFrame(1f, Vector3.Zero, Ease(c));
                    slide.Duration = Standard;
                    v.StartAnimation("Translation", slide);
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "motion: settle back");
            foreach (FrameworkElement row in rows)
            {
                VisualOf(row).Opacity = 1;
            }
        }
    }

    /// <summary>
    /// Puts one row back where it belongs, with no animation left on it.
    ///
    /// <b>"Translation" cannot be stopped through the Visual</b> (M4.6, bug 74): it is a property
    /// of the visual's PROPERTY SET that SetIsTranslationEnabled adds, so StartAnimation on the
    /// visual routes to it, but StopAnimation throws "The specified property was not found or
    /// cannot be animated. Context: Translation". The first build of the full-screen motion threw
    /// exactly there, inside the completion handler that changes the presenter - so F11 animated
    /// and then did nothing at all. Stop it on the property set, and write the value afterwards
    /// either way: an animation that has already finished has nothing to stop.
    /// </summary>
    private static void PutRowBack(FrameworkElement row)
    {
        Visual v = VisualOf(row);

        try
        {
            v.StopAnimation("Opacity");
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "motion: stop row fade");
        }

        // ...and "Translation" cannot be stopped at all - not through the visual, not through its
        // property set; both throw. What DOES work is starting another one: a one-millisecond
        // animation home replaces whatever was running and lands on zero, and costs nothing when
        // nothing was running.
        try
        {
            // M4.7 (bug 76): and it has to EXIST first. LiftAway and SettleBack both skip a row
            // that is not visible, so a row hidden when the chrome left - the bookmarks bar, most
            // often - never had Translation put in its property set, and StartAnimation throws on
            // it instead of creating one. SettleNow walks every row, visible or not, so it threw
            // on exactly those: caught one row at a time, but the Opacity line below it never ran
            // and the row stayed however the last animation left it. Enabling it here is free when
            // it is already on. (Seen in the 20 Sep test logs as "motion: settle now".)
            ElementCompositionPreview.SetIsTranslationEnabled(row, true);

            Vector3KeyFrameAnimation home = v.Compositor.CreateVector3KeyFrameAnimation();
            home.InsertKeyFrame(1f, Vector3.Zero);
            home.Duration = TimeSpan.FromMilliseconds(1);
            v.StartAnimation("Translation", home);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "motion: stop row slide");
        }

        // Whatever happened above, a row that is being put back is a row that must be visible:
        // this is the line bug 74 was about, and it goes after everything that can throw.
        v.Opacity = 1;
    }

    /// <summary>Stops any full-screen animation and puts the rows back where they belong at once.</summary>
    public static void SettleNow(IReadOnlyList<FrameworkElement> rows)
    {
        foreach (FrameworkElement row in rows)
        {
            try
            {
                PutRowBack(row);
            }
            catch (Exception ex)
            {
                Diagnostics.Swallowed(ex, "motion: settle now");
            }
        }
    }

    /// <summary>
    /// A menu or popover arriving (M4.6): it grows from the top edge it is anchored to, rather than
    /// appearing in place. Every Aviary menu goes through MainWindow.Themed, so one call there
    /// covers the app menu, the Aa menu, the tab and sidebar menus, downloads and Shields.
    /// </summary>
    public static void Bloom(FrameworkElement element)
    {
        Style style = Current;
        if (style == Style.Off)
        {
            return;
        }

        try
        {
            Visual v = VisualOf(element);
            Compositor c = v.Compositor;
            float w = (float)(element.ActualWidth > 0 ? element.ActualWidth : element.Width);
            float h = (float)(element.ActualHeight > 0 ? element.ActualHeight : element.Height);
            if (float.IsNaN(w) || float.IsNaN(h) || w <= 0 || h <= 0)
            {
                return;
            }

            // Grows out of the top edge, in the middle: right for a menu under a button, and it
            // never looks wrong for one placed elsewhere.
            v.CenterPoint = new Vector3(w / 2, 0, 0);

            ScalarKeyFrameAnimation fade = c.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0f, 0f);
            fade.InsertKeyFrame(1f, 1f, Ease(c));
            fade.Duration = Quick + TimeSpan.FromMilliseconds(30);
            v.StartAnimation("Opacity", fade);

            if (style == Style.Fluid)
            {
                v.Scale = new Vector3(0.94f, 0.9f, 1f);
                SpringVector3NaturalMotionAnimation grow = Spring3(c, 0.68f, 42);
                grow.FinalValue = Vector3.One;
                v.StartAnimation("Scale", grow);
            }
            else
            {
                Vector3KeyFrameAnimation grow = c.CreateVector3KeyFrameAnimation();
                grow.InsertKeyFrame(0f, new Vector3(0.97f, 0.94f, 1f));
                grow.InsertKeyFrame(1f, Vector3.One, Ease(c));
                grow.Duration = Quick + TimeSpan.FromMilliseconds(40);
                v.StartAnimation("Scale", grow);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "motion: bloom");
        }
    }

    /// <summary>Fades an element's composition opacity to <paramref name="to"/> (no layout, no storyboard).</summary>
    public static void FadeTo(UIElement element, float to, TimeSpan? duration = null)
    {
        try
        {
            Visual v = VisualOf(element);
            if (Current == Style.Off)
            {
                v.StopAnimation("Opacity");
                v.Opacity = to;
                return;
            }

            Compositor c = v.Compositor;
            ScalarKeyFrameAnimation fade = c.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1f, to, Ease(c));
            fade.Duration = duration ?? Standard;
            v.StartAnimation("Opacity", fade);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "motion: fade");
        }
    }
}
