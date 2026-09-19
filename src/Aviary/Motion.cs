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
    public const string MatchTheLook = "Match the look";

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

    public static readonly TimeSpan Quick = TimeSpan.FromMilliseconds(120);
    public static readonly TimeSpan Standard = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(320);

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
