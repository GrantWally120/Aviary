using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Microsoft.Graphics.DirectX;

namespace Aviary;

/// <summary>
/// Phase 0's Liquid Glass capsule, on the real bars (14 Sep 2026 — Grant: "Phase 0 capsules").
///
/// The interface study (Aviary-Phase0.html, `.gcap`) builds every glass bar from layers, in this
/// order: backdrop → fill → rim → sheen → ring, with a three-layer shadow under it and a spring
/// when a button is pressed. Until now the bars were only the fill — "flat translucent fills over
/// the window's own Mica" (LiquidGlass.xaml's own words). This adds the rest that a WinUI window can
/// honestly have:
///
///   - SHEEN — a vertical white wash, brightest at the top (AvGlassSheenBrush);
///   - RING — a 1px rim that is bright at two poles and nearly invisible along the flanks. It is
///     a CSS gradient at 157° in the study, and a CSS gradient's line depends on the box's size, so
///     each capsule gets its own brush in ABSOLUTE coordinates, recomputed when it resizes, with its
///     colours copied from AvGlassRingBrush (the theme keeps the colours; this keeps the geometry);
///   - EDGES — the inset 1px specular pair: light along the top lip, dark along the bottom
///     (AvEdgeTopBrush / AvEdgeBottomBrush — declared since Phase 1, drawn by nothing until now);
///   - SHADOW — three soft layers (AvGlassShadowSpec: "dx,dy,blur,alpha;…") plus the 0.5px hairline,
///     drawn with Win2D into a surface behind the capsule and then CUT AWAY under it. A composition
///     DropShadow cannot do that: under a translucent fill it would grey the glass from below,
///     where a CSS box-shadow is clipped to the outside of its box. This is why Win2D is here.
///   - PRESS — toolbar buttons dip to 94% in 90 ms and spring back (Phase 0's `.gbtn:active`),
///     unless Windows or Aviary asks for less motion.
///
/// Not attempted, deliberately: the study's backdrop blur, saturation and refraction rim. WebView2
/// cannot slide a page under the bars (there is no content inset), so the only thing behind them is
/// Windows' Mica — itself already a blurred wallpaper — and a blur of it shows nothing.
///
/// Classic wears none of this (one opaque plane welded to the top), and neither does High Contrast,
/// whose dictionaries give every glass key a transparent value and an empty shadow spec.
/// </summary>
internal sealed class GlassCapsule
{
    private readonly FrameworkElement _capsule;
    private readonly Panel _layerPanel;
    private readonly Func<string, object?> _theme;
    private readonly Border _sheen, _ring, _edgeTop, _edgeBottom;

    /// <summary>
    /// The gleam (M4.6): a soft light that follows the pointer along the capsule, the way a real
    /// pane of glass catches a moving light. Fluid only - it is the gel look's own flourish, and
    /// Smooth Flight is meant to be calm. Off with Reduce motion, like everything else.
    /// </summary>
    private readonly Border _gleam;
    private readonly RadialGradientBrush _gleamBrush;
    private double _gleamAt = -1;
    private readonly Canvas _shadowHost;
    private readonly SpriteVisual _shadow;
    private readonly LinearGradientBrush _ringBrush = new() { MappingMode = BrushMappingMode.Absolute };

    private bool _enabled;
    private string _drawn = string.Empty;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _settle;

    /// <summary>
    /// A resize: stretch the shadow already drawn to the new size at once, and draw it properly
    /// once the size has held still for 80 ms — the sidebar animates its width, and three blurs per
    /// frame would be a waste of every frame.
    /// </summary>
    private void Settle()
    {
        if (!_enabled)
        {
            return;
        }

        _shadow.Size = new Vector2((float)_capsule.ActualWidth + Pad * 2, (float)_capsule.ActualHeight + Pad * 2);
        Place();
        _settle.Stop();
        _settle.Start();
    }

    /// <summary>Room around the capsule for the widest shadow: its blur plus its drop.</summary>
    private const float Pad = 64f;

    /// <param name="capsule">The bar itself: a Grid (toolbar, tab strip) or a Border (sidebar).</param>
    /// <param name="layerPanel">Where the layers go, behind the bar's content — the bar when it is a
    /// Grid, the Border's child Grid otherwise.</param>
    /// <param name="shadowParent">The panel the bar sits in; the shadow's host goes in just before
    /// the bar, so it is drawn underneath it.</param>
    /// <param name="theme">Resolves a key against the theme actually on screen (MainWindow.ThemeValue).</param>
    public GlassCapsule(FrameworkElement capsule, Panel layerPanel, Panel shadowParent, Func<string, object?> theme)
    {
        _capsule = capsule;
        _layerPanel = layerPanel;
        _theme = theme;

        _sheen = Layer();
        _gleam = Layer();
        _gleamBrush = new RadialGradientBrush
        {
            Center = new Windows.Foundation.Point(0.5, 0.5),
            GradientOrigin = new Windows.Foundation.Point(0.5, 0.5),

            // Wide and shallow: a highlight that runs along the bar rather than a spotlight on it.
            RadiusX = 0.22,
            RadiusY = 1.4,
        };
        _gleamBrush.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF), Offset = 0 });
        _gleamBrush.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), Offset = 1 });
        _gleam.Background = _gleamBrush;
        _gleam.Opacity = 0;

        _ring = Layer();
        _ring.BorderThickness = new Thickness(1);
        _ring.BorderBrush = _ringBrush;
        _edgeTop = Layer();
        _edgeTop.BorderThickness = new Thickness(0, 1, 0, 0);
        _edgeBottom = Layer();
        _edgeBottom.BorderThickness = new Thickness(0, 0, 0, 1);

        // Behind the content, in the order the study stacks them: sheen, then the edges, then the ring.
        layerPanel.Children.Insert(0, _ring);
        layerPanel.Children.Insert(0, _edgeBottom);
        layerPanel.Children.Insert(0, _edgeTop);
        layerPanel.Children.Insert(0, _gleam);
        layerPanel.Children.Insert(0, _sheen);

        // The shadow's host shares the bar's cell and sits under it in z-order. It measures to
        // nothing, so it can never push the layout around; the sprite is placed by hand.
        _shadowHost = new Canvas { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        Grid.SetRow(_shadowHost, Grid.GetRow(capsule));
        Grid.SetColumn(_shadowHost, Grid.GetColumn(capsule));
        Grid.SetRowSpan(_shadowHost, Grid.GetRowSpan(capsule));
        Grid.SetColumnSpan(_shadowHost, Grid.GetColumnSpan(capsule));
        shadowParent.Children.Insert(Math.Max(0, shadowParent.Children.IndexOf(capsule)), _shadowHost);

        _compositor ??= ElementCompositionPreview.GetElementVisual(capsule).Compositor;
        _shadow = Compositor.CreateSpriteVisual();
        ElementCompositionPreview.SetElementChildVisual(_shadowHost, _shadow);

        // The gleam follows the pointer across the bar, and goes out when it leaves.
        capsule.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnGleamPointer), true);
        capsule.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler((_, _) => ShowGleam(false)), true);
        capsule.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler((_, _) => ShowGleam(false)), true);

        capsule.SizeChanged += (_, _) => Settle();
        _settle = capsule.DispatcherQueue.CreateTimer();
        _settle.Interval = TimeSpan.FromMilliseconds(80);
        _settle.IsRepeating = false;
        _settle.Tick += (_, _) => Refresh();
        capsule.LayoutUpdated += (_, _) => { if (_enabled) { Place(); } };
    }

    private static Compositor? _compositor;
    private static Compositor Compositor => _compositor ?? throw new InvalidOperationException("GlassCapsule used before any capsule was made.");

    /// <summary>
    /// Moves the gleam to the pointer. The brush's centre is in the capsule's own 0-1 space, so
    /// nothing here depends on the window's size or scale; it is two property writes, and only
    /// when the pointer has actually moved a couple of pixels along the bar.
    /// </summary>
    private void OnGleamPointer(object sender, PointerRoutedEventArgs e)
    {
        if (!_enabled || Motion.Current != Motion.Style.Fluid)
        {
            if (_gleam.Opacity > 0)
            {
                ShowGleam(false);
            }

            return;
        }

        try
        {
            double width = _capsule.ActualWidth;
            if (width <= 1)
            {
                return;
            }

            double x = e.GetCurrentPoint(_capsule).Position.X / width;
            if (_gleamAt >= 0 && Math.Abs(x - _gleamAt) * width < 2)
            {
                return;
            }

            _gleamAt = Math.Clamp(x, 0, 1);
            _gleamBrush.Center = new Windows.Foundation.Point(_gleamAt, 0.5);
            _gleamBrush.GradientOrigin = new Windows.Foundation.Point(_gleamAt, 0.42);
            ShowGleam(true);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "glass: gleam");
        }
    }

    private void ShowGleam(bool on)
    {
        if (on && _gleam.Opacity >= 1)
        {
            return;
        }

        Motion.FadeTo(_gleam, on ? 1f : 0f, TimeSpan.FromMilliseconds(on ? 180 : 260));
        if (!on)
        {
            _gleamAt = -1;
        }
    }

    private static Border Layer() => new()
    {
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    /// <summary>On for Liquid Glass, off for Classic. Everything is drawn again either way.</summary>
    public void SetEnabled(bool glass)
    {
        _enabled = glass;
        Visibility v = glass ? Visibility.Visible : Visibility.Collapsed;
        _sheen.Visibility = _gleam.Visibility = _ring.Visibility = _edgeTop.Visibility = _edgeBottom.Visibility = v;
        if (!glass)
        {
            _gleam.Opacity = 0;
            _gleamAt = -1;
        }
        _shadowHost.Visibility = v;
        _drawn = string.Empty;
        Refresh();
    }

    /// <summary>
    /// Re-reads the theme and redraws: after a look or theme change, a resize, or new geometry.
    /// Cheap when nothing changed — the shadow is redrawn only when its size, radius, scale or
    /// spec did.
    /// </summary>
    public void Refresh()
    {
        if (!_enabled)
        {
            return;
        }

        try
        {
            CornerRadius radius = CornerOf(_capsule);
            Thickness inset = PaddingOf(_capsule);

            foreach (Border layer in new[] { _sheen, _gleam, _ring, _edgeTop, _edgeBottom })
            {
                layer.CornerRadius = radius;

                // A Grid bar has padding; the layers must cover the whole capsule, padding and all.
                layer.Margin = new Thickness(-inset.Left, -inset.Top, -inset.Right, -inset.Bottom);
                if (_layerPanel is Grid grid)
                {
                    Grid.SetColumnSpan(layer, Math.Max(1, grid.ColumnDefinitions.Count));
                    Grid.SetRowSpan(layer, Math.Max(1, grid.RowDefinitions.Count));
                }
            }

            _sheen.Background = _theme("AvGlassSheenBrush") as Brush;
            _edgeTop.BorderBrush = _theme("AvEdgeTopBrush") as Brush;
            _edgeBottom.BorderBrush = _theme("AvEdgeBottomBrush") as Brush;

            CopyRingStops();
            AimRing(_capsule.ActualWidth, _capsule.ActualHeight);
            Place();
            DrawShadow();
        }
        catch (Exception ex)
        {
            // A capsule that cannot draw its glass is a plain translucent bar, which is how every
            // bar looked until today. Never worth a crash.
            Diagnostics.Swallowed(ex, "glass: refresh capsule " + _capsule.Name);
        }
    }

    // ── the ring ────────────────────────────────────────────────────────────

    private void CopyRingStops()
    {
        _ringBrush.GradientStops.Clear();
        if (_theme("AvGlassRingBrush") is LinearGradientBrush template)
        {
            foreach (GradientStop stop in template.GradientStops)
            {
                _ringBrush.GradientStops.Add(new GradientStop { Color = stop.Color, Offset = stop.Offset });
            }
        }
    }

    /// <summary>
    /// CSS's `linear-gradient(157deg, …)` on a W×H box, exactly: the gradient line runs through the
    /// centre at 157° (0° = up, clockwise), and is as long as it must be for its 0% and 100% lines
    /// to pass through the corners — so the top-left and bottom-right corners take the two bright
    /// poles and the long flanks of a wide bar take the faint middle stops.
    /// </summary>
    private void AimRing(double w, double h)
    {
        if (w <= 0 || h <= 0)
        {
            return;
        }

        double a = 157 * Math.PI / 180;
        double dx = Math.Sin(a), dy = -Math.Cos(a);
        double half = (Math.Abs(w * dx) + Math.Abs(h * dy)) / 2;
        _ringBrush.StartPoint = new Point(w / 2 - dx * half, h / 2 - dy * half);
        _ringBrush.EndPoint = new Point(w / 2 + dx * half, h / 2 + dy * half);
    }

    // ── the shadow ──────────────────────────────────────────────────────────

    /// <summary>Puts the shadow sprite under the capsule wherever layout has put the capsule.</summary>
    private void Place()
    {
        try
        {
            bool shown = _capsule.Visibility == Visibility.Visible && _capsule.ActualWidth > 0;
            if (_shadow.IsVisible != shown)
            {
                _shadow.IsVisible = shown;
            }

            if (!shown || _shadowHost.XamlRoot is null)
            {
                return;
            }

            Point at = _capsule.TransformToVisual(_shadowHost).TransformPoint(new Point(0, 0));
            var offset = new Vector3((float)at.X - Pad, (float)at.Y - Pad, 0);
            if (_shadow.Offset != offset)
            {
                _shadow.Offset = offset;
            }

            AimRing(_capsule.ActualWidth, _capsule.ActualHeight);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "glass: place shadow " + _capsule.Name);
        }
    }

    private static CanvasDevice? _device;
    private static CompositionGraphicsDevice? _graphics;

    private static CompositionGraphicsDevice Graphics()
    {
        if (_graphics is null)
        {
            _device = CanvasDevice.GetSharedDevice();
            _graphics = CanvasComposition.CreateCompositionGraphicsDevice(Compositor, _device);

            // A lost GPU device (driver update, sleep) takes every drawn surface with it: start a
            // new one, and the next Refresh of each capsule draws again.
            _device.DeviceLost += (_, _) =>
            {
                _graphics = null;
                _device = null;
            };
        }

        return _graphics;
    }

    private void DrawShadow()
    {
        double w = _capsule.ActualWidth, h = _capsule.ActualHeight;
        float scale = (float)(_capsule.XamlRoot?.RasterizationScale ?? 1.0);
        string spec = _theme("AvGlassShadowSpec") as string ?? string.Empty;
        Windows.UI.Color hair = _theme("AvHairlineBrush") is SolidColorBrush s ? s.Color : Microsoft.UI.Colors.Transparent;
        float r = (float)CornerOf(_capsule).TopLeft;

        string stamp = string.Join("|", w.ToString("0.#", CultureInfo.InvariantCulture), h.ToString("0.#", CultureInfo.InvariantCulture),
            r.ToString(CultureInfo.InvariantCulture), scale.ToString(CultureInfo.InvariantCulture), spec, hair.ToString());
        if (stamp == _drawn || w <= 0 || h <= 0)
        {
            return;
        }

        List<(float dx, float dy, float blur, float alpha)> layers = ParseShadow(spec);
        if (layers.Count == 0)
        {
            _shadow.Brush = null;
            _drawn = stamp;
            return;
        }

        float sw = (float)w + Pad * 2, sh = (float)h + Pad * 2;
        CompositionDrawingSurface surface = Graphics().CreateDrawingSurface(
            new Size(Math.Ceiling(sw * scale), Math.Ceiling(sh * scale)),
            DirectXPixelFormat.B8G8R8A8UIntNormalized, DirectXAlphaMode.Premultiplied);

        var box = new Rect(Pad, Pad, w, h);
        using (CanvasDrawingSession ds = CanvasComposition.CreateDrawingSession(surface, new Rect(0, 0, sw * scale, sh * scale), 96 * scale))
        {
            ds.Clear(Microsoft.UI.Colors.Transparent);
            CanvasDevice device = ds.Device;

            // Three soft layers, each the capsule's own shape dropped and blurred. CSS's blur
            // radius is twice the Gaussian's standard deviation, which is what Win2D takes.
            foreach ((float dx, float dy, float blur, float alpha) in layers)
            {
                using var shape = new CanvasCommandList(device);
                using (CanvasDrawingSession cs = shape.CreateDrawingSession())
                {
                    cs.FillRoundedRectangle(new Rect(box.X + dx, box.Y + dy, w, h), r, r,
                        Windows.UI.Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), 0, 0, 0));
                }

                using var soft = new GaussianBlurEffect
                {
                    Source = shape,
                    BlurAmount = Math.Max(0.01f, blur / 2),
                    BorderMode = EffectBorderMode.Soft,
                    Optimization = EffectOptimization.Quality,
                };
                ds.DrawImage(soft);
            }

            // …then cut away everything under the capsule: a CSS box-shadow is clipped to the
            // outside of its box, and under a translucent fill a shadow would grey the glass.
            using (var hole = new CanvasCommandList(device))
            {
                using (CanvasDrawingSession cs = hole.CreateDrawingSession())
                {
                    cs.FillRoundedRectangle(box, r, r, Microsoft.UI.Colors.Black);
                }

                ds.DrawImage(hole, Vector2.Zero, new Rect(0, 0, sw, sh), 1f,
                    CanvasImageInterpolation.Linear, CanvasComposite.DestinationOut);
            }

            // The 0.5px hairline just outside the edge (`0 0 0 .5px`).
            if (hair.A > 0)
            {
                ds.DrawRoundedRectangle(new Rect(box.X - 0.25, box.Y - 0.25, w + 0.5, h + 0.5), r + 0.25f, r + 0.25f, hair, 0.5f);
            }
        }

        CompositionSurfaceBrush brush = Compositor.CreateSurfaceBrush(surface);
        brush.Stretch = CompositionStretch.Fill;
        _shadow.Brush = brush;
        _shadow.Size = new Vector2(sw, sh);
        _drawn = stamp;
    }

    /// <summary>"dx,dy,blur,alpha;…" — CSS box-shadow numbers in DIPs, alpha 0–1. Anything malformed is skipped.</summary>
    internal static List<(float dx, float dy, float blur, float alpha)> ParseShadow(string spec)
    {
        var layers = new List<(float, float, float, float)>();
        foreach (string part in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] n = part.Split(',', StringSplitOptions.TrimEntries);
            if (n.Length == 4
                && float.TryParse(n[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float dx)
                && float.TryParse(n[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float dy)
                && float.TryParse(n[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float blur)
                && float.TryParse(n[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float alpha)
                && blur >= 0 && blur <= 60 && Math.Abs(dx) <= 24 && Math.Abs(dy) <= 24)
            {
                layers.Add((dx, dy, blur, alpha));
            }
        }

        return layers;
    }

    private static CornerRadius CornerOf(FrameworkElement e) => e switch
    {
        Grid g => g.CornerRadius,
        Border b => b.CornerRadius,
        Control c => c.CornerRadius,
        _ => new CornerRadius(0),
    };

    private static Thickness PaddingOf(FrameworkElement e) => e switch
    {
        Grid g => g.Padding,
        _ => new Thickness(0),
    };

    // ── the press ───────────────────────────────────────────────────────────

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ButtonBase, object> Pressable = new();

    /// <summary>
    /// Phase 0's `.gbtn:active{transform:scale(.94)}` with its spring back: down in 90 ms on the
    /// press curve, then released on a lightly damped spring. Only while <paramref name="active"/>
    /// says so (Liquid Glass, and motion allowed).
    /// </summary>
    public static void AttachPress(ButtonBase button, Func<bool> active)
    {
        if (Pressable.TryGetValue(button, out _))
        {
            return;
        }

        Pressable.Add(button, active);

        // handledEventsToo: a Button marks its own pointer events handled.
        button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => Press(button, active)), true);
        PointerEventHandler release = (_, _) => Release(button);
        button.AddHandler(UIElement.PointerReleasedEvent, release, true);
        button.AddHandler(UIElement.PointerCaptureLostEvent, release, true);
        button.AddHandler(UIElement.PointerCanceledEvent, release, true);
        button.AddHandler(UIElement.PointerExitedEvent, release, true);
    }

    private static void Press(ButtonBase button, Func<bool> active)
    {
        try
        {
            if (!active() || !button.IsEnabled)
            {
                return;
            }

            Visual v = ElementCompositionPreview.GetElementVisual(button);
            v.CenterPoint = new Vector3((float)button.ActualWidth / 2, (float)button.ActualHeight / 2, 0);

            Compositor c = v.Compositor;
            Vector3KeyFrameAnimation down = c.CreateVector3KeyFrameAnimation();
            down.InsertKeyFrame(1f, new Vector3(0.94f, 0.94f, 1f), c.CreateCubicBezierEasingFunction(new Vector2(0.40f, 0f), new Vector2(1f, 1f)));
            down.Duration = TimeSpan.FromMilliseconds(90);
            v.StartAnimation("Scale", down);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "glass: press");
        }
    }

    private static void Release(ButtonBase button)
    {
        try
        {
            // Always, even when nothing was pressed: a visual's Scale property reads back the last
            // value SET, never where an animation has taken it, so it cannot say whether we are down.
            Visual v = ElementCompositionPreview.GetElementVisual(button);
            SpringVector3NaturalMotionAnimation back = v.Compositor.CreateSpringVector3Animation();
            back.FinalValue = Vector3.One;
            back.DampingRatio = 0.55f;
            back.Period = TimeSpan.FromMilliseconds(42);
            v.StartAnimation("Scale", back);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "glass: release");
        }
    }
}
